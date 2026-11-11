// =============================================================================
// Terrarium/Leaves
// -----------------------------------------------------------------------------
// Purpose : Draws every leaf of every tree in one instanced call, as small
//           camera-facing leaf cards, and changes their colour and number with
//           the seasons.
//
// Idea    : Each leaf is one INSTANCE of a unit quad. Its data (position, size,
//           a random phase) sits in a GPU buffer, written by the CPU when a tree
//           grows. The leaf's tree is known from its instance number: tree slot =
//           instance / leavesPerTree. Each tree also has a small state record
//           (leaf amount, autumn tint, health, decay) updated every frame, so a
//           whole tree's leaves can turn and fall without touching the leaf buffer.
//
//           Leaves FALL one by one: leaf i stays while the tree's leaf amount is
//           above its own random phase, and shrinks to nothing as the amount
//           passes it:   present = smoothstep(phase, phase + 0.15, leafAmount).
//           Colour is mixed from summer greens to autumn oranges and reds by the
//           autumn tint, and toward dry brown as a tree dies:
//               albedo = lerp( lerp(summer(phase), autumn(phase), autumnTint), dead, decay )
//           Each leaf is an ellipse cut out of its card (pixels outside are discarded)
//           with a dark ink rim, then lit with the shared painterly shading.
//
// Equations:
//     world centre = _PlanetLocalToWorld * leafPosition
//     corner       = centre + (right * qx + up * qy) * size        right, up = the camera's axes
//     shape        : keep the pixel if (px / 0.62)^2 + py^2 < 1,   (px, py) in -1..1 across the card
//     rim          = smoothstep(0.55, 0.9, radius)
//
// Parameters (globals set by TreeManager each frame): _PlanetLocalToWorld, _LeavesPerTree.
// Genes   : _TreeTraits (per tree: leaf size, evergreen, warmth, hue) sizes the leaf, makes an evergreen's leaf a
//           narrow needle, and tints the summer green (see TreeGenes.cs). A new leaf unfurls over _TreeGrowth.y
//           seconds after its branch has grown (_TreeGrowth.x is the sim time).
//
// Buffers:   _Leaves   StructuredBuffer<LeafData>   40 bytes each (see LeafData in TreeMesher.cs)
//            _TreeState StructuredBuffer<float4>    x leaf amount, y autumn tint, z health, w decay
// Passes   : Forward draws; DepthOnly and DepthNormals write the leaf shapes into the depth / normals
//            textures so the fullscreen ink edges see them. (Leaves cast no shadows.)
// =============================================================================

Shader "Terrarium/Leaves"
{
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Assets/Shaders/Include/Painterly.hlsl"

        struct LeafData
        {
            float3 position;
            float size;
            float3 normal;
            float phase;
            float birth;      // sim time the leaf starts to unfurl
            float reserved;
        };

        float _PainterlyOutlinesEnabled;   // the shared outline switch (also declared in PainterlyPasses.hlsl, which leaves do not use)

        StructuredBuffer<LeafData> _Leaves;
        StructuredBuffer<float4> _TreeState;
        StructuredBuffer<float4> _TreeTraits;   // x leaf size, y evergreen, z warmth (temperature optimum, 0..1), w hue
        float4 _TreeGrowth;                      // x sim time, y seconds a leaf takes to unfurl
        float4x4 _PlanetLocalToWorld;
        uint _LeavesPerTree;

        struct LeafAttributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            uint instanceID : SV_InstanceID;
        };

        struct LeafVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 card : TEXCOORD2;          // -1..1 across the card
            nointerpolation float4 tint : TEXCOORD3;   // x phase, y autumn, z decay, w needle amount (evergreen)
            nointerpolation float4 trait : TEXCOORD4;  // x warmth, y hue, z evergreen
        };

        LeafVaryings LeafVertex(LeafAttributes input)
        {
            LeafVaryings output;
            LeafData leaf = _Leaves[input.instanceID];
            uint tree = input.instanceID / _LeavesPerTree;
            float4 state = _TreeState[tree];
            float4 traits = _TreeTraits[tree];

            // Leaves fall one by one as the tree's leaf amount drops past each leaf's own phase.
            float present = smoothstep(leaf.phase, leaf.phase + 0.15, state.x);

            // A new leaf unfurls over a few seconds once its branch has grown.
            float unfurl = smoothstep(0.0, 1.0, saturate((_TreeGrowth.x - leaf.birth) / max(_TreeGrowth.y, 0.01)));
            float size = leaf.size * present * unfurl * traits.x;

            float3 centre = mul(_PlanetLocalToWorld, float4(leaf.position, 1.0)).xyz;
            float3 cameraRight = UNITY_MATRIX_V[0].xyz;
            float3 cameraUp = UNITY_MATRIX_V[1].xyz;

            // Turn each card by its own angle so the leaves do not all point the same way.
            float angle = leaf.phase * 6.2831853;
            float s, c;
            sincos(angle, s, c);
            float2 corner = input.positionOS.xy * 2.0;
            float2 turned = float2(c * corner.x - s * corner.y, s * corner.x + c * corner.y);

            output.positionWS = centre + (cameraRight * turned.x + cameraUp * turned.y) * size * 0.5;
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = normalize(mul((float3x3)_PlanetLocalToWorld, leaf.normal));
            output.card = corner;
            output.tint = float4(leaf.phase, state.y, state.w, traits.y);
            output.trait = float4(traits.z, traits.w, traits.y, 0.0);
            return output;
        }

        // The leaf shape: an ellipse, narrower than the card is tall (much narrower for a needle-leaved evergreen).
        // Returns the "radius" (<1 inside).
        float LeafRadius(float2 card, float needle)
        {
            float width = lerp(0.62, 0.20, needle);
            return sqrt(card.x * card.x / (width * width) + card.y * card.y);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LeafVertex
            #pragma fragment LeafFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            half4 LeafFragment(LeafVaryings input) : SV_Target
            {
                float radius = LeafRadius(input.card, input.tint.w);
                clip(1.0 - radius);

                float phase = input.tint.x;
                float autumnTint = input.tint.y;
                float decay = input.tint.z;

                float shade = frac(phase * 7.31);
                float3 summer = lerp(float3(0.17, 0.42, 0.13), float3(0.40, 0.60, 0.20), shade);

                // The tree's genes colour its leaves: cold-adapted trees are cool blue-greens, warm-adapted are
                // lush yellow-greens, evergreens are darker, and a free hue gene gives each family its own tint.
                float warmth = input.trait.x;
                float evergreen = input.trait.z;
                summer *= lerp(float3(0.78, 0.95, 1.15), float3(1.15, 1.1, 0.7), warmth);
                summer *= lerp(1.0, 0.62, evergreen);
                summer += (input.trait.y - 0.5) * float3(0.16, 0.04, -0.10);
                summer = saturate(summer);
                float3 autumn = lerp(float3(0.88, 0.58, 0.12), float3(0.66, 0.20, 0.10), shade);
                float3 dead = float3(0.38, 0.30, 0.20);
                float3 albedo = lerp(lerp(summer, autumn, autumnTint), dead, decay);

                float3 color = PainterlyShadeSun(albedo, input.positionWS, normalize(input.normalWS), input.positionCS);

                // A dark ink rim, fading with the shared outline switch.
                float rim = smoothstep(0.55, 0.9, radius) * _PainterlyOutlinesEnabled;
                color = lerp(color, _PainterlyInkColor, rim * 0.85);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LeafVertex
            #pragma fragment DepthFragment

            half4 DepthFragment(LeafVaryings input) : SV_Target
            {
                clip(1.0 - LeafRadius(input.card, input.tint.w));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LeafVertex
            #pragma fragment DepthNormalsFragment

            half4 DepthNormalsFragment(LeafVaryings input) : SV_Target
            {
                clip(1.0 - LeafRadius(input.card, input.tint.w));
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
