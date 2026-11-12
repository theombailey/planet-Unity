// =============================================================================
// ConsoleControl
// -----------------------------------------------------------------------------
// Purpose : A push button: on the console (pause, new world, the three climate presets) and
//           on the tech board (one per technology: click to research it).
// Idea    : The ToolController finds the button under the mouse the same way it finds
//           tools and dials, highlights it with a thick outline, and calls Press() on a
//           click. A press sinks the button for a moment. The pause button also has an
//           indicator light that stays lit while the simulation is paused; the others
//           flash briefly to show they were pressed.
// Research buttons (tech board): the indicator light shows the state (off = locked, dim = can be
//           researched, pulsing = being researched, steady and bright = learnt), a bar under the button
//           fills as knowledge goes in, and a small lamp lights when the technology's eureka has happened.
// Parameters: kind (what it does), the cap that sinks, the indicator light.
// =============================================================================

using Terrarium.Tech;
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

        /// <summary>Research one technology (the tech board).</summary>
        Research,
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

        [Tooltip("Research buttons: the people's technology.")]
        [SerializeField] private TechSystem techSystem;

        [Tooltip("Research buttons: which technology this button researches.")]
        [SerializeField] private TechId techId;

        [Tooltip("Research buttons: the bar that fills as knowledge goes in (scaled along x from its left end).")]
        [SerializeField] private Transform progressBar;

        [Tooltip("Research buttons: the lamp that lights when the eureka has happened.")]
        [SerializeField] private Renderer eurekaLamp;

        [Tooltip("The way the cap sinks when pressed, in the button's own frame.")]
        [SerializeField] private Vector3 sinkDirection = Vector3.down;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Vector3 capRest;
        private string hintCache = string.Empty;
        private float hintTime = -10f;
        private int lastState = -1;
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
                if (kind == ConsoleControlKind.Research)
                {
                    // Rebuilt a few times a second, not every frame it is read.
                    if (Time.unscaledTime - hintTime > 0.25f)
                    {
                        hintTime = Time.unscaledTime;
                        hintCache = ResearchHint();
                    }

                    return hintCache;
                }

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

        /// <summary>Sets the references of a research button (used by the scene builder when it creates the board in code).</summary>
        public void ConfigureResearch(TechId id, SimRoot root, TechSystem system, Transform capTransform, Renderer indicatorRenderer, Transform bar, Renderer lamp, Vector3 sink)
        {
            Configure(ConsoleControlKind.Research, root, capTransform, indicatorRenderer);
            techId = id;
            techSystem = system;
            progressBar = bar;
            eurekaLamp = lamp;
            sinkDirection = sink;
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
                case ConsoleControlKind.Research:
                    PressResearch();
                    break;
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
                cap.localPosition = capRest + sinkDirection * (SinkDepth * sink);
            }

            if (kind == ConsoleControlKind.Research)
            {
                UpdateResearchVisuals();
                return;
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

        // ------------------------------------------------------------ research buttons

        // The state of this technology: 0 locked, 1 can be researched, 2 being researched, 3 learnt.
        private int ResearchState()
        {
            TechTree tree = techSystem.Tree;
            if (tree.IsLearnt(techId))
            {
                return 3;
            }

            if (tree.Researching != null && tree.Researching.Id == techId)
            {
                return 2;
            }

            return tree.IsAvailable(techId) ? 1 : 0;
        }

        private void PressResearch()
        {
            if (techSystem == null || simRoot == null)
            {
                return;
            }

            TechTree tree = techSystem.Tree;
            if (ResearchState() == 2)
            {
                tree.StopResearch();
            }
            else
            {
                tree.StartResearch(techId, simRoot.Config);
            }
        }

        private string ResearchHint()
        {
            if (techSystem == null || simRoot == null)
            {
                return "Technology";
            }

            TechTree tree = techSystem.Tree;
            TechDefinition definition = tree.Definitions[(int)techId];
            switch (ResearchState())
            {
                case 3:
                    return $"{definition.Name}  -  learnt: {definition.Effect}";
                case 0:
                    return $"{definition.Name}  -  needs {tree.Definitions[(int)definition.Requires[0]].Name} first";
                case 2:
                    return $"{definition.Name}  -  researching, {tree.Progress01(techId, simRoot.Config):P0} done  -  click to stop";
            }

            string eureka = tree.HasEureka(techId) ? "eureka: half price" : $"cheaper once: {definition.EurekaText}";
            return $"{definition.Name}  -  click to research: {definition.Effect}  ({tree.Cost(techId, simRoot.Config):0} knowledge; {eureka})";
        }

        // Lights, bar and cap tint follow the state of the technology.
        private void UpdateResearchVisuals()
        {
            if (techSystem == null || simRoot == null)
            {
                return;
            }

            TechTree tree = techSystem.Tree;
            int state = ResearchState();

            float glow = 0f;
            switch (state)
            {
                case 1: glow = 0.15f; break;
                case 2: glow = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f)); break;
                case 3: glow = 1f; break;
            }

            if (indicator != null)
            {
                indicator.GetPropertyBlock(block);
                block.SetFloat(GlowId, glow);
                indicator.SetPropertyBlock(block);
            }

            if (progressBar != null)
            {
                float progress = tree.Progress01(techId, simRoot.Config);
                Vector3 scale = progressBar.localScale;
                scale.x = Mathf.Max(0.001f, progress);
                progressBar.localScale = scale;
            }

            if (eurekaLamp != null)
            {
                bool lit = tree.HasEureka(techId);
                eurekaLamp.GetPropertyBlock(block);
                block.SetFloat(GlowId, lit ? (state == 3 ? 0.4f : 1f) : 0f);
                eurekaLamp.SetPropertyBlock(block);
            }

            // A locked technology's cap is dimmed.
            if (state != lastState && capRenderers != null)
            {
                lastState = state;
                float dim = state == 0 ? 0.35f : (state == 1 ? 0.75f : 1f);
                foreach (Renderer capRenderer in capRenderers)
                {
                    capRenderer.GetPropertyBlock(block);
                    Color colour = capRenderer.sharedMaterial != null ? capRenderer.sharedMaterial.GetColor(BaseColorId) : Color.white;
                    block.SetColor(BaseColorId, new Color(colour.r * dim, colour.g * dim, colour.b * dim, 1f));
                    capRenderer.SetPropertyBlock(block);
                }
            }
        }
    }
}
