// =============================================================================
// HouseSimulation
// -----------------------------------------------------------------------------
// Purpose : The C# side of Houses.compute: owns the per-spot house state and the list of
//           standing houses, and runs one update. Free of scene references, so tests can
//           drive it directly and TownRenderer can draw what it produces.
// Idea    : Each possible house spot keeps a remembered state on the GPU (see Houses.compute).
//           An update moves every spot's state by the rules for `deltaSeconds` of sim time and
//           rewrites the list of houses that now stand. The list's length is copied into an
//           indirect-draw argument buffer on the GPU, so the draw never waits for the count.
// Parameters: houseCandidates, thresholds, bonuses, build / abandon / upgrade times, temperature limits
//           (SimConfig "Towns and farms" and "Rules for houses ..."), and the people's current rules.
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
        /// <summary>Bytes per house in the draw list (three float4).</summary>
        public const int HouseStride = 48;

        private const int ThreadGroupSize = 64;

        private readonly ComputeShader shader;
        private readonly SimConfig config;
        private readonly int updateKernel;
        private readonly int clearKernel;
        private readonly uint[] argumentData = new uint[5];

        private ComputeBuffer houses;
        private ComputeBuffer state;
        private ComputeBuffer arguments;
        private int candidates;
        private bool needsClear = true;
        private uint frame;

        /// <summary>Creates a house simulation. <paramref name="indexCount"/> is the house mesh's index count (for the draw arguments).</summary>
        public HouseSimulation(ComputeShader houseShader, SimConfig simConfig, uint indexCount)
        {
            shader = houseShader;
            config = simConfig;
            updateKernel = shader.FindKernel("UpdateHouses");
            clearKernel = shader.FindKernel("ClearHouseState");

            arguments = new ComputeBuffer(1, argumentData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argumentData[0] = indexCount;
            arguments.SetData(argumentData);
        }

        /// <summary>The list of standing houses (an append buffer, three float4 each). Null until the first update.</summary>
        public ComputeBuffer Houses => houses;

        /// <summary>The indirect-draw arguments (index count, instance count, ...). The instance count is the house count.</summary>
        public ComputeBuffer Arguments => arguments;

        /// <summary>The per-spot state: float2 (progress, tier). For tests.</summary>
        public ComputeBuffer State => state;

        /// <summary>How many candidate spots there are.</summary>
        public int CandidateCount => candidates;

        /// <summary>Forgets every house (a new world starts); applied at the next update.</summary>
        public void Reset()
        {
            needsClear = true;
        }

        /// <summary>Moves every spot's state by the rules for <paramref name="deltaSeconds"/> of sim time and rebuilds the draw list.</summary>
        public void Update(PhysarumFields fields, SettlementRules rules, int resolution, float deltaSeconds)
        {
            int wanted = Mathf.Max(1000, config.houseCandidates);

            // The state is a fixed length, so a changed candidate count starts the town over.
            if (state == null || candidates != wanted)
            {
                // The draw list can hold one house per candidate spot, so its counter can never run past its end.
                state?.Release();
                houses?.Release();
                houses = new ComputeBuffer(wanted, HouseStride, ComputeBufferType.Append);
                state = new ComputeBuffer(wanted, 2 * sizeof(float));
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
            shader.SetBuffer(updateKernel, "_Houses", houses);
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
            shader.SetFloat("_AbandonRate", 1f / config.houseAbandonSeconds);
            shader.SetFloat("_UpgradeRate", 1f / config.houseUpgradeSeconds);
            shader.SetInt("_BuildingTier", rules.BuildingTier);
            shader.Dispatch(updateKernel, groups, 1, 1);

            ComputeBuffer.CopyCount(houses, arguments, sizeof(uint));
        }

        /// <summary>Releases the GPU buffers.</summary>
        public void Dispose()
        {
            houses?.Release();
            state?.Release();
            arguments?.Release();
            houses = null;
            state = null;
            arguments = null;
        }
    }
}
