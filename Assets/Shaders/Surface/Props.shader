// =============================================================================
// Terrarium/Props
// -----------------------------------------------------------------------------
// Purpose : Painterly shader for solid single-colour objects: the tools, the
//           lamp, and (until Phase 4 gives it terrain) the placeholder planet.
//
// Idea    : The object has one flat colour. All the character comes from the
//           shared painterly lighting (banded tone + hatching, Painterly.hlsl)
//           and from an inverted-hull ink outline (PainterlyPasses.hlsl).
//           _Glow lets a material ignore lighting and show its flat colour,
//           used for the lamp's bulb so it reads as the light source.
//
// Equations: color = lerp( PainterlyShade(_BaseColor, ...), _BaseColor, _Glow )
//
// Parameters:
//     _BaseColor    - the object's colour.
//     _Glow         - 0 = lit normally, 1 = flat unlit colour.
//     _OutlineScale - multiplies the global outline width (0 = no outline).
//
// Passes  : Forward (the visible shading), Outline, ShadowCaster, DepthOnly,
//           DepthNormals. The last three are what URP needs to cast shadows and
//           to build the depth / normals textures the fullscreen ink pass reads.
//
// Buffers & textures: none of our own; URP's light list and shadow maps.
// =============================================================================

Shader "Terrarium/Props"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.5, 0.5, 0.5, 1)
        _Glow ("Glow (0 = lit, 1 = flat colour)", Range(0, 1)) = 0
        _OutlineScale ("Outline width multiplier", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        // One block of per-material values, identical in every pass (required by URP's batcher).
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Glow;
            float _OutlineScale;
        CBUFFER_END

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

            // Variants URP switches between depending on the lights and shadows in view.
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

            half4 ForwardFragment(ForwardVaryings input) : SV_Target
            {
                float3 shaded = PainterlyShade(_BaseColor.rgb, input.positionWS, normalize(input.normalWS), input.positionCS);
                return half4(lerp(shaded, _BaseColor.rgb, _Glow), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            // URP draws passes tagged SRPDefaultUnlit as an extra pass alongside the forward pass.
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
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
