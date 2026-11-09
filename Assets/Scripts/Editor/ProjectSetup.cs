// =============================================================================
// ProjectSetup (editor only)
// -----------------------------------------------------------------------------
// Purpose : Creates the project's data assets from code, so the whole project
//           can be rebuilt from the scripts alone and nothing depends on
//           hand-clicked editor setup.
// Idea    : SimConfig and the Low / Medium / High QualityPreset assets are
//           created in Assets/Settings/Terrarium if they are missing. Existing
//           assets are never overwritten, so tuning is kept.
//           This runs automatically when the editor loads (and builds the scene
//           too if it is missing), and by hand from the Terrarium menu.
// Presets : Medium uses the spec's defaults. The development machine is an
//           RTX 2060 (6 GB), which Medium should run comfortably; Low and High
//           are first guesses to be tuned once the heavy systems exist and
//           there are real frame times to measure.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Terrarium.EditorTools
{
    /// <summary>Creates the default SimConfig and QualityPreset assets.</summary>
    public static class ProjectSetup
    {
        /// <summary>Folder holding the generated settings assets.</summary>
        public const string SettingsFolder = "Assets/Settings/Terrarium";

        /// <summary>Asset path of the SimConfig.</summary>
        public const string ConfigPath = SettingsFolder + "/SimConfig.asset";

        /// <summary>Asset paths of the quality presets, ordered light to heavy.</summary>
        public static readonly string[] PresetPaths =
        {
            SettingsFolder + "/Quality_Low.asset",
            SettingsFolder + "/Quality_Medium.asset",
            SettingsFolder + "/Quality_High.asset",
        };

        [InitializeOnLoadMethod]
        private static void EnsureOnEditorLoad()
        {
            // Asset work is not allowed during the load callback itself, so wait one editor tick.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                CreateDefaultAssets();
                if (!File.Exists(SceneBuilder.ScenePath))
                {
                    SceneBuilder.RebuildScene();
                }
            };
        }

        /// <summary>Creates SimConfig and the three quality presets if they do not exist yet.</summary>
        [MenuItem("Terrarium/Create Default Assets")]
        public static void CreateDefaultAssets()
        {
            SceneBuilder.EnsureFolder(SettingsFolder);

            if (AssetDatabase.LoadAssetAtPath<SimConfig>(ConfigPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SimConfig>(), ConfigPath);
            }

            CreatePresetIfMissing(PresetPaths[0], "Low", fieldResolution: 128, physarumAgents: 100_000, boidCount: 4_000, maxTrees: 150, maxSegmentsPerTree: 600, meshRingSides: 5);
            CreatePresetIfMissing(PresetPaths[1], "Medium", fieldResolution: 256, physarumAgents: 400_000, boidCount: 15_000, maxTrees: 300, maxSegmentsPerTree: 1_200, meshRingSides: 6);
            CreatePresetIfMissing(PresetPaths[2], "High", fieldResolution: 512, physarumAgents: 1_000_000, boidCount: 40_000, maxTrees: 500, maxSegmentsPerTree: 2_500, meshRingSides: 8);

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Overwrites the SimConfig asset with the default values written in SimConfig.cs.
        /// Unity remembers an asset's values once it has seen them, so editing a default in code
        /// does not change an existing asset; this is the explicit way to say "use the code's values".
        /// Any tuning done in the Inspector is lost, so it only ever runs on request.
        /// </summary>
        [MenuItem("Terrarium/Reset SimConfig To Code Defaults")]
        public static void ResetConfigToCodeDefaults()
        {
            CreateDefaultAssets();
            SimConfig asset = AssetDatabase.LoadAssetAtPath<SimConfig>(ConfigPath);
            SimConfig fresh = ScriptableObject.CreateInstance<SimConfig>();

            // JsonUtility copies only SimConfig's own fields, leaving Unity's bookkeeping for the
            // asset (its name and identity) untouched.
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), asset);
            Object.DestroyImmediate(fresh);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // Reload the asset from the file just written, so memory and disk are guaranteed to match.
            AssetDatabase.ImportAsset(ConfigPath, ImportAssetOptions.ForceUpdate);
        }

        private static void CreatePresetIfMissing(string path, string label, int fieldResolution, int physarumAgents, int boidCount, int maxTrees, int maxSegmentsPerTree, int meshRingSides)
        {
            if (AssetDatabase.LoadAssetAtPath<QualityPreset>(path) != null)
            {
                return;
            }

            var preset = ScriptableObject.CreateInstance<QualityPreset>();
            preset.label = label;
            preset.fieldResolution = fieldResolution;
            preset.physarumAgents = physarumAgents;
            preset.boidCount = boidCount;
            preset.maxTrees = maxTrees;
            preset.maxSegmentsPerTree = maxSegmentsPerTree;
            preset.meshRingSides = meshRingSides;
            AssetDatabase.CreateAsset(preset, path);
        }
    }
}
