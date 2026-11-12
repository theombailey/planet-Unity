// =============================================================================
// QualityPreset
// -----------------------------------------------------------------------------
// Purpose : The numbers that decide how HEAVY the simulation is, kept apart
//           from SimConfig, which decides how the simulation BEHAVES.
// Idea    : The demo machine's GPU is unknown, so every scale parameter (how
//           many agents, how fine the fields are) lives here. Switching preset
//           changes cost, not rules: the same local rules run on fewer or more
//           agents. Low / Medium / High assets are created by the editor tool
//           Terrarium > Create Default Assets, and can be switched at runtime
//           from the F1 debug panel.
// =============================================================================

using UnityEngine;

namespace Terrarium
{
    /// <summary>Scale parameters for one quality level (Low / Medium / High).</summary>
    [CreateAssetMenu(fileName = "Quality_Custom", menuName = "Terrarium/Quality Preset")]
    public sealed class QualityPreset : ScriptableObject
    {
        [Tooltip("Name shown on the debug panel button.")]
        public string label = "Custom";

        [Tooltip("Texels along one edge of each of the 6 cube-sphere faces (N). Fields are N x N x 6. Use 128, 256 or 512.")]
        [Range(64, 1024)] public int fieldResolution = 256;

        [Tooltip("Size of the pool of households (the physarum walkers). The living population emerges below this cap.")]
        [Range(500, 100_000)] public int physarumAgents = 5_000;

        [Tooltip("Size of the boid pool. The living population emerges below this cap.")]
        [Range(2_000, 100_000)] public int boidCount = 15_000;

        [Tooltip("Most trees alive at once. At the cap, seeds fail to germinate.")]
        [Range(10, 2_000)] public int maxTrees = 300;

        [Tooltip("Most branch segments one tree may grow.")]
        [Range(50, 10_000)] public int maxSegmentsPerTree = 1_200;

        [Tooltip("Vertices in each ring of a branch cylinder. More = rounder branches, more triangles.")]
        [Range(3, 12)] public int meshRingSides = 6;
    }
}
