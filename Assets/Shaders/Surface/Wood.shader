// =============================================================================
// Terrarium/Wood
// -----------------------------------------------------------------------------
// Purpose : Painterly bark for the tree branch meshes.
//
// Idea    : The branch mesh has no texture coordinates; bark is made from noise
//           in the branch's OWN coordinates (tree space, +y up the trunk). Noise
//           that varies quickly around and slowly along the branch gives the
//           long vertical furrows of bark. Two bark colours are mixed by that
//           noise. A dead tree is drawn greyer: `_Decay` (0..1, set per tree)
//           mixes the bark toward a dry ash grey.
//           Lighting is the shared painterly shading (banded tone + hatching)
//           and the outline is the same inverted hull the props use, drawn thin
//           because branches are thin.
//
// Equations:
//     streak = ValueNoise( (x * 140, y * 9, z * 140) )              furrows: fast around, slow along
//     bark   = lerp( dark, light, saturate( streak * 1.3 ) )
//     colour = lerp( bark, ash, _Decay )
//
// Parameters: _BarkLight, _BarkDark, _Ash, _Decay (per tree), _OutlineScale.
// Passes  : Forward, Outline, ShadowCaster, DepthOnly, DepthNormals (as Props.shader).
// =============================================================================

Shader "Terrarium/Wood"
{
    Properties
    {
        _BarkLight ("Bark light", Color) = (0.42, 0.29, 0.18, 1)
        _BarkDark ("Bark dark", Color) = (0.20, 0.13, 0.09, 1)
        _Ash ("Dead wood", Color) = (0.50, 0.48, 0.45, 1)
        _Decay ("Decay (0 alive, 1 dead)", Range(0, 1)) = 0
        _OutlineScale ("Outline width multiplier", Range(0, 4)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BarkLight;
            float4 _BarkDark;
            float4 _Ash;
            float _Decay;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            struct ForwardVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            ForwardVaryings ForwardVertex(PassAttributes input)
            {
                ForwardVaryings output;
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 ForwardFragment(ForwardVaryings input) : SV_Target
            {
                float3 p = input.positionOS;
                float streak = ValueNoise(float3(p.x * 140.0, p.y * 9.0, p.z * 140.0));
                float3 bark = lerp(_BarkDark.rgb, _BarkLight.rgb, saturate(streak * 1.3));
                float3 albedo = lerp(bark, _Ash.rgb, _Decay);

                return half4(PainterlyShade(albedo, input.positionWS, normalize(input.normalWS), input.positionCS), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
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
