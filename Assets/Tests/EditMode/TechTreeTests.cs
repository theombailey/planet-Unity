// =============================================================================
// TechTreeTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the people's technology, a pure piece of logic:
//           - nothing is known at the start and the starting rules apply,
//           - a technology needs what it requires, and a learnt one cannot be researched again,
//           - knowledge flows into the chosen research and finishes it at its cost,
//           - knowledge earned while nothing is chosen is kept and spent when something is,
//           - an eureka (the world already doing the thing) makes a technology cheaper, once,
//           - each technology moves exactly the rule it should,
//           - more people and more houses mean faster knowledge,
//           - a new world forgets everything.
// =============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using Terrarium.Physarum;
using Terrarium.Tech;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class TechTreeTests
    {
        private SimConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<SimConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        private static EcosystemStats World(int people = 1000, int houses = 0, float farms = 0f, float towns = 0f)
        {
            return new EcosystemStats { Valid = true, PeopleAlive = people, Houses = houses, FarmFraction = farms, TownFraction = towns };
        }

        // Runs the tree for the given sim seconds in one-second steps.
        private void Run(TechTree tree, EcosystemStats stats, float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f)
            {
                tree.Update(1f, stats, config);
            }
        }

        [Test]
        public void Start_NothingIsLearnt_AndTheStartingRulesApply()
        {
            var tree = new TechTree();

            foreach (TechDefinition definition in tree.Definitions)
            {
                Assert.IsFalse(tree.IsLearnt(definition.Id), definition.Name);
            }

            SettlementRules rules = tree.Rules();
            Assert.AreEqual(0, rules.BuildingTier);
            Assert.AreEqual(0, rules.RoadTier);
            Assert.IsFalse(rules.Irrigation);
            Assert.AreEqual(1f, rules.WaterReachScale);
        }

        [Test]
        public void ATechnology_NeedsWhatItRequires_AndCannotBeResearchedTwice()
        {
            var tree = new TechTree();

            Assert.IsFalse(tree.IsAvailable(TechId.Bricks), "Bricks need mud walls first.");
            Assert.IsFalse(tree.StartResearch(TechId.Bricks, config));
            Assert.IsTrue(tree.IsAvailable(TechId.MudWalls));

            Assert.IsTrue(tree.StartResearch(TechId.MudWalls, config));
            Run(tree, World(people: 4000, houses: 100), 400f);

            Assert.IsTrue(tree.IsLearnt(TechId.MudWalls));
            Assert.IsTrue(tree.IsAvailable(TechId.Bricks), "Learning mud walls opens bricks.");
            Assert.IsFalse(tree.IsAvailable(TechId.MudWalls));
            Assert.IsFalse(tree.StartResearch(TechId.MudWalls, config), "A learnt technology cannot be researched again.");
        }

        [Test]
        public void Knowledge_FlowsIntoTheChosenResearch_AndFinishesItAtItsCost()
        {
            var tree = new TechTree();
            var learnt = new List<TechId>();
            tree.Learnt += id => learnt.Add(id);
            EcosystemStats stats = World(people: 1000);   // gain = rate * 1 per second

            tree.StartResearch(TechId.Irrigation, config);
            Run(tree, stats, 10f);
            float expected = 10f * TechTree.KnowledgePerSecond(stats, config) / tree.Cost(TechId.Irrigation, config);
            Assert.AreEqual(expected, tree.Progress01(TechId.Irrigation, config), 1e-4f);
            Assert.IsEmpty(learnt);

            Run(tree, stats, 1000f);
            Assert.AreEqual(new[] { TechId.Irrigation }, learnt.ToArray(), "It is learnt once, when the cost is reached.");
            Assert.AreEqual(1f, tree.Progress01(TechId.Irrigation, config));
            Assert.IsNull(tree.Researching, "Nothing is being researched once it is done.");
        }

        [Test]
        public void KnowledgeEarnedWhileNothingIsChosen_IsKept_AndSpentWhenSomethingIs()
        {
            var tree = new TechTree();
            EcosystemStats stats = World(people: 4000, houses: 400);

            Run(tree, stats, 60f);
            Assert.Greater(tree.Banked, 50f, "Knowledge piles up while nothing is chosen.");
            float banked = tree.Banked;

            tree.StartResearch(TechId.Wells, config);
            Assert.AreEqual(0f, tree.Banked, 1e-4f, "Choosing spends the bank.");
            Assert.AreEqual(banked / tree.Cost(TechId.Wells, config), tree.Progress01(TechId.Wells, config), 1e-4f);
        }

        [Test]
        public void Eureka_MakesATechnologyCheaper_OnceOnly()
        {
            var tree = new TechTree();
            var eurekas = new List<TechId>();
            tree.EurekaReached += id => eurekas.Add(id);

            float full = tree.Cost(TechId.Irrigation, config);
            tree.Update(1f, World(farms: 0.001f), config);
            Assert.AreEqual(full, tree.Cost(TechId.Irrigation, config), "No eureka while there are hardly any farms.");

            tree.Update(1f, World(farms: 0.05f), config);
            tree.Update(1f, World(farms: 0.06f), config);
            Assert.AreEqual(full * (1f - config.eurekaDiscount), tree.Cost(TechId.Irrigation, config), 1e-3f);
            Assert.AreEqual(new[] { TechId.Irrigation }, eurekas.ToArray(), "The eureka is announced once.");
        }

        [Test]
        public void EachTechnology_MovesTheRuleItShould()
        {
            var tree = new TechTree();
            EcosystemStats stats = World(people: 9000, houses: 600, farms: 0.1f, towns: 0.1f);

            void Learn(TechId id)
            {
                tree.StartResearch(id, config);
                Run(tree, stats, 1000f);
                Assert.IsTrue(tree.IsLearnt(id), id.ToString());
            }

            Learn(TechId.Irrigation);
            Assert.IsTrue(tree.Rules().Irrigation);

            Learn(TechId.Wells);
            Assert.AreEqual(3f, tree.Rules().WaterReachScale);

            Learn(TechId.MudWalls);
            Assert.AreEqual(1, tree.Rules().BuildingTier);
            Learn(TechId.Bricks);
            Assert.AreEqual(2, tree.Rules().BuildingTier);

            Learn(TechId.StoneRoads);
            Assert.AreEqual(1, tree.Rules().RoadTier);
        }

        [Test]
        public void MorePeopleAndMoreHouses_MeanFasterKnowledge()
        {
            float few = TechTree.KnowledgePerSecond(World(people: 500, houses: 10), config);
            float many = TechTree.KnowledgePerSecond(World(people: 5000, houses: 10), config);
            float built = TechTree.KnowledgePerSecond(World(people: 500, houses: 800), config);

            Assert.Greater(many, few);
            Assert.Greater(built, few);
        }

        [Test]
        public void ANewWorld_ForgetsEverything()
        {
            var tree = new TechTree();
            tree.StartResearch(TechId.MudWalls, config);
            Run(tree, World(people: 4000, houses: 100), 400f);
            Assert.IsTrue(tree.IsLearnt(TechId.MudWalls));

            tree.Reset();

            Assert.IsFalse(tree.IsLearnt(TechId.MudWalls));
            Assert.IsNull(tree.Researching);
            Assert.AreEqual(0f, tree.Banked);
            Assert.AreEqual(0, tree.Rules().BuildingTier);
        }

        [Test]
        public void TheWaterRules_AreDistancesInsideTheFieldsReach()
        {
            SettlementRules rules = SettlementRules.Start;
            float near = rules.HouseWaterProximity(config);
            rules.WaterReachScale = 3f;
            float wells = rules.HouseWaterProximity(config);

            Assert.Greater(near, wells, "Wells let a house stand further from water, so it needs less proximity.");
            Assert.AreEqual(1f - config.houseWaterReach / config.waterFieldReach, near, 1e-5f);
        }
    }
}
