// =============================================================================
// TreeGenes
// -----------------------------------------------------------------------------
// Purpose : The inherited traits of one tree, and what each trait costs and buys.
//           Together they let different "species" appear by themselves: cold ground
//           ends up with narrow evergreen cones, warm wet ground with broad leafy
//           domes, dry ground with short thrifty trees. Nobody assigns a species to
//           a biome.
// Idea    : Each tree carries six numbers (its genes). A seed inherits its parent's
//           genes with a small random change (mutation). The genes change how well
//           the tree copes with the place it lands, so the trees that suit a place
//           grow, seed and spread there, and the others fail. Over a few generations
//           the forest of each region drifts toward what works there. This is natural
//           selection with no script: the only rules are the costs below.
//
//   The genes and their trade-offs (nothing is free, so no single tree wins everywhere):
//     OptimumTemperature  the temperature the tree grows best at (deg C). It also moves the
//                         whole tolerance range with it. COST: cold-adapted trees grow slower
//                         (vigor), warm-adapted ones a little faster but cannot stand the cold.
//     Cone                0 = broad dome crown, 1 = narrow cone. BUYS freeze hardiness and needs
//                         less water; COSTS crown size (less light caught is modelled by the
//                         narrower crown holding fewer branches).
//     Height              0.7 .. 1.3 times the normal height. COST: tall trees need wetter ground.
//     LeafSize            0.7 .. 1.5 times the normal leaf. Looks only (broad leaves vs fine).
//     Evergreen           0..1: keeps its leaves in the cold (and has needle-like leaves). BUYS
//                         leaves in winter and cold hardiness; COSTS growth (up to 25% slower).
//     Hue                 a free colour drift of the leaves. No effect on survival, so related
//                         trees keep a family resemblance you can see.
//
// Equations (defaults are the "plain tree" the game had before genes: Cone 0, Evergreen 0, Height 1):
//     temperature range   [opt - (T_opt - T_min), opt + (T_max - T_opt)]     (config numbers, moved by the gene)
//     vigor               1 + (opt - T_opt) * (0.02 if opt < T_opt else 0.01), clamped to 0.5 .. 1.15
//     evergreen cost      growth *= 1 - 0.25 * Evergreen
//     moisture shift      min moisture += 0.10 (Height - 1) / 0.3 - 0.08 Cone
//     freeze hardiness    freeze temperature += -(14 Cone + 6 Evergreen)
//     crown profile       r(t) = lerp( sqrt(1 - (2t - 1)^2), 1 - t, Cone )     t = 0 (crown bottom) .. 1 (top)
//                         (a dome at Cone = 0, a cone at Cone = 1)
//     child               each gene += uniform(-1, 1) * mutation * range, clamped to its range
//
// Parameters (SimConfig "Trees: genes and growth animation"): treeMutation, treeStartVariety.
// A default(TreeGenes) is "not set" (Valid = false); use Default(config) for the plain tree.
// =============================================================================

using UnityEngine;

namespace Terrarium.Trees
{
    /// <summary>How varied the living forest is (see TreeManager.MeasureDiversity).</summary>
    public struct TreeDiversity
    {
        /// <summary>Living trees counted.</summary>
        public int Trees;

        /// <summary>Lowest temperature optimum among them, deg C.</summary>
        public float MinOptimum;

        /// <summary>Highest temperature optimum among them, deg C.</summary>
        public float MaxOptimum;

        /// <summary>Mean temperature optimum, deg C.</summary>
        public float MeanOptimum;

        /// <summary>Share of trees with a conical crown (cone gene above 0.5).</summary>
        public float ConeShare;

        /// <summary>Share of evergreen trees (evergreen gene above 0.5).</summary>
        public float EvergreenShare;

        /// <summary>Correlation (-1..1) between a tree's temperature optimum and how far from the equator it stands. Strongly negative means cold-adapted trees have settled the poles: climate has selected different kinds in different places.</summary>
        public float OptimumVsLatitude;

        /// <summary>Number of distinct kinds (4 temperature bands x dome/cone x deciduous/evergreen) that each hold 3% or more of the trees.</summary>
        public int Groups;
    }

    /// <summary>The inherited traits of one tree.</summary>
    public struct TreeGenes
    {
        /// <summary>Lowest optimum temperature a tree can evolve, deg C.</summary>
        public const float OptimumMin = 4f;

        /// <summary>Highest optimum temperature a tree can evolve, deg C.</summary>
        public const float OptimumMax = 32f;

        /// <summary>Lowest height multiplier.</summary>
        public const float HeightMin = 0.7f;

        /// <summary>Highest height multiplier.</summary>
        public const float HeightMax = 1.3f;

        /// <summary>Lowest leaf size multiplier.</summary>
        public const float LeafSizeMin = 0.7f;

        /// <summary>Highest leaf size multiplier.</summary>
        public const float LeafSizeMax = 1.5f;

        /// <summary>False for a default(TreeGenes), which means "not set".</summary>
        public bool Valid;

        /// <summary>Temperature the tree grows best at, deg C.</summary>
        public float OptimumTemperature;

        /// <summary>0 = broad dome crown, 1 = narrow cone.</summary>
        public float Cone;

        /// <summary>Height multiplier.</summary>
        public float Height;

        /// <summary>Leaf size multiplier.</summary>
        public float LeafSize;

        /// <summary>0 = deciduous, 1 = keeps its (needle-like) leaves in the cold.</summary>
        public float Evergreen;

        /// <summary>Leaf colour drift, 0..1 (no effect on survival).</summary>
        public float Hue;

        /// <summary>The plain tree the game had before genes existed.</summary>
        public static TreeGenes Default(SimConfig config)
        {
            return new TreeGenes
            {
                Valid = true,
                OptimumTemperature = config.treeTempOptimum,
                Cone = 0f,
                Height = 1f,
                LeafSize = 1f,
                Evergreen = 0f,
                Hue = 0.5f,
            };
        }

        /// <summary>Genes for a first-generation tree: the plain tree blended toward fully random by treeStartVariety.</summary>
        public static TreeGenes Random(ref Rng rng, SimConfig config)
        {
            TreeGenes plain = Default(config);
            float variety = Mathf.Clamp01(config.treeStartVariety);

            return new TreeGenes
            {
                Valid = true,
                OptimumTemperature = Mathf.Lerp(plain.OptimumTemperature, rng.Range(OptimumMin + 2f, OptimumMax - 2f), variety),
                Cone = Mathf.Lerp(plain.Cone, rng.NextFloat(), variety),
                Height = Mathf.Lerp(plain.Height, rng.Range(HeightMin, HeightMax), variety),
                LeafSize = Mathf.Lerp(plain.LeafSize, rng.Range(LeafSizeMin, LeafSizeMax), variety),
                Evergreen = Mathf.Lerp(plain.Evergreen, rng.NextFloat() < 0.4f ? rng.Range(0.6f, 1f) : rng.Range(0f, 0.2f), variety),
                Hue = Mathf.Lerp(plain.Hue, rng.NextFloat(), variety),
            };
        }

        /// <summary>The genes of a seed: this tree's genes plus a small random change to each.</summary>
        public TreeGenes Child(ref Rng rng, SimConfig config)
        {
            float m = config.treeMutation;
            return new TreeGenes
            {
                Valid = true,
                OptimumTemperature = Mathf.Clamp(OptimumTemperature + rng.Range(-1f, 1f) * m * (OptimumMax - OptimumMin), OptimumMin, OptimumMax),
                Cone = Mathf.Clamp01(Cone + rng.Range(-1f, 1f) * m),
                Height = Mathf.Clamp(Height + rng.Range(-1f, 1f) * m * (HeightMax - HeightMin), HeightMin, HeightMax),
                LeafSize = Mathf.Clamp(LeafSize + rng.Range(-1f, 1f) * m * (LeafSizeMax - LeafSizeMin), LeafSizeMin, LeafSizeMax),
                Evergreen = Mathf.Clamp01(Evergreen + rng.Range(-1f, 1f) * m),
                Hue = Mathf.Clamp01(Hue + rng.Range(-1f, 1f) * m),
            };
        }

        /// <summary>
        /// Growth-speed multiplier: cold-adapted trees grow slower, warm-adapted a little faster, and keeping
        /// leaves all winter (evergreen) costs up to a quarter of the growth.
        /// </summary>
        public float Vigor(SimConfig config)
        {
            float offset = OptimumTemperature - config.treeTempOptimum;
            float temperatureVigor = Mathf.Clamp(1f + offset * (offset < 0f ? 0.02f : 0.01f), 0.5f, 1.15f);
            return temperatureVigor * (1f - 0.25f * Evergreen);
        }

        /// <summary>How much higher (+) or lower (-) the minimum moisture for growth is: tall trees are thirstier, cones thrifty.</summary>
        public float MoistureShift => 0.10f * (Height - 1f) / 0.3f - 0.08f * Cone;

        /// <summary>How much lower (-) the freezing temperature is: cones and evergreens are hardy.</summary>
        public float FreezeShift => -(14f * Cone + 6f * Evergreen);

        /// <summary>Share of the trunk that is bare (conifers carry branches nearly to the ground).</summary>
        public float TrunkScale => 1f - 0.6f * Cone;

        /// <summary>Extra pull toward up in each new branch: cones grow straighter.</summary>
        public float GravitropismAdd => 0.25f * Cone;

        /// <summary>Crown height multiplier: cones are taller and narrower.</summary>
        public float CrownHeightScale => 1f + 0.4f * Cone;

        /// <summary>Crown width multiplier (a cone is narrower than a dome).</summary>
        public float CrownWidthScale => 1f - 0.35f * Cone;

        /// <summary>Crown radius at a fraction t of the way up the crown (0 = bottom, 1 = top), as a share of the full width.</summary>
        public float CrownProfile(float t)
        {
            t = Mathf.Clamp01(t);
            float dome = Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * t - 1f) * (2f * t - 1f)));
            float cone = 1f - t;
            return Mathf.Max(0.08f, Mathf.Lerp(dome, cone, Cone));
        }
    }
}
