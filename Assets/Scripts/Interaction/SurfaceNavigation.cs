// =============================================================================
// SurfaceNavigation
// -----------------------------------------------------------------------------
// Purpose : The maths of moving and looking around on the planet's surface in
//           surface mode: where the camera stands, which way it faces, how a
//           pan moves it, and the camera pose that results.
// Idea    : The camera's place is described in the PLANET'S OWN frame (the frame
//           the fields live in) by two unit vectors:
//             position  - the point on the sphere it stands over (a direction)
//             heading   - the way it faces along the ground (a direction at
//                         right angles to `position`)
//           plus a height above the ground and a pitch. Because the pose is
//           built from the planet's rotation each frame, the ground stays still
//           under the camera while the planet spins and the lamp sweeps the sky.
//           Holding an explicit heading (instead of latitude/longitude) means
//           there is no pole where the controls break down.
//
// Equations:
//   Moving by arc a toward unit direction u (a tangent vector, u = forward*fwd + right*rgt):
//     position' = position cos a + u sin a                      (a great-circle step)
//     q         = FromToRotation(position, position')           (the shortest rotation taking
//                                                                one to the other)
//     heading'  = q * heading                                   (PARALLEL TRANSPORT: the heading
//                                                                is carried along without twisting)
//   Right-hand direction while standing at (position, heading): right = position x heading.
//   Turning to look around:   heading' = heading cos t + (position x heading) sin t
//   Camera pose (R = planet rotation, c = planet centre, r = planet radius, p = pitch downward):
//     up      = R position
//     forward = R heading cos p - up sin p
//     camera  = c + up (r + height),    rotation = LookRotation(forward, up)
// =============================================================================

using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>Pure maths for walking and looking on the planet's surface.</summary>
    public static class SurfaceNavigation
    {
        /// <summary>
        /// Moves the standpoint across the sphere and carries the heading with it.
        /// </summary>
        /// <param name="position">Unit vector, the point stood over (planet frame). Updated.</param>
        /// <param name="heading">Unit tangent vector, the way faced (planet frame). Updated.</param>
        /// <param name="forwardArc">Distance to move forward, in radians of arc (negative = backward).</param>
        /// <param name="rightArc">Distance to move to the right, in radians of arc.</param>
        public static void Move(ref Vector3 position, ref Vector3 heading, float forwardArc, float rightArc)
        {
            Vector3 right = Vector3.Cross(position, heading);
            Vector3 step = heading * forwardArc + right * rightArc;
            float arc = step.magnitude;
            if (arc < 1e-9f)
            {
                return;
            }

            Vector3 direction = step / arc;
            Vector3 newPosition = (position * Mathf.Cos(arc) + direction * Mathf.Sin(arc)).normalized;
            Quaternion carry = Quaternion.FromToRotation(position, newPosition);

            position = newPosition;
            heading = Orthogonalise(carry * heading, position);
        }

        /// <summary>Turns the heading about the standpoint by <paramref name="radians"/> (positive = to the right).</summary>
        public static void Turn(Vector3 position, ref Vector3 heading, float radians)
        {
            Vector3 right = Vector3.Cross(position, heading);
            heading = Orthogonalise(heading * Mathf.Cos(radians) + right * Mathf.Sin(radians), position);
        }

        /// <summary>
        /// A valid heading at a position, as close to <paramref name="preferred"/> as possible.
        /// If the preferred direction is (nearly) straight up or down, an arbitrary one is chosen.
        /// </summary>
        public static Vector3 HeadingNear(Vector3 position, Vector3 preferred)
        {
            Vector3 tangent = preferred - position * Vector3.Dot(preferred, position);
            if (tangent.sqrMagnitude < 1e-8f)
            {
                tangent = Mathf.Abs(position.y) < 0.99f ? Vector3.Cross(position, Vector3.up) : Vector3.Cross(position, Vector3.right);
            }

            return tangent.normalized;
        }

        /// <summary>
        /// The camera's world pose for a standpoint on the planet.
        /// </summary>
        /// <param name="planetRotation">The planet's current rotation (planet frame to world).</param>
        /// <param name="planetCentre">World position of the planet's centre.</param>
        /// <param name="planetRadius">Planet radius in world units.</param>
        /// <param name="position">Standpoint (planet frame, unit vector).</param>
        /// <param name="heading">Heading (planet frame, unit tangent).</param>
        /// <param name="height">Height above the radius, in world units.</param>
        /// <param name="pitchRadians">How far the camera looks down from the horizon.</param>
        public static void CameraPose(Quaternion planetRotation, Vector3 planetCentre, float planetRadius, Vector3 position, Vector3 heading,
            float height, float pitchRadians, out Vector3 cameraPosition, out Quaternion cameraRotation)
        {
            Vector3 up = planetRotation * position;
            Vector3 forward = (planetRotation * heading) * Mathf.Cos(pitchRadians) - up * Mathf.Sin(pitchRadians);

            cameraPosition = planetCentre + up * (planetRadius + height);
            cameraRotation = Quaternion.LookRotation(forward, up);
        }

        private static Vector3 Orthogonalise(Vector3 vector, Vector3 position)
        {
            return (vector - position * Vector3.Dot(vector, position)).normalized;
        }
    }
}
