// =============================================================================
// CubeSphereGpuTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : (e) Proves the HLSL cube-sphere functions agree with the C# ones, by
//           running CubeSphereTest.compute on fixed inputs and comparing. Also
//           checks Brush.compute is seam-safe: a blob centred on a cube corner
//           or edge splits EXACTLY equally between the faces that meet there.
// Why EditMode: compute shaders can be dispatched from the editor without
//           entering Play mode, which keeps the test fast and lets it run from
//           the Test Runner with no domain reload.
// Tolerances: the GPU's atan and tan are less precise than the CPU's, so face
//           coordinates are compared to 2e-4. A direction within that of a texel
//           boundary can fall either side of it on the two processors. So a
//           texel mismatch is accepted only if it is a boundary case: the GPU's
//           texel must be right next to the CPU's (within 1.6 texel spacings),
//           and at most 2% of cases may differ at all. A logic error would
//           give far-away texels or many more mismatches.
// =============================================================================

using NUnit.Framework;
using Terrarium.Sphere;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Tests
{
    public sealed class CubeSphereGpuTests
    {
        private const int Resolution = 64;
        private const int Count = 4096;

        private ComputeShader parityShader;
        private ComputeShader brushShader;

        [SetUp]
        public void LoadShaders()
        {
            parityShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tests/EditMode/CubeSphereTest.compute");
            brushShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Brush.compute");
            Assert.IsNotNull(parityShader, "CubeSphereTest.compute not found.");
            Assert.IsNotNull(brushShader, "Brush.compute not found.");
            Assert.IsTrue(SystemInfo.supportsComputeShaders, "This test needs compute shader support.");
        }

        [Test]
        public void CubeSphere_GpuAndCpu_DirToFaceAgree()
        {
            var random = new System.Random(555);
            var inputs = new Vector4[Count];
            for (int i = 0; i < Count; i++)
            {
                inputs[i] = RandomDirection(random);
            }

            var faceST = new Vector4[Count];
            var texels = new Vector4Int4[Count];
            RunDirToFace(inputs, faceST, texels);

            int texelMismatches = 0;
            for (int i = 0; i < Count; i++)
            {
                Vector3 direction = inputs[i];
                CubeSphere.DirToFaceST(direction, out int face, out float s, out float t);

                Assert.AreEqual(face, (int)faceST[i].x, $"Face disagrees for {direction}.");
                Assert.AreEqual(s, faceST[i].y, 2e-4f, $"s disagrees for {direction}.");
                Assert.AreEqual(t, faceST[i].z, 2e-4f, $"t disagrees for {direction}.");

                Texel cpu = CubeSphere.DirToTexel(direction, Resolution);
                var gpu = new Texel(texels[i].Z, texels[i].X, texels[i].Y);
                if (!cpu.SameAs(gpu))
                {
                    texelMismatches++;
                    AssertNextTo(cpu, gpu, $"Direction {direction}");
                }
            }

            Assert.LessOrEqual(texelMismatches, Count / 50, $"{texelMismatches} of {Count} texel indices differ between CPU and GPU.");
        }

        [Test]
        public void CubeSphere_GpuAndCpu_FaceToDirAgree()
        {
            var random = new System.Random(556);
            var inputs = new Vector4[Count];
            for (int i = 0; i < Count; i++)
            {
                // Include values slightly beyond [-1, 1], which neighbour lookups use.
                inputs[i] = new Vector4(random.Next(0, 6), (float)(random.NextDouble() * 2.2 - 1.1), (float)(random.NextDouble() * 2.2 - 1.1), 0f);
            }

            var outputs = new Vector4[Count];
            using (var inBuffer = new ComputeBuffer(Count, 16))
            using (var outBuffer = new ComputeBuffer(Count, 16))
            {
                inBuffer.SetData(inputs);
                int kernel = parityShader.FindKernel("FaceToDir");
                parityShader.SetInt("_Resolution", Resolution);
                parityShader.SetInt("_Count", Count);
                parityShader.SetBuffer(kernel, "_FaceSTInputs", inBuffer);
                parityShader.SetBuffer(kernel, "_DirOutputs", outBuffer);
                parityShader.Dispatch(kernel, Count / 64, 1, 1);
                outBuffer.GetData(outputs);
            }

            for (int i = 0; i < Count; i++)
            {
                Vector3 cpu = CubeSphere.FaceSTToDir((int)inputs[i].x, inputs[i].y, inputs[i].z);
                Assert.Less(Vector3.Distance(cpu, outputs[i]), 2e-4f, $"Direction disagrees for face {inputs[i].x}, s {inputs[i].y}, t {inputs[i].z}.");
            }
        }

        [Test]
        public void CubeSphere_GpuAndCpu_NeighbourTexelsAgree()
        {
            var inputs = new Vector4[Count];
            var cpuExpected = new Texel[Count];
            var random = new System.Random(557);

            for (int i = 0; i < Count; i++)
            {
                int face = random.Next(0, 6);
                bool useEdge = i % 2 == 0;
                int x = useEdge ? (random.Next(0, 2) == 0 ? 0 : Resolution - 1) : random.Next(0, Resolution);
                int y = random.Next(0, Resolution);
                int dx = random.Next(-1, 2);
                int dy = random.Next(-1, 2);

                inputs[i] = new Vector4(x, y, face, (dx + 1) + 3 * (dy + 1));
                cpuExpected[i] = CubeSphere.NeighbourTexel(face, x, y, dx, dy, Resolution);
            }

            var outputs = new Vector4Int4[Count];
            using (var inBuffer = new ComputeBuffer(Count, 16))
            using (var outBuffer = new ComputeBuffer(Count, 16))
            {
                var packed = new Vector4Int4[Count];
                for (int i = 0; i < Count; i++)
                {
                    packed[i] = new Vector4Int4 { X = (int)inputs[i].x, Y = (int)inputs[i].y, Z = (int)inputs[i].z, W = (int)inputs[i].w };
                }

                inBuffer.SetData(packed);
                int kernel = parityShader.FindKernel("Neighbours");
                parityShader.SetInt("_Resolution", Resolution);
                parityShader.SetInt("_Count", Count);
                parityShader.SetBuffer(kernel, "_NeighbourInputs", inBuffer);
                parityShader.SetBuffer(kernel, "_NeighbourOutputs", outBuffer);
                parityShader.Dispatch(kernel, Count / 64, 1, 1);
                outBuffer.GetData(outputs);
            }

            int mismatches = 0;
            for (int i = 0; i < Count; i++)
            {
                var gpu = new Texel(outputs[i].Z, outputs[i].X, outputs[i].Y);
                if (!cpuExpected[i].SameAs(gpu))
                {
                    mismatches++;
                    AssertNextTo(cpuExpected[i], gpu, $"Neighbour input {inputs[i]}");
                }
            }

            Assert.LessOrEqual(mismatches, Count / 50, $"{mismatches} of {Count} neighbour texels differ between CPU and GPU.");
        }

        [Test]
        public void Brush_AtCubeCorner_SplitsEquallyBetweenThreeFaces()
        {
            // The direction (1,1,1) is the corner where faces +X, +Y and +Z meet.
            float[] sums = PaintAndSumPerFace(new Vector3(1f, 1f, 1f), angularRadius: 0.3f);

            Assert.Greater(sums[0], 1f, "The blob should reach the +X face.");
            Assert.AreEqual(sums[0], sums[2], sums[0] * 1e-3f, "+X and +Y should receive the same paint.");
            Assert.AreEqual(sums[0], sums[4], sums[0] * 1e-3f, "+X and +Z should receive the same paint.");
            Assert.AreEqual(0f, sums[1] + sums[3] + sums[5], 1e-6f, "The faces on the far side of the planet must stay clean.");
        }

        [Test]
        public void Brush_AtEdgeMidpoint_SplitsEquallyBetweenTwoFaces()
        {
            // The direction (1,1,0) lies on the edge between faces +X and +Y.
            float[] sums = PaintAndSumPerFace(new Vector3(1f, 1f, 0f), angularRadius: 0.3f);

            Assert.Greater(sums[0], 1f);
            Assert.AreEqual(sums[0], sums[2], sums[0] * 1e-3f, "+X and +Y should receive the same paint.");
            Assert.AreEqual(0f, sums[1] + sums[3] + sums[5], 1e-6f);
        }

        [Test]
        public void Brush_LeavesUntouchedChannelsAlone()
        {
            RenderTexture target = CreateField();
            try
            {
                FillTarget(target, new Color(0.5f, 0.6f, 0.7f, 0.8f));
                var brush = new FieldBrush(brushShader);

                brush.Apply(target, Vector3.right, 0.4f, new Vector4(0.1f, 0f, 0f, 0f), 0f, 1f);

                // Texel nearest the brush centre: R changed, G B A did not.
                Texel centre = CubeSphere.DirToTexel(Vector3.right, Resolution);
                Color value = ReadTexel(target, centre);
                Assert.Greater(value.r, 0.55f);
                Assert.AreEqual(0.6f, value.g, 1e-6f);
                Assert.AreEqual(0.7f, value.b, 1e-6f);
                Assert.AreEqual(0.8f, value.a, 1e-6f);
            }
            finally
            {
                DestroyField(target);
            }
        }

        // ------------------------------------------------------------------ helpers

        // A boundary-case mismatch must still be the adjacent texel, not a far-away one.
        private static void AssertNextTo(Texel cpu, Texel gpu, string what)
        {
            Vector3 a = CubeSphere.TexelCentreDir(cpu.Face, cpu.X, cpu.Y, Resolution);
            Vector3 b = CubeSphere.TexelCentreDir(gpu.Face, gpu.X, gpu.Y, Resolution);
            float spacings = Vector3.Angle(a, b) * Mathf.Deg2Rad / (Mathf.PI * 0.5f / Resolution);

            Assert.Less(spacings, 1.6f, $"{what}: CPU says {cpu}, GPU says {gpu}, which is {spacings:0.0} texels apart.");
        }

        private struct Vector4Int4
        {
            public int X;
            public int Y;
            public int Z;
            public int W;
        }

        private static Vector4 RandomDirection(System.Random random)
        {
            while (true)
            {
                var v = new Vector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
                if (v.sqrMagnitude > 0.01f && v.sqrMagnitude <= 1f)
                {
                    return v.normalized;
                }
            }
        }

        private void RunDirToFace(Vector4[] inputs, Vector4[] faceST, Vector4Int4[] texels)
        {
            using (var inBuffer = new ComputeBuffer(Count, 16))
            using (var faceBuffer = new ComputeBuffer(Count, 16))
            using (var texelBuffer = new ComputeBuffer(Count, 16))
            {
                inBuffer.SetData(inputs);
                int kernel = parityShader.FindKernel("DirToFace");
                parityShader.SetInt("_Resolution", Resolution);
                parityShader.SetInt("_Count", Count);
                parityShader.SetBuffer(kernel, "_DirInputs", inBuffer);
                parityShader.SetBuffer(kernel, "_FaceSTOutputs", faceBuffer);
                parityShader.SetBuffer(kernel, "_TexelOutputs", texelBuffer);
                parityShader.Dispatch(kernel, Count / 64, 1, 1);
                faceBuffer.GetData(faceST);
                texelBuffer.GetData(texels);
            }
        }

        private static RenderTexture CreateField()
        {
            var texture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = CubeSphere.FaceCount,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };
            texture.Create();
            return texture;
        }

        private static void FillTarget(RenderTexture target, Color color)
        {
            var data = new float[Resolution * Resolution * 4];
            for (int i = 0; i < data.Length; i += 4)
            {
                data[i] = color.r;
                data[i + 1] = color.g;
                data[i + 2] = color.b;
                data[i + 3] = color.a;
            }

            var temp = new Texture2DArray(Resolution, Resolution, CubeSphere.FaceCount, TextureFormat.RGBAFloat, false, true);
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                temp.SetPixelData(data, 0, face);
            }

            temp.Apply();
            Graphics.CopyTexture(temp, target);
            Object.DestroyImmediate(temp);
        }

        private float[] PaintAndSumPerFace(Vector3 direction, float angularRadius)
        {
            RenderTexture target = CreateField();
            try
            {
                FillTarget(target, Color.black);
                new FieldBrush(brushShader).Apply(target, direction, angularRadius, new Vector4(1f, 0f, 0f, 0f), 0f, 1000f);

                var sums = new float[CubeSphere.FaceCount];
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError, "GPU readback failed.");

                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    NativeArray<float> slice = request.GetData<float>(face);
                    float sum = 0f;
                    for (int i = 0; i < slice.Length; i += 4)
                    {
                        sum += slice[i];
                    }

                    sums[face] = sum;
                }

                return sums;
            }
            finally
            {
                DestroyField(target);
            }
        }

        private static void DestroyField(RenderTexture target)
        {
            target.Release();
            Object.DestroyImmediate(target);
        }

        private static Color ReadTexel(RenderTexture target, Texel texel)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0);
            request.WaitForCompletion();
            NativeArray<float> slice = request.GetData<float>(texel.Face);
            int index = (texel.Y * Resolution + texel.X) * 4;
            return new Color(slice[index], slice[index + 1], slice[index + 2], slice[index + 3]);
        }
    }
}
