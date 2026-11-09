// =============================================================================
// SimConfig
// -----------------------------------------------------------------------------
// Purpose : The single home for every tunable number in the simulation.
// Idea    : No magic numbers in code. Each system reads its parameters from
//           this asset, so a value can be changed in the Inspector (or from the
//           F1 debug panel) without touching code, and so a marker reading the
//           code can find every constant in one place.
// Layout  : Fields are grouped by system with [Header]. Each has a [Tooltip]
//           that states its meaning and its units. Later phases add their own
//           groups (climate, physarum, trees, boids, audio) as they are built.
// Note    : This asset is treated as READ-ONLY at runtime. Values that change
//           while playing (the active seed, the time scale) live on SimRoot and
//           SimClock, so pressing Play never dirties the asset on disk.
// =============================================================================

using UnityEngine;

namespace Terrarium
{
    /// <summary>All tunable simulation parameters, grouped by system.</summary>
    [CreateAssetMenu(fileName = "SimConfig", menuName = "Terrarium/Sim Config")]
    public sealed class SimConfig : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Seed for every random choice in the sim. Same seed + same config = same world.")]
        public int seed = 12345;

        [Header("Scale (world units; the planet radius is the yardstick)")]
        [Tooltip("Radius of the planet at sea level, in world units.")]
        [Range(0.25f, 4f)] public float planetRadius = 1.0f;

        [Tooltip("Gap between the desk surface and the lowest point of the planet, in world units.")]
        [Range(0.1f, 2f)] public float planetHoverHeight = 0.4f;

        [Tooltip("Height of the planet's gentle up-and-down bob, in world units (peak deviation).")]
        [Range(0f, 0.2f)] public float bobAmplitude = 0.02f;

        [Tooltip("Time for one full bob cycle, in real seconds.")]
        [Range(1f, 30f)] public float bobPeriodSeconds = 6f;

        [Tooltip("Height of the smallest mature tree, in world units.")]
        [Range(0.005f, 0.1f)] public float treeHeightMin = 0.02f;

        [Tooltip("Height of the tallest mature tree, in world units.")]
        [Range(0.01f, 0.2f)] public float treeHeightMax = 0.06f;

        [Tooltip("Body length of one boid, in world units.")]
        [Range(0.001f, 0.02f)] public float boidLength = 0.004f;

        [Tooltip("Camera near clip plane, in world units. Small so the camera can get right down to the surface.")]
        [Range(0.0005f, 0.05f)] public float cameraNearClip = 0.001f;

        [Header("Desk scene (world units; the desk top is the plane y = 0)")]
        [Tooltip("Desk width (x), thickness (y) and depth (z), in world units.")]
        public Vector3 deskSize = new Vector3(6.4f, 0.12f, 3.8f);

        [Tooltip("World position of the lamp's bulb. The lamp is the only strong light; it shines in all directions from here.")]
        public Vector3 lampLightPosition = new Vector3(-1.75f, 2.55f, -0.45f);

        [Tooltip("Colour of the lamp light (warm).")]
        public Color lampColor = new Color(1.0f, 0.82f, 0.58f);

        [Tooltip("Lamp brightness. Light falls off with the square of distance, so this is the brightness at 1 world unit.")]
        [Range(0f, 100f)] public float lampIntensity = 9f;

        [Tooltip("Distance at which the lamp light is cut off, in world units.")]
        [Range(2f, 30f)] public float lampRange = 12f;

        [Tooltip("Dim, cool fill light that keeps the night side of the planet readable.")]
        public Color ambientColor = new Color(0.20f, 0.25f, 0.36f);

        [Tooltip("Colour of the dark lab behind the desk.")]
        public Color backgroundColor = new Color(0.045f, 0.040f, 0.036f);

        [Tooltip("Tools are laid out on an ellipse on the desk around the planet. x = half-width, y = half-depth, in world units.")]
        public Vector2 toolArcRadii = new Vector2(2.2f, 1.35f);

        [Tooltip("Radius of the decorative levitation ring drawn on the desk under the planet, in world units.")]
        [Range(0.1f, 2f)] public float levitationRingRadius = 0.8f;

        [Header("Camera: desk view (see CameraRig)")]
        [Tooltip("Vertical field of view, in degrees.")]
        [Range(20f, 80f)] public float cameraFieldOfView = 40f;

        [Tooltip("How far below the planet's centre the camera aims, in world units. Keeps the desk in frame.")]
        [Range(0f, 1f)] public float deskFocusDrop = 0.45f;

        [Tooltip("How far the camera may orbit left or right of straight-on, in degrees.")]
        [Range(0f, 180f)] public float deskYawLimitDegrees = 55f;

        [Tooltip("Lowest camera elevation above the horizontal, in degrees.")]
        [Range(0f, 45f)] public float deskPitchMinDegrees = 6f;

        [Tooltip("Highest camera elevation above the horizontal, in degrees.")]
        [Range(10f, 89f)] public float deskPitchMaxDegrees = 50f;

        [Tooltip("Camera elevation when Play starts, in degrees.")]
        [Range(0f, 89f)] public float deskPitchDefaultDegrees = 20f;

        [Tooltip("Closest desk-view distance from the focus point, in world units.")]
        [Range(1.2f, 5f)] public float deskDistanceMin = 1.8f;

        [Tooltip("Farthest desk-view distance from the focus point, in world units.")]
        [Range(3f, 20f)] public float deskDistanceMax = 8.5f;

        [Tooltip("Desk-view distance when Play starts, in world units.")]
        [Range(1.2f, 20f)] public float deskDistanceDefault = 6.4f;

        [Tooltip("Orbit speed: degrees of rotation per pixel of mouse drag.")]
        [Range(0.02f, 1f)] public float orbitDegreesPerPixel = 0.2f;

        [Tooltip("Fraction of the current distance removed by one scroll notch (0.12 = 12% closer).")]
        [Range(0.02f, 0.5f)] public float zoomFractionPerNotch = 0.12f;

        [Tooltip("Stiffness of the camera's smoothing spring, in rad/s. Higher = snappier. Settles in about 4.7 / omega seconds.")]
        [Range(1f, 40f)] public float cameraSpringOmega = 9f;

        [Header("Time (see SimClock)")]
        [Tooltip("Length of one planet day (one full spin) at 1x speed, in real seconds.")]
        [Range(4f, 240f)] public float dayLengthSeconds = 24f;

        [Tooltip("Length of one year (one full summer-winter cycle), in planet days.")]
        [Range(2f, 60f)] public float yearLengthDays = 10f;

        [Tooltip("Axial tilt epsilon, in degrees. Larger tilt = stronger seasons. Earth is about 23.")]
        [Range(0f, 45f)] public float axialTiltDegrees = 23f;

        [Tooltip("Size of one simulation step, in sim seconds. Every system advances by exactly this much per step.")]
        [Range(1f / 240f, 1f / 15f)] public float fixedStepSeconds = 1f / 60f;

        [Tooltip("Most sim steps allowed in one rendered frame. If more are owed the sim runs slow instead of freezing the frame. 16 lets the 16x dial keep up at 60 fps.")]
        [Range(1, 64)] public int maxStepsPerFrame = 16;

        /// <summary>World position of the planet's centre when it is not bobbing: above the desk by hover height + radius.</summary>
        public Vector3 PlanetRestCentre => new Vector3(0f, planetHoverHeight + planetRadius, 0f);
    }
}
