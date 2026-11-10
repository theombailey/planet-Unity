// =============================================================================
// CameraRig
// -----------------------------------------------------------------------------
// Purpose : Controls the camera as ONE continuous system with two modes:
//             DESK VIEW    orbit and zoom around the floating planet (limited
//                          orbit, so the desk stays in frame)
//             SURFACE MODE the camera stands over the ground, locked to the
//                          planet's rotation, and walks over it
//           Scrolling in past the closest desk distance flies the camera down to
//           the surface point under the cursor; scrolling out past the highest
//           surface height flies it back.
//
// Idea    : Every control moves a TARGET; a critically damped spring (SpringFloat)
//           eases the real value toward it, so nothing ever pops.
//           The two modes each define a full camera pose. A "blend" number
//           (0 = desk, 1 = surface), itself spring-smoothed, mixes the two poses:
//               position = lerp(deskPosition, surfacePosition, blend)
//               rotation = slerp(deskRotation, surfaceRotation, blend)
//           so the flight between them is just the blend moving from 0 to 1,
//           a smooth glide. While in surface mode the pose is rebuilt every
//           frame from the planet's CURRENT rotation (see SurfaceNavigation), so
//           the ground stays still under the camera while the lamp sweeps over
//           the sky. That is why this runs after PlanetMotion (execution order 100).
//
// Desk view   RMB-drag orbit (yaw/pitch clamped), scroll zooms: each notch removes a
//             fixed fraction of the distance (multiplicative, so a notch feels the
//             same near and far).
// Surface     scroll changes height the same way; WASD walks (faster when higher);
//             LMB-drag pans like grabbing the map (unless a tool is held or the field
//             view is painting); RMB-drag looks around.
// Equations   see SurfaceNavigation.cs for the standpoint, heading and pose maths.
// Parameters  all in SimConfig under "Camera: desk view" and "Camera: surface mode".
// Note        Camera motion uses real (unscaled) time on purpose: the view must stay
//             responsive when the simulation is paused or running at 16x.
// =============================================================================

using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>Desk-view and surface-mode camera with spring smoothing.</summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public sealed class CameraRig : MonoBehaviour
    {
        private const float MinSurfacePitchDegrees = 4f;
        private const float MaxSurfacePitchDegrees = 88f;

        [Tooltip("The simulation whose SimConfig holds the camera settings.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The planet. Its position and rotation place the surface-mode camera.")]
        [SerializeField] private Transform planet;

        private Camera controlledCamera;

        // Desk view.
        private float yawTarget;
        private float pitchTarget;
        private float distanceTarget;
        private SpringFloat yaw;
        private SpringFloat pitch;
        private SpringFloat distance;

        // Mode blend: 0 = desk view, 1 = surface mode.
        private SpringFloat blend;
        private float blendTarget;

        // Surface mode, in the planet's own frame.
        private Vector3 surfacePosition = Vector3.up;
        private Vector3 surfaceHeading = Vector3.forward;
        private float heightTarget;
        private float surfacePitchTarget;
        private SpringFloat height;
        private SpringFloat surfacePitch;

        /// <summary>True when the camera is in (or flying toward) surface mode.</summary>
        public bool InSurfaceMode => blendTarget > 0.5f;

        /// <summary>When true, left-drag does not pan the surface (a tool or the debug brush is using the button).</summary>
        public bool LeftDragBlocked { get; set; }

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

        /// <summary>
        /// Jumps the desk-view camera straight to a pose with no easing (clamped to the allowed ranges), leaving
        /// surface mode if it was active. Used by the editor's DevBridge to take close-up captures.
        /// </summary>
        public void SnapTo(float yawDegrees, float pitchDegrees, float distanceUnits)
        {
            SimConfig config = simRoot.Config;
            yawTarget = Mathf.Clamp(yawDegrees, -config.deskYawLimitDegrees, config.deskYawLimitDegrees);
            pitchTarget = Mathf.Clamp(pitchDegrees, config.deskPitchMinDegrees, config.deskPitchMaxDegrees);
            distanceTarget = Mathf.Clamp(distanceUnits, config.deskDistanceMin, config.deskDistanceMax);
            yaw = new SpringFloat(yawTarget);
            pitch = new SpringFloat(pitchTarget);
            distance = new SpringFloat(distanceTarget);
            blendTarget = 0f;
            blend = new SpringFloat(0f);
        }

        /// <summary>
        /// Jumps to surface mode over a point with no easing. Used by the editor's DevBridge to take surface captures.
        /// </summary>
        /// <param name="localDirection">Standpoint, a unit vector in the planet's local space.</param>
        /// <param name="heightUnits">Height above the surface, in world units.</param>
        /// <param name="pitchDegrees">How far the camera looks down from the horizon.</param>
        public void SnapToSurface(Vector3 localDirection, float heightUnits, float pitchDegrees)
        {
            SimConfig config = simRoot.Config;
            surfacePosition = localDirection.normalized;
            surfaceHeading = SurfaceNavigation.HeadingNear(surfacePosition, Vector3.up);
            heightTarget = Mathf.Clamp(heightUnits, config.surfaceHeightMin, config.surfaceHeightMax);
            surfacePitchTarget = Mathf.Clamp(pitchDegrees, MinSurfacePitchDegrees, MaxSurfacePitchDegrees);
            height = new SpringFloat(heightTarget);
            surfacePitch = new SpringFloat(surfacePitchTarget);
            blendTarget = 1f;
            blend = new SpringFloat(1f);
        }

        private void Start()
        {
            if (simRoot == null || simRoot.Config == null || planet == null)
            {
                Debug.LogError("CameraRig needs a SimRoot with a SimConfig and the planet.", this);
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
            blend = new SpringFloat(0f);

            heightTarget = config.surfaceHeightDefault;
            surfacePitchTarget = config.surfacePitchDefaultDegrees;
            height = new SpringFloat(heightTarget);
            surfacePitch = new SpringFloat(surfacePitchTarget);
        }

        private void LateUpdate()
        {
            SimConfig config = simRoot.Config;
            float deltaSeconds = Time.unscaledDeltaTime;

            ReadInput(config, deltaSeconds);

            yaw.Step(yawTarget, config.cameraSpringOmega, deltaSeconds);
            pitch.Step(pitchTarget, config.cameraSpringOmega, deltaSeconds);
            distance.Step(distanceTarget, config.cameraSpringOmega, deltaSeconds);
            height.Step(heightTarget, config.cameraSpringOmega, deltaSeconds);
            surfacePitch.Step(surfacePitchTarget, config.cameraSpringOmega, deltaSeconds);
            blend.Step(blendTarget, config.surfaceBlendOmega, deltaSeconds);

            DeskPose(config, yaw.Value, pitch.Value, distance.Value, out Vector3 position, out Quaternion rotation);

            float mix = Mathf.Clamp01(blend.Value);
            if (mix > 1e-4f)
            {
                SurfaceNavigation.CameraPose(
                    planet.rotation, planet.position, config.planetRadius, surfacePosition, surfaceHeading,
                    height.Value, surfacePitch.Value * Mathf.Deg2Rad, out Vector3 surfaceCameraPosition, out Quaternion surfaceCameraRotation);

                position = Vector3.Lerp(position, surfaceCameraPosition, mix);
                rotation = Quaternion.Slerp(rotation, surfaceCameraRotation, mix);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void ReadInput(SimConfig config, float deltaSeconds)
        {
            int notches = InputReader.ScrollNotches;
            Vector2 drag = InputReader.PointerDelta;

            if (blendTarget < 0.5f)
            {
                ReadDeskInput(config, notches, drag);
            }
            else
            {
                ReadSurfaceInput(config, notches, drag, deltaSeconds);
            }
        }

        private void ReadDeskInput(SimConfig config, int notches, Vector2 drag)
        {
            if (InputReader.OrbitHeld)
            {
                yawTarget += drag.x * config.orbitDegreesPerPixel;
                pitchTarget -= drag.y * config.orbitDegreesPerPixel;
            }

            if (notches > 0 && distanceTarget <= config.deskDistanceMin * 1.02f)
            {
                EnterSurface(config);
                return;
            }

            if (notches != 0)
            {
                distanceTarget *= Mathf.Pow(1f - config.zoomFractionPerNotch, notches);
            }

            yawTarget = Mathf.Clamp(yawTarget, -config.deskYawLimitDegrees, config.deskYawLimitDegrees);
            pitchTarget = Mathf.Clamp(pitchTarget, config.deskPitchMinDegrees, config.deskPitchMaxDegrees);
            distanceTarget = Mathf.Clamp(distanceTarget, config.deskDistanceMin, config.deskDistanceMax);
        }

        private void ReadSurfaceInput(SimConfig config, int notches, Vector2 drag, float deltaSeconds)
        {
            if (notches < 0 && heightTarget >= config.surfaceHeightMax * 0.98f)
            {
                blendTarget = 0f;   // zoomed out past the top: fly back to the desk
                return;
            }

            if (notches != 0)
            {
                heightTarget *= Mathf.Pow(1f - config.zoomFractionPerNotch, notches);
            }

            heightTarget = Mathf.Clamp(heightTarget, config.surfaceHeightMin, config.surfaceHeightMax);

            // Walk with WASD: slower near the ground, faster from high up.
            Vector2 walk = InputReader.MoveAxis;
            float walkArc = config.surfaceMoveSpeed * height.Value * deltaSeconds;
            if (walk != Vector2.zero)
            {
                SurfaceNavigation.Move(ref surfacePosition, ref surfaceHeading, walk.y * walkArc, walk.x * walkArc);
            }

            // Left-drag grabs the ground: dragging right moves the ground right, so the camera moves left.
            if (InputReader.PaintHeld && !LeftDragBlocked && drag != Vector2.zero)
            {
                float dragArc = config.surfaceDragSpeed * height.Value;
                SurfaceNavigation.Move(ref surfacePosition, ref surfaceHeading, -drag.y * dragArc, -drag.x * dragArc);
            }

            // Right-drag looks around.
            if (InputReader.OrbitHeld && drag != Vector2.zero)
            {
                SurfaceNavigation.Turn(surfacePosition, ref surfaceHeading, drag.x * config.surfaceLookDegreesPerPixel * Mathf.Deg2Rad);
                surfacePitchTarget = Mathf.Clamp(surfacePitchTarget - drag.y * config.surfaceLookDegreesPerPixel, MinSurfacePitchDegrees, MaxSurfacePitchDegrees);
            }
        }

        /// <summary>Starts the smooth flight down to the surface over a given point (for a demo fly-in or the editor's DevBridge).</summary>
        /// <param name="localDirection">Where to land, a unit vector in the planet's local space.</param>
        public void FlyToSurface(Vector3 localDirection)
        {
            SimConfig config = simRoot.Config;
            Quaternion inverse = Quaternion.Inverse(planet.rotation);

            surfacePosition = localDirection.normalized;
            surfaceHeading = SurfaceNavigation.HeadingNear(surfacePosition, inverse * transform.forward);
            heightTarget = config.surfaceHeightDefault;
            surfacePitchTarget = config.surfacePitchDefaultDegrees;
            height = new SpringFloat(heightTarget);
            surfacePitch = new SpringFloat(surfacePitchTarget);
            blendTarget = 1f;
        }

        /// <summary>Starts the smooth flight back up to the desk view.</summary>
        public void FlyToDesk()
        {
            blendTarget = 0f;
        }

        // Chooses the surface point under the cursor (or, if the cursor is off the planet, the point facing the camera)
        // and starts the flight down to it. The heading starts as the way the camera was already facing, so the
        // picture does not spin as it descends.
        private void EnterSurface(SimConfig config)
        {
            Ray ray = controlledCamera.ScreenPointToRay(InputReader.PointerPosition);

            Vector3 landing;
            if (!SurfacePicker.TryPickLocalDirection(ray, planet, config.planetRadius, out landing))
            {
                landing = (Quaternion.Inverse(planet.rotation) * (transform.position - planet.position)).normalized;
            }

            FlyToSurface(landing);
        }
    }
}
