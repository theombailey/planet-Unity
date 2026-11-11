// =============================================================================
// ClimateTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks the climate model does what Phase 4 asks of it:
//           - the lit side of the planet is warmer than the night side,
//           - the equator is warmer than the poles,
//           - over a year the warm and cold hemispheres swap (the seasons),
//           - ocean is saturated and wet coasts fade to drier inland,
//           - warm wet land weathers into nutrients; ocean does not,
//           - rain falls under clouds only and leaves the land wetter,
//           - every value stays in range and finite,
//           - the result does not depend on the field resolution.
//           The climate is run through the same code paths the game uses.
// Note    : latitude is read from the texel direction in the planet's own frame,
//           where +y is the north pole (spin is about it and the lean tips it).
// =============================================================================

using NUnit.Framework;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class ClimateTests
    {
        private const int Resolution = 64;
        private const float LandFraction = 0.38f;

        // Mean of channel `channel` over texels chosen by `include`.
        private static float MeanOf(WorldHarness world, float[] data, int channel, System.Func<Vector3, int, int, int, bool> include)
        {
            double sum = 0.0;
            int count = 0;
            world.ForEachTexel((face, x, y, direction) =>
            {
                if (include(direction, face, x, y))
                {
                    sum += data[world.Index(face, x, y) + channel];
                    count++;
                }
            });

            Assert.Greater(count, 0, "No texels matched.");
            return (float)(sum / count);
        }

        [Test]
        public void Climate_LitSideIsWarmerThanNightSide()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);
                float[] climate = world.Read(world.Climate.Current);

                // Channel 2 is the insolation the last step used.
                float day = MeanOf(world, climate, 0, (d, f, x, y) => climate[world.Index(f, x, y) + 2] > 0.5f);
                float night = MeanOf(world, climate, 0, (d, f, x, y) => climate[world.Index(f, x, y) + 2] <= 0.001f);

                Assert.Greater(day - night, 5f, $"Day side {day:0.0} C, night side {night:0.0} C.");
            }
        }

        [Test]
        public void Climate_EquatorIsWarmerThanThePoles()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);
                float[] climate = world.Read(world.Climate.Current);

                float equator = MeanOf(world, climate, 0, (d, f, x, y) => Mathf.Abs(d.y) < 0.2f);
                float poles = MeanOf(world, climate, 0, (d, f, x, y) => Mathf.Abs(d.y) > 0.9f);

                Assert.Greater(equator - poles, 15f, $"Equator {equator:0.0} C, poles {poles:0.0} C.");
                Assert.Less(poles, 0f, "The poles should be below freezing on average.");
                Assert.Greater(equator, 8f, "The equator should be comfortably above freezing on average.");
            }
        }

        [Test]
        public void Climate_SeasonsSwapTheWarmHemisphere()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);

                // Year phase 0.25: northern midsummer (lean +tilt). Phase 0.75: southern midsummer.
                double year = world.Clock.YearLengthSeconds;
                world.Run(0.0, 0.25 * year, 0.1f);
                float[] northSummer = world.Read(world.Climate.Current);
                world.Run(0.25 * year, 0.75 * year, 0.1f);
                float[] southSummer = world.Read(world.Climate.Current);

                float northInNorthSummer = MeanOf(world, northSummer, 0, (d, f, x, y) => d.y > 0.5f);
                float northInSouthSummer = MeanOf(world, southSummer, 0, (d, f, x, y) => d.y > 0.5f);
                float southInNorthSummer = MeanOf(world, northSummer, 0, (d, f, x, y) => d.y < -0.5f);
                float southInSouthSummer = MeanOf(world, southSummer, 0, (d, f, x, y) => d.y < -0.5f);

                Assert.Greater(northInNorthSummer - northInSouthSummer, 4f,
                    $"Northern hemisphere: {northInNorthSummer:0.0} C in its summer, {northInSouthSummer:0.0} C in its winter.");
                Assert.Greater(southInSouthSummer - southInNorthSummer, 4f,
                    $"Southern hemisphere: {southInSouthSummer:0.0} C in its summer, {southInNorthSummer:0.0} C in its winter.");
            }
        }

        [Test]
        public void Climate_OceanIsSaturated_AndWetCoastsFadeToDrierInland()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);
                float[] climate = world.Read(world.Climate.Current);
                float[] terrain = world.Read(world.Terrain.Current);

                bool IsOcean(int face, int x, int y) => terrain[world.Index(face, x, y)] < 0f;

                // A land texel is "coastal" if any texel within two steps is ocean; "inland" if none within six.
                bool NearOcean(int face, int x, int y, int reach)
                {
                    for (int dy = -reach; dy <= reach; dy++)
                    {
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            Texel t = CubeSphere.ResolveTexel(face, x + dx, y + dy, Resolution);
                            if (IsOcean(t.Face, t.X, t.Y))
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

                float oceanMin = float.MaxValue;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    if (IsOcean(face, x, y))
                    {
                        oceanMin = Mathf.Min(oceanMin, climate[world.Index(face, x, y) + 1]);
                    }
                });
                Assert.AreEqual(1f, oceanMin, 1e-5f, "Ocean must be saturated.");

                // Frozen ground keeps whatever moisture it had, so compare only land that is above freezing.
                float coastal = MeanOf(world, climate, 1, (d, f, x, y) => !IsOcean(f, x, y) && climate[world.Index(f, x, y)] > 0f && NearOcean(f, x, y, 2));
                float inland = MeanOf(world, climate, 1, (d, f, x, y) => !IsOcean(f, x, y) && climate[world.Index(f, x, y)] > 0f && !NearOcean(f, x, y, 6));

                Assert.Greater(coastal - inland, 0.15f, $"Coastal moisture {coastal:0.00}, inland {inland:0.00}.");
            }
        }

        [Test]
        public void Climate_WeatheringMakesNutrientsOnLandOnly()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);
                float[] soil = world.Read(world.Soil.Current);
                float[] terrain = world.Read(world.Terrain.Current);

                float oceanMax = 0f;
                float landMax = 0f;
                double landSum = 0.0;
                int land = 0;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int i = world.Index(face, x, y);
                    if (terrain[i] < 0f)
                    {
                        oceanMax = Mathf.Max(oceanMax, soil[i]);
                    }
                    else
                    {
                        landMax = Mathf.Max(landMax, soil[i]);
                        landSum += soil[i];
                        land++;
                    }
                });

                Assert.AreEqual(0f, oceanMax, 1e-6f, "Nothing should weather under the sea.");
                Assert.Greater(landMax, 0.2f, "Warm, wet land should have built up nutrients.");
                Assert.Less(landMax, world.Config.nutrientCap + 1e-4f, "Weathering must respect the nutrient cap.");
                Assert.Greater(landSum / land, 0.02, "Land as a whole should have some nutrients.");
            }
        }

        [Test]
        public void Climate_ValuesStayFiniteAndInRange_AfterAYear()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 9, landFraction: LandFraction, spinUp: true);
                world.Run(0.0, world.Clock.YearLengthSeconds, 0.1f);
                float[] climate = world.Read(world.Climate.Current);
                float[] soil = world.Read(world.Soil.Current);

                world.ForEachTexel((face, x, y, direction) =>
                {
                    int i = world.Index(face, x, y);
                    Assert.That(climate[i], Is.InRange(-80f, 90f), $"Temperature at face {face} ({x},{y}).");
                    Assert.That(climate[i + 1], Is.InRange(0f, 1f), $"Moisture at face {face} ({x},{y}).");
                    Assert.That(climate[i + 2], Is.InRange(0f, 1f), $"Insolation at face {face} ({x},{y}).");
                    Assert.That(climate[i + 3], Is.InRange(0f, 1f), $"Rain at face {face} ({x},{y}).");
                    Assert.That(soil[i], Is.InRange(0f, world.Config.nutrientCap + 1e-4f), $"Nutrients at face {face} ({x},{y}).");
                });
            }
        }

        [Test]
        public void Climate_RainFallsOnlyUnderCloud_AndWatersTheLand()
        {
            // Two identical worlds, one where rain waters the ground and one where it does not. The rain rate is
            // exaggerated on purpose: in the real game clouds already sit over wet ground, so the real rate only
            // tops up land that is nearly full, which is too small a difference to measure reliably here.
            float[] wet = RunHalfAYear(rainRate: 0.5f, out float[] wetTerrain);
            float[] dry = RunHalfAYear(rainRate: 0f, out _);

            double wetMoisture = 0.0;
            double dryMoisture = 0.0;
            int rainyLand = 0;
            int rainy = 0;
            int total = wet.Length / 4;
            for (int i = 0; i < wet.Length; i += 4)
            {
                if (wet[i + 3] > 0.05f)
                {
                    rainy++;
                }

                // Land that is being rained on right now: how wet is it with rain, and without?
                if (wet[i + 3] > 0.3f && wetTerrain[i] >= 0f)
                {
                    wetMoisture += wet[i + 1];
                    dryMoisture += dry[i + 1];
                    rainyLand++;
                }
            }

            Assert.Greater(rainy / (float)total, 0.005f, "Some of the planet should be under a raining cloud.");
            Assert.Less(rainy / (float)total, 0.6f, "It should not rain everywhere at once.");
            Assert.Greater(rainyLand, 10, "There should be raining land to look at.");
            Assert.Greater(wetMoisture / rainyLand, dryMoisture / rainyLand + 0.01,
                "Land under a raining cloud should be wetter than the same land in a world without rain water.");
        }

        private static float[] RunHalfAYear(float rainRate, out float[] terrain)
        {
            using (var world = new WorldHarness(Resolution, config => config.rainRate = rainRate))
            {
                world.Generate(seed: 5, landFraction: LandFraction, spinUp: true);
                world.Run(0.0, world.Clock.YearLengthSeconds * 0.5, 0.1f);
                terrain = world.Read(world.Terrain.Current);
                return world.Read(world.Climate.Current);
            }
        }

        [Test]
        public void Climate_DoesNotDependOnFieldResolution()
        {
            // The same planet simulated on a coarse and a fine grid should have the same average climate.
            // (Diffusion is sub-stepped so it covers the same distance at any resolution.)
            float coarse = AverageTemperatureAndMoisture(32, out float coarseMoisture);
            float fine = AverageTemperatureAndMoisture(64, out float fineMoisture);

            Assert.AreEqual(coarse, fine, 3f, "Average temperature differs between resolutions.");
            Assert.AreEqual(coarseMoisture, fineMoisture, 0.08f, "Average moisture differs between resolutions.");
        }

        [Test]
        public void Climate_Report_PrintsLatitudeTableThroughTheYear()
        {
            // A tuning aid, not a check: prints mean temperature, moisture and nutrients by latitude band
            // at four times of year, to the Console. Run it on its own when adjusting the climate settings.
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);
                double year = world.Clock.YearLengthSeconds;
                var text = new System.Text.StringBuilder("CLIMATE REPORT (mean over latitude bands; T in deg C)\n");
                text.AppendLine("lat band      |  T equinox  T N-summer  T equinox2  T S-summer |  land T (equinox) | land moisture | land nutrients");

                var snapshots = new float[4][];
                double[] times = { 0.0, 0.25 * year, 0.5 * year, 0.75 * year };
                double now = 0.0;
                for (int s = 0; s < 4; s++)
                {
                    world.Run(now, times[s], 0.1f);
                    now = System.Math.Max(now, times[s]);
                    snapshots[s] = world.Read(world.Climate.Current);
                }

                float[] terrain = world.Read(world.Terrain.Current);
                float[] soil = world.Read(world.Soil.Current);

                for (int lat = 75; lat >= -75; lat -= 30)
                {
                    float lo = Mathf.Sin((lat - 15) * Mathf.Deg2Rad);
                    float hi = Mathf.Sin((lat + 15) * Mathf.Deg2Rad);
                    bool InBand(Vector3 d) => d.y >= lo && d.y < hi;

                    string row = $"{lat - 15,4} to {lat + 15,4} | ";
                    for (int s = 0; s < 4; s++)
                    {
                        row += $"{MeanOf(world, snapshots[s], 0, (d, f, x, y) => InBand(d)),10:0.0}  ";
                    }

                    float landT = MeanOf(world, snapshots[0], 0, (d, f, x, y) => InBand(d) && terrain[world.Index(f, x, y)] >= 0f);
                    float landM = MeanOf(world, snapshots[0], 1, (d, f, x, y) => InBand(d) && terrain[world.Index(f, x, y)] >= 0f);
                    float landN = MeanOf(world, soil, 0, (d, f, x, y) => InBand(d) && terrain[world.Index(f, x, y)] >= 0f);
                    text.AppendLine($"{row}| {landT,16:0.0} | {landM,13:0.00} | {landN,14:0.00}");
                }

                for (int s = 0; s < 4; s++)
                {
                    float hottest = float.MinValue;
                    float coldest = float.MaxValue;
                    world.ForEachTexel((face, x, y, direction) =>
                    {
                        float t = snapshots[s][world.Index(face, x, y)];
                        hottest = Mathf.Max(hottest, t);
                        coldest = Mathf.Min(coldest, t);
                    });
                    text.AppendLine($"snapshot {s}: hottest texel {hottest:0.0} C, coldest {coldest:0.0} C");
                }

                Debug.Log(text.ToString());
                Assert.Pass();
            }
        }

        [TestCase(64)]
        [TestCase(256)]
        public void Climate_Report_PrintsStepCost(int resolution)
        {
            // A tuning aid, not a check: how long 1000 climate steps take to SUBMIT (CPU) and to FINISH
            // (CPU + GPU, forced by reading a texel back). Printed to the Console.
            using (var world = new WorldHarness(resolution))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: false);
                world.Run(0.0, 1.0, 1f / 60f);   // warm up shader and driver caches
                world.Read(world.Climate.Current);

                var clock = System.Diagnostics.Stopwatch.StartNew();
                world.Run(0.0, 1000.0 / 60.0, 1f / 60f);
                double submitMs = clock.Elapsed.TotalMilliseconds;
                world.Read(world.Climate.Current);
                double totalMs = clock.Elapsed.TotalMilliseconds;

                Debug.Log($"STEP COST at N={resolution}: 1000 steps submitted in {submitMs:0} ms ({submitMs / 1000:0.000} ms/step on the CPU), " +
                          $"finished after {totalMs:0} ms ({totalMs / 1000:0.000} ms/step including the GPU).");
                Assert.Pass();
            }
        }

        private static float AverageTemperatureAndMoisture(int resolution, out float moisture)
        {
            using (var world = new WorldHarness(resolution, config => config.climateSpinUpSeconds = 300f))
            {
                world.Generate(seed: 3, landFraction: LandFraction, spinUp: true);
                float[] climate = world.Read(world.Climate.Current);
                moisture = MeanOf(world, climate, 1, (d, f, x, y) => true);
                return MeanOf(world, climate, 0, (d, f, x, y) => true);
            }
        }
    }
}
