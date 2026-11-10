// =============================================================================
// PhysarumSystem
// -----------------------------------------------------------------------------
// Purpose : Puts the physarum simulation into the scene: creates it when
//           needed, seeds the agents for each new world, steps it when SimLoop
//           asks, and reports the living population to the debug panel.
// Idea    : Lazy setup. The simulation is created (and re-sized after a quality
//           preset change) at the first step that needs it, so it never has to
//           guess whether the fields exist yet. A new world (FieldSet.WorldReset)
//           only sets a flag; the agents are scattered on the NEW land at the
//           next step, by which time the terrain has been generated.
// Population readback: every quarter second the counters buffer is requested
//           with AsyncGPUReadback; the answer arrives a few frames later and is
//           stored for the panel. Nothing ever waits for the GPU.
// =============================================================================

using Terrarium.Sphere;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Physarum
{
    /// <summary>Scene glue for the physarum simulation.</summary>
    public sealed class PhysarumSystem : MonoBehaviour
    {
        private const float ReadbackIntervalSeconds = 0.25f;

        [Tooltip("The simulation: config, preset and seed.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("Physarum.compute.")]
        [SerializeField] private ComputeShader physarumShader;

        [Tooltip("Nutrients.compute.")]
        [SerializeField] private ComputeShader nutrientShader;

        private PhysarumSimulation simulation;
        private bool needsSeeding;
        private float readbackTimer;
        private bool readbackPending;

        /// <summary>Agents alive at the last readback.</summary>
        public uint AliveCount { get; private set; }

        /// <summary>Agents born in the step before the last readback.</summary>
        public uint BornLastStep { get; private set; }

        /// <summary>Agents that died in the step before the last readback.</summary>
        public uint DiedLastStep { get; private set; }

        /// <summary>The most agents the pool can hold (from the quality preset).</summary>
        public int PoolSize => simulation != null ? simulation.PoolSize : 0;

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || physarumShader == null || nutrientShader == null)
            {
                Debug.LogError("PhysarumSystem is missing a reference.", this);
                enabled = false;
                return;
            }

            fieldSet.WorldReset += HandleWorldReset;
        }

        private void OnDestroy()
        {
            if (fieldSet != null)
            {
                fieldSet.WorldReset -= HandleWorldReset;
            }
        }

        private void OnDisable()
        {
            if (simulation != null)
            {
                simulation.Dispose();
                simulation = null;
            }
        }

        private void HandleWorldReset()
        {
            needsSeeding = true;
        }

        private void Update()
        {
            if (simulation == null || !simulation.IsAllocated)
            {
                return;
            }

            readbackTimer -= Time.unscaledDeltaTime;
            if (readbackTimer <= 0f && !readbackPending)
            {
                readbackTimer = ReadbackIntervalSeconds;
                readbackPending = true;
                AsyncGPUReadback.Request(simulation.Counters, OnCountersRead);
            }
        }

        private void OnCountersRead(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (request.hasError || simulation == null)
            {
                return;
            }

            NativeArray<uint> counters = request.GetData<uint>();
            AliveCount = counters[0];
            BornLastStep = counters[1];
            DiedLastStep = counters[2];
        }

        /// <summary>Runs one simulation step. Called by SimLoop.</summary>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(float deltaSeconds)
        {
            SimConfig config = simRoot.Config;
            QualityPreset preset = simRoot.ActivePreset;

            if (simulation == null)
            {
                simulation = new PhysarumSimulation(physarumShader, nutrientShader, config);
                needsSeeding = true;
            }

            if (!simulation.IsAllocated || simulation.PoolSize != preset.physarumAgents || simulation.Resolution != fieldSet.Resolution)
            {
                simulation.Allocate(preset.physarumAgents, fieldSet.Resolution);
            }

            Field terrain = fieldSet.Get(FieldId.Terrain);

            if (needsSeeding)
            {
                simulation.SeedAgents(terrain.Current, simRoot.ActiveSeed, config.physarumStartAgents);
                needsSeeding = false;
            }

            simulation.Step(
                fieldSet.Get(FieldId.Trail),
                fieldSet.Get(FieldId.Soil),
                terrain.Current,
                fieldSet.Get(FieldId.Climate).Current,
                deltaSeconds);
        }

        /// <summary>The spore dish: starts agents at random points inside a circle on the planet.</summary>
        /// <param name="localDirection">Centre, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius in radians.</param>
        /// <param name="count">How many agents to try to start.</param>
        public void SpawnAgents(Vector3 localDirection, float angularRadius, int count)
        {
            if (simulation != null && simulation.IsAllocated)
            {
                simulation.SpawnAgents(localDirection, angularRadius, count);
            }
        }

        /// <summary>An impact: removes every agent inside a circle on the planet.</summary>
        public void KillAgents(Vector3 localDirection, float angularRadius)
        {
            if (simulation != null && simulation.IsAllocated)
            {
                simulation.KillAgents(localDirection, angularRadius);
            }
        }

        /// <summary>Immediate-mode readout, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            GUILayout.Label($"Physarum agents alive: {AliveCount:N0} of {PoolSize:N0}");
            GUILayout.Label($"  born {BornLastStep:N0}, died {DiedLastStep:N0} in the last step");
            if (GUILayout.Button("Re-scatter agents"))
            {
                needsSeeding = true;
            }
        }
    }
}
