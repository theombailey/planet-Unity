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
//                                  follow (Settlement.compute; reads Land.Current, writes Next, swap)
//             4. ClearDeposit      the deposit buffer is emptied
//             5. NutrientTransport nutrients spread through the soil (sub-stepped)
//           Every stage is a GPU dispatch; nothing is read back to the CPU
//           (the population count is read back asynchronously by PhysarumSystem).
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
    /// <summary>Runs the walkers, the path field, the towns and farms and the nutrient transport on the GPU.</summary>
    public sealed class PhysarumSimulation : IDisposable
    {
        private const int AgentGroupSize = 64;
        private const int TexelGroupSize = 8;
        private const int CounterCount = 4;
        private const float MaxExchangePerSubStep = 0.2f;

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

        /// <summary>Replaces the population with <paramref name="startAgents"/> people scattered at random on livable land.</summary>
        public void SeedAgents(Field land, Field soil, RenderTexture terrain, RenderTexture climate, int seed, int startAgents)
        {
            SetPhysarumParameters(0f);
            physarum.SetInt(StartAgentsId, Mathf.Min(startAgents, PoolSize));
            physarum.SetFloat(SeedId, seed);

            physarum.SetBuffer(initKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(initKernel, HeadingId, heading);
            physarum.SetBuffer(initKernel, AliveId, alive);
            physarum.SetTexture(initKernel, LandId, land.Current);
            physarum.SetTexture(initKernel, SoilId, soil.Current);
            physarum.SetTexture(initKernel, TerrainId, terrain);
            physarum.SetTexture(initKernel, ClimateId, climate);
            physarum.Dispatch(initKernel, Groups(PoolSize, AgentGroupSize), 1, 1);
        }

        /// <summary>
        /// Settlers: starts up to <paramref name="count"/> new people at random points inside a circle.
        /// Those that find no free slot are lost.
        /// </summary>
        /// <param name="localDirection">Centre of the circle, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius of the circle in radians.</param>
        /// <param name="count">How many people to try to start.</param>
        public void SpawnAgents(Field land, Field soil, RenderTexture terrain, RenderTexture climate, Vector3 localDirection, float angularRadius, int count)
        {
            DispatchSpawn(land, soil, terrain, climate, localDirection, angularRadius, count, anywhere: false);
        }

        /// <summary>
        /// Pioneers: tries to start up to <paramref name="count"/> people, each at the first good, empty
        /// place it finds anywhere on the planet.
        /// </summary>
        public void SpawnPioneers(Field land, Field soil, RenderTexture terrain, RenderTexture climate, int count)
        {
            DispatchSpawn(land, soil, terrain, climate, Vector3.up, 0f, count, anywhere: true);
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
        /// <param name="trail">Path field (ping-pong; swapped).</param>
        /// <param name="land">Land field: town density and farmland (ping-pong; swapped).</param>
        /// <param name="soil">Soil field (R nutrients, G organic matter; ping-pong; swapped by transport).</param>
        /// <param name="terrain">Terrain texture (read).</param>
        /// <param name="climate">Climate texture (read).</param>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(Field trail, Field land, Field soil, RenderTexture terrain, RenderTexture climate, float deltaSeconds)
        {
            frame++;
            SetPhysarumParameters(deltaSeconds);
            int texelGroups = Groups(Resolution, TexelGroupSize);
            int texelCount = Resolution * Resolution * CubeSphere.FaceCount;

            // 1. Walkers.
            physarum.SetBuffer(clearCountersKernel, CountersId, counters);
            physarum.Dispatch(clearCountersKernel, 1, 1, 1);

            physarum.SetBuffer(agentKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(agentKernel, HeadingId, heading);
            physarum.SetBuffer(agentKernel, AliveId, alive);
            physarum.SetBuffer(agentKernel, DepositId, deposit);
            physarum.SetBuffer(agentKernel, CountersId, counters);
            physarum.SetTexture(agentKernel, TrailReadId, trail.Current);
            physarum.SetTexture(agentKernel, LandId, land.Current);
            physarum.SetTexture(agentKernel, SoilId, soil.Current);
            physarum.SetTexture(agentKernel, TerrainId, terrain);
            physarum.SetTexture(agentKernel, ClimateId, climate);
            physarum.Dispatch(agentKernel, Groups(PoolSize, AgentGroupSize), 1, 1);

            // 2. Paths: add deposits, blur, fade.
            physarum.SetBuffer(trailKernel, DepositId, deposit);
            physarum.SetTexture(trailKernel, TrailReadId, trail.Current);
            physarum.SetTexture(trailKernel, TrailWriteId, trail.Next);
            physarum.Dispatch(trailKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
            trail.Swap();

            // 3. Towns and farms.
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
            settlement.SetBuffer(landKernel, DepositId, deposit);
            settlement.SetTexture(landKernel, LandReadId, land.Current);
            settlement.SetTexture(landKernel, LandWriteId, land.Next);
            settlement.SetTexture(landKernel, TerrainId, terrain);
            settlement.SetTexture(landKernel, ClimateId, climate);
            settlement.SetTexture(landKernel, SoilId, soil.Current);
            settlement.Dispatch(landKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
            land.Swap();

            // 4. Empty the deposit buffer for the next step.
            physarum.SetInt(ClearCountId, texelCount);
            physarum.SetBuffer(clearDepositKernel, DepositId, deposit);
            physarum.Dispatch(clearDepositKernel, Groups(texelCount, AgentGroupSize), 1, 1);

            // 5. Transport nutrients, in enough sub-steps to stay stable.
            float texelAngle = Mathf.PI * 0.5f / Resolution;
            float rate = config.nutrientDiffusion * deltaSeconds / (texelAngle * texelAngle);
            int subSteps = Mathf.Max(1, Mathf.CeilToInt(rate / MaxExchangePerSubStep));

            nutrients.SetInt(ResolutionId, Resolution);
            nutrients.SetFloat(DeltaTimeId, deltaSeconds / subSteps);
            nutrients.SetFloat(DiffusionId, config.nutrientDiffusion);
            nutrients.SetFloat(DecompositionRateId, config.decompositionRate);
            nutrients.SetTexture(transportKernel, TerrainId, terrain);
            nutrients.SetTexture(transportKernel, ClimateId, climate);

            for (int i = 0; i < subSteps; i++)
            {
                nutrients.SetTexture(transportKernel, SoilReadId, soil.Current);
                nutrients.SetTexture(transportKernel, SoilWriteId, soil.Next);
                nutrients.Dispatch(transportKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
                soil.Swap();
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

        private void DispatchSpawn(Field land, Field soil, RenderTexture terrain, RenderTexture climate, Vector3 localDirection, float angularRadius, int count, bool anywhere)
        {
            frame++;
            SetPhysarumParameters(0f);
            physarum.SetInt(SpawnCountId, count);
            physarum.SetInt(SpawnAnywhereId, anywhere ? 1 : 0);
            physarum.SetVector(EventDirectionId, localDirection.normalized);
            physarum.SetFloat(EventRadiusId, angularRadius);

            physarum.SetBuffer(spawnKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(spawnKernel, HeadingId, heading);
            physarum.SetBuffer(spawnKernel, AliveId, alive);
            physarum.SetBuffer(spawnKernel, CountersId, counters);
            physarum.SetTexture(spawnKernel, LandId, land.Current);
            physarum.SetTexture(spawnKernel, SoilId, soil.Current);
            physarum.SetTexture(spawnKernel, TerrainId, terrain);
            physarum.SetTexture(spawnKernel, ClimateId, climate);
            physarum.Dispatch(spawnKernel, Groups(count, AgentGroupSize), 1, 1);
        }

        private void SetPhysarumParameters(float deltaSeconds)
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
        private static readonly int LandId = Shader.PropertyToID("_Land");
        private static readonly int LandReadId = Shader.PropertyToID("_LandRead");
        private static readonly int LandWriteId = Shader.PropertyToID("_LandWrite");
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
    }
}
