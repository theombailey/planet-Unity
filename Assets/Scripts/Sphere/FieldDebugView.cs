// =============================================================================
// FieldDebugView
// -----------------------------------------------------------------------------
// Purpose : Lets you look at any simulation field painted on the planet, with
//           optional face-boundary lines and a texel grid. Toggle with F2 or
//           the F1 panel.
// Idea    : While the view is on, the planet's normal material is swapped for
//           the Terrarium/FieldDebug material, which is pointed at the chosen
//           field texture every frame (a ping-pong field's "Current" texture
//           changes as it swaps, so it is re-bound each frame). When the view
//           is turned off the original material is put back.
// Also    : Exposes which field and channel are being viewed, so DebugBrush can
//           paint into exactly what you are looking at.
// =============================================================================

using UnityEngine;
using Terrarium.Interaction;

namespace Terrarium.Sphere
{
    /// <summary>Shows a simulation field on the planet for debugging.</summary>
    public sealed class FieldDebugView : MonoBehaviour
    {
        private static readonly int FieldTextureId = Shader.PropertyToID("_Field");
        private static readonly int ChannelId = Shader.PropertyToID("_Channel");
        private static readonly int RangeMinId = Shader.PropertyToID("_RangeMin");
        private static readonly int RangeMaxId = Shader.PropertyToID("_RangeMax");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int ShowBoundariesId = Shader.PropertyToID("_ShowBoundaries");
        private static readonly int ShowGridId = Shader.PropertyToID("_ShowGrid");

        private static readonly string[] FieldNames = { "Terrain", "Climate", "Soil", "Trail", "Brush" };
        private static readonly string[] ChannelNames = { "R", "G", "B", "A", "RGB" };

        [Tooltip("Provides the field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("The planet's renderer, whose material is swapped while the view is on.")]
        [SerializeField] private Renderer planetRenderer;

        [Tooltip("The Terrarium/FieldDebug shader. Stored as a reference so it is included in builds.")]
        [SerializeField] private Shader debugShader;

        private Material debugMaterial;
        private Material normalMaterial;
        private bool showing;

        /// <summary>True while the debug view replaces the planet's normal look.</summary>
        public bool Showing => showing;

        /// <summary>Which field is being viewed.</summary>
        public FieldId ViewedField { get; private set; } = FieldId.Terrain;

        /// <summary>Which channel is being viewed (0..3 = R,G,B,A; 4 = RGB).</summary>
        public int ViewedChannel { get; private set; }

        /// <summary>Lowest value of the colour ramp.</summary>
        public float RangeMin { get; private set; }

        /// <summary>Highest value of the colour ramp.</summary>
        public float RangeMax { get; private set; } = 1f;

        /// <summary>Draw lines where the six cube faces meet.</summary>
        public bool ShowBoundaries { get; set; } = true;

        /// <summary>Draw a texel grid.</summary>
        public bool ShowGrid { get; set; }

        private void Start()
        {
            if (fieldSet == null || planetRenderer == null || debugShader == null)
            {
                Debug.LogError("FieldDebugView needs a FieldSet, the planet renderer and the FieldDebug shader.", this);
                enabled = false;
                return;
            }

            normalMaterial = planetRenderer.sharedMaterial;
            debugMaterial = new Material(debugShader) { name = "FieldDebug (runtime)" };
            ResetRange();
        }

        private void OnDestroy()
        {
            if (debugMaterial != null)
            {
                Destroy(debugMaterial);
            }
        }

        private void Update()
        {
            if (InputReader.FieldViewTogglePressed)
            {
                SetShowing(!showing);
            }

            if (showing)
            {
                ApplyMaterial();
            }
        }

        /// <summary>Turns the debug view on or off, swapping the planet's material.</summary>
        public void SetShowing(bool value)
        {
            showing = value;
            planetRenderer.sharedMaterial = showing ? debugMaterial : normalMaterial;
            if (showing)
            {
                ApplyMaterial();
            }
        }

        /// <summary>Chooses which field to view and resets the colour range to a sensible default for it.</summary>
        public void SelectField(FieldId field)
        {
            ViewedField = field;
            ResetRange();
            ApplyMaterial();
        }

        /// <summary>Chooses which channel to view: 0..3 = R,G,B,A; 4 = RGB.</summary>
        public void SelectChannel(int channel)
        {
            ViewedChannel = Mathf.Clamp(channel, 0, 4);
            ResetRange();
            ApplyMaterial();
        }

        // Points the debug material at the chosen field. Also run every frame, because a ping-pong
        // field's Current texture changes whenever it swaps.
        private void ApplyMaterial()
        {
            Field field = fieldSet.Get(ViewedField);
            debugMaterial.SetTexture(FieldTextureId, field.Current);
            debugMaterial.SetFloat(ChannelId, ViewedChannel);
            debugMaterial.SetFloat(RangeMinId, RangeMin);
            debugMaterial.SetFloat(RangeMaxId, RangeMax);
            debugMaterial.SetFloat(ResolutionId, fieldSet.Resolution);
            debugMaterial.SetFloat(ShowBoundariesId, ShowBoundaries ? 1f : 0f);
            debugMaterial.SetFloat(ShowGridId, ShowGrid ? 1f : 0f);
        }

        /// <summary>Sets the values mapped to the two ends of the colour ramp.</summary>
        public void SetRange(float min, float max)
        {
            RangeMin = min;
            RangeMax = max;
            ApplyMaterial();
        }

        /// <summary>Immediate-mode controls, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            if (fieldSet == null || debugMaterial == null)
            {
                return;
            }

            GUILayout.Label("Field view  (F2)");
            bool wantShowing = GUILayout.Toggle(showing, "Show field on planet");
            if (wantShowing != showing)
            {
                SetShowing(wantShowing);
            }

            if (!showing)
            {
                return;
            }

            int pickedField = GUILayout.Toolbar((int)ViewedField, FieldNames);
            if (pickedField != (int)ViewedField)
            {
                SelectField((FieldId)pickedField);
            }

            int pickedChannel = GUILayout.Toolbar(ViewedChannel, ChannelNames);
            if (pickedChannel != ViewedChannel)
            {
                SelectChannel(pickedChannel);
            }

            GUILayout.Label($"Range  {RangeMin:0.00} to {RangeMax:0.00}");
            RangeMin = GUILayout.HorizontalSlider(RangeMin, -50f, 50f);
            RangeMax = GUILayout.HorizontalSlider(RangeMax, -50f, 50f);

            GUILayout.BeginHorizontal();
            ShowBoundaries = GUILayout.Toggle(ShowBoundaries, "Face edges");
            ShowGrid = GUILayout.Toggle(ShowGrid, "Texel grid");
            GUILayout.EndHorizontal();
        }

        // Sets the colour ramp to the usual range of what is being viewed. The sliders adjust it.
        private void ResetRange()
        {
            RangeMin = 0f;
            RangeMax = 1f;

            if (ViewedChannel == 0 && ViewedField == FieldId.Terrain)
            {
                RangeMin = -1f;   // height: sea floor -1, shoreline 0, peaks 1
            }
            else if (ViewedChannel == 0 && ViewedField == FieldId.Climate)
            {
                RangeMin = -30f;  // temperature in deg C
                RangeMax = 45f;
            }
        }
    }
}
