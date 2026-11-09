// =============================================================================
// SimClockTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the clock's arithmetic: real time -> fixed steps, pausing,
//           the per-frame step cap, and the day / year / axis-lean phases.
// =============================================================================

using NUnit.Framework;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class SimClockTests
    {
        private SimConfig config;

        [SetUp]
        public void CreateConfig()
        {
            config = ScriptableObject.CreateInstance<SimConfig>();
            config.fixedStepSeconds = 0.01f;
            config.maxStepsPerFrame = 16;
            config.dayLengthSeconds = 10f;
            config.yearLengthDays = 4f;
            config.axialTiltDegrees = 20f;
        }

        [TearDown]
        public void DestroyConfig()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void SimClock_Advance_ReturnsWholeFixedSteps()
        {
            var clock = new SimClock(config);

            int steps = clock.Advance(0.035f);

            Assert.AreEqual(3, steps);
            Assert.AreEqual(0.03, clock.SimTimeSeconds, 1e-6);
        }

        [Test]
        public void SimClock_Advance_CarriesLeftoverTimeToNextFrame()
        {
            var clock = new SimClock(config);

            clock.Advance(0.035f);
            int steps = clock.Advance(0.006f);

            Assert.AreEqual(1, steps, "0.005 left over + 0.006 new = one more 0.01 step.");
        }

        [Test]
        public void SimClock_TimeScaleZero_TakesNoSteps()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(0);

            int steps = clock.Advance(0.05f);

            Assert.AreEqual(0, steps);
            Assert.AreEqual(0.0, clock.SimTimeSeconds);
        }

        [Test]
        public void SimClock_TimeScale_MultipliesSteps()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(4);

            int steps = clock.Advance(0.02f);

            Assert.AreEqual(8, steps);
        }

        [Test]
        public void SimClock_Advance_NeverExceedsMaxStepsPerFrame_AndDropsTheExcess()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(16);

            int first = clock.Advance(0.1f);
            int second = clock.Advance(0f);

            Assert.AreEqual(16, first, "160 steps owed, capped at 16.");
            Assert.AreEqual(0, second, "The excess must be dropped, not carried into later frames.");
        }

        [Test]
        public void SimClock_Phases_WrapWithDayAndYear()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(16);

            // 1 day = 10 s, 1 year = 4 days = 40 s. Advance 52.5 s = 5250 steps:
            // 5.25 days and 1.3125 years. (Deliberately not on a day boundary, where
            // float rounding could land on either side.)
            int total = 0;
            while (total < 5250)
            {
                total += clock.Advance(0.01f);
            }

            Assert.AreEqual(5, clock.DayIndex);
            Assert.AreEqual(1, clock.YearIndex);
            Assert.AreEqual(0.25f, clock.DayPhase, 0.01f);
            Assert.AreEqual(0.3125f, clock.YearPhase, 0.01f);
        }

        [Test]
        public void SimClock_Lean_PeaksAtTiltAngleInMidsummer()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(16);

            // Quarter of a year = 10 s = 1000 steps: yearPhase 0.25, sin = 1, lean = +tilt.
            int total = 0;
            while (total < 1000)
            {
                total += clock.Advance(0.01f);
            }

            Assert.AreEqual(20f, clock.LeanRadians * Mathf.Rad2Deg, 0.2f);
        }

        [Test]
        public void SimClock_Reset_ReturnsToTimeZero()
        {
            var clock = new SimClock(config);
            clock.Advance(0.05f);

            clock.Reset();

            Assert.AreEqual(0.0, clock.SimTimeSeconds);
            Assert.AreEqual(0, clock.StepCount);
        }
    }
}
