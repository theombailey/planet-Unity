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
//     dayCount  = (simTime + dayOffset)  / dayLength
//     yearCount = (simTime + yearOffset) / (dayLength * yearLengthDays)
//     dayPhase  = frac(dayCount)                       in [0,1)
//     yearPhase = frac(yearCount)                      in [0,1)
//     spinAngle = 2*pi * dayPhase                      (planet spin, radians)
//     lean      = epsilon * sin(2*pi * yearPhase)      (axis lean toward the lamp)
//
//     lean > 0 tips the north pole toward the lamp (northern summer),
//     lean < 0 tips it away (northern winter). That oscillation IS the seasons.
//
// Slow motion: the time scale can be below 1 (0.25x = a quarter speed). The accumulator then
//     collects a quarter of a step per frame, so a step is taken about every fourth frame.
//
// Spin dial (changing the day length while running):
//     If the day length simply changed, dayCount = time / dayLength would jump
//     and the planet would snap to a new angle. So the offsets are re-chosen at
//     the moment of change to keep the day and year COUNTS unchanged:
//         dayOffset'  = dayCount  * newLength           - simTime
//         yearOffset' = yearCount * newLength * years   - simTime
//     The planet then simply turns faster or slower from where it is.
//     The Per-time queries (SpinAngleRadiansAt) are valid for times at or after
//     the last change, which is all the simulation asks for.
//
// Parameters (SimConfig): dayLengthSeconds (the starting day length),
//     yearLengthDays, axialTiltDegrees (epsilon), fixedStepSeconds (h),
//     maxStepsPerFrame.
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
        public static readonly float[] TimeScaleSteps = { 0f, 0.25f, 1f, 4f, 16f };

        /// <summary>Shortest day the spin dial may set, in sim seconds.</summary>
        public const float MinDayLengthSeconds = 4f;

        /// <summary>Longest day the spin dial may set, in sim seconds.</summary>
        public const float MaxDayLengthSeconds = 240f;

        /// <summary>Longest real frame time the clock will accept, so a hitch or alt-tab cannot dump seconds of work into one frame.</summary>
        private const float MaxRealDeltaSeconds = 0.1f;

        private readonly SimConfig config;
        private float scaleBeforePause = 1f;
        private double accumulatorSeconds;
        private double dayOffsetSeconds;
        private double yearOffsetSeconds;

        /// <summary>Creates a clock at sim time zero, running at 1x.</summary>
        public SimClock(SimConfig config)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            DayLengthSeconds = config.dayLengthSeconds;
            TimeScale = 1f;
        }

        /// <summary>Total simulated seconds since the world was created.</summary>
        public double SimTimeSeconds { get; private set; }

        /// <summary>Total fixed steps taken since the world was created.</summary>
        public long StepCount { get; private set; }

        /// <summary>Current speed multiplier (one of <see cref="TimeScaleSteps"/>; 0 = paused).</summary>
        public float TimeScale { get; private set; }

        /// <summary>Length of one planet day in sim seconds. Starts at the config value; the spin dial changes it.</summary>
        public float DayLengthSeconds { get; private set; }

        /// <summary>Sim seconds advanced by one fixed step (h).</summary>
        public float FixedStepSeconds => config.fixedStepSeconds;

        /// <summary>Length of one year in sim seconds.</summary>
        public double YearLengthSeconds => (double)DayLengthSeconds * config.yearLengthDays;

        /// <summary>Fraction of the current day that has passed, in [0,1).</summary>
        public float DayPhase => Frac((SimTimeSeconds + dayOffsetSeconds) / DayLengthSeconds);

        /// <summary>Fraction of the current year that has passed, in [0,1). 0 = spring equinox, 0.25 = northern midsummer.</summary>
        public float YearPhase => Frac((SimTimeSeconds + yearOffsetSeconds) / YearLengthSeconds);

        /// <summary>Whole days elapsed.</summary>
        public int DayIndex => (int)Math.Floor((SimTimeSeconds + dayOffsetSeconds) / DayLengthSeconds);

        /// <summary>Whole years elapsed.</summary>
        public int YearIndex => (int)Math.Floor((SimTimeSeconds + yearOffsetSeconds) / YearLengthSeconds);

        /// <summary>Planet spin about its own axis, in radians: 2*pi * dayPhase.</summary>
        public float SpinAngleRadians => SpinAngleRadiansAt(SimTimeSeconds);

        /// <summary>Lean of the north pole toward the lamp, in radians: epsilon * sin(2*pi * yearPhase).</summary>
        public float LeanRadians => LeanRadiansAt(SimTimeSeconds);

        /// <summary>
        /// Spin angle at a given sim time. Lets each fixed step of a multi-step frame use its own
        /// exact time rather than the time at the end of the frame.
        /// </summary>
        public float SpinAngleRadiansAt(double simTimeSeconds)
        {
            return 2f * Mathf.PI * Frac((simTimeSeconds + dayOffsetSeconds) / DayLengthSeconds);
        }

        /// <summary>Axis lean toward the lamp at a given sim time.</summary>
        public float LeanRadiansAt(double simTimeSeconds)
        {
            float yearPhase = Frac((simTimeSeconds + yearOffsetSeconds) / YearLengthSeconds);
            return config.axialTiltDegrees * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * yearPhase);
        }

        /// <summary>True while the clock is stopped (time scale 0).</summary>
        public bool IsPaused => TimeScale <= 0f;

        /// <summary>Pauses, or resumes at the speed it had before the pause (1x if it was never running).</summary>
        public void TogglePause()
        {
            if (IsPaused)
            {
                SetTimeScale(scaleBeforePause > 0f ? scaleBeforePause : 1f);
            }
            else
            {
                scaleBeforePause = TimeScale;
                SetTimeScale(0f);
            }
        }

        /// <summary>Sets the speed multiplier. Negative values are treated as 0 (paused).</summary>
        public void SetTimeScale(float timeScale)
        {
            TimeScale = Math.Max(0f, timeScale);
        }

        /// <summary>
        /// Changes the day length without the planet jumping: the day and year counts are kept, so the
        /// planet just turns faster or slower from where it is. The year, which is a fixed number of days,
        /// speeds up or slows down with it.
        /// </summary>
        public void SetDayLength(float seconds)
        {
            float newLength = Mathf.Clamp(seconds, MinDayLengthSeconds, MaxDayLengthSeconds);
            double dayCount = (SimTimeSeconds + dayOffsetSeconds) / DayLengthSeconds;
            double yearCount = (SimTimeSeconds + yearOffsetSeconds) / YearLengthSeconds;

            DayLengthSeconds = newLength;
            dayOffsetSeconds = dayCount * newLength - SimTimeSeconds;
            yearOffsetSeconds = yearCount * YearLengthSeconds - SimTimeSeconds;
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

        /// <summary>Returns the world to sim time zero and the configured day length (used when the world is re-seeded).</summary>
        public void Reset()
        {
            accumulatorSeconds = 0.0;
            SimTimeSeconds = 0.0;
            StepCount = 0;
            DayLengthSeconds = config.dayLengthSeconds;
            dayOffsetSeconds = 0.0;
            yearOffsetSeconds = 0.0;
        }

        private static float Frac(double value)
        {
            return (float)(value - Math.Floor(value));
        }
    }
}
