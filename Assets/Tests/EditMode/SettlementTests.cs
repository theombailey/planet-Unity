// =============================================================================
// SettlementTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks the rules the people build by, on the GPU:
//           - the water-proximity field is 1 at water and falls with distance,
//           - a house only stands where its rules allow (land, water to hand, weather, not on a farm),
//             and where it is wanted,
//           - houses are built over time, abandoned when no longer wanted, and rebuilt in a better
//             style once one is known (tent -> mud -> brick),
//           - a tighter water rule means fewer houses, and wells loosen it,
//           - houses never stand closer than the minimum distance and keep off roads,
//           - scaffolding stands round a building while it is built, rebuilt or taken down, and only then,
//           - a busy path hardens into a permanent road that outlasts the path, and stone hardens faster,
//           - irrigation lets farms grow in dry land, but only near water.
// =============================================================================

using NUnit.Framework;
using Terrarium.Physarum;
using Terrarium.Sphere;
using Terrarium.Towns;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class SettlementTests
    {
        private const int Resolution = 64;

        private static ComputeShader Load(string name)
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>($"Assets/Shaders/Compute/{name}.compute");
        }

        private static void Paint(WorldHarness world, RenderTexture texture, Vector4 amount, float max)
        {
            new FieldBrush(Load("Brush")).Apply(texture, Vector3.up, Mathf.PI, amount, 0f, max);
        }

        // A world with land, a spun-up climate, the water field relaxed, and a uniform town density everywhere.
        private static PhysarumSimulation PrepareWorld(WorldHarness world, float townDensity)
        {
            world.Generate(seed: 3, landFraction: 0.5f, spinUp: true);
            PhysarumSimulation physarum = world.CreatePhysarum(1024);
            world.SeedPeople(physarum, seed: 1, count: 0);
            Paint(world, world.Land.Current, new Vector4(townDensity, 0f, 0f, 0f), 6f);
            return physarum;
        }

        // How many houses stand: the instance count the GPU wrote into the draw arguments.
        private static int Count(HouseSimulation simulation)
        {
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(simulation.Arguments);
            request.WaitForCompletion();
            return (int)request.GetData<uint>()[1];
        }

        // How many buildings are being built, rebuilt or taken down (the scaffold list).
        private static int ScaffoldCount(HouseSimulation simulation)
        {
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(simulation.ScaffoldArguments);
            request.WaitForCompletion();
            return (int)request.GetData<uint>()[1];
        }

        // The standing houses: three float4 each (see Houses.compute).
        private static Vector4[] ReadHouses(HouseSimulation simulation)
        {
            int count = Count(simulation);
            if (count == 0)
            {
                return new Vector4[0];
            }

            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(simulation.Houses, count * HouseSimulation.HouseStride, 0);
            request.WaitForCompletion();
            var result = new Vector4[count * 3];
            request.GetData<Vector4>().CopyTo(result);
            return result;
        }

        // Runs the house rules for a stretch of sim time.
        private static void Build(HouseSimulation simulation, WorldHarness world, SettlementRules rules, float seconds, float step = 5f)
        {
            for (float t = 0f; t < seconds; t += step)
            {
                simulation.Update(world.PeopleFields, rules, Resolution, step);
            }
        }

        private static HouseSimulation NewHouses(WorldHarness world)
        {
            return new HouseSimulation(Load("Houses"), world.Config, 42, 100);
        }

        // ---------------------------------------------------------------- water

        [Test]
        public void Water_IsOneAtTheSea_AndFallsWithDistance()
        {
            using (var world = new WorldHarness(Resolution))
            {
                PrepareWorld(world, 0f);
                float[] water = world.Read(world.Water.Current);
                float[] terrain = world.Read(world.Terrain.Current);

                var ocean = new System.Collections.Generic.List<Vector3>();
                world.ForEachTexel((face, x, y, direction) =>
                {
                    if (terrain[world.Index(face, x, y)] < 0f)
                    {
                        ocean.Add(direction);
                    }
                });

                Assert.Greater(ocean.Count, 100, "The test world needs a sea.");

                float reach = world.Config.waterFieldReach;
                int checkedLand = 0;
                int stride = 0;
                float worstError = 0f;
                double ratioSum = 0.0;
                int ratioCount = 0;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int cell = world.Index(face, x, y, 1);
                    bool isOcean = terrain[world.Index(face, x, y)] < 0f;
                    if (isOcean)
                    {
                        Assert.AreEqual(1f, water[cell], 1e-5f, "The sea is at proximity 1.");
                        return;
                    }

                    // Sample some land texels and compare with the true distance to the nearest sea texel.
                    if (stride++ % 37 != 0)
                    {
                        return;
                    }

                    float nearest = float.MaxValue;
                    foreach (Vector3 sea in ocean)
                    {
                        nearest = Mathf.Min(nearest, Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, sea), -1f, 1f)));
                    }

                    float expected = Mathf.Max(0f, 1f - nearest / reach);
                    if (nearest < reach * 0.8f)
                    {
                        // How much faster than the true distance the field falls (1 = exactly right).
                        ratioSum += (1f - water[cell]) / (nearest / reach);
                        ratioCount++;
                    }

                    worstError = Mathf.Max(worstError, Mathf.Abs(expected - water[cell]));
                    checkedLand++;
                });

                Assert.Greater(checkedLand, 30);
                Debug.Log($"WATER FALL RATIO (field drop / true drop): mean {ratioSum / Mathf.Max(1, ratioCount):0.00} over {ratioCount} texels, worst error {worstError:0.00}");
                Assert.Less(worstError, 0.35f, "The proximity field should follow the true distance to the sea.");
            }
        }

        // --------------------------------------------------------------- houses

        [Test]
        public void Houses_AreBuiltOverTime_OnlyWhereTheRulesAllow()
        {
            using (var world = new WorldHarness(Resolution))
            using (HouseSimulation simulation = NewHouses(world))
            {
                PrepareWorld(world, townDensity: 5f);
                SettlementRules rules = SettlementRules.Start;

                simulation.Update(world.PeopleFields, rules, Resolution, 0f);
                Assert.AreEqual(0, Count(simulation), "Nothing is built before time passes.");

                Build(simulation, world, rules, 100f);
                int count = Count(simulation);
                Assert.Greater(count, 5, "Houses should be built where people are crowded.");

                Vector4[] list = ReadHouses(simulation);
                float[] terrain = world.Read(world.Terrain.Current);
                float[] climate = world.Read(world.Climate.Current);
                float[] water = world.Read(world.Water.Current);
                SimConfig config = world.Config;
                float need = rules.HouseWaterProximity(config);

                for (int i = 0; i < count; i++)
                {
                    Vector3 direction = ((Vector3)list[i * 3]).normalized;
                    Texel texel = CubeSphere.DirToTexel(direction, Resolution);
                    int cell = world.Index(texel.Face, texel.X, texel.Y);
                    int waterCell = world.Index(texel.Face, texel.X, texel.Y, 1);
                    Assert.Greater(terrain[cell], -0.2f, "A house stands on land (or on the coast, to within a coarse texel).");
                    float proximity = water[waterCell];
                    bool wet = climate[cell + 1] >= config.houseWetMoisture - 0.1f;
                    Assert.IsTrue(proximity >= need - 0.1f || wet, $"House {i} has no water to hand (proximity {proximity:0.00}, need {need:0.00}).");
                    Assert.GreaterOrEqual(climate[cell], config.houseTempMinTent - config.houseTempMargin - 1f, "A tent is not built in the cold.");
                    Assert.AreEqual(0f, list[i * 3 + 2].w, "Every house starts as a tent.");
                }
            }
        }

        [Test]
        public void Houses_AreGivenUp_WhenNobodyNeedsThemAnyMore()
        {
            using (var world = new WorldHarness(Resolution))
            using (HouseSimulation simulation = NewHouses(world))
            {
                PrepareWorld(world, townDensity: 5f);
                SettlementRules rules = SettlementRules.Start;
                Build(simulation, world, rules, 100f);
                int before = Count(simulation);
                Assert.Greater(before, 5);

                Paint(world, world.Land.Current, new Vector4(-100f, 0f, 0f, 0f), 6f);   // the crowd is gone
                Build(simulation, world, rules, 20f);
                Assert.Greater(Count(simulation), before / 2, "Houses are not abandoned at once.");

                Build(simulation, world, rules, world.Config.houseAbandonSeconds + 30f);
                Assert.AreEqual(0, Count(simulation), "Empty houses are given up in the end.");
            }
        }

        [Test]
        public void ATighterWaterRule_MeansFewerHouses_AndWellsLoosenIt()
        {
            int Houses(float reach, float wells)
            {
                using (var world = new WorldHarness(Resolution, config =>
                {
                    config.houseWaterReach = reach;
                    config.houseWetMoisture = 5f;   // no rain-fed streams: only the water field counts
                }))
                using (HouseSimulation simulation = NewHouses(world))
                {
                    PrepareWorld(world, townDensity: 5f);
                    SettlementRules rules = SettlementRules.Start;
                    rules.WaterReachScale = wells;
                    Build(simulation, world, rules, 100f);
                    return Count(simulation);
                }
            }

            int tight = Houses(0.02f, 1f);
            int normal = Houses(0.07f, 1f);
            int withWells = Houses(0.07f, 3f);

            Debug.Log($"Houses by water rule: tight {tight}, normal {normal}, with wells {withWells}");
            Assert.Less(tight, normal, "A shorter reach to water leaves fewer places to build.");
            Assert.Less(normal, withWells, "Wells let houses stand further from the water.");
        }

        [Test]
        public void Houses_AreRebuiltInABetterStyle_OnceOneIsKnown()
        {
            using (var world = new WorldHarness(Resolution))
            using (HouseSimulation simulation = NewHouses(world))
            {
                PrepareWorld(world, townDensity: 5f);
                SettlementRules rules = SettlementRules.Start;
                Build(simulation, world, rules, 100f);
                int houses = Count(simulation);
                Assert.Greater(houses, 5);

                rules.BuildingTier = 2;   // bricks are learnt
                Build(simulation, world, rules, 400f);

                Vector4[] list = ReadHouses(simulation);
                int count = Count(simulation);
                int bricks = 0;
                for (int i = 0; i < count; i++)
                {
                    if (Mathf.RoundToInt(list[i * 3 + 2].w) == 2)
                    {
                        bricks++;
                    }
                }

                Assert.Greater(count, houses / 2, "The town is still there.");
                Assert.Greater(bricks, count * 0.8f, $"Most houses should be brick by now ({bricks} of {count}).");
            }
        }

        // ------------------------------------------------- lattice, roads, scaffolding

        // A world where only land, farms and roads limit building: water and weather rules are switched off.
        private static WorldHarness OpenWorld()
        {
            return new WorldHarness(Resolution, config =>
            {
                config.houseWetMoisture = 0f;       // moisture everywhere counts as water to hand
                config.houseTempMinTent = -100f;
                config.houseTempMinMud = -100f;
                config.houseTempMinBrick = -100f;
                config.houseTempMax = 200f;
            });
        }

        [Test]
        public void Houses_NeverStandCloserThanTheMinimumDistance_OrWiderThanTheGap()
        {
            using (WorldHarness world = OpenWorld())
            using (HouseSimulation simulation = NewHouses(world))
            {
                world.Generate(seed: 3, landFraction: 0.5f, spinUp: true);
                PhysarumSimulation physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 1, count: 0);
                new FieldBrush(Load("Brush")).Apply(world.Land.Current, Vector3.right, 0.45f, new Vector4(5f, 0f, 0f, 0f), 0f, 6f);   // a big town in one region

                Build(simulation, world, SettlementRules.Start, 100f);
                Vector4[] list = ReadHouses(simulation);
                int count = list.Length / 3;
                Assert.Greater(count, 300, "The test needs a town with plenty of houses.");

                float minimum = world.Config.houseMinDistance;
                float closest = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    Vector3 a = ((Vector3)list[i * 3]).normalized;
                    Assert.LessOrEqual(list[i * 3].w, 0.7f * minimum + 1e-6f, "A house is never wider than 0.7 of the gap.");
                    for (int j = i + 1; j < count; j++)
                    {
                        float dot = Vector3.Dot(a, ((Vector3)list[j * 3]).normalized);
                        closest = Mathf.Min(closest, Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)));
                    }
                }

                Debug.Log($"Closest pair of {count} houses: {closest:0.0000} rad (minimum asked {minimum:0.0000})");
                Assert.GreaterOrEqual(closest, minimum * 0.97f, "No two houses may be closer than the minimum distance.");
            }
        }

        [Test]
        public void Houses_KeepOffRoads()
        {
            using (WorldHarness world = OpenWorld())
            using (HouseSimulation simulation = NewHouses(world))
            {
                world.Generate(seed: 3, landFraction: 0.5f, spinUp: true);
                PhysarumSimulation physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 1, count: 0);

                // The middle of the biggest stretch of land we can find, to lay a road disc on.
                float[] terrain = world.Read(world.Terrain.Current);
                Vector3 centre = Vector3.right;
                float best = float.MinValue;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    float height = terrain[world.Index(face, x, y)];
                    if (height > best)
                    {
                        best = height;
                        centre = direction;
                    }
                });

                var brush = new FieldBrush(Load("Brush"));
                brush.Apply(world.Land.Current, Vector3.up, Mathf.PI, new Vector4(5f, 0f, 0f, 0f), 0f, 6f);              // crowded everywhere
                brush.Apply(world.Land.Current, centre, 0.15f, new Vector4(0f, 0f, 0f, 1f), 0f, 1f);                     // a road disc (fully paved)

                Build(simulation, world, SettlementRules.Start, 100f);
                Vector4[] list = ReadHouses(simulation);

                int onRoad = 0;
                int nearRoad = 0;
                for (int i = 0; i < list.Length / 3; i++)
                {
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(((Vector3)list[i * 3]).normalized, centre), -1f, 1f));
                    if (angle < 0.05f)
                    {
                        onRoad++;
                    }
                    else if (angle > 0.20f && angle < 0.40f)
                    {
                        nearRoad++;
                    }
                }

                Debug.Log($"Houses on the road disc: {onRoad}; in the ring round it: {nearRoad}");
                Assert.AreEqual(0, onRoad, "Nothing is built on a permanent road.");
                Assert.Greater(nearRoad, 0, "The land beside the road is built on.");
            }
        }

        [Test]
        public void Scaffolding_StandsWhileBuilding_Rebuilding_AndTakingDown_AndOnlyThen()
        {
            using (WorldHarness world = OpenWorld())
            using (HouseSimulation simulation = NewHouses(world))
            {
                world.Generate(seed: 3, landFraction: 0.5f, spinUp: true);
                PhysarumSimulation physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 1, count: 0);
                var brush = new FieldBrush(Load("Brush"));
                brush.Apply(world.Land.Current, Vector3.right, 0.45f, new Vector4(5f, 0f, 0f, 0f), 0f, 6f);

                SettlementRules rules = SettlementRules.Start;
                simulation.Update(world.PeopleFields, rules, Resolution, 0f);
                Assert.AreEqual(0, ScaffoldCount(simulation), "No building work, no scaffolding.");

                // New houses: scaffolding while they go up.
                Build(simulation, world, rules, 5f, step: 1f);
                Assert.Greater(ScaffoldCount(simulation), 0, "Houses going up have scaffolding.");
                Assert.AreEqual(Count(simulation), ScaffoldCount(simulation), "Every house is still under construction at this moment.");

                // Finished: it all comes down.
                Build(simulation, world, rules, 100f);
                Assert.Greater(Count(simulation), 10);
                Assert.AreEqual(0, ScaffoldCount(simulation), "Finished houses have no scaffolding.");

                // Rebuilt in a better style: scaffolding goes up again.
                rules.BuildingTier = 1;
                int sawRebuilding = 0;
                for (int t = 0; t < 120; t++)
                {
                    simulation.Update(world.PeopleFields, rules, Resolution, 1f);
                    sawRebuilding = Mathf.Max(sawRebuilding, ScaffoldCount(simulation));
                }

                Assert.Greater(sawRebuilding, 0, "A house being rebuilt in a better style has scaffolding.");

                // Taken down: scaffolding while they come down, and none once they are gone.
                Build(simulation, world, rules, 100f);
                Paint(world, world.Land.Current, new Vector4(-100f, 0f, 0f, 0f), 6f);
                int sawDemolition = 0;
                for (int t = 0; t < world.Config.houseAbandonSeconds + world.Config.houseDemolishSeconds + 40; t++)
                {
                    simulation.Update(world.PeopleFields, rules, Resolution, 1f);
                    sawDemolition = Mathf.Max(sawDemolition, ScaffoldCount(simulation));
                }

                Assert.Greater(sawDemolition, 0, "A house being taken down has scaffolding.");
                Assert.AreEqual(0, Count(simulation), "Abandoned houses are taken down in the end.");
                Assert.AreEqual(0, ScaffoldCount(simulation), "And the scaffolding goes with them.");
            }
        }

        [Test]
        public void TheLattice_HasMoreSpotsTheCloserHousesMayStand()
        {
            Assert.Greater(HouseSimulation.CandidatesFor(0.005f), HouseSimulation.CandidatesFor(0.01f));
            Assert.AreEqual(Mathf.CeilToInt(Mathf.Pow(HouseSimulation.LatticeGapFactor / 0.0065f, 2f)), HouseSimulation.CandidatesFor(0.0065f));
        }

        // ---------------------------------------------------------------- roads

        [Test]
        public void ABusyPath_HardensIntoARoad_ThatOutlastsThePath_AndStoneIsFaster()
        {
            float RoadAfter(int roadTier, float seconds)
            {
                using (var world = new WorldHarness(Resolution, config =>
                {
                    config.physarumTrailDecay = 0f;    // hold the path steady
                    config.pavingSeconds = 40f;
                }))
                {
                    world.Generate(seed: 3, landFraction: 0.97f, spinUp: false);
                    PhysarumSimulation physarum = world.CreatePhysarum(1024);
                    world.SeedPeople(physarum, seed: 1, count: 0);
                    new FieldBrush(Load("Brush")).Apply(world.Trail.Current, Vector3.right, 0.03f, new Vector4(40f, 0f, 0f, 0f), 0f, 8f);   // a thin, strong track

                    SettlementRules rules = SettlementRules.Start;
                    rules.RoadTier = roadTier;
                    for (float t = 0f; t < seconds; t += 1f)
                    {
                        physarum.Step(world.PeopleFields, rules, 1f);
                    }

                    float[] land = world.Read(world.Land.Current);
                    float peak = 0f;
                    world.ForEachTexel((face, x, y, direction) => peak = Mathf.Max(peak, land[world.Index(face, x, y) + 3]));
                    return peak;
                }
            }

            Assert.Less(RoadAfter(0, 3f), 0.2f, "A path does not harden at once.");
            float dirt = RoadAfter(0, 15f);
            float stone = RoadAfter(1, 15f);
            Debug.Log($"Road after 15 s: dirt {dirt:0.00}, stone {stone:0.00}");
            Assert.Greater(dirt, 0.1f, "A busy path hardens into a road.");
            Assert.Greater(stone, dirt, "Stone roads harden faster.");

            // The road outlasts the path: with the path fading away, the road is still there a long time after.
            using (var world = new WorldHarness(Resolution, config => config.pavingSeconds = 50f))
            {
                world.Generate(seed: 3, landFraction: 0.97f, spinUp: false);
                PhysarumSimulation physarum = world.CreatePhysarum(1024);
                world.SeedPeople(physarum, seed: 1, count: 0);
                new FieldBrush(Load("Brush")).Apply(world.Land.Current, Vector3.right, 0.2f, new Vector4(0f, 0f, 0f, 1f), 0f, 1f);   // a finished road

                for (int t = 0; t < 120; t++)
                {
                    physarum.Step(world.PeopleFields, SettlementRules.Start, 1f);
                }

                float[] land = world.Read(world.Land.Current);
                float peak = 0f;
                world.ForEachTexel((face, x, y, direction) => peak = Mathf.Max(peak, land[world.Index(face, x, y) + 3]));
                Assert.Greater(peak, 0.8f, "A finished road does not wear away in a couple of minutes.");
            }
        }

        // ----------------------------------------------------------- irrigation

        [Test]
        public void Irrigation_LetsFarmsGrowInDryLand_ButOnlyNearWater()
        {
            float[] FarmsWith(bool irrigation, out float[] water)
            {
                using (var world = new WorldHarness(Resolution, config =>
                {
                    config.townDecay = 0f;
                    config.townSpread = 0f;
                    config.farmGrowth = 0.5f;
                    config.peopleArrivalRate = 0f;
                }))
                {
                    world.Generate(seed: 3, landFraction: 0.6f, spinUp: true);
                    PhysarumSimulation physarum = world.CreatePhysarum(1024);
                    world.SeedPeople(physarum, seed: 1, count: 0);

                    Paint(world, world.Climate.Current, new Vector4(0f, -5f, 0f, 0f), 60f);                // bone dry everywhere
                    Paint(world, world.Land.Current, new Vector4(0.3f, 0f, 0f, 0f), 6f);                   // town density in the farm window

                    SettlementRules rules = SettlementRules.Start;
                    rules.Irrigation = irrigation;
                    for (int t = 0; t < 40; t++)
                    {
                        physarum.Step(world.PeopleFields, rules, 1f);
                    }

                    water = world.Read(world.Water.Current);
                    return world.Read(world.Land.Current);
                }
            }

            float[] dry = FarmsWith(false, out _);
            float[] irrigated = FarmsWith(true, out float[] water);

            using (var world = new WorldHarness(Resolution))
            {
                float near = SettlementRules.Start.IrrigationProximity(world.Config);
                float farmsDry = 0f;
                float farmsNear = 0f;
                float farmsFar = 0f;
                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    for (int y = 0; y < Resolution; y++)
                    {
                        for (int x = 0; x < Resolution; x++)
                        {
                            int cell = world.Index(face, x, y);
                            float proximity = water[world.Index(face, x, y, 1)];
                            farmsDry += dry[cell + 1];
                            if (proximity >= near + 0.15f)
                            {
                                farmsNear += irrigated[cell + 1];
                            }
                            else if (proximity < near - 0.05f)
                            {
                                farmsFar += irrigated[cell + 1];
                            }
                        }
                    }
                }

                Debug.Log($"Farms: dry world {farmsDry:0.0}, irrigated near water {farmsNear:0.0}, irrigated far from water {farmsFar:0.0}");
                Assert.Less(farmsDry, 1f, "In bone-dry land nothing is farmed without irrigation.");
                Assert.Greater(farmsNear, 3f, "With irrigation, farms grow in dry land near water.");
                Assert.Less(farmsFar, 1f, "Irrigation does not reach far from water.");
            }
        }
    }
}
