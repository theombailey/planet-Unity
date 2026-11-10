// =============================================================================
// SpringFloat
// -----------------------------------------------------------------------------
// Purpose : Smoothly moves a number toward a target with no overshoot and no
//           pops. Used for camera yaw / pitch / distance and mode blends.
//
// Idea    : A critically damped spring: the fastest spring that never
//           overshoots. Its motion obeys
//               x'' = -2w x' - w^2 (x - target)
//           where w (omega, rad/s) sets how stiff it is. This equation has an
//           exact solution, so instead of approximating with small Euler steps
//           we jump straight to the right answer for any dt. That makes the
//           result independent of frame rate and stable even after a hitch.
//
// Equations (d = x - target, v = velocity, e = exp(-w dt)):
//               x(dt) = target + (d + (v + w d) dt) e
//               v(dt) = (v - w (v + w d) dt) e
//
// Parameters: omega - stiffness in rad/s. The value covers ~95% of the
//               distance to the target in about 4.7 / omega seconds.
//
// Note    : This is a mutable struct. Keep it in a field and call Step on that
//           field directly; a copy would be stepped instead of the original.
// =============================================================================

using UnityEngine;

namespace Terrarium
{
    /// <summary>A critically damped spring on a 3D point, integrated exactly (three independent SpringFloats).</summary>
    public struct SpringVector3
    {
        /// <summary>Current smoothed value.</summary>
        public Vector3 Value;

        /// <summary>Current rate of change of <see cref="Value"/>, per second.</summary>
        public Vector3 Velocity;

        /// <summary>Creates a spring at rest at the given point.</summary>
        public SpringVector3(Vector3 value)
        {
            Value = value;
            Velocity = Vector3.zero;
        }

        /// <summary>Advances the spring by <paramref name="deltaSeconds"/> toward <paramref name="target"/>.</summary>
        public void Step(Vector3 target, float omega, float deltaSeconds)
        {
            float decay = Mathf.Exp(-omega * deltaSeconds);
            Vector3 offset = Value - target;
            Vector3 blend = (Velocity + omega * offset) * deltaSeconds;

            Velocity = (Velocity - omega * blend) * decay;
            Value = target + (offset + blend) * decay;
        }
    }

    /// <summary>A critically damped spring on one float, integrated exactly.</summary>
    public struct SpringFloat
    {
        /// <summary>Current smoothed value.</summary>
        public float Value;

        /// <summary>Current rate of change of <see cref="Value"/>, per second.</summary>
        public float Velocity;

        /// <summary>Creates a spring at rest at the given value.</summary>
        public SpringFloat(float value)
        {
            Value = value;
            Velocity = 0f;
        }

        /// <summary>Advances the spring by <paramref name="deltaSeconds"/> toward <paramref name="target"/>.</summary>
        public void Step(float target, float omega, float deltaSeconds)
        {
            float decay = Mathf.Exp(-omega * deltaSeconds);
            float offset = Value - target;
            float blend = (Velocity + omega * offset) * deltaSeconds;

            Velocity = (Velocity - omega * blend) * decay;
            Value = target + (offset + blend) * decay;
        }
    }
}
