// =============================================================================
// PaperLog
// -----------------------------------------------------------------------------
// Purpose : Decides WHAT goes on the paper roll. It watches the ecosystem numbers and
//           writes a short line when something worth reading happens: a new season,
//           a population milestone, a population that is fading or has recovered.
//           It never prints a steady stream of numbers; the nixie tubes are for those.
// Idea    : A small state machine. It remembers the last season it printed and which
//           milestones each population has already passed, and each time it is shown
//           the latest EcosystemStats it returns the new lines (if any). Pure logic:
//           no scene, no clock, so each rule can be tested on its own.
// Rules   :
//     SEASON     the year phase is split in four (0-.25 spring, .25-.5 summer, .5-.75 autumn, .75-1 winter);
//                a new season prints a three-line block: the year and season, the populations and the temperature.
//     MILESTONE  trees, birds and fungus each print once when they first pass a milestone, and again (as a fall)
//                when they drop back below the one under it. Milestones are kept apart by a margin so a population
//                wobbling around a number does not print over and over.
//     FADING     fungus below 100 prints "FUNGUS FADING"; back above 1000 prints "FUNGUS RECOVERS".
// =============================================================================

using System.Collections.Generic;

namespace Terrarium.Workshop
{
    /// <summary>Turns ecosystem snapshots into short lines for the paper roll.</summary>
    public sealed class PaperLog
    {
        private static readonly string[] SeasonNames = { "SPRING", "SUMMER", "AUTUMN", "WINTER" };
        private static readonly int[] TreeMilestones = { 50, 100, 250, 500, 750, 1000 };
        private static readonly int[] BirdMilestones = { 500, 1000, 2000, 5000 };
        private static readonly int[] FungusMilestones = { 1000, 10000, 100000 };

        private int lastSeason = -1;
        private int treeLevel;
        private int birdLevel;
        private int fungusLevel;
        private bool fungusFading;
        private bool primed;

        /// <summary>The season index 0..3 for a year phase (0 = spring equinox).</summary>
        public static int SeasonOf(float yearPhase)
        {
            int season = (int)(yearPhase * 4f);
            return season < 0 ? 0 : (season > 3 ? 3 : season);
        }

        /// <summary>The season's name.</summary>
        public static string SeasonName(int season)
        {
            return SeasonNames[season < 0 ? 0 : (season > 3 ? 3 : season)];
        }

        /// <summary>Forgets everything (a new world starts, so every milestone can print again).</summary>
        public void Reset()
        {
            lastSeason = -1;
            treeLevel = 0;
            birdLevel = 0;
            fungusLevel = 0;
            fungusFading = false;
            primed = false;
        }

        /// <summary>
        /// Looks at the latest numbers and appends any new lines to <paramref name="lines"/>.
        /// The very first snapshot after a reset only records where things stand (so it does not print a milestone
        /// for every level the world already started above).
        /// </summary>
        public void Poll(EcosystemStats stats, List<string> lines)
        {
            if (!stats.Valid)
            {
                return;
            }

            int season = SeasonOf(stats.YearPhase);
            int trees = stats.TreesAlive;
            int birds = stats.BoidsAlive;
            int fungus = stats.PhysarumAlive;

            if (!primed)
            {
                primed = true;
                lastSeason = season;
                treeLevel = LevelOf(trees, TreeMilestones);
                birdLevel = LevelOf(birds, BirdMilestones);
                fungusLevel = LevelOf(fungus, FungusMilestones);
                fungusFading = fungus < 100;
                lines.Add($"-- YEAR {stats.Year} {SeasonName(season)} --");
                return;
            }

            if (season != lastSeason)
            {
                lastSeason = season;
                lines.Add($"-- YEAR {stats.Year} {SeasonName(season)} --");
                lines.Add($"TREES {trees} BIRDS {birds}");
                lines.Add($"FUNGUS {Short(fungus)} TEMP {stats.MeanTemperature:0.0}C");
            }

            Milestones("TREES", trees, TreeMilestones, ref treeLevel, lines);
            Milestones("BIRDS", birds, BirdMilestones, ref birdLevel, lines);
            Milestones("FUNGUS", fungus, FungusMilestones, ref fungusLevel, lines);

            if (!fungusFading && fungus < 100)
            {
                fungusFading = true;
                lines.Add("FUNGUS FADING");
            }
            else if (fungusFading && fungus > 1000)
            {
                fungusFading = false;
                lines.Add("FUNGUS RECOVERS");
            }
        }

        /// <summary>A count in short form: 950, 12.3K, 1.2M.</summary>
        public static string Short(int count)
        {
            if (count >= 1_000_000)
            {
                return $"{count / 1_000_000f:0.0}M";
            }

            return count >= 10_000 ? $"{count / 1000f:0.0}K" : count.ToString();
        }

        // The number of milestones a value has reached (0 = below the first).
        private static int LevelOf(int value, int[] milestones)
        {
            int level = 0;
            while (level < milestones.Length && value >= milestones[level])
            {
                level++;
            }

            return level;
        }

        private static void Milestones(string name, int value, int[] milestones, ref int level, List<string> lines)
        {
            // Rising: print each milestone passed. Falling: only once the value is 10% BELOW the milestone, so wobble does not spam.
            while (level < milestones.Length && value >= milestones[level])
            {
                lines.Add($"{name} PASS {Short(milestones[level])}");
                level++;
            }

            while (level > 0 && value < milestones[level - 1] * 0.9f)
            {
                level--;
                lines.Add($"{name} UNDER {Short(milestones[level])}");
            }
        }
    }
}
