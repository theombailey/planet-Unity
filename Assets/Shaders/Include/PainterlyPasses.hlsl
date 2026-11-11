// =============================================================================
// PainterlyPasses.hlsl
// -----------------------------------------------------------------------------
// Purpose : The supporting passes every solid painterly surface needs, written
//           once and shared by Props.shader and Desk.shader:
//             ShadowCaster  - draws the object into the lamp's shadow map
//             DepthOnly     - draws depth for the camera depth texture
//             DepthNormals  - draws depth + world normal for the camera normals
//                             texture (the fullscreen ink pass finds edges in it)
//             Outline       - the "inverted hull" ink outline
//
// Idea (inverted hull outline):
//           Draw the object a second time, slightly fatter, in flat ink colour,
//           showing only its BACK faces. The fat back faces stick out round the
//           silhouette of the normal object and read as an outline.
//
// Equations (Outline vertex):
//     n_clip   = normalize( (VP * normalWS).xy )         normal direction on screen
//     offset   = n_clip * widthPixels * (2 / screenSize) * w
//     Multiplying by w cancels the perspective divide, so the outline is the
//     same number of pixels wide however far away the object is.
//     widthPixels is multiplied by noise so the line thickens and thins.
//
// Parameters: _PainterlyHullWidth (pixels at 720p), _PainterlyOutlineWobble (0..1),
//             _PainterlyOutlinesEnabled (debug toggle) - globals from PainterlySettings.cs.
//             _OutlineScale - per-material multiplier (0 disables the outline).
//
// Requires  : URP Core.hlsl, Shadows.hlsl and Noise.hlsl included first, and the
//             including shader must declare _OutlineScale in its UnityPerMaterial block.
//
// Buffers & textures: none read. ShadowCaster uses URP's _LightDirection / _LightPosition.
// =============================================================================

#ifndef TERRARIUM_PAINTERLY_PASSES_INCLUDED
#define TERRARIUM_PAINTERLY_PASSES_INCLUDED

float _PainterlyHullWidth;
float _PainterlyOutlineWobble;
float _PainterlyOutlinesEnabled;

// Set by URP while it renders a shadow map.
float3 _LightDirection;
float3 _LightPosition;

// Growth animation (only for shaders that define TERRARIUM_GROWTH before including this file, i.e. the tree wood).
// The mesh carries, per vertex: uv0 = (birth time, grow seconds, 1 if this vertex grows), uv1 = where its segment starts.
// The ring at the child end of a branch segment slides out from the segment's start (and fattens) over the grow
// time, so branches visibly grow; everything else stays put.
#ifdef TERRARIUM_GROWTH
float4 _TreeGrowth;   // x = sim time (set by TreeManager)
#endif

struct PassAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
#ifdef TERRARIUM_GROWTH
    float3 growthA : TEXCOORD0;
    float3 growthB : TEXCOORD1;
#endif
};

// Object-space position of a vertex, with the growth animation applied where there is one.
float3 GrownPosition(PassAttributes input)
{
#ifdef TERRARIUM_GROWTH
    float grown = input.growthA.y <= 0.0 ? 1.0 : saturate((_TreeGrowth.x - input.growthA.x) / input.growthA.y);
    grown = grown * grown * (3.0 - 2.0 * grown);
    float3 start = input.growthB;
    float3 slid = start + grown * (input.positionOS.xyz - start);
    return lerp(input.positionOS.xyz, slid, input.growthA.z);
#else
    return input.positionOS.xyz;
#endif
}

struct PassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
};

// ---- ShadowCaster ---------------------------------------------------------------

PassVaryings ShadowVertex(PassAttributes input)
{
    PassVaryings output;
    float3 positionWS = TransformObjectToWorld(GrownPosition(input));
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

    // A point or spot light shines in a different direction at every vertex; a sun does not.
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    // The bias nudges the surface away from the light so it does not shadow itself.
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
    output.normalWS = normalWS;
    return output;
}

half4 ShadowFragment(PassVaryings input) : SV_Target
{
    return 0;
}

// ---- DepthOnly / DepthNormals ---------------------------------------------------

PassVaryings DepthVertex(PassAttributes input)
{
    PassVaryings output;
    output.positionCS = TransformObjectToHClip(GrownPosition(input));
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

half4 DepthOnlyFragment(PassVaryings input) : SV_Target
{
    return 0;
}

half4 DepthNormalsFragment(PassVaryings input) : SV_Target
{
    return half4(normalize(input.normalWS), 0.0);
}

// ---- Outline (inverted hull) ----------------------------------------------------

PassVaryings OutlineVertex(PassAttributes input)
{
    PassVaryings output;
    float3 positionWS = TransformObjectToWorld(GrownPosition(input));
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    float4 positionCS = TransformWorldToHClip(positionWS);

    float2 normalOnScreen = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
    normalOnScreen = normalOnScreen / max(length(normalOnScreen), 1e-5);

    // Vary the width with noise fixed to the surface, re-rolled by the boil seed.
    float noise = ValueNoise(positionWS * 9.0 + _PainterlyBoilSeed * 0.83);
    float widthPixels = _PainterlyHullWidth * _OutlineScale * _PainterlyOutlinesEnabled
                      * lerp(1.0, 0.35 + 1.3 * noise, _PainterlyOutlineWobble);

    // Convert "pixels at 720p" to clip units: the screen is 2 clip units tall.
    float2 pixelToClip = float2(_ScreenParams.y / _ScreenParams.x, 1.0) * (2.0 / 720.0);
    positionCS.xy += normalOnScreen * widthPixels * pixelToClip * positionCS.w;

    output.positionCS = positionCS;
    output.normalWS = normalWS;
    return output;
}

half4 OutlineFragment(PassVaryings input) : SV_Target
{
    return half4(_PainterlyInkColor, 1.0);
}

#endif
