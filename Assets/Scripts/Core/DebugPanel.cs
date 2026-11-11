// =============================================================================
// DebugPanel
// -----------------------------------------------------------------------------
// Purpose : The F1 developer panel: frame rate, sim clock, time-scale dial,
//           quality preset selector, painterly effect toggles, terrain and
//           climate controls, field debug view and brush controls, and seed controls.
// Idea    : Drawn with Unity's built-in immediate-mode GUI (OnGUI), so it needs
//           no packages and no scene setup. It only calls public methods on
//           SimRoot / SimClock; it holds no simulation state of its own.
//           Later phases add their sliders and debug-view toggles here.
// Keys    : F1 show/hide, Space pause/resume, R re-seed with a random seed.
// Note    : OnGUI allocates a little memory each frame. That is acceptable
//           because this is a developer tool and is hidden in demo mode; the
//           readout text is still only rebuilt a few times a second.
// =============================================================================

using Terrarium.Boids;
using Terrarium.Climate;
using Terrarium.Interaction;
using Terrarium.Physarum;
using Terrarium.Rendering;
using Terrarium.Sphere;
using Terrarium.Trees;
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

        [Tooltip("The field debug view controls shown on the panel (optional).")]
        [SerializeField] private FieldDebugView fieldView;

        [Tooltip("The debug brush controls shown on the panel (optional).")]
        [SerializeField] private DebugBrush debugBrush;

        [Tooltip("The terrain and climate controls shown on the panel (optional).")]
        [SerializeField] private ClimateController climate;

        [Tooltip("The physarum population readout shown on the panel (optional).")]
        [SerializeField] private PhysarumSystem physarum;

        [Tooltip("The tree readout shown on the panel (optional).")]
        [SerializeField] private TreeManager trees;

        [Tooltip("The boid readout shown on the panel (optional).")]
        [SerializeField] private BoidSystem boids;

        [Tooltip("The ecosystem summary shown at the top of the panel (optional).")]
        [SerializeField] private StatsSystem stats;

        [Tooltip("The generative sound, for the volume slider (optional).")]
        [SerializeField] private Terrarium.Audio.GenerativeAudio sound;

        [Tooltip("The sky, atmosphere and clouds, for their on/off switches (optional).")]
        [SerializeField] private Terrarium.Rendering.AtmosphereController atmosphere;

        [Tooltip("The automatic multi-seed health check (optional).")]
        [SerializeField] private StabilityRunner stability;

        [Tooltip("Whether the panel is showing when Play starts.")]
        [SerializeField] private bool visible = true;

        private Vector2 scrollPosition;
        private float timeScaleBeforePause = 1f;
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

        /// <summary>Shows or hides the panel (demo mode hides it).</summary>
        public void SetVisible(bool show)
        {
            visible = show;
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

            GUILayout.BeginArea(PanelRect());
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("TERRARIUM DEBUG  (F1 to hide)");
            GUILayout.Label(readoutText);

            if (stats != null)
            {
                GUILayout.Label("Ecosystem");
                stats.OnDebugGui();
            }

            if (stability != null)
            {
                stability.OnDebugGui();
            }

            if (sound != null)
            {
                sound.OnDebugGui();
            }

            if (atmosphere != null)
            {
                atmosphere.OnDebugGui();
            }

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

            if (climate != null)
            {
                GUILayout.Label("Terrain and climate");
                climate.OnDebugGui();
            }

            if (physarum != null)
            {
                GUILayout.Label("Physarum network");
                physarum.OnDebugGui();
            }

            if (trees != null)
            {
                GUILayout.Label("Forest");
                trees.OnDebugGui();
            }

            if (boids != null)
            {
                GUILayout.Label("Boids");
                boids.OnDebugGui();
            }

            if (fieldView != null)
            {
                fieldView.OnDebugGui();
            }

            if (debugBrush != null)
            {
                debugBrush.OnDebugGui();
            }

            GUILayout.Label("Presets  (change the climate, then start a new world)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Lush"))
            {
                SimPresets.Apply(simRoot.Config, SimPresetKind.Lush);
                simRoot.Reseed(simRoot.Config.seed);
            }
            if (GUILayout.Button("Harsh"))
            {
                SimPresets.Apply(simRoot.Config, SimPresetKind.Harsh);
                simRoot.Reseed(simRoot.Config.seed);
            }
            if (GUILayout.Button("Fresh start"))
            {
                SimPresets.Apply(simRoot.Config, SimPresetKind.FreshStart);
                simRoot.Reseed(simRoot.Config.seed);
            }
            GUILayout.EndHorizontal();

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
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>True when a screen position (pixels, origin bottom-left) is over the visible panel.</summary>
        public bool IsPointerOver(Vector2 screenPosition)
        {
            if (!visible)
            {
                return false;
            }

            // OnGUI rectangles have their origin at the top-left, screen positions at the bottom-left.
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return PanelRect().Contains(guiPoint);
        }

        private static Rect PanelRect()
        {
            return new Rect(PanelMargin, PanelMargin, PanelWidth, Screen.height - 2f * PanelMargin);
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
