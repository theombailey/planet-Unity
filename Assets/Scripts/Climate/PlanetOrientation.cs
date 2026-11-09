// =============================================================================
// PlanetOrientation
// -----------------------------------------------------------------------------
// Purpose : The planet's rotation as a pure function of the clock. This one
//           rotation is what you see on screen AND (from Phase 4) what the
//           climate shaders use to work out which side faces the lamp, so the
//           picture and the simulation can never disagree.
//
// Idea    : Two rotations, applied in this order to a point on the planet:
//             1. SPIN  about the planet's own axis (local +Y) - gives day/night.
//             2. LEAN  tips that axis toward or away from the lamp - gives the
//                seasons, because it changes which hemisphere gets more light.
//
// Equations:
//     L        = horizontal unit direction from the planet to the lamp
//     tiltAxis = up x L             (horizontal, at right angles to L)
//     R        = Rot(tiltAxis, lean) * Rot(up, spin)
//
//     Rotating "up" about (up x L) by a positive angle moves it toward L, so
//     lean > 0 tips the north pole toward the lamp (northern summer).
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
        /// <param name="leanRadians">Tip of the north pole toward the lamp (negative = away).</param>
        /// <param name="planetToLamp">World-space vector from the planet centre to the lamp. Need not be normalised.</param>
        public static Quaternion Compute(float spinRadians, float leanRadians, Vector3 planetToLamp)
        {
            Vector3 towardLamp = Vector3.ProjectOnPlane(planetToLamp, Vector3.up);
            if (towardLamp.sqrMagnitude < 1e-8f)
            {
                // Lamp directly above or below: "toward the lamp" has no horizontal direction, so pick one.
                towardLamp = Vector3.right;
            }

            Vector3 tiltAxis = Vector3.Cross(Vector3.up, towardLamp.normalized);
            Quaternion lean = Quaternion.AngleAxis(leanRadians * Mathf.Rad2Deg, tiltAxis);
            Quaternion spin = Quaternion.AngleAxis(spinRadians * Mathf.Rad2Deg, Vector3.up);
            return lean * spin;
        }
    }
}
