// =============================================================================
// Terrarium/FieldDebug
// -----------------------------------------------------------------------------
// Purpose : Paints one simulation field straight onto the planet, so a field
//           can be looked at while the sim runs. Essential for checking the
//           cube-sphere seams (Phase 3) and for tuning every later field.
//
// Idea    : For each pixel of the planet, take the unit direction from the
//           planet's centre (its object-space position), look the field up at
//           that direction with our own seam-correct bilinear sampler
//           (CubeSphere.hlsl), and colour the result. Colouring is either a
//           ramp for one chosen channel, or the first three channels as RGB.
//
//           Two overlays help check the cube-sphere:
//             FACE BOUNDARIES - red lines where the mesh's six faces meet. They
//                 are drawn from the mesh's own per-vertex face coordinates
//                 (uv0), so they show where the mesh really is. A field that
//                 is seam-correct is smooth right across them.
//             TEXEL GRID - yellow lines every `_GridCells` texels, to see the
//                 field resolution when zoomed in.
//
// Equations:
//     value  = SampleField(_Field, normalize(positionOS)).channel
//     t      = saturate( (value - min) / (max - min) )
//     colour = ramp(t)                           (a 5-stop viridis-style ramp)
//     Line mask for a coordinate x with distance-to-edge e:
//         mask = 1 - smoothstep(0, fwidth(x) * 1.5, e)
//         fwidth keeps the line about 1.5 pixels wide at any zoom.
//
// Parameters:
//     _Channel         0..3 = that channel through the ramp, 4 = RGB.
//     _RangeMin/_Max   values mapped to the ramp's ends.
//     _Resolution      field texels per face edge.
//     _ShowBoundaries  1 = draw face boundaries.
//     _ShowGrid        1 = draw the texel grid.
//     _GridCells       texels between grid lines.
//
// Buffers & textures: _Field, a 6-slice Texture2DArray (slice = cube face).
//
// Passes  : Forward draws the field unlit; ShadowCaster / DepthOnly / DepthNormals
//           keep the planet's shadow and the fullscreen ink edges working.
// =============================================================================

Shader "Terrarium/FieldDebug"
{
    Properties
    {
        [NoScaleOffset] _Field ("Field (texture array)", 2DArray) = "" {}
        _Channel ("Channel (0-3, 4 = RGB)", Range(0, 4)) = 0
        _RangeMin ("Range min", Float) = 0
        _RangeMax ("Range max", Float) = 1
        _Resolution ("Resolution", Float) = 256
        _ShowBoundaries ("Show face boundaries", Range(0, 1)) = 1
        _ShowGrid ("Show texel grid", Range(0, 1)) = 0
        _GridCells ("Texels between grid lines", Float) = 16
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Channel;
            float _RangeMin;
            float _RangeMax;
            float _Resolution;
            float _ShowBoundaries;
            float _ShowGrid;
            float _GridCells;
        CBUFFER_END

        // No hull outline on the debug view; the fullscreen pass still inks its silhouette.
        static const float _OutlineScale = 0.0;

        #include "Assets/Shaders/Include/Painterly.hlsl"
        #include "Assets/Shaders/Include/PainterlyPasses.hlsl"
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex DebugVertex
            #pragma fragment DebugFragment
            #pragma require 2darray

            #include "Assets/Shaders/Include/CubeSphere.hlsl"

            Texture2DArray<float4> _Field;
            CS_DEFINE_SAMPLER(CS_SampleField4, Texture2DArray<float4>, float4)

            struct DebugAttributes
            {
                float4 positionOS : POSITION;
                float2 faceUV : TEXCOORD0;
            };

            struct DebugVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                float2 faceUV : TEXCOORD1;
            };

            DebugVaryings DebugVertex(DebugAttributes input)
            {
                DebugVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionOS = input.positionOS.xyz;
                output.faceUV = input.faceUV;
                return output;
            }

            // Five-stop viridis-style ramp (dark purple -> teal -> yellow), authored in sRGB.
            float3 Ramp(float t)
            {
                const float3 stops[5] =
                {
                    float3(0.267, 0.005, 0.329), float3(0.230, 0.322, 0.546), float3(0.128, 0.567, 0.551),
                    float3(0.369, 0.789, 0.383), float3(0.993, 0.906, 0.144)
                };

                float x = saturate(t) * 4.0;
                int i = min((int)floor(x), 3);
                return lerp(stops[i], stops[i + 1], x - i);
            }

            // 1 on a line, 0 away from it. `edge` is the distance to the line in the units of `coord`.
            float LineMask(float edge, float coord)
            {
                return 1.0 - smoothstep(0.0, fwidth(coord) * 1.5, edge);
            }

            half4 DebugFragment(DebugVaryings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float4 value = CS_SampleField4(_Field, direction, (int)_Resolution);

                float range = max(_RangeMax - _RangeMin, 1e-6);
                float3 color;
                if (_Channel < 3.5)
                {
                    float channelValue = value[(int)round(_Channel)];
                    color = Ramp((channelValue - _RangeMin) / range);
                }
                else
                {
                    color = saturate((value.rgb - _RangeMin) / range);
                }

                float2 uv = input.faceUV;

                // Face boundaries: distance (in uv) to the nearest of the four face edges.
                float edgeDistance = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float boundary = LineMask(edgeDistance, max(uv.x, uv.y)) * _ShowBoundaries;
                color = lerp(color, float3(1.0, 0.12, 0.08), boundary);

                // Texel grid: a line every _GridCells texels.
                float2 gridCoord = uv * _Resolution / _GridCells;
                float2 gridDistance = abs(frac(gridCoord + 0.5) - 0.5);
                float grid = max(LineMask(gridDistance.x, gridCoord.x), LineMask(gridDistance.y, gridCoord.y)) * _ShowGrid;
                color = lerp(color, float3(1.0, 0.85, 0.2), grid * 0.7);

                // The colours above are authored as sRGB; the framebuffer expects linear light.
                return half4(pow(max(color, 0.0), 2.2), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthOnlyFragment
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthNormalsFragment
            ENDHLSL
        }
    }
}
