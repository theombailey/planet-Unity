// =============================================================================
// StatsGpuTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks Stats.compute: the GPU's totals match the same averages worked
//           out on the CPU from a full readback of the fields, and the boid
//           reduction matches a readback of the boid buffers.
// =============================================================================

using NUnit.Framework;
using Terrarium.Boids;
using Terrarium.Sphere;
using UnityEditor;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class StatsGpuTests
    {
        private const int Resolution = 64;

        private static uint[] Reduce(WorldHarness world, BoidSimulation boids)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Stats.compute");
            var accumulator = new ComputeBuffer(16, 4);
            try
            {
                int clear = shader.FindKernel("ClearStats");
                int fields = shader.FindKernel("ReduceFields");
                int boidKernel = shader.FindKernel("ReduceBoids");
                shader.SetInt("_Resolution", Resolution);

                shader.SetBuffer(clear, "_StatsAcc", accumulator);
                shader.Dispatch(clear, 1, 1, 1);

                shader.SetBuffer(fields, "_StatsAcc", accumulator);
                shader.SetTexture(fields, "_Soil", world.Soil.Current);
                shader.SetTexture(fields, "_Climate", world.Climate.Current);
                shader.SetTexture(fields, "_Terrain", world.Terrain.Current);
                shader.SetTexture(fields, "_Trail", world.Trail.Current);
                shader.SetTexture(fields, "_Land", world.Land.Current);
                shader.Dispatch(fields, (Resolution + 7) / 8, (Resolution + 7) / 8, CubeSphere.FaceCount);

                if (boids != null)
                {
                    shader.SetInt("_PoolSize", boids.PoolSize);
                    shader.SetBuffer(boidKernel, "_StatsAcc", accumulator);
                    shader.SetBuffer(boidKernel, "_PosEnergy", boids.PosEnergy);
                    shader.SetBuffer(boidKernel, "_VelSeed", boids.VelSeed);
                    shader.SetBuffer(boidKernel, "_Alive", boids.Alive);
                    shader.Dispatch(boidKernel, (boids.PoolSize + 63) / 64, 1, 1);
                }

                return WorldHarness.ReadBuffer<uint>(accumulator);
            }
            finally
            {
                accumulator.Release();
            }
        }

        private static float CpuMean(WorldHarness world, float[] data, int channel)
        {
            double sum = 0.0;
            int count = 0;
            world.ForEachTexel((face, x, y, direction) =>
            {
                sum += data[world.Index(face, x, y) + channel];
                count++;
            });
            return (float)(sum / count);
        }

        [Test]
        public void FieldReduction_MatchesAFullReadback()
        {
            using (var world = new WorldHarness(Resolution))
            {
                var brush = new FieldBrush(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Brush.compute"));
                brush.Apply(world.Terrain.Current, new Vector3(1f, 1f, 0f).normalized, 0.9f, new Vector4(2f, 0f, 0f, 0f), -10f, 10f);   // some land
                brush.Apply(world.Soil.Current, Vector3.up, 0.8f, new Vector4(3f, 1.5f, 1.2f, 0f), 0f, 8f);
                brush.Apply(world.Soil.Current, Vector3.left, 0.5f, new Vector4(1f, 0.5f, 0.4f, 0f), 0f, 8f);
                brush.Apply(world.Climate.Current, Vector3.forward, 1.1f, new Vector4(30f, 0f, 0f, 0f), -80f, 90f);
                brush.Apply(world.Climate.Current, Vector3.back, 0.9f, new Vector4(-25f, 0f, 0f, 0f), -80f, 90f);

                uint[] acc = Reduce(world, null);
                float texels = Resolution * Resolution * CubeSphere.FaceCount;

                float[] soil = world.Read(world.Soil.Current);
                float[] climate = world.Read(world.Climate.Current);
                float[] terrain = world.Read(world.Terrain.Current);

                Assert.AreEqual(CpuMean(world, soil, 0), acc[0] / (128f * texels), 0.01f, "mean nutrients");
                Assert.AreEqual(CpuMean(world, soil, 1), acc[1] / (128f * texels), 0.01f, "mean organic matter");
                Assert.AreEqual(CpuMean(world, soil, 2), acc[2] / (128f * texels), 0.01f, "mean canopy");
                Assert.AreEqual(CpuMean(world, climate, 0), acc[3] / (16f * texels) - 100f, 0.1f, "mean temperature");

                int land = 0;
                world.ForEachTexel((face, x, y, direction) =>
                {
                    if (terrain[world.Index(face, x, y)] > 0f)
                    {
                        land++;
                    }
                });
                Assert.AreEqual(land, (int)acc[5], "land texel count");
                Assert.Greater(acc[0], 0u, "the painted nutrients must show up");
            }
        }

        [Test]
        public void BoidReduction_MatchesAReadbackOfTheBoids()
        {
            using (var world = new WorldHarness(Resolution, c => c.boidStartEnergy = 5f))
            {
                BoidSimulation boids = world.CreateBoids(2000);
                boids.SpawnBoids(Vector3.up, 0.1f, 150, world.Terrain.Current);
                world.RunBoids(boids, 5, 0.1f);

                uint[] acc = Reduce(world, boids);

                Vector4[] posEnergy = WorldHarness.ReadBuffer<Vector4>(boids.PosEnergy);
                Vector4[] velSeed = WorldHarness.ReadBuffer<Vector4>(boids.VelSeed);
                uint[] alive = WorldHarness.ReadBuffer<uint>(boids.Alive);

                int live = 0;
                double speed = 0.0;
                double energy = 0.0;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] == 1u)
                    {
                        live++;
                        speed += new Vector3(velSeed[i].x, velSeed[i].y, velSeed[i].z).magnitude;
                        energy += posEnergy[i].w;
                    }
                }

                Assert.Greater(live, 100);
                Assert.AreEqual(live, (int)acc[8], "live boid count");
                Assert.AreEqual((float)(speed / live), acc[9] / (10000f * acc[8]), 0.0005f, "mean boid speed");
                Assert.AreEqual((float)(energy / live), acc[10] / (100f * acc[8]), 0.02f, "mean boid energy");
            }
        }
    }
}
