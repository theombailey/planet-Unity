// =============================================================================
// PhysarumSystem
// -----------------------------------------------------------------------------
// Purpose : Puts the people (the physarum walkers who make roads, towns and farms)
//           into the scene: creates the simulation when needed, seeds the people
//           for each new world, steps it when SimLoop asks, and reports the living
//           population to the debug panel.
// Idea    : Lazy setup. The simulation is created (and re-sized after a quality
//           preset change) at the first step that needs it, so it never has to
//           guess whether the fields exist yet. A new world (FieldSet.WorldReset)
//           only sets a flag; the people are scattered on the NEW land at the
//           next step, by which time the terrain and climate have been generated.
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
        private const float PioneerBatch = 4f;

        [Tooltip("The simulation: config, preset and seed.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("Physarum.compute.")]
        [SerializeField] private ComputeShader physarumShader;

        [Tooltip("Settlement.compute.")]
        [SerializeField] private ComputeShader settlementShader;

        [Tooltip("Nutrients.compute.")]
        [SerializeField] private ComputeShader nutrientShader;

        private PhysarumSimulation simulation;
        private bool needsSeeding;
        private float pioneerBacklog;
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

        /// <summary>The running simulation (null until the first step). For drawing the people, the DevBridge and tests.</summary>
        public PhysarumSimulation Simulation => simulation;

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || physarumShader == null || settlementShader == null || nutrientShader == null)
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
                simulation = new PhysarumSimulation(physarumShader, settlementShader, nutrientShader, config);
                needsSeeding = true;
            }

            if (!simulation.IsAllocated || simulation.PoolSize != preset.physarumAgents || simulation.Resolution != fieldSet.Resolution)
            {
                simulation.Allocate(preset.physarumAgents, fieldSet.Resolution);
            }

            Field terrain = fieldSet.Get(FieldId.Terrain);
            Field land = fieldSet.Get(FieldId.Land);
            Field soil = fieldSet.Get(FieldId.Soil);
            Field trail = fieldSet.Get(FieldId.Trail);
            RenderTexture climate = fieldSet.Get(FieldId.Climate).Current;

            if (needsSeeding)
            {
                simulation.SeedAgents(land, soil, terrain.Current, climate, simRoot.ActiveSeed, config.physarumStartAgents);
                pioneerBacklog = 0f;
                needsSeeding = false;
            }

            // Pioneers: a faint background supply of new people who each settle the first good, empty place they
            // find, so a continent nobody reached (or a world whose people died out) can still be settled.
            // Landed in batches to keep the number of dispatches low.
            pioneerBacklog += config.peopleArrivalRate * deltaSeconds;
            if (pioneerBacklog >= PioneerBatch)
            {
                int count = (int)pioneerBacklog;
                pioneerBacklog -= count;
                simulation.SpawnPioneers(land, soil, terrain.Current, climate, count);
            }

            simulation.Step(trail, land, soil, terrain.Current, climate, deltaSeconds);
        }
        /// <summary>The settlers' jar: starts people at random points inside a circle on the planet.</summary>
        /// <param name="localDirection">Centre, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius in radians.</param>
        /// <param name="count">How many people to try to start.</param>
        public void SpawnAgents(Vector3 localDirection, float angularRadius, int count)
        {
            if (simulation != null && simulation.IsAllocated && fieldSet.Resolution > 0)
            {
                simulation.SpawnAgents(
                    fieldSet.Get(FieldId.Land),
                    fieldSet.Get(FieldId.Soil),
                    fieldSet.Get(FieldId.Terrain).Current,
                    fieldSet.Get(FieldId.Climate).Current,
                    localDirection,
                    angularRadius,
                    count);
            }
        }

        /// <summary>An impact: removes every person inside a circle on the planet.</summary>
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
            GUILayout.Label($"People alive: {AliveCount:N0} of {PoolSize:N0}");
            GUILayout.Label($"  born {BornLastStep:N0}, died {DiedLastStep:N0} in the last step");
            if (GUILayout.Button("Re-scatter people"))
            {
                needsSeeding = true;
            }
        }
    }
}
