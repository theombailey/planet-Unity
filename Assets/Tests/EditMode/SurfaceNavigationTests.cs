// =============================================================================
// SurfaceNavigationTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the surface-mode camera maths: moving keeps the standpoint on
//           the sphere with a tangent heading, forward then back returns home, a
//           full lap of a great circle returns to the start, nothing breaks at
//           the poles, and the camera pose puts the camera above the standpoint
//           looking along the heading.
// =============================================================================

using NUnit.Framework;
using Terrarium.Interaction;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class SurfaceNavigationTests
    {
        private static void AssertValid(Vector3 position, Vector3 heading, string message = "")
        {
            Assert.AreEqual(1f, position.magnitude, 1e-4f, "position length " + message);
            Assert.AreEqual(1f, heading.magnitude, 1e-4f, "heading length " + message);
            Assert.AreEqual(0f, Vector3.Dot(position, heading), 1e-4f, "heading not tangent " + message);
        }

        [Test]
        public void SurfaceNavigation_RandomWalk_KeepsPositionOnSphereAndHeadingTangent()
        {
            var random = new System.Random(5);
            Vector3 position = Vector3.up;
            Vector3 heading = Vector3.forward;

            for (int i = 0; i < 20_000; i++)
            {
                float forward = (float)(random.NextDouble() - 0.5) * 0.2f;
                float right = (float)(random.NextDouble() - 0.5) * 0.2f;
                SurfaceNavigation.Move(ref position, ref heading, forward, right);
                SurfaceNavigation.Turn(position, ref heading, (float)(random.NextDouble() - 0.5) * 0.3f);

                AssertValid(position, heading, $"at step {i}");
            }
        }

        [Test]
        public void SurfaceNavigation_ForwardThenBack_ReturnsToTheStart()
        {
            Vector3 position = new Vector3(0.3f, 0.5f, 0.81f).normalized;
            Vector3 heading = SurfaceNavigation.HeadingNear(position, Vector3.right);
            Vector3 startPosition = position;
            Vector3 startHeading = heading;

            SurfaceNavigation.Move(ref position, ref heading, 0.7f, 0f);
            SurfaceNavigation.Move(ref position, ref heading, -0.7f, 0f);

            Assert.Less(Vector3.Distance(position, startPosition), 1e-4f);
            Assert.Less(Vector3.Distance(heading, startHeading), 1e-3f);
        }

        [Test]
        public void SurfaceNavigation_FullLapOfAGreatCircle_ReturnsToTheStart_WithTheSameHeading()
        {
            Vector3 position = Vector3.right;
            Vector3 heading = Vector3.up;
            Vector3 startHeading = heading;

            const int steps = 720;
            for (int i = 0; i < steps; i++)
            {
                SurfaceNavigation.Move(ref position, ref heading, 2f * Mathf.PI / steps, 0f);
            }

            Assert.Less(Vector3.Distance(position, Vector3.right), 1e-3f);
            Assert.Less(Vector3.Distance(heading, startHeading), 1e-2f, "A great circle has no holonomy: the heading must come back unchanged.");
        }

        [Test]
        public void SurfaceNavigation_RightStep_MovesTowardTheRightHandSide()
        {
            Vector3 position = Vector3.up;
            Vector3 heading = Vector3.forward;

            SurfaceNavigation.Move(ref position, ref heading, 0f, 0.3f);

            // Standing on top of the sphere facing +z, the right-hand side is +x (Unity's left-handed axes).
            Assert.Greater(position.x, 0.25f);
            Assert.AreEqual(0f, position.z, 1e-5f);
        }

        [Test]
        public void SurfaceNavigation_WalkingThroughAPole_HasNoSingularity()
        {
            Vector3 position = Vector3.Slerp(Vector3.up, Vector3.forward, 0.9f);
            Vector3 heading = SurfaceNavigation.HeadingNear(position, Vector3.up);

            for (int i = 0; i < 200; i++)
            {
                SurfaceNavigation.Move(ref position, ref heading, 0.02f, 0f);
                AssertValid(position, heading, $"step {i}");
            }
        }

        [Test]
        public void SurfaceNavigation_HeadingNear_HandlesAPreferredDirectionStraightUp()
        {
            Vector3 heading = SurfaceNavigation.HeadingNear(Vector3.up, Vector3.up);

            AssertValid(Vector3.up, heading);
            Assert.IsFalse(float.IsNaN(heading.x));
        }

        [Test]
        public void SurfaceNavigation_CameraPose_StandsAboveThePointLookingAlongTheHeading()
        {
            Quaternion planetRotation = Quaternion.Euler(20f, 70f, 5f);
            Vector3 centre = new Vector3(0f, 1.4f, 0f);
            Vector3 position = new Vector3(0.2f, 0.9f, 0.3f).normalized;
            Vector3 heading = SurfaceNavigation.HeadingNear(position, Vector3.right);
            const float height = 0.2f;

            SurfaceNavigation.CameraPose(planetRotation, centre, 1f, position, heading, height, 0f, out Vector3 cameraPosition, out Quaternion cameraRotation);

            Vector3 up = planetRotation * position;
            Assert.Less(Vector3.Distance(cameraPosition, centre + up * (1f + height)), 1e-5f, "Camera must stand above the point.");
            Assert.Less(Vector3.Distance(cameraRotation * Vector3.up, up), 1e-4f, "Camera up must be the surface normal.");
            Assert.Less(Vector3.Distance(cameraRotation * Vector3.forward, planetRotation * heading), 1e-4f, "With no pitch the camera looks along the heading.");
        }

        [Test]
        public void SurfaceNavigation_CameraPose_PitchLooksDownTowardTheGround()
        {
            Vector3 position = Vector3.up;
            Vector3 heading = Vector3.forward;

            SurfaceNavigation.CameraPose(Quaternion.identity, Vector3.zero, 1f, position, heading, 0.2f, 45f * Mathf.Deg2Rad, out _, out Quaternion rotation);

            Vector3 forward = rotation * Vector3.forward;
            Assert.AreEqual(-Mathf.Sin(45f * Mathf.Deg2Rad), forward.y, 1e-4f);
            Assert.Greater(forward.z, 0.6f);
        }

        [Test]
        public void SurfaceNavigation_ThePlanetSpinning_DoesNotMoveTheStandpointInThePlanetFrame()
        {
            // The standpoint is stored in the planet's own frame, so spinning the planet carries the camera
            // with it: the camera's position relative to the planet's centre, expressed back in the planet
            // frame, must not change.
            Vector3 position = new Vector3(0.4f, 0.4f, 0.82f).normalized;
            Vector3 heading = SurfaceNavigation.HeadingNear(position, Vector3.right);

            foreach (float spin in new[] { 0f, 40f, 130f })
            {
                Quaternion rotation = Quaternion.Euler(0f, spin, 12f);
                SurfaceNavigation.CameraPose(rotation, Vector3.zero, 1f, position, heading, 0.1f, 0.5f, out Vector3 cameraPosition, out _);

                Vector3 backInPlanetFrame = Quaternion.Inverse(rotation) * cameraPosition;
                Assert.Less(Vector3.Distance(backInPlanetFrame, position * 1.1f), 1e-4f);
            }
        }
    }
}
