// =============================================================================
// AtmosphereGlobals.hlsl
// -----------------------------------------------------------------------------
// Purpose : The few GLOBAL shader values that the sky, the atmosphere glow, the
//           clouds and the planet all need to agree on. Declared once here so
//           no shader re-declares them differently.
//
// Idea    : AtmosphereController (C#) writes these every frame with
//           Shader.SetGlobal*, so every shader that includes this file sees the
//           same lamp and planet positions.
//
// Values  : _AtmoCentre             xyz = planet centre (world), w = planet radius (world units)
//           _TerrariumLampPosition  world position of the lamp's bulb
// =============================================================================

#ifndef TERRARIUM_ATMOSPHERE_GLOBALS_INCLUDED
#define TERRARIUM_ATMOSPHERE_GLOBALS_INCLUDED

float4 _AtmoCentre;
float4 _TerrariumLampPosition;

#endif
