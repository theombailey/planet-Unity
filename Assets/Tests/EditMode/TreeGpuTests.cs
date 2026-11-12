// =============================================================================
// TreeGpuTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks Trees.compute:
//           TreeSample  - reads the world at a point exactly as a direct readback does,
//           TreeUptake  - takes the requested nutrients (no more than the soil holds,
//                         never below zero), also across a cube corner,
//           CanopySplat - paints a tree's shade: full at its centre, none beyond its
//                         radius, and evenly across the three faces at a cube corner.
// =============================================================================

using NUnit.Framework;
using Terrarium.Sphere;
using UnityEditor;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class TreeGpuTests
    {
        private const int Resolution = 64;

        private struct Gpu
        {
            public Vector3 Direction;
            public float CanopyRadius;
            public float Uptake;
            public float Strength;
            public float CosRadius;
            public float Pad;
        }

        private static ComputeShader Shader()
        {
            return AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Trees.compute");
        }

        private static ComputeShader Brush()
        {
            return AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Brush.compute");
        }

        private static Gpu Tree(Vector3 direction, float radius = 0.1f, float uptake = 0f, float strength = 1f)
        {
            return new Gpu { Direction = direction.normalized, CanopyRadius = radius, Uptake = uptake, Strength = strength, CosRadius = Mathf.Cos(radius) };
        }

        private static void Run(WorldHarness world, Gpu[] trees, string kernelName, int sampleCount = 0, Vector4[] samples = null, ComputeBuffer sampleOut = null)
        {
            ComputeShader shader = Shader();
            int kernel = shader.FindKernel(kernelName);
            var treeBuffer = new ComputeBuffer(Mathf.Max(1, trees.Length), 32);
            treeBuffer.SetData(trees);

            shader.SetInt("_Resolution", Resolution);
            shader.SetInt("_TreeCount", trees.Length);
            shader.SetInt("_UptakeRadius", 4);
            shader.SetInt("_SampleCount", sampleCount);
            shader.SetBuffer(kernel, "_Trees", treeBuffer);
            shader.SetTexture(kernel, "_SoilRW", world.Soil.Current);
            shader.SetTexture(kernel, "_Soil", world.Soil.Current);
            shader.SetTexture(kernel, "_Climate", world.Climate.Current);
            shader.SetTexture(kernel, "_Terrain", world.Terrain.Current);
            shader.SetTexture(kernel, "_Land", world.Land.Current);

            ComputeBuffer dirBuffer = null;
            if (kernelName == "TreeSample")
            {
                dirBuffer = new ComputeBuffer(Mathf.Max(1, sampleCount), 16);
                dirBuffer.SetData(samples);
                shader.SetBuffer(kernel, "_SampleDirs", dirBuffer);
                shader.SetBuffer(kernel, "_Samples", sampleOut);
            }

            int groups = kernelName == "CanopySplat" ? (Resolution + 7) / 8 : 1;
            shader.Dispatch(kernel, kernelName == "CanopySplat" ? groups : Mathf.Max(1, (Mathf.Max(trees.Length, sampleCount) + 63) / 64), kernelName == "CanopySplat" ? groups : 1, kernelName == "CanopySplat" ? 6 : 1);

            treeBuffer.Release();
            dirBuffer?.Release();
        }

        private static float TotalNutrients(WorldHarness world, float[] soil)
        {
            double sum = 0.0;
            world.ForEachTexel((face, x, y, direction) => sum += soil[world.Index(face, x, y)]);
            return (float)sum;
        }

        [Test]
        public void TreeSample_ReadsTheWorldExactlyLikeADirectReadback()
        {
            using (var world = new WorldHarness(Resolution))
            {
                world.Generate(seed: 3, landFraction: 0.4f, spinUp: true);
                var brush = new FieldBrush(Brush());
                brush.Apply(world.Soil.Current, Vector3.right, 0.3f, new Vector4(0.7f, 0f, 0.4f, 0f), 0f, 5f);   // nutrients (R) and canopy (B)
                brush.Apply(world.Land.Current, Vector3.right, 0.3f, new Vector4(0.2f, 0.4f, 0f, 0f), 0f, 10f);   // a town edge and a field

                float[] soil = world.Read(world.Soil.Current);
                float[] climate = world.Read(world.Climate.Current);
                float[] terrain = world.Read(world.Terrain.Current);
                float[] land = world.Read(world.Land.Current);

                var directions = new[] { new Vector4(1f, 0f, 0f, 0f), new Vector4(0.3f, 0.8f, -0.5f, 0f) };
                using (var output = new ComputeBuffer(4, 16))
                {
                    Run(world, new Gpu[0], "TreeSample", 2, directions, output);
                    var answer = new Vector4[4];
                    output.GetData(answer);

                    for (int s = 0; s < 2; s++)
                    {
                        Vector3 direction = ((Vector3)directions[s]).normalized;
                        Texel texel = CubeSphere.DirToTexel(direction, Resolution);
                        int i = world.Index(texel.Face, texel.X, texel.Y);

                        // Nutrients are the mean of the 3x3 neighbourhood.
                        double mean = 0.0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                Texel n = CubeSphere.ResolveTexel(texel.Face, texel.X + dx, texel.Y + dy, Resolution);
                                mean += soil[world.Index(n.Face, n.X, n.Y)];
                            }
                        }

                        Assert.AreEqual((float)(mean / 9.0), answer[s * 2].x, 1e-4f, "nutrients");
                        Assert.AreEqual(climate[i + 1], answer[s * 2].y, 1e-5f, "moisture");
                        Assert.AreEqual(climate[i], answer[s * 2].z, 1e-4f, "temperature");
                        Assert.AreEqual(soil[i + 2], answer[s * 2].w, 1e-5f, "canopy");
                        float cleared = Mathf.Clamp01(Mathf.Max(land[i], land[i + 1]));
                        Assert.AreEqual(cleared, answer[s * 2 + 1].x, 1e-5f, "cleared land");
                        Assert.AreEqual(climate[i + 2], answer[s * 2 + 1].y, 1e-5f, "insolation");
                        Assert.AreEqual(terrain[i], answer[s * 2 + 1].z, 1e-5f, "height");
                    }
                }
            }
        }

        [Test]
        public void TreeUptake_TakesTheRequestedAmount_AcrossACubeCorner()
        {
            using (var world = new WorldHarness(Resolution))
            {
                // Rich soil everywhere (1.0), a tree on the cube corner asking for 5 units.
                new FieldBrush(Brush()).Apply(world.Soil.Current, Vector3.up, Mathf.PI, new Vector4(1000f, 0f, 0f, 0f), 0f, 1f);
                float before = TotalNutrients(world, world.Read(world.Soil.Current));

                Run(world, new[] { Tree(new Vector3(1f, 1f, 1f), uptake: 5f) }, "TreeUptake");
                float[] after = world.Read(world.Soil.Current);

                Assert.AreEqual(5f, before - TotalNutrients(world, after), 0.05f, "The tree must take what it asked for.");

                float lowest = float.MaxValue;
                world.ForEachTexel((face, x, y, direction) => lowest = Mathf.Min(lowest, after[world.Index(face, x, y)]));
                Assert.GreaterOrEqual(lowest, 0f);
            }
        }

        [Test]
        public void TreeUptake_NeverTakesMoreThanTheSoilHolds()
        {
            using (var world = new WorldHarness(Resolution))
            {
                // Almost nothing in the soil but a tree asking for a lot.
                new FieldBrush(Brush()).Apply(world.Soil.Current, Vector3.up, Mathf.PI, new Vector4(1000f, 0f, 0f, 0f), 0f, 0.01f);
                float before = TotalNutrients(world, world.Read(world.Soil.Current));

                Run(world, new[] { Tree(Vector3.right, uptake: 50f) }, "TreeUptake");
                float[] after = world.Read(world.Soil.Current);

                world.ForEachTexel((face, x, y, direction) => Assert.GreaterOrEqual(after[world.Index(face, x, y)], 0f, "Soil went negative."));
                Assert.Less(TotalNutrients(world, after), before, "Some nutrients should have been taken.");
            }
        }

        [Test]
        public void CanopySplat_IsFullAtTheCentre_AndNoneBeyondTheRadius()
        {
            using (var world = new WorldHarness(Resolution))
            {
                Vector3 centre = new Vector3(0.3f, 0.5f, 0.8f).normalized;
                const float radius = 0.25f;

                Run(world, new[] { Tree(centre, radius, strength: 0.8f) }, "CanopySplat");
                float[] soil = world.Read(world.Soil.Current);

                Texel middle = CubeSphere.DirToTexel(centre, Resolution);
                Assert.AreEqual(0.8f, soil[world.Index(middle.Face, middle.X, middle.Y) + 2], 0.05f, "Full strength at the tree.");

                world.ForEachTexel((face, x, y, direction) =>
                {
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f));
                    float canopy = soil[world.Index(face, x, y) + 2];
                    if (angle > radius + 1e-3f)
                    {
                        Assert.AreEqual(0f, canopy, 1e-6f, "No shade beyond the crown.");
                    }
                    else
                    {
                        Assert.That(canopy, Is.InRange(0f, 0.8f + 1e-4f));
                    }
                });
            }
        }

        [Test]
        public void CanopySplat_AtACubeCorner_IsEvenAcrossTheThreeFaces_AndOverlappingTreesAdd()
        {
            using (var world = new WorldHarness(Resolution))
            {
                Run(world, new[] { Tree(new Vector3(1f, 1f, 1f), 0.3f) }, "CanopySplat");
                float[] soil = world.Read(world.Soil.Current);

                var perFace = new double[CubeSphere.FaceCount];
                world.ForEachTexel((face, x, y, direction) => perFace[face] += soil[world.Index(face, x, y) + 2]);

                Assert.Greater(perFace[0], 1.0);
                Assert.AreEqual(perFace[0], perFace[2], perFace[0] * 1e-3);
                Assert.AreEqual(perFace[0], perFace[4], perFace[0] * 1e-3);
                Assert.AreEqual(0.0, perFace[1] + perFace[3] + perFace[5], 1e-6);

                // Two trees on top of each other give twice the shade, up to the cap of 2.
                Run(world, new[] { Tree(Vector3.right, 0.2f), Tree(Vector3.right, 0.2f) }, "CanopySplat");
                float[] doubled = world.Read(world.Soil.Current);
                Texel centre = CubeSphere.DirToTexel(Vector3.right, Resolution);
                Assert.AreEqual(2f, doubled[world.Index(centre.Face, centre.X, centre.Y) + 2], 0.1f);
            }
        }
    }
}
