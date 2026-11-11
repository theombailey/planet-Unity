// =============================================================================
// Terrarium/Nixie
// -----------------------------------------------------------------------------
// Purpose : Draws one nixie tube: a glass tube with a glowing orange digit inside,
//           like the old cold-cathode number displays. The digit is chosen per tube
//           (property _Digit), so a row of these shows a number.
// Idea    : Everything is drawn by distance maths on a flat quad, no textures.
//           The tube is a rounded rectangle (pixels outside are discarded). Each digit
//           is a SEVEN-SEGMENT shape: seven short straight "wires" (a..g). A pixel's
//           glow depends on how close it is to the nearest LIT wire, so the digit is a
//           bright thin line with a soft orange halo around it. The unlit wires are
//           still there, very faint, as in a real tube, where all the digit cathodes
//           are stacked behind one another.
//
// Equations (p = position on the quad, x in -0.5..0.5, y in -0.9..0.9):
//   distance to a wire from A to B:  d = | p - (A + B) * clamp( dot(p - A, B - A) / |B - A|^2, 0, 1 ) |
//   core  = smoothstep(0.040, 0.022, d)          the bright wire
//   halo  = exp( -d^2 / 0.014 )                  the soft glow
//   glass = rounded box: |p| inside (0.46, 0.86) with corner radius 0.22
//   Digit masks (bit 0 = a ... bit 6 = g): 0 = 0x3F, 1 = 0x06, 2 = 0x5B, 3 = 0x4F, 4 = 0x66,
//                                          5 = 0x6D, 6 = 0x7D, 7 = 0x07, 8 = 0x7F, 9 = 0x6F, minus = 0x40
//
// Parameters (per tube, via a property block): _Digit (-1 blank, 0..9, 10 minus),
//   _Point (1 = decimal point lit), _Glow (brightness, 0 = off), _Seed (flicker phase).
// =============================================================================

Shader "Terrarium/Nixie"
{
    Properties
    {
        _Digit ("Digit (-1 blank, 10 minus)", Float) = -1
        _Point ("Decimal point", Float) = 0
        _Glow ("Glow", Float) = 1
        _Seed ("Flicker seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Nixie"
            Cull Off

            HLSLPROGRAM
            #pragma vertex NixieVertex
            #pragma fragment NixieFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Digit;
                float _Point;
                float _Glow;
                float _Seed;
            CBUFFER_END

            struct NixieAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct NixieVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            NixieVaryings NixieVertex(NixieAttributes input)
            {
                NixieVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Distance from p to the straight wire a-b.
            float WireDistance(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / dot(ab, ab));
                return length(p - (a + ab * t));
            }

            // Signed distance to a rounded box (negative inside).
            float RoundedBox(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            half4 NixieFragment(NixieVaryings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * float2(1.0, 1.8);

                // The glass tube's outline: everything outside is not drawn.
                float glass = RoundedBox(p, float2(0.46, 0.86), 0.22);
                clip(-glass);

                const float w = 0.20;
                const float h = 0.50;
                const float2 wireStart[7] = { float2(-w, h), float2(w, h), float2(w, 0.0), float2(-w, -h), float2(-w, 0.0), float2(-w, h), float2(-w, 0.0) };
                const float2 wireEnd[7]   = { float2(w, h), float2(w, 0.0), float2(w, -h), float2(w, -h), float2(-w, -h), float2(-w, 0.0), float2(w, 0.0) };
                static const uint masks[12] = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F, 0x40, 0x00 };

                int digit = _Digit < -0.5 ? 11 : clamp((int)round(_Digit), 0, 10);
                uint mask = masks[digit];

                float flicker = 0.92 + 0.08 * sin(_Time.y * 31.0 + _Seed * 12.9) * sin(_Time.y * 7.3 + _Seed * 4.1);

                float core = 0.0;
                float halo = 0.0;
                float ghost = 0.0;
                [unroll]
                for (int i = 0; i < 7; i++)
                {
                    float d = WireDistance(p, wireStart[i], wireEnd[i]);
                    ghost = max(ghost, smoothstep(0.016, 0.006, d));
                    if (((mask >> i) & 1u) != 0u)
                    {
                        core = max(core, smoothstep(0.040, 0.022, d));
                        halo = max(halo, exp(-d * d / 0.014));
                    }
                }

                // The decimal point, bottom right.
                float pointDistance = length(p - float2(0.33, -0.56));
                if (_Point > 0.5)
                {
                    core = max(core, smoothstep(0.07, 0.04, pointDistance));
                    halo = max(halo, exp(-pointDistance * pointDistance / 0.02));
                }

                // Tube colours: dark amber glass, bright orange glow, pale-hot core.
                float3 glassColour = float3(0.040, 0.022, 0.014);
                float3 glowColour = float3(1.00, 0.36, 0.06);
                float3 coreColour = float3(1.00, 0.80, 0.55);

                float lit = _Glow * flicker;
                float3 colour = glassColour;
                colour += float3(0.30, 0.10, 0.02) * halo * 0.55 * lit;        // haze inside the glass
                colour += float3(0.16, 0.07, 0.04) * ghost * 0.5;              // the unlit digits behind
                colour = lerp(colour, glowColour * 1.6, saturate(halo * 0.5) * lit * 0.5);
                colour = lerp(colour, coreColour * 1.8, core * lit);

                // Glass: a bright rim at the edge and a long highlight down the left side.
                float rim = smoothstep(-0.07, 0.0, glass);
                colour += float3(0.30, 0.26, 0.22) * rim * 0.5;
                float streak = smoothstep(0.07, 0.0, abs(p.x + 0.33)) * smoothstep(-0.7, 0.4, p.y) * (1.0 - smoothstep(0.4, 0.7, p.y));
                colour += float3(0.20, 0.18, 0.16) * streak * 0.35;

                // The brass base at the bottom of the tube.
                float base = smoothstep(-0.70, -0.74, p.y);
                colour = lerp(colour, float3(0.30, 0.22, 0.12) * (0.7 + 0.3 * sin(p.x * 40.0)), base);

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
