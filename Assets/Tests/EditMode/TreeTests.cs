// =============================================================================
// TreeTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the tree system's pure parts:
//           TreeMesher - the right numbers of vertices and triangles, valid
//             indices, unit normals, outward-facing triangles, leaves on twigs
//             only, deterministic, never more than the limit;
//           TreeLife - each rule responds the right way to the conditions:
//             temperature, moisture, nutrients (and the fungal network), shade;
//             health falls under stress and recovers; leaves bud, turn and fall;
//             seeds only from healthy mature trees; germination is rare.
// =============================================================================

using NUnit.Framework;
using Terrarium.Trees;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class TreeTests
    {
        private SimConfig config;

        [SetUp]
        public void CreateConfig()
        {
            config = ScriptableObject.CreateInstance<SimConfig>();
        }

        [TearDown]
        public void DestroyConfig()
        {
            Object.DestroyImmediate(config);
        }

        private static SpaceColonization GrownTree(int seed = 4)
        {
            var settings = new SpaceColonization.Settings
            {
                SegmentLength = 0.05f,
                InfluenceRadius = 0.3f,
                KillRadius = 0.08f,
                Gravitropism = 0.1f,
                Phototropism = 0f,
                Wobble = 0.1f,
                MaxNodes = 800,
                TipRadius = 0.004f,
                PipeExponent = 2.5f,
            };

            var tree = new SpaceColonization(seed, settings);
            tree.AddTrunk(0.3f);
            tree.AddAttractors(new Vector3(0f, 0.65f, 0f), new Vector3(0.35f, 0.35f, 0.35f), 250);
            while (tree.Grow(Vector3.up, Vector3.up) > 0)
            {
            }

            tree.ComputeRadii();
            return tree;
        }

        // ------------------------------------------------------------------- mesher

        [TestCase(3)]
        [TestCase(6)]
        [TestCase(8)]
        public void TreeMesher_HasTheExpectedCounts_ValidIndices_AndUnitNormals(int sides)
        {
            SpaceColonization tree = GrownTree();
            var mesh = new Mesh();
            try
            {
                int vertices = new TreeMesher().Build(tree, sides, mesh);
                int segments = tree.NodeCount - 1;

                Assert.AreEqual(2 * sides * segments, vertices);
                Assert.AreEqual(vertices, mesh.vertexCount);
                Assert.AreEqual(6 * sides * segments, (int)mesh.GetIndexCount(0));

                int[] triangles = mesh.triangles;
                foreach (int index in triangles)
                {
                    Assert.That(index, Is.InRange(0, vertices - 1));
                }

                foreach (Vector3 normal in mesh.normals)
                {
                    Assert.AreEqual(1f, normal.magnitude, 1e-4f);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TreeMesher_AllTrianglesFaceOutward_AndTheMeshSurroundsTheSkeleton()
        {
            SpaceColonization tree = GrownTree();
            var mesh = new Mesh();
            try
            {
                new TreeMesher().Build(tree, 6, mesh);
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] triangles = mesh.triangles;

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]];
                    Vector3 b = vertices[triangles[t + 1]];
                    Vector3 c = vertices[triangles[t + 2]];
                    Vector3 normal = normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]];

                    Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), normal), 0f, $"Triangle {t / 3} faces inward.");
                }

                // Every skeleton node lies inside the mesh's bounds.
                for (int n = 0; n < tree.NodeCount; n++)
                {
                    Assert.IsTrue(mesh.bounds.Contains(tree.PositionOf(n)), $"Node {n} is outside the mesh bounds.");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TreeMesher_BranchesTaper_TheTrunkBaseIsWiderThanATwig()
        {
            SpaceColonization tree = GrownTree();
            var mesh = new Mesh();
            try
            {
                new TreeMesher().Build(tree, 6, mesh);
                Vector3[] vertices = mesh.vertices;

                // The first segment (trunk base) occupies vertices 0..11; its parent-end ring is the first 6.
                float baseWidth = 0f;
                for (int k = 1; k < 6; k++)
                {
                    baseWidth = Mathf.Max(baseWidth, Vector3.Distance(vertices[0], vertices[k]));
                }

                // The last segment ends at a twig tip.
                int last = vertices.Length - 6;
                float tipWidth = 0f;
                for (int k = 1; k < 6; k++)
                {
                    tipWidth = Mathf.Max(tipWidth, Vector3.Distance(vertices[last], vertices[last + k]));
                }

                Assert.Greater(baseWidth, tipWidth * 3f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TreeMesher_Leaves_AreOnTwigsOnly_RespectTheLimit_AndAreDeterministic()
        {
            SpaceColonization tree = GrownTree();
            var mesher = new TreeMesher();
            Matrix4x4 placement = Matrix4x4.Translate(new Vector3(10f, 0f, 0f));

            var leavesA = new LeafData[300];
            var leavesB = new LeafData[300];
            int countA = mesher.BuildLeaves(tree, placement, Vector3.up, 0.02f, 0.01f, 77, leavesA);
            int countB = mesher.BuildLeaves(tree, placement, Vector3.up, 0.02f, 0.01f, 77, leavesB);

            Assert.AreEqual(countA, countB);
            Assert.Greater(countA, 20);
            Assert.LessOrEqual(countA, 300);
            for (int i = 0; i < countA; i++)
            {
                Assert.AreEqual(leavesA[i].Position, leavesB[i].Position, "Leaves must be deterministic.");
                Assert.Greater(leavesA[i].Size, 0.01f);
                Assert.That(leavesA[i].Phase, Is.InRange(0f, 1f));
                Assert.That(leavesA[i].Position.x, Is.InRange(9f, 11f), "The placement transform must be applied.");
            }

            // A tiny limit trims evenly, never overflowing the array.
            var few = new LeafData[10];
            Assert.AreEqual(10, mesher.BuildLeaves(tree, placement, Vector3.up, 0.02f, 0.01f, 77, few));
        }

        [Test]
        public void TreeMesher_AnEmptyTree_MakesNoMesh()
        {
            var settings = new SpaceColonization.Settings { SegmentLength = 0.05f, InfluenceRadius = 0.3f, KillRadius = 0.08f, MaxNodes = 10, TipRadius = 0.004f, PipeExponent = 2.5f };
            var tree = new SpaceColonization(1, settings);
            tree.AddRoot();
            tree.ComputeRadii();

            var mesh = new Mesh();
            try
            {
                Assert.AreEqual(0, new TreeMesher().Build(tree, 6, mesh));
                Assert.AreEqual(0, mesh.vertexCount);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        // ---------------------------------------------------------------------- tree

        // Grows a whole tree to maturity on its own (no scene), returning it. The caller destroys the mesh.
        private Terrarium.Trees.Tree GrowToMaturity(float richness, Vector3 toLight)
        {
            var settings = new SpaceColonization.Settings
            {
                SegmentLength = config.treeSegmentFraction * 0.05f,
                InfluenceRadius = config.treeInfluenceFraction * 0.05f,
                KillRadius = config.treeKillFraction * 0.05f,
                Gravitropism = config.treeGravitropism,
                Phototropism = config.treePhototropism,
                Wobble = config.treeWobble,
                MaxNodes = 1200,
                TipRadius = 0.0004f,
                PipeExponent = 2.5f,
            };

            var rng = new Rng(5);
            var tree = new Terrarium.Trees.Tree(0, 99, Vector3.up, 1f, 0.05f, settings, config, ref rng) { Richness = richness };
            for (int i = 0; i < 400 && !tree.Mature; i++)
            {
                tree.GrowStep(toLight);
            }

            return tree;
        }

        [Test]
        public void Tree_PoorGround_GrowsASparserTree_ThanRichGround()
        {
            Terrarium.Trees.Tree rich = GrowToMaturity(0.95f, Vector3.zero);
            Terrarium.Trees.Tree poor = GrowToMaturity(0.1f, Vector3.zero);
            try
            {
                Assert.IsTrue(rich.Mature && poor.Mature, "Both trees must finish growing.");
                Assert.Greater(rich.Colony.NodeCount, poor.Colony.NodeCount * 1.8f,
                    $"Rich ground: {rich.Colony.NodeCount} branches; poor ground: {poor.Colony.NodeCount}.");
            }
            finally
            {
                Object.DestroyImmediate(rich.Mesh);
                Object.DestroyImmediate(poor.Mesh);
            }
        }

        [Test]
        public void Tree_CrownLeansTowardTheLight()
        {
            Terrarium.Trees.Tree lit = GrowToMaturity(0.7f, new Vector3(1f, 0f, 0f));
            Terrarium.Trees.Tree dark = GrowToMaturity(0.7f, Vector3.zero);
            try
            {
                float MeanX(Terrarium.Trees.Tree tree)
                {
                    float sum = 0f;
                    for (int i = 0; i < tree.Colony.NodeCount; i++)
                    {
                        sum += tree.Colony.PositionOf(i).x;
                    }

                    return sum / tree.Colony.NodeCount;
                }

                Assert.Greater(MeanX(lit) - MeanX(dark), 0.003f, $"Lit tree mean x {MeanX(lit):0.0000}, unlit {MeanX(dark):0.0000} (tree height is 0.05).");
            }
            finally
            {
                Object.DestroyImmediate(lit.Mesh);
                Object.DestroyImmediate(dark.Mesh);
            }
        }

        [Test]
        public void Tree_BecomesMature_AndThenStopsGrowing()
        {
            Terrarium.Trees.Tree tree = GrowToMaturity(0.6f, Vector3.zero);
            try
            {
                Assert.IsTrue(tree.Mature);
                int nodes = tree.Colony.NodeCount;

                Assert.IsFalse(tree.GrowStep(Vector3.zero), "A mature tree grows no more.");
                Assert.AreEqual(nodes, tree.Colony.NodeCount);
            }
            finally
            {
                Object.DestroyImmediate(tree.Mesh);
            }
        }

        [Test]
        public void Tree_ADeadTree_DoesNotGrow()
        {
            var settings = new SpaceColonization.Settings { SegmentLength = 0.002f, InfluenceRadius = 0.015f, KillRadius = 0.004f, MaxNodes = 100, TipRadius = 0.0004f, PipeExponent = 2.5f };
            var rng = new Rng(2);
            var tree = new Terrarium.Trees.Tree(0, 3, Vector3.up, 1f, 0.05f, settings, config, ref rng) { Dead = true };
            try
            {
                int nodes = tree.Colony.NodeCount;
                Assert.IsFalse(tree.GrowStep(Vector3.zero));
                Assert.AreEqual(nodes, tree.Colony.NodeCount);
            }
            finally
            {
                Object.DestroyImmediate(tree.Mesh);
            }
        }

        // --------------------------------------------------------------------- life

        [Test]
        public void TreeLife_TemperatureFactor_IsABump()
        {
            Assert.AreEqual(0f, TreeLife.TemperatureFactor(config.treeTempMin - 5f, config));
            Assert.AreEqual(0f, TreeLife.TemperatureFactor(config.treeTempMin, config), 1e-5f);
            Assert.AreEqual(1f, TreeLife.TemperatureFactor(config.treeTempOptimum, config), 1e-5f);
            Assert.AreEqual(0f, TreeLife.TemperatureFactor(config.treeTempMax, config), 1e-5f);
            Assert.AreEqual(0f, TreeLife.TemperatureFactor(config.treeTempMax + 10f, config));

            float previous = -1f;
            for (float t = config.treeTempMin; t <= config.treeTempOptimum; t += 0.5f)
            {
                float factor = TreeLife.TemperatureFactor(t, config);
                Assert.GreaterOrEqual(factor, previous - 1e-6f, "Warming toward the optimum must never reduce growth.");
                previous = factor;
            }
        }

        [Test]
        public void TreeLife_NutrientFactor_RisesWithNutrients()
        {
            Assert.AreEqual(0f, TreeLife.NutrientFactor(0f, config));
            Assert.AreEqual(0.5f, TreeLife.NutrientFactor(config.treeNutrientHalf, config), 1e-5f);
            Assert.Greater(TreeLife.NutrientFactor(0.5f, config), TreeLife.NutrientFactor(0.1f, config));
            Assert.Less(TreeLife.NutrientFactor(10f, config), 1f);
        }

        [Test]
        public void TreeLife_ClearedLand_StarvesTrees_AndBlocksSeeds()
        {
            TreeEnvironment open = Good;
            TreeEnvironment farmed = Good;
            farmed.Cleared = 0.5f;
            TreeEnvironment builtOver = Good;
            builtOver.Cleared = 1f;

            Assert.AreEqual(1f, TreeLife.ClearingFactor(0f, config), 1e-6f);
            Assert.Less(TreeLife.GrowthRate(farmed, config), TreeLife.GrowthRate(open, config), "A tree among the fields grows worse.");
            Assert.Less(TreeLife.GrowthRate(builtOver, config), TreeLife.GrowthRate(farmed, config));
            Assert.Greater(TreeLife.GrowthRate(builtOver, config), 0f, "Clearing starves a tree; it does not instantly kill it.");

            Assert.Greater(TreeLife.GerminationChance(open, config), 0f);
            Assert.AreEqual(0f, TreeLife.GerminationChance(builtOver, config), "Nothing germinates on land that people have built over.");
        }

        [Test]
        public void TreeLife_Shade_ReducesGrowth()
        {
            Assert.AreEqual(1f, TreeLife.LightFactor(0f, config), 1e-6f);
            Assert.Less(TreeLife.LightFactor(1f, config), TreeLife.LightFactor(0.2f, config));
            Assert.Greater(TreeLife.LightFactor(5f, config), 0f);
        }

        private static TreeEnvironment Good => new TreeEnvironment { Temperature = 22f, Moisture = 0.8f, Nutrients = 1f, Cleared = 0f, Crowding = 0f };

        [Test]
        public void TreeLife_GrowthRate_IsHighInGoodConditions_AndZeroWhenAnyNecessityIsMissing()
        {
            Assert.Greater(TreeLife.GrowthRate(Good, config), 0.8f);

            TreeEnvironment frozen = Good;
            frozen.Temperature = -10f;
            TreeEnvironment dry = Good;
            dry.Moisture = 0f;
            TreeEnvironment barren = Good;
            barren.Nutrients = 0f;

            Assert.AreEqual(0f, TreeLife.GrowthRate(frozen, config));
            Assert.AreEqual(0f, TreeLife.GrowthRate(dry, config));
            Assert.AreEqual(0f, TreeLife.GrowthRate(barren, config));
        }

        [Test]
        public void TreeLife_Health_FallsUnderStress_AndRecoversWhenThriving()
        {
            TreeEnvironment good = Good;
            float thriving = TreeLife.HealthChange(good, TreeLife.GrowthRate(good, config), 10f, config);
            Assert.Greater(thriving, 0f);

            TreeEnvironment frozen = Good;
            frozen.Temperature = config.treeFreezeTemp - 5f;
            Assert.Less(TreeLife.HealthChange(frozen, TreeLife.GrowthRate(frozen, config), 10f, config), 0f, "Freezing hurts.");

            TreeEnvironment drought = Good;
            drought.Moisture = 0.01f;
            Assert.Less(TreeLife.HealthChange(drought, TreeLife.GrowthRate(drought, config), 10f, config), 0f, "Drought hurts.");

            TreeEnvironment scorched = Good;
            scorched.Temperature = config.treeHeatTemp + 5f;
            Assert.Less(TreeLife.HealthChange(scorched, TreeLife.GrowthRate(scorched, config), 10f, config), 0f, "Scorching hurts.");

            TreeEnvironment starving = Good;
            starving.Nutrients = 0f;
            Assert.Less(TreeLife.HealthChange(starving, TreeLife.GrowthRate(starving, config), 10f, config), 0f, "Starvation hurts.");
        }

        [Test]
        public void TreeLife_Leaves_FallInTheCold_TurnFirst_AndReturnGreenInSpring()
        {
            float leaf = 1f;
            float autumn = 0f;

            // Cooling toward the drop temperature: the leaves colour first...
            for (float t = 20f; t >= config.treeBudTemp; t -= 0.5f)
            {
                TreeLife.UpdateLeaves(ref leaf, ref autumn, t, 5f, config);
            }

            Assert.Greater(autumn, 0.8f, "Leaves should have turned by the time it is this cool.");
            Assert.Greater(leaf, 0.5f, "...but most are still on the tree.");

            // ...then fall in the cold.
            for (int i = 0; i < 100; i++)
            {
                TreeLife.UpdateLeaves(ref leaf, ref autumn, config.treeLeafDropTemp - 5f, 5f, config);
            }

            Assert.AreEqual(0f, leaf, 1e-4f, "A bare winter tree.");

            // Spring: new leaves are green.
            for (int i = 0; i < 100; i++)
            {
                TreeLife.UpdateLeaves(ref leaf, ref autumn, config.treeBudTemp + 8f, 5f, config);
            }

            Assert.AreEqual(1f, leaf, 1e-4f, "In full leaf again.");
            Assert.AreEqual(0f, autumn, 1e-4f, "New leaves have no autumn colour.");
        }

        [Test]
        public void TreeLife_Seeds_OnlyFromAHealthyMatureLeafyGrowingTree()
        {
            float clock = 0f;
            Assert.AreEqual(0, TreeLife.SeedsDue(ref clock, mature: false, health: 1f, leafAmount: 1f, growthRate: 1f, deltaSeconds: 1000f, config), "A young tree makes no seeds.");
            Assert.AreEqual(0, TreeLife.SeedsDue(ref clock, true, 0.3f, 1f, 1f, 1000f, config), "A sick tree makes none.");
            Assert.AreEqual(0, TreeLife.SeedsDue(ref clock, true, 1f, 0.1f, 1f, 1000f, config), "A bare tree makes none.");
            Assert.AreEqual(0, TreeLife.SeedsDue(ref clock, true, 1f, 1f, 0.1f, 1000f, config), "A starved tree makes none.");

            int seeds = TreeLife.SeedsDue(ref clock, true, 1f, 1f, 1f, config.treeSeedInterval * 3.5f, config);
            Assert.AreEqual(3, seeds);
            Assert.AreEqual(config.treeSeedInterval * 0.5f, clock, 1e-3f, "The leftover time carries over.");
        }

        [Test]
        public void TreeLife_Germination_IsRare_AndBetterInBetterConditions()
        {
            float ideal = TreeLife.GerminationChance(Good, config);
            TreeEnvironment poor = Good;
            poor.Nutrients = 0.02f;

            Assert.LessOrEqual(ideal, config.treeGerminationChance + 1e-6f);
            Assert.Less(ideal, 0.5f, "Even in ideal conditions most seeds fail.");
            Assert.Greater(ideal, TreeLife.GerminationChance(poor, config));
            Assert.AreEqual(0f, TreeLife.GerminationChance(new TreeEnvironment { Temperature = -20f, Moisture = 1f, Nutrients = 1f }, config));
        }
    }
}
