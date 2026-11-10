// =============================================================================
// Terrarium/Boids
// -----------------------------------------------------------------------------
// Purpose : Draws every boid in one instanced call, as a tiny flapping bird
//           shape in ink, oriented along its flight.
//
// Idea    : A boid is one INSTANCE of a small mesh made of two triangles (a left
//           wing and a right wing joined at the body). Its position, velocity and
//           "alive" flag are read straight from the simulation's GPU buffers, so
//           nothing is copied to the CPU. Dead boids are thrown outside the clip
//           volume, which removes them.
//
//           Flap: the wing tips (x = +-1 in the mesh) move up and down with a sine,
//           each boid at its own phase, so a flock shimmers instead of beating as one:
//               y = flapAmount * sin(time * flapRate + phase) * |x|
//
//           Distance: a boid is a few millimetres long, so from the desk it would be
//           invisible. Its size is never allowed below `minPixels` on screen:
//               worldPerPixel = distance * 2 / (P11 * screenHeight)
//               size          = max(boidSize, minPixels * worldPerPixel)
//           and the more the size was raised, the more the bird turns to solid ink, so a
//           faraway flock reads as a drifting ink smudge (a murmuration) while a close
//           bird shows a light body with an ink outline:
//               far = saturate(minPixels * worldPerPixel / boidSize - 1)
//               color = lerp(body, ink, max(rim, far))
//
// Equations:
//     up      = normalize(position)                      (local "up" on the sphere)
//     forward = normalize(velocity - up (velocity . up)) (the way it is flying, along the ground)
//     right   = up x forward
//     world   = _PlanetLocalToWorld * position + (right x + up y + forward z) * size * 0.5
//
// Parameters (globals set by BoidSystem each frame): _PlanetLocalToWorld, _BoidSize, _BoidMinPixels, _BoidFlapRate.
// Buffers:   _BoidPosEnergy float4 (xyz position, w energy)   _BoidVelSeed float4 (xyz velocity, w seed timer)
//            _BoidAlive uint (1 = alive)
// =============================================================================

Shader "Terrarium/Boids"
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
            #pragma vertex BoidVertex
            #pragma fragment BoidFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/Include/Painterly.hlsl"

            float _PainterlyOutlinesEnabled;   // the shared outline switch

            StructuredBuffer<float4> _BoidPosEnergy;
            StructuredBuffer<float4> _BoidVelSeed;
            StructuredBuffer<uint> _BoidAlive;
            float4x4 _PlanetLocalToWorld;
            float _BoidSize;
            float _BoidMinPixels;
            float _BoidFlapRate;

            struct BoidAttributes
            {
                float3 positionOS : POSITION;      // x wing axis (+-1 at the tips), y up, z forward
                float2 uv : TEXCOORD0;             // barycentric coordinates inside the triangle
                uint instanceID : SV_InstanceID;
            };

            struct BoidVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 barycentric : TEXCOORD2;
                nointerpolation float2 tint : TEXCOORD3;   // x far (0 near .. 1 smudge), y carrying a seed (0 or 1)
            };

            BoidVaryings BoidVertex(BoidAttributes input)
            {
                BoidVaryings output;
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);    // outside the clip volume unless the boid lives
                output.positionWS = 0;
                output.normalWS = float3(0, 1, 0);
                output.barycentric = input.uv;
                output.tint = 0;

                if (_BoidAlive[input.instanceID] != 1u)
                {
                    return output;
                }

                float4 posEnergy = _BoidPosEnergy[input.instanceID];
                float4 velSeed = _BoidVelSeed[input.instanceID];

                float3 up = normalize(posEnergy.xyz);
                float3 tangentVelocity = velSeed.xyz - up * dot(velSeed.xyz, up);
                float3 forward = normalize(tangentVelocity + up * 1e-5 + float3(1e-6, 0, 0));
                float3 right = cross(up, forward);

                float3 centre = mul(_PlanetLocalToWorld, float4(posEnergy.xyz, 1.0)).xyz;
                float3 worldUp = normalize(mul((float3x3)_PlanetLocalToWorld, up));
                float3 worldForward = normalize(mul((float3x3)_PlanetLocalToWorld, forward));
                float3 worldRight = normalize(mul((float3x3)_PlanetLocalToWorld, right));

                // Keep the bird visible: never smaller than _BoidMinPixels on screen.
                float distanceToCamera = -mul(UNITY_MATRIX_V, float4(centre, 1.0)).z;
                float worldPerPixel = max(distanceToCamera, 0.001) * 2.0 / (unity_CameraProjection._m11 * _ScreenParams.y);
                float wanted = _BoidMinPixels * worldPerPixel;
                float size = max(_BoidSize, wanted);

                // Each bird flaps at its own phase.
                float phase = frac(input.instanceID * 0.61803398) * 6.2831853;
                float flap = sin(_Time.y * _BoidFlapRate * 6.2831853 + phase) * 0.45;

                float3 local = input.positionOS;
                local.y += flap * abs(local.x);

                output.positionWS = centre + (worldRight * local.x + worldUp * local.y + worldForward * local.z) * size * 0.5;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = worldUp;
                output.tint = float2(saturate(wanted / _BoidSize - 1.0), velSeed.w > 0.0 ? 1.0 : 0.0);
                return output;
            }

            half4 BoidFragment(BoidVaryings input) : SV_Target
            {
                float far = input.tint.x;
                float carrying = input.tint.y;

                // Light body, warmer when it carries a seed.
                float3 albedo = lerp(float3(0.86, 0.82, 0.72), float3(0.90, 0.62, 0.22), carrying);
                float3 color = PainterlyShade(albedo, input.positionWS, normalize(input.normalWS), input.positionCS);

                // Ink outline hugging the edges of the triangle; solid ink when the bird is only a smudge.
                float edge = min(min(input.barycentric.x, input.barycentric.y), 1.0 - input.barycentric.x - input.barycentric.y);
                float rim = (1.0 - smoothstep(0.0, 0.22, edge)) * _PainterlyOutlinesEnabled;
                color = lerp(color, _PainterlyInkColor, saturate(max(rim, far)) * 0.9);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
