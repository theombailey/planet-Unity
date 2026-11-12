// =============================================================================
// SettlementRules
// -----------------------------------------------------------------------------
// Purpose : The handful of numbers that say what the people are ALLOWED to do right now:
//           how far from water a house may stand, how cold a house can be built in, whether
//           farms may be irrigated, how hard a road is. Technologies change these and nothing
//           else: no technology adds a model or a script, it only moves one of these rules.
// Idea    : A small value type that the compute shaders' drivers read each step. The tech tree
//           (TechSystem) builds one from what the people have learnt; with no tech tree the
//           starting rules apply (tents, no irrigation, dirt roads).
// Fields  :
//     BuildingTier     0 tent, 1 mud-wall house, 2 brick house. Sets the style of new buildings and
//                      the coldest weather they are built in.
//     RoadTier         0 dirt, 1 stone. Harder roads form faster and wear away slower.
//     Irrigation       farms and homes may spread into drier country near water.
//     WaterReachScale  1 at the start; wells multiply how far from water a house may stand.
// Equations (proximity p is the Water field: 1 at water, 0 at waterFieldReach):
//     a house may stand where   p >= 1 - houseWaterReach * WaterReachScale / waterFieldReach
//     irrigation reaches where  p >= 1 - irrigationReach / waterFieldReach
// =============================================================================

using UnityEngine;

namespace Terrarium.Physarum
{
    /// <summary>What the people are currently allowed to do, set by their technology.</summary>
    public struct SettlementRules
    {
        /// <summary>0 tent, 1 mud-wall house, 2 brick house.</summary>
        public int BuildingTier;

        /// <summary>0 dirt road, 1 stone road.</summary>
        public int RoadTier;

        /// <summary>True once irrigation is learnt.</summary>
        public bool Irrigation;

        /// <summary>Multiplier on how far from water a house may stand (1 before wells).</summary>
        public float WaterReachScale;

        /// <summary>The starting rules: tents, dirt roads, no irrigation, houses close to water.</summary>
        public static SettlementRules Start => new SettlementRules { BuildingTier = 0, RoadTier = 0, Irrigation = false, WaterReachScale = 1f };

        /// <summary>The Water-field proximity a spot needs for a house to stand there.</summary>
        public float HouseWaterProximity(SimConfig config)
        {
            return Mathf.Clamp01(1f - config.houseWaterReach * WaterReachScale / config.waterFieldReach);
        }

        /// <summary>The Water-field proximity at the edge of irrigation's reach.</summary>
        public float IrrigationProximity(SimConfig config)
        {
            return Mathf.Clamp01(1f - config.irrigationReach / config.waterFieldReach);
        }

        /// <summary>The coldest temperature a building of the given tier is built in.</summary>
        public static float MinTemperature(int tier, SimConfig config)
        {
            switch (tier)
            {
                case 0: return config.houseTempMinTent;
                case 1: return config.houseTempMinMud;
                default: return config.houseTempMinBrick;
            }
        }
    }
}
