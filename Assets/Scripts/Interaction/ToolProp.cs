// =============================================================================
// ToolProp
// -----------------------------------------------------------------------------
// Purpose : One tool lying on the desk. It remembers its place, glows with a
//           thicker ink outline when the mouse is over it, and when picked up it
//           follows a target position smoothly, swinging a little as it moves.
// Idea    : ToolController decides WHERE the prop should be (on the desk, or
//           hovering over the planet at the cursor). The prop itself just moves
//           toward that target with critically damped springs (no overshoot, no
//           jerk) and tilts a little in the direction it is travelling, like a
//           held object with weight. When released, its target is home again.
//
// Equations: position   spring toward target (omega = followOmega)
//            tilt       euler(velocity.z * k, 0, -velocity.x * k), clamped to +-25 degrees,
//                       smoothed by its own spring, applied on top of the resting rotation
// =============================================================================

using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>A tool prop that can be hovered, picked up and put back.</summary>
    public sealed class ToolProp : MonoBehaviour
    {
        private const float FollowOmega = 14f;
        private const float TiltOmega = 10f;
        private const float TiltPerSpeed = 14f;
        private const float MaxTiltDegrees = 25f;
        private const float HighlightedOutline = 4.5f;

        private static readonly int OutlineScaleId = Shader.PropertyToID("_OutlineScale");

        [Tooltip("Which tool this prop is.")]
        [SerializeField] private ToolKind kind;

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private SpringVector3 position;
        private SpringVector3 tilt;
        private Vector3 target;
        private bool held;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private bool highlighted;

        /// <summary>Which tool this prop is.</summary>
        public ToolKind Kind => kind;

        /// <summary>True while the prop is in the player's hand.</summary>
        public bool IsHeld => held;

        private void Awake()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            position = new SpringVector3(homePosition);
            tilt = new SpringVector3(Vector3.zero);
            target = homePosition;
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        /// <summary>Picks the prop up: from now on it follows the targets it is given.</summary>
        public void PickUp()
        {
            held = true;
        }

        /// <summary>Moves the held prop's goal. The prop eases toward it.</summary>
        public void SetTarget(Vector3 worldPosition)
        {
            target = worldPosition;
        }

        /// <summary>Puts the prop back: it eases home and rests where it started.</summary>
        public void PutBack()
        {
            held = false;
            target = homePosition;
        }

        /// <summary>Turns the thick hover outline on or off.</summary>
        public void SetHighlighted(bool on)
        {
            if (on == highlighted)
            {
                return;
            }

            highlighted = on;
            foreach (Renderer propRenderer in renderers)
            {
                propRenderer.GetPropertyBlock(block);
                block.SetFloat(OutlineScaleId, on ? HighlightedOutline : 1f);
                propRenderer.SetPropertyBlock(block);
            }
        }

        private void Update()
        {
            float deltaSeconds = Time.unscaledDeltaTime;
            position.Step(target, FollowOmega, deltaSeconds);

            // Lean the way it is moving; ease back to upright when still.
            Vector3 lean = held
                ? new Vector3(
                    Mathf.Clamp(position.Velocity.z * TiltPerSpeed, -MaxTiltDegrees, MaxTiltDegrees),
                    0f,
                    Mathf.Clamp(-position.Velocity.x * TiltPerSpeed, -MaxTiltDegrees, MaxTiltDegrees))
                : Vector3.zero;
            tilt.Step(lean, TiltOmega, deltaSeconds);

            transform.SetPositionAndRotation(position.Value, Quaternion.Euler(tilt.Value) * homeRotation);
        }
    }
}
