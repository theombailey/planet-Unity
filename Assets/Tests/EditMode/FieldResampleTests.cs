// =============================================================================
// FieldResampleTests (EditMode, runs compute shaders)
// -----------------------------------------------------------------------------
// Purpose : Checks that FieldInit.compute's Resample4 kernel carries a field
//           from one resolution to another, so changing the quality preset
//           keeps the painted / simulated state instead of wiping it.
// Method  : Paint a blob (known formula) onto a 64-texel field, resample it to
//           128 and to 32, and compare many texels of the result with the
//           blob's exact formula. A blob straddling a cube corner is used so
//           the seam handling is exercised too.
// =============================================================================

using NUnit.Framework;
using Terrarium.Sphere;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Tests
{
    public sealed class FieldResampleTests
    {
        private const float BrushRadius = 0.5f;

        private ComputeShader fieldInit;
        private ComputeShader brushShader;

        [SetUp]
        public void LoadShaders()
        {
            fieldInit = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/FieldInit.compute");
            brushShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Brush.compute");
            Assert.IsNotNull(fieldInit);
            Assert.IsNotNull(brushShader);
        }

        [TestCase(128)]
        [TestCase(32)]
        public void FieldInit_Resample_PreservesAPaintedBlob(int targetResolution)
        {
            Vector3 centre = new Vector3(1f, 1f, 1f).normalized;   // a cube corner: three faces meet here
            RenderTexture source = CreateCleared(64);
            RenderTexture target = CreateCleared(targetResolution);

            try
            {
                new FieldBrush(brushShader).Apply(source, centre, BrushRadius, new Vector4(1f, 0f, 0f, 0f), 0f, 1f);

                int kernel = fieldInit.FindKernel("Resample4");
                fieldInit.SetInt("_Resolution", targetResolution);
                fieldInit.SetInt("_SourceResolution", 64);
                fieldInit.SetTexture(kernel, "_Source4", source);
                fieldInit.SetTexture(kernel, "_Target4", target);
                int groups = (targetResolution + 7) / 8;
                fieldInit.Dispatch(kernel, groups, groups, CubeSphere.FaceCount);

                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(target, 0);
                request.WaitForCompletion();
                Assert.IsFalse(request.hasError);

                // Coarser grids and bilinear sampling cost some accuracy; a wiped or misplaced field would be wrong by ~1.
                float tolerance = targetResolution < 64 ? 0.12f : 0.06f;
                float worst = 0f;
                float peak = 0f;

                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    NativeArray<float> slice = request.GetData<float>(face);
                    for (int y = 0; y < targetResolution; y++)
                    {
                        for (int x = 0; x < targetResolution; x++)
                        {
                            Vector3 direction = CubeSphere.TexelCentreDir(face, x, y, targetResolution);
                            float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f));
                            float expected = 1f - Smoothstep(0f, BrushRadius, angle);
                            float actual = slice[(y * targetResolution + x) * 4];

                            worst = Mathf.Max(worst, Mathf.Abs(actual - expected));
                            peak = Mathf.Max(peak, actual);
                        }
                    }
                }

                Assert.Less(worst, tolerance, $"Resampled blob differs from the exact blob by {worst:0.000}.");
                Assert.Greater(peak, 0.9f, "The blob's peak was lost.");
            }
            finally
            {
                Destroy(source);
                Destroy(target);
            }
        }

        private RenderTexture CreateCleared(int resolution)
        {
            var texture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = CubeSphere.FaceCount,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };
            texture.Create();

            int kernel = fieldInit.FindKernel("Clear");
            fieldInit.SetInt("_Resolution", resolution);
            fieldInit.SetTexture(kernel, "_Target", texture);
            int groups = (resolution + 7) / 8;
            fieldInit.Dispatch(kernel, groups, groups, CubeSphere.FaceCount);
            return texture;
        }

        private static void Destroy(RenderTexture texture)
        {
            texture.Release();
            Object.DestroyImmediate(texture);
        }

        // Same smoothstep as HLSL: 0 below a, 1 above b, smooth in between.
        private static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
