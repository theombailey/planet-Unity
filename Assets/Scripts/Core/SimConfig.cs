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
    }
}
