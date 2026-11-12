// =============================================================================
// Terrarium/People
// -----------------------------------------------------------------------------
// Purpose : Draws every living person as a tiny peg figure, in one instanced call,
//           walking where the simulation says they are.
//
// Idea    : A person is one INSTANCE of a small body-and-head mesh (TownMeshes.BuildPerson).
//           Their position and "alive" flag are read straight from the simulation's GPU
//           buffers, so nothing is copied to the CPU. Dead slots are thrown outside the
//           clip volume, and so is everyone beyond `_PersonVisibleDistance` from the camera:
//           a person is far smaller than a pixel from the desk, and drawing the whole
//           population as noise there would only look like dirt.
//
//           Walking: each figure hops a little with a sine, at its own phase:
//               hop = hopHeight * |sin(time * pace + phase)|
//           so a crowd bobs unevenly instead of in step. The figure faces the way it walks.
//
//           Dress: each person is given one of five clothing colours by a hash of their slot
//           number (the colour is only a fixed function of the slot, so a person never
//           changes clothes, but a new person who takes the slot of one who died does).
//
// Equations:
//     up      = normalize(position)
//     forward = normalize(heading)         (tangent to the sphere, supplied by the simulation)
//     right   = cross(up, forward)
//     world   = _PlanetLocalToWorld * position + (right x + up (y + hop) + forward z) * size
//
// Parameters (globals set by TownRenderer): _PlanetLocalToWorld, _PersonSize (local units),
//   _PersonVisibleDistance (world units).
// Buffers:   _PersonPosEnergy float4 (xyz position, w energy)   _PersonHeading float4 (xyz heading)
//            _PersonAlive uint (1 = alive)
// =============================================================================

Shader "Terrarium/People"
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
            #pragma vertex PersonVertex
            #pragma fragment PersonFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/Include/Painterly.hlsl"

            float _PainterlyOutlinesEnabled;   // the shared outline switch

            StructuredBuffer<float4> _PersonPosEnergy;
            StructuredBuffer<float4> _PersonHeading;
            StructuredBuffer<uint> _PersonAlive;
            float4x4 _PlanetLocalToWorld;
            float _PersonSize;
            float _PersonVisibleDistance;

            struct PersonAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 barycentric : TEXCOORD0;      // xyz barycentric, w part: 0 body, 1 head
                uint instanceID : SV_InstanceID;
            };

            struct PersonVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 barycentric : TEXCOORD2;
                nointerpolation float clothes : TEXCOORD3;   // 0..1, picks the clothing colour
            };

            PersonVaryings PersonVertex(PersonAttributes input)
            {
                PersonVaryings output;
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);    // outside the clip volume unless drawn
                output.positionWS = 0;
                output.normalWS = float3(0, 1, 0);
                output.barycentric = input.barycentric;
                output.clothes = 0;

                if (_PersonAlive[input.instanceID] != 1u)
                {
                    return output;
                }

                float4 posEnergy = _PersonPosEnergy[input.instanceID];
                float3 up = normalize(posEnergy.xyz);
                float3 centre = mul(_PlanetLocalToWorld, float4(posEnergy.xyz, 1.0)).xyz;

                float distanceToCamera = distance(_WorldSpaceCameraPos, centre);
                if (distanceToCamera > _PersonVisibleDistance)
                {
                    return output;
                }

                float3 forward = _PersonHeading[input.instanceID].xyz;
                forward = normalize(forward - up * dot(forward, up) + float3(1e-6, 0, 0));
                float3 right = cross(up, forward);

                float3 worldUp = normalize(mul((float3x3)_PlanetLocalToWorld, up));
                float3 worldForward = normalize(mul((float3x3)_PlanetLocalToWorld, forward));
                float3 worldRight = normalize(mul((float3x3)_PlanetLocalToWorld, right));

                float planetScale = length(float3(_PlanetLocalToWorld._m00, _PlanetLocalToWorld._m10, _PlanetLocalToWorld._m20));
                float size = _PersonSize * planetScale;

                uint hash = HashUint(input.instanceID);
                float phase = (hash & 1023u) * (6.2831853 / 1024.0);
                float hop = 0.12 * abs(sin(_Time.y * 7.0 + phase));

                float3 local = input.positionOS;
                local.y += hop;
                output.positionWS = centre + (worldRight * local.x + worldUp * local.y + worldForward * local.z) * size;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = normalize(worldRight * input.normalOS.x + worldUp * input.normalOS.y + worldForward * input.normalOS.z);
                output.clothes = ((hash >> 10) & 255u) / 255.0;
                return output;
            }

            half4 PersonFragment(PersonVaryings input) : SV_Target
            {
                bool head = input.barycentric.w > 0.5;

                // Five clothing colours: red, blue, ochre, green, plum.
                float pick = input.clothes * 5.0;
                float3 clothes = pick < 1.0 ? float3(0.78, 0.25, 0.20)
                    : (pick < 2.0 ? float3(0.24, 0.40, 0.72)
                    : (pick < 3.0 ? float3(0.82, 0.62, 0.20)
                    : (pick < 4.0 ? float3(0.30, 0.58, 0.30) : float3(0.55, 0.30, 0.50))));
                float3 skin = float3(0.90, 0.72, 0.56);
                float3 albedo = head ? skin : clothes;

                float3 color = PainterlyShadeSun(albedo, input.positionWS, normalize(input.normalWS), input.positionCS);

                float edge = min(input.barycentric.x, min(input.barycentric.y, input.barycentric.z));
                float rim = (1.0 - smoothstep(0.0, 0.10, edge)) * _PainterlyOutlinesEnabled;
                color = lerp(color, _PainterlyInkColor, saturate(rim) * 0.85);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
