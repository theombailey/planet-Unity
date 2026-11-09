// =============================================================================
// SimRoot
// -----------------------------------------------------------------------------
// Purpose : The one object in the scene that owns the simulation's shared
//           state: which SimConfig is in use, which QualityPreset is active,
//           the current seed, and the SimClock.
// Idea    : Other systems are handed a SimRoot and read from it, so there are
//           no hidden globals. SimRoot only HOLDS state; the per-frame work of
//           advancing the clock and running the systems in order is SimLoop's.
// Runtime state kept here (not in the SimConfig asset, which is read-only):
//           ActiveSeed, ActivePresetIndex, Clock.
// =============================================================================

using System;
using UnityEngine;

namespace Terrarium
{
    /// <summary>Owns the config, the active quality preset, the seed and the clock.</summary>
    public sealed class SimRoot : MonoBehaviour
    {
        [Tooltip("All tunable simulation parameters.")]
        [SerializeField] private SimConfig config;

        [Tooltip("Quality presets offered at runtime, ordered light to heavy (Low, Medium, High).")]
        [SerializeField] private QualityPreset[] presets = Array.Empty<QualityPreset>();

        [Tooltip("Index into Presets used when Play starts. 1 = Medium.")]
        [SerializeField] private int startingPresetIndex = 1;

        /// <summary>Raised after the active quality preset changes, so systems can rebuild their buffers.</summary>
        public event Action<QualityPreset> PresetChanged;

        /// <summary>Raised after the seed changes, so systems can regenerate the world.</summary>
        public event Action<int> SeedChanged;

        /// <summary>The tunable parameters in use.</summary>
        public SimConfig Config => config;

        /// <summary>The quality presets that can be selected at runtime.</summary>
        public QualityPreset[] Presets => presets;

        /// <summary>Index of the active preset in <see cref="Presets"/>.</summary>
        public int ActivePresetIndex { get; private set; }

        /// <summary>The preset currently deciding agent counts and field resolution.</summary>
        public QualityPreset ActivePreset => presets[ActivePresetIndex];

        /// <summary>The seed the current world was generated from.</summary>
        public int ActiveSeed { get; private set; }

        /// <summary>The simulation clock. Created in Awake.</summary>
        public SimClock Clock { get; private set; }

        /// <summary>Fixed steps the clock asked for in the most recent frame (shown on the debug panel).</summary>
        public int StepsLastFrame { get; private set; }

        private void Awake()
        {
            if (config == null || presets.Length == 0)
            {
                Debug.LogError("SimRoot needs a SimConfig and at least one QualityPreset. Run Terrarium > Create Default Assets, then Terrarium > Rebuild Scene.", this);
                enabled = false;
                return;
            }

            Clock = new SimClock(config);
            ActiveSeed = config.seed;
            ActivePresetIndex = Mathf.Clamp(startingPresetIndex, 0, presets.Length - 1);
        }

        /// <summary>
        /// Feeds one frame of real time into the clock and returns how many fixed steps to run.
        /// Called once per frame by SimLoop, which then runs the systems that many times.
        /// </summary>
        public int AdvanceClock(float realDeltaSeconds)
        {
            StepsLastFrame = Clock.Advance(realDeltaSeconds);
            return StepsLastFrame;
        }

        /// <summary>Switches to another quality preset and tells listeners to rebuild.</summary>
        public void SelectPreset(int index)
        {
            int clamped = Mathf.Clamp(index, 0, presets.Length - 1);
            if (clamped == ActivePresetIndex)
            {
                return;
            }

            ActivePresetIndex = clamped;
            PresetChanged?.Invoke(ActivePreset);
        }

        /// <summary>Starts a new world from the given seed: resets the clock and tells listeners to regenerate.</summary>
        public void Reseed(int seed)
        {
            ActiveSeed = seed;
            Clock.Reset();
            SeedChanged?.Invoke(seed);
        }

        /// <summary>Starts a new world from a seed taken from the system timer (the "randomise seed" button).</summary>
        public void ReseedRandom()
        {
            Reseed(Environment.TickCount);
        }
    }
}
