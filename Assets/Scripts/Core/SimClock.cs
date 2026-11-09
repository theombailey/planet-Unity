// =============================================================================
// SimClock
// -----------------------------------------------------------------------------
// Purpose : The one source of time for the whole simulation. Nothing in the sim
//           reads Time.deltaTime directly; everything asks this clock.
//
// Idea    : Real time is turned into a whole number of equal "fixed steps".
//           Each frame we add (real seconds x time scale) to an accumulator,
//           then take as many fixed steps out of it as fit. Equal steps make
//           the sim reproducible: the same seed gives the same world whatever
//           the frame rate.
//
// Equations (h = fixedStepSeconds, s = time scale, dt = real frame time):
//     accumulator += dt * s
//     steps        = min( floor(accumulator / h), maxStepsPerFrame )
//     simTime     += steps * h
//
//     dayPhase  = frac( simTime / dayLengthSeconds )            in [0,1)
//     yearPhase = frac( simTime / (dayLength * yearLengthDays) ) in [0,1)
//     spinAngle = 2*pi * dayPhase                   (planet spin, radians)
//     lean      = epsilon * sin(2*pi * yearPhase)   (axis lean toward the lamp)
//
//     lean > 0 tips the north pole toward the lamp (northern summer),
//     lean < 0 tips it away (northern winter). That oscillation IS the seasons.
//
// Parameters (all from SimConfig): dayLengthSeconds, yearLengthDays,
//     axialTiltDegrees (epsilon), fixedStepSeconds (h), maxStepsPerFrame.
//
// Note    : If a frame owes more steps than maxStepsPerFrame, the extra time is
//           thrown away, so a slow machine runs the sim slower rather than
//           falling into a "spiral of death" where each frame owes even more.
// =============================================================================

using System;
using UnityEngine;

namespace Terrarium
{
    /// <summary>Converts real time into fixed simulation steps and exposes day / year phase.</summary>
    public sealed class SimClock
    {
        /// <summary>The speeds the time dial can be set to. 0 means paused.</summary>
        public static readonly int[] TimeScaleSteps = { 0, 1, 4, 16 };

        /// <summary>Longest real frame time the clock will accept, so a hitch or alt-tab cannot dump seconds of work into one frame.</summary>
        private const float MaxRealDeltaSeconds = 0.1f;

        private readonly SimConfig config;
        private double accumulatorSeconds;

        /// <summary>Creates a clock at sim time zero, running at 1x.</summary>
        public SimClock(SimConfig config)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            TimeScale = 1;
        }

        /// <summary>Total simulated seconds since the world was created.</summary>
        public double SimTimeSeconds { get; private set; }

        /// <summary>Total fixed steps taken since the world was created.</summary>
        public long StepCount { get; private set; }

        /// <summary>Current speed multiplier (one of <see cref="TimeScaleSteps"/>; 0 = paused).</summary>
        public int TimeScale { get; private set; }

        /// <summary>Sim seconds advanced by one fixed step (h).</summary>
        public float FixedStepSeconds => config.fixedStepSeconds;

        /// <summary>Length of one year in sim seconds.</summary>
        public double YearLengthSeconds => (double)config.dayLengthSeconds * config.yearLengthDays;

        /// <summary>Fraction of the current day that has passed, in [0,1).</summary>
        public float DayPhase => Frac(SimTimeSeconds / config.dayLengthSeconds);

        /// <summary>Fraction of the current year that has passed, in [0,1). 0 = spring equinox, 0.25 = northern midsummer.</summary>
        public float YearPhase => Frac(SimTimeSeconds / YearLengthSeconds);

        /// <summary>Whole days elapsed.</summary>
        public int DayIndex => (int)Math.Floor(SimTimeSeconds / config.dayLengthSeconds);

        /// <summary>Whole years elapsed.</summary>
        public int YearIndex => (int)Math.Floor(SimTimeSeconds / YearLengthSeconds);

        /// <summary>Planet spin about its own axis, in radians: 2*pi * dayPhase.</summary>
        public float SpinAngleRadians => 2f * Mathf.PI * DayPhase;

        /// <summary>Lean of the north pole toward the lamp, in radians: epsilon * sin(2*pi * yearPhase).</summary>
        public float LeanRadians => config.axialTiltDegrees * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * YearPhase);

        /// <summary>Sets the speed multiplier. Negative values are treated as 0 (paused).</summary>
        public void SetTimeScale(int timeScale)
        {
            TimeScale = Math.Max(0, timeScale);
        }

        /// <summary>
        /// Feeds one rendered frame of real time into the clock.
        /// Returns how many fixed steps the sim should run this frame (0 when paused).
        /// </summary>
        public int Advance(float realDeltaSeconds)
        {
            float clampedDelta = Mathf.Clamp(realDeltaSeconds, 0f, MaxRealDeltaSeconds);
            accumulatorSeconds += (double)clampedDelta * TimeScale;

            double step = config.fixedStepSeconds;
            int owed = (int)Math.Floor(accumulatorSeconds / step);
            int steps = Math.Min(owed, config.maxStepsPerFrame);

            // Keep only the leftover fraction of a step. Any whole steps beyond the
            // per-frame cap are dropped on purpose (see the Note in the header).
            accumulatorSeconds -= owed * step;

            StepCount += steps;
            SimTimeSeconds += steps * step;
            return steps;
        }

        /// <summary>Returns the world to sim time zero (used when the world is re-seeded).</summary>
        public void Reset()
        {
            accumulatorSeconds = 0.0;
            SimTimeSeconds = 0.0;
            StepCount = 0;
        }

        private static float Frac(double value)
        {
            return (float)(value - Math.Floor(value));
        }
    }
}
