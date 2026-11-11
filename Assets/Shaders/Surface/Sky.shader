// =============================================================================
// Terrarium/Sky
// -----------------------------------------------------------------------------
// Purpose : Paints everything behind the scene. In DESK VIEW it is a dark workshop
//           wall: near-black, a little lighter low down, with a soft warm pool where
//           the desk lamp lights it. In SURFACE MODE it is the planet's sky, and the
//           desk lamp is its SUN: blue by day, deepening overhead; a glowing orange
//           band along the horizon under the sun and a violet sky away from it when
//           the sun is low (sunset); dark with stars once the sun has dropped behind
//           the planet's edge. The two are mixed by _SkyBlend, the camera's own
//           desk-to-surface blend, so the flight down turns the dark room into
//           daylight smoothly.
//
// Idea    : A big sphere that follows the camera. It writes no depth, so it is
//           always behind everything. For each pixel the colour depends only on
//           the VIEW DIRECTION v, the local "up" at the standpoint and the
//           direction to the lamp L, so no textures are needed.
//
// Equations (h = dot(v, up) view height, e = dot(L, up) lamp height,
//            dip = height of the planet's true horizon seen from the camera = -sqrt(1 - (R/r)^2), r = camera distance from the centre):
//     s         = saturate((h - dip) / (1 - dip))          0 at the planet's horizon, 1 overhead
//                 (the planet is small, so its horizon is far below level: 42 degrees at the default height)
//     x         = e - dip                                  the sun's height above the planet's TRUE horizon
//                 (so the sun sets, and the sky darkens, exactly when it can no longer be seen)
//     day       = smoothstep(-0.06, 0.30, x)               0 at night, 1 in full day
//     twilight  = exp(-((x - 0.05) / 0.20)^2)              1 when the sun is at the horizon
//     horizon   = (1 - s)^2.6                              1 along the horizon, 0 overhead
//     sky       = lerp(night, lerp(zenith, horizonColour, horizon), day)
//     side      = dot(flat(v), flat(L)) / 2 + 1/2          1 toward the lamp's compass direction
//     sunset    : sky = lerp(sky, sunsetColour, twilight * (0.9 exp(-3 s) (0.08 + 0.92 side^4) + 0.8 dot(v, L)^6))
//     stars     : jittered point per cell of a 3D grid, hashed, faded out by day
//     clouds    : the view ray is followed to a cloud layer (a sphere SkyCloudHeight above the
//                 surface); Clouds.hlsl gives the density there, shaded by CloudShade; they are
//                 drawn over the sky colour and hide the stars behind them
//
// Parameters (globals set by AtmosphereController from SimConfig):
//     _SkyUp, _SkyLampDir, _SkyBlend, _SkyZenith, _SkyHorizon, _SkySunset,
//     _SkyNight, _SkyDeskBase, _SkyStars, _SkyDeskHaze
// =============================================================================

Shader "Terrarium/Sky"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sky"
            Cull Front
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex SkyVertex
            #pragma fragment SkyFragment
            #pragma require 2darray

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Include/CubeSphere.hlsl"
            #include "Assets/Shaders/Include/Clouds.hlsl"

            Texture2DArray<float4> _ClimateField;
            float _FieldResolution;

            CS_DEFINE_SAMPLER(CS_SampleField4, Texture2DArray<float4>, float4)

            float4x4 _PlanetWorldToLocal;   // turns a world direction into the planet's own frame (rotation only)
            float4 _SkyCloudParams;         // x = height of the sky's cloud layer above the surface, y = pattern scale multiplier, z = 1 when on

            float3 _SkyUp;
            float3 _SkyLampDir;
            float _SkyBlend;
            float _RainAmount;              // 0..1, how hard it rains where the surface camera stands (AtmosphereController)
            float _SkyHorizonDip;          // height (dot with up) of the planet's horizon as the camera sees it; negative
            float4 _SkyZenith;
            float4 _SkyHorizon;
            float4 _SkySunset;
            float4 _SkyNight;
            float4 _SkyDeskBase;
            float _SkyStars;
            float _SkyDeskHaze;

            struct SkyAttributes
            {
                float4 positionOS : POSITION;
            };

            struct SkyVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            SkyVaryings SkyVertex(SkyAttributes input)
            {
                SkyVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            // Stars: each cell of a fine 3D grid may hold one soft point, in a random place, size and brightness.
            // `amount` in 0..1 sets how many cells do.
            float Stars(float3 v, float amount)
            {
                float3 q = v * 260.0;
                float3 cell = floor(q);
                int3 id = (int3)cell;
                float rnd = Hash3(id);
                float present = step(1.0 - 0.25 * amount, rnd);

                float3 jitter = float3(Hash3(id + int3(7, 3, 1)), Hash3(id + int3(13, 5, 9)), Hash3(id + int3(29, 17, 2))) - 0.5;
                float3 centre = cell + 0.5 + jitter * 0.6;
                float size = 0.12 + 0.28 * frac(rnd * 37.0);
                float twinkle = 0.75 + 0.25 * sin(_Time.y * (1.5 + rnd * 3.0) + rnd * 40.0);
                return present * smoothstep(size, 0.0, distance(q, centre)) * twinkle * (0.35 + 0.65 * frac(rnd * 91.0));
            }

            // The dark workshop behind the desk: a dim, warm-grey wall that is a little lighter low down (light
            // bouncing off the bench) and fades to near-black toward the ceiling, with a soft warm pool where the
            // desk lamp lights the wall. No stars: the room is a room.
            float3 DeskSky(float3 v, float3 lampDir)
            {
                float3 colour = _SkyDeskBase.rgb;

                float lowDown = 1.0 - smoothstep(-0.35, 0.75, v.y);
                colour += float3(0.016, 0.014, 0.012) * lowDown * _SkyDeskHaze;

                float pool = pow(saturate(dot(v, lampDir)), 6.0);
                colour += float3(0.30, 0.17, 0.09) * pool * 0.05 * _SkyDeskHaze;
                return colour;
            }

            // Cloud in the sky along a view direction: returns its alpha, and its colour in `colour`.
            // The ray from the camera is followed up to a layer of cloud (a big sphere round the planet);
            // the cloud found there is the same weather as the planet's cloud layer (same noise, same wet
            // ground), so clouds gather above wet land. They turn with the planet because the hit point is
            // converted to the planet's own frame first.
            float SkyCloud(float3 v, float s, float lampHeight, out float3 colour)
            {
                colour = 0.0;
                if (_SkyCloudParams.z < 0.5 || _FieldResolution < 1.0 || s <= 0.0)
                {
                    return 0.0;
                }

                float3 centre = _AtmoCentre.xyz;
                float3 toCamera = _WorldSpaceCameraPos - centre;

                // The layer is a fixed height above the ground, but always at least a little above the camera,
                // so there is always cloud overhead however high you zoom.
                float layerRadius = max(_AtmoCentre.w + _SkyCloudParams.x, length(toCamera) + 0.25);
                float b = dot(toCamera, v);
                float disc = b * b - (dot(toCamera, toCamera) - layerRadius * layerRadius);
                if (disc <= 0.0)
                {
                    return 0.0;
                }

                float t = -b + sqrt(disc);
                float3 hit = toCamera + v * t;
                float3 hitDirection = normalize(hit);
                float3 localDirection = normalize(mul((float3x3)_PlanetWorldToLocal, hitDirection));

                float4 climate = CS_SampleField4(_ClimateField, localDirection, (int)_FieldResolution);
                float thickness;
                float density = CloudDensity(localDirection, climate, _SkyCloudParams.y, thickness);

                // Fewer, thinner clouds toward the planet's horizon, where the layer is seen edge-on.
                float alpha = density * _CloudLayer.y * smoothstep(0.0, 0.25, s) * _CloudLayer.w;

                // In rain the sky clouds over you thicken and darken.
                alpha = saturate(alpha * (1.0 + 1.2 * _RainAmount));

                float3 toLamp = _SunDirection.xyz;
                // Lit by how high the lamp is in THIS sky (mostly), so the clouds are sunlit whenever the sky is
                // blue, whichever way you look; a little of the true angle keeps them shaded on the far side.
                float trueLight = dot(hitDirection, toLamp);
                colour = CloudShade(lerp(trueLight, lampHeight, 0.8), thickness, 0.70);
                colour *= lerp(1.0, 0.65, saturate(_RainAmount));
                return alpha;
            }

            // The planet's sky, from the surface.
            float3 SurfaceSky(float3 v, float3 up, float3 lampDir)
            {
                float h = dot(v, up);
                float e = dot(lampDir, up);

                // The planet is small, so from any height the true horizon dips well below level
                // (_SkyHorizonDip, a negative number). `s` measures the sky from that horizon (0)
                // up to the zenith (1).
                float s = saturate((h - _SkyHorizonDip) / (1.0 - _SkyHorizonDip));

                // The lamp is the sun, so day and night follow whether you can SEE it. x is the lamp's height
                // above the planet's true horizon: the lamp sets (and the sky goes dark) when it drops behind
                // the planet's edge, not when it drops below level, which from up here is long before.
                float x = e - _SkyHorizonDip;
                float day = smoothstep(-0.06, 0.30, x);
                float twilightOffset = (x - 0.05) / 0.20;
                float twilight = exp(-twilightOffset * twilightOffset);
                float horizon = pow(1.0 - s, 2.6);

                float3 dayColour = lerp(_SkyZenith.rgb, _SkyHorizon.rgb, horizon);
                float3 nightColour = lerp(_SkyNight.rgb, _SkyNight.rgb * 3.0 + 0.004, horizon);
                float3 colour = lerp(nightColour, dayColour, day);

                // Sunset: the glow is where the sun is. A warm band along the horizon under it (strongest
                // straight below the lamp, fading round to the sides) and a glow round the lamp itself.
                float3 vFlat = normalize(v - up * h + 1e-4);
                float3 lFlat = normalize(lampDir - up * e + 1e-4);
                float side = dot(vFlat, lFlat) * 0.5 + 0.5;
                float band = exp(-s * 3.0);
                float cap = pow(saturate(dot(v, lampDir)), 6.0);
                float glow = twilight * (band * (0.08 + 0.92 * pow(side, 4.0)) * 0.9 + cap * 0.8);
                colour = lerp(colour, _SkySunset.rgb * 1.15, saturate(glow));

                // Away from the sun the dusk sky goes rose and violet before it reaches the deep blue.
                float3 dusk = float3(0.46, 0.24, 0.50);
                colour = lerp(colour, dusk, twilight * smoothstep(0.15, 0.50, s) * (1.0 - smoothstep(0.55, 0.95, s)) * (0.25 + 0.5 * (1.0 - side)));

                // Clouds in the sky (lit by how high the sun is).
                float3 cloudColour;
                float cloudAlpha = SkyCloud(v, s, x, cloudColour);
                colour = lerp(colour, cloudColour, cloudAlpha);

                // The sun's own glow: a wide soft halo and a tight bright core, only while it is above the horizon.
                float cosAngle = saturate(dot(v, lampDir));
                float3 lampColour = lerp(float3(1.0, 0.92, 0.72), _SkySunset.rgb, saturate(twilight));
                colour += lampColour * (pow(cosAngle, 24.0) * 0.35 + pow(cosAngle, 220.0) * 0.8) * smoothstep(-0.04, 0.03, x);

                // In rain the whole sky greys and dims.
                float rainGrey = dot(colour, float3(0.3, 0.55, 0.15));
                colour = lerp(colour, float3(rainGrey, rainGrey, rainGrey) * 0.8, saturate(_RainAmount) * 0.5);

                // Below the planet's horizon the view is of dim haze.
                colour *= lerp(1.0, 0.55, saturate((_SkyHorizonDip - h) * 4.0));

                // Stars come out as the lamp goes down.
                colour += Stars(v, saturate(_SkyStars * 1.6)) * (1.0 - day) * (1.0 - cloudAlpha) * saturate(s * 3.0) * float3(0.8, 0.85, 1.0);
                return colour;
            }

            half4 SkyFragment(SkyVaryings input) : SV_Target
            {
                float3 v = normalize(input.positionWS - _WorldSpaceCameraPos);
                float3 up = normalize(_SkyUp);
                float3 lampDir = normalize(_SkyLampDir);

                float3 desk = DeskSky(v, lampDir);
                float3 surface = SurfaceSky(v, up, lampDir);
                return half4(lerp(desk, surface, _SkyBlend), 1.0);
            }
            ENDHLSL
        }
    }
}
