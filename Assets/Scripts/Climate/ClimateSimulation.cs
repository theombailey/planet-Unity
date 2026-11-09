// =============================================================================
// ClimateSimulation
// -----------------------------------------------------------------------------
// Purpose : The C# side of Climate.compute: hands the kernel its parameters and
//           textures and dispatches it. Kept free of MonoBehaviour and scene
//           references so it can be driven directly by tests.
// Idea    : See Climate.compute for the model. This class only translates:
//           SimConfig values -> shader parameters, a rotation -> a matrix, and
//           field textures -> kernel bindings. Property ids are looked up once,
//           so a step allocates nothing.
// =============================================================================

using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Climate
{
    /// <summary>Where the planet and the lamp are for one climate step.</summary>
    public readonly struct PlanetPose
    {
        /// <summary>Rotation taking planet-local directions to world directions.</summary>
        public readonly Quaternion Rotation;

        /// <summary>World position of the planet's centre.</summary>
        public readonly Vector3 Centre;

        /// <summary>World position of the lamp's bulb.</summary>
        public readonly Vector3 LampPosition;

        /// <summary>Creates a pose.</summary>
        public PlanetPose(Quaternion rotation, Vector3 centre, Vector3 lampPosition)
        {
            Rotation = rotation;
            Centre = centre;
            LampPosition = lampPosition;
        }
    }

    /// <summary>Dispatches the climate kernels.</summary>
    public sealed class ClimateSimulation
    {
        private const int ThreadGroupSize = 8;

        // Largest D dt / h^2 allowed in one sub-step; the explicit scheme is stable up to 0.25.
        private const float MaxDiffusionPerSubStep = 0.2f;

        private static readonly int TerrainId = Shader.PropertyToID("_Terrain");
        private static readonly int ClimateReadId = Shader.PropertyToID("_ClimateRead");
        private static readonly int ClimateWriteId = Shader.PropertyToID("_ClimateWrite");
        private static readonly int SoilId = Shader.PropertyToID("_Soil");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int PlanetRotationId = Shader.PropertyToID("_PlanetRotation");
        private static readonly int PlanetCentreId = Shader.PropertyToID("_PlanetCentre");
        private static readonly int PlanetRadiusId = Shader.PropertyToID("_PlanetRadius");
        private static readonly int LampPositionId = Shader.PropertyToID("_LampPosition");
        private static readonly int BaseTemperatureId = Shader.PropertyToID("_BaseTemperature");
        private static readonly int SunGainId = Shader.PropertyToID("_SunGain");
        private static readonly int AltitudeLapseId = Shader.PropertyToID("_AltitudeLapse");
        private static readonly int LatitudeCoolingId = Shader.PropertyToID("_LatitudeCooling");
        private static readonly int LandResponseId = Shader.PropertyToID("_LandResponse");
        private static readonly int OceanResponseId = Shader.PropertyToID("_OceanResponse");
        private static readonly int HeatDiffusionId = Shader.PropertyToID("_HeatDiffusion");
        private static readonly int MoistureDiffusionId = Shader.PropertyToID("_MoistureDiffusion");
        private static readonly int EvaporationId = Shader.PropertyToID("_Evaporation");
        private static readonly int WeatherRateId = Shader.PropertyToID("_WeatherRate");
        private static readonly int NutrientCapId = Shader.PropertyToID("_NutrientCap");

        private readonly ComputeShader shader;
        private readonly SimConfig config;
        private readonly int initKernel;
        private readonly int stepKernel;

        /// <summary>Creates a simulation that uses the given Climate.compute and parameters.</summary>
        public ClimateSimulation(ComputeShader climateShader, SimConfig simConfig)
        {
            shader = climateShader;
            config = simConfig;
            initKernel = shader.FindKernel("InitClimate");
            stepKernel = shader.FindKernel("ClimateStep");
        }

        /// <summary>Writes a plausible starting climate into a climate texture.</summary>
        public void Initialise(RenderTexture terrain, RenderTexture climate)
        {
            SetParameters(climate.width, 0f);
            shader.SetTexture(initKernel, TerrainId, terrain);
            shader.SetTexture(initKernel, ClimateWriteId, climate);
            Dispatch(initKernel, climate.width);
        }

        /// <summary>
        /// Advances the climate by <paramref name="deltaSeconds"/>. Reads and writes the climate field's
        /// ping-pong pair and swaps it, so afterwards <c>climate.Current</c> is the new state.
        /// </summary>
        /// <param name="terrain">Terrain field (read).</param>
        /// <param name="climate">Climate field (read, then rewritten, then swapped).</param>
        /// <param name="soil">Soil field; nutrients are increased in place by weathering.</param>
        /// <param name="pose">Planet rotation and the planet and lamp positions for this step.</param>
        /// <param name="deltaSeconds">Step length in sim seconds.</param>
        public void Step(RenderTexture terrain, Field climate, RenderTexture soil, PlanetPose pose, float deltaSeconds)
        {
            int resolution = climate.Resolution;

            // Explicit diffusion is only stable while  D dt / h^2 <= 0.25  (h = texel angle). Rather than
            // cap the diffusion (which would make a finer grid diffuse more slowly than a coarse one,
            // so the High preset would have a different climate), split the step into enough equal
            // sub-steps to keep it stable. The physics then does not depend on the preset.
            float texelAngle = Mathf.PI * 0.5f / resolution;
            float largestDiffusion = Mathf.Max(config.heatDiffusion, config.moistureDiffusion);
            float rate = largestDiffusion * deltaSeconds / (texelAngle * texelAngle);
            int subSteps = Mathf.Max(1, Mathf.CeilToInt(rate / MaxDiffusionPerSubStep));
            float subSeconds = deltaSeconds / subSteps;

            SetParameters(resolution, subSeconds);
            shader.SetMatrix(PlanetRotationId, Matrix4x4.Rotate(pose.Rotation));
            shader.SetVector(PlanetCentreId, pose.Centre);
            shader.SetVector(LampPositionId, pose.LampPosition);
            shader.SetTexture(stepKernel, TerrainId, terrain);
            shader.SetTexture(stepKernel, SoilId, soil);

            for (int i = 0; i < subSteps; i++)
            {
                shader.SetTexture(stepKernel, ClimateReadId, climate.Current);
                shader.SetTexture(stepKernel, ClimateWriteId, climate.Next);
                Dispatch(stepKernel, resolution);
                climate.Swap();
            }
        }

        /// <summary>
        /// Advances the climate by one step at a given sim time, working out the planet's rotation
        /// for that moment from the clock's day and year.
        /// </summary>
        /// <param name="clock">Gives the spin angle and axis lean at any time.</param>
        /// <param name="lampPosition">World position of the lamp.</param>
        /// <param name="simTimeSeconds">Sim time of this step. Each step of a multi-step frame passes its own time.</param>
        public void StepAtTime(RenderTexture terrain, Field climate, RenderTexture soil, SimClock clock, Vector3 lampPosition, float deltaSeconds, double simTimeSeconds)
        {
            Vector3 centre = config.PlanetRestCentre;
            Quaternion rotation = PlanetOrientation.Compute(
                clock.SpinAngleRadiansAt(simTimeSeconds),
                clock.LeanRadiansAt(simTimeSeconds),
                lampPosition - centre);

            Step(terrain, climate, soil, new PlanetPose(rotation, centre, lampPosition), deltaSeconds);
        }

        /// <summary>
        /// Runs the climate through the stretch of time BEFORE time zero, with the planet spinning and
        /// the seasons turning as they will, so the world appears already settled instead of starting
        /// at a uniform temperature with moisture still to spread. Uses the configured spin-up length
        /// and ends at time zero.
        /// </summary>
        public void SpinUp(RenderTexture terrain, Field climate, RenderTexture soil, SimClock clock, Vector3 lampPosition)
        {
            SpinUp(terrain, climate, soil, clock, lampPosition, config.climateSpinUpSeconds, 0.0);
        }

        /// <summary>
        /// Runs the climate for <paramref name="durationSeconds"/> of sim time ending at
        /// <paramref name="endTimeSeconds"/>, in 0.1 s steps (coarser than the live 1/60 s but fine for a
        /// settling run; sub-stepping keeps diffusion stable).
        /// </summary>
        public void SpinUp(RenderTexture terrain, Field climate, RenderTexture soil, SimClock clock, Vector3 lampPosition, float durationSeconds, double endTimeSeconds)
        {
            const float stepSeconds = 0.1f;
            int steps = Mathf.RoundToInt(durationSeconds / stepSeconds);

            for (int i = 0; i < steps; i++)
            {
                double time = endTimeSeconds - (steps - i) * (double)stepSeconds;
                StepAtTime(terrain, climate, soil, clock, lampPosition, stepSeconds, time);
            }
        }

        private void SetParameters(int resolution, float deltaSeconds)
        {
            shader.SetInt(ResolutionId, resolution);
            shader.SetFloat(DeltaTimeId, deltaSeconds);
            shader.SetFloat(PlanetRadiusId, config.planetRadius);
            shader.SetFloat(BaseTemperatureId, config.baseTemperature);
            shader.SetFloat(SunGainId, config.sunGain);
            shader.SetFloat(AltitudeLapseId, config.altitudeLapse);
            shader.SetFloat(LatitudeCoolingId, config.latitudeCooling);
            shader.SetFloat(LandResponseId, config.landResponse);
            shader.SetFloat(OceanResponseId, config.oceanResponse);
            shader.SetFloat(HeatDiffusionId, config.heatDiffusion);
            shader.SetFloat(MoistureDiffusionId, config.moistureDiffusion);
            shader.SetFloat(EvaporationId, config.evaporation);
            shader.SetFloat(WeatherRateId, config.weatherRate);
            shader.SetFloat(NutrientCapId, config.nutrientCap);
        }

        private void Dispatch(int kernel, int resolution)
        {
            int groups = (resolution + ThreadGroupSize - 1) / ThreadGroupSize;
            shader.Dispatch(kernel, groups, groups, CubeSphere.FaceCount);
        }
    }
}
