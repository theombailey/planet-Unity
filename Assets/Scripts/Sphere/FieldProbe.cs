// =============================================================================
// FieldProbe
// -----------------------------------------------------------------------------
// Purpose : Reads the value of one texel of a field texture back from the GPU,
//           without waiting. Used by the field lens to show what the ground is
//           like under the cursor.
// Idea    : AsyncGPUReadback can fetch just a 1 x 1 x 1 region of a texture
//           array (one texel of one face). The request is queued now; the value
//           arrives a few frames later in a callback. Nothing ever blocks.
// =============================================================================

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Sphere
{
    /// <summary>Asynchronous single-texel readback of field textures.</summary>
    public static class FieldProbe
    {
        /// <summary>
        /// Requests one texel of a field texture. The callback receives the texel's channels
        /// (x, y, z, w = R, G, B, A; a single-channel texture fills only x).
        /// </summary>
        /// <param name="texture">A six-slice field texture (ARGBFloat or RFloat).</param>
        /// <param name="texel">Which texel to read.</param>
        /// <param name="onValue">Called when the value arrives; not called if the readback fails.</param>
        public static void Request(RenderTexture texture, Texel texel, Action<Vector4> onValue)
        {
            bool singleChannel = texture.format == RenderTextureFormat.RFloat;
            TextureFormat format = singleChannel ? TextureFormat.RFloat : TextureFormat.RGBAFloat;

            AsyncGPUReadback.Request(texture, 0, texel.X, 1, texel.Y, 1, texel.Face, 1, format, request =>
            {
                if (request.hasError)
                {
                    return;
                }

                NativeArray<float> data = request.GetData<float>();
                onValue(singleChannel
                    ? new Vector4(data[0], 0f, 0f, 0f)
                    : new Vector4(data[0], data[1], data[2], data[3]));
            });
        }
    }
}
