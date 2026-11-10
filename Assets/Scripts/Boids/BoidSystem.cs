// =============================================================================
// BoidSystem
// -----------------------------------------------------------------------------
// Purpose : Puts the boids into the scene: creates the simulation when needed,
//           seeds the starting flocks for each new world, steps it when SimLoop
//           asks, draws the flocks, and hands carried seeds to the trees.
// Idea    : Lazy setup, like PhysarumSystem. The simulation is created (and re-sized
//           after a quality preset change) at the first step that needs it. A new
//           world only sets a flag; the flocks are scattered on the NEW land at the
//           next step, when the terrain exists.
// Readback: a few times a second two small buffers are requested with
//           AsyncGPUReadback: the population counters (for the debug panel) and the
//           seed-drop events. The events are cleared on the GPU right after the
//           request, so none is read twice and none is lost (the request sees the
//           buffer as it was when it was issued). Each event is a ground direction;
//           it goes to TreeManager.ReceiveSeed, which decides whether the seed
//           germinates there. This is the only way boids affect trees.
// Drawing : one DrawMeshInstancedProcedural call for the whole pool; the vertex
//           shader reads the simulation buffers directly (see Boids.shader).
// =============================================================================

using Terrarium.Sphere;
using Terrarium.Trees;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Boids
{
    /// <summary>Scene glue for the boid simulation.</summary>
    public sealed class BoidSystem : MonoBehaviour
    {
        [Tooltip("The simulation: config, preset and seed.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("Boids.compute.")]
        [SerializeField] private ComputeShader boidShader;

        [Tooltip("The planet. Boid positions are in its local space.")]
        [SerializeField] private Transform planet;

        [Tooltip("Where carried seeds are dropped (optional).")]
        [SerializeField] private TreeManager trees;

        [Tooltip("Material using Terrarium/Boids.")]
        [SerializeField] private Material boidMaterial;

        private BoidSimulation simulation;
        private Mesh birdMesh;
        private MaterialPropertyBlock block;
        private bool needsSeeding;
        private float readbackTimer;
        private bool countersPending;
        private bool dropsPending;
        private int dropsDelivered;

        /// <summary>Boids alive at the last readback.</summary>
        public uint AliveCount { get; private set; }

        /// <summary>Boids born in the step before the last readback.</summary>
        public uint BornLastStep { get; private set; }

        /// <summary>Boids that died in the step before the last readback.</summary>
        public uint DiedLastStep { get; private set; }

        /// <summary>Seeds boids have dropped since the world began (delivered to the trees).</summary>
        public int SeedsDropped => dropsDelivered;

        /// <summary>The most boids the pool can hold (from the quality preset).</summary>
        public int PoolSize => simulation != null ? simulation.PoolSize : 0;

        /// <summary>The running simulation (null until the first step). For the editor's DevBridge and tests.</summary>
        public BoidSimulation Simulation => simulation;

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || boidShader == null || planet == null || boidMaterial == null)
            {
                Debug.LogError("BoidSystem is missing a reference.", this);
                enabled = false;
                return;
            }

            block = new MaterialPropertyBlock();
            birdMesh = BuildBirdMesh();
            fieldSet.WorldReset += HandleWorldReset;
        }

        private void OnDestroy()
        {
            if (fieldSet != null)
            {
                fieldSet.WorldReset -= HandleWorldReset;
            }

            if (birdMesh != null)
            {
                Destroy(birdMesh);
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
            dropsDelivered = 0;
        }

        private void Update()
        {
            if (simulation == null || !simulation.IsAllocated)
            {
                return;
            }

            readbackTimer -= Time.unscaledDeltaTime;
            if (readbackTimer <= 0f && !countersPending && !dropsPending)
            {
                readbackTimer = simRoot.Config.boidReadbackRealSeconds;
                countersPending = true;
                dropsPending = true;
                AsyncGPUReadback.Request(simulation.Counters, OnCountersRead);
                AsyncGPUReadback.Request(simulation.Drops, OnDropsRead);

                // The requests above see the buffers as they are now; clearing the events now loses none.
                simulation.ClearDrops();
            }
        }

        private void LateUpdate()
        {
            if (simulation == null || !simulation.IsAllocated || fieldSet.Resolution == 0)
            {
                return;
            }

            SimConfig config = simRoot.Config;
            block.SetBuffer("_BoidPosEnergy", simulation.PosEnergy);
            block.SetBuffer("_BoidVelSeed", simulation.VelSeed);
            block.SetBuffer("_BoidAlive", simulation.Alive);
            block.SetMatrix("_PlanetLocalToWorld", planet.localToWorldMatrix);
            block.SetFloat("_BoidSize", config.boidSize);
            block.SetFloat("_BoidMinPixels", config.boidMinPixels);
            block.SetFloat("_BoidFlapRate", config.boidFlapRate);

            Graphics.DrawMeshInstancedProcedural(birdMesh, 0, boidMaterial, new Bounds(planet.position, Vector3.one * 8f),
                simulation.PoolSize, block, ShadowCastingMode.Off, false);
        }

        private void OnCountersRead(AsyncGPUReadbackRequest request)
        {
            countersPending = false;
            if (request.hasError || simulation == null)
            {
                return;
            }

            NativeArray<uint> counters = request.GetData<uint>();
            AliveCount = counters[0];
            BornLastStep = counters[1];
            DiedLastStep = counters[2];
        }

        private void OnDropsRead(AsyncGPUReadbackRequest request)
        {
            dropsPending = false;
            if (request.hasError || simulation == null)
            {
                return;
            }

            NativeArray<Vector4> events = request.GetData<Vector4>();
            for (int i = 0; i < events.Length; i++)
            {
                Vector4 drop = events[i];
                if (drop.w > 0.5f)
                {
                    dropsDelivered++;
                    if (trees != null)
                    {
                        trees.ReceiveSeed(new Vector3(drop.x, drop.y, drop.z));
                    }
                }
            }
        }

        /// <summary>Runs one simulation step. Called by SimLoop.</summary>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(float deltaSeconds)
        {
            SimConfig config = simRoot.Config;
            QualityPreset preset = simRoot.ActivePreset;

            if (fieldSet.Resolution == 0)
            {
                return;
            }

            if (simulation == null)
            {
                simulation = new BoidSimulation(boidShader, config);
                needsSeeding = true;
            }

            if (!simulation.IsAllocated || simulation.PoolSize != preset.boidCount || simulation.Resolution != fieldSet.Resolution)
            {
                simulation.Allocate(preset.boidCount, fieldSet.Resolution);
            }

            Field terrain = fieldSet.Get(FieldId.Terrain);

            if (needsSeeding)
            {
                simulation.SeedBoids(terrain.Current, simRoot.ActiveSeed);
                needsSeeding = false;
            }

            simulation.Step(fieldSet.Get(FieldId.Soil), terrain.Current, fieldSet.Get(FieldId.Climate).Current, deltaSeconds);
        }

        /// <summary>The bird cage: releases a flock at a point on the planet.</summary>
        /// <param name="localDirection">Centre, a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Radius in radians.</param>
        /// <param name="count">How many boids to try to release.</param>
        public void ReleaseFlock(Vector3 localDirection, float angularRadius, int count)
        {
            if (simulation != null && simulation.IsAllocated)
            {
                simulation.SpawnBoids(localDirection, angularRadius, count, fieldSet.Get(FieldId.Terrain).Current);
            }
        }

        /// <summary>An impact: removes every boid inside a circle on the planet.</summary>
        public void KillInRadius(Vector3 localDirection, float angularRadius)
        {
            if (simulation != null && simulation.IsAllocated)
            {
                simulation.KillBoids(localDirection, angularRadius);
            }
        }

        /// <summary>Immediate-mode readout, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            GUILayout.Label($"Boids alive: {AliveCount:N0} of {PoolSize:N0}");
            GUILayout.Label($"  born {BornLastStep:N0}, died {DiedLastStep:N0} in the last step");
            GUILayout.Label($"  seeds dropped so far: {dropsDelivered:N0}");
            if (GUILayout.Button("Re-scatter boids"))
            {
                needsSeeding = true;
            }
        }

        // A bird made of two triangles, a wing each, joined along the body. In mesh space x is the wing axis
        // (+-1 at the wing tips), y is up and z is forward. Each triangle has its own vertices because the
        // uv holds the corner's barycentric coordinates (used for the ink outline).
        private static Mesh BuildBirdMesh()
        {
            var nose = new Vector3(0f, 0f, 0.6f);
            var tail = new Vector3(0f, 0f, -0.4f);
            var leftTip = new Vector3(-1f, 0f, -0.15f);
            var rightTip = new Vector3(1f, 0f, -0.15f);

            var mesh = new Mesh { name = "Bird" };
            mesh.vertices = new[] { nose, leftTip, tail, nose, tail, rightTip };
            mesh.uv = new[]
            {
                new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f),
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return mesh;
        }
    }
}
