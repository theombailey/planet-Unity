// =============================================================================
// ConsoleReadouts
// -----------------------------------------------------------------------------
// Purpose : Connects the simulation's numbers to the console: the nixie tubes show the
//           key readings, and the paper roll prints events. This replaces reading the
//           F1 debug panel during normal play.
// Idea    : Four times a second it reads the latest EcosystemStats (the same numbers the
//           panel used) and writes them to the tubes. The same snapshot goes to a PaperLog,
//           which decides whether anything is worth a line on the paper. It also prints a
//           line when a world starts or a one-shot tool is used (meteor, settlers, seeds,
//           birds), since those are events the player caused and wants to see logged. Research
//           events (a technology chosen, an eureka reached, a discovery made) are printed too.
// Readings : YEAR (3 tubes), TEMP deg C (4 tubes, 1 decimal), TREES (4), BIRDS (5), FAMILIES (4: each walker is a household).
// =============================================================================

using System.Collections.Generic;
using Terrarium.Interaction;
using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>Feeds the nixie tubes and the paper roll.</summary>
    public sealed class ConsoleReadouts : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        [Tooltip("Where the ecosystem numbers come from.")]
        [SerializeField] private StatsSystem stats;

        [Tooltip("The simulation (to notice a new world).")]
        [SerializeField] private SimRoot simRoot;

        [SerializeField] private NixieDisplay year;
        [SerializeField] private NixieDisplay temperature;
        [SerializeField] private NixieDisplay trees;
        [SerializeField] private NixieDisplay birds;
        [SerializeField] private NixieDisplay people;
        [SerializeField] private PaperRoll paper;

        [Tooltip("The people, to print a line when humans are created (optional).")]
        [SerializeField] private Terrarium.Physarum.PhysarumSystem humans;

        [Tooltip("The people's technology, whose events go on the paper (optional).")]
        [SerializeField] private Terrarium.Tech.TechSystem tech;

        private readonly PaperLog log = new PaperLog();
        private readonly List<string> lines = new List<string>(8);
        private float timer;
        private bool humansWereCreated;
        private double lastSimTime = double.MaxValue;
        private int lastSeed = int.MinValue;

        private void OnEnable()
        {
            if (tech != null)
            {
                tech.Tree.ResearchStarted += HandleResearchStarted;
                tech.Tree.EurekaReached += HandleEureka;
                tech.Tree.Learnt += HandleLearnt;
            }
        }

        private void OnDisable()
        {
            if (tech != null)
            {
                tech.Tree.ResearchStarted -= HandleResearchStarted;
                tech.Tree.EurekaReached -= HandleEureka;
                tech.Tree.Learnt -= HandleLearnt;
            }
        }

        private string NameOf(Terrarium.Tech.TechId id)
        {
            return tech.Tree.Definitions[(int)id].Name.ToUpperInvariant();
        }

        private void HandleResearchStarted(Terrarium.Tech.TechId id)
        {
            paper?.Print("RESEARCH " + NameOf(id));
        }

        private void HandleEureka(Terrarium.Tech.TechId id)
        {
            paper?.Print("EUREKA! " + NameOf(id));
        }

        private void HandleLearnt(Terrarium.Tech.TechId id)
        {
            paper?.Print("LEARNT " + NameOf(id));
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f)
            {
                return;
            }

            timer = RefreshSeconds;
            DetectNewWorld();

            if (humans != null)
            {
                bool created = humans.HumansCreated;
                if (created && !humansWereCreated && paper != null)
                {
                    paper.Print("HUMANS CREATED");
                }

                humansWereCreated = created;
            }

            EcosystemStats s = stats != null ? stats.Latest : default;
            if (!s.Valid)
            {
                return;
            }

            if (year != null) { year.Show(s.Year); }
            if (temperature != null) { temperature.Show(s.MeanTemperature); }
            if (trees != null) { trees.Show(s.TreesAlive); }
            if (birds != null) { birds.Show(s.BoidsAlive); }
            if (people != null) { people.Show(s.PeopleAlive); }

            lines.Clear();
            log.Poll(s, lines);
            if (paper != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    paper.Print(lines[i]);
                }
            }
        }

        // A new world starts the sim clock over (or changes the seed): let every milestone print again.
        private void DetectNewWorld()
        {
            if (simRoot == null || simRoot.Clock == null)
            {
                return;
            }

            double now = simRoot.Clock.SimTimeSeconds;
            int seed = simRoot.ActiveSeed;
            if (now < lastSimTime - 1e-6 || seed != lastSeed)
            {
                if (lastSeed != int.MinValue && paper != null)
                {
                    paper.Print($"NEW WORLD SEED {seed}");
                }

                log.Reset();
            }

            lastSimTime = now;
            lastSeed = seed;
        }

        /// <summary>Called when a tool is used: one-shot tools leave a line on the paper.</summary>
        public void NotifyTool(ToolKind kind)
        {
            if (paper == null)
            {
                return;
            }

            switch (kind)
            {
                case ToolKind.Meteor: paper.Print("METEOR STRIKE"); break;
                case ToolKind.Settlers: paper.Print("SETTLERS SENT"); break;
                case ToolKind.SeedJar: paper.Print("SEED PLANTED"); break;
                case ToolKind.BoidCage: paper.Print("BIRDS RELEASED"); break;
            }
        }
    }
}
