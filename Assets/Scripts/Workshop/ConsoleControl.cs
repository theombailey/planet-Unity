// =============================================================================
// ConsoleControl
// -----------------------------------------------------------------------------
// Purpose : A push button on the console: pause, new world, and the three climate
//           presets. It replaces the same buttons that were on the F1 panel.
// Idea    : The ToolController finds the button under the mouse the same way it finds
//           tools and dials, highlights it with a thick outline, and calls Press() on a
//           click. A press sinks the button for a moment. The pause button also has an
//           indicator light that stays lit while the simulation is paused; the others
//           flash briefly to show they were pressed.
// Parameters: kind (what it does), the cap that sinks, the indicator light.
// =============================================================================

using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>What a console button does.</summary>
    public enum ConsoleControlKind
    {
        /// <summary>Pause or resume the simulation.</summary>
        Pause,

        /// <summary>Start a new world with a random seed.</summary>
        NewWorld,

        /// <summary>Warm, wet, rich world (same seed).</summary>
        PresetLush,

        /// <summary>The default climate (same seed).</summary>
        PresetFresh,

        /// <summary>Cold, dry, poor world (same seed).</summary>
        PresetHarsh,
    }

    /// <summary>A push button with an indicator light.</summary>
    public sealed class ConsoleControl : MonoBehaviour
    {
        private const float SinkSeconds = 0.14f;
        private const float SinkDepth = 0.012f;
        private const float FlashSeconds = 0.6f;
        private const float HighlightedOutline = 4.5f;

        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int OutlineScaleId = Shader.PropertyToID("_OutlineScale");

        [Tooltip("What this button does.")]
        [SerializeField] private ConsoleControlKind kind;

        [Tooltip("The simulation.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The part that moves down when pressed.")]
        [SerializeField] private Transform cap;

        [Tooltip("The indicator light.")]
        [SerializeField] private Renderer indicator;

        private Vector3 capRest;
        private float pressedAt = -10f;
        private bool highlighted;
        private Renderer[] capRenderers;
        private MaterialPropertyBlock block;

        /// <summary>What this button does.</summary>
        public ConsoleControlKind Kind => kind;

        /// <summary>A short description for the hover hint.</summary>
        public string Hint
        {
            get
            {
                switch (kind)
                {
                    case ConsoleControlKind.Pause: return "Pause button  -  click to stop or start time";
                    case ConsoleControlKind.NewWorld: return "New world button  -  click for a new random planet";
                    case ConsoleControlKind.PresetLush: return "Lush button  -  warm, wet, rich world";
                    case ConsoleControlKind.PresetFresh: return "Fresh start button  -  the default world";
                    default: return "Harsh button  -  cold, dry, poor world";
                }
            }
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (cap != null)
            {
                capRest = cap.localPosition;
                capRenderers = cap.GetComponentsInChildren<Renderer>();
            }
        }

        /// <summary>Sets the references (used by the scene builder when it creates the button in code).</summary>
        public void Configure(ConsoleControlKind controlKind, SimRoot root, Transform capTransform, Renderer indicatorRenderer)
        {
            kind = controlKind;
            simRoot = root;
            cap = capTransform;
            indicator = indicatorRenderer;
        }

        /// <summary>Turns the thick hover outline on or off.</summary>
        public void SetHighlighted(bool on)
        {
            if (on == highlighted || capRenderers == null)
            {
                return;
            }

            highlighted = on;
            foreach (Renderer capRenderer in capRenderers)
            {
                capRenderer.GetPropertyBlock(block);
                block.SetFloat(OutlineScaleId, on ? HighlightedOutline : 1f);
                capRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Presses the button.</summary>
        public void Press()
        {
            if (simRoot == null)
            {
                return;
            }

            pressedAt = Time.unscaledTime;
            switch (kind)
            {
                case ConsoleControlKind.Pause:
                    simRoot.Clock.TogglePause();
                    break;
                case ConsoleControlKind.NewWorld:
                    simRoot.ReseedRandom();
                    break;
                case ConsoleControlKind.PresetLush:
                    SimPresets.Apply(simRoot.Config, SimPresetKind.Lush);
                    simRoot.Reseed(simRoot.Config.seed);
                    break;
                case ConsoleControlKind.PresetFresh:
                    SimPresets.Apply(simRoot.Config, SimPresetKind.FreshStart);
                    simRoot.Reseed(simRoot.Config.seed);
                    break;
                case ConsoleControlKind.PresetHarsh:
                    SimPresets.Apply(simRoot.Config, SimPresetKind.Harsh);
                    simRoot.Reseed(simRoot.Config.seed);
                    break;
            }
        }

        private void Update()
        {
            float sinceCap = Time.unscaledTime - pressedAt;
            if (cap != null)
            {
                float sink = sinceCap < SinkSeconds ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(sinceCap / SinkSeconds)) : 0f;
                cap.localPosition = capRest + Vector3.down * (SinkDepth * sink);
            }

            if (indicator != null)
            {
                bool lit = kind == ConsoleControlKind.Pause
                    ? simRoot != null && simRoot.Clock.TimeScale == 0f
                    : sinceCap < FlashSeconds;
                indicator.GetPropertyBlock(block);
                block.SetFloat(GlowId, lit ? 1f : 0f);
                indicator.SetPropertyBlock(block);
            }
        }
    }
}
