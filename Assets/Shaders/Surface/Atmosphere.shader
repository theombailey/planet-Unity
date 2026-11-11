// =============================================================================
// Terrarium/Atmosphere
// -----------------------------------------------------------------------------
// Purpose : The thin glowing air round the planet, seen from the desk: a blue
//           rim on the lamp's side that turns orange where day meets night.
//
// Idea    : A shell of air of height A sits on the planet. For each pixel we
//           send a ray from the camera and add up the light scattered toward
//           the camera by the air along it ("ray marching", 8 samples):
//             - the air is thin high up: density = exp(-altitude / H)
//             - a sample only glows if the lamp can see it (the sun side)
//             - a ray that skims the edge of the planet travels a long way
//               through air and so glows brightly; a ray straight at the planet
//               crosses only a little. That alone makes the glowing rim.
//           The ray stops where it hits the planet or any other solid thing
//           (it reads the depth texture), so trees, birds and the desk poke
//           through the glow correctly.
//           The shell is only a bounding shape to draw on; the glow is worked
//           out exactly per pixel, so its outline is smooth.
//           It fades away as the camera dives to the surface (the sky shader
//           takes over there).
//
// Equations (Rp planet radius, Ra = Rp + A, H = 0.3 A):
//     ray ∩ sphere:  t = -b ± sqrt(b^2 - c),  b = dot(o - centre, d),  c = |o - centre|^2 - R^2
//     density(p)     = exp(-(|p - centre| - Rp) / H)
//     sunDot(p)      = dot(normal(p), toLamp(p))
//     lit(p)         = 0.10 + 0.90 smoothstep(-0.30, 0.30, sunDot)
//     twilight(p)    = exp(-(sunDot / 0.28)^2)             warm where the lamp is at the horizon
//     weight         = sum lit * density * dt / H
//     glow           = colour * (1 - exp(-0.04 * strength * weight^2))
//
// Parameters (globals): _AtmoCentre, _TerrariumLampPosition (AtmosphereGlobals.hlsl),
//     _AtmoParams  x = shell height A, y = strength, z = fade (1 in desk view, 0 on the surface)
//     _AtmoColor   daylight air colour, _SkySunset  sunset colour
// =============================================================================

Shader "Terrarium/Atmosphere"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Atmosphere"
            Blend One One
            Cull Front
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex AtmosphereVertex
            #pragma fragment AtmosphereFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Shaders/Include/AtmosphereGlobals.hlsl"

            float4 _AtmoParams;
            float4 _AtmoColor;
            float4 _SkySunset;

            struct AtmosphereAttributes
            {
                float4 positionOS : POSITION;
            };

            struct AtmosphereVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            AtmosphereVaryings AtmosphereVertex(AtmosphereAttributes input)
            {
                AtmosphereVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 AtmosphereFragment(AtmosphereVaryings input) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos;
                float3 direction = normalize(input.positionWS - origin);

                float planetRadius = _AtmoCentre.w;
                float shellRadius = planetRadius + _AtmoParams.x;
                float3 toOrigin = origin - _AtmoCentre.xyz;

                // Where the ray crosses the shell of air.
                float b = dot(toOrigin, direction);
                float shellDisc = b * b - (dot(toOrigin, toOrigin) - shellRadius * shellRadius);
                if (shellDisc <= 0.0 || _AtmoParams.z <= 0.001)
                {
                    return half4(0, 0, 0, 0);
                }

                float root = sqrt(shellDisc);
                float tStart = max(-b - root, 0.0);
                float tEnd = -b + root;
                if (tEnd <= 0.0)
                {
                    return half4(0, 0, 0, 0);
                }

                // Stop at the planet...
                float planetDisc = b * b - (dot(toOrigin, toOrigin) - planetRadius * planetRadius);
                if (planetDisc > 0.0)
                {
                    float tPlanet = -b - sqrt(planetDisc);
                    if (tPlanet > 0.0)
                    {
                        tEnd = min(tEnd, tPlanet);
                    }
                }

                // ...or at anything solid in front of it.
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool hasSurface = rawDepth > 1e-5;
            #else
                bool hasSurface = rawDepth < 1.0 - 1e-5;
            #endif
                if (hasSurface)
                {
                    float3 sceneWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                    tEnd = min(tEnd, distance(sceneWS, origin));
                }

                if (tEnd <= tStart)
                {
                    return half4(0, 0, 0, 0);
                }

                const int steps = 8;
                float stepLength = (tEnd - tStart) / steps;
                float scaleHeight = max(_AtmoParams.x * 0.3, 1e-3);

                float3 colourSum = 0.0;
                float weightSum = 0.0;
                for (int i = 0; i < steps; i++)
                {
                    float3 p = origin + direction * (tStart + (i + 0.5) * stepLength);
                    float3 fromCentre = p - _AtmoCentre.xyz;
                    float radius = max(length(fromCentre), 1e-4);
                    float density = exp(-max(radius - planetRadius, 0.0) / scaleHeight);

                    float3 normal = fromCentre / radius;
                    float sunDot = dot(normal, _SunDirection.xyz);
                    float lit = 0.10 + 0.90 * smoothstep(-0.30, 0.30, sunDot);
                    float twilight = exp(-(sunDot / 0.28) * (sunDot / 0.28));

                    float weight = lit * density * stepLength / scaleHeight;
                    colourSum += lerp(_AtmoColor.rgb, _SkySunset.rgb, saturate(twilight * 0.85)) * weight;
                    weightSum += weight;
                }

                float3 averageColour = colourSum / max(weightSum, 1e-5);
                // Squared so a ray skimming the edge (a long path, weight ~8) glows strongly while one
                // straight at the planet (weight ~1) only gets a faint veil.
                float amount = 1.0 - exp(-0.04 * _AtmoParams.y * weightSum * weightSum);
                return half4(averageColour * amount * _AtmoParams.z, 0.0);
            }
            ENDHLSL
        }
    }
}
