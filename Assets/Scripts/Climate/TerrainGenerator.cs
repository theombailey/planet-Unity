// =============================================================================
// TerrainGenerator
// -----------------------------------------------------------------------------
// Purpose : The C# side of terrain generation: sets the parameters and runs the
//           three terrain kernels of FieldInit.compute in order.
// Idea    : See FieldInit.compute. Raw noise -> histogram -> sea level ->
//           final height, all on the GPU. The CPU's only jobs are to turn the
//           world seed into a noise offset (so each seed gives a different
//           world) and to zero the histogram before counting.
// Seed    : three offsets drawn from Rng(seed), each in [0, 500). Adding them
//           to the sample position slides the sphere to a different part of
//           the infinite 3D noise.
// =============================================================================

using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Climate
{
    /// <summary>Generates the Terrain field (height and rock hardness) from a seed.</summary>
    public sealed class TerrainGenerator
    {
        private const int ThreadGroupSize = 8;
        private const int HistogramBins = 1024;   // must match HISTOGRAM_BINS in FieldInit.compute

        private readonly ComputeShader shader;

        /// <summary>Creates a generator that uses the given FieldInit.compute.</summary>
        public TerrainGenerator(ComputeShader fieldInit)
        {
            shader = fieldInit;
        }

        /// <summary>Fills a terrain texture: R = height (-1..1, 0 = shoreline), G = rock hardness.</summary>
        /// <param name="terrain">Six-slice random-write field texture.</param>
        /// <param name="config">Terrain parameters.</param>
        /// <param name="seed">World seed.</param>
        /// <param name="landFraction">Share of the surface above sea level, 0..1.</param>
        public void Generate(RenderTexture terrain, SimConfig config, int seed, float landFraction)
        {
            int resolution = terrain.width;
            int groups = (resolution + ThreadGroupSize - 1) / ThreadGroupSize;

            var rng = new Rng(seed);
            var seedOffset = new Vector3(rng.Range(0f, 500f), rng.Range(0f, 500f), rng.Range(0f, 500f));

            shader.SetInt("_Resolution", resolution);
            shader.SetVector("_SeedOffset", seedOffset);
            shader.SetFloat("_ContinentFrequency", config.continentFrequency);
            shader.SetInt("_TerrainOctaves", config.terrainOctaves);
            shader.SetFloat("_RidgeFrequency", config.ridgeFrequency);
            shader.SetFloat("_RidgeAmount", config.ridgeAmount);
            shader.SetFloat("_LandFraction", landFraction);
            shader.SetFloat("_ReliefSpan", config.reliefSpan);

            var histogram = new ComputeBuffer(HistogramBins, sizeof(uint));
            var seaLevel = new ComputeBuffer(1, sizeof(float));
            try
            {
                histogram.SetData(new uint[HistogramBins]);

                int rawKernel = shader.FindKernel("TerrainRaw");
                shader.SetTexture(rawKernel, "_Target", terrain);
                shader.SetBuffer(rawKernel, "_Histogram", histogram);
                shader.Dispatch(rawKernel, groups, groups, CubeSphere.FaceCount);

                int seaKernel = shader.FindKernel("TerrainSeaLevel");
                shader.SetBuffer(seaKernel, "_Histogram", histogram);
                shader.SetBuffer(seaKernel, "_SeaLevel", seaLevel);
                shader.Dispatch(seaKernel, 1, 1, 1);

                int finaliseKernel = shader.FindKernel("TerrainFinalise");
                shader.SetTexture(finaliseKernel, "_Target", terrain);
                shader.SetBuffer(finaliseKernel, "_SeaLevel", seaLevel);
                shader.Dispatch(finaliseKernel, groups, groups, CubeSphere.FaceCount);
            }
            finally
            {
                // The GPU still holds these until the queued dispatches have run; Release only
                // drops our handle, which is safe.
                histogram.Release();
                seaLevel.Release();
            }
        }
    }
}
