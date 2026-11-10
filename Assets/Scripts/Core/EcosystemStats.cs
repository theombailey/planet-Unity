// =============================================================================
// EcosystemStats
// -----------------------------------------------------------------------------
// Purpose : A small snapshot of how the whole ecosystem is doing, refreshed a few
//           times a second. It is what the debug panel shows, what the stability
//           check judges, and what the generative audio listens to.
// Idea    : Most numbers are averages over the whole planet computed on the GPU
//           (Stats.compute) and read back asynchronously; the counts of living
//           things come from the systems' own population counters.
// =============================================================================

namespace Terrarium
{
    /// <summary>A snapshot of the ecosystem's key numbers.</summary>
    public struct EcosystemStats
    {
        /// <summary>Whole sim years elapsed.</summary>
        public int Year;

        /// <summary>How far through the year, 0..1 (see SimClock.YearPhase).</summary>
        public float YearPhase;

        /// <summary>Average soil nutrients per texel.</summary>
        public float MeanNutrients;

        /// <summary>Average organic matter per texel.</summary>
        public float MeanOrganic;

        /// <summary>Average tree canopy density per texel.</summary>
        public float MeanCanopy;

        /// <summary>Average temperature per texel, deg C.</summary>
        public float MeanTemperature;

        /// <summary>Average fungal trail per texel (the size and activity of the network).</summary>
        public float MeanTrail;

        /// <summary>Share of texels that are land, 0..1.</summary>
        public float LandFraction;

        /// <summary>Living slime-mould agents.</summary>
        public int PhysarumAlive;

        /// <summary>Trees standing (including dead ones that have not collapsed).</summary>
        public int TreesStanding;

        /// <summary>Trees alive.</summary>
        public int TreesAlive;

        /// <summary>Average leaf cover of living trees, 0 (bare) to 1 (full leaf).</summary>
        public float MeanLeafCover;

        /// <summary>Living boids.</summary>
        public int BoidsAlive;

        /// <summary>Average boid speed, planet radii per sim second.</summary>
        public float BoidMeanSpeed;

        /// <summary>Average boid energy.</summary>
        public float BoidMeanEnergy;

        /// <summary>Boids currently carrying a seed.</summary>
        public int BoidsCarryingSeeds;

        /// <summary>Boids born in the last step.</summary>
        public int BoidsBorn;

        /// <summary>Boids that died in the last step.</summary>
        public int BoidsDied;

        /// <summary>True once the first GPU readback has arrived.</summary>
        public bool Valid;
    }
}
