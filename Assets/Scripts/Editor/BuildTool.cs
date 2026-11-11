// =============================================================================
// BuildTool (editor only)
// -----------------------------------------------------------------------------
// Purpose : Makes the standalone Windows game with one menu click
//           (Terrarium > Build Windows Player), so the demo can be handed in
//           and played without the Unity editor.
// Idea    : Calls Unity's BuildPipeline on the scenes listed in the Build
//           Settings, writes Builds/Terrarium/Terrarium.exe, and reports
//           the result. Nothing else is changed.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Terrarium.EditorTools
{
    /// <summary>Builds the Windows standalone player.</summary>
    public static class BuildTool
    {
        private const string OutputFolder = "Builds/Terrarium";

        /// <summary>Menu entry: builds and shows the outcome in the console.</summary>
        [MenuItem("Terrarium/Build Windows Player")]
        public static void BuildMenu()
        {
            Debug.Log(BuildWindows());
        }

        /// <summary>Builds the player. Returns a one-line summary of the outcome.</summary>
        public static string BuildWindows()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            if (scenes.Count == 0)
            {
                return "build failed: no scenes are enabled in the Build Settings";
            }

            Directory.CreateDirectory(OutputFolder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = OutputFolder + "/Terrarium.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            return summary.result == BuildResult.Succeeded
                ? $"build ok: {options.locationPathName}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s"
                : $"build {summary.result}: {summary.totalErrors} errors (see the Console)";
        }
    }
}
