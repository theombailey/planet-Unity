// =============================================================================
// PlanetOrientation
// -----------------------------------------------------------------------------
// Purpose : The planet's rotation as a pure function of the clock. This one
//           rotation is what you see on screen AND (from Phase 4) what the
//           climate shaders use to work out which side faces the lamp, so the
//           picture and the simulation can never disagree.
//
// Idea    : Three things decide where the planet's axis points:
//             1. REST AXIS - with no lean, the axis is at right angles to the
//                direction of the lamp, as upright as that allows. So at the
//                equinox the lamp is exactly over the equator, wherever the
//                lamp happens to stand on the desk. (If the rest axis were
//                simply "up", a lamp above the planet would give the north
//                permanent summer and the hemispheres would never swap.)
//             2. LEAN - tips the axis toward (+) or away from (-) the lamp.
//                This is the seasons: lean = latitude the lamp is directly over.
//             3. SPIN - turns the planet about that axis: day and night.
//
// Equations (L = unit direction from planet to lamp):
//     rest     = normalize( up - L (up . L) )     the part of "up" at right angles to L
//     tiltAxis = rest x L
//     axis     = Rot(tiltAxis, lean) * rest        (moves the axis toward L by `lean`)
//     R        = Rot(tiltAxis, lean) * FromTo(up, rest) * Rot(up, spin)
//
//     Check: rest . L = 0, and after the lean  axis . L = sin(lean).
//
// Parameters: spin (radians) = 2*pi * dayPhase            from SimClock
//             lean (radians) = epsilon * sin(2*pi * yearPhase)  from SimClock
// =============================================================================

using UnityEngine;

namespace Terrarium.Climate
{
    /// <summary>Computes the planet's rotation from spin angle, axis lean and lamp direction.</summary>
    public static class PlanetOrientation
    {
        /// <summary>
        /// Returns the rotation taking planet-local directions to world directions.
        /// </summary>
        /// <param name="spinRadians">Rotation about the planet's own axis.</param>
        /// <param name="leanRadians">Tip of the north pole toward the lamp (negative = away). Equals the latitude the lamp is over.</param>
        /// <param name="planetToLamp">World-space vector from the planet centre to the lamp. Need not be normalised.</param>
        public static Quaternion Compute(float spinRadians, float leanRadians, Vector3 planetToLamp)
        {
            Vector3 towardLamp = planetToLamp.sqrMagnitude > 1e-10f ? planetToLamp.normalized : Vector3.right;

            Vector3 rest = Vector3.up - towardLamp * Vector3.Dot(Vector3.up, towardLamp);
            if (rest.sqrMagnitude < 1e-8f)
            {
                // Lamp directly above or below: every horizontal axis is equally "upright", so pick one.
                rest = Vector3.forward;
            }

            rest.Normalize();
            Vector3 tiltAxis = Vector3.Cross(rest, towardLamp);

            Quaternion lean = Quaternion.AngleAxis(leanRadians * Mathf.Rad2Deg, tiltAxis);
            Quaternion toRest = Quaternion.FromToRotation(Vector3.up, rest);
            Quaternion spin = Quaternion.AngleAxis(spinRadians * Mathf.Rad2Deg, Vector3.up);
            return lean * toRest * spin;
        }
    }
}
