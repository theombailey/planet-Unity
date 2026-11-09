// =============================================================================
// PlanetMotion
// -----------------------------------------------------------------------------
// Purpose : Moves the planet object each frame: spin and seasonal lean from the
//           SimClock, plus a gentle decorative bob above the desk.
// Idea    : Rotation comes entirely from PlanetOrientation (see that file for
//           the maths), driven by sim time, so pausing the sim stops the spin
//           and 16x makes days fly past.
//           The bob is purely cosmetic and is the one thing here driven by
//           REAL time: it shows the planet is levitating, and it should stay
//           calm whatever the time dial says.
// Equations:
//     rotation = PlanetOrientation.Compute(clock.spin, clock.lean, lamp - centre)
//     centre.y = restHeight + bobAmplitude * sin(2*pi * realTime / bobPeriod)
// Parameters (SimConfig): planetRadius, planetHoverHeight, bobAmplitude,
//     bobPeriodSeconds. Day length, year length and tilt act through SimClock.
// =============================================================================

using UnityEngine;

namespace Terrarium.Climate
{
    /// <summary>Applies spin, seasonal lean and the levitation bob to the planet transform.</summary>
    public sealed class PlanetMotion : MonoBehaviour
    {
        [Tooltip("The simulation whose clock drives the spin and the seasons.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The lamp's light. The planet's axis leans toward and away from it over a year.")]
        [SerializeField] private Transform lamp;

        private void LateUpdate()
        {
            if (simRoot == null || lamp == null || simRoot.Clock == null)
            {
                return;
            }

            SimConfig config = simRoot.Config;
            SimClock clock = simRoot.Clock;

            Vector3 restCentre = config.PlanetRestCentre;
            float bob = config.bobAmplitude * Mathf.Sin(2f * Mathf.PI * Time.unscaledTime / config.bobPeriodSeconds);

            transform.SetPositionAndRotation(
                restCentre + Vector3.up * bob,
                PlanetOrientation.Compute(clock.SpinAngleRadians, clock.LeanRadians, lamp.position - restCentre));
        }
    }
}
