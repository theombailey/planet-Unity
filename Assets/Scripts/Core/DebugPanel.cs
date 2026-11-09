// =============================================================================
// DebugPanel
// -----------------------------------------------------------------------------
// Purpose : The F1 developer panel: frame rate, sim clock, time-scale dial,
//           quality preset selector, painterly effect toggles and seed controls.
// Idea    : Drawn with Unity's built-in immediate-mode GUI (OnGUI), so it needs
//           no packages and no scene setup. It only calls public methods on
//           SimRoot / SimClock; it holds no simulation state of its own.
//           Later phases add their sliders and debug-view toggles here.
// Keys    : F1 show/hide, Space pause/resume, R re-seed with a random seed.
// Note    : OnGUI allocates a little memory each frame. That is acceptable
//           because this is a developer tool and is hidden in demo mode; the
//           readout text is still only rebuilt a few times a second.
// =============================================================================

using Terrarium.Interaction;
using Terrarium.Rendering;
using UnityEngine;

namespace Terrarium
{
    /// <summary>Immediate-mode developer panel toggled with F1.</summary>
    public sealed class DebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 300f;
        private const float PanelMargin = 10f;
        private const float ReadoutRefreshSeconds = 0.25f;

        [Tooltip("The simulation this panel displays and controls.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The painterly effect switches shown on the panel (optional).")]
        [SerializeField] private PainterlySettings painterly;

        [Tooltip("Whether the panel is showing when Play starts.")]
        [SerializeField] private bool visible = true;

        private int timeScaleBeforePause = 1;
        private float smoothedFrameSeconds = 1f / 60f;
        private float readoutTimer;
        private string readoutText = string.Empty;
        private string[] presetLabels = System.Array.Empty<string>();
        private string[] timeScaleLabels = System.Array.Empty<string>();

        private void Start()
        {
            if (simRoot == null || !simRoot.enabled)
            {
                enabled = false;
                return;
            }

            presetLabels = new string[simRoot.Presets.Length];
            for (int i = 0; i < presetLabels.Length; i++)
            {
                presetLabels[i] = simRoot.Presets[i].label;
            }

            timeScaleLabels = new string[SimClock.TimeScaleSteps.Length];
            for (int i = 0; i < timeScaleLabels.Length; i++)
            {
                timeScaleLabels[i] = SimClock.TimeScaleSteps[i] == 0 ? "Pause" : SimClock.TimeScaleSteps[i] + "x";
            }
        }

        private void Update()
        {
            if (InputReader.DebugPanelTogglePressed)
            {
                visible = !visible;
            }

            if (InputReader.PausePressed)
            {
                TogglePause();
            }

            if (InputReader.ReseedPressed)
            {
                simRoot.ReseedRandom();
            }

            // Exponential moving average of frame time, so the FPS number is steady enough to read.
            smoothedFrameSeconds = Mathf.Lerp(smoothedFrameSeconds, Time.unscaledDeltaTime, 0.05f);

            readoutTimer -= Time.unscaledDeltaTime;
            if (visible && readoutTimer <= 0f)
            {
                readoutTimer = ReadoutRefreshSeconds;
                readoutText = BuildReadout();
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(PanelMargin, PanelMargin, PanelWidth, Screen.height - 2f * PanelMargin));
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("TERRARIUM DEBUG  (F1 to hide)");
            GUILayout.Label(readoutText);

            GUILayout.Label("Time scale  (Space = pause)");
            SimClock clock = simRoot.Clock;
            int currentScaleIndex = System.Array.IndexOf(SimClock.TimeScaleSteps, clock.TimeScale);
            int pickedScaleIndex = GUILayout.Toolbar(currentScaleIndex, timeScaleLabels);
            if (pickedScaleIndex != currentScaleIndex && pickedScaleIndex >= 0)
            {
                clock.SetTimeScale(SimClock.TimeScaleSteps[pickedScaleIndex]);
            }

            GUILayout.Label("Quality preset");
            int pickedPreset = GUILayout.Toolbar(simRoot.ActivePresetIndex, presetLabels);
            if (pickedPreset != simRoot.ActivePresetIndex)
            {
                simRoot.SelectPreset(pickedPreset);
            }

            if (painterly != null)
            {
                GUILayout.Label("Painterly effects");
                GUILayout.BeginHorizontal();
                painterly.BandsEnabled = GUILayout.Toggle(painterly.BandsEnabled, "Bands");
                painterly.HatchingEnabled = GUILayout.Toggle(painterly.HatchingEnabled, "Hatching");
                painterly.OutlinesEnabled = GUILayout.Toggle(painterly.OutlinesEnabled, "Outlines");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                painterly.PaperEnabled = GUILayout.Toggle(painterly.PaperEnabled, "Paper");
                painterly.BoilEnabled = GUILayout.Toggle(painterly.BoilEnabled, "Boil");
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("World seed  (R = randomise)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Randomise seed"))
            {
                simRoot.ReseedRandom();
            }
            if (GUILayout.Button("Config seed"))
            {
                simRoot.Reseed(simRoot.Config.seed);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void TogglePause()
        {
            SimClock clock = simRoot.Clock;
            if (clock.TimeScale == 0)
            {
                clock.SetTimeScale(timeScaleBeforePause);
            }
            else
            {
                timeScaleBeforePause = clock.TimeScale;
                clock.SetTimeScale(0);
            }
        }

        private string BuildReadout()
        {
            SimClock clock = simRoot.Clock;
            QualityPreset preset = simRoot.ActivePreset;
            float fps = 1f / Mathf.Max(smoothedFrameSeconds, 1e-5f);

            return
                $"FPS {fps:0}   ({smoothedFrameSeconds * 1000f:0.0} ms)\n" +
                $"Year {clock.YearIndex}  day {clock.DayIndex}\n" +
                $"Day phase {clock.DayPhase:0.00}   year phase {clock.YearPhase:0.00}\n" +
                $"Axis lean {clock.LeanRadians * Mathf.Rad2Deg:+0.0;-0.0} deg\n" +
                $"Sim steps this frame {simRoot.StepsLastFrame}\n" +
                $"Seed {simRoot.ActiveSeed}\n" +
                $"Preset {preset.label}: field {preset.fieldResolution}, physarum {preset.physarumAgents:N0},\n" +
                $"  boids {preset.boidCount:N0}, trees {preset.maxTrees}";
        }
    }
}
