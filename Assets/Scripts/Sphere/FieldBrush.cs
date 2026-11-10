// =============================================================================
// FieldBrush
// -----------------------------------------------------------------------------
// Purpose : The C# side of Brush.compute: paints a soft round blob onto any
//           cube-sphere field texture. Every tool in the game goes through
//           this one class.
// Idea    : See Brush.compute. The brush is described by a direction on the
//           planet (in the planet's own, non-rotating frame), a radius as an
//           angle, and an amount per channel.
// Radius  : Tools think in world units. Over a sphere of radius R, an arc of
//           length L subtends an angle of L / R radians, so
//               angularRadius = worldRadius / planetRadius.
// =============================================================================

using UnityEngine;

namespace Terrarium.Sphere
{
    /// <summary>Dispatches Brush.compute to paint onto a field texture.</summary>
    public sealed class FieldBrush
    {
        private const int ThreadGroupSize = 8;
        private const float MinAngularRadius = 0.002f;

        private readonly ComputeShader shader;
        private readonly int kernel;
        private readonly int craterKernel;

        /// <summary>Creates a brush that uses the given Brush.compute.</summary>
        public FieldBrush(ComputeShader brushShader)
        {
            shader = brushShader;
            kernel = shader.FindKernel("BrushAdd");
            craterKernel = shader.FindKernel("Crater");
        }

        /// <summary>
        /// A meteor impact: digs a bowl into the terrain, raises a rim and scatters organic matter around it.
        /// </summary>
        /// <param name="terrain">Terrain field texture (height in R), changed in place.</param>
        /// <param name="soil">Soil field texture (organic matter in G), changed in place.</param>
        /// <param name="localDirection">Impact point as a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Crater radius in radians.</param>
        /// <param name="depth">How far the centre is dug down, in height units (the height range is -1..1).</param>
        /// <param name="rimHeight">How far the rim is raised.</param>
        /// <param name="organicAmount">Organic matter thrown onto the ring just outside the crater.</param>
        public void ApplyCrater(RenderTexture terrain, RenderTexture soil, Vector3 localDirection, float angularRadius, float depth, float rimHeight, float organicAmount)
        {
            int resolution = terrain.width;
            int groups = (resolution + ThreadGroupSize - 1) / ThreadGroupSize;

            shader.SetInt("_Resolution", resolution);
            shader.SetTexture(craterKernel, "_CraterTerrain", terrain);
            shader.SetTexture(craterKernel, "_CraterSoil", soil);
            shader.SetVector("_BrushDirection", localDirection.normalized);
            shader.SetFloat("_BrushAngularRadius", Mathf.Max(angularRadius, MinAngularRadius));
            shader.SetFloat("_CraterDepth", depth);
            shader.SetFloat("_CraterRim", rimHeight);
            shader.SetFloat("_CraterOrganic", organicAmount);
            shader.Dispatch(craterKernel, groups, groups, CubeSphere.FaceCount);
        }

        /// <summary>Converts a radius in world units into the angle the brush uses.</summary>
        public static float AngularRadius(float worldRadius, float planetRadius)
        {
            return Mathf.Clamp(worldRadius / planetRadius, MinAngularRadius, Mathf.PI);
        }

        /// <summary>
        /// Adds <paramref name="amount"/> (scaled by a soft falloff) to a field texture.
        /// </summary>
        /// <param name="target">A six-slice, random-write field texture.</param>
        /// <param name="localDirection">Brush centre as a unit vector in the planet's local space.</param>
        /// <param name="angularRadius">Brush radius in radians.</param>
        /// <param name="amount">Added per channel at the brush centre. A zero channel is left untouched.</param>
        /// <param name="clampMin">Lowest value a painted channel may reach.</param>
        /// <param name="clampMax">Highest value a painted channel may reach.</param>
        public void Apply(RenderTexture target, Vector3 localDirection, float angularRadius, Vector4 amount, float clampMin, float clampMax)
        {
            int resolution = target.width;
            int groups = (resolution + ThreadGroupSize - 1) / ThreadGroupSize;

            shader.SetTexture(kernel, "_Target", target);
            shader.SetInt("_Resolution", resolution);
            shader.SetVector("_BrushDirection", localDirection.normalized);
            shader.SetFloat("_BrushAngularRadius", Mathf.Max(angularRadius, MinAngularRadius));
            shader.SetVector("_BrushAmount", amount);
            shader.SetVector("_ClampRange", new Vector2(clampMin, clampMax));
            shader.Dispatch(kernel, groups, groups, CubeSphere.FaceCount);
        }
    }
}
