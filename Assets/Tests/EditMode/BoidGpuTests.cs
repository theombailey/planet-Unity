// =============================================================================
// BoidGpuTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks Boids.compute through BoidSimulation:
//           - flocks stay together, move the same way and do not pile up on one point,
//           - boids fly at hover height over the terrain, rising over hills,
//           - boids climb the canopy gradient toward food (compared with boids that are
//             given no interest in food),
//           - energy: boids over canopy gain, boids over bare ground lose, starving boids
//             die and their bodies add to the soil, well-fed boids breed,
//           - boids pick up seeds over canopy and drop them as events the CPU can read,
//           - an impact kills exactly the boids inside its circle,
//           - a bigger pool keeps the boids that already exist,
//           - the starting flocks are placed on dry land only.
// =============================================================================

using System;
using NUnit.Framework;
using Terrarium.Boids;
using Terrarium.Sphere;
using UnityEditor;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class BoidGpuTests
    {
        private const int Resolution = 64;
        private const float Step = 0.1f;

        // Switches off every energy term, so a test about steering is not disturbed by boids starving.
        private static void FreeEnergy(SimConfig config)
        {
            config.boidUpkeep = 0f;
            config.boidFlightCost = 0f;
            config.boidCrowdCost = 0f;
            config.boidColdCost = 0f;
            config.boidReproduceEnergy = 100f;
            config.boidStartEnergy = 1f;
        }

        private static FieldBrush Brush()
        {
            return new FieldBrush(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Brush.compute"));
        }

        // Adds canopy (Soil.B) in a soft blob.
        private static void PaintCanopy(WorldHarness world, Vector3 centre, float radius, float amount = 5f, float max = 1f)
        {
            Brush().Apply(world.Soil.Current, centre, radius, new Vector4(0f, 0f, amount, 0f), 0f, max);
        }

        private static void Spawn(WorldHarness world, BoidSimulation boids, Vector3 centre, float radius, int count)
        {
            boids.SpawnBoids(centre, radius, count, world.Terrain.Current);
        }

        private struct Boid
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Energy;
            public float Carry;
        }

        private static Boid[] LiveBoids(BoidSimulation boids)
        {
            Vector4[] posEnergy = WorldHarness.ReadBuffer<Vector4>(boids.PosEnergy);
            Vector4[] velSeed = WorldHarness.ReadBuffer<Vector4>(boids.VelSeed);
            uint[] alive = WorldHarness.ReadBuffer<uint>(boids.Alive);

            var list = new System.Collections.Generic.List<Boid>();
            for (int i = 0; i < alive.Length; i++)
            {
                if (alive[i] != 0u)
                {
                    list.Add(new Boid
                    {
                        Position = new Vector3(posEnergy[i].x, posEnergy[i].y, posEnergy[i].z),
                        Velocity = new Vector3(velSeed[i].x, velSeed[i].y, velSeed[i].z),
                        Energy = posEnergy[i].w,
                        Carry = velSeed[i].w,
                    });
                }
            }

            return list.ToArray();
        }

        private static float AngleBetween(Vector3 a, Vector3 b)
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f));
        }

        private static float MeanAngleTo(Boid[] boids, Vector3 point)
        {
            float sum = 0f;
            foreach (Boid boid in boids)
            {
                sum += AngleBetween(boid.Position, point);
            }

            return sum / boids.Length;
        }

        private static float HeightAt(WorldHarness world, float[] terrain, Vector3 direction)
        {
            Texel texel = CubeSphere.DirToTexel(direction, world.Resolution);
            return terrain[world.Index(texel.Face, texel.X, texel.Y)];
        }

        // ------------------------------------------------------------------ flocking

        [Test]
        public void Flock_StaysTogether_AndMovesTheSameWay()
        {
            using (var world = new WorldHarness(Resolution, FreeEnergy))
            {
                BoidSimulation boids = world.CreateBoids(2000);
                Vector3 centre = new Vector3(0.3f, 0.8f, 0.5f).normalized;
                Spawn(world, boids, centre, 0.03f, 80);
                world.RunBoids(boids, 250, Step);

                Boid[] flock = LiveBoids(boids);
                Assert.GreaterOrEqual(flock.Length, 70, "Most of the flock should have found a slot.");

                // Order parameter: 1 when everyone flies the same way, near 0 when headings are random.
                Vector3 sum = Vector3.zero;
                float speeds = 0f;
                Vector3 mean = Vector3.zero;
                foreach (Boid boid in flock)
                {
                    Vector3 up = boid.Position.normalized;
                    Vector3 tangent = boid.Velocity - up * Vector3.Dot(boid.Velocity, up);
                    sum += tangent.normalized;
                    speeds += 1f;
                    mean += boid.Position.normalized;
                }

                float order = sum.magnitude / speeds;
                Assert.Greater(order, 0.6f, $"The flock should fly roughly one way (order {order:0.00}).");

                // Cohesion: the flock is still a flock (it started 0.04 rad wide; a few stragglers are allowed).
                Vector3 middle = mean.normalized;
                float far = 0f;
                float total = 0f;
                foreach (Boid boid in flock)
                {
                    float angle = AngleBetween(boid.Position, middle);
                    far = Mathf.Max(far, angle);
                    total += angle;
                }

                float average = total / flock.Length;
                Assert.Less(average, 0.3f, $"The flock should stay together (average {average:0.00} rad from its middle).");
                Assert.Less(far, 1.0f, $"Nobody should drift off alone (furthest {far:0.00} rad).");
            }
        }

        [Test]
        public void Separation_KeepsBoidsFromPilingOnOnePoint()
        {
            using (var world = new WorldHarness(Resolution, FreeEnergy))
            {
                BoidSimulation boids = world.CreateBoids(2000);
                Spawn(world, boids, Vector3.up, 0.01f, 200);   // packed far tighter than the separation radius
                world.RunBoids(boids, 120, Step);

                Boid[] flock = LiveBoids(boids);
                float limit = world.Config.boidSeparationRadius * 0.25f;
                int crowded = 0;
                for (int i = 0; i < flock.Length; i++)
                {
                    for (int j = i + 1; j < flock.Length; j++)
                    {
                        if ((flock[i].Position - flock[j].Position).magnitude < limit)
                        {
                            crowded++;
                            break;
                        }
                    }
                }

                Assert.Less(crowded, flock.Length * 0.1f, $"{crowded} of {flock.Length} boids still sit almost on top of another.");
            }
        }

        [Test]
        public void Boids_FlyAtHoverHeight_RisingOverHills()
        {
            using (var world = new WorldHarness(Resolution, FreeEnergy))
            {
                Vector3 hill = new Vector3(0f, 1f, 0.2f).normalized;
                Brush().Apply(world.Terrain.Current, hill, 0.4f, new Vector4(3f, 0f, 0f, 0f), -10f, 10f);

                BoidSimulation boids = world.CreateBoids(2000);
                Spawn(world, boids, hill, 0.1f, 200);
                world.RunBoids(boids, 20, Step);   // flocks fly on quickly, so look while they are still over the hill

                float[] terrain = world.Read(world.Terrain.Current);
                Boid[] flock = LiveBoids(boids);
                Assert.Greater(flock.Length, 100);

                float worst = 0f;
                float highest = 0f;
                foreach (Boid boid in flock)
                {
                    float expected = 1f + world.Config.terrainHeightScale * Mathf.Max(HeightAt(world, terrain, boid.Position.normalized), 0f) + world.Config.boidHoverHeight;
                    worst = Mathf.Max(worst, Mathf.Abs(boid.Position.magnitude - expected));
                    highest = Mathf.Max(highest, boid.Position.magnitude);
                }

                Assert.Less(worst, 0.015f, $"Boids should sit near hover height over the ground (worst error {worst:0.0000}).");
                Assert.Greater(highest, 1f + world.Config.boidHoverHeight + 0.02f, "Over a hill the flock should be lifted above plain hover height.");
            }
        }

        [Test]
        public void Boids_ClimbTheCanopyGradient_TowardFood()
        {
            using (var world = new WorldHarness(Resolution, FreeEnergy))
            {
                Vector3 forest = Vector3.up;
                PaintCanopy(world, forest, 0.12f);

                // The flock starts a short way from the forest, inside the range boids can sense it from.
                Vector3 start = Quaternion.AngleAxis(Mathf.Rad2Deg * 0.2f, Vector3.right) * forest;

                world.Config.boidFoodWeight = 0f;
                BoidSimulation control = world.CreateBoids(2000);
                Spawn(world, control, start, 0.03f, 200);
                world.RunBoids(control, 400, Step);
                float withoutFood = MeanAngleTo(LiveBoids(control), forest);

                world.Config.boidFoodWeight = 4f;
                BoidSimulation hungry = world.CreateBoids(2000);
                Spawn(world, hungry, start, 0.03f, 200);
                world.RunBoids(hungry, 400, Step);
                float withFood = MeanAngleTo(LiveBoids(hungry), forest);

                Assert.Less(withFood, withoutFood * 0.8f, $"Hungry boids ended {withFood:0.00} rad from the forest, indifferent ones {withoutFood:0.00}.");
            }
        }

        // ------------------------------------------------------------------ energy

        [Test]
        public void Energy_RisesOverCanopy_AndFallsOverBareGround()
        {
            using (var world = new WorldHarness(Resolution, c =>
            {
                c.boidReproduceEnergy = 100f;
                c.boidCrowdCost = 0f;
                c.boidStartEnergy = 1f;
            }))
            {
                PaintCanopy(world, Vector3.up, 0.5f);

                BoidSimulation fed = world.CreateBoids(2000);
                Spawn(world, fed, Vector3.up, 0.02f, 100);
                world.RunBoids(fed, 80, Step);
                float fedEnergy = Average(LiveBoids(fed));

                BoidSimulation starved = world.CreateBoids(2000);
                Spawn(world, starved, Vector3.down, 0.02f, 100);
                world.RunBoids(starved, 80, Step);
                float starvedEnergy = Average(LiveBoids(starved));

                Assert.Greater(fedEnergy, 1f, $"Boids over food should gain energy (mean {fedEnergy:0.000}).");
                Assert.Less(starvedEnergy, 1f, $"Boids over bare ground should lose energy (mean {starvedEnergy:0.000}).");
            }
        }

        private static float Average(Boid[] boids)
        {
            float sum = 0f;
            foreach (Boid boid in boids)
            {
                sum += boid.Energy;
            }

            return boids.Length == 0 ? 0f : sum / boids.Length;
        }

        [Test]
        public void StarvingBoids_Die_AndTheirBodiesFeedTheSoil()
        {
            using (var world = new WorldHarness(Resolution, c =>
            {
                c.boidUpkeep = 2f;                 // starve within a second or so
                c.boidStartEnergy = 1f;
                c.boidDeathOrganic = 0.05f;
            }))
            {
                BoidSimulation boids = world.CreateBoids(1000);
                Spawn(world, boids, Vector3.up, 0.05f, 100);
                world.RunBoids(boids, 1, Step);    // a newborn only joins in on the next step
                int before = LiveBoids(boids).Length;
                Assert.Greater(before, 80);

                float organicBefore = TotalOrganic(world);
                world.RunBoids(boids, 40, Step);

                Assert.AreEqual(0, LiveBoids(boids).Length, "Every boid should have starved.");

                float gained = TotalOrganic(world) - organicBefore;
                float expected = before * world.Config.boidDeathOrganic;
                Assert.AreEqual(expected, gained, expected * 0.02f, "Each dead boid should return its body mass to the soil.");
            }
        }

        private static float TotalOrganic(WorldHarness world)
        {
            float[] soil = world.Read(world.Soil.Current);
            double sum = 0.0;
            world.ForEachTexel((face, x, y, direction) => sum += soil[world.Index(face, x, y) + 1]);
            return (float)sum;
        }

        [Test]
        public void WellFedBoids_Breed()
        {
            using (var world = new WorldHarness(Resolution, c =>
            {
                c.boidStartEnergy = 3f;            // already above the breeding threshold
                c.boidReproduceEnergy = 2f;
                c.boidBreedCrowdLimit = 200;
                c.boidCrowdCost = 0f;
            }))
            {
                PaintCanopy(world, Vector3.up, 0.6f);

                BoidSimulation boids = world.CreateBoids(4000);
                Spawn(world, boids, Vector3.up, 0.05f, 100);
                int start = LiveBoids(boids).Length;

                world.RunBoids(boids, 60, Step);
                int later = LiveBoids(boids).Length;

                Assert.Greater(later, start * 1.3f, $"A well-fed flock should grow ({start} -> {later}).");
            }
        }

        // ------------------------------------------------------------------ seeds

        [Test]
        public void Boids_PickUpSeedsOverCanopy_AndDropThemAsEvents()
        {
            using (var world = new WorldHarness(Resolution, c =>
            {
                FreeEnergy(c);
                c.boidSeedPickupRate = 1000f;      // pick up at once
                c.boidSeedMinCanopy = 0.1f;
                c.boidSeedCarrySeconds = 1f;
                c.boidSeedCooldown = 0f;
            }))
            {
                PaintCanopy(world, Vector3.up, 0.6f);

                BoidSimulation boids = world.CreateBoids(2000);
                Spawn(world, boids, Vector3.up, 0.05f, 100);
                world.RunBoids(boids, 60, Step);

                Vector4[] events = WorldHarness.ReadBuffer<Vector4>(boids.Drops);
                int drops = 0;
                foreach (Vector4 drop in events)
                {
                    if (drop.w > 0.5f)
                    {
                        drops++;
                        Assert.AreEqual(1f, new Vector3(drop.x, drop.y, drop.z).magnitude, 1e-3f, "A drop event holds a ground direction.");
                        Assert.Less(AngleBetween(new Vector3(drop.x, drop.y, drop.z), Vector3.up), 1.2f, "Seeds are dropped where the boids were: near the forest.");
                    }
                }

                Assert.Greater(drops, 20, "Boids that carried seeds should have dropped some.");

                boids.ClearDrops();
                events = WorldHarness.ReadBuffer<Vector4>(boids.Drops);
                foreach (Vector4 drop in events)
                {
                    Assert.AreEqual(0f, drop.w, "ClearDrops empties the event buffer.");
                }
            }
        }

        [Test]
        public void Boids_DoNotCarrySeeds_OverBareGround()
        {
            using (var world = new WorldHarness(Resolution, c =>
            {
                FreeEnergy(c);
                c.boidSeedPickupRate = 1000f;
                c.boidSeedCarrySeconds = 1f;
            }))
            {
                BoidSimulation boids = world.CreateBoids(2000);
                Spawn(world, boids, Vector3.up, 0.05f, 100);
                world.RunBoids(boids, 60, Step);

                Vector4[] events = WorldHarness.ReadBuffer<Vector4>(boids.Drops);
                foreach (Vector4 drop in events)
                {
                    Assert.AreEqual(0f, drop.w, "With no trees there are no seeds to pick up.");
                }
            }
        }

        // ------------------------------------------------------------------ impacts, pools, starting flocks

        [Test]
        public void Impact_KillsExactlyTheBoidsInsideItsCircle()
        {
            using (var world = new WorldHarness(Resolution, FreeEnergy))
            {
                BoidSimulation boids = world.CreateBoids(2000);
                Vector3 a = Vector3.up;
                Vector3 b = Vector3.down;
                Spawn(world, boids, a, 0.05f, 100);
                Spawn(world, boids, b, 0.05f, 100);
                world.RunBoids(boids, 1, Step);

                boids.KillBoids(a, 0.3f);

                int nearA = 0;
                int nearB = 0;
                foreach (Boid boid in LiveBoids(boids))
                {
                    if (AngleBetween(boid.Position, a) < 1.0f)
                    {
                        nearA++;
                    }
                    else
                    {
                        nearB++;
                    }
                }

                Assert.AreEqual(0, nearA, "Everything inside the blast should be gone.");
                Assert.Greater(nearB, 80, "Boids far from the blast should be untouched.");
            }
        }

        [Test]
        public void ABiggerPool_KeepsTheBoidsThatAlreadyExist()
        {
            using (var world = new WorldHarness(Resolution, FreeEnergy))
            {
                BoidSimulation boids = world.CreateBoids(1000);
                Spawn(world, boids, Vector3.up, 0.05f, 100);
                world.RunBoids(boids, 2, Step);
                int before = LiveBoids(boids).Length;
                Assert.Greater(before, 80);

                boids.Allocate(5000, world.Resolution);

                Assert.AreEqual(5000, boids.PoolSize);
                Assert.AreEqual(before, LiveBoids(boids).Length, "Resizing the pool must not lose boids.");
            }
        }

        [Test]
        public void StartingFlocks_AreScatteredOnDryLandOnly()
        {
            using (var world = new WorldHarness(Resolution, c =>
            {
                FreeEnergy(c);
                c.boidStartCount = 300;
                c.boidStartClusters = 30;
            }))
            {
                Vector3 island = new Vector3(0.4f, 0.6f, 0.7f).normalized;
                Brush().Apply(world.Terrain.Current, island, 0.7f, new Vector4(2f, 0f, 0f, 0f), -10f, 10f);   // the only land

                BoidSimulation boids = world.CreateBoids(2000);
                boids.SeedBoids(world.Terrain.Current, 7);

                Boid[] flock = LiveBoids(boids);
                Assert.Greater(flock.Length, 10, "Some starting clusters should have found the island.");

                float furthest = 0f;
                foreach (Boid boid in flock)
                {
                    furthest = Mathf.Max(furthest, AngleBetween(boid.Position, island));
                }

                Assert.Less(furthest, 0.7f + 0.15f, $"Starting boids must be over land (furthest {furthest:0.00} rad from the island centre).");
            }
        }
    }
}
