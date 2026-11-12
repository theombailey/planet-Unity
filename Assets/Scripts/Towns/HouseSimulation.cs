// =============================================================================
// HouseSimulation
// -----------------------------------------------------------------------------
// Purpose : The C# side of Houses.compute: owns the per-spot house state and the lists of
//           standing houses and of buildings under work, and runs one update. Free of scene
//           references, so tests can drive it directly and TownRenderer can draw what it produces.
// Idea    : Each possible house spot keeps a remembered state on the GPU (see Houses.compute).
//           An update moves every spot's state by the rules for `deltaSeconds` of sim time and
//           rewrites the two lists: every house that stands, and every one that is being built,
//           rebuilt or taken down (those get scaffolding). The lists' lengths are copied into
//           indirect-draw argument buffers on the GPU, so a draw never waits for a count.
// Lattice : the number of spots follows from the minimum distance wanted between houses, so that no two
//           spots can be closer than it (see Houses.compute):  spots = (2.38 / minDistance)^2.
// Parameters: houseMinDistance, thresholds, bonuses, build / abandon / demolish / upgrade times,
//           temperature limits (SimConfig "Towns and farms" and "Rules for houses ..."), and the
//           people's current rules.
// =============================================================================

using System;
using Terrarium.Physarum;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Towns
{
    /// <summary>Runs the house rules on the GPU.</summary>
    public sealed class HouseSimulation : IDisposable
    {
        /// <summary>Bytes per house in the draw lists (three float4).</summary>
        public const int HouseStride = 48;

        /// <summary>Closest-pair distance of the spiral lattice, in units of 1 / sqrt(spots) radians, less the nudge (see Houses.compute).</summary>
        public const float LatticeGapFactor = 2.38f;

        private const int ThreadGroupSize = 64;

        private readonly ComputeShader shader;
        private readonly SimConfig config;
        private readonly int updateKernel;
        private readonly int clearKernel;
        private readonly uint[] argumentData = new uint[5];
        private readonly uint[] scaffoldIndexCount;

        private ComputeBuffer houses;
        private ComputeBuffer scaffolds;
        private ComputeBuffer state;
        private ComputeBuffer arguments;
        private ComputeBuffer scaffoldArguments;
        private int candidates;
        private bool needsClear = true;
        private uint frame;

        /// <summary>
        /// Creates a house simulation. <paramref name="houseIndexCount"/> and <paramref name="scaffoldIndexCount"/> are the
        /// index counts of the house and scaffold meshes (for the draw arguments).
        /// </summary>
        public HouseSimulation(ComputeShader houseShader, SimConfig simConfig, uint houseIndexCount, uint scaffoldIndices)
        {
            shader = houseShader;
            config = simConfig;
            updateKernel = shader.FindKernel("UpdateHouses");
            clearKernel = shader.FindKernel("ClearHouseState");
            scaffoldIndexCount = new[] { scaffoldIndices, 0u, 0u, 0u, 0u };

            arguments = new ComputeBuffer(1, argumentData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argumentData[0] = houseIndexCount;
            arguments.SetData(argumentData);

            scaffoldArguments = new ComputeBuffer(1, argumentData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            scaffoldArguments.SetData(scaffoldIndexCount);
        }

        /// <summary>The number of candidate spots that keep houses a given distance apart.</summary>
        public static int CandidatesFor(float minDistance)
        {
            float perRadian = LatticeGapFactor / Mathf.Max(minDistance, 1e-4f);
            return Mathf.Max(1000, Mathf.CeilToInt(perRadian * perRadian));
        }

        /// <summary>The list of standing houses (an append buffer, three float4 each). Null until the first update.</summary>
        public ComputeBuffer Houses => houses;

        /// <summary>The list of buildings being built, rebuilt or taken down (same layout). Null until the first update.</summary>
        public ComputeBuffer Scaffolds => scaffolds;

        /// <summary>The indirect-draw arguments for the houses (index count, instance count, ...). The instance count is the house count.</summary>
        public ComputeBuffer Arguments => arguments;

        /// <summary>The indirect-draw arguments for the scaffolding. The instance count is the number of buildings under work.</summary>
        public ComputeBuffer ScaffoldArguments => scaffoldArguments;

        /// <summary>The per-spot state: float4 (progress, tier, previous tier, idle seconds). For tests.</summary>
        public ComputeBuffer State => state;

        /// <summary>How many candidate spots there are.</summary>
        public int CandidateCount => candidates;

        /// <summary>Forgets every house (a new world starts); applied at the next update.</summary>
        public void Reset()
        {
            needsClear = true;
        }

        /// <summary>Moves every spot's state by the rules for <paramref name="deltaSeconds"/> of sim time and rebuilds the draw lists.</summary>
        public void Update(PhysarumFields fields, SettlementRules rules, int resolution, float deltaSeconds)
        {
            int wanted = CandidatesFor(config.houseMinDistance);

            // The state is a fixed length, so a changed minimum distance starts the town over.
            if (state == null || candidates != wanted)
            {
                // The lists can hold one entry per candidate spot, so a counter can never run past its end.
                state?.Release();
                houses?.Release();
                scaffolds?.Release();
                houses = new ComputeBuffer(wanted, HouseStride, ComputeBufferType.Append);
                scaffolds = new ComputeBuffer(wanted, HouseStride, ComputeBufferType.Append);
                state = new ComputeBuffer(wanted, 4 * sizeof(float));
                candidates = wanted;
                needsClear = true;
            }

            int groups = (candidates + ThreadGroupSize - 1) / ThreadGroupSize;
            shader.SetInt("_CandidateCount", candidates);
            if (needsClear)
            {
                needsClear = false;
                shader.SetBuffer(clearKernel, "_State", state);
                shader.Dispatch(clearKernel, groups, 1, 1);
                deltaSeconds = 0f;
            }

            frame++;
            houses.SetCounterValue(0);
            scaffolds.SetCounterValue(0);
            shader.SetBuffer(updateKernel, "_Houses", houses);
            shader.SetBuffer(updateKernel, "_Scaffolds", scaffolds);
            shader.SetBuffer(updateKernel, "_State", state);
            shader.SetTexture(updateKernel, "_Land", fields.Land.Current);
            shader.SetTexture(updateKernel, "_Terrain", fields.Terrain);
            shader.SetTexture(updateKernel, "_Climate", fields.Climate);
            shader.SetTexture(updateKernel, "_Water", fields.Water.Current);
            shader.SetTexture(updateKernel, "_Trail", fields.Trail.Current);
            shader.SetInt("_Resolution", resolution);
            shader.SetInt("_Frame", (int)frame);
            shader.SetFloat("_DeltaSeconds", deltaSeconds);
            shader.SetFloat("_HouseThreshold", config.houseThreshold);
            shader.SetFloat("_ThresholdSpread", config.houseThresholdSpread);
            shader.SetFloat("_WaterBonus", config.houseWaterBonus);
            shader.SetFloat("_RoadBonus", config.houseRoadBonus);
            shader.SetFloat("_HouseSize", config.houseSize);
            shader.SetFloat("_MaxSize", 0.7f * config.houseMinDistance);
            shader.SetFloat("_HeightScale", config.terrainHeightScale);
            shader.SetFloat("_WaterNeed", rules.HouseWaterProximity(config));
            shader.SetFloat("_WetMoisture", config.houseWetMoisture);
            shader.SetVector("_TempMin", new Vector4(
                SettlementRules.MinTemperature(0, config),
                SettlementRules.MinTemperature(1, config),
                SettlementRules.MinTemperature(2, config),
                0f));
            shader.SetFloat("_TempMax", config.houseTempMax);
            shader.SetFloat("_TempMargin", config.houseTempMargin);
            shader.SetFloat("_BuildRate", 1f / config.houseBuildSeconds);
            shader.SetFloat("_AbandonSeconds", config.houseAbandonSeconds);
            shader.SetFloat("_DemolishRate", 1f / config.houseDemolishSeconds);
            shader.SetFloat("_UpgradeRate", 1f / config.houseUpgradeSeconds);
            shader.SetInt("_BuildingTier", rules.BuildingTier);
            shader.Dispatch(updateKernel, groups, 1, 1);

            ComputeBuffer.CopyCount(houses, arguments, sizeof(uint));
            ComputeBuffer.CopyCount(scaffolds, scaffoldArguments, sizeof(uint));
        }

        /// <summary>Releases the GPU buffers.</summary>
        public void Dispose()
        {
            houses?.Release();
            scaffolds?.Release();
            state?.Release();
            arguments?.Release();
            scaffoldArguments?.Release();
            houses = null;
            scaffolds = null;
            state = null;
            arguments = null;
            scaffoldArguments = null;
        }
    }
}
