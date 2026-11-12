// =============================================================================
// SimPresets
// -----------------------------------------------------------------------------
// Purpose : Named starting moods for the world: "Lush", "Harsh" and "Fresh start".
// Idea    : A preset is just a handful of SimConfig numbers changed on top of the
//           code defaults. Applying one first restores every field to its default
//           (so presets never stack on top of each other), then nudges the few
//           numbers that set the climate and food supply. Nothing here runs per
//           frame; the F1 panel calls Apply and then starts a new world.
// Parameters (all SimConfig fields): baseTemperature, evaporation, weatherRate,
//           nutrientCap, peopleArrivalRate, treeGerminationChance, boidEatRate.
// =============================================================================

using UnityEngine;

namespace Terrarium
{
    /// <summary>The named presets the player can pick.</summary>
    public enum SimPresetKind
    {
        /// <summary>Warm and wet, rich soil: everything grows quickly.</summary>
        Lush,

        /// <summary>Cold and dry, poor soil: life has to fight for it.</summary>
        Harsh,

        /// <summary>The code defaults, nothing changed.</summary>
        FreshStart,
    }

    /// <summary>Applies named presets to a <see cref="SimConfig"/>.</summary>
    public static class SimPresets
    {
        /// <summary>Resets the config to its defaults, then applies the preset's changes.</summary>
        /// <param name="config">The config to change (its seed is kept).</param>
        /// <param name="kind">Which preset.</param>
        public static void Apply(SimConfig config, SimPresetKind kind)
        {
            int keepSeed = config.seed;

            SimConfig defaults = ScriptableObject.CreateInstance<SimConfig>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(defaults), config);
            Object.DestroyImmediate(defaults);
            config.seed = keepSeed;

            switch (kind)
            {
                case SimPresetKind.Lush:
                    config.baseTemperature += 5f;
                    config.evaporation *= 0.7f;
                    config.weatherRate *= 1.5f;
                    config.nutrientCap = 1.4f;
                    config.peopleArrivalRate *= 1.5f;
                    config.treeGerminationChance = 0.35f;
                    break;

                case SimPresetKind.Harsh:
                    config.baseTemperature -= 6f;
                    config.evaporation *= 1.6f;
                    config.weatherRate *= 0.6f;
                    config.nutrientCap = 0.6f;
                    config.peopleArrivalRate *= 0.5f;
                    config.treeGerminationChance = 0.15f;
                    break;

                case SimPresetKind.FreshStart:
                default:
                    break;
            }
        }
    }
}
