// =============================================================================
// Terrarium/Desk
// -----------------------------------------------------------------------------
// Purpose : The wooden desk: procedural wood grain, painterly lighting, and a
//           worn-varnish highlight where the lamp glances off it.
//
// Idea    : The desk is made of PLANKS running along x. Each plank gets its
//           own tint and its own slice of 3D noise, so no two look the same.
//           GRAIN is noise stretched a long way along the plank: sampling it
//           finely across and coarsely along turns blobs into streaks. Taking
//           frac() of the scaled noise turns smooth blobs into repeating
//           light/dark RINGS, like growth rings cut at an angle.
//           The lamp's "pool of light" needs no special code: the light fades
//           with distance, and the shared banding turns that fade into rings
//           of tone.
//
// Equations (p = world position on the desk top):
//     plank    = floor(p.z / plankWidth)
//     g        = Fbm( (p.x * alongScale, p.z * acrossScale, plank * 5.3) )
//     rings    = triangle( frac(g * ringCount) )     triangle(t) = |2t - 1|
//     albedo   = lerp(dark, light, rings blended with fine streak noise) * plankTint
//     gap      = darken within gapWidth of a plank edge
//
//   Varnish highlight (a banded Blinn-Phong glint):
//     h        = normalize(lightDir + viewDir)
//     glint    = pow( max(0, N.h), shininess ) * tone
//     color   += varnish * smoothstep(0.5 - s, 0.5 + s, glint * wear)
//     `wear` is noise, so the varnish looks rubbed away in patches.
//
// Parameters:
//     _WoodLight, _WoodDark - the two wood colours.
//     _PlankWidth           - plank width in world units.
//     _RingCount            - how many grain rings per noise blob.
//     _Varnish              - strength of the varnish highlight.
//
// Buffers & textures: none of our own; URP's light list and shadow maps.
// =============================================================================

Shader "Terrarium/Desk"
{
    Properties
    {
        _WoodLight ("Wood light colour", Color) = (0.62, 0.42, 0.24, 1)
        _WoodDark ("Wood dark colour", Color) = (0.34, 0.20, 0.11, 1)
        _PlankWidth ("Plank width (world units)", Range(0.1, 2)) = 0.62
        _RingCount ("Grain rings per noise blob", Range(1, 30)) = 9
        _Varnish ("Varnish highlight strength", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _WoodLight;
            float4 _WoodDark;
            float _PlankWidth;
            float _RingCount;
            float _Varnish;
        CBUFFER_END

        // The desk has no hull outline; its edges are inked by the fullscreen pass.
        static const float _OutlineScale = 0.0;

        #include "Assets/Shaders/Include/Painterly.hlsl"
        #include "Assets/Shaders/Include/PainterlyPasses.hlsl"
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex ForwardVertex
            #pragma fragment ForwardFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            struct ForwardVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            ForwardVaryings ForwardVertex(PassAttributes input)
            {
                ForwardVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Wood colour at a world position. See "Equations" in the header.
            float3 WoodAlbedo(float3 p)
            {
                float plankCoord = p.z / _PlankWidth;
                float plank = floor(plankCoord);
                float acrossPlank = plankCoord - plank;                 // 0..1 across one plank

                // Each plank is cut from a different part of the "tree" and starts at a different length.
                float plankRandom = Hash3(int3((int)plank, 7, 13));
                float3 q = float3((p.x + plankRandom * 40.0) * 0.55, p.z * 5.0, plank * 5.3);

                float grain = Fbm(q, 4);
                float rings = abs(frac(grain * _RingCount) * 2.0 - 1.0);

                // Fine streaks: noise sampled very finely across the plank and coarsely along it.
                float streaks = ValueNoise(float3(p.x * 2.5, p.z * 90.0, plank));

                float3 wood = lerp(_WoodDark.rgb, _WoodLight.rgb, saturate(rings * 0.75 + streaks * 0.35));
                wood *= lerp(0.82, 1.1, plankRandom);

                // Dark gap between planks.
                float edgeDistance = min(acrossPlank, 1.0 - acrossPlank) * _PlankWidth;
                wood *= lerp(0.35, 1.0, smoothstep(0.0, 0.012, edgeDistance));
                return wood;
            }

            half4 ForwardFragment(ForwardVaryings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 albedo = WoodAlbedo(input.positionWS);
                float3 color = PainterlyShade(albedo, input.positionWS, normalWS, input.positionCS);

                // Varnish highlight, snapped to a single soft-edged shape like a painted glint.
                PainterlyLight light = GatherLight(input.positionWS, normalWS, GetNormalizedScreenSpaceUV(input.positionCS));
                float3 viewDirection = normalize(GetCameraPositionWS() - input.positionWS);
                float3 halfVector = normalize(light.direction + viewDirection);
                float glint = pow(saturate(dot(normalWS, halfVector)), 40.0) * light.tone;
                float wear = lerp(0.55, 1.0, ValueNoise(input.positionWS * float3(1.5, 1.0, 9.0)));
                color += light.hue * _Varnish * smoothstep(0.42, 0.58, glint * wear);

                return half4(color, 1.0);
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
