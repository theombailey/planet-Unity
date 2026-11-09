// =============================================================================
// CapabilityReport
// -----------------------------------------------------------------------------
// Purpose : Logs what this machine's GPU can do, once, when Play starts.
// Idea    : The whole sim runs in compute shaders on Texture2DArray fields with
//           async readback. Before building any of that we need to know the
//           machine supports it, and how strong the GPU is, so the quality
//           presets (agent counts, field resolution) can be set sensibly.
// Output  : One multi-line message in the Console, and the same text saved to
//           a file so it is easy to copy:
//             in the editor  -> <project>/Logs/CapabilityReport.txt
//             in a build     -> <persistentDataPath>/CapabilityReport.txt
// =============================================================================

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium
{
    /// <summary>Gathers and logs the graphics capabilities the simulation depends on.</summary>
    public static class CapabilityReport
    {
        private const string FileName = "CapabilityReport.txt";

        /// <summary>Builds the report text. Pure query: logs nothing and writes nothing.</summary>
        public static string Build()
        {
            var text = new StringBuilder(1024);
            text.AppendLine("=== TERRARIUM CAPABILITY REPORT ===");
            text.AppendLine($"Unity version            : {Application.unityVersion}");
            text.AppendLine($"Platform                 : {Application.platform}");
            text.AppendLine($"Operating system         : {SystemInfo.operatingSystem}");
            text.AppendLine($"CPU                      : {SystemInfo.processorType} ({SystemInfo.processorCount} threads)");
            text.AppendLine($"System memory            : {SystemInfo.systemMemorySize} MB");
            text.AppendLine($"Graphics API             : {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceVersion})");
            text.AppendLine($"GPU                      : {SystemInfo.graphicsDeviceName} [{SystemInfo.graphicsDeviceVendor}]");
            text.AppendLine($"VRAM                     : {SystemInfo.graphicsMemorySize} MB");
            text.AppendLine($"Render pipeline          : {DescribeRenderPipeline()}");
            text.AppendLine($"Compute shaders          : {SystemInfo.supportsComputeShaders}");
            text.AppendLine($"Max compute group size   : {SystemInfo.maxComputeWorkGroupSize} total " +
                            $"({SystemInfo.maxComputeWorkGroupSizeX} x {SystemInfo.maxComputeWorkGroupSizeY} x {SystemInfo.maxComputeWorkGroupSizeZ})");
            text.AppendLine($"Async GPU readback       : {SystemInfo.supportsAsyncGPUReadback}");
            text.AppendLine($"2D array textures        : {SystemInfo.supports2DArrayTextures}");
            text.AppendLine($"Max texture array slices : {SystemInfo.maxTextureArraySlices}");
            text.AppendLine($"Max texture size         : {SystemInfo.maxTextureSize}");
            text.AppendLine($"GPU instancing           : {SystemInfo.supportsInstancing}");
            text.AppendLine($"Random write RFloat      : {SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat)}");
            text.AppendLine($"Random write ARGBHalf    : {SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf)}");
            text.AppendLine($"Random write ARGBFloat   : {SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat)}");
            text.Append("===================================");
            return text.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LogOnStartup()
        {
            string report = Build();
            Debug.Log(report);
            TrySaveToFile(report);
        }

        private static string DescribeRenderPipeline()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline != null ? $"{pipeline.GetType().Name} ({pipeline.name})" : "Built-in (no SRP asset!)";
        }

        private static void TrySaveToFile(string report)
        {
            try
            {
                string folder = Application.isEditor
                    ? Path.Combine(Application.dataPath, "..", "Logs")
                    : Application.persistentDataPath;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, FileName), report);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Saving is a convenience only; the Console message above is the real report.
                Debug.LogWarning($"CapabilityReport: could not save the report file ({exception.Message}).");
            }
        }
    }
}
