// =============================================================================
// StabilityRunner
// -----------------------------------------------------------------------------
// Purpose : An automatic health check for the whole ecosystem. For each of a few
//           world seeds it starts a new world, runs the clock at 16x for a number
//           of sim years, writes down the populations every season, and decides
//           whether the fungus, trees and birds all survived without the birds
//           exploding to fill their pool.
// Idea    : It only watches. It reads EcosystemStats (the same numbers the panel
//           shows) and never touches the simulation except to change the world
//           seed and the clock speed, so what it proves is true of the real game.
// Judging : After a short settling period (SettleYears), every sample must have
//           fungus > 0, trees alive > 0 and birds > 0, and birds and fungus must stay below
//           MaxBirdPoolFraction of their pools. The seasonal cycle is reported as
//           the spread of the mean leaf cover (max - min) over each run.
// Parameters: seeds, yearsPerSeed, settleYears, MaxBirdPoolFraction (inspector).
// =============================================================================

using System.Text;
using UnityEngine;
using Terrarium.Boids;
using Terrarium.Physarum;

namespace Terrarium
{
    /// <summary>Runs a multi-seed accelerated ecosystem check and keeps a text report.</summary>
    public sealed class StabilityRunner : MonoBehaviour
    {
        private const int SamplesPerYear = 4;

        [Tooltip("The simulation (clock and seed).")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("Where the ecosystem numbers come from.")]
        [SerializeField] private StatsSystem stats;

        [Tooltip("To know how big the bird pool is.")]
        [SerializeField] private BoidSystem boids;

        [Tooltip("To know how big the fungus pool is.")]
        [SerializeField] private PhysarumSystem physarum;

        [Tooltip("Seeds to try.")]
        [SerializeField] private int[] seeds = { 12345, 1, 2, 3, 4 };

        [Tooltip("Sim years to run for each seed.")]
        [SerializeField] private int yearsPerSeed = 10;

        [Tooltip("Years at the start of each run that are not judged (the world is still settling).")]
        [SerializeField] private int settleYears = 2;

        [Tooltip("Birds above this share of the pool count as an explosion.")]
        [SerializeField] private float maxBirdPoolFraction = 0.95f;

        private readonly StringBuilder report = new StringBuilder(2048);
        private int seedIndex;
        private double startTime;
    private int framesToWait;
        private int lastSampleNumber;
        private bool running;
        private bool waitingForFirstStats;
        private bool seedFailed;
        private float leafMin;
        private float leafMax;
        private int passed;
        private int failed;
        private int clockBefore;

        /// <summary>True while a check is running.</summary>
        public bool IsRunning => running;

        /// <summary>The text report so far (or of the last finished check).</summary>
        public string Report => report.ToString();

        /// <summary>One line: running, PASS or FAIL, with the counts.</summary>
        public string Summary =>
            running ? $"running: seed {seedIndex + 1} of {seeds.Length}, {passed} passed, {failed} failed"
            : (passed + failed == 0 ? "not run" : (failed == 0 ? $"PASS ({passed} seeds)" : $"FAIL ({failed} of {passed + failed} seeds)"));

        /// <summary>Starts the check from the first seed.</summary>
        public void Begin()
        {
            if (running || simRoot == null || stats == null || seeds == null || seeds.Length == 0)
            {
                return;
            }

            report.Clear();
            report.AppendLine($"Stability check: {seeds.Length} seeds x {yearsPerSeed} sim years at 16x");
            passed = 0;
            failed = 0;
            seedIndex = 0;
            clockBefore = simRoot.Clock.TimeScale;
            running = true;
            StartSeed();
        }

        /// <summary>Stops a running check and puts the clock speed back.</summary>
        public void Cancel()
        {
            if (running)
            {
                running = false;
                simRoot.Clock.SetTimeScale(clockBefore);
                report.AppendLine("cancelled");
            }
        }

        private void StartSeed()
        {
            simRoot.Reseed(seeds[seedIndex]);
            simRoot.Clock.SetTimeScale(16);
            waitingForFirstStats = true;
            framesToWait = 3;
            seedFailed = false;
            leafMin = float.MaxValue;
            leafMax = float.MinValue;
            lastSampleNumber = -1;
            report.AppendLine($"-- seed {seeds[seedIndex]}");
        }

        private void Update()
        {
            if (!running)
            {
                return;
            }

            EcosystemStats s = stats.Latest;
            if (!s.Valid)
            {
                return;
            }

            if (waitingForFirstStats)
            {
                // Let the reseed take effect, then time the run from the clock itself (it restarts on a reseed).
                framesToWait--;
                if (framesToWait <= 0)
                {
                    startTime = simRoot.Clock.SimTimeSeconds;
                    lastSampleNumber = 0;
                    waitingForFirstStats = false;
                }

                return;
            }

            double elapsedYears = (simRoot.Clock.SimTimeSeconds - startTime) / simRoot.Clock.YearLengthSeconds;
            int number = (int)(elapsedYears * SamplesPerYear);
            if (number == lastSampleNumber)
            {
                return;
            }

            lastSampleNumber = number;
            Judge(s, number);

            if (elapsedYears >= yearsPerSeed)
            {
                FinishSeed();
            }
        }

        private void Judge(EcosystemStats s, int number)
        {
            float years = number / (float)SamplesPerYear;
            leafMin = Mathf.Min(leafMin, s.MeanLeafCover);
            leafMax = Mathf.Max(leafMax, s.MeanLeafCover);

            string flag = string.Empty;
            if (years >= settleYears)
            {
                int pool = boids != null ? boids.PoolSize : 0;
                bool extinct = s.PhysarumAlive <= 0 || s.TreesAlive <= 0 || s.BoidsAlive <= 0;
                int fungusPool = physarum != null ? physarum.PoolSize : 0;
                bool exploded = (pool > 0 && s.BoidsAlive > maxBirdPoolFraction * pool)
                             || (fungusPool > 0 && s.PhysarumAlive > maxBirdPoolFraction * fungusPool);
                if (extinct || exploded)
                {
                    seedFailed = true;
                    flag = extinct ? "  <-- EXTINCT" : "  <-- EXPLODED";
                }
            }

            report.AppendLine($"  y{years:0.00}: fungus {s.PhysarumAlive}, trees {s.TreesAlive}, birds {s.BoidsAlive}, leaf {s.MeanLeafCover:0.00}, temp {s.MeanTemperature:0.0}{flag}");
        }

        private void FinishSeed()
        {
            float swing = leafMax - leafMin;
            report.AppendLine($"  result: {(seedFailed ? "FAIL" : "ok")}, seasonal leaf swing {swing:0.00}");
            if (seedFailed)
            {
                failed++;
            }
            else
            {
                passed++;
            }

            seedIndex++;
            if (seedIndex >= seeds.Length)
            {
                running = false;
                simRoot.Clock.SetTimeScale(clockBefore);
                report.AppendLine(Summary);
                Debug.Log(report.ToString());
            }
            else
            {
                StartSeed();
            }
        }

        /// <summary>Panel readout and start button.</summary>
        public void OnDebugGui()
        {
            GUILayout.Label($"Stability check: {Summary}");
            if (!running && GUILayout.Button("Run stability check (5 seeds)"))
            {
                Begin();
            }
            else if (running && GUILayout.Button("Cancel check"))
            {
                Cancel();
            }
        }
    }
}
