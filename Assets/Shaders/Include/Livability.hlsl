// =============================================================================
// Livability.hlsl
// -----------------------------------------------------------------------------
// Purpose : One shared answer to "how good is this ground for people to live and
//           farm on?", used by the walkers (what they head for, how well they eat)
//           and by the settlement field (where farms can grow).
//
// Idea    : Four things have to be right at once, so the answer is their product.
//           Any one of them near zero (a frozen, bone-dry, mountain-top or barren
//           place) makes the ground useless, which is how real settlement works.
//
// Equations (T temperature in deg C, m moisture 0..1, h height 0..1, n nutrients):
//     warm = smoothstep(-2, 12, T) * (1 - smoothstep(32, 44, T))     frost and furnace both rule it out
//     wet  = smoothstep(0.08, 0.40, m)                               deserts are empty
//     low  = 1 - smoothstep(0.45, 0.80, h)                           nobody farms a summit
//     rich = 0.35 + 0.65 * saturate(2 n)                             poor soil is still livable, rich soil is better
//     livability = warm * wet * low * rich                           0..1
//
// Irrigation: once people have learnt it (a tech), ground near water counts as wetter than the weather
//   makes it, so farms and homes can spread into drier country. IrrigatedMoisture() applies it before
//   Livability() is asked:
//     reached = irrigationOn * smoothstep(near, near + 0.12, waterProximity)
//     m       = max(moisture, irrigationMoisture * reached)
//   waterProximity is the Water field: 1 at the water, falling to 0 at the field's reach. "near" is the
//   proximity at the edge of how far irrigation can carry water.
//
// Parameters: none for the shape of the answer; the constants above are not a tuning knob. How hungry
//   people are, and what a good place is worth, are in SimConfig. The irrigation values are set by the
//   shader's C# driver from the people's current technology.
// =============================================================================

#ifndef TERRARIUM_LIVABILITY_INCLUDED
#define TERRARIUM_LIVABILITY_INCLUDED

float _IrrigationOn;         // 0 or 1: has the tech been learnt
float _IrrigationMoisture;   // the moisture irrigated ground is treated as having
float _IrrigationNear;       // water proximity at the edge of irrigation's reach

float IrrigatedMoisture(float moisture, float waterProximity)
{
    float reached = _IrrigationOn * smoothstep(_IrrigationNear, max(_IrrigationNear + 0.12, _IrrigationNear + 1e-3), waterProximity);
    return max(moisture, _IrrigationMoisture * reached);
}

float Livability(float temperature, float moisture, float height, float nutrients)
{
    float warm = smoothstep(-2.0, 12.0, temperature) * (1.0 - smoothstep(32.0, 44.0, temperature));
    float wet = smoothstep(0.08, 0.40, moisture);
    float low = 1.0 - smoothstep(0.45, 0.80, height);
    float rich = 0.35 + 0.65 * saturate(2.0 * nutrients);
    return warm * wet * low * rich;
}

#endif
