// =============================================================================
// ToolEffectsTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks what the tools do to the world, without a scene:
//           - a meteor crater digs a bowl, raises a rim, scatters organic matter,
//             leaves everything beyond it untouched, and treats a cube corner
//             evenly across the three faces that meet there,
//           - the spore dish starts agents inside its circle,
//           - an impact kills every agent inside its circle and none outside,
//           - picking accounts for terrain relief,
//           - the CPU height cache agrees with the GPU terrain,
//           - the dial mappings are consistent.
// =============================================================================

using NUnit.Framework;
using Terrarium.Interaction;
using Terrarium.Sphere;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Tests
{
    public sealed class ToolEffectsTests
    {
        private const int Resolution = 64;

        private static ComputeShader Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<ComputeShader>($"Assets/Shaders/Compute/{name}.compute");
        }

        // ------------------------------------------------------------------ crater

        [Test]
        public void Crater_DigsABowl_RaisesARim_AndLeavesTheFarSideAlone()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.5f, spinUp: false);
                float[] before = world.Read(world.Terrain.Current);

                Vector3 centre = new Vector3(0.3f, 0.2f, 1f).normalized;
                const float radius = 0.4f;
                new FieldBrush(Load("Brush")).ApplyCrater(world.Terrain.Current, world.Soil.Current, centre, radius, depth: 0.4f, rimHeight: 0.1f, organicAmount: 2f);

                float[] after = world.Read(world.Terrain.Current);
                float[] soil = world.Read(world.Soil.Current);

                float bowlDepth = 0f;
                float rimRise = 0f;
                double organicInside = 0.0;
                double organicRing = 0.0;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int i = world.Index(face, x, y);
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f));
                    float change = after[i] - before[i];

                    if (angle > radius * 1.8f + 0.001f)
                    {
                        Assert.AreEqual(0f, change, 1e-6f, "The terrain beyond the crater must be untouched.");
                        Assert.AreEqual(0f, soil[i + 1], 1e-6f, "No organic matter may land beyond the ring.");
                    }

                    if (angle < radius * 0.3f)
                    {
                        bowlDepth = Mathf.Max(bowlDepth, -change);
                        organicInside += soil[i + 1];
                    }
                    else if (angle > radius * 0.95f && angle < radius * 1.1f)
                    {
                        rimRise = Mathf.Max(rimRise, change);
                    }

                    if (angle > radius && angle < radius * 1.5f)
                    {
                        organicRing += soil[i + 1];
                    }
                });

                Assert.Greater(bowlDepth, 0.3f, "The crater centre should be dug down.");
                Assert.Greater(rimRise, 0.05f, "A rim should be piled up around the crater.");
                Assert.AreEqual(0.0, organicInside, 1e-6, "Organic matter is thrown OUTSIDE the crater, not into it.");
                Assert.Greater(organicRing, 5.0, "A ring of organic matter should land just outside the crater.");
            }
        }

        [Test]
        public void Crater_AtACubeCorner_IsEvenAcrossTheThreeFaces()
        {
            using (var world = new WorldHarness(Resolution))
            {
                // A flat world at height 0.5 everywhere, so only the crater changes anything.
                var brush = new FieldBrush(Load("Brush"));
                brush.Apply(world.Terrain.Current, Vector3.up, Mathf.PI, new Vector4(1000f, 0f, 0f, 0f), 0f, 0.5f);
                float[] before = world.Read(world.Terrain.Current);

                brush.ApplyCrater(world.Terrain.Current, world.Soil.Current, new Vector3(1f, 1f, 1f), 0.3f, depth: 0.4f, rimHeight: 0f, organicAmount: 0f);
                float[] after = world.Read(world.Terrain.Current);

                var dug = new double[CubeSphere.FaceCount];
                world.ForEachTexel((face, x, y, direction) =>
                {
                    int i = world.Index(face, x, y);
                    dug[face] += before[i] - after[i];
                });

                Assert.Greater(dug[0], 1.0);
                Assert.AreEqual(dug[0], dug[2], dug[0] * 1e-3, "+X and +Y must be dug equally.");
                Assert.AreEqual(dug[0], dug[4], dug[0] * 1e-3, "+X and +Z must be dug equally.");
                Assert.AreEqual(0.0, dug[1] + dug[3] + dug[5], 1e-6, "The far faces must be untouched.");
            }
        }

        [Test]
        public void Crater_OnLowland_LeavesACraterLake()
        {
            using (var world = new WorldHarness(Resolution))
            {
                var brush = new FieldBrush(Load("Brush"));
                brush.Apply(world.Terrain.Current, Vector3.up, Mathf.PI, new Vector4(1000f, 0f, 0f, 0f), 0f, 0.2f);   // low land, height 0.2

                brush.ApplyCrater(world.Terrain.Current, world.Soil.Current, Vector3.right, 0.4f, depth: 0.4f, rimHeight: 0.1f, organicAmount: 0f);
                float[] terrain = world.Read(world.Terrain.Current);

                Texel centre = CubeSphere.DirToTexel(Vector3.right, Resolution);
                Assert.Less(terrain[world.Index(centre.Face, centre.X, centre.Y)], 0f, "The crater floor should be below sea level: a lake.");
            }
        }

        // ------------------------------------------------------------- spawn / kill

        [Test]
        public void SporeDish_StartsAgentsInsideItsCircle()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.5f, spinUp: false);
                var physarum = world.CreatePhysarum(8192);
                physarum.SeedAgents(world.Terrain.Current, seed: 1, startAgents: 0);

                Vector3 centre = new Vector3(0f, 1f, 1f).normalized;
                physarum.SpawnAgents(centre, 0.15f, 300);

                uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
                Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);

                int count = 0;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] == 0u)
                    {
                        continue;
                    }

                    count++;
                    Vector3 position = bodies[i];
                    Assert.AreEqual(1f, position.magnitude, 1e-4f);
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(position, centre), -1f, 1f));
                    Assert.LessOrEqual(angle, 0.15f + 1e-3f, "A spore landed outside the dish's circle.");
                }

                Assert.Greater(count, 270, "Nearly all of the 300 spores should find a free slot.");
                Assert.LessOrEqual(count, 300);
            }
        }

        [Test]
        public void Impact_KillsEveryAgentInsideItsCircle_AndNoneOutside()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.9f, spinUp: false);
                var physarum = world.CreatePhysarum(8192);
                physarum.SeedAgents(world.Terrain.Current, seed: 2, startAgents: 4000);

                Vector3 centre = Vector3.right;
                const float radius = 0.7f;

                int aliveBefore = CountAlive(physarum, out int insideBefore, centre, radius);
                Assert.Greater(insideBefore, 100, "The test needs agents inside the circle to start with.");

                physarum.KillAgents(centre, radius);

                int aliveAfter = CountAlive(physarum, out int insideAfter, centre, radius);
                Assert.AreEqual(0, insideAfter, "No agent may survive inside the blast.");
                Assert.AreEqual(aliveBefore - insideBefore, aliveAfter, "Agents outside the blast must be untouched.");
            }
        }

        private static int CountAlive(Terrarium.Physarum.PhysarumSimulation physarum, out int inside, Vector3 centre, float radius)
        {
            uint[] alive = WorldHarness.ReadBuffer<uint>(physarum.Alive);
            Vector4[] bodies = WorldHarness.ReadBuffer<Vector4>(physarum.PosEnergy);

            int total = 0;
            inside = 0;
            for (int i = 0; i < alive.Length; i++)
            {
                if (alive[i] == 0u)
                {
                    continue;
                }

                total++;
                if (Mathf.Acos(Mathf.Clamp(Vector3.Dot(((Vector3)bodies[i]).normalized, centre), -1f, 1f)) <= radius)
                {
                    inside++;
                }
            }

            return total;
        }

        // ------------------------------------------------------------------ picking

        private sealed class OneMountain : ITerrainHeights
        {
            private readonly Vector3 peak = new Vector3(0f, 0f, -1f);

            public float HeightAt(Vector3 localDirection)
            {
                // A tall smooth mountain 0.5 rad across, height 1 at its peak.
                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(localDirection, peak), -1f, 1f));
                return Mathf.Clamp01(1f - angle / 0.5f);
            }
        }

        [Test]
        public void SurfacePicker_OnAMountain_HitsTheRealSurface_NotTheSeaLevelSphere()
        {
            var planet = new GameObject("Planet");
            try
            {
                planet.transform.position = Vector3.zero;
                const float heightScale = 0.1f;   // an exaggerated mountain: 10% of the radius
                var mountain = new OneMountain();

                // A ray aimed squarely at the mountain's peak from the front.
                var ray = new Ray(new Vector3(0f, 0f, -5f), Vector3.forward);
                bool hit = SurfacePicker.TryPick(ray, planet.transform, 1f, heightScale, mountain, out Vector3 local, out float distance);

                Assert.IsTrue(hit);
                Assert.AreEqual(5f - 1f * (1f + heightScale), distance, 2e-3f, "The ray should stop at the peak, 10% above sea level.");
                Assert.Less(Vector3.Distance(local, new Vector3(0f, 0f, -1f)), 1e-3f);
                Assert.Less(Vector3.Distance(ray.origin + ray.direction * distance, local * (1f + heightScale)), 2e-3f, "The hit point must lie on the real surface.");
            }
            finally
            {
                Object.DestroyImmediate(planet);
            }
        }

        [Test]
        public void SurfacePicker_OnAMountainSlope_PicksAPointOnTheSlope()
        {
            var planet = new GameObject("Planet");
            try
            {
                const float heightScale = 0.1f;
                var mountain = new OneMountain();

                // A ray that passes through the mountain's upper slope but would hit the sea-level sphere further round.
                var ray = new Ray(new Vector3(0.25f, 0f, -5f), Vector3.forward);
                Assert.IsTrue(SurfacePicker.TryPick(ray, planet.transform, 1f, heightScale, mountain, out Vector3 local, out float distance));

                float groundRadius = 1f + heightScale * mountain.HeightAt(local);
                Vector3 hitPoint = ray.origin + ray.direction * distance;
                Assert.AreEqual(groundRadius, hitPoint.magnitude, 2e-3f, "The pick must land on the terrain surface.");
            }
            finally
            {
                Object.DestroyImmediate(planet);
            }
        }

        [Test]
        public void SurfacePicker_WithNoHeights_BehavesLikeAPlainSphere()
        {
            var planet = new GameObject("Planet");
            try
            {
                var ray = new Ray(new Vector3(0f, 0f, -5f), Vector3.forward);
                Assert.IsTrue(SurfacePicker.TryPick(ray, planet.transform, 1f, 0.1f, null, out _, out float distance));
                Assert.AreEqual(4f, distance, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(planet);
            }
        }

        // --------------------------------------------------------------- height cache

        [Test]
        public void TerrainHeightCache_AgreesWithTheGpuTerrain()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 8, landFraction: 0.4f, spinUp: false);
                float[] terrain = world.Read(world.Terrain.Current);

                var cache = new TerrainHeightCache();
                Assert.AreEqual(0f, cache.HeightAt(Vector3.up), "Before any data arrives the ground reads as flat.");

                cache.Request(world.Terrain.Current);
                AsyncGPUReadback.WaitAllRequests();

                Assert.IsTrue(cache.HasData);
                var random = new System.Random(4);
                for (int i = 0; i < 500; i++)
                {
                    Vector3 direction = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f).normalized;
                    Texel texel = CubeSphere.DirToTexel(direction, Resolution);
                    Assert.AreEqual(terrain[world.Index(texel.Face, texel.X, texel.Y)], cache.HeightAt(direction), 1e-6f);
                }
            }
        }

        // ------------------------------------------------------------- field probe

        [Test]
        public void FieldProbe_ReadsOneTexel_AndMatchesTheFullReadback()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 8, landFraction: 0.4f, spinUp: true);
                float[] climate = world.Read(world.Climate.Current);

                var texel = new Texel(3, 17, 41);
                Vector4 climateValue = Vector4.zero;
                bool arrived = false;
                FieldProbe.Request(world.Climate.Current, texel, value => { climateValue = value; arrived = true; });

                Vector4 trailValue = new Vector4(-1f, 0f, 0f, 0f);
                FieldProbe.Request(world.Trail.Current, texel, value => trailValue = value);

                AsyncGPUReadback.WaitAllRequests();

                int i = world.Index(texel.Face, texel.X, texel.Y);
                Assert.IsTrue(arrived, "The probe callback must be called.");
                Assert.AreEqual(climate[i], climateValue.x, 1e-6f);
                Assert.AreEqual(climate[i + 1], climateValue.y, 1e-6f);
                Assert.AreEqual(climate[i + 2], climateValue.z, 1e-6f);
                Assert.AreEqual(0f, trailValue.x, 1e-6f, "A single-channel (trail) texel reads into x.");
            }
        }

        // -------------------------------------------------------------------- dials

        [TestCase(0, -75f)]
        [TestCase(1, -37.5f)]
        [TestCase(2, 0f)]
        [TestCase(3, 37.5f)]
        [TestCase(4, 75f)]
        public void Dial_TimeStops_RoundTrip(int stop, float angle)
        {
            Assert.AreEqual(angle, DialProp.AngleForTimeStop(stop));
            Assert.AreEqual(stop, DialProp.TimeStopForAngle(angle));
            Assert.AreEqual(stop, DialProp.TimeStopForAngle(angle + 15f), "Within half a spacing of a stop it still snaps to it.");
            Assert.AreEqual(stop, DialProp.TimeStopForAngle(angle - 15f));
        }

        [Test]
        public void Dial_TimeStops_MatchTheClockSpeeds()
        {
            Assert.AreEqual(5, SimClock.TimeScaleSteps.Length);
            Assert.AreEqual(0.25f, SimClock.TimeScaleSteps[1]);
            Assert.AreEqual(1f, SimClock.TimeScaleSteps[2], "The middle of the dial is normal speed.");
        }

        [Test]
        public void Dial_SpinAngleAndMultiplier_AreInverses_AndCentredOnOne()
        {
            const float range = 4f;

            Assert.AreEqual(1f, DialProp.SpinMultiplier(0f, range), 1e-5f);
            Assert.AreEqual(range, DialProp.SpinMultiplier(90f, range), 1e-4f);
            Assert.AreEqual(1f / range, DialProp.SpinMultiplier(-90f, range), 1e-4f);

            foreach (float multiplier in new[] { 0.3f, 0.7f, 1f, 2.5f, 3.9f })
            {
                Assert.AreEqual(multiplier, DialProp.SpinMultiplier(DialProp.SpinAngle(multiplier, range), range), 1e-4f);
            }
        }
    }
}
