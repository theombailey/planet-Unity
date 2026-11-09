// =============================================================================
// PlanetOrientationTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the planet's rotation maths, which the seasons depend on:
//           - at lean 0 the lamp is exactly over the equator (an equinox),
//             wherever the lamp stands,
//           - a positive lean puts the lamp over that northern latitude,
//           - spin turns the planet about its axis without moving the axis,
//           - the axis stays as upright as the lamp's position allows.
// =============================================================================

using NUnit.Framework;
using Terrarium.Climate;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class PlanetOrientationTests
    {
        // Roughly where the scene's lamp is, relative to the planet's centre.
        private static readonly Vector3 PlanetToLamp = new Vector3(-1.75f, 1.15f, -0.45f);

        private static float LampLatitudeRadians(float spin, float lean)
        {
            // The lamp's direction in the planet's own frame; its y component is sin(latitude).
            Vector3 local = Quaternion.Inverse(PlanetOrientation.Compute(spin, lean, PlanetToLamp)) * PlanetToLamp.normalized;
            return Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f));
        }

        [Test]
        public void PlanetOrientation_ZeroLean_PutsTheLampOverTheEquator()
        {
            foreach (float spin in new[] { 0f, 1f, 2.5f, 4f })
            {
                Assert.AreEqual(0f, LampLatitudeRadians(spin, 0f), 1e-5f, $"Spin {spin}.");
            }
        }

        [TestCase(23f)]
        [TestCase(-23f)]
        [TestCase(10f)]
        public void PlanetOrientation_Lean_IsTheLatitudeUnderTheLamp_AtAnySpin(float leanDegrees)
        {
            float lean = leanDegrees * Mathf.Deg2Rad;

            foreach (float spin in new[] { 0f, 0.7f, 2.2f, 5f })
            {
                Assert.AreEqual(lean, LampLatitudeRadians(spin, lean), 1e-4f, $"Spin {spin}.");
            }
        }

        [Test]
        public void PlanetOrientation_PositiveLean_TipsNorthPoleTowardLamp()
        {
            float lean = 23f * Mathf.Deg2Rad;

            Vector3 pole = PlanetOrientation.Compute(0f, lean, PlanetToLamp) * Vector3.up;

            Assert.AreEqual(Mathf.Sin(lean), Vector3.Dot(pole, PlanetToLamp.normalized), 1e-5f);
        }

        [Test]
        public void PlanetOrientation_NegativeLean_TipsNorthPoleAwayFromLamp()
        {
            Vector3 pole = PlanetOrientation.Compute(0f, -0.3f, PlanetToLamp) * Vector3.up;

            Assert.Less(Vector3.Dot(pole, PlanetToLamp.normalized), 0f);
        }

        [Test]
        public void PlanetOrientation_ZeroLean_KeepsThePoleAsUprightAsTheLampAllows()
        {
            Vector3 pole = PlanetOrientation.Compute(0f, 0f, PlanetToLamp) * Vector3.up;

            // The pole must be at right angles to the lamp direction, so it is tilted from vertical by
            // exactly the lamp's elevation, and still points upward.
            float elevation = Mathf.Asin(PlanetToLamp.normalized.y);
            Assert.AreEqual(Mathf.Cos(elevation), pole.y, 1e-5f);
            Assert.Greater(pole.y, 0.8f);
        }

        [Test]
        public void PlanetOrientation_Spin_DoesNotChangeWhereThePolePoints()
        {
            float lean = 0.4f;

            Vector3 poleA = PlanetOrientation.Compute(0f, lean, PlanetToLamp) * Vector3.up;
            Vector3 poleB = PlanetOrientation.Compute(2.5f, lean, PlanetToLamp) * Vector3.up;

            Assert.Less((poleA - poleB).magnitude, 1e-5f, "Spin is about the pole, so it must not move the pole.");
        }

        [Test]
        public void PlanetOrientation_QuarterSpin_TurnsASurfacePointByNinetyDegrees()
        {
            // A point on the planet's equator (local +x) is carried a quarter of the way round.
            Vector3 before = PlanetOrientation.Compute(0f, 0.2f, PlanetToLamp) * Vector3.right;
            Vector3 after = PlanetOrientation.Compute(Mathf.PI * 0.5f, 0.2f, PlanetToLamp) * Vector3.right;

            Assert.AreEqual(90f, Vector3.Angle(before, after), 1e-2f);
        }

        [Test]
        public void PlanetOrientation_LampDirectlyOverhead_StillGivesAValidRotation()
        {
            Quaternion rotation = PlanetOrientation.Compute(0.5f, 0.3f, Vector3.up * 3f);

            Vector3 pole = rotation * Vector3.up;
            Assert.AreEqual(1f, pole.magnitude, 1e-5f);
            Assert.IsFalse(float.IsNaN(pole.x));
        }
    }
}
