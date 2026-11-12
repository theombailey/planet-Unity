// =============================================================================
// TechTree
// -----------------------------------------------------------------------------
// Purpose : The people's technology: which discoveries they have made, which one they
//           are working on, and how much knowledge it has soaked up. Pure logic with no
//           scene and no clock, so every rule can be tested alone.
// Idea    : A technology does not add a model or a script. It changes one RULE of how the
//           world builds itself (see SettlementRules): irrigation lets farms spread into drier
//           ground, mud walls and bricks change what houses are made of and how cold a place
//           they can be built in, wells let houses stand further from water, stone roads make
//           roads harder. Everything people build is still drawn procedurally; only the rules move.
//
//           KNOWLEDGE is earned by living: the more people there are, and the more houses they
//           have built, the faster it comes:
//               gain per second = knowledgeRate * ( sqrt(people / 1000) + houses / 400 )
//           It all goes into the technology the player has chosen to research. Knowledge earned
//           while nothing is chosen is kept and spent the moment something is chosen.
//
//           EUREKA: each technology has a condition that the world itself can meet (farms already
//           exist, a certain number of houses stand, ...). When it is met the technology costs less:
//               cost = baseCost * (1 - eurekaDiscount)     once its eureka has happened
//           so what the people are already doing steers what they learn fastest.
//
//           A technology can be researched when every technology it requires has been learnt.
// Parameters: knowledgeRate and eurekaDiscount (SimConfig); costs and conditions are below.
// =============================================================================

using System;
using Terrarium.Physarum;
using UnityEngine;

namespace Terrarium.Tech
{
    /// <summary>The technologies the people can discover.</summary>
    public enum TechId
    {
        /// <summary>Farms and homes spread into drier country near water; channels run through the fields.</summary>
        Irrigation,

        /// <summary>Houses are built of mud walls, and can stand in colder places.</summary>
        MudWalls,

        /// <summary>Houses are built of brick, and can stand in the coldest places.</summary>
        Bricks,

        /// <summary>Houses can stand much further from water.</summary>
        Wells,

        /// <summary>Roads harden faster and wear away slower.</summary>
        StoneRoads,
    }

    /// <summary>One technology: what it is called, what it costs, what it needs and what makes it cheaper.</summary>
    public sealed class TechDefinition
    {
        /// <summary>Creates a definition.</summary>
        public TechDefinition(TechId id, string name, string effect, string eurekaText, float cost, TechId[] requires, Func<EcosystemStats, bool> eureka)
        {
            Id = id;
            Name = name;
            Effect = effect;
            EurekaText = eurekaText;
            Cost = cost;
            Requires = requires;
            Eureka = eureka;
        }

        /// <summary>Which technology this is.</summary>
        public TechId Id { get; }

        /// <summary>Its display name.</summary>
        public string Name { get; }

        /// <summary>What it changes, in a short phrase.</summary>
        public string Effect { get; }

        /// <summary>What makes it cheaper, in a short phrase.</summary>
        public string EurekaText { get; }

        /// <summary>Knowledge needed to learn it.</summary>
        public float Cost { get; }

        /// <summary>Technologies that must be learnt first.</summary>
        public TechId[] Requires { get; }

        /// <summary>True when the world has met its eureka condition.</summary>
        public Func<EcosystemStats, bool> Eureka { get; }
    }

    /// <summary>The state of the people's technology.</summary>
    public sealed class TechTree
    {
        private readonly TechDefinition[] definitions;
        private readonly bool[] learnt;
        private readonly bool[] eurekaReached;
        private readonly float[] invested;
        private float banked;
        private int researching = -1;

        /// <summary>Creates a tree with nothing learnt.</summary>
        public TechTree()
        {
            definitions = new[]
            {
                new TechDefinition(TechId.Irrigation, "Irrigation", "farms and homes reach into drier land near water; channels", "farms already stand", 150f,
                    new TechId[0], stats => stats.FarmFraction >= 0.01f),
                new TechDefinition(TechId.MudWalls, "Mud walls", "tents give way to mud-walled houses; colder places can be settled", "30 houses stand", 120f,
                    new TechId[0], stats => stats.Houses >= 30),
                new TechDefinition(TechId.Bricks, "Bricks", "brick houses; even colder places can be settled", "250 houses stand", 400f,
                    new[] { TechId.MudWalls }, stats => stats.Houses >= 250),
                new TechDefinition(TechId.Wells, "Wells", "houses can stand much further from water", "2500 people live", 200f,
                    new TechId[0], stats => stats.PeopleAlive >= 2500),
                new TechDefinition(TechId.StoneRoads, "Stone roads", "roads harden faster and last longer", "towns cover 1.5% of the land", 300f,
                    new[] { TechId.MudWalls }, stats => stats.TownFraction >= 0.015f),
            };

            learnt = new bool[definitions.Length];
            eurekaReached = new bool[definitions.Length];
            invested = new float[definitions.Length];
        }

        /// <summary>Raised when a technology is learnt.</summary>
        public event Action<TechId> Learnt;

        /// <summary>Raised when a technology's eureka condition is first met.</summary>
        public event Action<TechId> EurekaReached;

        /// <summary>Raised when the player starts researching a technology.</summary>
        public event Action<TechId> ResearchStarted;

        /// <summary>Every technology, in display order.</summary>
        public TechDefinition[] Definitions => definitions;

        /// <summary>The technology being researched, or null.</summary>
        public TechDefinition Researching => researching >= 0 ? definitions[researching] : null;

        /// <summary>Knowledge earned while nothing was being researched.</summary>
        public float Banked => banked;

        /// <summary>True once the technology has been learnt.</summary>
        public bool IsLearnt(TechId id)
        {
            return learnt[(int)id];
        }

        /// <summary>True once the technology's eureka condition has been met.</summary>
        public bool HasEureka(TechId id)
        {
            return eurekaReached[(int)id];
        }

        /// <summary>True when the technology is not yet learnt and everything it requires has been.</summary>
        public bool IsAvailable(TechId id)
        {
            if (learnt[(int)id])
            {
                return false;
            }

            foreach (TechId required in definitions[(int)id].Requires)
            {
                if (!learnt[(int)required])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>What the technology costs now: its base cost, less the eureka discount if it has been earned.</summary>
        public float Cost(TechId id, SimConfig config)
        {
            float discount = eurekaReached[(int)id] ? config.eurekaDiscount : 0f;
            return definitions[(int)id].Cost * (1f - discount);
        }

        /// <summary>How far through the technology's cost the research is, 0..1 (1 once learnt).</summary>
        public float Progress01(TechId id, SimConfig config)
        {
            if (learnt[(int)id])
            {
                return 1f;
            }

            return Mathf.Clamp01(invested[(int)id] / Cost(id, config));
        }

        /// <summary>
        /// Chooses what to research. Returns false if it cannot be researched (already learnt, or something it requires is
        /// missing). Any knowledge banked while nothing was chosen is spent on it at once.
        /// </summary>
        public bool StartResearch(TechId id, SimConfig config)
        {
            if (!IsAvailable(id))
            {
                return false;
            }

            if (researching == (int)id)
            {
                return true;
            }

            researching = (int)id;
            ResearchStarted?.Invoke(id);
            AddKnowledge(0f, config);   // spends the bank
            return true;
        }

        /// <summary>Gives up the current research; what has been invested in it is kept.</summary>
        public void StopResearch()
        {
            researching = -1;
        }

        /// <summary>The knowledge the people earn per sim second from how many of them there are and how much they have built.</summary>
        public static float KnowledgePerSecond(EcosystemStats stats, SimConfig config)
        {
            return config.knowledgeRate * (Mathf.Sqrt(Mathf.Max(0, stats.PeopleAlive) / 1000f) + Mathf.Max(0, stats.Houses) / 400f);
        }

        /// <summary>Advances the tree by <paramref name="deltaSeconds"/> sim seconds, given how the world looks now.</summary>
        public void Update(float deltaSeconds, EcosystemStats stats, SimConfig config)
        {
            if (!stats.Valid)
            {
                return;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                if (!learnt[i] && !eurekaReached[i] && definitions[i].Eureka(stats))
                {
                    eurekaReached[i] = true;
                    EurekaReached?.Invoke(definitions[i].Id);
                }
            }

            AddKnowledge(KnowledgePerSecond(stats, config) * deltaSeconds, config);
        }

        /// <summary>Forgets everything (a new world starts).</summary>
        public void Reset()
        {
            Array.Clear(learnt, 0, learnt.Length);
            Array.Clear(eurekaReached, 0, eurekaReached.Length);
            Array.Clear(invested, 0, invested.Length);
            banked = 0f;
            researching = -1;
        }

        /// <summary>The rules the people live by, given what they have learnt.</summary>
        public SettlementRules Rules()
        {
            SettlementRules rules = SettlementRules.Start;
            rules.Irrigation = learnt[(int)TechId.Irrigation];
            rules.BuildingTier = learnt[(int)TechId.Bricks] ? 2 : (learnt[(int)TechId.MudWalls] ? 1 : 0);
            rules.RoadTier = learnt[(int)TechId.StoneRoads] ? 1 : 0;
            rules.WaterReachScale = learnt[(int)TechId.Wells] ? 3f : 1f;
            return rules;
        }

        private void AddKnowledge(float amount, SimConfig config)
        {
            if (researching < 0)
            {
                banked += amount;
                return;
            }

            float total = amount + banked;
            banked = 0f;
            invested[researching] += total;

            TechId id = definitions[researching].Id;
            float cost = Cost(id, config);
            if (invested[researching] >= cost)
            {
                banked = invested[researching] - cost;
                invested[researching] = cost;
                learnt[researching] = true;
                researching = -1;
                Learnt?.Invoke(id);
            }
        }
    }
}
