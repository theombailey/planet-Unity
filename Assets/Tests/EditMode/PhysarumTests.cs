// =============================================================================
// PhysarumTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks the people (physarum walkers), their roads, the towns and farms
//           that follow, and the nutrient transport:
//           - people stay exactly on the sphere with a tangent heading, also as
//             they cross cube-face seams, and start on livable land,
//           - the road grows where people walk and fades when they are gone,
//           - people stay on land and keep to ground that can be lived on,
//           - town density builds where people spend time and never at sea,
//           - farms grow in a ring round a town, not under it and not far from it,
//           - transport conserves nutrients (including across seams),
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
        public void People_StartOnLivableLand()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(8192);
                world.SeedPeople(physarum, seed: 1, count: 4000);

                uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
                Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);
                float[] terrain = world.Read(world.Terrain.Current);
                float[] climate = world.Read(world.Climate.Current);

                int count = 0;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] != 1u)
                    {
                        continue;
                    }

                    count++;
                    Texel texel = CubeSphere.DirToTexel(bodies[i], Resolution);
                    int index = world.Index(texel.Face, texel.X, texel.Y);
                    Assert.Greater(terrain[index], 0f, $"Person {i} did not start on land.");
                    Assert.Greater(climate[index], -2f, $"Person {i} started on frozen ground.");
                }

                Assert.Greater(count, 1000, "A good share of the requested people should find livable ground.");
                Assert.LessOrEqual(count, 4000);
            }
        }

        [Test]
        public void People_StayOnTheSphere_WithTangentHeadings_AndCrossEveryFace()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.9f, spinUp: true);   // mostly land: people roam everywhere
                var physarum = world.CreatePhysarum(8192);
                world.SeedPeople(physarum, seed: 2, count: 3000);

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
                        Assert.IsFalse(float.IsNaN(position.x) || float.IsNaN(heading.x), $"Person {i} has NaN.");
                        Assert.AreEqual(1f, position.magnitude, 1e-4f, $"Person {i} left the unit sphere.");
                        Assert.AreEqual(1f, heading.magnitude, 1e-3f, $"Person {i} heading is not unit length.");
                        Assert.AreEqual(0f, Vector3.Dot(position, heading), 1e-3f, $"Person {i} heading is not tangent to the sphere.");
                        facesVisited[CubeSphere.FaceOf(position)] = true;
                    }
                }

                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    Assert.IsTrue(facesVisited[face], $"Nobody was ever on face {face}.");
                }
            }
        }

        [Test]
        public void People_Road_GrowsWhereTheyWalk()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(8192);
                world.SeedPeople(physarum, seed: 4, count: 2000);

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

                Assert.Greater(max, 0.3f, "People should have worn a visible path.");
                Assert.Greater(sum, 50.0, "There should be path overall.");
            }
        }

        [Test]
        public void People_KeepToLand_AndToGroundThatCanBeLivedOn()
        {
            // Run for a while with nobody arriving. People who wander into the sea or onto frozen ground lose energy
            // and die, so those who are left should be on land, in a place that is neither frozen nor scorched.
            using (var world = new WorldHarness(Resolution, config => config.peopleArrivalRate = 0f))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(30_000);
                world.SeedPeople(physarum, seed: 5, count: 3000);

                world.RunPhysarum(physarum, 1800, Step);   // 30 sim seconds

                uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
                Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);
                float[] terrain = world.Read(world.Terrain.Current);
                float[] climate = world.Read(world.Climate.Current);

                int total = 0;
                int ashore = 0;
                int bearable = 0;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] == 0u)
                    {
                        continue;
                    }

                    total++;
                    Texel texel = CubeSphere.DirToTexel(bodies[i], Resolution);
                    int index = world.Index(texel.Face, texel.X, texel.Y);
                    if (terrain[index] >= 0f)
                    {
                        ashore++;
                    }

                    if (climate[index] > -5f && climate[index] < 45f)
                    {
                        bearable++;
                    }
                }

                Debug.Log($"People after 30 s: {total} alive, {ashore} ashore, {bearable} in bearable weather.");
                Assert.Greater(total, 100, "The population died out.");
                Assert.Greater(ashore, total * 0.95f, "People should stay on land.");
                Assert.Greater(bearable, total * 0.9f, "People should keep to ground that is neither frozen nor scorched.");
            }
        }

        [Test]
        public void People_RoadFadesAwayWithNobodyWalking()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: false);
                var physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 4, count: 0);

                new FieldBrush(AssetDatabase_Load("Brush")).Apply(world.Trail.Current, Vector3.right, 0.3f, new Vector4(1f, 0f, 0f, 0f), 0f, 10f);
                float before = Max(world.Read(world.Trail.Current));
                Assert.Greater(before, 0.9f);

                float seconds = 20f;
                world.RunPhysarum(physarum, (int)(seconds / Step), Step);
                float after = Max(world.Read(world.Trail.Current));

                // Decay alone leaves exp(-decay * 20 s); blurring lowers the peak further.
                float expected = Mathf.Exp(-world.Config.physarumTrailDecay * seconds);
                Assert.Less(after, expected + 0.02f, "With nobody walking the road must fade away.");
            }
        }

        // ------------------------------------------------------------------ towns and farms

        [Test]
        public void Towns_BuildWherePeopleSpendTime_AndNeverAtSea()
        {
            using (var world = new WorldHarness(Resolution, config => config.peopleArrivalRate = 0f))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(30_000);
                world.SeedPeople(physarum, seed: 6, count: 3000);

                world.RunPhysarum(physarum, 3600, Step);   // 60 sim seconds
                float[] land = world.Read(world.Land.Current);
                float[] terrain = world.Read(world.Terrain.Current);

                float peak = 0f;
                bool anyAtSea = false;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int cell = world.Index(face, x, y);
                    peak = Mathf.Max(peak, land[cell]);
                    if (terrain[cell] < 0f && (land[cell] != 0f || land[cell + 1] != 0f))
                    {
                        anyAtSea = true;
                    }
                });

                Debug.Log($"Peak town density after 60 s: {peak:0.000}");
                Assert.Greater(peak, world.Config.farmStart, "People who gather should leave a town behind.");
                Assert.IsFalse(anyAtSea, "Nothing is built at sea.");
            }
        }

        [Test]
        public void Towns_WithNobodyThere_StayEmpty()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 6, count: 0);

                world.RunPhysarum(physarum, 600, Step);
                float[] land = world.Read(world.Land.Current);

                Assert.AreEqual(0f, Max(land), 1e-6f, "No people, no towns, no farms.");
            }
        }

        [Test]
        public void Farms_GrowRoundATown_NotUnderIt_AndNotFarFromIt()
        {
            using (var world = new WorldHarness(Resolution, config =>
            {
                config.peopleArrivalRate = 0f;
                config.farmGrowth = 0.5f;       // fast, so the test can be short
                config.townDecay = 0.0f;        // keep the painted town exactly as painted
                config.townSpread = 0.0f;
            }))
            {
                world.Generate(seed: 3, landFraction: 0.97f, spinUp: true);   // almost all land, so livability is the only limit
                var physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 6, count: 0);

                // Put the town where the ground is best: the warmest, wettest land.
                float[] climateBefore = world.Read(world.Climate.Current);
                float[] terrainBefore = world.Read(world.Terrain.Current);
                Vector3 centre = Vector3.right;
                float bestScore = float.MinValue;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int cell = world.Index(face, x, y);
                    float score = terrainBefore[cell] > 0.05f && terrainBefore[cell] < 0.4f
                        ? Mathf.Clamp01(climateBefore[cell + 1]) - Mathf.Abs(climateBefore[cell] - 20f) * 0.03f
                        : float.MinValue;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        centre = direction;
                    }
                });

                // A town painted as three rings of density: a core, a halo, and nothing outside.
                var brush = new FieldBrush(AssetDatabase_Load("Brush"));
                brush.Apply(world.Land.Current, centre, 0.30f, new Vector4(0.55f, 0f, 0f, 0f), 0f, 50f);    // everywhere in the disc: the halo
                brush.Apply(world.Land.Current, centre, 0.12f, new Vector4(5f, 0f, 0f, 0f), 0f, 5.9f);       // the core

                world.RunPhysarum(physarum, 1200, Step);   // 20 sim seconds
                float[] land = world.Read(world.Land.Current);
                float[] terrain = world.Read(world.Terrain.Current);
                float[] climate = world.Read(world.Climate.Current);

                float coreFarm = 0f;
                float haloFarm = 0f;
                float farFarm = 0f;
                int haloLivable = 0;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int cell = world.Index(face, x, y);
                    if (terrain[cell] < 0f)
                    {
                        return;
                    }

                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f));
                    float farm = land[cell + 1];
                    if (angle < 0.04f)
                    {
                        coreFarm = Mathf.Max(coreFarm, farm);
                    }
                    else if (angle > 0.15f && angle < 0.26f)
                    {
                        haloFarm = Mathf.Max(haloFarm, farm);
                        if (climate[cell] > 12f && climate[cell + 1] > 0.35f)
                        {
                            haloLivable++;
                        }
                    }
                    else if (angle > 0.40f)
                    {
                        farFarm = Mathf.Max(farFarm, farm);
                    }
                });

                Debug.Log($"Farm strength: core {coreFarm:0.000}, halo {haloFarm:0.000} ({haloLivable} warm, wet texels), far {farFarm:0.000}");
                Assert.Less(coreFarm, 0.05f, "Land under the town is built over, not farmed.");
                Assert.Less(farFarm, 0.01f, "Land far from any town is not farmed.");
                Assert.Greater(haloLivable, 0, "The test needs some warm, wet ground in the halo.");
                Assert.Greater(haloFarm, 0.1f, "The good ground round the town should be farmed.");
            }
        }

        // ------------------------------------------------------------------ nutrients

        [Test]
        public void Nutrients_Transport_ConservesTheTotalOnLand_IncludingAcrossSeams()
        {
            using (var world = new WorldHarness(Resolution, config => config.nutrientDiffusion = 0.0002f))
            {
                world.Generate(seed: 3, landFraction: 0.95f, spinUp: false);   // nearly all land, so blobs spread over seams
                var physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 1, count: 0);

                var brush = new FieldBrush(AssetDatabase_Load("Brush"));
                brush.Apply(world.Soil.Current, new Vector3(1f, 1f, 1f), 0.12f, new Vector4(4f, 0f, 0f, 0f), 0f, 50f);   // a cube corner
                brush.Apply(world.Soil.Current, new Vector3(-1f, 1f, 0f), 0.12f, new Vector4(8f, 0f, 0f, 0f), 0f, 50f);   // an edge

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
        public void Nutrients_Decomposition_TurnsOrganicMatterIntoNutrientsOneForOne()
        {
            using (var world = new WorldHarness(Resolution, config => config.decompositionRate = 0.2f))
            {
                world.Generate(seed: 3, landFraction: 0.95f, spinUp: true);   // warm, moist land
                var physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 1, count: 0);

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

        // ------------------------------------------------------------------ population

        [Test]
        public void People_Population_NeitherDiesOutNorFillsThePool()
        {
            // With the default config the population should rise from its start, then settle at what the land can
            // feed: food limits it, not the pool. The pool is deliberately larger than the settled population, so
            // reaching it would mean food is not limiting.
            // Run at the field size and pool the game uses on its Medium setting (a field of 64 has too little land for
            // the density rules to mean the same thing).
            const int pool = 24_000;

            using (var world = new WorldHarness(128))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var physarum = world.CreatePhysarum(pool);
                world.SeedPeople(physarum, seed: 1, count: world.Config.physarumStartAgents);

                // Climate (so the seasons turn) and people advance together, in rounds.
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
                    Assert.Greater(alive[round], 100u, $"The population died out by round {round}.");
                    Assert.Less(alive[round], pool * 0.9f, $"The population filled the pool at round {round}: it is not limited by food.");
                }
            }
        }

        [Test]
        public void People_Report_PopulationTownsAndFarmsOverTime()
        {
            // A diagnostic, not a pass/fail check: prints how the settlement develops, for tuning.
            // Run it with:  run-tests People_Report   and read "PEOPLE REPORT" in the editor log.
            const int resolution = 128;
            const int pool = 24_000;
            foreach (int seed in new[] { 3, 7 })
            {
                using (var world = new WorldHarness(resolution))
                {
                    world.Generate(seed: seed, landFraction: 0.4f, spinUp: true);
                    var physarum = world.CreatePhysarum(pool);
                    world.SeedPeople(physarum, seed: 1, count: world.Config.physarumStartAgents);

                    const int stepsPerRound = 600;   // 10 sim seconds at 1/60
                    double time = 0.0;
                    var report = new System.Text.StringBuilder();
                    report.AppendLine($"PEOPLE REPORT seed {seed}, field {resolution}, pool {pool}");
                    for (int round = 0; round < 40; round++)
                    {
                        world.Run(time, time + stepsPerRound * Step, Step);
                        world.RunPhysarum(physarum, stepsPerRound, Step);
                        time += stepsPerRound * Step;

                        if (round % 3 == 2)
                        {
                            float[] land = world.Read(world.Land.Current);
                            float[] terrain = world.Read(world.Terrain.Current);
                            float peak = 0f;
                            int townTexels = 0;
                            int houseTexels = 0;
                            double farm = 0.0;
                            int landTexels = 0;
                            world.ForEachTexel((face, x, y, direction) =>
                            {
                                int cell = world.Index(face, x, y);
                                if (terrain[cell] < 0f)
                                {
                                    return;
                                }

                                landTexels++;
                                peak = Mathf.Max(peak, land[cell]);
                                farm += land[cell + 1];
                                if (land[cell] > 0.6f) { townTexels++; }
                                if (land[cell] > world.Config.houseThreshold) { houseTexels++; }
                            });

                            uint alive = WorldHarness.ReadBuffer<uint>(physarum.Counters)[0];
                            report.AppendLine($"  t={time:0}s  people {alive,6}  peakTown {peak,6:0.00}  townTexels(>0.6) {townTexels,5}  houseTexels {houseTexels,5}  farm {farm / landTexels:P2} of land");
                        }
                    }

                    Debug.Log(report.ToString());
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
