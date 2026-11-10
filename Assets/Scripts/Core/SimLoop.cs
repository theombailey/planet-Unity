// =============================================================================
// SimLoop
// -----------------------------------------------------------------------------
// Purpose : The single orchestrator of the simulation. Once per frame it asks
//           the clock how many fixed steps to run, then runs every system that
//           many times, always in the same order.
// Idea    : One place decides the order things happen in, so the simulation is
//           reproducible and easy to follow. Systems do not have their own
//           Update; they expose Step(dt, time) and are called from here.
//
// Tick order per step (later phases fill in the rest):
//     1. Climate   - insolation, temperature, moisture, weathering
//     2. Physarum  - sense / move / deposit, trail diffusion, nutrient transport
//     3. Trees     - growth, seeding, death, canopy splat
//     4. Boids     - steering, energy, seed carrying                               (Phase 8)
//     5. Stats     - GPU reductions for audio and the HUD                          (Phase 9)
//
// Time    : each step i of a frame gets its own sim time,
//               t_i = t_end - (steps - 1 - i) * h
//           so systems that depend on the time of day see a smooth sweep even
//           when 16 steps run in one frame.
// Real time: Time.unscaledDeltaTime is used so the sim's speed is set only by
//           the SimClock dial, never by Unity's own Time.timeScale.
// =============================================================================

using Terrarium.Boids;
using Terrarium.Climate;
using Terrarium.Physarum;
using Terrarium.Trees;
using UnityEngine;

namespace Terrarium
{
    /// <summary>Runs the simulation systems in a fixed order, a whole number of fixed steps per frame.</summary>
    public sealed class SimLoop : MonoBehaviour
    {
        [Tooltip("The simulation state and clock.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("Step 1: terrain and climate.")]
        [SerializeField] private ClimateController climate;

        [Tooltip("Step 2: physarum agents, trail and nutrient transport.")]
        [SerializeField] private PhysarumSystem physarum;

        [Tooltip("Step 3: trees (time is accumulated here; the work is done in their own bounded ticks).")]
        [SerializeField] private TreeManager trees;

        [Tooltip("Step 4: boids (steering, energy, seed carrying).")]
        [SerializeField] private BoidSystem boids;

        private void Update()
        {
            if (simRoot == null || simRoot.Clock == null || climate == null || !climate.enabled || physarum == null || !physarum.enabled)
            {
                return;
            }

            int steps = simRoot.AdvanceClock(Time.unscaledDeltaTime);
            float stepSeconds = simRoot.Clock.FixedStepSeconds;
            double endTime = simRoot.Clock.SimTimeSeconds;

            for (int i = 0; i < steps; i++)
            {
                double stepTime = endTime - (steps - 1 - i) * (double)stepSeconds;
                climate.Step(stepSeconds, stepTime);
                physarum.Step(stepSeconds);
                if (trees != null)
                {
                    trees.Step(stepSeconds);
                }

                if (boids != null && boids.enabled)
                {
                    boids.Step(stepSeconds);
                }
            }
        }
    }
}
