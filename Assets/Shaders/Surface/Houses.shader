// =============================================================================
// Terrarium/Houses
// -----------------------------------------------------------------------------
// Purpose : Draws every house of every town in one instanced call, in a style that
//           follows the climate where it stands.
//
// Idea    : A house is one INSTANCE of a small box-and-roof mesh (TownMeshes.BuildHouse). Its
//           place, size, yaw and the local climate come from the GPU list that Houses.compute
//           wrote, so nothing is read back to the CPU. The climate picks the look:
//               cold   = saturate(1 - (T + 2) / 12)          steep dark roof, timber walls, snow on top
//               hot    = smoothstep(18, 30, T)               adobe walls, a flat pale roof
//               wet    = smoothstep(0.35, 0.70, moisture)    a steep thatched roof, green-washed walls
//           The three blend, so a place that is warm and middling gets plaster and terracotta, the
//           temperate look, and a border between biomes shows a gradual change of building style.
//               roofHeight = lerp(1, 0.35, hot * (1 - wet)) * lerp(1, 1.5, max(cold, wet))
//           The roof vertices (mesh part 1) are stretched or squashed by roofHeight about the eaves.
//
//           Size: a house is a few millimetres wide, so from the desk it would vanish. It is never
//           drawn smaller than `_HouseMinPixels` on screen:
//               worldPerPixel = distance * 2 / (P11 * screenHeight)
//               size          = max(houseSize, minPixels * worldPerPixel)
//           so a faraway town shows as a cluster of coloured flecks.
//
//           Night: the windows glow warm on the dark side of the planet, using the same sun
//           direction the planet uses.
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
                float3 localOS : TEXCOORD3;          // position in the house's own units, for the windows
                nointerpolation float4 look : TEXCOORD4;    // x cold, y hot, z wet, w random
                nointerpolation float2 farMix : TEXCOORD5;  // x far (0 near .. 1 fleck), y town density
            };

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

                // The planet's local units become world units through its scale.
                float planetScale = length(float3(_PlanetLocalToWorld._m00, _PlanetLocalToWorld._m10, _PlanetLocalToWorld._m20));
                float wanted = house.positionSize.w * planetScale;

                float distanceToCamera = -mul(UNITY_MATRIX_V, float4(centre, 1.0)).z;
                float worldPerPixel = max(distanceToCamera, 0.001) * 2.0 / (unity_CameraProjection._m11 * _ScreenParams.y);
                float minimum = _HouseMinPixels * worldPerPixel;
                float size = max(wanted, minimum);

                // Climate decides the style.
                float temperature = house.style.z;
                float moisture = house.style.w;
                float cold = saturate(1.0 - (temperature + 2.0) / 12.0);
                float hot = smoothstep(18.0, 30.0, temperature);
                float wet = smoothstep(0.35, 0.70, moisture);
                float roofHeight = lerp(1.0, 0.35, hot * (1.0 - wet)) * lerp(1.0, 1.5, max(cold, wet));

                float3 local = input.positionOS;
                float tall = house.extra.x;
                if (input.barycentric.w > 0.5 || local.y > WALL_HEIGHT + 1e-4)
                {
                    local.y = WALL_HEIGHT + (local.y - WALL_HEIGHT) * roofHeight;
                }

                local.y *= tall;
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
                output.farMix = float2(saturate(minimum / max(wanted, 1e-5) - 1.0), house.extra.y);
                return output;
            }

            half4 HouseFragment(HouseVaryings input) : SV_Target
            {
                float cold = input.look.x;
                float hot = input.look.y;
                float wet = input.look.z;
                float variety = input.look.w;
                bool roof = input.barycentric.w > 0.5;

                // Walls: plaster in the temperate middle, adobe in the heat, timber in the cold, green-washed where it is wet.
                float3 plaster = float3(0.88, 0.80, 0.64);
                float3 adobe = float3(0.82, 0.57, 0.40);
                float3 timber = float3(0.52, 0.35, 0.22);
                float3 washed = float3(0.68, 0.76, 0.60);
                float3 wall = lerp(plaster, adobe, hot);
                wall = lerp(wall, timber, cold);
                wall = lerp(wall, washed, wet * (1.0 - cold) * 0.7);
                wall *= 0.88 + 0.24 * variety;

                // Roofs: terracotta, pale flat clay in the heat, dark slate in the cold, thatch where it is wet.
                float3 terracotta = float3(0.72, 0.32, 0.22);
                float3 clay = float3(0.80, 0.68, 0.48);
                float3 slate = float3(0.30, 0.32, 0.38);
                float3 thatch = float3(0.66, 0.58, 0.24);
                float3 roofColour = lerp(terracotta, clay, hot);
                roofColour = lerp(roofColour, slate, cold);
                roofColour = lerp(roofColour, thatch, wet * (1.0 - cold));
                roofColour *= 0.85 + 0.3 * frac(variety * 7.31);

                float3 albedo = roof ? roofColour : wall;

                // Snow settles on roofs in the cold, more on the faces that look up.
                if (roof)
                {
                    float3 upWS = normalize(input.positionWS - _AtmoCentre.xyz);
                    float lies = saturate(dot(normalize(input.normalWS), upWS) * 1.4);
                    albedo = lerp(albedo, float3(0.94, 0.96, 0.99), smoothstep(0.35, 0.8, cold) * lies);
                }

                float3 color = PainterlyShadeSun(albedo, input.positionWS, normalize(input.normalWS), input.positionCS);

                // Windows glow at night on the dark side of the planet.
                float3 up = normalize(input.positionWS - _AtmoCentre.xyz);
                float night = 1.0 - smoothstep(-0.12, 0.05, dot(up, _SunDirection.xyz));
                if (!roof && night > 0.01 && input.localOS.y < WALL_HEIGHT)
                {
                    float across = abs(input.localOS.z) > 0.499 ? input.localOS.x : input.localOS.z;
                    float pane = (1.0 - smoothstep(0.10, 0.16, abs(frac(across * 2.0 + 0.5) - 0.5))) * (1.0 - smoothstep(0.08, 0.14, abs(input.localOS.y - 0.32)));
                    color += float3(1.0, 0.72, 0.38) * pane * night * 1.2;
                }

                // Ink outline along the visible edges; solid ink once the house is only a fleck on screen.
                float edge = min(input.barycentric.x, min(input.barycentric.y, input.barycentric.z));
                float rim = (1.0 - smoothstep(0.0, 0.07, edge)) * _PainterlyOutlinesEnabled;
                float far = input.farMix.x;
                color = lerp(color, _PainterlyInkColor, saturate(rim) * 0.85 * (1.0 - 0.5 * far));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
