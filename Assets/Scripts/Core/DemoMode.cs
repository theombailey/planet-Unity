// =============================================================================
// DemoMode
// -----------------------------------------------------------------------------
// Purpose : A clean mode for showing the game off (and for recording the video):
//           press F3 to hide the debug panel, glide the camera in from far away,
//           and then let it wander on its own until you touch the mouse.
// Idea    : The camera is steered by calling CameraRig.SnapTo each frame with a
//           pose computed from the clock: a slow swing left and right, a gentle
//           nod up and down, and an intro that eases the distance from the far
//           limit to a comfortable framing (smoothstep). Every tour period the
//           camera flies down to stand on the surface at a random place, stays
//           for a while, then flies back up to the desk view.
// Equations:
//   intro   distance = lerp(far, near, smoothstep(t / introSeconds))
//   swing   yaw = A * sin(2*pi*t / period)
//   smoothstep(x) = 3x^2 - 2x^3
// Keys    : F3 start / stop. Any mouse button or scroll stops the auto-tour.
// Parameters: introSeconds, tourPeriodSeconds, surfaceStaySeconds (inspector).
// =============================================================================

using UnityEngine;
using UnityEngine.InputSystem;
using Terrarium.Interaction;

namespace Terrarium
{
    /// <summary>Hides the debug panel, plays an intro camera move and runs an optional auto-tour.</summary>
    public sealed class DemoMode : MonoBehaviour
    {
        [Tooltip("The simulation (config limits for the camera).")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The camera rig to steer.")]
        [SerializeField] private CameraRig cameraRig;

        [Tooltip("The debug panel to hide.")]
        [SerializeField] private DebugPanel panel;

        [Tooltip("Seconds for the opening camera glide.")]
        [SerializeField] private float introSeconds = 8f;

        [Tooltip("Seconds between trips down to the surface.")]
        [SerializeField] private float tourPeriodSeconds = 45f;

        [Tooltip("Seconds spent standing on the surface during a trip.")]
        [SerializeField] private float surfaceStaySeconds = 15f;

        private bool active;
        private float time;
        private float nextTrip;
        private float tripEnds;
        private bool onTrip;

        /// <summary>True while demo mode is on.</summary>
        public bool IsActive => active;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                SetActive(!active);
            }

            if (!active)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && time > introSeconds &&
                (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.scroll.ReadValue().sqrMagnitude > 0.01f))
            {
                // The player took over: keep the clean screen but stop steering.
                active = false;
                return;
            }

            time += Time.unscaledDeltaTime;

            if (!onTrip)
            {
                SteerDesk();
                if (time > nextTrip)
                {
                    onTrip = true;
                    tripEnds = time + surfaceStaySeconds;
                    cameraRig.FlyToSurface(Random.onUnitSphere);
                }
            }
            else if (time > tripEnds)
            {
                onTrip = false;
                nextTrip = time + tourPeriodSeconds;
                cameraRig.FlyToDesk();
            }
        }

        private void SetActive(bool on)
        {
            active = on;
            if (panel != null)
            {
                panel.SetVisible(!on);
            }

            if (on)
            {
                time = 0f;
                onTrip = false;
                nextTrip = introSeconds + tourPeriodSeconds;
            }
        }

        private void SteerDesk()
        {
            SimConfig config = simRoot.Config;
            float x = Mathf.Clamp01(time / introSeconds);
            float eased = x * x * (3f - 2f * x);
            float near = Mathf.Lerp(config.deskDistanceMin, config.deskDistanceMax, 0.35f);
            float distance = Mathf.Lerp(config.deskDistanceMax, near, eased);

            float yaw = 18f * Mathf.Sin(2f * Mathf.PI * time / 40f);
            float pitch = config.deskPitchDefaultDegrees + 6f * Mathf.Sin(2f * Mathf.PI * time / 27f);
            cameraRig.SnapTo(yaw, pitch, distance);
        }
    }
}
