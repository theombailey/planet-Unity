// =============================================================================
// DebugBrush
// -----------------------------------------------------------------------------
// Purpose : A development brush: while the field debug view is on, hold the
//           left mouse button over the planet to paint into the field and
//           channel being viewed (hold Shift to erase). Used to check that
//           painting is smooth across cube-face edges and corners. The real
//           tools (Phase 6) reuse FieldBrush the same way.
// Idea    : Each frame the mouse ray is intersected with the planet
//           (SurfacePicker), the hit is converted to a direction in the planet's
//           own frame, and FieldBrush adds a soft blob there. The amount added
//           is strength x frame time, so holding the button builds up smoothly
//           and the result does not depend on the frame rate.
// Equations: amount = +- strength * dt          (added at the brush centre)
//            angularRadius = brushRadius / planetRadius
// =============================================================================

using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>Paints into the viewed field with the mouse while the debug view is on.</summary>
    public sealed class DebugBrush : MonoBehaviour
    {
        [Tooltip("Provides the field textures to paint into.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("Says which field and channel is being viewed, and whether the view is on.")]
        [SerializeField] private FieldDebugView view;

        [Tooltip("The planet's transform (position and current spin/tilt).")]
        [SerializeField] private Transform planet;

        [Tooltip("The simulation: planet radius and brush defaults.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("Brush.compute.")]
        [SerializeField] private ComputeShader brushShader;

        [Tooltip("Optional: the F1 panel, so clicks on the panel do not paint the planet behind it.")]
        [SerializeField] private DebugPanel panel;

        [Tooltip("Optional: the tool controller; the debug brush stays idle while a tool is in hand or a dial is turning.")]
        [SerializeField] private ToolController tools;

        private FieldBrush brush;
        private Camera viewCamera;

        /// <summary>Brush radius in world units.</summary>
        public float RadiusWorld { get; set; }

        /// <summary>Amount added per second at the brush centre.</summary>
        public float Strength { get; set; }

        private void Start()
        {
            if (fieldSet == null || view == null || planet == null || simRoot == null || brushShader == null)
            {
                Debug.LogError("DebugBrush is missing a reference.", this);
                enabled = false;
                return;
            }

            brush = new FieldBrush(brushShader);
            RadiusWorld = simRoot.Config.debugBrushRadius;
            Strength = simRoot.Config.debugBrushStrength;
        }

        private void Update()
        {
            if (!view.Showing || !InputReader.PaintHeld)
            {
                return;
            }

            if (tools != null && (tools.HoldingTool || tools.TurningDial))
            {
                return;
            }

            Vector2 pointer = InputReader.PointerPosition;
            if (panel != null && panel.IsPointerOver(pointer))
            {
                return;
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                {
                    return;
                }
            }

            Ray ray = viewCamera.ScreenPointToRay(pointer);
            float planetRadius = simRoot.Config.planetRadius;
            if (!SurfacePicker.TryPickLocalDirection(ray, planet, planetRadius, out Vector3 localDirection))
            {
                return;
            }

            float amount = (InputReader.ShiftHeld ? -1f : 1f) * Strength * Time.unscaledDeltaTime;
            PaintAtLocalDirection(localDirection, RadiusWorld, amount);
        }

        /// <summary>
        /// Adds <paramref name="amount"/> at a point on the planet, to the field and channel currently viewed
        /// (in RGB mode, the red channel). Also used by the editor's DevBridge to paint at chosen points.
        /// </summary>
        /// <param name="localDirection">The point, as a unit vector in the planet's local space.</param>
        /// <param name="radiusWorld">Brush radius in world units.</param>
        /// <param name="amount">Added at the brush centre.</param>
        public void PaintAtLocalDirection(Vector3 localDirection, float radiusWorld, float amount)
        {
            int channel = Mathf.Min(view.ViewedChannel, 3);
            var perChannel = Vector4.zero;
            perChannel[channel] = amount;

            float planetRadius = simRoot.Config.planetRadius;
            Field field = fieldSet.Get(view.ViewedField);
            brush.Apply(field.Current, localDirection, FieldBrush.AngularRadius(radiusWorld, planetRadius), perChannel, -50f, 50f);
        }

        /// <summary>Immediate-mode controls, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            if (!view.Showing)
            {
                return;
            }

            GUILayout.Label("Brush: hold LMB on planet, Shift = erase");
            GUILayout.Label($"Radius {RadiusWorld:0.00}");
            RadiusWorld = GUILayout.HorizontalSlider(RadiusWorld, 0.02f, 1f);
            GUILayout.Label($"Strength {Strength:0.00} /s");
            Strength = GUILayout.HorizontalSlider(Strength, 0.05f, 10f);
        }
    }
}
