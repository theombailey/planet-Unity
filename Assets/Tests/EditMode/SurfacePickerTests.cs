// =============================================================================
// SurfacePickerTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks ray-sphere picking: hits and misses, a sphere behind the
//           ray, a ray starting inside the sphere, and that the local direction
//           is independent of the planet's current spin.
// =============================================================================

using NUnit.Framework;
using Terrarium.Interaction;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class SurfacePickerTests
    {
        [Test]
        public void SurfacePicker_RayThroughCentre_HitsNearSideAtDistanceMinusRadius()
        {
            bool hit = SurfacePicker.RaySphere(new Vector3(0, 0, -5), Vector3.forward, Vector3.zero, 1f, out float distance);

            Assert.IsTrue(hit);
            Assert.AreEqual(4f, distance, 1e-5f);
        }

        [Test]
        public void SurfacePicker_RayPassingBesideSphere_Misses()
        {
            Assert.IsFalse(SurfacePicker.RaySphere(new Vector3(1.5f, 0, -5), Vector3.forward, Vector3.zero, 1f, out _));
        }

        [Test]
        public void SurfacePicker_SphereBehindTheRay_Misses()
        {
            Assert.IsFalse(SurfacePicker.RaySphere(new Vector3(0, 0, 5), Vector3.forward, Vector3.zero, 1f, out _));
        }

        [Test]
        public void SurfacePicker_RayStartingInside_HitsTheFarSide()
        {
            bool hit = SurfacePicker.RaySphere(Vector3.zero, Vector3.forward, Vector3.zero, 1f, out float distance);

            Assert.IsTrue(hit);
            Assert.AreEqual(1f, distance, 1e-5f);
        }

        [Test]
        public void SurfacePicker_NonUnitDirection_GivesTheSameDistance()
        {
            SurfacePicker.RaySphere(new Vector3(0, 0, -5), new Vector3(0, 0, 7), Vector3.zero, 1f, out float distance);

            Assert.AreEqual(4f, distance, 1e-5f);
        }

        [Test]
        public void SurfacePicker_LocalDirection_IgnoresPlanetSpin()
        {
            // A fixed point on the planet's surface, e.g. local +x. As the planet spins, the ray that hits it
            // changes, but the local direction found must stay the same.
            var planetObject = new GameObject("Planet");
            try
            {
                Vector3 localPoint = new Vector3(0.6f, 0.0f, 0.8f);

                foreach (float spinDegrees in new[] { 0f, 40f, 130f, 250f })
                {
                    planetObject.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, 0f), Quaternion.Euler(10f, spinDegrees, 5f));

                    Vector3 worldPoint = planetObject.transform.TransformPoint(localPoint);
                    Vector3 origin = worldPoint + (worldPoint - planetObject.transform.position).normalized * 3f;
                    var ray = new Ray(origin, worldPoint - origin);

                    bool hit = SurfacePicker.TryPickLocalDirection(ray, planetObject.transform, 1f, out Vector3 local);

                    Assert.IsTrue(hit);
                    Assert.Less(Vector3.Distance(local, localPoint.normalized), 1e-4f, $"Spin {spinDegrees}: got {local}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(planetObject);
            }
        }
    }
}
