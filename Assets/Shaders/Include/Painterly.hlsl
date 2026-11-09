// =============================================================================
// Painterly.hlsl
// -----------------------------------------------------------------------------
// Purpose : The shared "hand-drawn" shading used by every surface shader, so
//           the whole scene is lit and inked by one set of rules.
//
// Idea    : Real lighting is smooth. A painter instead fills areas with a few
//           flat tones, and shades dark areas with pen strokes. So we:
//             1. measure how much lamp light reaches a point (a smooth "tone"),
//             2. snap that tone to a few BANDS, with noisy edges so the band
//                borders look brushed rather than computed,
//             3. draw pen HATCHING over the darker bands, and cross-hatching
//                over the darkest,
//             4. make the noise jump a few times a second ("BOIL"), like
//                drawings re-traced frame by frame.
//
// Equations:
//   Tone      tone = saturate( exposure * SUM_lights( I * atten * shadow * max(0, N.L) ) )
//             I = light brightness, atten = distance falloff (~1/d^2).
//
//   Bands     t     = tone * bandCount + wobble          wobble = (noise - 0.5) * amount
//             banded = ( floor(t) + smoothstep(0.5 - s, 0.5 + s, frac(t)) ) / bandCount
//             floor() makes the flat steps; the smoothstep blends each step over
//             a width s so the edge is soft; the noise bends where the edge falls.
//
//   Hatching  Rotate the pixel position by the hatch angle to get (along, across).
//             lineCoord = across / spacing + sideways wobble
//             d         = |frac(lineCoord) - 0.5| * 2      0 on a line, 1 between lines
//             ink       = 1 - smoothstep(width - a, width + a, d)
//             Line width grows with darkness; noise along the line varies the
//             "pen pressure" and lifts the pen off the paper now and then.
//
//   Boil      seed = floor(time * rateHz)   added to every noise lookup.
//
// Parameters: all are global shader values set each frame by PainterlySettings.cs
//             from SimConfig (group "Rendering: painterly look").
//
// Requires  : URP Core.hlsl and Lighting.hlsl included first, plus Noise.hlsl.
// =============================================================================

#ifndef TERRARIUM_PAINTERLY_INCLUDED
#define TERRARIUM_PAINTERLY_INCLUDED

#include "Assets/Shaders/Include/Noise.hlsl"

// ---- Globals set by PainterlySettings.cs --------------------------------------
float3 _PainterlyAmbient;        // dim fill light colour (keeps the night side readable)
float3 _PainterlyInkColor;       // colour of outlines and hatch strokes
float _PainterlyExposure;        // scales raw light into the 0..1 tone range
float _PainterlyBandCount;       // number of flat tone steps
float _PainterlyBandSoftness;    // half-width of the blend between steps (0..0.5 of a band)
float _PainterlyBandWobble;      // how far noise pushes a band edge, in bands
float _PainterlyWobbleFrequency; // band-edge noise blobs per world unit
float _PainterlyHatchSpacing;    // distance between hatch lines, in pixels at 720p
float _PainterlyHatchAngle;      // hatch direction, radians
float _PainterlyHatchStrength;   // how dark a hatch stroke is (0 = invisible, 1 = solid ink)
float _PainterlyBoilSeed;        // floor(time * rate); 0 when boil is off
float _PainterlyBandsEnabled;    // 1 = banded tone, 0 = smooth tone (debug toggle)
float _PainterlyHatchEnabled;    // 1 = draw hatching (debug toggle)

// What the lights contribute at one surface point.
struct PainterlyLight
{
    float tone;        // smooth brightness, 0..1
    float3 hue;        // colour of the light reaching the point (brightest channel = 1)
    float3 direction;  // direction toward the strongest light (world space)
};

// Adds up every light that reaches the point.
// URP note: in the Forward+ path lights are found per screen tile, and URP's LIGHT_LOOP_BEGIN
// macro expects a variable literally named `inputData` carrying the position and screen UV.
PainterlyLight GatherLight(float3 positionWS, float3 normalWS, float2 screenUV)
{
    PainterlyLight result;
    result.tone = 0.0;
    result.hue = float3(0.0, 0.0, 0.0);
    result.direction = float3(0.0, 1.0, 0.0);
    float strongest = 0.0;

#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;

    uint lightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(lightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));

        float brightness = max(light.color.r, max(light.color.g, light.color.b));
        float received = brightness * light.distanceAttenuation * light.shadowAttenuation
                       * saturate(dot(normalWS, light.direction));

        result.tone += received;
        result.hue += light.color / max(brightness, 1e-4) * received;

        // Direction ignores shadow and facing, so a highlight still knows where the lamp is.
        float reach = brightness * light.distanceAttenuation;
        if (reach > strongest)
        {
            strongest = reach;
            result.direction = light.direction;
        }
    LIGHT_LOOP_END
#endif

    // max() keeps the division safe even though the ternary below discards that case.
    result.hue = result.tone > 1e-5 ? result.hue / max(result.tone, 1e-5) : float3(1.0, 1.0, 1.0);
    result.tone = saturate(result.tone * _PainterlyExposure);
    return result;
}

// Snaps a smooth tone to flat bands with noisy, soft edges. See "Bands" in the header.
float BandTone(float tone, float3 positionWS)
{
    float noise = ValueNoise(positionWS * _PainterlyWobbleFrequency + _PainterlyBoilSeed * 0.37);

    // The wobble fades out at the very bottom so fully unlit areas stay in the darkest band.
    float wobble = (noise - 0.5) * _PainterlyBandWobble * saturate(tone * _PainterlyBandCount * 2.0);

    float t = tone * _PainterlyBandCount + wobble;
    float whole = floor(t);
    float blend = smoothstep(0.5 - _PainterlyBandSoftness, 0.5 + _PainterlyBandSoftness, t - whole);
    float banded = saturate((whole + blend) / _PainterlyBandCount);

    return lerp(tone, banded, _PainterlyBandsEnabled);
}

// One set of parallel pen strokes. Returns ink amount 0..1.
//   pixel    - position on screen, in pixels at 720p
//   angle    - stroke direction, radians
//   coverage - how much of the gap between lines is inked (0 = none, 1 = solid)
float HatchLayer(float2 pixel, float angle, float coverage)
{
    float s, c;
    sincos(angle, s, c);
    float along = c * pixel.x + s * pixel.y;
    float across = -s * pixel.x + c * pixel.y;

    // Low-frequency noise bends each line slightly so it is not ruler-straight.
    float bend = ValueNoise(float3(along * 0.012, across * 0.012, _PainterlyBoilSeed * 0.61)) - 0.5;
    float lineCoord = across / _PainterlyHatchSpacing + bend * 0.9;
    float lineIndex = floor(lineCoord);
    float distanceFromLine = abs(lineCoord - lineIndex - 0.5) * 2.0;

    // "Pen pressure": changes along each stroke and differs from stroke to stroke.
    float pressure = ValueNoise(float3(along * 0.035, lineIndex * 1.71, _PainterlyBoilSeed * 1.13));
    float width = coverage * lerp(0.55, 1.25, pressure);
    float penDown = smoothstep(0.16, 0.26, pressure);   // the pen lifts where pressure is lowest

    return (1.0 - smoothstep(width - 0.12, width + 0.12, distanceFromLine)) * penDown;
}

// Pen hatching for a given banded tone: one layer in mid-shadow, a crossing layer in deep shadow.
float HatchInk(float bandedTone, float2 pixel)
{
    float darkness = 1.0 - bandedTone;

    // First layer appears once the surface is darker than ~45% and thickens as it darkens.
    float firstCoverage = saturate((darkness - 0.45) / 0.55) * 0.42;
    // Second (crossing) layer appears only in the very darkest band, and stays thin so it
    // reads as a few crossing strokes rather than a woven grid.
    float crossCoverage = saturate((darkness - 0.88) / 0.12) * 0.26;

    float ink = HatchLayer(pixel, _PainterlyHatchAngle, firstCoverage);
    ink = max(ink, HatchLayer(pixel + 37.0, _PainterlyHatchAngle + 1.05, crossCoverage));
    return ink * _PainterlyHatchEnabled;
}

// Screen position in "pixels at 720p", so stroke spacing looks the same at any resolution.
float2 PainterlyPixel(float4 positionCS)
{
    return positionCS.xy * (720.0 / _ScreenParams.y);
}

// Full painterly shading of one surface point.
//   albedo     - the surface's own colour
//   positionCS - SV_POSITION of the fragment (pixel coordinates)
float3 PainterlyShade(float3 albedo, float3 positionWS, float3 normalWS, float4 positionCS)
{
    PainterlyLight light = GatherLight(positionWS, normalWS, GetNormalizedScreenSpaceUV(positionCS));
    float banded = BandTone(light.tone, positionWS);

    float3 color = albedo * (_PainterlyAmbient + light.hue * banded);

    float ink = HatchInk(banded, PainterlyPixel(positionCS));
    return lerp(color, color * _PainterlyInkColor, ink * _PainterlyHatchStrength);
}

#endif
