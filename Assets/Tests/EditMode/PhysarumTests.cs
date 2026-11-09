// =============================================================================
// PhysarumTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks the physarum agents and the nutrient transport:
//           - agents stay exactly on the sphere with a tangent heading, also as
//             they cross cube-face seams, and start on dry land,
//           - the trail grows where agents walk and fades when they are gone,
//           - transport conserves nutrients (including across seams), and is
//             much faster along trails,
//           - decomposition turns organic matter into nutrients one-for-one,
//           - the population neither dies out nor fills the pool.
// =============================================================================

using NUnit.Framework;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class PhysarumTests
    {
        private const int Resolution = 64;
        private const float Step = 1f / 60f;

        [Test]
        public void Physarum_StartAgents_AreScatteredOnDryLand()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: false);
                var physarum = world.CreatePhysarum(8192);
                physarum.SeedAgents(world.Terrain.Current, seed: 1, startAgents: 4000);

                uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
                Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);
                float[] terrain = world.Read(world.Terrain.Current);

                int count = 0;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] != 1u)
                    {
                        continue;
                    }

                    count++;
                    Texel texel = CubeSphere.DirToTexel(bodies[i], Resolution);
                    Assert.Greater(terrain[world.Index(texel.Face, texel.X, texel.Y)], 0f, $"Agent {i} did not start on land.");
                }

                Assert.Greater(count, 3800, "Nearly all of the requested start agents should be placed.");
                Assert.LessOrEqual(count, 4000);
            }
        }

        [Test]
        public void Physarum_Agents_StayOnTheSphere_WithTangentHeadings_AndCrossEveryFace()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.9f, spinUp: false);   // mostly land: agents roam everywhere
                var physarum = world.CreatePhysarum(8192);
                physarum.SeedAgents(world.Terrain.Current, seed: 2, startAgents: 3000);

                var facesVisited = new bool[CubeSphere.FaceCount];
                for (int round = 0; round < 8; round++)
                {
                    world.RunPhysarum(physarum, 100, Step);

                    uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
                    Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);
                    Vector4[] headings = WorldHarness.ReadBuffer<Vector4>(physarum.Heading);

                    for (int i = 0; i < alive.Length; i++)
                    {
                        if (alive[i] == 0u)
                        {
                            continue;
                        }

                        Vector3 position = bodies[i];
                        Vector3 heading = headings[i];
                        Assert.IsFalse(float.IsNaN(position.x) || float.IsNaN(heading.x), $"Agent {i} has NaN.");
                        Assert.AreEqual(1f, position.magnitude, 1e-4f, $"Agent {i} left the unit sphere.");
                        Assert.AreEqual(1f, heading.magnitude, 1e-3f, $"Agent {i} heading is not unit length.");
                        Assert.AreEqual(0f, Vector3.Dot(position, heading), 1e-3f, $"Agent {i} heading is not tangent to the sphere.");
                        facesVisited[CubeSphere.FaceOf(position)] = true;
                    }
                }

                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    Assert.IsTrue(facesVisited[face], $"No agent was ever on face {face}.");
                }
            }
        }

        [Test]
        public void Physarum_Trail_GrowsWhereAgentsWalk()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: false);
                var physarum = world.CreatePhysarum(8192);
                physarum.SeedAgents(world.Terrain.Current, seed: 4, startAgents: 2000);

                world.RunPhysarum(physarum, 120, Step);
                float[] trail = world.Read(world.Trail.Current);

                float max = 0f;
                double sum = 0.0;
                foreach (float value in trail)
                {
                    Assert.IsFalse(float.IsNaN(value));
                    max = Mathf.Max(max, value);
                    sum += value;
                }

                Assert.Greater(max, 0.3f, "Agents should have laid a visible trail.");
                Assert.Greater(sum, 50.0, "There should be trail overall.");
            }
        }

        [Test]
        public void Physarum_Agents_FindAndGatherAtNutrientPatches()
        {
            // Bare land (no weathering is stepped) with two nutrient patches painted on, as the brush tool will.
            // Agents are scattered everywhere; the ones that find food live and multiply, the rest starve, so the
            // population ends up concentrated on the patches. Nothing tells them where the patches are.
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.97f, spinUp: false);
                var physarum = world.CreatePhysarum(30_000);
                physarum.SeedAgents(world.Terrain.Current, seed: 5, startAgents: 6000);

                Vector3 patchA = new Vector3(1f, 0f, 0f);
                Vector3 patchB = new Vector3(0.6f, 0.8f, 0f);
                var brush = new FieldBrush(AssetDatabase_Load("Brush"));
                brush.Apply(world.Soil.Current, patchA, 0.15f, new Vector4(4f, 0f, 0f, 0f), 0f, 50f);
                brush.Apply(world.Soil.Current, patchB, 0.15f, new Vector4(4f, 0f, 0f, 0f), 0f, 50f);

                world.RunPhysarum(physarum, 1800, Step);   // 30 sim seconds

                uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
                Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);

                int total = 0;
                int near = 0;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] == 0u)
                    {
                        continue;
                    }

                    total++;
                    Vector3 position = bodies[i];
                    float angleA = Mathf.Acos(Mathf.Clamp(Vector3.Dot(position, patchA.normalized), -1f, 1f));
                    float angleB = Mathf.Acos(Mathf.Clamp(Vector3.Dot(position, patchB.normalized), -1f, 1f));
                    if (angleA < 0.35f || angleB < 0.35f)
                    {
                        near++;
                    }
                }

                // Two caps of radius 0.35 rad cover about 6% of the sphere, so a population spread at random
                // would have about 6% near the patches.
                float share = total > 0 ? (float)near / total : 0f;
                Debug.Log($"Physarum gathering: {near} of {total} agents within 0.35 rad of a nutrient patch ({share:P0}; random spread would be about 6%).");
                Assert.Greater(total, 100, "The population died out.");
                Assert.Greater(share, 0.5f, "Agents should gather where the nutrients are.");
            }
        }

        [Test]
        public void Physarum_Trail_FadesAwayWithNoAgents()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: false);
                var physarum = world.CreatePhysarum(1024);
                physarum.SeedAgents(world.Terrain.Current, seed: 4, startAgents: 0);

                new FieldBrush(AssetDatabase_Load("Brush")).Apply(world.Trail.Current, Vector3.right, 0.3f, new Vector4(1f, 0f, 0f, 0f), 0f, 10f);
                float before = Max(world.Read(world.Trail.Current));
                Assert.Greater(before, 0.9f);

                world.RunPhysarum(physarum, 300, Step);   // 5 seconds
                float after = Max(world.Read(world.Trail.Current));

                // Decay alone leaves exp(-0.9 * 5) = 1%; blurring lowers the peak further.
                Assert.Less(after, 0.03f, "With no agents the trail must fade away.");
            }
        }

        [Test]
        public void Nutrients_Transport_ConservesTheTotalOnLand_IncludingAcrossSeams()
        {
            using (var world = new WorldHarness(Resolution, config => config.physarumTrailDecay = 0f))
            {
                world.Generate(seed: 3, landFraction: 0.95f, spinUp: false);   // nearly all land, so blobs spread over seams
                var physarum = world.CreatePhysarum(1024);
                physarum.SeedAgents(world.Terrain.Current, seed: 1, startAgents: 0);

                var brush = new FieldBrush(AssetDatabase_Load("Brush"));
                brush.Apply(world.Soil.Current, new Vector3(1f, 1f, 1f), 0.12f, new Vector4(4f, 0f, 0f, 0f), 0f, 50f);   // a cube corner
                brush.Apply(world.Soil.Current, new Vector3(-1f, 1f, 0f), 0.12f, new Vector4(8f, 0f, 0f, 0f), 0f, 50f);   // an edge
                brush.Apply(world.Trail.Current, Vector3.up, Mathf.PI, new Vector4(1000f, 0f, 0f, 0f), 0f, 2f);   // uniform trail speeds spreading up

                float before = LandTotal(world, world.Read(world.Soil.Current), 0);
                float peakBefore = Max(world.Read(world.Soil.Current));
                world.RunPhysarum(physarum, 600, Step);
                float[] soilAfter = world.Read(world.Soil.Current);
                float after = LandTotal(world, soilAfter, 0);

                Assert.Greater(before, 100f);
                Assert.AreEqual(before, after, before * 0.01f, "Transport must not create or destroy nutrients (1% allowed for the seams).");
                Assert.Less(Max(soilAfter), peakBefore * 0.8f, "The blobs should have spread out.");
            }
        }

        [Test]
        public void Nutrients_Transport_IsMuchFasterAlongATrail()
        {
            float withoutTrail = PeakAfterSpreading(trailValue: 0f);
            float withTrail = PeakAfterSpreading(trailValue: 2f);

            Assert.Less(withTrail, withoutTrail * 0.8f, $"Peak with trail {withTrail:0.000}, without {withoutTrail:0.000}: the network should spread nutrients much faster.");
        }

        private static float PeakAfterSpreading(float trailValue)
        {
            using (var world = new WorldHarness(Resolution, config => config.physarumTrailDecay = 0f))
            {
                world.Generate(seed: 3, landFraction: 0.95f, spinUp: false);
                var physarum = world.CreatePhysarum(1024);
                physarum.SeedAgents(world.Terrain.Current, seed: 1, startAgents: 0);

                var brush = new FieldBrush(AssetDatabase_Load("Brush"));
                brush.Apply(world.Soil.Current, new Vector3(0.3f, 1f, 0.2f), 0.15f, new Vector4(5f, 0f, 0f, 0f), 0f, 50f);
                if (trailValue > 0f)
                {
                    // Uniform trail everywhere (weight > 0.002 anywhere except the antipode, clamped to the value).
                    brush.Apply(world.Trail.Current, Vector3.up, Mathf.PI, new Vector4(1000f, 0f, 0f, 0f), 0f, trailValue);
                }

                world.RunPhysarum(physarum, 600, Step);
                return Max(world.Read(world.Soil.Current));
            }
        }

        [Test]
        public void Nutrients_Decomposition_TurnsOrganicMatterIntoNutrientsOneForOne()
        {
            using (var world = new WorldHarness(Resolution, config => config.decompositionRate = 0.2f))
            {
                world.Generate(seed: 3, landFraction: 0.95f, spinUp: true);   // warm, moist land
                var physarum = world.CreatePhysarum(1024);
                physarum.SeedAgents(world.Terrain.Current, seed: 1, startAgents: 0);

                // Put organic matter (channel G) on the land, then look at nutrients + organic together.
                new FieldBrush(AssetDatabase_Load("Brush")).Apply(world.Soil.Current, Vector3.right, 0.8f, new Vector4(0f, 3f, 0f, 0f), 0f, 50f);
                float[] before = world.Read(world.Soil.Current);
                float massBefore = LandTotal(world, before, 0) + LandTotal(world, before, 1);
                float organicBefore = LandTotal(world, before, 1);

                world.RunPhysarum(physarum, 900, Step);
                float[] after = world.Read(world.Soil.Current);
                float massAfter = LandTotal(world, after, 0) + LandTotal(world, after, 1);
                float organicAfter = LandTotal(world, after, 1);

                Assert.Less(organicAfter, organicBefore * 0.9f, "Organic matter should decompose.");
                Assert.AreEqual(massBefore, massAfter, massBefore * 0.01f, "Decomposition must turn organic matter into nutrients, not lose it.");
            }
        }

        [Test]
        public void Physarum_Population_NeitherDiesOutNorFillsThePool()
        {
            // With the default config the population should rise from its start, overshoot as it eats through
            // the soil's stock, then settle back to what the weathering income can feed: a damped boom and
            // bust. The pool is deliberately larger than the overshoot, so reaching it would mean food is
            // not limiting.
            const int pool = 30_000;
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(pool);
                physarum.SeedAgents(world.Terrain.Current, seed: 1, startAgents: 3000);

                // Climate (so weathering keeps feeding the soil) and agents advance together, in rounds.
                const int stepsPerRound = 300;
                const int rounds = 40;   // 200 sim seconds
                double time = 0.0;
                var alive = new uint[rounds];
                for (int round = 0; round < rounds; round++)
                {
                    world.Run(time, time + stepsPerRound * Step, Step);
                    world.RunPhysarum(physarum, stepsPerRound, Step);
                    time += stepsPerRound * Step;
                    alive[round] = WorldHarness.ReadBuffer<uint>(physarum.Counters)[0];
                }

                Debug.Log("POPULATION (every " + stepsPerRound * Step + " s): " + string.Join(" ", alive));

                // After the initial rise the population must sit well inside the pool and above extinction.
                for (int round = rounds / 2; round < rounds; round++)
                {
                    Assert.Greater(alive[round], 200u, $"The population died out by round {round}.");
                    Assert.Less(alive[round], pool * 0.9f, $"The population filled the pool at round {round}: it is not limited by food.");
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static ComputeShader AssetDatabase_Load(string name)
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>($"Assets/Shaders/Compute/{name}.compute");
        }

        private static float Max(float[] data)
        {
            float max = float.MinValue;
            foreach (float value in data)
            {
                max = Mathf.Max(max, value);
            }

            return max;
        }

        // Sum of one channel of a 4-channel soil array over land texels only.
        private static float LandTotal(WorldHarness world, float[] soil, int channel)
        {
            float[] terrain = world.Read(world.Terrain.Current);
            double total = 0.0;
            world.ForEachTexel((face, x, y, direction) =>
            {
                int i = world.Index(face, x, y);
                if (terrain[i] >= 0f)
                {
                    total += soil[i + channel];
                }
            });

            return (float)total;
        }
    }
}
