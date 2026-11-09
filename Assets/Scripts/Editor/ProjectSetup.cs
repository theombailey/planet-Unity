// =============================================================================
// ProjectSetup (editor only)
// -----------------------------------------------------------------------------
// Purpose : Creates the project's data assets and its scene from code, so the
//           whole project can be rebuilt from the scripts alone and nothing
//           depends on hand-clicked editor setup.
// Idea    : - SimConfig and the Low / Medium / High QualityPreset assets are
//             created in Assets/Settings/Terrarium if they are missing.
//             Existing assets are never overwritten, so tuning is kept.
//           - The scene Assets/Scenes/Terrarium.unity is built from code and
//             set as the first scene in Build Settings.
//           Both run automatically once when the editor loads and something is
//           missing, and can be run by hand from the Terrarium menu.
// Presets : Medium uses the spec's defaults and is meant to be safe on an
//           integrated or mid-range GPU. Low and High are first guesses to be
//           tuned once the capability report from the demo machine is known.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Terrarium.EditorTools
{
    /// <summary>Builds the default assets and the Terrarium scene from code.</summary>
    public static class ProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings/Terrarium";
        private const string ConfigPath = SettingsFolder + "/SimConfig.asset";
        private const string ScenePath = "Assets/Scenes/Terrarium.unity";

        private static readonly string[] PresetPaths =
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
                if (!File.Exists(ScenePath))
                {
                    RebuildScene();
                }
            };
        }

        /// <summary>Creates SimConfig and the three quality presets if they do not exist yet.</summary>
        [MenuItem("Terrarium/Create Default Assets")]
        public static void CreateDefaultAssets()
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(SettingsFolder).Replace('\\', '/'), Path.GetFileName(SettingsFolder));
            }

            if (AssetDatabase.LoadAssetAtPath<SimConfig>(ConfigPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SimConfig>(), ConfigPath);
            }

            CreatePresetIfMissing(PresetPaths[0], "Low", fieldResolution: 128, physarumAgents: 100_000, boidCount: 4_000, maxTrees: 150, maxSegmentsPerTree: 600, meshRingSides: 5);
            CreatePresetIfMissing(PresetPaths[1], "Medium", fieldResolution: 256, physarumAgents: 400_000, boidCount: 15_000, maxTrees: 300, maxSegmentsPerTree: 1_200, meshRingSides: 6);
            CreatePresetIfMissing(PresetPaths[2], "High", fieldResolution: 512, physarumAgents: 1_000_000, boidCount: 40_000, maxTrees: 500, maxSegmentsPerTree: 2_500, meshRingSides: 8);

            AssetDatabase.SaveAssets();
        }

        /// <summary>Builds the Terrarium scene from scratch and makes it the first scene in Build Settings.</summary>
        [MenuItem("Terrarium/Rebuild Scene")]
        public static void RebuildScene()
        {
            CreateDefaultAssets();

            // Build in a separate (additive) scene so whatever the user has open is left untouched.
            // Unity cannot add a scene next to an unsaved "Untitled" one, so in that case
            // offer to save it and build in its place instead.
            Scene previousActive = SceneManager.GetActiveScene();
            bool previousWasDirty = previousActive.isDirty;
            bool buildAdditively = !string.IsNullOrEmpty(previousActive.path);
            if (!buildAdditively && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, buildAdditively ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            BuildCamera();
            BuildSimulationRoot();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, enabled: true) };

            if (buildAdditively)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, removeScene: true);

                // Switch to the new scene only if that cannot lose any of the user's unsaved work.
                if (!previousWasDirty)
                {
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }
            }

            Debug.Log($"Terrarium: built {ScenePath}.");
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();

            SimConfig config = AssetDatabase.LoadAssetAtPath<SimConfig>(ConfigPath);
            camera.nearClipPlane = config.cameraNearClip;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.025f, 0.02f);
            cameraObject.transform.position = new Vector3(0f, 2f, -5f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.4f, 0f));
        }

        private static void BuildSimulationRoot()
        {
            var rootObject = new GameObject("Simulation");
            var simRoot = rootObject.AddComponent<SimRoot>();
            var debugPanel = rootObject.AddComponent<DebugPanel>();

            // The fields are private + [SerializeField], so they are set through SerializedObject,
            // the same route the Inspector uses.
            var serializedRoot = new SerializedObject(simRoot);
            serializedRoot.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SimConfig>(ConfigPath);
            SerializedProperty presets = serializedRoot.FindProperty("presets");
            presets.arraySize = PresetPaths.Length;
            for (int i = 0; i < PresetPaths.Length; i++)
            {
                presets.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<QualityPreset>(PresetPaths[i]);
            }
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            var serializedPanel = new SerializedObject(debugPanel);
            serializedPanel.FindProperty("simRoot").objectReferenceValue = simRoot;
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();
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
