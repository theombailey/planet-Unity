// =============================================================================
// StatsSystem
// -----------------------------------------------------------------------------
// Purpose : Keeps an up-to-date EcosystemStats: a few times a second it asks the
//           GPU (Stats.compute) to add up the whole world, reads the answer back
//           without waiting, and combines it with the systems' population counts.
// Idea    : The GPU does the heavy summing over hundreds of thousands of texels and
//           boids; the CPU only ever receives a dozen numbers. The readback is
//           asynchronous, so the numbers are a few frames old, which does not matter
//           for a HUD, a stability check or a slowly moving soundscape.
// Equations: mean = total / (scale * count), see Stats.compute for the fixed-point scales.
// =============================================================================

using Terrarium.Boids;
using Terrarium.Physarum;
using Terrarium.Sphere;
using Terrarium.Trees;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium
{
    /// <summary>Maintains the latest <see cref="EcosystemStats"/> from GPU reductions and population counters.</summary>
    public sealed class StatsSystem : MonoBehaviour
    {
        private const float FieldScale = 128f;
        private const float TempScale = 16f;
        private const float SpeedScale = 10000f;
        private const float EnergyScale = 100f;
        private const int SlotCount = 16;

        [Tooltip("The simulation (clock and config).")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("Stats.compute.")]
        [SerializeField] private ComputeShader statsShader;

        [Tooltip("Boids to count (optional).")]
        [SerializeField] private BoidSystem boids;

        [Tooltip("The houses to count (optional).")]
        [SerializeField] private Terrarium.Towns.TownRenderer towns;

        [Tooltip("The people to count (optional).")]
        [SerializeField] private PhysarumSystem physarum;

        [Tooltip("Trees to count (optional).")]
        [SerializeField] private TreeManager trees;

        [Tooltip("Real seconds between refreshes.")]
        [SerializeField] private float refreshSeconds = 0.25f;

        private ComputeBuffer accumulator;
        private float timer;
        private bool pending;
        private EcosystemStats latest;

        /// <summary>The newest snapshot. Check <see cref="EcosystemStats.Valid"/> before trusting it.</summary>
        public EcosystemStats Latest => latest;

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || statsShader == null)
            {
                Debug.LogError("StatsSystem is missing a reference.", this);
                enabled = false;
                return;
            }

            accumulator = new ComputeBuffer(SlotCount, 4);
            accumulator.SetData(new uint[SlotCount]);
        }

        private void OnDestroy()
        {
            if (accumulator != null)
            {
                accumulator.Release();
                accumulator = null;
            }
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f || pending || fieldSet.Resolution == 0)
            {
                return;
            }

            timer = refreshSeconds;
            Request();
        }

        private void Request()
        {
            int resolution = fieldSet.Resolution;
            int groups = (resolution + 7) / 8;

            int clear = statsShader.FindKernel("ClearStats");
            int fields = statsShader.FindKernel("ReduceFields");
            int boidKernel = statsShader.FindKernel("ReduceBoids");

            statsShader.SetInt("_Resolution", resolution);

            statsShader.SetBuffer(clear, "_StatsAcc", accumulator);
            statsShader.Dispatch(clear, 1, 1, 1);

            statsShader.SetBuffer(fields, "_StatsAcc", accumulator);
            statsShader.SetTexture(fields, "_Soil", fieldSet.Get(FieldId.Soil).Current);
            statsShader.SetTexture(fields, "_Climate", fieldSet.Get(FieldId.Climate).Current);
            statsShader.SetTexture(fields, "_Terrain", fieldSet.Get(FieldId.Terrain).Current);
            statsShader.SetTexture(fields, "_Trail", fieldSet.Get(FieldId.Trail).Current);
            statsShader.SetTexture(fields, "_Land", fieldSet.Get(FieldId.Land).Current);
            statsShader.Dispatch(fields, groups, groups, CubeSphere.FaceCount);

            BoidSimulation boidSimulation = boids != null ? boids.Simulation : null;
            if (boidSimulation != null && boidSimulation.IsAllocated)
            {
                statsShader.SetInt("_PoolSize", boidSimulation.PoolSize);
                statsShader.SetBuffer(boidKernel, "_StatsAcc", accumulator);
                statsShader.SetBuffer(boidKernel, "_PosEnergy", boidSimulation.PosEnergy);
                statsShader.SetBuffer(boidKernel, "_VelSeed", boidSimulation.VelSeed);
                statsShader.SetBuffer(boidKernel, "_Alive", boidSimulation.Alive);
                statsShader.Dispatch(boidKernel, (boidSimulation.PoolSize + 63) / 64, 1, 1);
            }

            pending = true;
            AsyncGPUReadback.Request(accumulator, OnRead);
        }

        private void OnRead(AsyncGPUReadbackRequest request)
        {
            pending = false;
            if (request.hasError || accumulator == null)
            {
                return;
            }

            NativeArray<uint> acc = request.GetData<uint>();
            float texels = Mathf.Max(1, fieldSet.Resolution * fieldSet.Resolution * CubeSphere.FaceCount);
            SimClock clock = simRoot.Clock;

            var stats = new EcosystemStats
            {
                Year = clock.YearIndex,
                YearPhase = clock.YearPhase,
                MeanNutrients = acc[0] / (FieldScale * texels),
                MeanOrganic = acc[1] / (FieldScale * texels),
                MeanCanopy = acc[2] / (FieldScale * texels),
                MeanTemperature = acc[3] / (TempScale * texels) - 100f,
                MeanRoad = acc[4] / (FieldScale * texels),
                LandFraction = acc[5] / texels,
                FarmFraction = acc[5] > 0 ? acc[6] / (FieldScale * acc[5]) : 0f,
                TownFraction = acc[5] > 0 ? (float)acc[7] / acc[5] : 0f,
                BoidsAlive = (int)acc[8],
                BoidMeanSpeed = acc[8] > 0 ? acc[9] / (SpeedScale * acc[8]) : 0f,
                BoidMeanEnergy = acc[8] > 0 ? acc[10] / (EnergyScale * acc[8]) : 0f,
                BoidsCarryingSeeds = (int)acc[11],
                Valid = true,
            };

            if (physarum != null)
            {
                stats.PeopleAlive = (int)physarum.AliveCount;
            }

            if (towns != null)
            {
                stats.Houses = towns.HouseCount;
            }

            if (boids != null)
            {
                stats.BoidsBorn = (int)boids.BornLastStep;
                stats.BoidsDied = (int)boids.DiedLastStep;
            }

            if (trees != null)
            {
                stats.TreesStanding = trees.TreeCount;
                stats.TreesAlive = trees.LiveCount;
                stats.MeanLeafCover = trees.MeanLeafCover;
            }

            latest = stats;
        }

        /// <summary>Immediate-mode readout, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            if (!latest.Valid)
            {
                GUILayout.Label("Waiting for the first reading ...");
                return;
            }

            GUILayout.Label($"Year {latest.Year}, {latest.YearPhase:P0} through   land {latest.LandFraction:P0}");
            GUILayout.Label($"  temperature {latest.MeanTemperature:0.0} C   nutrients {latest.MeanNutrients:0.000}   organic {latest.MeanOrganic:0.000}");
            GUILayout.Label($"  canopy {latest.MeanCanopy:0.000}   roads {latest.MeanRoad:0.000}   farms {latest.FarmFraction:P1} of land   towns {latest.TownFraction:P1}");
            GUILayout.Label($"  life: {latest.PeopleAlive:N0} people, {latest.TreesAlive} trees, {latest.BoidsAlive:N0} birds");
            GUILayout.Label($"  birds: speed {latest.BoidMeanSpeed:0.000}, energy {latest.BoidMeanEnergy:0.0}, carrying {latest.BoidsCarryingSeeds}");
        }
    }
}
