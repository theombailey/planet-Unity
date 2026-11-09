// =============================================================================
// Hidden/Terrarium/InkPaper
// -----------------------------------------------------------------------------
// Purpose : A fullscreen pass over the finished image that (1) inks an outline
//           wherever the scene has an edge, and (2) lays the picture on paper:
//           fibre grain plus darkened corners (vignette).
//
// Idea (edges): An outline belongs wherever the surface under one pixel is not
//           the same surface as under its neighbours. Two signals show that:
//             DEPTH  - a jump in distance from the camera (a silhouette),
//             NORMAL - a sudden change in which way the surface faces (a crease).
//           We sample both at the pixel and at four diagonal neighbours.
//
// Equations:
//   Depth edge. On a flat surface, depth changes at a steady rate across the
//   screen, so the average of two opposite neighbours equals the centre. Only a
//   real jump breaks that. With centre c and diagonal pairs (a,d), (b,e):
//       jump  = ( |a + d - 2c| + |b + e - 2c| ) / c
//       edgeD = smoothstep(T, 2T, jump)
//   (Comparing against the centre like this, instead of just |a - d|, stops the
//   desk from inking itself where it recedes at a shallow angle.)
//
//   Normal edge (Roberts cross: differences along the two diagonals):
//       turn  = |Na - Nd| + |Nb - Ne|
//       edgeN = smoothstep(Tn, 2Tn, turn)
//
//   edge  = max(edgeD, edgeN);      color = lerp(color, ink, edge)
//   The sampling distance (= line width) is multiplied by noise, so the line
//   thickens and thins; the boil seed re-rolls that noise a few times a second.
//
//   Paper   color *= 1 - grainStrength * (1 - grain)
//           grain = blotchy noise * fine fibres (noise stretched along x)
//   Vignette color *= 1 - vignetteStrength * smoothstep(0.35, 1.0, r)^2
//           r = distance from the screen centre (0 centre, ~1 corners)
//
// Parameters (globals from PainterlySettings.cs / SimConfig):
//   _PainterlyEdgeWidth, _PainterlyEdgeDepthThreshold (T), _PainterlyEdgeNormalThreshold (Tn),
//   _PainterlyOutlineWobble, _PainterlyOutlinesEnabled, _PainterlyInkColor,
//   _PainterlyPaperStrength, _PainterlyVignette, _PainterlyPaperEnabled, _PainterlyBoilSeed.
//
// Buffers & textures:
//   _BlitTexture          - the rendered scene colour (bound by InkPaperFeature.cs)
//   _CameraDepthTexture   - scene depth   (URP)
//   _CameraNormalsTexture - world normals (URP, written by our DepthNormals passes)
// =============================================================================

Shader "Hidden/Terrarium/InkPaper"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "InkPaper"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment InkPaperFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Assets/Shaders/Include/Noise.hlsl"

            float3 _PainterlyInkColor;
            float _PainterlyEdgeWidth;
            float _PainterlyEdgeDepthThreshold;
            float _PainterlyEdgeNormalThreshold;
            float _PainterlyOutlineWobble;
            float _PainterlyOutlinesEnabled;
            float _PainterlyPaperStrength;
            float _PainterlyVignette;
            float _PainterlyPaperEnabled;
            float _PainterlyBoilSeed;

            // Distance from the camera, in world units, at a screen position.
            float EyeDepthAt(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // 0 = no edge, 1 = full ink line. See "Equations" in the header.
            float EdgeAmount(float2 uv, float2 pixel)
            {
                float noise = ValueNoise(float3(pixel * 0.045, _PainterlyBoilSeed * 0.71));
                float widthPixels = _PainterlyEdgeWidth * lerp(1.0, 0.4 + 1.2 * noise, _PainterlyOutlineWobble);

                // Offset to a diagonal neighbour, in UV units. Width is given in pixels at 720p.
                float2 offset = widthPixels * (_ScreenParams.y / 720.0) / _ScreenParams.xy;
                float2 uvA = uv + float2(-offset.x, -offset.y);
                float2 uvD = uv + float2(offset.x, offset.y);
                float2 uvB = uv + float2(offset.x, -offset.y);
                float2 uvE = uv + float2(-offset.x, offset.y);

                float centre = EyeDepthAt(uv);
                float jump = (abs(EyeDepthAt(uvA) + EyeDepthAt(uvD) - 2.0 * centre)
                            + abs(EyeDepthAt(uvB) + EyeDepthAt(uvE) - 2.0 * centre)) / centre;
                float depthEdge = smoothstep(_PainterlyEdgeDepthThreshold, 2.0 * _PainterlyEdgeDepthThreshold, jump);

                float turn = length(SampleSceneNormals(uvA) - SampleSceneNormals(uvD))
                           + length(SampleSceneNormals(uvB) - SampleSceneNormals(uvE));
                float normalEdge = smoothstep(_PainterlyEdgeNormalThreshold, 2.0 * _PainterlyEdgeNormalThreshold, turn);

                return max(depthEdge, normalEdge) * _PainterlyOutlinesEnabled;
            }

            // Paper brightness multiplier, slightly below 1 where there is a fibre or blotch.
            float PaperGrain(float2 pixel)
            {
                float blotches = Fbm(float3(pixel * 0.006, 3.0), 3);
                float fibres = ValueNoise(float3(pixel.x * 0.05, pixel.y * 0.9, 11.0))
                             * ValueNoise(float3(pixel.x * 0.7, pixel.y * 0.04, 23.0));
                float grain = saturate(blotches * 0.6 + fibres * 1.2);
                return 1.0 - _PainterlyPaperStrength * (1.0 - grain);
            }

            half4 InkPaperFragment(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 pixel = uv * _ScreenParams.xy * (720.0 / _ScreenParams.y);   // pixels at 720p

                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                color = lerp(color, _PainterlyInkColor, EdgeAmount(uv, pixel));

                // Paper and vignette (skipped entirely when the debug toggle is off).
                float2 fromCentre = (uv - 0.5) * 2.0;
                float corner = smoothstep(0.35, 1.0, length(fromCentre) * 0.75);
                float paper = PaperGrain(pixel) * (1.0 - _PainterlyVignette * corner * corner);
                color *= lerp(1.0, paper, _PainterlyPaperEnabled);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
