// =============================================================================
// TreeLife
// -----------------------------------------------------------------------------
// Purpose : The rules that turn LOCAL CONDITIONS into what a tree does: how fast
//           it grows, whether it stays healthy or declines toward death, when its
//           leaves bud, colour and fall, and when it drops seeds. Pure functions of
//           numbers (no scene, no GPU), so each rule can be tested on its own.
// Idea    : Nothing here is scripted by place or time. A tree samples the world at
//           its root (temperature, moisture, nutrients, cleared land, shade from
//           neighbours) and every rule is a smooth response to those numbers.
//           Forests, thinning in winter and regrowth in spring are what comes out.
//
// Growth rate g in [0,1] is a product of four factors, each in [0,1]. A tree grows only if ALL of
//           them are decent, which is Liebig's law of the minimum.
//   temperature  smooth bump: 0 below tMin, 1 at tOpt, back to 0 above tMax
//                f = smoothstep(tMin, tOpt, T) * (1 - smoothstep(tOpt, tMax, T))
//   moisture     f = saturate( (M - mMin) / mSpan )
//   nutrients    f = n / (n + nHalf)                    (a saturating curve: half-effect at nHalf)
//   light        f = 1 / (1 + shade * crowding)         crowding = canopy density of the OTHERS
//   clearing     f = 1 - clearingStress * cleared       cleared = how much people have built or farmed here (0..1)
//                Seeds do not germinate at all where cleared exceeds clearedLimit.
//
// Health h in [0,1] (death at 0), per second:
//   h' = h - (starve + drought + freeze + heat) * dt + recover * dt
//   starve  when g is below a small value       drought when moisture is very low
//   freeze  when T is far below zero            heat    when T is extreme
//   recover when g is healthy (> 0.3)
//
// Leaves (a deciduous tree): the leaf amount L in [0,1] chases a target set by temperature,
//   target = smoothstep(dropT, budT, T)            (0 when cold, 1 when mild)
//   L moves toward it at leafRate per second; the "autumn" tint A in [0,1] rises as it cools
//   toward the drop temperature, and snaps back to 0 as new leaves bud in spring.
//
// Seeds: a mature, healthy, leafy, growing tree makes one seed every seedInterval seconds
//   (a clock that only runs under those conditions).
//
// Genes   : every rule also has a version that takes the tree's TreeGenes (see TreeGenes.cs). The genes
//           move the tolerable temperature range, change how much water a tree needs, how cold-hardy
//           it is, whether it keeps its leaves in winter, and its overall vigor. The versions without
//           genes are the plain tree and behave exactly as the game did before genes existed.
//
// Parameters: all from SimConfig "Trees". Units: temperature deg C, rates per sim second.
// =============================================================================

using UnityEngine;

namespace Terrarium.Trees
{
    /// <summary>The conditions at a tree's root.</summary>
    public struct TreeEnvironment
    {
        /// <summary>Temperature, deg C.</summary>
        public float Temperature;

        /// <summary>Moisture, 0..1.</summary>
        public float Moisture;

        /// <summary>Nutrients in the soil around the root.</summary>
        public float Nutrients;

        /// <summary>How much of the ground at the root people have cleared for a town or a farm, 0..1.</summary>
        public float Cleared;

        /// <summary>Canopy density of the OTHER trees at the root (0 = open sky).</summary>
        public float Crowding;
    }

    /// <summary>Growth, health, leaf and seed rules for one tree, as pure functions.</summary>
    public static class TreeLife
    {
        /// <summary>Temperature factor in [0,1] for the plain (default) tree: 0 outside the tolerable range, 1 at the optimum.</summary>
        public static float TemperatureFactor(float temperature, SimConfig config)
        {
            return TemperatureFactor(temperature, TreeGenes.Default(config), config);
        }

        /// <summary>
        /// Temperature factor in [0,1] for a tree with these genes: its whole tolerable range moves with its
        /// optimum temperature, keeping the config's width below and above the optimum.
        /// </summary>
        public static float TemperatureFactor(float temperature, TreeGenes genes, SimConfig config)
        {
            float optimum = genes.OptimumTemperature;
            float minimum = optimum - (config.treeTempOptimum - config.treeTempMin);
            float maximum = optimum + (config.treeTempMax - config.treeTempOptimum);
            float rising = Smoothstep(minimum, optimum, temperature);
            float falling = 1f - Smoothstep(optimum, maximum, temperature);
            return rising * falling;
        }

        /// <summary>Moisture factor in [0,1] for the plain tree.</summary>
        public static float MoistureFactor(float moisture, SimConfig config)
        {
            return Mathf.Clamp01((moisture - config.treeMoistureMin) / config.treeMoistureSpan);
        }

        /// <summary>Moisture factor in [0,1] for a tree with these genes: tall trees need wetter ground, cones less.</summary>
        public static float MoistureFactor(float moisture, TreeGenes genes, SimConfig config)
        {
            return Mathf.Clamp01((moisture - (config.treeMoistureMin + genes.MoistureShift)) / config.treeMoistureSpan);
        }

        /// <summary>Nutrient factor in [0,1]: a saturating curve with half the effect at the configured nutrient level.</summary>
        public static float NutrientFactor(float nutrients, SimConfig config)
        {
            float available = Mathf.Max(0f, nutrients);
            return available / (available + config.treeNutrientHalf);
        }

        /// <summary>Clearing factor in [0,1]: standing on land that people have built over or farmed starves a tree.</summary>
        public static float ClearingFactor(float cleared, SimConfig config)
        {
            return 1f - config.treeClearingStress * Mathf.Clamp01(cleared);
        }

        /// <summary>Light factor in [0,1]: shaded by neighbours' canopy.</summary>
        public static float LightFactor(float crowding, SimConfig config)
        {
            return 1f / (1f + config.treeShadeStrength * Mathf.Max(0f, crowding));
        }

        /// <summary>Growth rate g in [0,1] for the plain tree: the product of the four factors.</summary>
        public static float GrowthRate(TreeEnvironment environment, SimConfig config)
        {
            return GrowthRate(environment, TreeGenes.Default(config), config);
        }

        /// <summary>Growth rate g for a tree with these genes: the four factors, times its vigor.</summary>
        public static float GrowthRate(TreeEnvironment environment, TreeGenes genes, SimConfig config)
        {
            return TemperatureFactor(environment.Temperature, genes, config)
                 * MoistureFactor(environment.Moisture, genes, config)
                 * NutrientFactor(environment.Nutrients, config)
                 * LightFactor(environment.Crowding, config)
                 * ClearingFactor(environment.Cleared, config)
                 * genes.Vigor(config);
        }

        /// <summary>The change of health for the plain tree over <paramref name="deltaSeconds"/>.</summary>
        public static float HealthChange(TreeEnvironment environment, float growthRate, float deltaSeconds, SimConfig config)
        {
            return HealthChange(environment, TreeGenes.Default(config), growthRate, deltaSeconds, config);
        }

        /// <summary>The change of health over <paramref name="deltaSeconds"/>: negative under stress, positive when thriving.</summary>
        public static float HealthChange(TreeEnvironment environment, TreeGenes genes, float growthRate, float deltaSeconds, SimConfig config)
        {
            float stress = 0f;
            if (growthRate < config.treeStarveBelow)
            {
                stress += config.treeStarveRate;
            }

            if (environment.Moisture < config.treeDroughtMoisture)
            {
                stress += config.treeDroughtRate;
            }

            if (environment.Temperature < config.treeFreezeTemp + genes.FreezeShift)
            {
                stress += config.treeFreezeRate;
            }

            if (environment.Temperature > config.treeHeatTemp)
            {
                stress += config.treeHeatRate;
            }

            float recovery = growthRate > 0.3f ? config.treeRecoverRate : 0f;
            return (recovery - stress) * deltaSeconds;
        }

        /// <summary>Moves the leaf amount and autumn tint one step, for the plain (deciduous) tree.</summary>
        public static void UpdateLeaves(ref float leafAmount, ref float autumn, float temperature, float deltaSeconds, SimConfig config)
        {
            UpdateLeaves(ref leafAmount, ref autumn, temperature, deltaSeconds, TreeGenes.Default(config), config);
        }

        /// <summary>
        /// Moves the leaf amount and autumn tint one step. An evergreen keeps its leaves down to a much colder
        /// temperature (its drop and bud temperatures are lowered by up to 12 deg C) and barely turns colour.
        /// </summary>
        public static void UpdateLeaves(ref float leafAmount, ref float autumn, float temperature, float deltaSeconds, TreeGenes genes, SimConfig config)
        {
            float dropTemp = config.treeLeafDropTemp - 12f * genes.Evergreen;
            float budTemp = config.treeBudTemp - 12f * genes.Evergreen;
            float target = Smoothstep(dropTemp, budTemp, temperature);
            float delta = target - leafAmount;
            float step = config.treeLeafRate * deltaSeconds;
            leafAmount = Mathf.Clamp01(leafAmount + Mathf.Clamp(delta, -step, step));

            if (delta > 0f)
            {
                // New leaves are green: the autumn tint is gone as soon as the tree starts budding.
                autumn = Mathf.Max(0f, autumn - 4f * step);
            }
            else
            {
                float tint = (1f - Smoothstep(budTemp, config.treeAutumnTemp - 12f * genes.Evergreen, temperature)) * (1f - genes.Evergreen);
                autumn = Mathf.Max(autumn, Mathf.Clamp01(tint));
            }
        }

        /// <summary>
        /// Advances the seed clock; returns how many seeds the tree drops in this interval.
        /// The clock only runs while the tree is mature, healthy, in leaf and growing.
        /// </summary>
        public static int SeedsDue(ref float seedClock, bool mature, float health, float leafAmount, float growthRate, float deltaSeconds, SimConfig config)
        {
            if (!mature || health < 0.5f || leafAmount < 0.5f || growthRate < 0.25f)
            {
                return 0;
            }

            seedClock += deltaSeconds;
            int seeds = 0;
            while (seedClock >= config.treeSeedInterval)
            {
                seedClock -= config.treeSeedInterval;
                seeds++;
            }

            return seeds;
        }

        /// <summary>Chance that a plain seed landing in these conditions germinates: low, and proportional to how good the spot is.</summary>
        public static float GerminationChance(TreeEnvironment environment, SimConfig config)
        {
            if (environment.Cleared > config.treeClearedLimit)
            {
                return 0f;   // people have built or farmed here
            }

            return config.treeGerminationChance * GrowthRate(environment, config);
        }

        /// <summary>
        /// Chance that a seed with these genes germinates where it landed. This is where selection happens first:
        /// a seed whose genes do not suit the place (too hot or cold for its temperature optimum, too dry for its
        /// height) rarely takes root.
        /// </summary>
        public static float GerminationChance(TreeEnvironment environment, TreeGenes genes, SimConfig config)
        {
            if (environment.Cleared > config.treeClearedLimit)
            {
                return 0f;   // people have built or farmed here
            }

            return config.treeGerminationChance * GrowthRate(environment, genes, config);
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-6f));
            return t * t * (3f - 2f * t);
        }
    }
}
