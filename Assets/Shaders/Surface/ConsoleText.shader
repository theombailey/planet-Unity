// =============================================================================
// Terrarium/ConsoleText
// -----------------------------------------------------------------------------
// Purpose : Shows a small pixel texture on the console: the label plates under the
//           nixie tubes, and the paper roll with its printed lines.
// Idea    : The texture is made by code (BitmapFont5x7 draws text into it), so there
//           are no art assets. It is shown with point filtering so the dots of the
//           font stay crisp and look like a dot-matrix printer.
//           For PAPER (_Paper = 1) the window shows only part of a taller texture,
//           and _Scroll slides that window up as a new line is printed (the paper
//           feeding out). The paper is shaded so it curls away at the top, fades
//           into shadow where it goes into the printer slot at the bottom, and has a
//           faint fibre grain.
//
// Equations:
//   uv'      = ( u,  v * window + scroll )         window = share of the texture that is visible
//   curl     = 1 - 0.30 * smoothstep(0.70, 1.0, v)        (the top edge darkens as the paper curls away)
//   slot     = smoothstep(0.0, 0.14, v)                   (shadow where it enters the printer)
//   colour   = texture * tint * curl * lerp(0.35, 1, slot)
//
// Parameters (per material): _MainTex, _Tint, _Paper (0 plate, 1 paper), _Window, _Scroll.
// =============================================================================

Shader "Terrarium/ConsoleText"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Paper ("Paper (0 = plate, 1 = paper)", Float) = 0
        _Window ("Visible share of the texture", Float) = 1
        _Scroll ("Scroll", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ConsoleText"
            Cull Off

            HLSLPROGRAM
            #pragma vertex TextVertex
            #pragma fragment TextFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Include/Noise.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_point_clamp);   // Unity's inline sampler: point filtering, clamped edges

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _Paper;
                float _Window;
                float _Scroll;
            CBUFFER_END

            struct TextAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct TextVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TextVaryings TextVertex(TextAttributes input)
            {
                TextVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 TextFragment(TextVaryings input) : SV_Target
            {
                float2 uv = float2(input.uv.x, input.uv.y * _Window + _Scroll);
                float3 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_point_clamp, uv).rgb * _Tint.rgb;

                if (_Paper > 0.5)
                {
                    float curl = 1.0 - 0.30 * smoothstep(0.70, 1.0, input.uv.y);
                    float slot = lerp(0.35, 1.0, smoothstep(0.0, 0.14, input.uv.y));
                    float grain = 0.96 + 0.04 * ValueNoise(float3(input.uv * float2(180.0, 260.0), 3.0));
                    colour *= curl * slot * grain;
                }

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
