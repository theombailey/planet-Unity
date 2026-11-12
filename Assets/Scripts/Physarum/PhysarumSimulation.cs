// =============================================================================
// PhysarumSimulation
// -----------------------------------------------------------------------------
// Purpose : The C# side of Physarum.compute, Settlement.compute and Nutrients.compute:
//           owns the walker and per-texel GPU buffers and runs one simulation step.
//           Free of scene references, so tests can drive it directly.
// Idea    : One step, in this order:
//             1. AgentStep         every walker senses, steers, moves, leaves a path,
//                                  earns food, dies or has a child
//             2. TrailDiffuse      the deposits are added to the path field, which is
//                                  blurred and faded (reads Current, writes Next, swap)
//             3. LandStep          the deposits also feed the town density, from which farms
//                                  follow, and busy paths harden into permanent roads
//                                  (Settlement.compute; reads Land.Current, writes Next, swap)
//             4. ClearDeposit      the deposit buffer is emptied
//             5. WaterRelax        the water-proximity field relaxes a few texels (a few passes)
//             6. NutrientTransport nutrients spread through the soil (sub-stepped)
//           Every stage is a GPU dispatch; nothing is read back to the CPU
//           (the population count is read back asynchronously by PhysarumSystem).
// Rules   : what the people may do (irrigation, road hardness) comes in as a SettlementRules value,
//           set by their technology, and is handed to the shaders each step.
// Agent storage: three buffers, one element per pool slot (see Physarum.compute):
//           position + energy, heading, alive flag. A dead slot is free for a
//           newborn, so the pool size caps the population but does not set it.
// Sub-steps (transport): the transport scheme is stable only while each
//           exchange is at most 0.2 of the gap. Rather than clamp it (which would
//           make fine grids transport more slowly than coarse ones) the step is
//           split into enough equal sub-steps that the clamp is never needed.
// =============================================================================

using System;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Physarum
{
    /// <summary>The fields the people's simulation reads and writes.</summary>
    public struct PhysarumFields
    {
        /// <summary>Path strength (ping-pong).</summary>
        public Field Trail;

        /// <summary>Town density, farmland, crowding, permanent road (ping-pong).</summary>
        public Field Land;

        /// <summary>Proximity to water (ping-pong).</summary>
        public Field Water;

        /// <summary>Nutrients and organic matter (ping-pong).</summary>
        public Field Soil;

        /// <summary>Terrain texture (read).</summary>
        public RenderTexture Terrain;

        /// <summary>Climate texture (read).</summary>
        public RenderTexture Climate;
    }

    /// <summary>Runs the walkers, the path field, the towns, farms and roads and the nutrient transport on the GPU.</summary>
    public sealed class PhysarumSimulation : IDisposable
    {
        private const int AgentGroupSize = 64;
        private const int TexelGroupSize = 8;
        private const int CounterCount = 4;
        private const float MaxExchangePerSubStep = 0.2f;
        private const int WaterPassesPerStep = 4;
        private const int WaterPassesAtStart = 200;

        /// <summary>Field size at which one "person per texel" is the reference density for towns.</summary>
        private const float ReferenceResolution = 256f;

        private readonly ComputeShader physarum;
        private readonly ComputeShader settlement;
        private readonly ComputeShader nutrients;
        private readonly SimConfig config;

        private readonly int initKernel;
        private readonly int copyKernel;
        private readonly int clearCountersKernel;
        private readonly int agentKernel;
        private readonly int trailKernel;
        private readonly int clearDepositKernel;
        private readonly int landKernel;
        private readonly int waterKernel;
        private readonly int transportKernel;
        private readonly int spawnKernel;
        private readonly int killKernel;

        private ComputeBuffer posEnergy;
        private ComputeBuffer heading;
        private ComputeBuffer alive;
        private ComputeBuffer counters;
        private ComputeBuffer deposit;
        private uint frame;

        /// <summary>Creates a simulation using the three compute shaders and the given parameters.</summary>
        public PhysarumSimulation(ComputeShader physarumShader, ComputeShader settlementShader, ComputeShader nutrientShader, SimConfig simConfig)
        {
            physarum = physarumShader;
            settlement = settlementShader;
            nutrients = nutrientShader;
            config = simConfig;

            initKernel = physarum.FindKernel("InitAgents");
            copyKernel = physarum.FindKernel("CopyAgents");
            clearCountersKernel = physarum.FindKernel("ClearCounters");
            agentKernel = physarum.FindKernel("AgentStep");
            trailKernel = physarum.FindKernel("TrailDiffuse");
            clearDepositKernel = physarum.FindKernel("ClearDeposit");
            spawnKernel = physarum.FindKernel("SpawnAgents");
            killKernel = physarum.FindKernel("KillAgents");
            landKernel = settlement.FindKernel("LandStep");
            waterKernel = settlement.FindKernel("WaterRelax");
            transportKernel = nutrients.FindKernel("NutrientTransport");
        }

        /// <summary>Number of agent slots (the most people that can be alive).</summary>
        public int PoolSize { get; private set; }

        /// <summary>Texels per face edge of the per-texel buffers.</summary>
        public int Resolution { get; private set; }

        /// <summary>True once buffers exist.</summary>
        public bool IsAllocated => posEnergy != null;

        /// <summary>Population counters: [0] alive, [1] born this step, [2] died this step. For readback.</summary>
        public ComputeBuffer Counters => counters;

        /// <summary>Agent position (xyz) and energy (w). For drawing and for readback in tests.</summary>
        public ComputeBuffer PosEnergy => posEnergy;

        /// <summary>Agent heading (xyz). For drawing and for readback in tests.</summary>
        public ComputeBuffer Heading => heading;

        /// <summary>Agent alive flags (0 dead, 1 alive, 2 newborn). For drawing and for readback in tests.</summary>
        public ComputeBuffer Alive => alive;

        /// <summary>
        /// Creates or resizes the buffers. If agents already exist and the pool size changes, they are
        /// carried over (as many as fit); the per-texel buffers are always emptied.
        /// </summary>
        public void Allocate(int poolSize, int resolution)
        {
            int texelCount = resolution * resolution * CubeSphere.FaceCount;

            if (deposit == null || Resolution != resolution)
            {
                ReleaseTexelBuffers();
                deposit = NewZeroedBuffer(texelCount);
            }

            if (counters == null)
            {
                counters = NewZeroedBuffer(CounterCount);
            }

            if (posEnergy == null || PoolSize != poolSize)
            {
                ComputeBuffer oldPosEnergy = posEnergy;
                ComputeBuffer oldHeading = heading;
                ComputeBuffer oldAlive = alive;
                int oldSize = PoolSize;

                posEnergy = new ComputeBuffer(poolSize, 16);
                heading = new ComputeBuffer(poolSize, 16);
                alive = new ComputeBuffer(poolSize, 4);
                PoolSize = poolSize;

                if (oldPosEnergy != null)
                {
                    physarum.SetInt(PoolSizeId, poolSize);
                    physarum.SetInt(OldPoolSizeId, oldSize);
                    physarum.SetBuffer(copyKernel, PosEnergyId, posEnergy);
                    physarum.SetBuffer(copyKernel, HeadingId, heading);
                    physarum.SetBuffer(copyKernel, AliveId, alive);
                    physarum.SetBuffer(copyKernel, OldPosEnergyId, oldPosEnergy);
                    physarum.SetBuffer(copyKernel, OldHeadingId, oldHeading);
                    physarum.SetBuffer(copyKernel, OldAliveId, oldAlive);
                    physarum.Dispatch(copyKernel, Groups(poolSize, AgentGroupSize), 1, 1);

                    oldPosEnergy.Release();
                    oldHeading.Release();
                    oldAlive.Release();
                }
                else
                {
                    // Brand new pool: everything dead until SeedAgents runs.
                    alive.SetData(new uint[poolSize]);
                }
            }

            Resolution = resolution;
        }

        /// <summary>
        /// Replaces the population with <paramref name="startAgents"/> people scattered at random on livable land.
        /// The water-proximity field is brought up to date first, since livability may depend on it.
        /// </summary>
        public void SeedAgents(PhysarumFields fields, SettlementRules rules, int seed, int startAgents)
        {
            RelaxWater(fields, WaterPassesAtStart);

            SetPhysarumParameters(0f, rules);
            physarum.SetInt(StartAgentsId, Mathf.Min(startAgents, PoolSize));
            physarum.SetFloat(SeedId, seed);

            physarum.SetBuffer(initKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(initKernel, HeadingId, heading);
            physarum.SetBuffer(initKernel, AliveId, alive);
            BindWorld(initKernel, fields);
            physarum.Dispatch(initKernel, Groups(PoolSize, AgentGroupSize), 1, 1);
        }

        /// <summary>
        /// Settlers: starts up to <paramref name="count"/> new people at random points inside a circle.
        /// Those that find no free slot are lost.
        /// </summary>
        /// <param name="localDirection">Centre of the circle, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius of the circle in radians.</param>
        /// <param name="count">How many people to try to start.</param>
        public void SpawnAgents(PhysarumFields fields, SettlementRules rules, Vector3 localDirection, float angularRadius, int count)
        {
            DispatchSpawn(fields, rules, localDirection, angularRadius, count, anywhere: false);
        }

        /// <summary>
        /// Pioneers: tries to start up to <paramref name="count"/> people, each at the first good, empty
        /// place it finds anywhere on the planet.
        /// </summary>
        public void SpawnPioneers(PhysarumFields fields, SettlementRules rules, int count)
        {
            DispatchSpawn(fields, rules, Vector3.up, 0f, count, anywhere: true);
        }

        /// <summary>An impact: kills every person inside a circle.</summary>
        /// <param name="localDirection">Centre of the circle, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius of the circle in radians.</param>
        public void KillAgents(Vector3 localDirection, float angularRadius)
        {
            physarum.SetInt(PoolSizeId, PoolSize);
            physarum.SetInt(ResolutionId, Resolution);
            physarum.SetVector(EventDirectionId, localDirection.normalized);
            physarum.SetFloat(EventRadiusId, angularRadius);

            physarum.SetBuffer(killKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(killKernel, AliveId, alive);
            physarum.SetBuffer(killKernel, CountersId, counters);
            physarum.Dispatch(killKernel, Groups(PoolSize, AgentGroupSize), 1, 1);
        }

        /// <summary>Runs one simulation step of <paramref name="deltaSeconds"/> sim seconds.</summary>
        /// <param name="fields">The fields to read and write (ping-pong fields are swapped).</param>
        /// <param name="rules">What the people are currently allowed to do.</param>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(PhysarumFields fields, SettlementRules rules, float deltaSeconds)
        {
            frame++;
            SetPhysarumParameters(deltaSeconds, rules);
            int texelGroups = Groups(Resolution, TexelGroupSize);
            int texelCount = Resolution * Resolution * CubeSphere.FaceCount;
            Field trail = fields.Trail;
            Field land = fields.Land;
            Field soil = fields.Soil;

            // 1. Walkers.
            physarum.SetBuffer(clearCountersKernel, CountersId, counters);
            physarum.Dispatch(clearCountersKernel, 1, 1, 1);

            physarum.SetBuffer(agentKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(agentKernel, HeadingId, heading);
            physarum.SetBuffer(agentKernel, AliveId, alive);
            physarum.SetBuffer(agentKernel, DepositId, deposit);
            physarum.SetBuffer(agentKernel, CountersId, counters);
            physarum.SetTexture(agentKernel, TrailReadId, trail.Current);
            BindWorld(agentKernel, fields);
            physarum.Dispatch(agentKernel, Groups(PoolSize, AgentGroupSize), 1, 1);

            // 2. Paths: add deposits, blur, fade.
            physarum.SetBuffer(trailKernel, DepositId, deposit);
            physarum.SetTexture(trailKernel, TrailReadId, trail.Current);
            physarum.SetTexture(trailKernel, TrailWriteId, trail.Next);
            physarum.Dispatch(trailKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
            trail.Swap();

            // 3. Towns, farms and permanent roads.
            float referenceArea = Resolution / ReferenceResolution;
            settlement.SetInt(ResolutionId, Resolution);
            settlement.SetFloat(DeltaTimeId, deltaSeconds);
            settlement.SetFloat(DepositAmountId, config.physarumDepositAmount);
            settlement.SetFloat(TrafficScaleId, referenceArea * referenceArea);
            settlement.SetFloat(TownDecayId, config.townDecay);
            settlement.SetFloat(TownSpreadId, config.townSpread);
            settlement.SetFloat(FarmStartId, config.farmStart);
            settlement.SetFloat(FarmFullId, config.farmFull);
            settlement.SetFloat(CoreStartId, config.coreStart);
            settlement.SetFloat(CoreFullId, config.coreFull);
            settlement.SetFloat(FarmGrowthId, config.farmGrowth);
            settlement.SetFloat(FarmAbandonId, config.farmAbandon);
            settlement.SetFloat(CrowdRateId, config.crowdingRate);
            SetRuleValues(settlement, rules);

            float roadHardness = 1f + 2f * rules.RoadTier;
            settlement.SetFloat(PaveThresholdId, config.pavingThreshold);
            settlement.SetFloat(PaveRateId, roadHardness / config.pavingSeconds);
            settlement.SetFloat(PaveFadeId, 1f / (config.pavingFadeSeconds * roadHardness));

            settlement.SetBuffer(landKernel, DepositId, deposit);
            settlement.SetTexture(landKernel, LandReadId, land.Current);
            settlement.SetTexture(landKernel, LandWriteId, land.Next);
            settlement.SetTexture(landKernel, TerrainId, fields.Terrain);
            settlement.SetTexture(landKernel, ClimateId, fields.Climate);
            settlement.SetTexture(landKernel, SoilId, soil.Current);
            settlement.SetTexture(landKernel, TrailId, trail.Current);
            settlement.SetTexture(landKernel, WaterId, fields.Water.Current);
            settlement.Dispatch(landKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
            land.Swap();

            // 4. Empty the deposit buffer for the next step.
            physarum.SetInt(ClearCountId, texelCount);
            physarum.SetBuffer(clearDepositKernel, DepositId, deposit);
            physarum.Dispatch(clearDepositKernel, Groups(texelCount, AgentGroupSize), 1, 1);

            // 5. Keep the water-proximity field up to date.
            RelaxWater(fields, WaterPassesPerStep);

            // 6. Transport nutrients, in enough sub-steps to stay stable.
            float texelAngle = Mathf.PI * 0.5f / Resolution;
            float rate = config.nutrientDiffusion * deltaSeconds / (texelAngle * texelAngle);
            int subSteps = Mathf.Max(1, Mathf.CeilToInt(rate / MaxExchangePerSubStep));

            nutrients.SetInt(ResolutionId, Resolution);
            nutrients.SetFloat(DeltaTimeId, deltaSeconds / subSteps);
            nutrients.SetFloat(DiffusionId, config.nutrientDiffusion);
            nutrients.SetFloat(DecompositionRateId, config.decompositionRate);
            nutrients.SetTexture(transportKernel, TerrainId, fields.Terrain);
            nutrients.SetTexture(transportKernel, ClimateId, fields.Climate);

            for (int i = 0; i < subSteps; i++)
            {
                nutrients.SetTexture(transportKernel, SoilReadId, soil.Current);
                nutrients.SetTexture(transportKernel, SoilWriteId, soil.Next);
                nutrients.Dispatch(transportKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
                soil.Swap();
            }
        }

        /// <summary>
        /// Relaxes the water-proximity field by <paramref name="passes"/> texels (1 at water, falling to 0 at the
        /// configured reach). A step does a few passes; a new world does enough to cover the whole reach.
        /// </summary>
        public void RelaxWater(PhysarumFields fields, int passes)
        {
            int texelGroups = Groups(Resolution, TexelGroupSize);
            float texelAngle = Mathf.PI * 0.5f / Resolution;
            settlement.SetInt(ResolutionId, Resolution);
            settlement.SetFloat(WaterFallId, texelAngle / config.waterFieldReach);
            settlement.SetTexture(waterKernel, TerrainId, fields.Terrain);

            for (int i = 0; i < passes; i++)
            {
                settlement.SetTexture(waterKernel, WaterReadId, fields.Water.Current);
                settlement.SetTexture(waterKernel, WaterWriteId, fields.Water.Next);
                settlement.Dispatch(waterKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
                fields.Water.Swap();
            }
        }

        /// <summary>Releases every GPU buffer.</summary>
        public void Dispose()
        {
            ReleaseAgentBuffers();
            ReleaseTexelBuffers();
            if (counters != null)
            {
                counters.Release();
                counters = null;
            }
        }

        // ------------------------------------------------------------------ internals

        private void DispatchSpawn(PhysarumFields fields, SettlementRules rules, Vector3 localDirection, float angularRadius, int count, bool anywhere)
        {
            frame++;
            SetPhysarumParameters(0f, rules);
            physarum.SetInt(SpawnCountId, count);
            physarum.SetInt(SpawnAnywhereId, anywhere ? 1 : 0);
            physarum.SetVector(EventDirectionId, localDirection.normalized);
            physarum.SetFloat(EventRadiusId, angularRadius);

            physarum.SetBuffer(spawnKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(spawnKernel, HeadingId, heading);
            physarum.SetBuffer(spawnKernel, AliveId, alive);
            physarum.SetBuffer(spawnKernel, CountersId, counters);
            BindWorld(spawnKernel, fields);
            physarum.Dispatch(spawnKernel, Groups(count, AgentGroupSize), 1, 1);
        }

        // The textures a kernel of Physarum.compute reads about the world.
        private void BindWorld(int kernel, PhysarumFields fields)
        {
            physarum.SetTexture(kernel, LandId, fields.Land.Current);
            physarum.SetTexture(kernel, WaterId, fields.Water.Current);
            physarum.SetTexture(kernel, SoilId, fields.Soil.Current);
            physarum.SetTexture(kernel, TerrainId, fields.Terrain);
            physarum.SetTexture(kernel, ClimateId, fields.Climate);
        }

        // The irrigation values Livability.hlsl reads, on whichever compute shader includes it.
        private void SetRuleValues(ComputeShader shader, SettlementRules rules)
        {
            shader.SetFloat(IrrigationOnId, rules.Irrigation ? 1f : 0f);
            shader.SetFloat(IrrigationMoistureId, config.irrigationMoisture);
            shader.SetFloat(IrrigationNearId, rules.IrrigationProximity(config));
        }

        private void SetPhysarumParameters(float deltaSeconds, SettlementRules rules)
        {
            physarum.SetInt(PoolSizeId, PoolSize);
            physarum.SetInt(ResolutionId, Resolution);
            physarum.SetInt(FrameId, (int)frame);
            physarum.SetFloat(DeltaTimeId, deltaSeconds);

            physarum.SetFloat(SensorAngleId, config.physarumSensorAngleDegrees * Mathf.Deg2Rad);
            physarum.SetFloat(SensorDistanceId, config.physarumSensorDistance);
            physarum.SetFloat(TurnSpeedId, config.physarumTurnSpeed);
            physarum.SetFloat(SpeedId, config.physarumSpeed);
            physarum.SetFloat(DepositAmountId, config.physarumDepositAmount);
            physarum.SetFloat(TrailDiffusionId, config.physarumTrailDiffusion);
            physarum.SetFloat(TrailDecayId, config.physarumTrailDecay);
            physarum.SetFloat(TrailMaxId, config.physarumTrailMax);
            physarum.SetFloat(FoodWeightId, config.physarumFoodWeight);
            physarum.SetFloat(TownWeightId, config.physarumTownWeight);
            physarum.SetFloat(FoodRateId, config.physarumFoodRate);
            physarum.SetFloat(UpkeepId, config.physarumUpkeep);
            physarum.SetFloat(StartEnergyId, config.physarumStartEnergy);
            physarum.SetFloat(ReproduceEnergyId, config.physarumReproduceEnergy);
            physarum.SetFloat(BirthCostId, config.physarumBirthCost);
            physarum.SetFloat(OceanPenaltyId, config.physarumOceanPenalty);
            physarum.SetFloat(FarmBoostId, config.farmBoost);
            physarum.SetFloat(CapacityBaseId, config.townCapacity);
            physarum.SetFloat(CapacityFarmId, config.farmCapacity);
            physarum.SetFloat(TownSlowdownId, config.townSlowdown);
            physarum.SetFloat(TownFullId, config.townFull);
            physarum.SetFloat(RoadWeightId, config.roadAttraction * (1f + 0.5f * rules.RoadTier));
            physarum.SetFloat(RoadSpeedId, config.roadSpeedBoost * (1f + 0.5f * rules.RoadTier));
            SetRuleValues(physarum, rules);
        }

        private static int Groups(int count, int groupSize)
        {
            return (count + groupSize - 1) / groupSize;
        }

        private static ComputeBuffer NewZeroedBuffer(int count)
        {
            var buffer = new ComputeBuffer(count, 4);
            buffer.SetData(new uint[count]);
            return buffer;
        }

        private void ReleaseAgentBuffers()
        {
            if (posEnergy != null)
            {
                posEnergy.Release();
                heading.Release();
                alive.Release();
                posEnergy = null;
                heading = null;
                alive = null;
            }
        }

        private void ReleaseTexelBuffers()
        {
            if (deposit != null)
            {
                deposit.Release();
                deposit = null;
            }
        }

        // Shader property ids, looked up once so a step allocates nothing.
        private static readonly int PosEnergyId = Shader.PropertyToID("_PosEnergy");
        private static readonly int HeadingId = Shader.PropertyToID("_Heading");
        private static readonly int AliveId = Shader.PropertyToID("_Alive");
        private static readonly int OldPosEnergyId = Shader.PropertyToID("_OldPosEnergy");
        private static readonly int OldHeadingId = Shader.PropertyToID("_OldHeading");
        private static readonly int OldAliveId = Shader.PropertyToID("_OldAlive");
        private static readonly int DepositId = Shader.PropertyToID("_Deposit");
        private static readonly int CountersId = Shader.PropertyToID("_Counters");
        private static readonly int TrailReadId = Shader.PropertyToID("_TrailRead");
        private static readonly int TrailWriteId = Shader.PropertyToID("_TrailWrite");
        private static readonly int TrailId = Shader.PropertyToID("_Trail");
        private static readonly int LandId = Shader.PropertyToID("_Land");
        private static readonly int LandReadId = Shader.PropertyToID("_LandRead");
        private static readonly int LandWriteId = Shader.PropertyToID("_LandWrite");
        private static readonly int WaterId = Shader.PropertyToID("_Water");
        private static readonly int WaterReadId = Shader.PropertyToID("_WaterRead");
        private static readonly int WaterWriteId = Shader.PropertyToID("_WaterWrite");
        private static readonly int SoilId = Shader.PropertyToID("_Soil");
        private static readonly int SoilReadId = Shader.PropertyToID("_SoilRead");
        private static readonly int SoilWriteId = Shader.PropertyToID("_SoilWrite");
        private static readonly int TerrainId = Shader.PropertyToID("_Terrain");
        private static readonly int ClimateId = Shader.PropertyToID("_Climate");
        private static readonly int PoolSizeId = Shader.PropertyToID("_PoolSize");
        private static readonly int OldPoolSizeId = Shader.PropertyToID("_OldPoolSize");
        private static readonly int StartAgentsId = Shader.PropertyToID("_StartAgents");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int ClearCountId = Shader.PropertyToID("_ClearCount");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int StartEnergyId = Shader.PropertyToID("_StartEnergy");
        private static readonly int SensorAngleId = Shader.PropertyToID("_SensorAngle");
        private static readonly int SensorDistanceId = Shader.PropertyToID("_SensorDistance");
        private static readonly int TurnSpeedId = Shader.PropertyToID("_TurnSpeed");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int DepositAmountId = Shader.PropertyToID("_DepositAmount");
        private static readonly int TrailDiffusionId = Shader.PropertyToID("_TrailDiffusion");
        private static readonly int TrailDecayId = Shader.PropertyToID("_TrailDecay");
        private static readonly int TrailMaxId = Shader.PropertyToID("_TrailMax");
        private static readonly int FoodWeightId = Shader.PropertyToID("_FoodWeight");
        private static readonly int TownWeightId = Shader.PropertyToID("_TownWeight");
        private static readonly int FoodRateId = Shader.PropertyToID("_FoodRate");
        private static readonly int UpkeepId = Shader.PropertyToID("_Upkeep");
        private static readonly int ReproduceEnergyId = Shader.PropertyToID("_ReproduceEnergy");
        private static readonly int BirthCostId = Shader.PropertyToID("_BirthCost");
        private static readonly int OceanPenaltyId = Shader.PropertyToID("_OceanPenalty");
        private static readonly int FarmBoostId = Shader.PropertyToID("_FarmBoost");
        private static readonly int CapacityBaseId = Shader.PropertyToID("_CapacityBase");
        private static readonly int CapacityFarmId = Shader.PropertyToID("_CapacityFarm");
        private static readonly int TownSlowdownId = Shader.PropertyToID("_TownSlowdown");
        private static readonly int TownFullId = Shader.PropertyToID("_TownFull");
        private static readonly int RoadWeightId = Shader.PropertyToID("_RoadWeight");
        private static readonly int RoadSpeedId = Shader.PropertyToID("_RoadSpeed");
        private static readonly int SpawnCountId = Shader.PropertyToID("_SpawnCount");
        private static readonly int SpawnAnywhereId = Shader.PropertyToID("_SpawnAnywhere");
        private static readonly int EventDirectionId = Shader.PropertyToID("_EventDirection");
        private static readonly int EventRadiusId = Shader.PropertyToID("_EventRadius");
        private static readonly int DiffusionId = Shader.PropertyToID("_Diffusion");
        private static readonly int DecompositionRateId = Shader.PropertyToID("_DecompositionRate");
        private static readonly int TrafficScaleId = Shader.PropertyToID("_TrafficScale");
        private static readonly int TownDecayId = Shader.PropertyToID("_TownDecay");
        private static readonly int TownSpreadId = Shader.PropertyToID("_TownSpread");
        private static readonly int FarmStartId = Shader.PropertyToID("_FarmStart");
        private static readonly int FarmFullId = Shader.PropertyToID("_FarmFull");
        private static readonly int CoreStartId = Shader.PropertyToID("_CoreStart");
        private static readonly int CoreFullId = Shader.PropertyToID("_CoreFull");
        private static readonly int FarmGrowthId = Shader.PropertyToID("_FarmGrowth");
        private static readonly int FarmAbandonId = Shader.PropertyToID("_FarmAbandon");
        private static readonly int CrowdRateId = Shader.PropertyToID("_CrowdRate");
        private static readonly int PaveThresholdId = Shader.PropertyToID("_PaveThreshold");
        private static readonly int PaveRateId = Shader.PropertyToID("_PaveRate");
        private static readonly int PaveFadeId = Shader.PropertyToID("_PaveFade");
        private static readonly int WaterFallId = Shader.PropertyToID("_WaterFall");
        private static readonly int IrrigationOnId = Shader.PropertyToID("_IrrigationOn");
        private static readonly int IrrigationMoistureId = Shader.PropertyToID("_IrrigationMoisture");
        private static readonly int IrrigationNearId = Shader.PropertyToID("_IrrigationNear");
    }
}
