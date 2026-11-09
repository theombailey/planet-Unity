// =============================================================================
// WorldHarness (test helper)
// -----------------------------------------------------------------------------
// Purpose : Builds a small simulated world (terrain, climate, soil fields) and
//           runs the real terrain and climate code on it, so tests can ask
//           "what does the climate look like after a year?" without a scene.
// Idea    : The same classes the game uses (TerrainGenerator, ClimateSimulation,
//           PlanetOrientation) are driven directly. The lamp and planet are put
//           where the scene puts them. Results are read back to plain arrays
//           indexed  [(face * N + y) * N + x) * channels + channel].
// =============================================================================

using System;
using Terrarium.Climate;
using Terrarium.Sphere;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Tests
{
    /// <summary>A small world for tests: fields plus the code that generates and steps them.</summary>
    internal sealed class WorldHarness : IDisposable
    {
        private readonly TerrainGenerator terrainGenerator;
        private readonly ClimateSimulation simulation;

        /// <summary>Creates the fields and loads the compute shaders. Tweak <paramref name="configure"/> to change parameters.</summary>
        public WorldHarness(int resolution, Action<SimConfig> configure = null)
        {
            Resolution = resolution;
            Config = ScriptableObject.CreateInstance<SimConfig>();
            configure?.Invoke(Config);

            var fieldInit = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/FieldInit.compute");
            var climateShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Compute/Climate.compute");
            if (fieldInit == null || climateShader == null)
            {
                throw new InvalidOperationException("FieldInit.compute or Climate.compute not found.");
            }

            FieldInit = fieldInit;
            terrainGenerator = new TerrainGenerator(fieldInit);
            simulation = new ClimateSimulation(climateShader, Config);
            Clock = new SimClock(Config);
            LampPosition = Config.lampLightPosition;

            Terrain = new Field(FieldId.Terrain, resolution, RenderTextureFormat.ARGBFloat, pingPong: false);
            Climate = new Field(FieldId.Climate, resolution, RenderTextureFormat.ARGBFloat, pingPong: true);
            Soil = new Field(FieldId.Soil, resolution, RenderTextureFormat.ARGBFloat, pingPong: true);
            Clear(Terrain);
            Clear(Climate);
            Clear(Soil);
        }

        public SimConfig Config { get; }

        public int Resolution { get; }

        public ComputeShader FieldInit { get; }

        public SimClock Clock { get; }

        public Vector3 LampPosition { get; }

        public Field Terrain { get; }

        public Field Climate { get; }

        public Field Soil { get; }

        /// <summary>Generates terrain and a starting climate, and (optionally) runs the spin-up.</summary>
        public void Generate(int seed, float landFraction, bool spinUp)
        {
            terrainGenerator.Generate(Terrain.Current, Config, seed, landFraction);
            foreach (RenderTexture buffer in Climate.Buffers)
            {
                simulation.Initialise(Terrain.Current, buffer);
            }

            if (spinUp)
            {
                simulation.SpinUp(Terrain.Current, Climate, Soil.Current, Clock, LampPosition);
            }
        }

        /// <summary>Runs the climate from one sim time to another in steps of <paramref name="stepSeconds"/>.</summary>
        public void Run(double fromSeconds, double toSeconds, float stepSeconds)
        {
            for (double t = fromSeconds; t < toSeconds - 1e-9; t += stepSeconds)
            {
                simulation.StepAtTime(Terrain.Current, Climate, Soil.Current, Clock, LampPosition, stepSeconds, t);
            }
        }

        /// <summary>Reads a whole field back to the CPU as floats: 4 per texel (or 1 for an R-only field).</summary>
        public float[] Read(RenderTexture texture)
        {
            int channels = texture.format == RenderTextureFormat.RFloat ? 1 : 4;
            var result = new float[CubeSphere.FaceCount * Resolution * Resolution * channels];

            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0);
            request.WaitForCompletion();
            if (request.hasError)
            {
                throw new InvalidOperationException("GPU readback failed.");
            }

            int perFace = Resolution * Resolution * channels;
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                NativeArray<float> slice = request.GetData<float>(face);
                NativeArray<float>.Copy(slice, 0, result, face * perFace, perFace);
            }

            return result;
        }

        /// <summary>Index of a texel's first channel in an array returned by <see cref="Read"/>.</summary>
        public int Index(int face, int x, int y, int channels = 4)
        {
            return ((face * Resolution + y) * Resolution + x) * channels;
        }

        /// <summary>Calls <paramref name="visit"/> for every texel with its direction in the planet's own frame.</summary>
        public void ForEachTexel(Action<int, int, int, Vector3> visit)
        {
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int y = 0; y < Resolution; y++)
                {
                    for (int x = 0; x < Resolution; x++)
                    {
                        visit(face, x, y, CubeSphere.TexelCentreDir(face, x, y, Resolution));
                    }
                }
            }
        }

        public void Dispose()
        {
            Terrain.Release();
            Climate.Release();
            Soil.Release();
            UnityEngine.Object.DestroyImmediate(Config);
        }

        private void Clear(Field field)
        {
            int kernel = FieldInit.FindKernel("Clear");
            int groups = (Resolution + 7) / 8;
            FieldInit.SetInt("_Resolution", Resolution);

            foreach (RenderTexture buffer in field.Buffers)
            {
                FieldInit.SetTexture(kernel, "_Target", buffer);
                FieldInit.Dispatch(kernel, groups, groups, CubeSphere.FaceCount);
            }
        }
    }
}
