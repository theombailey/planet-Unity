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

        [Header("Planet surface and debug brush (see PlanetMeshBuilder, DebugBrush)")]
        [Tooltip("Quads along one edge of each of the 6 cube faces of the planet mesh. 128 gives ~99k vertices.")]
        [Range(16, 256)] public int planetMeshSubdivisions = 128;

        [Tooltip("Radius of the debug brush on the planet, in world units. Adjustable on the F1 panel.")]
        [Range(0.02f, 1f)] public float debugBrushRadius = 0.18f;

        [Tooltip("How fast the debug brush adds to a field, in field units per second at the brush centre.")]
        [Range(0.05f, 10f)] public float debugBrushStrength = 1.5f;

        [Header("Rendering: painterly look (see Painterly.hlsl, InkPaper.shader)")]
        [Tooltip("Colour of ink outlines and hatch strokes.")]
        public Color inkColor = new Color(0.10f, 0.07f, 0.08f);

        [Tooltip("Scales raw lamp light into the 0..1 tone range before banding. Higher = more of the scene reaches the brightest band.")]
        [Range(0.05f, 3f)] public float toneExposure = 0.85f;

        [Tooltip("Number of flat tone steps the lighting is snapped to.")]
        [Range(2, 8)] public int bandCount = 4;

        [Tooltip("Softness of the edge between two tone bands, as a fraction of one band (0 = hard edge).")]
        [Range(0.005f, 0.5f)] public float bandSoftness = 0.07f;

        [Tooltip("How far noise pushes a band edge back and forth, in bands. This is what makes the edges look brushed.")]
        [Range(0f, 1.5f)] public float bandWobble = 0.45f;

        [Tooltip("Size of the band-edge wobble: noise blobs per world unit.")]
        [Range(0.2f, 20f)] public float bandWobbleFrequency = 3.5f;

        [Tooltip("Gap between hatch lines, in pixels at 720p.")]
        [Range(3f, 30f)] public float hatchSpacingPixels = 8f;

        [Tooltip("Direction of the first set of hatch lines, in degrees from horizontal.")]
        [Range(-90f, 90f)] public float hatchAngleDegrees = 35f;

        [Tooltip("How dark a hatch stroke is (0 = invisible, 1 = solid ink).")]
        [Range(0f, 1f)] public float hatchStrength = 0.6f;

        [Tooltip("Width of the inverted-hull outline on props, in pixels at 720p.")]
        [Range(0f, 8f)] public float hullOutlinePixels = 1.8f;

        [Tooltip("Width of the fullscreen edge-detection outline, in pixels at 720p.")]
        [Range(0.5f, 6f)] public float edgeOutlinePixels = 1.2f;

        [Tooltip("Depth jump (relative to distance) that starts an ink line. Lower = more lines.")]
        [Range(0.001f, 0.5f)] public float edgeDepthThreshold = 0.03f;

        [Tooltip("Change in surface direction that starts an ink line. Lower = more lines.")]
        [Range(0.05f, 2f)] public float edgeNormalThreshold = 0.6f;

        [Tooltip("How much outline width varies along the line (0 = constant, 1 = strongly uneven).")]
        [Range(0f, 1f)] public float outlineWobble = 0.6f;

        [Tooltip("Strength of the paper fibre grain (0 = none).")]
        [Range(0f, 0.6f)] public float paperGrainStrength = 0.16f;

        [Tooltip("How much the screen corners are darkened (0 = none).")]
        [Range(0f, 1f)] public float vignetteStrength = 0.5f;

        [Tooltip("How many times a second the line noise re-rolls ('boil'), in Hz. Hand-drawn animation is typically 8 to 12.")]
        [Range(1f, 24f)] public float boilRateHz = 9f;

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
