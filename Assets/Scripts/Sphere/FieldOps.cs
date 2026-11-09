// =============================================================================
// FieldOps
// -----------------------------------------------------------------------------
// Purpose : Small whole-field operations on cube-sphere textures that several
//           systems need: clearing a field to zero, and copying a field into
//           one of a different resolution.
// Idea    : Both run as compute kernels in FieldInit.compute. Resampling looks
//           up the OLD field at each NEW texel's direction with the seam-correct
//           bilinear sampler (see CubeSphere.hlsl), so it works between any two
//           resolutions and across the cube-face seams. Going to a coarser grid
//           samples rather than averages, so detail finer than one texel is lost.
// Uses    : FieldSet (clear on a new world, carry a world across a quality
//           change) and ClimateController (settle the climate on a coarse grid,
//           then copy it up to the full-size grid).
// =============================================================================

using UnityEngine;

namespace Terrarium.Sphere
{
    /// <summary>Clear and resample operations on cube-sphere field textures.</summary>
    public static class FieldOps
    {
        private const int ThreadGroupSize = 8;

        /// <summary>Sets every channel of every texel of a field texture to zero.</summary>
        /// <param name="fieldInit">FieldInit.compute.</param>
        /// <param name="texture">Six-slice, random-write field texture.</param>
        public static void Clear(ComputeShader fieldInit, RenderTexture texture)
        {
            int kernel = fieldInit.FindKernel("Clear");
            fieldInit.SetInt("_Resolution", texture.width);
            fieldInit.SetTexture(kernel, "_Target", texture);
            Dispatch(fieldInit, kernel, texture.width);
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="target"/>, which may have a different
        /// resolution. Both must have the same number of channels (RFloat or ARGBFloat).
        /// </summary>
        public static void Resample(ComputeShader fieldInit, RenderTexture source, RenderTexture target)
        {
            bool singleChannel = target.format == RenderTextureFormat.RFloat;
            int kernel = fieldInit.FindKernel(singleChannel ? "Resample1" : "Resample4");

            fieldInit.SetInt("_Resolution", target.width);
            fieldInit.SetInt("_SourceResolution", source.width);
            fieldInit.SetTexture(kernel, singleChannel ? "_Source1" : "_Source4", source);
            fieldInit.SetTexture(kernel, singleChannel ? "_Target1" : "_Target4", target);
            Dispatch(fieldInit, kernel, target.width);
        }

        private static void Dispatch(ComputeShader shader, int kernel, int resolution)
        {
            int groups = (resolution + ThreadGroupSize - 1) / ThreadGroupSize;
            shader.Dispatch(kernel, groups, groups, CubeSphere.FaceCount);
        }
    }
}
