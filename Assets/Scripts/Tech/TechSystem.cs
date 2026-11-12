// =============================================================================
// TechSystem
// -----------------------------------------------------------------------------
// Purpose : Puts the tech tree into the scene: feeds it the passage of sim time and the
//           world's numbers, resets it for each new world, and offers the rules that the
//           people's simulation, the houses and the ground shader live by.
// Idea    : The tree itself (TechTree) is pure logic. This component only supplies what it
//           cannot know: how much SIM time has passed (so it stops when time is paused and
//           runs fast when time runs fast), the latest EcosystemStats, and a signal that a
//           new world began. It also sets a few shader globals the ground shader reads
//           (road tier, whether irrigation is known) so the ground can look the part.
// Parameters: none of its own; knowledgeRate and eurekaDiscount come from SimConfig.
// =============================================================================

using Terrarium.Physarum;
using UnityEngine;

namespace Terrarium.Tech
{
    /// <summary>Runs the people's technology.</summary>
    public sealed class TechSystem : MonoBehaviour
    {
        private const float LongestStepSeconds = 5f;

        private static readonly int RoadTierId = Shader.PropertyToID("_RoadTier");
        private static readonly int IrrigationOnId = Shader.PropertyToID("_IrrigationOn");
        private static readonly int IrrigationNearId = Shader.PropertyToID("_IrrigationNear");

        [Tooltip("The simulation (clock, config, seed).")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("Where the world's numbers come from.")]
        [SerializeField] private StatsSystem stats;

        private readonly TechTree tree = new TechTree();
        private SettlementRules rules = SettlementRules.Start;
        private double lastSimTime = double.MaxValue;
        private int lastSeed = int.MinValue;

        /// <summary>The tech tree.</summary>
        public TechTree Tree => tree;

        /// <summary>What the people may do right now.</summary>
        public SettlementRules Rules => rules;

        private void OnEnable()
        {
            tree.Learnt += HandleLearnt;
        }

        private void OnDisable()
        {
            tree.Learnt -= HandleLearnt;
        }

        private void HandleLearnt(TechId id)
        {
            rules = tree.Rules();
        }

        private void Update()
        {
            if (simRoot == null || simRoot.Clock == null)
            {
                return;
            }

            double now = simRoot.Clock.SimTimeSeconds;
            int seed = simRoot.ActiveSeed;
            if (now < lastSimTime - 1e-6 || seed != lastSeed)
            {
                if (lastSeed != int.MinValue)
                {
                    tree.Reset();
                    rules = tree.Rules();
                }

                lastSimTime = now;
                lastSeed = seed;
                return;
            }

            float delta = Mathf.Clamp((float)(now - lastSimTime), 0f, LongestStepSeconds);
            lastSimTime = now;

            EcosystemStats latest = stats != null ? stats.Latest : default;
            if (delta > 0f && latest.Valid)
            {
                tree.Update(delta, latest, simRoot.Config);
            }

            // Globals for the ground shader.
            SimConfig config = simRoot.Config;
            Shader.SetGlobalFloat(RoadTierId, rules.RoadTier);
            Shader.SetGlobalFloat(IrrigationOnId, rules.Irrigation ? 1f : 0f);
            Shader.SetGlobalFloat(IrrigationNearId, rules.IrrigationProximity(config));
        }
    }
}
