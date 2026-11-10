// =============================================================================
// TreeLife
// -----------------------------------------------------------------------------
// Purpose : The rules that turn LOCAL CONDITIONS into what a tree does: how fast
//           it grows, whether it stays healthy or declines toward death, when its
//           leaves bud, colour and fall, and when it drops seeds. Pure functions of
//           numbers (no scene, no GPU), so each rule can be tested on its own.
// Idea    : Nothing here is scripted by place or time. A tree samples the world at
//           its root (temperature, moisture, nutrients, trail, shade from
//           neighbours) and every rule is a smooth response to those numbers.
//           Forests, thinning in winter and regrowth in spring are what comes out.
//
// Growth rate g in [0,1] is a product of four factors, each in [0,1]. A tree grows only if ALL of
//           them are decent, which is Liebig's law of the minimum.
//   temperature  smooth bump: 0 below tMin, 1 at tOpt, back to 0 above tMax
//                f = smoothstep(tMin, tOpt, T) * (1 - smoothstep(tOpt, tMax, T))
//   moisture     f = saturate( (M - mMin) / mSpan )
//   nutrients    f = n_eff / (n_eff + nHalf)            (a saturating curve: half-effect at nHalf)
//                n_eff = n * (1 + mycorrhizaBoost * saturate(trail))
//                The fungal network (trail) makes a tree's uptake more efficient, as mycorrhizae do.
//   light        f = 1 / (1 + shade * crowding)         crowding = canopy density of the OTHERS
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

        /// <summary>Fungal network (trail) strength at the root.</summary>
        public float Trail;

        /// <summary>Canopy density of the OTHER trees at the root (0 = open sky).</summary>
        public float Crowding;
    }

    /// <summary>Growth, health, leaf and seed rules for one tree, as pure functions.</summary>
    public static class TreeLife
    {
        /// <summary>Temperature factor in [0,1]: 0 outside the tolerable range, 1 at the optimum.</summary>
        public static float TemperatureFactor(float temperature, SimConfig config)
        {
            float rising = Smoothstep(config.treeTempMin, config.treeTempOptimum, temperature);
            float falling = 1f - Smoothstep(config.treeTempOptimum, config.treeTempMax, temperature);
            return rising * falling;
        }

        /// <summary>Moisture factor in [0,1].</summary>
        public static float MoistureFactor(float moisture, SimConfig config)
        {
            return Mathf.Clamp01((moisture - config.treeMoistureMin) / config.treeMoistureSpan);
        }

        /// <summary>Nutrient factor in [0,1], with the fungal network making uptake more efficient.</summary>
        public static float NutrientFactor(float nutrients, float trail, SimConfig config)
        {
            float effective = Mathf.Max(0f, nutrients) * (1f + config.treeMycorrhizaBoost * Mathf.Clamp01(trail));
            return effective / (effective + config.treeNutrientHalf);
        }

        /// <summary>Light factor in [0,1]: shaded by neighbours' canopy.</summary>
        public static float LightFactor(float crowding, SimConfig config)
        {
            return 1f / (1f + config.treeShadeStrength * Mathf.Max(0f, crowding));
        }

        /// <summary>Growth rate g in [0,1]: the product of the four factors.</summary>
        public static float GrowthRate(TreeEnvironment environment, SimConfig config)
        {
            return TemperatureFactor(environment.Temperature, config)
                 * MoistureFactor(environment.Moisture, config)
                 * NutrientFactor(environment.Nutrients, environment.Trail, config)
                 * LightFactor(environment.Crowding, config);
        }

        /// <summary>The change of health over <paramref name="deltaSeconds"/>: negative under stress, positive when thriving.</summary>
        public static float HealthChange(TreeEnvironment environment, float growthRate, float deltaSeconds, SimConfig config)
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

            if (environment.Temperature < config.treeFreezeTemp)
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

        /// <summary>Moves the leaf amount and autumn tint one step.</summary>
        public static void UpdateLeaves(ref float leafAmount, ref float autumn, float temperature, float deltaSeconds, SimConfig config)
        {
            float target = Smoothstep(config.treeLeafDropTemp, config.treeBudTemp, temperature);
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
                float tint = 1f - Smoothstep(config.treeBudTemp, config.treeAutumnTemp, temperature);
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

        /// <summary>Chance that a seed landing in these conditions germinates: low, and proportional to how good the spot is.</summary>
        public static float GerminationChance(TreeEnvironment environment, SimConfig config)
        {
            return config.treeGerminationChance * GrowthRate(environment, config);
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-6f));
            return t * t * (3f - 2f * t);
        }
    }
}
