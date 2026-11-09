// =============================================================================
// ClimateController
// -----------------------------------------------------------------------------
// Purpose : Owns the planet's terrain and climate. Generates a new world when
//           the fields are reset, and advances the climate one step whenever
//           SimLoop asks.
// Idea    : Glue between the scene and the pure classes that do the work:
//             TerrainGenerator  (FieldInit.compute) - makes the land
//             ClimateSimulation (Climate.compute)   - steps the climate
//             PlanetOrientation                     - the planet's rotation
//           Each step is given its own time, so at 16x the lamp sweeps smoothly
//           across the 16 steps of a frame instead of jumping once per frame.
// Position: the climate uses the planet's rest centre and ignores the small
//           decorative bob (2% of the radius), so the bob cannot nudge the
//           climate up and down.
// =============================================================================

using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Climate
{
    /// <summary>Generates terrain and steps the climate.</summary>
    public sealed class ClimateController : MonoBehaviour
    {
        [Tooltip("The simulation: config, seed and clock.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("The lamp's light: its position drives insolation.")]
        [SerializeField] private Transform lamp;

        [Tooltip("FieldInit.compute (terrain generation).")]
        [SerializeField] private ComputeShader fieldInit;

        [Tooltip("Climate.compute.")]
        [SerializeField] private ComputeShader climateShader;

        private TerrainGenerator terrainGenerator;
        private ClimateSimulation simulation;

        /// <summary>Share of the surface above sea level for the next generated world, 0..1.</summary>
        public float LandFraction { get; set; }

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || lamp == null || fieldInit == null || climateShader == null)
            {
                Debug.LogError("ClimateController is missing a reference.", this);
                enabled = false;
                return;
            }

            terrainGenerator = new TerrainGenerator(fieldInit);
            simulation = new ClimateSimulation(climateShader, simRoot.Config);
            LandFraction = simRoot.Config.landFraction;

            // Subscribed in Awake so it is in place before FieldSet creates the fields in Start.
            fieldSet.WorldReset += GenerateWorld;
        }

        private void OnDestroy()
        {
            if (fieldSet != null)
            {
                fieldSet.WorldReset -= GenerateWorld;
            }
        }

        /// <summary>Makes new terrain from the current seed and gives the climate a settled starting state.</summary>
        public void GenerateWorld()
        {
            SimConfig config = simRoot.Config;
            Field terrain = fieldSet.Get(FieldId.Terrain);
            Field climate = fieldSet.Get(FieldId.Climate);
            Field soil = fieldSet.Get(FieldId.Soil);

            terrainGenerator.Generate(terrain.Current, config, simRoot.ActiveSeed, LandFraction);

            int coarseResolution = Mathf.Min(config.spinUpResolution, terrain.Resolution);
            if (coarseResolution >= terrain.Resolution)
            {
                InitialiseClimate(terrain.Current, climate);
                simulation.SpinUp(terrain.Current, climate, soil.Current, simRoot.Clock, lamp.position);
                return;
            }

            // The long settling run is expensive at full size (thousands of steps, each a pass over
            // every texel). So do most of it on a coarse grid, where a step costs a fraction as much,
            // copy the result up, then finish with a short run at full size.
            float settle = Mathf.Min(config.climateSettleSeconds, config.climateSpinUpSeconds);
            var coarseTerrain = new Field(FieldId.Terrain, coarseResolution, RenderTextureFormat.ARGBFloat, pingPong: false);
            var coarseClimate = new Field(FieldId.Climate, coarseResolution, RenderTextureFormat.ARGBFloat, pingPong: true);
            var coarseSoil = new Field(FieldId.Soil, coarseResolution, RenderTextureFormat.ARGBFloat, pingPong: true);
            try
            {
                FieldOps.Clear(fieldInit, coarseSoil.Current);
                terrainGenerator.Generate(coarseTerrain.Current, config, simRoot.ActiveSeed, LandFraction);
                InitialiseClimate(coarseTerrain.Current, coarseClimate);
                simulation.SpinUp(coarseTerrain.Current, coarseClimate, coarseSoil.Current, simRoot.Clock, lamp.position,
                    config.climateSpinUpSeconds - settle, -settle);

                FieldOps.Resample(fieldInit, coarseClimate.Current, climate.Current);
                FieldOps.Resample(fieldInit, coarseSoil.Current, soil.Current);
            }
            finally
            {
                coarseTerrain.Release();
                coarseClimate.Release();
                coarseSoil.Release();
            }

            simulation.SpinUp(terrain.Current, climate, soil.Current, simRoot.Clock, lamp.position, settle, 0.0);
        }

        private void InitialiseClimate(RenderTexture terrain, Field climate)
        {
            foreach (RenderTexture buffer in climate.Buffers)
            {
                simulation.Initialise(terrain, buffer);
            }
        }

        /// <summary>Advances the climate by one fixed step.</summary>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        /// <param name="simTimeSeconds">Sim time at this step, used for the planet's rotation.</param>
        public void Step(float deltaSeconds, double simTimeSeconds)
        {
            simulation.StepAtTime(
                fieldSet.Get(FieldId.Terrain).Current,
                fieldSet.Get(FieldId.Climate),
                fieldSet.Get(FieldId.Soil).Current,
                simRoot.Clock,
                lamp.position,
                deltaSeconds,
                simTimeSeconds);
        }

        /// <summary>Immediate-mode controls, called from the F1 debug panel.</summary>
        public void OnDebugGui()
        {
            GUILayout.Label($"Land fraction {LandFraction:0.00}");
            LandFraction = GUILayout.HorizontalSlider(LandFraction, 0.1f, 0.8f);
            if (GUILayout.Button("Regenerate terrain (same seed)"))
            {
                simRoot.Reseed(simRoot.ActiveSeed);
            }
        }
    }
}
