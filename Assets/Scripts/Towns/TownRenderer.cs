// =============================================================================
// TownRenderer
// -----------------------------------------------------------------------------
// Purpose : Puts the settlements on screen: the houses of every town, and the people
//           walking between them.
// Idea    : Both are drawn by the GPU straight from simulation data, with no CPU copy.
//             Houses  Houses.compute keeps a remembered state for every possible spot and, a couple
//                     of times a second, moves each one by the rules (water to hand, weather the
//                     style can stand, not on a farm, people who need a home; see Houses.compute).
//                     The standing houses are appended to a list and drawn with one indirect instanced
//                     call, whose instance count is copied from the list's own counter. The state
//                     moves by SIM time, so a paused world builds nothing and a fast one builds quickly.
//             People  the people simulation's own buffers (position, heading, alive) are the
//                     instance data, so one instanced call draws the whole pool; the shader
//                     skips the dead and the far away.
// Readback: the house count is read back asynchronously a couple of times a second, for the
//           console and the stability check. Nothing ever waits for the GPU.
// Parameters: houseCandidates, houseThreshold, houseSize, houseMinPixels, personSize,
//           personVisibleDistance (SimConfig "Towns and farms").
// =============================================================================

using Terrarium.Physarum;
using Terrarium.Sphere;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Towns
{
    /// <summary>
    /// Draws the houses and people of the planet's towns. It runs after the planet has been turned for the frame
    /// (PlanetMotion.LateUpdate), so the matrix it reads is this frame's: read earlier, everything on the planet
    /// would lag a frame behind the ground, which at fast time is many house-widths.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class TownRenderer : MonoBehaviour
    {
        private const float RebuildIntervalSeconds = 0.5f;
        private const int ThreadGroupSize = 64;
        private const float MaxSimSecondsPerRebuild = 40f;

        [Tooltip("The simulation: config and preset.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("The people simulation, whose buffers are drawn.")]
        [SerializeField] private PhysarumSystem people;

        [Tooltip("The planet. Positions are in its local space.")]
        [SerializeField] private Transform planet;

        [Tooltip("Houses.compute.")]
        [SerializeField] private ComputeShader houseShader;

        [Tooltip("Material using Terrarium/Houses.")]
        [SerializeField] private Material houseMaterial;

        [Tooltip("Material using Terrarium/People.")]
        [SerializeField] private Material personMaterial;

        private Mesh houseMesh;
        private Mesh personMesh;
        private HouseSimulation simulation;
        private MaterialPropertyBlock houseBlock;
        private MaterialPropertyBlock personBlock;
        private double lastSimTime = double.MaxValue;
        private float timer;
        private bool readbackPending;

        /// <summary>Houses standing at the last readback.</summary>
        public int HouseCount { get; private set; }

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || people == null || planet == null || houseShader == null || houseMaterial == null || personMaterial == null)
            {
                Debug.LogError("TownRenderer is missing a reference.", this);
                enabled = false;
                return;
            }

            fieldSet.WorldReset += HandleWorldReset;
            houseBlock = new MaterialPropertyBlock();
            personBlock = new MaterialPropertyBlock();
            houseMesh = TownMeshes.BuildHouse();
            personMesh = TownMeshes.BuildPerson();

            simulation = new HouseSimulation(houseShader, simRoot.Config, houseMesh.GetIndexCount(0));
        }

        private void HandleWorldReset()
        {
            simulation?.Reset();
        }

        private void OnDestroy()
        {
            if (fieldSet != null)
            {
                fieldSet.WorldReset -= HandleWorldReset;
            }

            simulation?.Dispose();
            if (houseMesh != null)
            {
                Destroy(houseMesh);
            }

            if (personMesh != null)
            {
                Destroy(personMesh);
            }
        }

        private void Update()
        {
            if (fieldSet.Resolution == 0)
            {
                return;
            }

            timer -= Time.unscaledDeltaTime;
            if (timer > 0f)
            {
                return;
            }

            timer = RebuildIntervalSeconds;
            RebuildHouses();

            if (!readbackPending)
            {
                readbackPending = true;
                AsyncGPUReadback.Request(simulation.Arguments, OnArgumentsRead);
            }
        }

        private void LateUpdate()
        {
            if (fieldSet.Resolution == 0)
            {
                return;
            }

            SimConfig config = simRoot.Config;
            var bounds = new Bounds(planet.position, Vector3.one * 8f);

            if (simulation.Houses == null)
            {
                return;   // the first update has not run yet
            }

            houseBlock.SetBuffer("_Houses", simulation.Houses);
            houseBlock.SetMatrix("_PlanetLocalToWorld", planet.localToWorldMatrix);
            houseBlock.SetFloat("_HouseMinPixels", config.houseMinPixels);
            Graphics.DrawMeshInstancedIndirect(houseMesh, 0, houseMaterial, bounds, simulation.Arguments, 0, houseBlock, ShadowCastingMode.Off, false);

            PhysarumSimulation peopleSimulation = people.Simulation;
            if (peopleSimulation != null && peopleSimulation.IsAllocated)
            {
                personBlock.SetBuffer("_PersonPosEnergy", peopleSimulation.PosEnergy);
                personBlock.SetBuffer("_PersonHeading", peopleSimulation.Heading);
                personBlock.SetBuffer("_PersonAlive", peopleSimulation.Alive);
                personBlock.SetMatrix("_PlanetLocalToWorld", planet.localToWorldMatrix);
                personBlock.SetFloat("_PersonSize", config.personSize);
                personBlock.SetFloat("_PersonVisibleDistance", config.personVisibleDistance);
                Graphics.DrawMeshInstancedProcedural(personMesh, 0, personMaterial, bounds, peopleSimulation.PoolSize, personBlock, ShadowCastingMode.Off, false);
            }
        }

        // Moves the houses' remembered state by the sim time that has passed, and rebuilds the draw list.
        private void RebuildHouses()
        {
            // Sim time since the last rebuild (zero while paused, and across a new world).
            double now = simRoot.Clock.SimTimeSeconds;
            float deltaSeconds = now < lastSimTime ? 0f : Mathf.Min((float)(now - lastSimTime), MaxSimSecondsPerRebuild);
            lastSimTime = now;

            simulation.Update(people.Fields, people.Rules, fieldSet.Resolution, deltaSeconds);
        }
        private void OnArgumentsRead(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (request.hasError || simulation == null)
            {
                return;
            }

            NativeArray<uint> data = request.GetData<uint>();
            HouseCount = (int)data[1];
        }
    }
}
