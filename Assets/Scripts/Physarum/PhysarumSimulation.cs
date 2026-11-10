// =============================================================================
// PhysarumSimulation
// -----------------------------------------------------------------------------
// Purpose : The C# side of Physarum.compute and Nutrients.compute: owns the agent
//           and per-texel GPU buffers and runs one simulation step. Free of scene
//           references, so tests can drive it directly.
// Idea    : One step, in this order:
//             1. AgentStep        every agent senses, steers, moves, leaves trail,
//                                 eats, pays upkeep, dies or splits
//             2. TrailDiffuse     the deposits are added to the trail, which is
//                                 blurred and faded (reads Current, writes Next, swap)
//             3. ClearDeposit     the deposit buffer is emptied
//             4. ApplyEatReturn   what was eaten leaves the soil; dead agents'
//                                 mass joins the soil's organic matter
//             5. NutrientTransport nutrients spread, fast along trails (sub-stepped)
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
    /// <summary>Runs the physarum agents, the trail and the nutrient transport on the GPU.</summary>
    public sealed class PhysarumSimulation : IDisposable
    {
        private const int AgentGroupSize = 64;
        private const int TexelGroupSize = 8;
        private const int CounterCount = 4;
        private const float MaxExchangePerSubStep = 0.2f;

        private readonly ComputeShader physarum;
        private readonly ComputeShader nutrients;
        private readonly SimConfig config;

        private readonly int initKernel;
        private readonly int copyKernel;
        private readonly int clearCountersKernel;
        private readonly int agentKernel;
        private readonly int trailKernel;
        private readonly int clearDepositKernel;
        private readonly int applyKernel;
        private readonly int transportKernel;
        private readonly int spawnKernel;
        private readonly int killKernel;

        private ComputeBuffer posEnergy;
        private ComputeBuffer heading;
        private ComputeBuffer alive;
        private ComputeBuffer counters;
        private ComputeBuffer deposit;
        private ComputeBuffer eaten;
        private ComputeBuffer returned;
        private uint frame;

        /// <summary>Creates a simulation using the two compute shaders and the given parameters.</summary>
        public PhysarumSimulation(ComputeShader physarumShader, ComputeShader nutrientShader, SimConfig simConfig)
        {
            physarum = physarumShader;
            nutrients = nutrientShader;
            config = simConfig;

            initKernel = physarum.FindKernel("InitAgents");
            copyKernel = physarum.FindKernel("CopyAgents");
            clearCountersKernel = physarum.FindKernel("ClearCounters");
            agentKernel = physarum.FindKernel("AgentStep");
            trailKernel = physarum.FindKernel("TrailDiffuse");
            clearDepositKernel = physarum.FindKernel("ClearDeposit");
            applyKernel = nutrients.FindKernel("ApplyEatReturn");
            transportKernel = nutrients.FindKernel("NutrientTransport");
            spawnKernel = physarum.FindKernel("SpawnAgents");
            killKernel = physarum.FindKernel("KillAgents");
        }

        /// <summary>Number of agent slots (the most agents that can be alive).</summary>
        public int PoolSize { get; private set; }

        /// <summary>Texels per face edge of the per-texel buffers.</summary>
        public int Resolution { get; private set; }

        /// <summary>True once buffers exist.</summary>
        public bool IsAllocated => posEnergy != null;

        /// <summary>Population counters: [0] alive, [1] born this step, [2] died this step. For readback.</summary>
        public ComputeBuffer Counters => counters;

        /// <summary>Agent position (xyz) and energy (w). For readback in tests.</summary>
        public ComputeBuffer PosEnergy => posEnergy;

        /// <summary>Agent heading (xyz). For readback in tests.</summary>
        public ComputeBuffer Heading => heading;

        /// <summary>Agent alive flags (0 dead, 1 alive, 2 newborn). For readback in tests.</summary>
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
                eaten = NewZeroedBuffer(texelCount);
                returned = NewZeroedBuffer(texelCount);
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

        /// <summary>Replaces the population with <paramref name="startAgents"/> agents scattered at random on dry land.</summary>
        public void SeedAgents(RenderTexture terrain, int seed, int startAgents)
        {
            physarum.SetInt(PoolSizeId, PoolSize);
            physarum.SetInt(StartAgentsId, Mathf.Min(startAgents, PoolSize));
            physarum.SetInt(ResolutionId, Resolution);
            physarum.SetFloat(SeedId, seed);
            physarum.SetFloat(StartEnergyId, config.physarumStartEnergy);

            physarum.SetBuffer(initKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(initKernel, HeadingId, heading);
            physarum.SetBuffer(initKernel, AliveId, alive);
            physarum.SetTexture(initKernel, TerrainId, terrain);
            physarum.Dispatch(initKernel, Groups(PoolSize, AgentGroupSize), 1, 1);
        }

        /// <summary>
        /// The spore dish: starts up to <paramref name="count"/> new agents at random points inside a circle.
        /// Spores that find no free slot are lost.
        /// </summary>
        /// <param name="localDirection">Centre of the circle, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius of the circle in radians.</param>
        /// <param name="count">How many agents to try to start.</param>
        public void SpawnAgents(Vector3 localDirection, float angularRadius, int count)
        {
            frame++;
            physarum.SetInt(PoolSizeId, PoolSize);
            physarum.SetInt(FrameId, (int)frame);
            physarum.SetInt(ResolutionId, Resolution);
            physarum.SetInt(SpawnCountId, count);
            physarum.SetVector(EventDirectionId, localDirection.normalized);
            physarum.SetFloat(EventRadiusId, angularRadius);
            physarum.SetFloat(StartEnergyId, config.physarumStartEnergy);

            physarum.SetBuffer(spawnKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(spawnKernel, HeadingId, heading);
            physarum.SetBuffer(spawnKernel, AliveId, alive);
            physarum.SetBuffer(spawnKernel, CountersId, counters);
            physarum.Dispatch(spawnKernel, Groups(count, AgentGroupSize), 1, 1);
        }

        /// <summary>An impact: kills every agent inside a circle; their body mass returns to the soil.</summary>
        /// <param name="localDirection">Centre of the circle, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius of the circle in radians.</param>
        public void KillAgents(Vector3 localDirection, float angularRadius)
        {
            physarum.SetInt(PoolSizeId, PoolSize);
            physarum.SetInt(ResolutionId, Resolution);
            physarum.SetVector(EventDirectionId, localDirection.normalized);
            physarum.SetFloat(EventRadiusId, angularRadius);
            physarum.SetFloat(BirthCostId, config.physarumBirthCost);

            physarum.SetBuffer(killKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(killKernel, AliveId, alive);
            physarum.SetBuffer(killKernel, ReturnedId, returned);
            physarum.SetBuffer(killKernel, CountersId, counters);
            physarum.Dispatch(killKernel, Groups(PoolSize, AgentGroupSize), 1, 1);
        }

        /// <summary>Runs one simulation step of <paramref name="deltaSeconds"/> sim seconds.</summary>
        /// <param name="trail">Trail field (ping-pong; swapped).</param>
        /// <param name="soil">Soil field (R nutrients, G organic matter; ping-pong; swapped by transport).</param>
        /// <param name="terrain">Terrain texture (read).</param>
        /// <param name="climate">Climate texture (read).</param>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(Field trail, Field soil, RenderTexture terrain, RenderTexture climate, float deltaSeconds)
        {
            frame++;
            SetPhysarumParameters(deltaSeconds);
            int texelGroups = Groups(Resolution, TexelGroupSize);
            int texelCount = Resolution * Resolution * CubeSphere.FaceCount;

            // 1. Agents.
            physarum.SetBuffer(clearCountersKernel, CountersId, counters);
            physarum.Dispatch(clearCountersKernel, 1, 1, 1);

            physarum.SetBuffer(agentKernel, PosEnergyId, posEnergy);
            physarum.SetBuffer(agentKernel, HeadingId, heading);
            physarum.SetBuffer(agentKernel, AliveId, alive);
            physarum.SetBuffer(agentKernel, DepositId, deposit);
            physarum.SetBuffer(agentKernel, EatenId, eaten);
            physarum.SetBuffer(agentKernel, ReturnedId, returned);
            physarum.SetBuffer(agentKernel, CountersId, counters);
            physarum.SetTexture(agentKernel, TrailReadId, trail.Current);
            physarum.SetTexture(agentKernel, SoilId, soil.Current);
            physarum.SetTexture(agentKernel, TerrainId, terrain);
            physarum.SetTexture(agentKernel, ClimateId, climate);
            physarum.Dispatch(agentKernel, Groups(PoolSize, AgentGroupSize), 1, 1);

            // 2. Trail: add deposits, blur, fade.
            physarum.SetBuffer(trailKernel, DepositId, deposit);
            physarum.SetTexture(trailKernel, TrailReadId, trail.Current);
            physarum.SetTexture(trailKernel, TrailWriteId, trail.Next);
            physarum.Dispatch(trailKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
            trail.Swap();

            // 3. Empty the deposit buffer for the next step.
            physarum.SetInt(ClearCountId, texelCount);
            physarum.SetBuffer(clearDepositKernel, DepositId, deposit);
            physarum.Dispatch(clearDepositKernel, Groups(texelCount, AgentGroupSize), 1, 1);

            // 4. Apply eating and returns to the soil.
            nutrients.SetInt(ResolutionId, Resolution);
            nutrients.SetTexture(applyKernel, SoilInPlaceId, soil.Current);
            nutrients.SetBuffer(applyKernel, EatenId, eaten);
            nutrients.SetBuffer(applyKernel, ReturnedId, returned);
            nutrients.Dispatch(applyKernel, texelGroups, texelGroups, CubeSphere.FaceCount);

            // 5. Transport nutrients, in enough sub-steps to stay stable.
            float texelAngle = Mathf.PI * 0.5f / Resolution;
            float fastestDiffusion = config.nutrientDiffusion + config.nutrientNetworkDiffusion * config.nutrientTrailSaturation;
            float rate = fastestDiffusion * deltaSeconds / (texelAngle * texelAngle);
            int subSteps = Mathf.Max(1, Mathf.CeilToInt(rate / MaxExchangePerSubStep));

            nutrients.SetFloat(DeltaTimeId, deltaSeconds / subSteps);
            nutrients.SetFloat(DiffusionBaseId, config.nutrientDiffusion);
            nutrients.SetFloat(DiffusionNetworkId, config.nutrientNetworkDiffusion);
            nutrients.SetFloat(TrailSaturationId, config.nutrientTrailSaturation);
            nutrients.SetFloat(DecompositionRateId, config.decompositionRate);
            nutrients.SetTexture(transportKernel, TrailId, trail.Current);
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
            physarum.SetFloat(NutrientWeightId, config.physarumNutrientWeight);
            physarum.SetFloat(EatRateId, config.physarumEatRate);
            physarum.SetFloat(EnergyPerNutrientId, config.physarumEnergyPerNutrient);
            physarum.SetFloat(UpkeepId, config.physarumUpkeep);
            physarum.SetFloat(ReproduceEnergyId, config.physarumReproduceEnergy);
            physarum.SetFloat(BirthCostId, config.physarumBirthCost);
            physarum.SetFloat(OceanPenaltyId, config.physarumOceanPenalty);
            physarum.SetFloat(ColdLimitId, config.physarumColdLimit);
            physarum.SetFloat(ColdPenaltyId, config.physarumColdPenalty);
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
                eaten.Release();
                returned.Release();
                deposit = null;
                eaten = null;
                returned = null;
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
        private static readonly int EatenId = Shader.PropertyToID("_Eaten");
        private static readonly int ReturnedId = Shader.PropertyToID("_Returned");
        private static readonly int CountersId = Shader.PropertyToID("_Counters");
        private static readonly int TrailReadId = Shader.PropertyToID("_TrailRead");
        private static readonly int TrailWriteId = Shader.PropertyToID("_TrailWrite");
        private static readonly int TrailId = Shader.PropertyToID("_Trail");
        private static readonly int SoilId = Shader.PropertyToID("_Soil");
        private static readonly int SoilInPlaceId = Shader.PropertyToID("_Soil");
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
        private static readonly int NutrientWeightId = Shader.PropertyToID("_NutrientWeight");
        private static readonly int EatRateId = Shader.PropertyToID("_EatRate");
        private static readonly int EnergyPerNutrientId = Shader.PropertyToID("_EnergyPerNutrient");
        private static readonly int UpkeepId = Shader.PropertyToID("_Upkeep");
        private static readonly int ReproduceEnergyId = Shader.PropertyToID("_ReproduceEnergy");
        private static readonly int BirthCostId = Shader.PropertyToID("_BirthCost");
        private static readonly int OceanPenaltyId = Shader.PropertyToID("_OceanPenalty");
        private static readonly int ColdLimitId = Shader.PropertyToID("_ColdLimit");
        private static readonly int SpawnCountId = Shader.PropertyToID("_SpawnCount");
        private static readonly int EventDirectionId = Shader.PropertyToID("_EventDirection");
        private static readonly int EventRadiusId = Shader.PropertyToID("_EventRadius");
        private static readonly int ColdPenaltyId = Shader.PropertyToID("_ColdPenalty");
        private static readonly int DiffusionBaseId = Shader.PropertyToID("_DiffusionBase");
        private static readonly int DiffusionNetworkId = Shader.PropertyToID("_DiffusionNetwork");
        private static readonly int TrailSaturationId = Shader.PropertyToID("_TrailSaturation");
        private static readonly int DecompositionRateId = Shader.PropertyToID("_DecompositionRate");
    }
}
