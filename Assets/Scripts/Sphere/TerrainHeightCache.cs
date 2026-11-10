// =============================================================================
// TerrainHeightCache
// -----------------------------------------------------------------------------
// Purpose : A copy of the terrain heights on the CPU, so code that cannot ask
//           the GPU a question mid-frame (mouse picking, the camera) can still
//           know how high the ground is at any point on the planet.
// Idea    : The terrain lives in a GPU texture. Reading it back is asynchronous
//           (the CPU never waits): Request() asks, and a few frames later the
//           heights arrive and are copied into a plain array, one float per
//           texel. HeightAt() then just finds the texel containing a direction.
//           The copy is only refreshed when the terrain changes (a new world, a
//           meteor crater), not every frame, and until the first answer arrives
//           every height reads as 0 (flat sea level).
// Memory  : N x N x 6 floats, allocated once per resolution (1.5 MB at N = 256).
// =============================================================================

using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Sphere
{
    /// <summary>Anything that can say how high the ground is in a given direction.</summary>
    public interface ITerrainHeights
    {
        /// <summary>Terrain height (0 = shoreline, 1 = highest peak, negative = sea floor) in a planet-frame direction.</summary>
        float HeightAt(Vector3 localDirection);
    }

    /// <summary>CPU copy of the terrain height field, filled by asynchronous GPU readback.</summary>
    public sealed class TerrainHeightCache : ITerrainHeights
    {
        private float[] heights;
        private int resolution;
        private bool pending;
        private bool requestAgain;
        private RenderTexture lastSource;

        /// <summary>True once at least one readback has arrived.</summary>
        public bool HasData => heights != null;

        /// <summary>
        /// Asks for a fresh copy of a terrain texture. If a request is already on its way, one more is made
        /// when it lands, so a change that happened mid-flight is never missed.
        /// </summary>
        public void Request(RenderTexture terrain)
        {
            lastSource = terrain;
            if (pending)
            {
                requestAgain = true;
                return;
            }

            pending = true;
            AsyncGPUReadback.Request(terrain, 0, TextureFormat.RGBAFloat, OnRead);
        }

        /// <inheritdoc/>
        public float HeightAt(Vector3 localDirection)
        {
            if (heights == null)
            {
                return 0f;
            }

            Texel texel = CubeSphere.DirToTexel(localDirection, resolution);
            return heights[(texel.Face * resolution + texel.Y) * resolution + texel.X];
        }

        private void OnRead(AsyncGPUReadbackRequest request)
        {
            pending = false;

            if (!request.hasError)
            {
                int n = request.width;
                if (heights == null || resolution != n)
                {
                    heights = new float[CubeSphere.FaceCount * n * n];
                    resolution = n;
                }

                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    NativeArray<float> slice = request.GetData<float>(face);
                    int offset = face * n * n;
                    for (int i = 0; i < n * n; i++)
                    {
                        heights[offset + i] = slice[i * 4];   // channel R of RGBA
                    }
                }
            }

            if (requestAgain && lastSource != null)
            {
                requestAgain = false;
                Request(lastSource);
            }
        }
    }
}
