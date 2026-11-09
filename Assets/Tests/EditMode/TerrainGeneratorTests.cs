// =============================================================================
// TerrainGeneratorTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks terrain generation: the requested land fraction is met, a seed
//           reproduces the same world while another seed makes a different one,
//           heights stay in range with no NaN, the coastline has no break along
//           cube-face seams, and the terrain is not trivially flat.
// =============================================================================

using NUnit.Framework;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class TerrainGeneratorTests
    {
        private const int Resolution = 64;

        [TestCase(0.25f)]
        [TestCase(0.38f)]
        [TestCase(0.60f)]
        public void Terrain_LandFraction_MatchesTheRequest(float requested)
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 42, landFraction: requested, spinUp: false);
                float[] terrain = world.Read(world.Terrain.Current);

                int land = 0;
                int total = 0;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    total++;
                    if (terrain[world.Index(face, x, y)] > 0f)
                    {
                        land++;
                    }
                });

                Assert.AreEqual(requested, (float)land / total, 0.03f);
            }
        }

        [Test]
        public void Terrain_HeightsStayInRange_WithNoNaN_AndHaveRealRelief()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 7, landFraction: 0.4f, spinUp: false);
                float[] terrain = world.Read(world.Terrain.Current);

                float min = float.MaxValue;
                float max = float.MinValue;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int i = world.Index(face, x, y);
                    Assert.IsFalse(float.IsNaN(terrain[i]) || float.IsInfinity(terrain[i]), "Height is not a number.");
                    Assert.That(terrain[i + 1], Is.InRange(0f, 1f), "Rock hardness out of range.");
                    min = Mathf.Min(min, terrain[i]);
                    max = Mathf.Max(max, terrain[i]);
                });

                Assert.GreaterOrEqual(min, -1f);
                Assert.LessOrEqual(max, 1f);
                Assert.Less(min, -0.3f, "There should be deep ocean.");
                Assert.Greater(max, 0.5f, "There should be high mountains.");
            }
        }

        [Test]
        public void Terrain_SameSeed_GivesTheSameWorld_AndAnotherSeedADifferentOne()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 5, landFraction: 0.4f, spinUp: false);
                float[] first = world.Read(world.Terrain.Current);

                world.Generate(seed: 5, landFraction: 0.4f, spinUp: false);
                float[] again = world.Read(world.Terrain.Current);

                world.Generate(seed: 6, landFraction: 0.4f, spinUp: false);
                float[] other = world.Read(world.Terrain.Current);

                float sameDifference = 0f;
                float otherDifference = 0f;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int i = world.Index(face, x, y);
                    sameDifference += Mathf.Abs(first[i] - again[i]);
                    otherDifference += Mathf.Abs(first[i] - other[i]);
                });

                Assert.AreEqual(0f, sameDifference, 1e-4f, "The same seed must reproduce the same terrain.");
                Assert.Greater(otherDifference / (6 * Resolution * Resolution), 0.1f, "A different seed must make different terrain.");
            }
        }

        [Test]
        public void Terrain_HasNoBreakAcrossFaceSeams()
        {
            // Heights of texels across a seam should differ by about as much as heights of texels inside a
            // face. A broken seam would show as a much larger typical jump.
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 11, landFraction: 0.4f, spinUp: false);
                float[] terrain = world.Read(world.Terrain.Current);

                double seamJump = 0.0;
                int seamPairs = 0;
                double interiorJump = 0.0;
                int interiorPairs = 0;

                world.ForEachTexel((face, x, y, direction) =>
                {
                    Texel next = CubeSphere.NeighbourTexel(face, x, y, 1, 0, Resolution);
                    float jump = Mathf.Abs(terrain[world.Index(face, x, y)] - terrain[world.Index(next.Face, next.X, next.Y)]);

                    if (next.Face != face)
                    {
                        seamJump += jump;
                        seamPairs++;
                    }
                    else
                    {
                        interiorJump += jump;
                        interiorPairs++;
                    }
                });

                Assert.Greater(seamPairs, 0);
                double seamMean = seamJump / seamPairs;
                double interiorMean = interiorJump / interiorPairs;
                Assert.Less(seamMean, interiorMean * 2.0, $"Seam jump {seamMean:0.0000} vs interior {interiorMean:0.0000}.");
            }
        }
    }
}
