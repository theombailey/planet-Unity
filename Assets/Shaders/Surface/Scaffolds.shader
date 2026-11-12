// =============================================================================
// Terrarium/Scaffolds
// -----------------------------------------------------------------------------
// Purpose : Draws the scaffolding round every building that is being built, rebuilt in a
//           better style, or taken down, in one instanced call.
//
// Idea    : The same instance list as the houses (Houses.compute writes a second list of just the
//           buildings with work going on), drawn with a mesh of thin poles, rails and braces
//           (TownMeshes.BuildScaffold). It is brief by nature: it is there only while the building's
//           progress is between 0 and 1. It rises quickly as work starts and is lowered away as the
//           work finishes (or the building is gone), so it stands tallest in the middle of the job:
//               rise   = smoothstep(0, 0.12, progress) * (1 - smoothstep(0.88, 1, progress))
//               height = scaffoldHeight(tier) * tallness * lerp(0.25, 1, rise)
//           It is a little wider than the building (1.15 of its width) and as tall as the finished
//           building will be, so it reads as a frame round it.
//
//           Size: like a house, it is never drawn smaller than `_HouseMinPixels` on screen.
//
// Equations (as the houses): world = _PlanetLocalToWorld * position + (side x + up y + forward z) * size
// Parameters (globals set by TownRenderer): _PlanetLocalToWorld, _HouseMinPixels.
// Buffers:   _Scaffolds (the same layout as Houses.compute's list)
// =============================================================================

Shader "Terrarium/Scaffolds"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ScaffoldVertex
            #pragma fragment ScaffoldFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/Include/Painterly.hlsl"

            float _PainterlyOutlinesEnabled;   // the shared outline switch

            struct House
            {
                float4 positionSize;
                float4 style;
                float4 extra;
            };

            StructuredBuffer<House> _Scaffolds;
            float4x4 _PlanetLocalToWorld;
            float _HouseMinPixels;

            struct ScaffoldAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 barycentric : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct ScaffoldVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 barycentric : TEXCOORD2;
                nointerpolation float variety : TEXCOORD3;
            };

            ScaffoldVaryings ScaffoldVertex(ScaffoldAttributes input)
            {
                ScaffoldVaryings output;
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                output.positionWS = 0;
                output.normalWS = float3(0, 1, 0);
                output.barycentric = input.barycentric;
                output.variety = 0;

                House house = _Scaffolds[input.instanceID];
                float3 localPosition = house.positionSize.xyz;
                float3 up = normalize(localPosition);
                float3 reference = abs(up.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 east = normalize(cross(reference, up));
                float3 north = cross(up, east);

                float yaw = house.style.x;
                float3 forward = east * cos(yaw) + north * sin(yaw);
                float3 side = north * cos(yaw) - east * sin(yaw);

                float3 centre = mul(_PlanetLocalToWorld, float4(localPosition, 1.0)).xyz;
                float3 worldUp = normalize(mul((float3x3)_PlanetLocalToWorld, up));
                float3 worldForward = normalize(mul((float3x3)_PlanetLocalToWorld, forward));
                float3 worldSide = normalize(mul((float3x3)_PlanetLocalToWorld, side));

                int tier = (int)round(house.extra.w);
                float progress = saturate(house.extra.z);

                float planetScale = length(float3(_PlanetLocalToWorld._m00, _PlanetLocalToWorld._m10, _PlanetLocalToWorld._m20));
                float tierWidth = tier <= 0 ? 0.85 : (tier == 1 ? 1.0 : 1.12);
                float wanted = house.positionSize.w * planetScale * tierWidth * 1.15 / 1.24;   // the mesh is 1.24 wide

                float distanceToCamera = -mul(UNITY_MATRIX_V, float4(centre, 1.0)).z;
                float worldPerPixel = max(distanceToCamera, 0.001) * 2.0 / (unity_CameraProjection._m11 * _ScreenParams.y);
                float size = max(wanted, _HouseMinPixels * worldPerPixel);

                float rise = smoothstep(0.0, 0.12, progress) * (1.0 - smoothstep(0.88, 1.0, progress));
                float finishedHeight = (tier <= 0 ? 0.95 : (tier == 1 ? 0.95 : 1.1)) * house.extra.x * 1.1;

                float3 local = input.positionOS;
                local.y *= finishedHeight * lerp(0.25, 1.0, rise);
                output.positionWS = centre + (worldSide * local.x + worldUp * local.y + worldForward * local.z) * size;
                output.positionCS = TransformWorldToHClip(output.positionWS);

                float3 n = input.normalOS;
                output.normalWS = normalize(worldSide * n.x + worldUp * n.y + worldForward * n.z);
                output.variety = house.style.y;
                return output;
            }

            half4 ScaffoldFragment(ScaffoldVaryings input) : SV_Target
            {
                // Raw timber, each building's a slightly different tone.
                float3 wood = float3(0.66, 0.50, 0.30) * (0.88 + 0.24 * input.variety);
                float3 color = PainterlyShadeSun(wood, input.positionWS, normalize(input.normalWS), input.positionCS);

                float edge = min(input.barycentric.x, min(input.barycentric.y, input.barycentric.z));
                float rim = (1.0 - smoothstep(0.0, 0.10, edge)) * _PainterlyOutlinesEnabled;
                color = lerp(color, _PainterlyInkColor, saturate(rim) * 0.8);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
