// =============================================================================
// Terrarium/PlanetSurface
// -----------------------------------------------------------------------------
// Purpose : Draws the planet from the simulation fields: terrain relief, ocean,
//           and watercolour biomes (ice, rock, sand, desert, grass, tundra).
//
// Idea    : Everything is looked up by DIRECTION. The mesh is a unit sphere; a
//           point's direction from the centre is its position, and the fields
//           (cube-sphere textures, see CubeSphere.hlsl) are sampled there with
//           the seam-correct bilinear sampler. So colour, height, temperature
//           and moisture agree with the simulation exactly.
//
//   RELIEF   The vertex shader pushes each vertex out to radius
//                r = 1 + heightScale * max(height, 0)
//            so land stands above a flat sea. The pixel shader then tilts the
//            normal by the height field's slope (central differences) so the
//            light picks out mountains.
//   BIOMES   Each biome has a "how much" weight from temperature, moisture and
//            height, built from smoothsteps so the weights overlap and blend.
//            Biome edges are painted, not computed:
//              BLEED   low-frequency noise nudges temperature and moisture
//                      before the weights are formed, so boundaries wander.
//              POOLING at the middle of a blend (weight near 0.5) the pigment
//                      is darkened, like watercolour collecting at the edge of
//                      a wash:  pool = 1 - 0.18 * 4 w (1 - w).
//   OCEAN    A flat wash that deepens with depth, a few drifting ink wave
//            strokes, and a broken white foam line at the shoreline.
//   CLOUD    A cloud between a ground point and the lamp darkens the ground: the cloud
//   SHADOWS  that shades a point is found by following the lamp ray up to the cloud
//            layer, and its density comes from the same function as the clouds you
//            see (Clouds.hlsl), so a shadow always sits under its cloud.
//   LIGHT    The result goes through the shared painterly shading
//            (banded tone + hatching, Painterly.hlsl).
//
// Equations:
//   colour weights:  w_snow   = smoothstep(0, -4, T)
//                    w_rock   = smoothstep(0.50, 0.68, height)   (+ hard, dry ground)
//                    w_sand   = 1 - smoothstep(0, 0.05, height)
//                    w_desert = smoothstep(0.30, 0.12, M) * smoothstep(8, 22, T)
//                    grass    = lerp(dry, lush, saturate((M - 0.15) / 0.6)), cooled toward tundra
//   slope tilt:      normal = normalize( n - s1 t1 - s2 t2 )
//                    s_i = normalStrength * heightScale * dh/d(arc along t_i),
//                    with the total tilt capped at ~22 degrees
//   (T = temperature deg C, M = moisture 0..1, both with noise bleed applied.)
//
// Parameters: palette colours and _NormalStrength below (per material);
//             _PlanetHeightScale (global, from SimConfig.terrainHeightScale).
//
// Buffers & textures (global, published by FieldSet each frame):
//   _TerrainField  R height (0 = shoreline, <0 sea), G rock hardness
//   _ClimateField  R temperature, G moisture, B insolation
//   _SoilField     R nutrients, G organic matter, B canopy
//   _FieldResolution  texels per face edge
// =============================================================================

Shader "Terrarium/PlanetSurface"
{
    Properties
    {
        _OceanShallow ("Ocean shallow", Color) = (0.36, 0.64, 0.68, 1)
        _OceanDeep ("Ocean deep", Color) = (0.10, 0.25, 0.42, 1)
        _Ice ("Sea ice", Color) = (0.86, 0.93, 0.96, 1)
        _Sand ("Sand", Color) = (0.86, 0.77, 0.52, 1)
        _GrassLush ("Grass (wet)", Color) = (0.28, 0.55, 0.24, 1)
        _GrassDry ("Grass (dry)", Color) = (0.72, 0.64, 0.30, 1)
        _Tundra ("Tundra", Color) = (0.46, 0.52, 0.40, 1)
        _Desert ("Desert", Color) = (0.85, 0.65, 0.38, 1)
        _Rock ("Rock", Color) = (0.50, 0.45, 0.43, 1)
        _Snow ("Snow", Color) = (0.94, 0.96, 0.99, 1)
        _Mycelium ("Mycelium ink", Color) = (0.17, 0.10, 0.07, 1)
        _NormalStrength ("Relief shading strength", Range(0, 20)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Assets/Shaders/Include/CubeSphere.hlsl"

        Texture2DArray<float4> _TerrainField;
        Texture2DArray<float4> _ClimateField;
        Texture2DArray<float4> _SoilField;
        Texture2DArray<float> _TrailField;
        float _FieldResolution;
        float _PlanetHeightScale;

        CS_DEFINE_SAMPLER(CS_SampleField4, Texture2DArray<float4>, float4)
        CS_DEFINE_SAMPLER(CS_SampleField1, Texture2DArray<float>, float)

        CBUFFER_START(UnityPerMaterial)
            float4 _OceanShallow;
            float4 _OceanDeep;
            float4 _Ice;
            float4 _Sand;
            float4 _GrassLush;
            float4 _GrassDry;
            float4 _Tundra;
            float4 _Desert;
            float4 _Rock;
            float4 _Snow;
            float4 _Mycelium;
            float _NormalStrength;
        CBUFFER_END

        #include "Assets/Shaders/Include/Painterly.hlsl"
        #include "Assets/Shaders/Include/Clouds.hlsl"

        // Height of the ground in the field, 0 until the fields exist.
        float TerrainHeight(float3 direction)
        {
            int n = (int)_FieldResolution;
            return n > 0 ? CS_SampleField4(_TerrainField, direction, n).r : 0.0;
        }

        // Radius of the surface in that direction: land stands above a flat sea.
        float SurfaceRadius(float3 direction)
        {
            return 1.0 + _PlanetHeightScale * max(TerrainHeight(direction), 0.0);
        }

        struct SurfaceAttributes
        {
            float4 positionOS : POSITION;
        };

        struct SurfaceVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 directionOS : TEXCOORD1;
        };

        SurfaceVaryings SurfaceVertex(SurfaceAttributes input)
        {
            SurfaceVaryings output;
            float3 direction = normalize(input.positionOS.xyz);
            output.directionOS = direction;
            output.positionWS = TransformObjectToWorld(direction * SurfaceRadius(direction));
            output.positionCS = TransformWorldToHClip(output.positionWS);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex SurfaceVertex
            #pragma fragment SurfaceFragment
            #pragma require 2darray
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            // 1 inside a blend's middle, 0 at its ends: darkens pigment where two washes meet.
            float Pooling(float weight)
            {
                return 1.0 - 0.18 * 4.0 * weight * (1.0 - weight);
            }

            // Surface normal tilted by the height field's slope, in object space.
            float3 TiltedNormal(float3 direction)
            {
                float3 t1 = normalize(cross(direction, abs(direction.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 t2 = cross(direction, t1);

                // Sample a small angle away (one texel is about 0.006 rad at the Medium preset).
                const float epsilon = 0.012;
                float hx0 = max(TerrainHeight(normalize(direction - epsilon * t1)), 0.0);
                float hx1 = max(TerrainHeight(normalize(direction + epsilon * t1)), 0.0);
                float hy0 = max(TerrainHeight(normalize(direction - epsilon * t2)), 0.0);
                float hy1 = max(TerrainHeight(normalize(direction + epsilon * t2)), 0.0);

                // dr/d(arc) = heightScale * dh/d(arc); the normal leans against the slope.
                float scale = _NormalStrength * _PlanetHeightScale / (2.0 * epsilon);
                float2 slope = float2(hx1 - hx0, hy1 - hy0) * scale;

                // Cap the tilt (about 22 degrees): a steep coast must shade as a slope, never turn
                // away from the light entirely and render black.
                slope *= min(1.0, 0.4 / max(length(slope), 1e-5));
                return normalize(direction - slope.x * t1 - slope.y * t2);
            }

            float3 OceanAlbedo(float3 direction, float height, float temperature, float noiseA)
            {
                float depth = saturate(-height * 2.5);
                float3 colour = lerp(_OceanShallow.rgb, _OceanDeep.rgb, smoothstep(0.0, 1.0, depth));
                colour *= 1.0 + (noiseA - 0.5) * 0.14;   // uneven wash

                // A few ink wave strokes: thin contour lines of slowly drifting noise.
                float wave = frac(Fbm(direction * 22.0 + float3(0.0, _Time.y * 0.03, 0.0), 3) * 7.0);
                float stroke = smoothstep(0.90, 0.98, wave) * (1.0 - smoothstep(0.98, 1.0, wave));
                colour = lerp(colour, colour * 1.45 + 0.04, stroke * 0.55 * (1.0 - depth * 0.5));

                // Foam: a broken pale line hugging the shore.
                float shoreBand = 1.0 - smoothstep(0.0, 0.05, -height);
                float broken = smoothstep(0.38, 0.62, ValueNoise(direction * 55.0 + float3(0.0, _Time.y * 0.05, 0.0)));
                colour = lerp(colour, float3(0.92, 0.96, 0.97), shoreBand * broken * 0.85);

                // Sea ice below freezing.
                float ice = smoothstep(-1.0, -6.0, temperature + (noiseA - 0.5) * 6.0);
                return lerp(colour, _Ice.rgb, ice);
            }

            float3 LandAlbedo(float3 direction, float height, float hardness, float temperature, float moisture, float noiseA, float noiseB)
            {
                // Bleed: noise nudges the quantities biomes are chosen from, so boundaries wander.
                float t = temperature + (noiseA - 0.5) * 8.0;
                float m = saturate(moisture + (noiseB - 0.5) * 0.35);

                // Vegetation: dry -> lush with moisture, cooled toward tundra in the cold.
                float3 colour = lerp(_GrassDry.rgb, _GrassLush.rgb, saturate((m - 0.15) / 0.6));
                colour = lerp(_Tundra.rgb, colour, smoothstep(0.0, 14.0, t));

                // Desert: hot and dry.
                float desert = smoothstep(0.30, 0.12, m) * smoothstep(8.0, 22.0, t);
                colour = lerp(colour, _Desert.rgb, desert) * Pooling(desert);

                // Sand along coasts.
                float sand = 1.0 - smoothstep(0.0, 0.05 + (noiseB - 0.5) * 0.04, height);
                colour = lerp(colour, _Sand.rgb, sand) * Pooling(sand);

                // Bare rock: high ground, and hard ground that is too dry to hold plants.
                float rock = smoothstep(0.50, 0.68, height + (noiseA - 0.5) * 0.12);
                rock = max(rock, smoothstep(0.62, 0.80, hardness) * smoothstep(0.35, 0.15, m));
                colour = lerp(colour, _Rock.rgb * (0.9 + 0.2 * noiseB), rock) * Pooling(rock);

                // Snow and ice below freezing.
                float snow = smoothstep(0.0, -4.0, t);
                colour = lerp(colour, _Snow.rgb, snow) * Pooling(snow);
                return colour;
            }

            half4 SurfaceFragment(SurfaceVaryings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                int n = (int)_FieldResolution;

                float4 terrain = n > 0 ? CS_SampleField4(_TerrainField, direction, n) : float4(0, 0, 0, 0);
                float4 climate = n > 0 ? CS_SampleField4(_ClimateField, direction, n) : float4(15, 0.5, 0, 0);

                float height = terrain.r;
                float noiseA = Fbm(direction * 7.0 + 3.1, 3);
                float noiseB = Fbm(direction * 15.0 + 9.7, 3);

                float3 albedo = height < 0.0
                    ? OceanAlbedo(direction, height, climate.r, noiseA)
                    : LandAlbedo(direction, height, terrain.g, climate.r, climate.g, noiseA, noiseB);

                // The fungal / root network: ink threads wherever the physarum trail is strong. Up close
                // the threshold drops and a fuzz of noise breaks the lines into fine root hairs.
                if (height >= 0.0 && n > 0)
                {
                    float trail = CS_SampleField1(_TrailField, direction, n);
                    float closeness = 1.0 - smoothstep(1.2, 5.0, distance(_WorldSpaceCameraPos, input.positionWS));
                    float fuzz = ValueNoise(direction * 220.0) - 0.5;
                    float threshold = lerp(0.45, 0.12, closeness);
                    float thread = smoothstep(threshold, threshold + 0.3, trail + fuzz * 0.25 * closeness);
                    albedo = lerp(albedo, _Mycelium.rgb, thread * 0.8);
                }

                // Cloud shadows: darken the ground where a cloud stands between it and the lamp.
                if (n > 0 && _CloudLayer.w > 0.5 && _CloudLayer.z > 0.0)
                {
                    float3 towardLamp = normalize(_TerrariumLampPosition.xyz - input.positionWS);
                    float3 upWS = normalize(input.positionWS - _AtmoCentre.xyz);
                    float lampHeight = dot(upWS, towardLamp);
                    if (lampHeight > 0.02)
                    {
                        float3 cloudWS = CloudAboveAlongLamp(input.positionWS, _AtmoCentre.xyz, towardLamp, _AtmoCentre.w * _CloudLayer.x);
                        float3 cloudDirection = normalize(TransformWorldToObject(cloudWS));
                        float cloudMoisture = CS_SampleField4(_ClimateField, cloudDirection, n).g;
                        float cloudThickness;
                        float shade = CloudDensity(cloudDirection, cloudMoisture, 1.0, cloudThickness);
                        albedo *= 1.0 - _CloudLayer.z * shade * smoothstep(0.02, 0.25, lampHeight);
                    }
                }

                float3 normalOS = height < 0.0 ? direction : TiltedNormal(direction);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

                return half4(PainterlyShade(albedo, input.positionWS, normalWS, input.positionCS), 1.0);
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
            #pragma require 2darray
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVertex(SurfaceAttributes input)
            {
                ShadowVaryings output;
                float3 direction = normalize(input.positionOS.xyz);
                float3 positionWS = TransformObjectToWorld(direction * SurfaceRadius(direction));
                float3 normalWS = TransformObjectToWorldNormal(direction);

            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
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
            #pragma require 2darray

            float4 DepthVertex(SurfaceAttributes input) : SV_POSITION
            {
                float3 direction = normalize(input.positionOS.xyz);
                return TransformWorldToHClip(TransformObjectToWorld(direction * SurfaceRadius(direction)));
            }

            half4 DepthOnlyFragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma require 2darray

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVertex(SurfaceAttributes input)
            {
                DepthNormalsVaryings output;
                float3 direction = normalize(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(direction * SurfaceRadius(direction)));
                output.normalWS = TransformObjectToWorldNormal(direction);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
