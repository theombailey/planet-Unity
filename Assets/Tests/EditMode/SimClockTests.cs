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

            // 1 day = 10 s, 1 year = 4 days = 40 s. Advance 52 s = 5200 steps:
            // 5.2 days and 1.3 years. (Deliberately not on a day boundary, where
            // float rounding could land on either side.) Each call takes 16 steps
            // at 16x, and 5200 = 325 x 16, so the loop stops exactly on target.
            const int targetSteps = 5200;
            int total = 0;
            while (total < targetSteps)
            {
                total += clock.Advance(0.01f);
            }

            Assert.AreEqual(targetSteps, total);
            Assert.AreEqual(5, clock.DayIndex);
            Assert.AreEqual(1, clock.YearIndex);
            Assert.AreEqual(0.2f, clock.DayPhase, 0.001f);
            Assert.AreEqual(0.3f, clock.YearPhase, 0.001f);
        }

        [Test]
        public void SimClock_Lean_PeaksAtTiltAngleInMidsummer()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(4);

            // Quarter of a year = 10 s = 1000 steps: yearPhase 0.25, sin = 1, lean = +tilt.
            // Each call takes 4 steps at 4x, and 1000 = 250 x 4, so the loop stops exactly on target.
            const int targetSteps = 1000;
            int total = 0;
            while (total < targetSteps)
            {
                total += clock.Advance(0.01f);
            }

            Assert.AreEqual(targetSteps, total);
            Assert.AreEqual(20f, clock.LeanRadians * Mathf.Rad2Deg, 0.2f);
        }

        [Test]
        public void SimClock_SetDayLength_KeepsTheSpinAngleAndSeasonContinuous()
        {
            var clock = new SimClock(config);
            clock.SetTimeScale(4);
            int total = 0;
            while (total < 1300)
            {
                total += clock.Advance(0.01f);
            }

            float spinBefore = clock.SpinAngleRadians;
            float leanBefore = clock.LeanRadians;
            int dayBefore = clock.DayIndex;
            int yearBefore = clock.YearIndex;

            clock.SetDayLength(5f);   // twice as fast as the 10 s day

            Assert.AreEqual(spinBefore, clock.SpinAngleRadians, 1e-4f, "The planet must not jump when the dial turns.");
            Assert.AreEqual(leanBefore, clock.LeanRadians, 1e-4f, "The season must not jump either.");
            Assert.AreEqual(dayBefore, clock.DayIndex);
            Assert.AreEqual(yearBefore, clock.YearIndex);
            Assert.AreEqual(5f, clock.DayLengthSeconds);
        }

        [Test]
        public void SimClock_SetDayLength_ThenTurnsAtTheNewRate()
        {
            var clock = new SimClock(config);
            clock.SetDayLength(5f);
            float start = clock.DayPhase;

            // 125 steps of 0.01 s is 1.25 s: a quarter of a 5 s day.
            clock.SetTimeScale(1);
            int steps = 0;
            while (steps < 125)
            {
                steps += clock.Advance(0.0125f);
            }

            float advanced = (clock.DayPhase - start + 1f) % 1f;
            Assert.AreEqual(0.25f, advanced, 0.01f);
        }

        [Test]
        public void SimClock_SetDayLength_IsClampedToTheAllowedRange()
        {
            var clock = new SimClock(config);

            clock.SetDayLength(0.1f);
            Assert.AreEqual(SimClock.MinDayLengthSeconds, clock.DayLengthSeconds);

            clock.SetDayLength(10_000f);
            Assert.AreEqual(SimClock.MaxDayLengthSeconds, clock.DayLengthSeconds);
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
