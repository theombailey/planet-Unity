// =============================================================================
// InkPaperFeature
// -----------------------------------------------------------------------------
// Purpose : Plugs our fullscreen ink-outline + paper shader (InkPaper.shader)
//           into URP, so it runs over every frame after the scene is drawn.
//
// Idea    : URP lets you add your own step to its frame by writing a
//           "renderer feature" (this class) that enqueues a "render pass".
//           Our pass does one thing: draw a fullscreen triangle with the
//           InkPaper material, reading the finished scene colour and writing
//           the inked result.
//
//           A shader cannot read and write the same texture at once, so the
//           pass reads the camera's colour texture, writes to a NEW texture,
//           and then tells URP "the new one is the camera colour from now on".
//           That swap avoids an extra copy.
//
//           The shader also needs the scene's depth and normals. Calling
//           ConfigureInput(Depth | Normal) tells URP to produce those textures
//           before our pass (it does so by drawing every object's
//           DepthNormals pass, which is why our surface shaders have one).
//
// Unity 6 : URP builds each frame as a "render graph": passes declare which
//           textures they read and write (UseTexture / SetRenderAttachment)
//           and URP works out the order and memory. That is all
//           RecordRenderGraph below does.
//
// Parameters: none here. The look is controlled by global shader values set
//           in PainterlySettings.cs from SimConfig.
// =============================================================================

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Terrarium.Rendering
{
    /// <summary>URP renderer feature that runs the fullscreen ink-outline and paper pass.</summary>
    public sealed class InkPaperFeature : ScriptableRendererFeature
    {
        /// <summary>Name of the fullscreen shader this feature runs.</summary>
        public const string ShaderName = "Hidden/Terrarium/InkPaper";

        [Tooltip("The Hidden/Terrarium/InkPaper shader. Stored as a reference so it is included in builds.")]
        [SerializeField] private Shader shader;

        private Material material;
        private InkPaperPass pass;

        /// <summary>Sets the shader reference (used by the editor setup script).</summary>
        public void SetShader(Shader inkPaperShader)
        {
            shader = inkPaperShader;
        }

        /// <inheritdoc/>
        public override void Create()
        {
            if (shader == null)
            {
                shader = Shader.Find(ShaderName);
            }

            if (shader != null)
            {
                material = CoreUtils.CreateEngineMaterial(shader);
            }

            pass = new InkPaperPass(material);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Skip the small preview and reflection cameras; ink the Game and Scene views.
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (material == null || cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            {
                return;
            }

            renderer.EnqueuePass(pass);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
        }

        /// <summary>The single fullscreen draw.</summary>
        private sealed class InkPaperPass : ScriptableRenderPass
        {
            private static readonly Vector4 FullScaleNoOffset = new Vector4(1f, 1f, 0f, 0f);
            private readonly Material material;

            private sealed class PassData
            {
                public TextureHandle Source;
                public Material Material;
            }

            public InkPaperPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                {
                    // Drawing straight to the screen: there is no texture to read the scene from.
                    return;
                }

                TextureHandle source = resources.activeColorTexture;
                TextureDesc description = renderGraph.GetTextureDesc(source);
                description.name = "_TerrariumInkPaper";
                description.clearBuffer = false;
                description.depthBufferBits = DepthBits.None;
                TextureHandle destination = renderGraph.CreateTexture(description);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Terrarium Ink + Paper", out PassData data))
                {
                    data.Source = source;
                    data.Material = material;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, passData.Source, FullScaleNoOffset, passData.Material, 0));
                }

                // From here on, the inked image is the camera's colour.
                resources.cameraColor = destination;
            }
        }
    }
}
