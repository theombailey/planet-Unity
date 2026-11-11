// =============================================================================
// Clouds.hlsl
// -----------------------------------------------------------------------------
// Purpose : The one cloud function, shared by the cloud layer (Clouds.shader)
//           and by the planet's ground shader (for cloud shadows), so a cloud
//           and its shadow always agree.
//
// Idea    : Cloud cover is a field over the sphere, found by DIRECTION from the
//           planet centre (like every other field in the game):
//             1. SHAPE   fractal noise (big billows plus a finer layer) sampled
//                        at the direction after it has been turned about the
//                        planet's axis by an angle that grows with sim time.
//                        That turning is the wind carrying the clouds round.
//             2. WEATHER the ground decides where clouds may form. Cloud forms
//                        where the shape value is above a THRESHOLD, and the
//                        threshold is LOWER over wet ground, so clouds gather
//                        over seas and rainy land and thin out over desert.
//                        (Nothing paints clouds; they are the noise cut by the
//                        moisture field, so they move when the climate moves.)
//             3. EDGE    a smooth step above the threshold, so a cloud has a
//                        soft rim instead of a hard cut.
//
// Equations (d = direction, m = moisture 0..1, c = coverage, w = moisture influence):
//     angle      = driftRadiansPerSecond * simTime
//     p          = rotateAboutY(d, angle)
//     field      = fbm(p * scale) + 0.30 * (fbm(p * scale * 4) - 0.5)
//     threshold  = 0.70 - 0.40 c - 0.40 w (m - 0.5)
//     density    = smoothstep(threshold, threshold + 0.12, field)
//   Shading (CloudShade): 3 flat bands of L * 0.85 + 0.15 + bias - 0.18 * thickness, warm at sunset, dark at night
//   Shadow: the cloud that shades a ground point P from a lamp in direction L is where the ray
//     P + L t crosses the cloud shell of radius Rc (centre at the origin):
//     t = -b + sqrt(b^2 - (|P|^2 - Rc^2)),  b = dot(P, L)
//
// Parameters (global, from SimConfig via AtmosphereController):
//     _CloudTime    sim seconds
//     _CloudParams  x = scale, y = drift (rad per sim second), z = coverage, w = moisture influence
//     _CloudLayer   x = shell radius (planet-local units, 1 = surface), y = opacity,
//                   z = shadow strength, w = 1 when clouds are switched on
//     _CloudShellFade  1 in desk view, 0 on the surface (the shell fades out; the sky draws its own clouds)
// =============================================================================

#ifndef TERRARIUM_CLOUDS_INCLUDED
#define TERRARIUM_CLOUDS_INCLUDED

#include "Assets/Shaders/Include/Noise.hlsl"
#include "Assets/Shaders/Include/AtmosphereGlobals.hlsl"

float _CloudTime;
float4 _CloudParams;
float4 _CloudLayer;
float _CloudShellFade;

// Cloud density in [0,1] at a planet-local direction over ground with the given moisture.
// `scaleMultiplier` makes the pattern finer (the sky uses it so its clouds are smaller than the shell's).
// `thickness` returns how far into the cloud this point is (0 at the rim, 1 deep inside).
float CloudDensity(float3 direction, float moisture, float scaleMultiplier, out float thickness)
{
    float angle = _CloudTime * _CloudParams.y;
    float s, c;
    sincos(angle, s, c);
    float3 p = float3(c * direction.x + s * direction.z, direction.y, -s * direction.x + c * direction.z);

    float scale = _CloudParams.x * scaleMultiplier;
    float shape = Fbm(p * scale + float3(3.1, 0.0, 7.7), 5);
    float detail = Fbm(p * (scale * 4.0) + float3(11.0, 5.0, 2.0), 3);
    float field = shape + (detail - 0.5) * 0.30;

    float threshold = 0.70 - 0.40 * _CloudParams.z - 0.40 * _CloudParams.w * (moisture - 0.5);
    thickness = saturate((field - threshold) / 0.30);
    return smoothstep(threshold, threshold + 0.12, field);
}

// Three flat tones with soft steps between them (the painterly banding, for clouds).
float CloudBand3(float x)
{
    float scaled = saturate(x) * 3.0;
    float whole = floor(scaled);
    return (min(whole, 2.0) + smoothstep(0.42, 0.58, frac(scaled))) / 3.0;
}

// Colour of a cloud. L = how directly the lamp lights it (dot of the cloud's outward normal and the
// direction to the lamp), thickness = how deep inside the cloud (from CloudDensity).
// lightBias brightens the tones without moving the sunset or night thresholds: high clouds in the sky
// catch light that the ground has already lost, so the sky passes a positive bias.
float3 CloudShade(float L, float thickness, float lightBias)
{
    float lightness = CloudBand3(L * 0.85 + 0.15 + lightBias - 0.18 * thickness);
    float3 shadowTone = float3(0.52, 0.58, 0.76);
    float3 litTone = float3(1.00, 0.98, 0.94);
    float3 colour = lerp(shadowTone, litTone, lightness);

    // Warm at sunset, dark blue at night.
    float sunsetOffset = (L - 0.05) / 0.18;
    float sunset = exp(-sunsetOffset * sunsetOffset) * 0.7;
    colour = lerp(colour, float3(1.0, 0.52, 0.30) * (0.55 + 0.6 * lightness), sunset);
    float day = smoothstep(-0.20, 0.15, L);
    return lerp(float3(0.045, 0.06, 0.12), colour, day);
}

// World position of the cloud layer straight "up the lamp ray" from a ground point.
float3 CloudAboveAlongLamp(float3 groundWS, float3 centreWS, float3 towardLamp, float cloudRadiusWS)
{
    float3 P = groundWS - centreWS;
    float b = dot(P, towardLamp);
    float t = -b + sqrt(max(b * b - (dot(P, P) - cloudRadiusWS * cloudRadiusWS), 0.0));
    return centreWS + P + towardLamp * t;
}

#endif
