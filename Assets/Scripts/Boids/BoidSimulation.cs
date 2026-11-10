// =============================================================================
// BoidSimulation
// -----------------------------------------------------------------------------
// Purpose : The C# side of Boids.compute: owns the boid buffers and the spatial
//           hash grid, and runs one simulation step. Free of scene references, so
//           tests can drive it directly.
// Idea    : One step, in this order (every stage is a GPU dispatch; nothing is
//           read back during the step):
//             1. ClearCounters   the births / deaths / alive counters go to zero
//             2. ClearGrid       the spatial hash grid is emptied
//             3. BuildGrid       every live boid adds itself to its cell's list
//             4. BoidStep        every boid sees its neighbours, steers, flies, eats,
//                                carries or drops a seed, dies or breeds
//             5. ApplyReturn     dead boids' bodies join the soil's organic matter
//           The seed-drop events and the counters are read by BoidSystem with
//           AsyncGPUReadback, a few times a second.
// Grid    : cells are cubes at least `perception` wide, covering [-extent, extent]
//           on each axis (the shell the boids fly in). The grid is capped at
//           MaxGridDim cells per axis so a tiny perception cannot allocate gigabytes;
//           a bigger cell only means each boid looks at a few more neighbours.
// Storage : two float4 buffers and one uint per boid (see Boids.compute), so a
//           15,000-boid pool is under a megabyte.
// =============================================================================

using System;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Boids
{
    /// <summary>Runs the boids and their spatial hash grid on the GPU.</summary>
    public sealed class BoidSimulation : IDisposable
    {
        /// <summary>Most seed-drop events kept between two reads.</summary>
        public const int DropCapacity = 256;

        private const int GroupSize = 64;
        private const int TexelGroupSize = 8;
        private const int CounterCount = 4;
        private const int MaxGridDim = 128;
        private const float GridExtent = 1.3f;

        private readonly ComputeShader shader;
        private readonly SimConfig config;

        private readonly int initKernel;
        private readonly int copyKernel;
        private readonly int clearCountersKernel;
        private readonly int clearDropsKernel;
        private readonly int clearGridKernel;
        private readonly int buildGridKernel;
        private readonly int stepKernel;
        private readonly int spawnKernel;
        private readonly int killKernel;
        private readonly int applyReturnKernel;

        private ComputeBuffer posEnergy;
        private ComputeBuffer velSeed;
        private ComputeBuffer alive;
        private ComputeBuffer next;
        private ComputeBuffer cellHead;
        private ComputeBuffer counters;
        private ComputeBuffer drops;
        private ComputeBuffer returned;
        private int gridDim;
        private float cellSize;
        private uint frame;

        /// <summary>Creates a simulation using the compute shader and the given parameters.</summary>
        public BoidSimulation(ComputeShader boidShader, SimConfig simConfig)
        {
            shader = boidShader;
            config = simConfig;

            initKernel = shader.FindKernel("InitBoids");
            copyKernel = shader.FindKernel("CopyBoids");
            clearCountersKernel = shader.FindKernel("ClearCounters");
            clearDropsKernel = shader.FindKernel("ClearDrops");
            clearGridKernel = shader.FindKernel("ClearGrid");
            buildGridKernel = shader.FindKernel("BuildGrid");
            stepKernel = shader.FindKernel("BoidStep");
            spawnKernel = shader.FindKernel("SpawnBoids");
            killKernel = shader.FindKernel("KillBoids");
            applyReturnKernel = shader.FindKernel("ApplyReturn");
        }

        /// <summary>Number of boid slots (the most boids that can be alive).</summary>
        public int PoolSize { get; private set; }

        /// <summary>Texels per face edge of the per-texel return buffer.</summary>
        public int Resolution { get; private set; }

        /// <summary>True once buffers exist.</summary>
        public bool IsAllocated => posEnergy != null;

        /// <summary>Population counters: [0] alive, [1] born this step, [2] died this step, [3] seed drops since the last clear.</summary>
        public ComputeBuffer Counters => counters;

        /// <summary>Seed-drop events (float4: xyz ground direction, w = 1; unused entries are zero).</summary>
        public ComputeBuffer Drops => drops;

        /// <summary>Boid position (xyz) and energy (w). For drawing and tests.</summary>
        public ComputeBuffer PosEnergy => posEnergy;

        /// <summary>Boid velocity (xyz) and seed timer (w). For drawing and tests.</summary>
        public ComputeBuffer VelSeed => velSeed;

        /// <summary>Boid alive flags (0 dead, 1 alive, 2 newborn). For drawing and tests.</summary>
        public ComputeBuffer Alive => alive;

        /// <summary>
        /// Creates or resizes the buffers. If boids already exist and the pool size changes, they are carried
        /// over (as many as fit). The return buffer is emptied when the field resolution changes.
        /// </summary>
        public void Allocate(int poolSize, int resolution)
        {
            int texelCount = resolution * resolution * CubeSphere.FaceCount;

            if (returned == null || Resolution != resolution)
            {
                returned?.Release();
                returned = NewZeroedBuffer(texelCount);
            }

            if (counters == null)
            {
                counters = NewZeroedBuffer(CounterCount);
                drops = new ComputeBuffer(DropCapacity, 16);
                drops.SetData(new Vector4[DropCapacity]);
            }

            BuildGridBuffer();

            if (posEnergy == null || PoolSize != poolSize)
            {
                ComputeBuffer oldPosEnergy = posEnergy;
                ComputeBuffer oldVelSeed = velSeed;
                ComputeBuffer oldAlive = alive;
                int oldSize = PoolSize;

                next?.Release();
                posEnergy = new ComputeBuffer(poolSize, 16);
                velSeed = new ComputeBuffer(poolSize, 16);
                alive = new ComputeBuffer(poolSize, 4);
                next = new ComputeBuffer(poolSize, 4);
                PoolSize = poolSize;

                if (oldPosEnergy != null)
                {
                    shader.SetInt(PoolSizeId, poolSize);
                    shader.SetInt(OldPoolSizeId, oldSize);
                    shader.SetBuffer(copyKernel, PosEnergyId, posEnergy);
                    shader.SetBuffer(copyKernel, VelSeedId, velSeed);
                    shader.SetBuffer(copyKernel, AliveId, alive);
                    shader.SetBuffer(copyKernel, NextId, next);
                    shader.SetBuffer(copyKernel, OldPosEnergyId, oldPosEnergy);
                    shader.SetBuffer(copyKernel, OldVelSeedId, oldVelSeed);
                    shader.SetBuffer(copyKernel, OldAliveId, oldAlive);
                    shader.Dispatch(copyKernel, Groups(poolSize), 1, 1);

                    oldPosEnergy.Release();
                    oldVelSeed.Release();
                    oldAlive.Release();
                }
                else
                {
                    // Brand new pool: everything dead until SeedBoids runs.
                    alive.SetData(new uint[poolSize]);
                    posEnergy.SetData(new Vector4[poolSize]);
                    velSeed.SetData(new Vector4[poolSize]);
                    next.SetData(new uint[poolSize]);
                }
            }

            Resolution = resolution;
        }

        /// <summary>Replaces the population with the starting flocks, scattered in clusters on dry land.</summary>
        public void SeedBoids(RenderTexture terrain, int seed)
        {
            SetCommon();
            shader.SetFloat(SeedId, seed);
            shader.SetInt(StartCountId, Mathf.Min(config.boidStartCount, PoolSize));
            shader.SetInt(StartClustersId, Mathf.Max(1, config.boidStartClusters));

            shader.SetBuffer(initKernel, PosEnergyId, posEnergy);
            shader.SetBuffer(initKernel, VelSeedId, velSeed);
            shader.SetBuffer(initKernel, AliveId, alive);
            shader.SetBuffer(initKernel, NextId, next);
            shader.SetTexture(initKernel, TerrainId, terrain);
            shader.Dispatch(initKernel, Groups(PoolSize), 1, 1);
        }

        /// <summary>The bird cage: releases up to <paramref name="count"/> boids inside a circle, all heading the same way.</summary>
        /// <param name="localDirection">Centre of the circle, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius in radians.</param>
        /// <param name="count">How many boids to try to release (those that find no free slot are lost).</param>
        /// <param name="terrain">Terrain texture (read), so the flock starts at hover height over the ground.</param>
        public void SpawnBoids(Vector3 localDirection, float angularRadius, int count, RenderTexture terrain)
        {
            frame++;
            SetCommon();
            shader.SetInt(SpawnCountId, count);
            shader.SetVector(EventDirectionId, localDirection.normalized);
            shader.SetFloat(EventRadiusId, angularRadius);

            shader.SetBuffer(spawnKernel, PosEnergyId, posEnergy);
            shader.SetBuffer(spawnKernel, VelSeedId, velSeed);
            shader.SetBuffer(spawnKernel, AliveId, alive);
            shader.SetBuffer(spawnKernel, CountersId, counters);
            shader.SetTexture(spawnKernel, TerrainId, terrain);
            shader.Dispatch(spawnKernel, Groups(count), 1, 1);
        }

        /// <summary>An impact: kills every boid inside a circle; their bodies join the soil at the next step.</summary>
        public void KillBoids(Vector3 localDirection, float angularRadius)
        {
            SetCommon();
            shader.SetVector(EventDirectionId, localDirection.normalized);
            shader.SetFloat(EventRadiusId, angularRadius);

            shader.SetBuffer(killKernel, PosEnergyId, posEnergy);
            shader.SetBuffer(killKernel, AliveId, alive);
            shader.SetBuffer(killKernel, CountersId, counters);
            shader.SetBuffer(killKernel, ReturnedId, returned);
            shader.Dispatch(killKernel, Groups(PoolSize), 1, 1);
        }

        /// <summary>Empties the seed-drop events. Call right after requesting a readback of them.</summary>
        public void ClearDrops()
        {
            shader.SetInt(DropCapacityId, DropCapacity);
            shader.SetBuffer(clearDropsKernel, DropsId, drops);
            shader.SetBuffer(clearDropsKernel, CountersId, counters);
            shader.Dispatch(clearDropsKernel, Groups(DropCapacity), 1, 1);
        }

        /// <summary>Runs one simulation step of <paramref name="deltaSeconds"/> sim seconds.</summary>
        /// <param name="soil">Soil field (B canopy read; G organic matter written in place).</param>
        /// <param name="terrain">Terrain texture (read).</param>
        /// <param name="climate">Climate texture (read).</param>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(Field soil, RenderTexture terrain, RenderTexture climate, float deltaSeconds)
        {
            frame++;
            SetCommon();
            SetSteering(deltaSeconds);

            shader.SetBuffer(clearCountersKernel, CountersId, counters);
            shader.Dispatch(clearCountersKernel, 1, 1, 1);

            shader.SetBuffer(clearGridKernel, CellHeadId, cellHead);
            shader.Dispatch(clearGridKernel, Groups(cellHead.count), 1, 1);

            shader.SetBuffer(buildGridKernel, PosEnergyId, posEnergy);
            shader.SetBuffer(buildGridKernel, AliveId, alive);
            shader.SetBuffer(buildGridKernel, CellHeadId, cellHead);
            shader.SetBuffer(buildGridKernel, NextId, next);
            shader.Dispatch(buildGridKernel, Groups(PoolSize), 1, 1);

            shader.SetBuffer(stepKernel, PosEnergyId, posEnergy);
            shader.SetBuffer(stepKernel, VelSeedId, velSeed);
            shader.SetBuffer(stepKernel, AliveId, alive);
            shader.SetBuffer(stepKernel, CellHeadId, cellHead);
            shader.SetBuffer(stepKernel, NextId, next);
            shader.SetBuffer(stepKernel, CountersId, counters);
            shader.SetBuffer(stepKernel, DropsId, drops);
            shader.SetBuffer(stepKernel, ReturnedId, returned);
            shader.SetTexture(stepKernel, SoilId, soil.Current);
            shader.SetTexture(stepKernel, TerrainId, terrain);
            shader.SetTexture(stepKernel, ClimateId, climate);
            shader.Dispatch(stepKernel, Groups(PoolSize), 1, 1);

            int texelGroups = (Resolution + TexelGroupSize - 1) / TexelGroupSize;
            shader.SetBuffer(applyReturnKernel, ReturnedId, returned);
            shader.SetTexture(applyReturnKernel, SoilRWId, soil.Current);
            shader.Dispatch(applyReturnKernel, texelGroups, texelGroups, CubeSphere.FaceCount);
        }

        /// <summary>Releases every GPU buffer.</summary>
        public void Dispose()
        {
            Release(ref posEnergy);
            Release(ref velSeed);
            Release(ref alive);
            Release(ref next);
            Release(ref cellHead);
            Release(ref counters);
            Release(ref drops);
            Release(ref returned);
            PoolSize = 0;
        }

        // ------------------------------------------------------------------ internals

        private void BuildGridBuffer()
        {
            int dim = Mathf.Clamp(Mathf.CeilToInt(2f * GridExtent / Mathf.Max(config.boidPerception, 0.005f)), 4, MaxGridDim);
            if (cellHead != null && dim == gridDim)
            {
                return;
            }

            cellHead?.Release();
            gridDim = dim;
            cellSize = 2f * GridExtent / dim;
            cellHead = new ComputeBuffer(dim * dim * dim, 4);
            cellHead.SetData(new uint[dim * dim * dim]);
        }

        // Parameters every kernel needs.
        private void SetCommon()
        {
            shader.SetInt(PoolSizeId, PoolSize);
            shader.SetInt(ResolutionId, Resolution);
            shader.SetInt(FrameId, (int)frame);
            shader.SetInt(GridDimId, gridDim);
            shader.SetInt(GridCellsId, cellHead.count);
            shader.SetInt(DropCapacityId, DropCapacity);
            shader.SetFloat(GridExtentId, GridExtent);
            shader.SetFloat(CellSizeId, cellSize);
            shader.SetFloat(HoverId, config.boidHoverHeight);
            shader.SetFloat(HeightScaleId, config.terrainHeightScale);
            shader.SetFloat(MaxSpeedId, config.boidMaxSpeed);
            shader.SetFloat(StartEnergyId, config.boidStartEnergy);
            shader.SetFloat(DeathOrganicId, config.boidDeathOrganic);
        }

        // The steering and energy parameters, which only BoidStep uses.
        private void SetSteering(float deltaSeconds)
        {
            shader.SetFloat(DeltaTimeId, deltaSeconds);
            shader.SetFloat(PerceptionId, config.boidPerception);
            shader.SetFloat(SeparationRadiusId, config.boidSeparationRadius);
            shader.SetFloat(SeparationWeightId, config.boidSeparationWeight);
            shader.SetFloat(AlignmentWeightId, config.boidAlignmentWeight);
            shader.SetFloat(CohesionWeightId, config.boidCohesionWeight);
            shader.SetFloat(MinSpeedId, config.boidMinSpeed);
            shader.SetFloat(MaxForceId, config.boidMaxForce);
            shader.SetFloat(AltitudeSpringId, config.boidAltitudeSpring);
            shader.SetFloat(AltitudeDampingId, config.boidAltitudeDamping);
            shader.SetFloat(FoodWeightId, config.boidFoodWeight);
            shader.SetFloat(SenseReachId, config.boidSenseReach);
            shader.SetFloat(PreferredTempId, config.boidPreferredTemp);
            shader.SetFloat(WarmWeightId, config.boidWarmWeight);
            shader.SetFloat(WanderId, config.boidWander);

            shader.SetFloat(EatRateId, config.boidEatRate);
            shader.SetFloat(UpkeepId, config.boidUpkeep);
            shader.SetFloat(FlightCostId, config.boidFlightCost);
            shader.SetFloat(CrowdCostId, config.boidCrowdCost);
            shader.SetFloat(ColdLimitId, config.boidColdLimit);
            shader.SetFloat(ColdCostId, config.boidColdCost);
            shader.SetFloat(ReproduceEnergyId, config.boidReproduceEnergy);
            shader.SetFloat(BirthCostId, config.boidBirthCost);
            shader.SetInt(BreedCrowdLimitId, config.boidBreedCrowdLimit);
            shader.SetFloat(SeedPickupRateId, config.boidSeedPickupRate);
            shader.SetFloat(SeedMinCanopyId, config.boidSeedMinCanopy);
            shader.SetFloat(SeedCarryId, config.boidSeedCarrySeconds);
            shader.SetFloat(SeedCooldownId, config.boidSeedCooldown);
        }

        private static int Groups(int count)
        {
            return Mathf.Max(1, (count + GroupSize - 1) / GroupSize);
        }

        private static ComputeBuffer NewZeroedBuffer(int count)
        {
            var buffer = new ComputeBuffer(count, 4);
            buffer.SetData(new uint[count]);
            return buffer;
        }

        private static void Release(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        // Shader property ids, looked up once so a step allocates nothing.
        private static readonly int PosEnergyId = Shader.PropertyToID("_PosEnergy");
        private static readonly int VelSeedId = Shader.PropertyToID("_VelSeed");
        private static readonly int AliveId = Shader.PropertyToID("_Alive");
        private static readonly int OldPosEnergyId = Shader.PropertyToID("_OldPosEnergy");
        private static readonly int OldVelSeedId = Shader.PropertyToID("_OldVelSeed");
        private static readonly int OldAliveId = Shader.PropertyToID("_OldAlive");
        private static readonly int CellHeadId = Shader.PropertyToID("_CellHead");
        private static readonly int NextId = Shader.PropertyToID("_Next");
        private static readonly int CountersId = Shader.PropertyToID("_Counters");
        private static readonly int DropsId = Shader.PropertyToID("_Drops");
        private static readonly int ReturnedId = Shader.PropertyToID("_Returned");
        private static readonly int SoilId = Shader.PropertyToID("_Soil");
        private static readonly int SoilRWId = Shader.PropertyToID("_SoilRW");
        private static readonly int TerrainId = Shader.PropertyToID("_Terrain");
        private static readonly int ClimateId = Shader.PropertyToID("_Climate");
        private static readonly int PoolSizeId = Shader.PropertyToID("_PoolSize");
        private static readonly int OldPoolSizeId = Shader.PropertyToID("_OldPoolSize");
        private static readonly int StartCountId = Shader.PropertyToID("_StartCount");
        private static readonly int StartClustersId = Shader.PropertyToID("_StartClusters");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int GridDimId = Shader.PropertyToID("_GridDim");
        private static readonly int GridCellsId = Shader.PropertyToID("_GridCells");
        private static readonly int DropCapacityId = Shader.PropertyToID("_DropCapacity");
        private static readonly int GridExtentId = Shader.PropertyToID("_GridExtent");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int PerceptionId = Shader.PropertyToID("_Perception");
        private static readonly int SeparationRadiusId = Shader.PropertyToID("_SeparationRadius");
        private static readonly int SeparationWeightId = Shader.PropertyToID("_SeparationWeight");
        private static readonly int AlignmentWeightId = Shader.PropertyToID("_AlignmentWeight");
        private static readonly int CohesionWeightId = Shader.PropertyToID("_CohesionWeight");
        private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
        private static readonly int MinSpeedId = Shader.PropertyToID("_MinSpeed");
        private static readonly int MaxForceId = Shader.PropertyToID("_MaxForce");
        private static readonly int HoverId = Shader.PropertyToID("_Hover");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int AltitudeSpringId = Shader.PropertyToID("_AltitudeSpring");
        private static readonly int AltitudeDampingId = Shader.PropertyToID("_AltitudeDamping");
        private static readonly int FoodWeightId = Shader.PropertyToID("_FoodWeight");
        private static readonly int SenseReachId = Shader.PropertyToID("_SenseReach");
        private static readonly int PreferredTempId = Shader.PropertyToID("_PreferredTemp");
        private static readonly int WarmWeightId = Shader.PropertyToID("_WarmWeight");
        private static readonly int WanderId = Shader.PropertyToID("_Wander");
        private static readonly int EatRateId = Shader.PropertyToID("_EatRate");
        private static readonly int UpkeepId = Shader.PropertyToID("_Upkeep");
        private static readonly int FlightCostId = Shader.PropertyToID("_FlightCost");
        private static readonly int CrowdCostId = Shader.PropertyToID("_CrowdCost");
        private static readonly int ColdLimitId = Shader.PropertyToID("_ColdLimit");
        private static readonly int ColdCostId = Shader.PropertyToID("_ColdCost");
        private static readonly int StartEnergyId = Shader.PropertyToID("_StartEnergy");
        private static readonly int ReproduceEnergyId = Shader.PropertyToID("_ReproduceEnergy");
        private static readonly int BirthCostId = Shader.PropertyToID("_BirthCost");
        private static readonly int BreedCrowdLimitId = Shader.PropertyToID("_BreedCrowdLimit");
        private static readonly int SeedPickupRateId = Shader.PropertyToID("_SeedPickupRate");
        private static readonly int SeedMinCanopyId = Shader.PropertyToID("_SeedMinCanopy");
        private static readonly int SeedCarryId = Shader.PropertyToID("_SeedCarry");
        private static readonly int SeedCooldownId = Shader.PropertyToID("_SeedCooldown");
        private static readonly int DeathOrganicId = Shader.PropertyToID("_DeathOrganic");
        private static readonly int SpawnCountId = Shader.PropertyToID("_SpawnCount");
        private static readonly int EventDirectionId = Shader.PropertyToID("_EventDirection");
        private static readonly int EventRadiusId = Shader.PropertyToID("_EventRadius");
    }
}
