// =============================================================================
// TreeManager
// -----------------------------------------------------------------------------
// Purpose : Runs the whole forest: plants trees, lets each one sense the world
//           and grow, drops and germinates seeds, kills and removes trees, takes
//           nutrients from the soil, paints canopy shade, and draws the branches
//           and leaves.
// Idea    : Trees are CPU objects (a branch graph does not suit the GPU) living in
//           GPU fields. The two sides talk through a small "tick", a few times a
//           second in real time, never every frame:
//
//             CPU                         GPU                           CPU
//             send the trees and   ->  TreeSample  (look up the world   ->  trees decide: grow, thrive,
//             any seed spots           at each root and seed spot)           suffer, seed, die
//                                      TreeUptake  (take nutrients)
//                                      CanopySplat (paint shade)
//                                              \\__ results come back asynchronously a few frames later
//
//           Between ticks the CPU only does cheap, bounded work each frame:
//             - a few growth steps (each adds one layer of branches to one tree),
//             - a few mesh rebuilds,
//             - drawing. So many trees growing at once spread over many frames and
//               never make a frame-time spike.
//           Growth is driven by SIM time (so the time dial speeds it up) accumulated
//           between ticks; the tick rate is real time (so 16x does not mean 16x the GPU work).
//
// Placement: there is none. A seed falls near its tree (or is scattered at the start, or
//           dropped by the player, or later carried by a bird) and germinates only if the
//           spot is land and the conditions are good, and then only with a modest chance.
//           Forests exist where the world supports them.
//
// Frames   : every position here is in the PLANET's frame (the frame the fields use); the
//           planet's current transform is applied only when drawing, so trees turn with the
//           planet. Drawing runs in LateUpdate, after PlanetMotion has turned the planet this
//           frame, or trees would lag a frame behind the ground.
//
// Draw     : branches - one Graphics.DrawMesh per tree (a mesh per tree, rebuilt when it
//                       grows; the choice is documented in TreeMesher).
//            leaves   - ONE instanced call for all trees: a leaf buffer with a fixed
//                       block of slots per tree, plus a per-tree state record.
// =============================================================================

using System;
using System.Collections.Generic;
using Terrarium.Sphere;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Trees
{
    /// <summary>Owns, grows and draws every tree.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class TreeManager : MonoBehaviour
    {
        private const int MaxSeedCandidatesPerTick = 64;
        private const int MaxSeedQueue = 1024;
        private const int ThreadGroupSize = 8;
        private const float MinRootHeight = 0.015f;   // seeds do not germinate on beaches or in water

        [SerializeField] private SimRoot simRoot;
        [SerializeField] private FieldSet fieldSet;
        [SerializeField] private Transform planet;
        [SerializeField] private Transform lamp;
        [SerializeField] private ComputeShader treeShader;
        [SerializeField] private ComputeShader brushShader;
        [SerializeField] private Material woodMaterial;
        [SerializeField] private Material leafMaterial;

        // ---- trees and slots
        private Tree[] slots = Array.Empty<Tree>();
        private readonly Stack<int> freeSlots = new Stack<int>();
        private int treeCount;
        private int capacity;
        private int treeCounter;
        private Rng rng;
        private readonly TreeMesher mesher = new TreeMesher();
        private readonly Queue<Tree> meshQueue = new Queue<Tree>();
        private int stepCursor;

        // ---- GPU
        private ComputeBuffer treeBuffer;
        private ComputeBuffer sampleDirBuffer;
        private ComputeBuffer sampleBuffer;
        private ComputeBuffer leafBuffer;
        private ComputeBuffer stateBuffer;
        private TreeGpu[] treeScratch;
        private Vector4[] sampleDirScratch;
        private LeafData[] leafScratch;
        private LeafData[] zeroLeaves;
        private Vector4[] stateScratch;
        private Mesh leafQuad;
        private MaterialPropertyBlock woodBlock;
        private MaterialPropertyBlock leafBlock;
        private TerrainHeightCache heights;
        private bool heightsDirty = true;

        // ---- tick
        private Tree[] snapshotTrees;
        private int snapshotTreeCount;
        private Vector3[] candidateDirs;
        private bool[] candidatePioneer;
        private int candidateCount;
        private float snapshotSeconds;
        private float accumulatedSeconds;
        private float lastTickTime;
        private bool readbackPending;
        private bool canopyDirty;
        private readonly Queue<Vector3> seedQueue = new Queue<Vector3>();
        private int pioneerSeedsLeft;

        /// <summary>Layout of one tree in the GPU buffer (32 bytes, matches Trees.compute).</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct TreeGpu
        {
            public Vector3 Direction;
            public float CanopyRadius;
            public float Uptake;
            public float Strength;
            public float CosRadius;
            public float Pad;
        }

        /// <summary>Trees standing (including dead ones that have not collapsed).</summary>
        public int TreeCount => treeCount;

        /// <summary>Trees that are alive.</summary>
        public int LiveCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null && !slots[i].Dead)
                    {
                        live++;
                    }
                }

                return live;
            }
        }

        /// <summary>The most trees the quality preset allows.</summary>
        public int Capacity => capacity;

        /// <summary>The tree in a slot, or null (for tests and tools).</summary>
        public Tree TreeAt(int slot) => slot >= 0 && slot < slots.Length ? slots[slot] : null;

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || planet == null || lamp == null || treeShader == null || brushShader == null || woodMaterial == null || leafMaterial == null)
            {
                Debug.LogError("TreeManager is missing a reference.", this);
                enabled = false;
                return;
            }

            heights = new TerrainHeightCache();
            woodBlock = new MaterialPropertyBlock();
            leafBlock = new MaterialPropertyBlock();
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
            ReleaseTrees();
            ReleaseBuffers();
        }

        private void HandleWorldReset()
        {
            ClearAllTrees();
            rng = new Rng(simRoot.ActiveSeed ^ 0x5bd1e995);
            pioneerSeedsLeft = simRoot.Config.treeStartSeeds;
            heightsDirty = true;
            canopyDirty = true;
        }

        // ================================================================= public actions

        /// <summary>
        /// Accumulates simulation time. Called once per simulation step by SimLoop; the trees use the total
        /// at the next GPU tick, so a fast-forwarded sim just means bigger chunks of time.
        /// </summary>
        public void Step(float deltaSeconds)
        {
            accumulatedSeconds += deltaSeconds;
        }

        /// <summary>
        /// The seed jar: plants a seed at a point on the planet. Returns null on success, or a short reason it could not be planted.
        /// </summary>
        public string PlantSeed(Vector3 localDirection)
        {
            EnsureCapacity();
            if (!heights.HasData)
            {
                return "The planet is still settling: try again in a moment.";
            }

            float height = heights.HeightAt(localDirection);
            if (height < MinRootHeight)
            {
                return "Seeds need dry land.";
            }

            if (treeCount >= capacity)
            {
                return "The planet is as full of trees as this quality setting allows.";
            }

            Plant(localDirection.normalized, height);
            return null;
        }

        /// <summary>An impact: every tree inside the circle is destroyed at once.</summary>
        public void KillInRadius(Vector3 localDirection, float angularRadius)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Tree tree = slots[i];
                if (tree == null)
                {
                    continue;
                }

                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(tree.Direction, localDirection), -1f, 1f));
                if (angle <= angularRadius)
                {
                    RemoveTree(tree, returnOrganic: false);
                }
            }

            // The terrain changed: the cached heights are out of date.
            heightsDirty = true;
        }

        // ================================================================= per frame

        private void Update()
        {
            EnsureCapacity();
            if (capacity == 0)
            {
                return;
            }

            if (heightsDirty && fieldSet.Resolution > 0)
            {
                heightsDirty = false;
                heights.Request(fieldSet.Get(FieldId.Terrain).Current);
            }

            RunGrowthSteps();
            RebuildMeshes();
            MaybeTick();
        }

        private void LateUpdate()
        {
            if (capacity == 0)
            {
                return;
            }

            Draw();
        }

        // Runs a bounded number of growth steps, rotating through the trees so each gets its turn.
        private void RunGrowthSteps()
        {
            SimConfig config = simRoot.Config;
            Vector3 lampLocal = Quaternion.Inverse(planet.rotation) * (lamp.position - planet.position).normalized;
            int budget = config.treeStepsPerFrame;

            for (int visited = 0; visited < slots.Length && budget > 0; visited++)
            {
                stepCursor = (stepCursor + 1) % slots.Length;
                Tree tree = slots[stepCursor];
                if (tree == null || tree.PendingSteps <= 0)
                {
                    continue;
                }

                // The lamp's direction in this tree's own frame; only the part along the ground, and only by day.
                Vector3 toLamp = Quaternion.Inverse(tree.Frame) * lampLocal;
                float daylight = Mathf.Clamp01(toLamp.y * 2f);
                var horizontal = new Vector3(toLamp.x, 0f, toLamp.z);
                Vector3 toLight = horizontal.sqrMagnitude > 1e-6f ? horizontal.normalized * daylight : Vector3.zero;

                while (tree.PendingSteps > 0 && budget > 0)
                {
                    tree.PendingSteps--;
                    budget--;
                    if (tree.GrowStep(toLight))
                    {
                        Enqueue(tree);
                    }
                    else if (tree.Mature)
                    {
                        tree.PendingSteps = 0;
                        Enqueue(tree);
                    }
                }
            }
        }

        private void Enqueue(Tree tree)
        {
            if (!tree.Queued)
            {
                tree.Queued = true;
                meshQueue.Enqueue(tree);
            }
        }

        // Rebuilds a few branch meshes and their leaves per frame.
        private void RebuildMeshes()
        {
            SimConfig config = simRoot.Config;
            float unit = 1f / config.planetRadius;
            int budget = config.treeMeshesPerFrame;

            while (budget-- > 0 && meshQueue.Count > 0)
            {
                Tree tree = meshQueue.Dequeue();
                tree.Queued = false;
                if (tree.Removed || !tree.MeshDirty)
                {
                    continue;
                }

                tree.MeshDirty = false;
                tree.Colony.ComputeRadii();
                mesher.Build(tree.Colony, simRoot.ActivePreset.meshRingSides, tree.Mesh);

                float twig = config.treeTipRadius * unit * config.treeTwigRadiusMultiple;
                int count = mesher.BuildLeaves(tree.Colony, tree.LocalToPlanet, tree.Direction, config.treeLeafSize * unit, twig, tree.Slot * 7919 + 13, leafScratch);
                leafBuffer.SetData(leafScratch, 0, tree.Slot * config.treeLeavesPerTree, count);

                // If the tree used to have more leaves than now, blank the rest of its block.
                if (tree.LeafCount > count)
                {
                    leafBuffer.SetData(zeroLeaves, 0, tree.Slot * config.treeLeavesPerTree + count, tree.LeafCount - count);
                }

                tree.LeafCount = count;
            }
        }

        private void Draw()
        {
            SimConfig config = simRoot.Config;
            Matrix4x4 planetToWorld = planet.localToWorldMatrix;

            for (int i = 0; i < slots.Length; i++)
            {
                Tree tree = slots[i];
                stateScratch[i] = tree == null ? Vector4.zero : new Vector4(tree.LeafAmount, tree.Autumn, tree.Health, tree.Decay);
                if (tree == null || tree.Mesh.vertexCount == 0)
                {
                    continue;
                }

                woodBlock.SetFloat("_Decay", tree.Decay);
                Graphics.DrawMesh(tree.Mesh, planetToWorld * tree.LocalToPlanet, woodMaterial, 0, null, 0, woodBlock);
            }

            stateBuffer.SetData(stateScratch);

            if (treeCount > 0)
            {
                leafBlock.SetBuffer("_Leaves", leafBuffer);
                leafBlock.SetBuffer("_TreeState", stateBuffer);
                leafBlock.SetMatrix("_PlanetLocalToWorld", planetToWorld);
                leafBlock.SetInt("_LeavesPerTree", config.treeLeavesPerTree);
                Graphics.DrawMeshInstancedProcedural(leafQuad, 0, leafMaterial, new Bounds(planet.position, Vector3.one * 8f),
                    capacity * config.treeLeavesPerTree, leafBlock, ShadowCastingMode.Off, false);
            }

            Shader.SetGlobalFloat("_Season", simRoot.Clock.YearPhase);
        }

        // ================================================================= the GPU tick

        private void MaybeTick()
        {
            SimConfig config = simRoot.Config;
            if (readbackPending || fieldSet.Resolution == 0 || Time.unscaledTime - lastTickTime < config.treeTickRealSeconds)
            {
                return;
            }

            bool seedsWaiting = seedQueue.Count > 0 || pioneerSeedsLeft > 0;
            bool simAdvanced = accumulatedSeconds > 0f;
            if (!(treeCount > 0 && simAdvanced) && !seedsWaiting && !canopyDirty)
            {
                return;
            }

            lastTickTime = Time.unscaledTime;
            canopyDirty = false;
            RunTick(config);
        }

        private void RunTick(SimConfig config)
        {
            float planetRadius = config.planetRadius;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                Tree tree = slots[i];
                if (tree == null)
                {
                    continue;
                }

                float strength = tree.Dead ? 0f : config.treeCanopyStrength * tree.SizeFraction * tree.LeafAmount;
                float uptake = tree.Dead ? 0f : tree.GrowthRate * config.treeUptakePerSecond * (0.3f + 0.7f * tree.SizeFraction) * accumulatedSeconds;
                float radius = tree.CrownRadius;

                tree.LastCanopyStrength = strength;
                snapshotTrees[n] = tree;
                treeScratch[n] = new TreeGpu { Direction = tree.Direction, CanopyRadius = radius, Uptake = uptake, Strength = strength, CosRadius = Mathf.Cos(Mathf.Min(radius, Mathf.PI)) };
                sampleDirScratch[n] = tree.Direction;
                n++;
            }

            snapshotTreeCount = n;

            // Places where seeds have landed, and pioneer seeds, to be sampled too.
            candidateCount = 0;
            while (candidateCount < MaxSeedCandidatesPerTick && (seedQueue.Count > 0 || pioneerSeedsLeft > 0))
            {
                bool pioneer = seedQueue.Count == 0;
                Vector3 direction = pioneer ? RandomDirection() : seedQueue.Dequeue();
                if (pioneer)
                {
                    pioneerSeedsLeft--;
                }

                candidateDirs[candidateCount] = direction;
                candidatePioneer[candidateCount] = pioneer;
                sampleDirScratch[n + candidateCount] = direction;
                candidateCount++;
            }

            int sampleCount = n + candidateCount;
            snapshotSeconds = accumulatedSeconds;
            accumulatedSeconds = 0f;

            if (n > 0)
            {
                treeBuffer.SetData(treeScratch, 0, 0, n);
            }

            if (sampleCount > 0)
            {
                sampleDirBuffer.SetData(sampleDirScratch, 0, 0, sampleCount);
            }

            int resolution = fieldSet.Resolution;
            int groups = (resolution + ThreadGroupSize - 1) / ThreadGroupSize;
            treeShader.SetInt("_Resolution", resolution);
            treeShader.SetInt("_SampleCount", sampleCount);
            treeShader.SetInt("_TreeCount", n);
            treeShader.SetInt("_UptakeRadius", 4);

            RenderTexture soil = fieldSet.Get(FieldId.Soil).Current;
            int sampleKernel = treeShader.FindKernel("TreeSample");
            int uptakeKernel = treeShader.FindKernel("TreeUptake");
            int canopyKernel = treeShader.FindKernel("CanopySplat");

            if (sampleCount > 0)
            {
                treeShader.SetBuffer(sampleKernel, "_SampleDirs", sampleDirBuffer);
                treeShader.SetBuffer(sampleKernel, "_Samples", sampleBuffer);
                treeShader.SetTexture(sampleKernel, "_Soil", soil);
                treeShader.SetTexture(sampleKernel, "_Climate", fieldSet.Get(FieldId.Climate).Current);
                treeShader.SetTexture(sampleKernel, "_Terrain", fieldSet.Get(FieldId.Terrain).Current);
                treeShader.SetTexture(sampleKernel, "_Trail", fieldSet.Get(FieldId.Trail).Current);
                treeShader.Dispatch(sampleKernel, (sampleCount + 63) / 64, 1, 1);
            }

            if (n > 0)
            {
                treeShader.SetBuffer(uptakeKernel, "_Trees", treeBuffer);
                treeShader.SetTexture(uptakeKernel, "_SoilRW", soil);
                treeShader.Dispatch(uptakeKernel, (n + 63) / 64, 1, 1);
            }

            treeShader.SetBuffer(canopyKernel, "_Trees", treeBuffer);
            treeShader.SetTexture(canopyKernel, "_SoilRW", soil);
            treeShader.Dispatch(canopyKernel, groups, groups, CubeSphere.FaceCount);

            if (sampleCount > 0)
            {
                readbackPending = true;
                AsyncGPUReadback.Request(sampleBuffer, sampleCount * 2 * 16, 0, OnSamples);
            }
        }

        // The GPU's answer has arrived: let every sampled tree and seed act on it.
        private void OnSamples(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (request.hasError || treeBuffer == null)
            {
                return;
            }

            NativeArray<Vector4> data = request.GetData<Vector4>();
            SimConfig config = simRoot.Config;

            for (int i = 0; i < snapshotTreeCount; i++)
            {
                Tree tree = snapshotTrees[i];
                if (tree != null && !tree.Removed)
                {
                    ApplySample(tree, data[i * 2], data[i * 2 + 1], snapshotSeconds, config);
                }
            }

            for (int j = 0; j < candidateCount; j++)
            {
                int index = (snapshotTreeCount + j) * 2;
                TryGerminate(candidateDirs[j], candidatePioneer[j], data[index], data[index + 1], config);
            }
        }

        // ================================================================= life of one tree

        private void ApplySample(Tree tree, Vector4 a, Vector4 b, float deltaSeconds, SimConfig config)
        {
            // a = (nutrients, moisture, temperature, canopy)   b = (trail, insolation, height, 0)
            var environment = new TreeEnvironment
            {
                Nutrients = a.x,
                Moisture = a.y,
                Temperature = a.z,
                Crowding = Mathf.Max(0f, a.w - tree.LastCanopyStrength),
                Trail = b.x,
            };

            tree.Age += deltaSeconds;

            if (tree.Dead)
            {
                tree.DeadTime += deltaSeconds;
                float noLeaves = tree.LeafAmount;
                float noTint = tree.Autumn;
                TreeLife.UpdateLeaves(ref noLeaves, ref noTint, -50f, deltaSeconds, config);
                tree.LeafAmount = noLeaves;
                if (tree.DeadTime >= config.treeDeadSeconds)
                {
                    RemoveTree(tree, returnOrganic: true);
                }

                return;
            }

            float growth = TreeLife.GrowthRate(environment, config);
            tree.GrowthRate = growth;

            float quality = TreeLife.NutrientFactor(environment.Nutrients, environment.Trail, config) * TreeLife.MoistureFactor(environment.Moisture, config);
            tree.Richness = Mathf.Clamp01(Mathf.Lerp(tree.Richness, quality, Mathf.Clamp01(deltaSeconds / 40f)));

            tree.Health = Mathf.Clamp01(tree.Health + TreeLife.HealthChange(environment, growth, deltaSeconds, config));

            float leaf = tree.LeafAmount;
            float autumn = tree.Autumn;
            TreeLife.UpdateLeaves(ref leaf, ref autumn, environment.Temperature, deltaSeconds, config);
            tree.LeafAmount = leaf;
            tree.Autumn = autumn;

            if (tree.Health <= 0f || tree.Age >= tree.Lifespan)
            {
                tree.Dead = true;
                tree.PendingSteps = 0;
                return;
            }

            if (!tree.Mature)
            {
                tree.GrowthProgress += growth * deltaSeconds / config.treeGrowSecondsPerStep;
                int steps = Mathf.FloorToInt(tree.GrowthProgress);
                if (steps > 0)
                {
                    tree.GrowthProgress -= steps;
                    tree.PendingSteps = Mathf.Min(tree.PendingSteps + steps, 40);
                }
            }

            int seeds = TreeLife.SeedsDue(ref tree.SeedClock, tree.Mature, tree.Health, tree.LeafAmount, growth, deltaSeconds, config);
            for (int s = 0; s < seeds && seedQueue.Count < MaxSeedQueue; s++)
            {
                seedQueue.Enqueue(SeedLandingPoint(tree, config));
            }
        }

        // A point near the tree where one of its seeds comes to rest.
        private Vector3 SeedLandingPoint(Tree tree, SimConfig config)
        {
            Vector3 up = tree.Direction;
            Vector3 reference = Mathf.Abs(up.y) < 0.95f ? Vector3.up : Vector3.right;
            Vector3 east = Vector3.Cross(up, reference).normalized;
            Vector3 north = Vector3.Cross(up, east);
            float bearing = rng.Range(0f, 2f * Mathf.PI);
            float distance = rng.Range(config.treeSeedDispersalMin, config.treeSeedDispersalMax);

            Vector3 outward = east * Mathf.Cos(bearing) + north * Mathf.Sin(bearing);
            return (up * Mathf.Cos(distance) + outward * Mathf.Sin(distance)).normalized;
        }

        private void TryGerminate(Vector3 direction, bool pioneer, Vector4 a, Vector4 b, SimConfig config)
        {
            if (b.z < MinRootHeight || treeCount >= capacity)
            {
                return;
            }

            var environment = new TreeEnvironment
            {
                Nutrients = a.x,
                Moisture = a.y,
                Temperature = a.z,
                Crowding = a.w,
                Trail = b.x,
            };

            // Pioneer seeds (the first rain of seed on a new world) have better odds, so a forest can start.
            float chance = TreeLife.GerminationChance(environment, config) * (pioneer ? 3f : 1f);
            if (rng.NextFloat() < chance)
            {
                // The seedling starts out as rich as the spot it landed on, so its crown reflects the soil from the first branch.
                float quality = TreeLife.NutrientFactor(environment.Nutrients, environment.Trail, config) * TreeLife.MoistureFactor(environment.Moisture, config);
                Plant(direction, b.z, quality);
            }
        }

        private Vector3 RandomDirection()
        {
            // A uniform point on the sphere: z uniform in [-1, 1], longitude uniform.
            float z = rng.Range(-1f, 1f);
            float phi = rng.Range(0f, 2f * Mathf.PI);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(phi), z, r * Mathf.Sin(phi));
        }

        // ================================================================= create / remove

        private void Plant(Vector3 direction, float height, float richness = 0.5f)
        {
            SimConfig config = simRoot.Config;
            float unit = 1f / config.planetRadius;
            int slot = freeSlots.Pop();

            float matureHeight = rng.Range(config.treeHeightMin, config.treeHeightMax) * unit;
            var settings = new SpaceColonization.Settings
            {
                SegmentLength = config.treeSegmentFraction * matureHeight,
                InfluenceRadius = config.treeInfluenceFraction * matureHeight,
                KillRadius = config.treeKillFraction * matureHeight,
                Gravitropism = config.treeGravitropism,
                Phototropism = config.treePhototropism,
                Wobble = config.treeWobble,
                MaxNodes = simRoot.ActivePreset.maxSegmentsPerTree,
                TipRadius = config.treeTipRadius * unit,
                PipeExponent = config.treePipeExponent,
            };

            float rootRadius = 1f + config.terrainHeightScale * Mathf.Max(height, 0f);
            var tree = new Tree(slot, simRoot.ActiveSeed * 31 + treeCounter++, direction, rootRadius, matureHeight, settings, config, ref rng);
            tree.PendingSteps = 1;
            tree.Richness = Mathf.Clamp01(richness);
            slots[slot] = tree;
            treeCount++;

            // Blank the leaf block this slot may have held for a previous tree.
            leafBuffer.SetData(zeroLeaves, 0, slot * config.treeLeavesPerTree, config.treeLeavesPerTree);
            Enqueue(tree);
            canopyDirty = true;
        }

        private void RemoveTree(Tree tree, bool returnOrganic)
        {
            if (tree.Removed)
            {
                return;
            }

            SimConfig config = simRoot.Config;
            if (returnOrganic)
            {
                // The dead tree rots into the soil: organic matter, which decomposes into nutrients.
                var brush = new FieldBrush(brushShader);
                float organic = config.treeDeathOrganic * (0.25f + 0.75f * tree.SizeFraction);
                brush.Apply(fieldSet.Get(FieldId.Soil).Current, tree.Direction, 0.025f, new Vector4(0f, organic, 0f, 0f), 0f, 50f);
            }

            slots[tree.Slot] = null;
            freeSlots.Push(tree.Slot);
            treeCount--;
            leafBuffer.SetData(zeroLeaves, 0, tree.Slot * config.treeLeavesPerTree, config.treeLeavesPerTree);
            tree.Destroy();
            canopyDirty = true;
        }

        private void ClearAllTrees()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].Destroy();
                    slots[i] = null;
                }
            }

            freeSlots.Clear();
            for (int i = slots.Length - 1; i >= 0; i--)
            {
                freeSlots.Push(i);
            }

            treeCount = 0;
            meshQueue.Clear();
            seedQueue.Clear();
            if (leafBuffer != null)
            {
                leafBuffer.SetData(new LeafData[leafBuffer.count]);
            }
        }

        private void ReleaseTrees()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].Destroy();
                    slots[i] = null;
                }
            }

            treeCount = 0;
        }

        // ================================================================= buffers

        private void EnsureCapacity()
        {
            int wanted = simRoot.ActivePreset.maxTrees;
            SimConfig config = simRoot.Config;
            if (capacity == wanted && treeBuffer != null && leafBuffer.count == wanted * config.treeLeavesPerTree)
            {
                return;
            }

            // Keep the trees that still fit, drop the rest, and rebuild the buffers at the new size.
            Tree[] old = slots;
            ReleaseBuffers();

            capacity = wanted;
            slots = new Tree[capacity];
            freeSlots.Clear();
            int kept = 0;
            for (int i = 0; i < old.Length; i++)
            {
                if (old[i] == null)
                {
                    continue;
                }

                if (kept < capacity)
                {
                    // Slot numbers are part of a tree's identity (leaf block), so a moved tree is simply re-queued.
                    slots[kept] = old[i];
                    old[i].MeshDirty = true;
                    old[i].Queued = false;
                    kept++;
                }
                else
                {
                    old[i].Destroy();
                }
            }

            for (int i = capacity - 1; i >= kept; i--)
            {
                freeSlots.Push(i);
            }

            treeCount = kept;
            meshQueue.Clear();

            int leaves = config.treeLeavesPerTree;
            treeBuffer = new ComputeBuffer(capacity, 32);
            sampleDirBuffer = new ComputeBuffer(capacity + MaxSeedCandidatesPerTick, 16);
            sampleBuffer = new ComputeBuffer((capacity + MaxSeedCandidatesPerTick) * 2, 16);
            leafBuffer = new ComputeBuffer(capacity * leaves, 32);
            stateBuffer = new ComputeBuffer(capacity, 16);
            leafBuffer.SetData(new LeafData[capacity * leaves]);

            treeScratch = new TreeGpu[capacity];
            sampleDirScratch = new Vector4[capacity + MaxSeedCandidatesPerTick];
            snapshotTrees = new Tree[capacity];
            candidateDirs = new Vector3[MaxSeedCandidatesPerTick];
            candidatePioneer = new bool[MaxSeedCandidatesPerTick];
            leafScratch = new LeafData[leaves];
            zeroLeaves = new LeafData[leaves];
            stateScratch = new Vector4[capacity];

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].MeshDirty = true;
                    Enqueue(slots[i]);
                }
            }

            if (leafQuad == null)
            {
                leafQuad = BuildQuad();
            }
        }

        private void ReleaseBuffers()
        {
            ReleaseBuffer(ref treeBuffer);
            ReleaseBuffer(ref sampleDirBuffer);
            ReleaseBuffer(ref sampleBuffer);
            ReleaseBuffer(ref leafBuffer);
            ReleaseBuffer(ref stateBuffer);
            readbackPending = false;
        }

        private static void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        // A unit quad centred on the origin, facing -z, with 0..1 uvs.
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "LeafQuad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return mesh;
        }

        /// <summary>Immediate-mode readout, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            GUILayout.Label($"Trees: {LiveCount} alive, {TreeCount - LiveCount} dead (room for {Capacity})");
            GUILayout.Label($"  seeds waiting: {seedQueue.Count + pioneerSeedsLeft}");
        }
    }
}
