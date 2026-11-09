// =============================================================================
// PlanetOrientationTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the planet's rotation maths: a positive lean tips the north
//           pole TOWARD the lamp (so the sign convention for seasons is right),
//           spin alone leaves the pole upright, and spin turns the surface.
// =============================================================================

using NUnit.Framework;
using Terrarium.Climate;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class PlanetOrientationTests
    {
        private static readonly Vector3 PlanetToLamp = new Vector3(-2f, 1.2f, -0.7f);

        [Test]
        public void PlanetOrientation_PositiveLean_TipsNorthPoleTowardLamp()
        {
            float lean = 23f * Mathf.Deg2Rad;

            Vector3 pole = PlanetOrientation.Compute(0f, lean, PlanetToLamp) * Vector3.up;

            Vector3 towardLamp = Vector3.ProjectOnPlane(PlanetToLamp, Vector3.up).normalized;
            Assert.AreEqual(Mathf.Sin(lean), Vector3.Dot(pole, towardLamp), 1e-5f, "Pole should lean toward the lamp by exactly the lean angle.");
            Assert.AreEqual(Mathf.Cos(lean), pole.y, 1e-5f);
        }

        [Test]
        public void PlanetOrientation_NegativeLean_TipsNorthPoleAwayFromLamp()
        {
            Vector3 pole = PlanetOrientation.Compute(0f, -0.3f, PlanetToLamp) * Vector3.up;

            Vector3 towardLamp = Vector3.ProjectOnPlane(PlanetToLamp, Vector3.up).normalized;
            Assert.Less(Vector3.Dot(pole, towardLamp), 0f);
        }

        [Test]
        public void PlanetOrientation_SpinOnly_KeepsPoleUpright()
        {
            Vector3 pole = PlanetOrientation.Compute(1.234f, 0f, PlanetToLamp) * Vector3.up;

            Assert.AreEqual(1f, pole.y, 1e-6f);
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
        public void PlanetOrientation_QuarterSpin_TurnsEquatorPointByNinetyDegrees()
        {
            Vector3 before = PlanetOrientation.Compute(0f, 0f, PlanetToLamp) * Vector3.forward;
            Vector3 after = PlanetOrientation.Compute(Mathf.PI * 0.5f, 0f, PlanetToLamp) * Vector3.forward;

            Assert.AreEqual(0f, Vector3.Dot(before, after), 1e-5f);
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
