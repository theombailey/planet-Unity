// =============================================================================
// Terrarium/Houses
// -----------------------------------------------------------------------------
// Purpose : Draws every house of every settlement in one instanced call. What a house looks
//           like follows what the people know (its style tier) and the climate where it stands.
//
// Idea    : A house is one INSTANCE of a small box-and-roof mesh (TownMeshes.BuildHouse). Its
//           place, size, yaw, style tier, build progress and the local climate come from the GPU
//           list that Houses.compute wrote, so nothing is read back to the CPU. There is no model
//           per style: the same mesh is reshaped and recoloured by numbers.
//
//           STYLE TIER (set by technology):
//               0 tent        almost no wall, a steep A-frame of cloth or hide
//               1 mud house   half-height mud walls, a thatched roof
//               2 brick house full walls of brick courses, a tiled or slate roof
//           shape:  wallHeight = (0.18, 0.75, 1.0)[tier],  footprint = (0.85, 1.0, 1.12)[tier]
//                   the roof is stretched or squashed about the eaves by roofHeight
//
//           CLIMATE shapes the roof of a built house (tier 1 and 2) and tints it:
//               cold   = saturate(1 - (T + 2) / 12)          steep dark roof, snow on top
//               hot    = smoothstep(18, 30, T)               paler walls, a flat roof
//               wet    = smoothstep(0.35, 0.70, moisture)    a steep thatched roof
//               roofHeight = lerp(1, 0.35, hot * (1 - wet)) * lerp(1, 1.5, max(cold, wet))
//           A tent always has the steep roof of an A-frame.
//
//           BUILD PROGRESS: a house being built rises and widens from nothing (and, being taken down,
//           sinks and narrows to nothing again):
//               grow = smoothstep(0, 1, progress);  height *= lerp(0.15, 1, grow);  width *= lerp(0.5, 1, grow)
//           A house being REBUILT in a better style (previous tier >= 0) does not shrink: it keeps its
//           size and its old look until the work is half done, then shows the new style. Scaffolding
//           (Scaffolds.shader) stands round it all the while, so the change happens under cover.
//
//           Size: a house is a few millimetres wide, so from the desk it would vanish. It is never
//           drawn smaller than `_HouseMinPixels` on screen:
//               worldPerPixel = distance * 2 / (P11 * screenHeight)
//               size          = max(houseSize, minPixels * worldPerPixel)
//           so a faraway settlement shows as a scatter of coloured flecks.
//
//           Night: windows glow warm on the dark side of the planet (a tent glows all over, being
//           cloth with a fire in it), and a house too small to see detail shows as a lit fleck, so a
//           settlement still shows from the desk when the sun is away.
//
// Equations:
//     up      = normalize(position)            (local "up" on the sphere)
//     east    = normalize(cross(ref, up)),  north = cross(up, east)
//     forward = east * cos(yaw) + north * sin(yaw),   side = north * cos(yaw) - east * sin(yaw)
//     world   = _PlanetLocalToWorld * position + (side x + up y + forward z) * size
//
// Parameters (globals set by TownRenderer): _PlanetLocalToWorld, _HouseMinPixels.
// Buffers:   _Houses (see Houses.compute: positionSize, style, extra)
// =============================================================================

Shader "Terrarium/Houses"
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
            #pragma vertex HouseVertex
            #pragma fragment HouseFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/Include/Painterly.hlsl"
            #include "Assets/Shaders/Include/AtmosphereGlobals.hlsl"

            float _PainterlyOutlinesEnabled;   // the shared outline switch

            struct House
            {
                float4 positionSize;
                float4 style;
                float4 extra;
            };

            StructuredBuffer<House> _Houses;
            float4x4 _PlanetLocalToWorld;
            float _HouseMinPixels;

            static const float WALL_HEIGHT = 0.6;

            struct HouseAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 barycentric : TEXCOORD0;      // xyz barycentric (diagonals hidden), w part: 0 wall, 1 roof
                uint instanceID : SV_InstanceID;
            };

            struct HouseVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 barycentric : TEXCOORD2;
                float3 localOS : TEXCOORD3;          // position in the house's own units, for windows and bricks
                nointerpolation float4 look : TEXCOORD4;    // x cold, y hot, z wet, w random
                nointerpolation float2 farMix : TEXCOORD5;  // x far (0 near .. 1 fleck), y town density
                nointerpolation float2 build : TEXCOORD6;   // x style tier, y progress
            };

            float TierValue(int tier, float a, float b, float c)
            {
                return tier <= 0 ? a : (tier == 1 ? b : c);
            }

            HouseVaryings HouseVertex(HouseAttributes input)
            {
                HouseVaryings output;
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                output.positionWS = 0;
                output.normalWS = float3(0, 1, 0);
                output.barycentric = input.barycentric;
                output.localOS = input.positionOS;
                output.look = 0;
                output.farMix = 0;
                output.build = 0;

                House house = _Houses[input.instanceID];
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

                float previous = house.extra.y;
                bool rebuilding = previous > -0.5;
                int tier = (int)round(house.extra.w);
                float progress = saturate(house.extra.z);
                if (rebuilding && progress < 0.5)
                {
                    tier = (int)round(previous);
                }

                float grow = rebuilding ? 1.0 : smoothstep(0.0, 1.0, progress);

                // The planet's local units become world units through its scale.
                float planetScale = length(float3(_PlanetLocalToWorld._m00, _PlanetLocalToWorld._m10, _PlanetLocalToWorld._m20));
                float wanted = house.positionSize.w * planetScale * TierValue(tier, 0.85, 1.0, 1.12);

                float distanceToCamera = -mul(UNITY_MATRIX_V, float4(centre, 1.0)).z;
                float worldPerPixel = max(distanceToCamera, 0.001) * 2.0 / (unity_CameraProjection._m11 * _ScreenParams.y);
                float minimum = _HouseMinPixels * worldPerPixel;
                float size = max(wanted, minimum);

                // Climate shapes the roof of a built house; a tent is always an A-frame.
                float temperature = house.style.z;
                float moisture = house.style.w;
                float cold = saturate(1.0 - (temperature + 2.0) / 12.0);
                float hot = smoothstep(18.0, 30.0, temperature);
                float wet = smoothstep(0.35, 0.70, moisture);
                float climateRoof = lerp(1.0, 0.35, hot * (1.0 - wet)) * lerp(1.0, 1.5, max(cold, wet));
                float roofHeight = tier == 0 ? 1.6 : climateRoof;
                float wallScale = TierValue(tier, 0.18, 0.75, 1.0);

                float3 local = input.positionOS;
                float eaves = WALL_HEIGHT * wallScale;
                if (local.y > WALL_HEIGHT + 1e-4 || input.barycentric.w > 0.5)
                {
                    local.y = eaves + (local.y - WALL_HEIGHT) * roofHeight;
                }
                else
                {
                    local.y *= wallScale;
                }

                local.y *= house.extra.x * lerp(0.15, 1.0, grow);
                local.xz *= lerp(0.5, 1.0, grow);
                output.positionWS = centre + (worldSide * local.x + worldUp * local.y + worldForward * local.z) * size;
                output.positionCS = TransformWorldToHClip(output.positionWS);

                // Face normals: rotate the mesh normal into the house's frame. Roofs of a different pitch tilt their normals.
                float3 n = input.normalOS;
                if (input.barycentric.w > 0.5)
                {
                    n = normalize(float3(n.x, n.y / max(roofHeight, 0.3), n.z));
                }

                output.normalWS = normalize(worldSide * n.x + worldUp * n.y + worldForward * n.z);
                output.look = float4(cold, hot, wet, house.style.y);
                output.farMix = float2(saturate(minimum / max(wanted, 1e-5) - 1.0), 0.0);
                output.build = float2((float)tier, progress);
                return output;
            }

            half4 HouseFragment(HouseVaryings input) : SV_Target
            {
                float cold = input.look.x;
                float hot = input.look.y;
                float wet = input.look.z;
                float variety = input.look.w;
                int tier = (int)round(input.build.x);
                bool roof = input.barycentric.w > 0.5;

                // Tent: cloth in the warm, hide in the cold; wall and roof are one cover.
                float3 canvas = float3(0.86, 0.80, 0.66);
                float3 hide = float3(0.54, 0.40, 0.28);
                float3 tentColour = lerp(canvas, hide, cold) * (0.88 + 0.24 * variety);

                // Mud house: earth walls (paler in the heat), thatch (darker in the cold).
                float3 mudWall = lerp(float3(0.58, 0.42, 0.29), float3(0.78, 0.62, 0.44), hot) * (0.9 + 0.2 * variety);
                float3 thatch = lerp(float3(0.70, 0.60, 0.30), float3(0.46, 0.38, 0.22), cold) * (0.88 + 0.24 * frac(variety * 7.31));

                // Brick house: red-brown courses, and a roof that follows the climate.
                float3 brickWall = float3(0.64, 0.31, 0.23) * (0.9 + 0.2 * variety);
                float3 terracotta = float3(0.72, 0.32, 0.22);
                float3 clay = float3(0.80, 0.68, 0.48);
                float3 slate = float3(0.30, 0.32, 0.38);
                float3 tile = lerp(terracotta, clay, hot);
                tile = lerp(tile, slate, cold) * (0.85 + 0.3 * frac(variety * 7.31));

                float3 albedo = tentColour;
                if (tier == 1)
                {
                    albedo = roof ? thatch : mudWall;
                }
                else if (tier >= 2)
                {
                    albedo = roof ? tile : brickWall;

                    // Brick courses: pale mortar lines, the joints staggered from course to course.
                    if (!roof)
                    {
                        float across = abs(input.localOS.z) > 0.499 ? input.localOS.x : input.localOS.z;
                        float course = input.localOS.y * 9.0;
                        float jointShift = 0.5 * fmod(floor(course), 2.0);
                        float mortar = max(smoothstep(0.86, 0.94, frac(course)), smoothstep(0.90, 0.97, frac(across * 5.0 + jointShift)));
                        albedo = lerp(albedo, float3(0.80, 0.74, 0.64), mortar * 0.7);
                    }
                }

                // Snow settles on roofs in the cold, more on the faces that look up.
                if (roof)
                {
                    float3 upWS = normalize(input.positionWS - _AtmoCentre.xyz);
                    float lies = saturate(dot(normalize(input.normalWS), upWS) * 1.4);
                    albedo = lerp(albedo, float3(0.94, 0.96, 0.99), smoothstep(0.35, 0.8, cold) * lies);
                }

                float3 color = PainterlyShadeSun(albedo, input.positionWS, normalize(input.normalWS), input.positionCS);

                // Night: windows glow (a tent glows all over); a house only a fleck on screen shows as a lit speck.
                float3 up = normalize(input.positionWS - _AtmoCentre.xyz);
                float night = 1.0 - smoothstep(-0.12, 0.05, dot(up, _SunDirection.xyz));
                float finished = smoothstep(0.5, 0.9, input.build.y);
                float far = input.farMix.x;
                if (night > 0.01 && finished > 0.01)
                {
                    float3 warm = float3(1.0, 0.72, 0.38);
                    if (tier == 0)
                    {
                        color += warm * 0.45 * night * finished;
                    }
                    else if (!roof && input.localOS.y < WALL_HEIGHT)
                    {
                        float across = abs(input.localOS.z) > 0.499 ? input.localOS.x : input.localOS.z;
                        float pane = (1.0 - smoothstep(0.10, 0.16, abs(frac(across * 2.0 + 0.5) - 0.5))) * (1.0 - smoothstep(0.08, 0.14, abs(input.localOS.y - 0.32)));
                        color += warm * pane * night * finished * 1.2;
                    }

                    color += warm * far * night * finished * 0.8;
                }

                // Ink outline along the visible edges; softer once the house is only a fleck on screen.
                float edge = min(input.barycentric.x, min(input.barycentric.y, input.barycentric.z));
                float rim = (1.0 - smoothstep(0.0, 0.07, edge)) * _PainterlyOutlinesEnabled;
                color = lerp(color, _PainterlyInkColor, saturate(rim) * 0.85 * (1.0 - 0.5 * far));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
