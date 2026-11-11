// =============================================================================
// TreeGenesTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the tree genes and the growth animation data:
//           - the plain (default) genes behave exactly as the game did before genes,
//           - a seed's genes are its parent's plus a small change, always in range,
//           - cold-adapted trees do better in the cold and warm-adapted trees in the warmth
//             (so the climate can select different "species" in different places),
//           - an evergreen keeps its leaves in the cold where a deciduous tree loses them,
//           - a cone crown is narrower at the top than a dome crown,
//           - every node and mesh vertex carries the birth time and grow time the wood
//             shader uses to animate growth.
// =============================================================================

using NUnit.Framework;
using Terrarium.Trees;
using UnityEngine;
using Tree = Terrarium.Trees.Tree;

namespace Terrarium.Tests
{
    public sealed class TreeGenesTests
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

        private static TreeEnvironment Environment(float temperature, float moisture = 0.8f)
        {
            return new TreeEnvironment { Temperature = temperature, Moisture = moisture, Nutrients = 0.5f, Trail = 0f, Crowding = 0f };
        }

        [Test]
        public void PlainGenes_BehaveExactlyLikeNoGenes()
        {
            TreeGenes plain = TreeGenes.Default(config);
            foreach (float temperature in new[] { -5f, 3f, 12f, 22f, 35f, 45f })
            {
                TreeEnvironment env = Environment(temperature);
                Assert.AreEqual(TreeLife.GrowthRate(env, config), TreeLife.GrowthRate(env, plain, config), 1e-6f);
                Assert.AreEqual(TreeLife.HealthChange(env, 0.5f, 1f, config), TreeLife.HealthChange(env, plain, 0.5f, 1f, config), 1e-6f);
            }

            float leafA = 0.5f, autumnA = 0f, leafB = 0.5f, autumnB = 0f;
            TreeLife.UpdateLeaves(ref leafA, ref autumnA, 8f, 3f, config);
            TreeLife.UpdateLeaves(ref leafB, ref autumnB, 8f, 3f, plain, config);
            Assert.AreEqual(leafA, leafB, 1e-6f);
            Assert.AreEqual(autumnA, autumnB, 1e-6f);
        }

        [Test]
        public void Child_StaysInRange_ChangesLittle_AndZeroMutationCopiesTheParent()
        {
            var rng = new Rng(7);
            TreeGenes parent = TreeGenes.Random(ref rng, config);

            config.treeMutation = 0f;
            TreeGenes copy = parent.Child(ref rng, config);
            Assert.AreEqual(parent.OptimumTemperature, copy.OptimumTemperature, 1e-6f);
            Assert.AreEqual(parent.Cone, copy.Cone, 1e-6f);
            Assert.AreEqual(parent.Evergreen, copy.Evergreen, 1e-6f);

            config.treeMutation = 0.06f;
            TreeGenes lineage = parent;
            for (int generation = 0; generation < 500; generation++)
            {
                TreeGenes next = lineage.Child(ref rng, config);
                Assert.That(next.OptimumTemperature, Is.InRange(TreeGenes.OptimumMin, TreeGenes.OptimumMax));
                Assert.That(next.Cone, Is.InRange(0f, 1f));
                Assert.That(next.Evergreen, Is.InRange(0f, 1f));
                Assert.That(next.Height, Is.InRange(TreeGenes.HeightMin, TreeGenes.HeightMax));
                Assert.That(next.LeafSize, Is.InRange(TreeGenes.LeafSizeMin, TreeGenes.LeafSizeMax));
                Assert.LessOrEqual(Mathf.Abs(next.OptimumTemperature - lineage.OptimumTemperature), 0.06f * (TreeGenes.OptimumMax - TreeGenes.OptimumMin) + 1e-4f,
                    "One generation changes a gene by at most the mutation share of its range.");
                lineage = next;
            }
        }

        [Test]
        public void ColdAdaptedTrees_WinInTheCold_AndWarmAdaptedTrees_InTheWarmth()
        {
            TreeGenes cold = TreeGenes.Default(config);
            cold.OptimumTemperature = 8f;
            TreeGenes warm = TreeGenes.Default(config);
            warm.OptimumTemperature = 26f;

            Assert.Greater(TreeLife.GrowthRate(Environment(8f), cold, config), TreeLife.GrowthRate(Environment(8f), warm, config) + 0.1f,
                "In cool ground the cold-adapted tree grows much better.");
            Assert.Greater(TreeLife.GrowthRate(Environment(26f), warm, config), TreeLife.GrowthRate(Environment(26f), cold, config) + 0.1f,
                "In warm ground the warm-adapted tree grows much better.");
            Assert.Greater(TreeLife.GerminationChance(Environment(8f), cold, config), TreeLife.GerminationChance(Environment(8f), warm, config),
                "Selection starts at germination: the seed that suits the place takes root.");
        }

        [Test]
        public void Evergreen_KeepsItsLeavesInTheCold_WhereADeciduousTreeLosesThem()
        {
            TreeGenes deciduous = TreeGenes.Default(config);
            TreeGenes evergreen = TreeGenes.Default(config);
            evergreen.Evergreen = 1f;

            float leafD = 1f, autumnD = 0f, leafE = 1f, autumnE = 0f;
            for (int i = 0; i < 400; i++)
            {
                TreeLife.UpdateLeaves(ref leafD, ref autumnD, 2f, 1f, deciduous, config);
                TreeLife.UpdateLeaves(ref leafE, ref autumnE, 2f, 1f, evergreen, config);
            }

            Assert.Less(leafD, 0.05f, "A deciduous tree is bare at 2 C.");
            Assert.Greater(leafE, 0.9f, "An evergreen is still in leaf at 2 C.");
            Assert.Less(autumnE, 0.05f, "An evergreen does not turn autumn colours.");
        }

        [Test]
        public void CrownProfile_IsADomeAtZeroCone_AndANarrowingConeAtOne()
        {
            TreeGenes dome = TreeGenes.Default(config);
            TreeGenes cone = TreeGenes.Default(config);
            cone.Cone = 1f;

            Assert.Greater(dome.CrownProfile(0.5f), dome.CrownProfile(0.1f), "A dome is widest in the middle.");
            Assert.Greater(dome.CrownProfile(0.5f), dome.CrownProfile(0.9f));
            Assert.Greater(cone.CrownProfile(0.1f), cone.CrownProfile(0.5f), "A cone narrows steadily toward the top.");
            Assert.Greater(cone.CrownProfile(0.5f), cone.CrownProfile(0.9f));
        }

        private SpaceColonization.Settings TreeSettings()
        {
            return new SpaceColonization.Settings
            {
                SegmentLength = 0.01f, InfluenceRadius = 0.05f, KillRadius = 0.012f, Gravitropism = 0.1f, Phototropism = 0f,
                Wobble = 0.1f, MaxNodes = 2000, TipRadius = 0.0008f, PipeExponent = 2.5f,
                SegmentGrowSeconds = 5f, TrunkGrowSeconds = 12f,
            };
        }

        private Tree GrownTree(float cone)
        {
            var rng = new Rng(11);
            TreeGenes genes = TreeGenes.Default(config);
            genes.Cone = cone;
            var tree = new Tree(0, 5, Vector3.up, 1f, 0.12f, TreeSettings(), config, ref rng, genes, 0f);
            tree.Richness = 0.9f;
            for (int i = 0; i < 60; i++)
            {
                tree.GrowStep(Vector3.zero, 0f);
            }

            return tree;
        }

        // Widest horizontal reach of the nodes in a band of heights, as a share of the tree's height.
        private static float Reach(Tree tree, float fromFraction, float toFraction)
        {
            float top = 0f;
            for (int i = 0; i < tree.Colony.NodeCount; i++)
            {
                top = Mathf.Max(top, tree.Colony.PositionOf(i).y);
            }

            float widest = 0f;
            for (int i = 0; i < tree.Colony.NodeCount; i++)
            {
                Vector3 p = tree.Colony.PositionOf(i);
                if (p.y >= fromFraction * top && p.y <= toFraction * top)
                {
                    widest = Mathf.Max(widest, new Vector2(p.x, p.z).magnitude);
                }
            }

            return widest / Mathf.Max(top, 1e-4f);
        }

        [Test]
        public void ConeTree_IsNarrowerAtTheTopThanADomeTree()
        {
            Tree dome = GrownTree(0f);
            Tree cone = GrownTree(1f);

            float domeTop = Reach(dome, 0.75f, 1f);
            float coneTop = Reach(cone, 0.75f, 1f);
            Assert.Greater(dome.Colony.NodeCount, 20);
            Assert.Greater(cone.Colony.NodeCount, 20);
            Assert.Less(coneTop, domeTop, $"The top of a cone ({coneTop:0.000}) should be narrower than the top of a dome ({domeTop:0.000}).");

            Object.DestroyImmediate(dome.Mesh);
            Object.DestroyImmediate(cone.Mesh);
        }

        [Test]
        public void Nodes_CarryBirthTimeAndGrowSeconds_TrunkSlowerThanBranches()
        {
            var colony = new SpaceColonization(3, TreeSettings());
            colony.CurrentTime = 10f;
            colony.AddTrunk(0.05f);
            Assert.AreEqual(10f, colony.BirthOf(1), 1e-6f);
            Assert.AreEqual(12f, colony.GrowSecondsOf(1), 1e-6f, "The trunk takes the trunk grow time.");

            colony.CurrentTime = 40f;
            colony.AddAttractors(new Vector3(0f, 0.12f, 0f), new Vector3(0.05f, 0.05f, 0.05f), 40);
            int before = colony.NodeCount;
            Assert.Greater(colony.Grow(Vector3.up, Vector3.zero), 0);
            Assert.AreEqual(40f, colony.BirthOf(before), 1e-6f, "A new branch is stamped with the time it grew.");
            Assert.AreEqual(5f, colony.GrowSecondsOf(before), 1e-6f, "A new branch takes the segment grow time.");
        }

        [Test]
        public void Mesh_CarriesGrowthData_OnlyTheChildEndRingGrows()
        {
            var colony = new SpaceColonization(3, TreeSettings());
            colony.CurrentTime = 7f;
            colony.AddTrunk(0.04f);
            colony.ComputeRadii();

            const int sides = 5;
            var mesh = new Mesh();
            int vertices = new TreeMesher().Build(colony, sides, mesh);

            var a = new System.Collections.Generic.List<Vector3>();
            var b = new System.Collections.Generic.List<Vector3>();
            mesh.GetUVs(0, a);
            mesh.GetUVs(1, b);
            Assert.AreEqual(vertices, a.Count);
            Assert.AreEqual(vertices, b.Count);

            // First segment: its first `sides` vertices are the parent-end ring (does not grow), the next `sides` the child end (grows).
            Assert.AreEqual(0f, a[0].z);
            Assert.AreEqual(1f, a[sides].z);
            Assert.AreEqual(7f, a[sides].x, 1e-6f);
            Assert.AreEqual(colony.PositionOf(0).y, b[sides].y, 1e-6f, "The growing ring slides out from the segment's start point.");

            Object.DestroyImmediate(mesh);
        }
    }
}
