// =============================================================================
// SpaceColonizationTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the tree-growing algorithm: the same seed gives the same tree
//           and another seed a different one; growth always finishes; the result
//           is a proper tree (every branch hangs off its parent, segments have the
//           right length); the pipe-model radii obey r_parent^e = sum r_child^e;
//           the tropisms bend the tree the way they should (toward the lamp and
//           up); fewer attractors give a sparser tree; and the grid acceleration
//           does not miss nodes (no attractor is left within reach of a node, and
//           none is left within kill distance of one).
// =============================================================================

using NUnit.Framework;
using Terrarium.Trees;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class SpaceColonizationTests
    {
        private static SpaceColonization.Settings DefaultSettings(float gravitropism = 0.1f, float phototropism = 0f)
        {
            return new SpaceColonization.Settings
            {
                SegmentLength = 0.05f,
                InfluenceRadius = 0.3f,
                KillRadius = 0.08f,
                Gravitropism = gravitropism,
                Phototropism = phototropism,
                Wobble = 0.1f,
                MaxNodes = 1500,
                TipRadius = 0.004f,
                PipeExponent = 2.5f,
            };
        }

        // A tree of height ~1: trunk to 0.3, an ellipsoid crown above it.
        private static SpaceColonization Grown(int seed, SpaceColonization.Settings settings, int attractors = 300, int iterations = 200, Vector3? toLight = null)
        {
            var tree = new SpaceColonization(seed, settings);
            tree.AddTrunk(0.3f);
            tree.AddAttractors(new Vector3(0f, 0.65f, 0f), new Vector3(0.35f, 0.35f, 0.35f), attractors);

            for (int i = 0; i < iterations; i++)
            {
                if (tree.Grow(Vector3.up, toLight ?? Vector3.up) == 0)
                {
                    break;
                }
            }

            tree.ComputeRadii();
            return tree;
        }

        [Test]
        public void SpaceColonization_SameSeed_GivesTheSameTree()
        {
            SpaceColonization a = Grown(11, DefaultSettings());
            SpaceColonization b = Grown(11, DefaultSettings());

            Assert.AreEqual(a.NodeCount, b.NodeCount);
            for (int i = 0; i < a.NodeCount; i++)
            {
                Assert.AreEqual(a.PositionOf(i), b.PositionOf(i), $"Node {i} differs.");
                Assert.AreEqual(a.ParentOf(i), b.ParentOf(i));
            }
        }

        [Test]
        public void SpaceColonization_DifferentSeeds_GiveDifferentTrees()
        {
            SpaceColonization a = Grown(11, DefaultSettings());
            SpaceColonization b = Grown(12, DefaultSettings());

            bool same = a.NodeCount == b.NodeCount;
            for (int i = 0; same && i < a.NodeCount; i++)
            {
                same = a.PositionOf(i) == b.PositionOf(i);
            }

            Assert.IsFalse(same, "Two seeds must not make an identical tree.");
        }

        [Test]
        public void SpaceColonization_Growth_AlwaysTerminates_WithinTheNodeLimit()
        {
            var tree = new SpaceColonization(3, DefaultSettings());
            tree.AddTrunk(0.3f);
            tree.AddAttractors(new Vector3(0f, 0.65f, 0f), new Vector3(0.35f, 0.35f, 0.35f), 400);

            int iterations = 0;
            while (tree.Grow(Vector3.up, Vector3.up) > 0)
            {
                iterations++;
                Assert.Less(iterations, 500, "Growth must stop: attractors are used up or nodes run out.");
            }

            Assert.LessOrEqual(tree.NodeCount, 1500);
            Assert.Less(tree.AttractorCount, 400, "Some attractors must have been reached.");
        }

        [Test]
        public void SpaceColonization_NodeLimit_IsRespected()
        {
            var settings = DefaultSettings();
            settings.MaxNodes = 40;
            var tree = new SpaceColonization(5, settings);
            tree.AddTrunk(0.3f);
            tree.AddAttractors(new Vector3(0f, 0.65f, 0f), new Vector3(0.35f, 0.35f, 0.35f), 400);

            for (int i = 0; i < 100; i++)
            {
                tree.Grow(Vector3.up, Vector3.up);
            }

            Assert.AreEqual(40, tree.NodeCount);
            Assert.IsTrue(tree.IsFull);
        }

        [Test]
        public void SpaceColonization_IsAProperTree_ParentsPrecedeChildren_AndSegmentsHaveTheRightLength()
        {
            SpaceColonization tree = Grown(7, DefaultSettings());

            Assert.AreEqual(-1, tree.ParentOf(0));
            Assert.AreEqual(Vector3.zero, tree.PositionOf(0));

            for (int i = 1; i < tree.NodeCount; i++)
            {
                int parent = tree.ParentOf(i);
                Assert.That(parent, Is.InRange(0, i - 1), $"Node {i}'s parent must come before it.");
                Assert.AreEqual(0.05f, Vector3.Distance(tree.PositionOf(i), tree.PositionOf(parent)), 1e-4f, $"Segment {i} has the wrong length.");
            }

            // Child counts agree with the parent links.
            var counted = new int[tree.NodeCount];
            for (int i = 1; i < tree.NodeCount; i++)
            {
                counted[tree.ParentOf(i)]++;
            }

            for (int i = 0; i < tree.NodeCount; i++)
            {
                Assert.AreEqual(counted[i], tree.ChildCountOf(i));
            }
        }

        [Test]
        public void SpaceColonization_Crown_FillsTheEnvelope_AndStaysAboveTheGround()
        {
            SpaceColonization tree = Grown(7, DefaultSettings());

            float highest = 0f;
            for (int i = 0; i < tree.NodeCount; i++)
            {
                Assert.GreaterOrEqual(tree.PositionOf(i).y, -1e-4f, "No branch may grow below the root.");
                highest = Mathf.Max(highest, tree.PositionOf(i).y);
            }

            Assert.Greater(highest, 0.8f, "The crown should reach most of the way up its envelope (top at y = 1.0).");
            Assert.Greater(tree.NodeCount, 60, "A full crown has many branches.");
        }

        [Test]
        public void SpaceColonization_PipeModel_ParentCrossSectionEqualsTheSumOfChildren()
        {
            SpaceColonization tree = Grown(7, DefaultSettings());
            const float exponent = 2.5f;

            for (int i = 0; i < tree.NodeCount; i++)
            {
                if (tree.ChildCountOf(i) == 0)
                {
                    Assert.AreEqual(0.004f, tree.RadiusOf(i), 1e-6f, "A tip has the tip radius.");
                    continue;
                }

                double sum = 0.0;
                for (int c = i + 1; c < tree.NodeCount; c++)
                {
                    if (tree.ParentOf(c) == i)
                    {
                        sum += Mathf.Pow(tree.RadiusOf(c), exponent);
                        Assert.GreaterOrEqual(tree.RadiusOf(i), tree.RadiusOf(c) - 1e-6f, "A branch is never thicker than the one it grows from.");
                    }
                }

                Assert.AreEqual(sum, Mathf.Pow(tree.RadiusOf(i), exponent), sum * 1e-3, $"Node {i} breaks the pipe model.");
            }

            Assert.Greater(tree.RadiusOf(0), tree.RadiusOf(tree.NodeCount - 1) * 3f, "The trunk must be much thicker than a twig.");
        }

        [Test]
        public void SpaceColonization_Phototropism_BendsGrowthTowardTheLamp()
        {
            // The force bends each new segment. Every attractor is eventually reached, so the FINISHED shape of a
            // symmetric cloud barely changes; the bend shows in how the branches get there. Look at early growth.
            var lamp = new Vector3(1f, 0f, 0f);

            float EarlyMeanX(float phototropism)
            {
                var tree = new SpaceColonization(21, DefaultSettings(phototropism: phototropism));
                tree.AddTrunk(0.3f);
                int trunkNodes = tree.NodeCount;
                tree.AddAttractors(new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 0.4f, 0.6f), 300);
                for (int i = 0; i < 6; i++)
                {
                    tree.Grow(Vector3.up, lamp);
                }

                float sum = 0f;
                for (int n = trunkNodes; n < tree.NodeCount; n++)
                {
                    sum += tree.PositionOf(n).x;
                }

                return sum / Mathf.Max(1, tree.NodeCount - trunkNodes);
            }

            float with = EarlyMeanX(0.8f);
            float without = EarlyMeanX(0f);
            Assert.Greater(with - without, 0.04f, $"With the light the early branches' mean x is {with:0.000}, without {without:0.000}.");
        }

        [Test]
        public void SpaceColonization_Gravitropism_ReachesHigher()
        {
            // A wide, flat crown so that upward pull is what decides the height.
            float Height(float gravitropism)
            {
                var tree = new SpaceColonization(9, DefaultSettings(gravitropism));
                tree.AddTrunk(0.3f);
                tree.AddAttractors(new Vector3(0f, 0.6f, 0f), new Vector3(0.7f, 0.5f, 0.7f), 500);
                for (int i = 0; i < 300 && tree.Grow(Vector3.up, Vector3.up) > 0; i++)
                {
                }

                float highest = 0f;
                for (int i = 0; i < tree.NodeCount; i++)
                {
                    highest = Mathf.Max(highest, tree.PositionOf(i).y);
                }

                return highest;
            }

            Assert.Greater(Height(0.6f), Height(0f), "Branches pulled toward the sky should reach higher.");
        }

        [Test]
        public void SpaceColonization_FewerAttractors_GiveASparserTree()
        {
            // Poor soil only supports a thin crown: fewer attractors must mean fewer branches.
            int rich = Grown(5, DefaultSettings(), attractors: 400).NodeCount;
            int poor = Grown(5, DefaultSettings(), attractors: 60).NodeCount;

            Assert.Greater(rich, poor * 1.4f, $"Rich ground made {rich} nodes, poor ground {poor}.");
        }

        [Test]
        public void SpaceColonization_GridSearch_MissesNothing()
        {
            var settings = DefaultSettings();
            var tree = new SpaceColonization(31, settings);
            tree.AddTrunk(0.3f);
            tree.AddAttractors(new Vector3(0f, 0.65f, 0f), new Vector3(0.5f, 0.35f, 0.5f), 500);

            while (tree.Grow(Vector3.up, Vector3.up) > 0)
            {
            }

            int tooClose = 0;
            int reachable = 0;
            for (int a = 0; a < tree.AttractorCount; a++)
            {
                float nearest = float.MaxValue;
                for (int n = 0; n < tree.NodeCount; n++)
                {
                    nearest = Mathf.Min(nearest, Vector3.Distance(tree.AttractorAt(a), tree.PositionOf(n)));
                }

                if (nearest < settings.KillRadius)
                {
                    tooClose++;
                }

                if (nearest < settings.InfluenceRadius * 0.98f)
                {
                    reachable++;
                }
            }

            Assert.AreEqual(0, tooClose, "No attractor within kill distance of a node may remain.");
            Assert.LessOrEqual(reachable, Mathf.Max(2, tree.AttractorCount / 20),
                "When growth stops, almost no remaining attractor should still have a node in reach.");
        }

        private static float MeanX(SpaceColonization tree)
        {
            float sum = 0f;
            for (int i = 0; i < tree.NodeCount; i++)
            {
                sum += tree.PositionOf(i).x;
            }

            return sum / tree.NodeCount;
        }
    }
}
