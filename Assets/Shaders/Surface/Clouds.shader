// =============================================================================
// Terrarium/Clouds
// -----------------------------------------------------------------------------
// Purpose : The layer of clouds above the planet. Clouds gather over wet ground
//           (seas, rainy land), thin out over deserts, drift round the planet
//           with the wind, are lit by the lamp, and turn pink-orange at sunset.
//
// Idea    : A transparent sphere a little above the ground. Each pixel asks
//           Clouds.hlsl "how much cloud is in this direction?", which cuts
//           noise with the moisture field (see that file). The cloud is then
//           shaded like the rest of the painterly world: a few flat tones
//           chosen by how directly the lamp lights it, a darker belly where it
//           is thick, a warm tint when the lamp is low, and a deep blue-grey at
//           night. It is in the object-space of the planet, so clouds turn with
//           the planet and the wind drift is added on top.
//
// Ink edges: the solid core of each cloud (alpha above ~0.35) is also written
//           into the camera's depth and normals (DepthOnly / DepthNormals
//           passes), so the fullscreen ink pass draws a pen outline round every
//           cloud, like the rest of the drawing, and the planet's own outline
//           no longer shows through the cloud in front of it.
//
// Soft near fade: a cloud very close to the camera would fill the screen with a
//           white wall (and flicker as the camera passes through the layer), so
//           it fades out within a short distance. When the camera is ABOVE the
//           layer the fade is wider, so the ground right below stays visible
//           while the clouds toward the horizon still show.
//
// On the surface: this layer fades out (_CloudShellFade goes to 0 as the camera
//           dives) and the sky shader draws clouds in the sky instead, from the
//           same weather function, so there is always a cloud ABOVE you rather
//           than a white band below the horizon.
//
// Equations (L = dot(normal, toLamp)):
//     colour      = CloudShade(L, thickness, 0)    (Clouds.hlsl: flat bands, sunset tint, night)
//     alpha       = density * opacity * nearFade * shellFade
//
// Parameters (globals): _CloudTime, _CloudParams, _CloudLayer (Clouds.hlsl);
//     _AtmoCentre and _TerrariumLampPosition (AtmosphereGlobals.hlsl);
//     _ClimateField, _FieldResolution (the simulation's moisture field).
// =============================================================================

Shader "Terrarium/Clouds"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/Include/CubeSphere.hlsl"
        #include "Assets/Shaders/Include/Clouds.hlsl"

        Texture2DArray<float4> _ClimateField;
        float _FieldResolution;

        CS_DEFINE_SAMPLER(CS_SampleField4, Texture2DArray<float4>, float4)

        struct CloudAttributes
        {
            float4 positionOS : POSITION;
        };

        struct CloudVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 directionOS : TEXCOORD1;
        };

        CloudVaryings CloudVertex(CloudAttributes input)
        {
            CloudVaryings output;
            output.directionOS = normalize(input.positionOS.xyz);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            return output;
        }

        // Alpha of the cloud at this pixel (0 = none): density x opacity x the fade near the camera.
        // `thickness` returns how deep inside the cloud the pixel is.
        float CloudAlpha(CloudVaryings input, out float thickness)
        {
            thickness = 0.0;
            if (_CloudLayer.w < 0.5 || _CloudShellFade < 0.01)
            {
                return 0.0;
            }

            float3 direction = normalize(input.directionOS);
            int n = (int)_FieldResolution;
            float moisture = n > 0 ? CS_SampleField4(_ClimateField, direction, n).g : 0.5;
            float density = CloudDensity(direction, moisture, 1.0, thickness);

            // Fade clouds that are right next to the camera; wider when the camera is above the layer.
            float cameraRadius = distance(_WorldSpaceCameraPos, _AtmoCentre.xyz);
            float layerRadius = _AtmoCentre.w * _CloudLayer.x;
            float distanceToCloud = distance(_WorldSpaceCameraPos, input.positionWS);
            float nearEdge = cameraRadius > layerRadius ? 0.15 : 0.02;
            float farEdge = cameraRadius > layerRadius ? 0.70 : 0.12;
            float nearFade = smoothstep(nearEdge, farEdge, distanceToCloud);

            return density * _CloudLayer.y * nearFade * _CloudShellFade;
        }
        ENDHLSL

        Pass
        {
            Name "Clouds"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex CloudVertex
            #pragma fragment CloudFragment
            #pragma require 2darray

            half4 CloudFragment(CloudVaryings input) : SV_Target
            {
                float thickness;
                float alpha = CloudAlpha(input, thickness);
                if (alpha < 0.003)
                {
                    return half4(0, 0, 0, 0);
                }

                float3 normalWS = normalize(input.positionWS - _AtmoCentre.xyz);
                float3 toLamp = normalize(_TerrariumLampPosition.xyz - input.positionWS);
                float L = dot(normalWS, toLamp);

                return half4(CloudShade(L, thickness, 0.0), alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex CloudVertex
            #pragma fragment CloudDepthFragment
            #pragma require 2darray

            half4 CloudDepthFragment(CloudVaryings input) : SV_Target
            {
                float thickness;
                clip(CloudAlpha(input, thickness) - 0.35);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex CloudVertex
            #pragma fragment CloudNormalsFragment
            #pragma require 2darray

            half4 CloudNormalsFragment(CloudVaryings input) : SV_Target
            {
                float thickness;
                clip(CloudAlpha(input, thickness) - 0.35);
                return half4(normalize(input.positionWS - _AtmoCentre.xyz), 0.0);
            }
            ENDHLSL
        }
    }
}
