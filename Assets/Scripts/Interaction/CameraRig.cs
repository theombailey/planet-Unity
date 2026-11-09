// =============================================================================
// CameraRig
// -----------------------------------------------------------------------------
// Purpose : Controls the camera. Phase 1 implements DESK VIEW: the camera
//           orbits a focus point just below the planet, within limits that
//           keep the desk in frame, and scroll zooms in and out. Phase 6 adds
//           zoom-to-surface and surface mode to this same class, so it stays
//           one continuous camera system rather than two.
//
// Idea    : The camera's pose is described by three numbers: yaw, pitch and
//           distance. Input changes the TARGET for each number; a critically
//           damped spring (SpringFloat) then eases the actual value toward its
//           target, which is what makes every movement smooth with no pops.
//
// Equations:
//     RMB drag : yawTarget   += dx * orbitDegreesPerPixel     (clamped +-limit)
//                pitchTarget -= dy * orbitDegreesPerPixel     (clamped min..max)
//     scroll   : distanceTarget *= (1 - zoomFractionPerNotch) ^ notches
//                (multiplicative, so each notch feels the same size near or far)
//     pose     : rotation = Euler(pitch, yaw, 0)
//                position = focus - rotation * forward * distance
//
// Parameters: all in SimConfig under "Camera: desk view".
// Note    : Camera motion uses real (unscaled) time on purpose: the view must
//           stay responsive when the simulation is paused or running at 16x.
// =============================================================================

using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>Desk-view orbit and zoom camera with spring smoothing.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [Tooltip("The simulation whose SimConfig holds the camera settings.")]
        [SerializeField] private SimRoot simRoot;

        private Camera controlledCamera;
        private float yawTarget;
        private float pitchTarget;
        private float distanceTarget;
        private SpringFloat yaw;
        private SpringFloat pitch;
        private SpringFloat distance;

        /// <summary>
        /// The desk-view camera pose for a given yaw, pitch (degrees) and distance (world units).
        /// Shared with the scene builder so the saved scene and the running game agree.
        /// </summary>
        public static void DeskPose(SimConfig config, float yawDegrees, float pitchDegrees, float distance, out Vector3 position, out Quaternion rotation)
        {
            Vector3 focus = config.PlanetRestCentre + Vector3.down * config.deskFocusDrop;
            rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            position = focus - rotation * Vector3.forward * distance;
        }

        private void Start()
        {
            if (simRoot == null || simRoot.Config == null)
            {
                Debug.LogError("CameraRig needs a SimRoot with a SimConfig.", this);
                enabled = false;
                return;
            }

            SimConfig config = simRoot.Config;
            controlledCamera = GetComponent<Camera>();
            controlledCamera.fieldOfView = config.cameraFieldOfView;
            controlledCamera.nearClipPlane = config.cameraNearClip;

            yawTarget = 0f;
            pitchTarget = config.deskPitchDefaultDegrees;
            distanceTarget = config.deskDistanceDefault;
            yaw = new SpringFloat(yawTarget);
            pitch = new SpringFloat(pitchTarget);
            distance = new SpringFloat(distanceTarget);
        }

        private void LateUpdate()
        {
            SimConfig config = simRoot.Config;
            ReadInput(config);

            float deltaSeconds = Time.unscaledDeltaTime;
            yaw.Step(yawTarget, config.cameraSpringOmega, deltaSeconds);
            pitch.Step(pitchTarget, config.cameraSpringOmega, deltaSeconds);
            distance.Step(distanceTarget, config.cameraSpringOmega, deltaSeconds);

            DeskPose(config, yaw.Value, pitch.Value, distance.Value, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
        }

        private void ReadInput(SimConfig config)
        {
            if (InputReader.OrbitHeld)
            {
                Vector2 drag = InputReader.PointerDelta;
                yawTarget += drag.x * config.orbitDegreesPerPixel;
                pitchTarget -= drag.y * config.orbitDegreesPerPixel;
            }

            int notches = InputReader.ScrollNotches;
            if (notches != 0)
            {
                distanceTarget *= Mathf.Pow(1f - config.zoomFractionPerNotch, notches);
            }

            yawTarget = Mathf.Clamp(yawTarget, -config.deskYawLimitDegrees, config.deskYawLimitDegrees);
            pitchTarget = Mathf.Clamp(pitchTarget, config.deskPitchMinDegrees, config.deskPitchMaxDegrees);
            distanceTarget = Mathf.Clamp(distanceTarget, config.deskDistanceMin, config.deskDistanceMax);
        }
    }
}
