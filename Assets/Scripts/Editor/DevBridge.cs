// =============================================================================
// DevBridge (editor only, development aid)
// -----------------------------------------------------------------------------
// Purpose : Lets a tool outside the editor (a script, or the AI assistant that
//           helped write this project) ask Unity to do a few fixed things and
//           see the result, without clicking in the editor.
// Idea    : Twice a second the editor looks for a one-line text file,
//               Logs/DevBridge/command.txt
//           runs the command it names, deletes the file, and writes the outcome
//           to Logs/DevBridge/result.txt. Only the fixed commands below exist;
//           nothing in the file is ever executed as code.
// Commands: refresh          re-import changed files and recompile scripts
//           rebuild-scene    run SceneBuilder.RebuildScene
//           reset-config     overwrite the SimConfig asset with the code defaults
//           view on|off [field] [channel] [edges on|off] [grid on|off]   field debug view
//           paint corner|edge radius amount   paint at the cube corner/edge facing the camera
//           camera yaw pitch distance         jump the desk camera
//           preset <index>                    switch quality preset (0 Low, 1 Medium, 2 High)
//           probe <field> <face> <x> <y>      read one field texel back from the GPU
//           fx <name> on|off ...   painterly switches: bands hatch outlines paper boil
//           ink on|off      enable / disable the fullscreen ink + paper pass
//           shader-errors    list compile errors / warnings of every shader in Assets/Shaders
//           capture [w h]    render the main camera to Logs/DevBridge/capture.png
//           run-tests        run all EditMode tests, write a pass/fail summary
//           play / stop      enter / leave Play mode
// Note    : The Logs folder is git-ignored, so none of these files are part of
//           the project. This class is not used by the game itself.
// =============================================================================

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Terrarium.EditorTools
{
    /// <summary>Runs fixed editor commands requested through a text file.</summary>
    [InitializeOnLoad]
    public static class DevBridge
    {
        private const string Folder = "Logs/DevBridge";
        private const string CommandPath = Folder + "/command.txt";
        private const string ResultPath = Folder + "/result.txt";
        private const string ReadyPath = Folder + "/ready.txt";
        private const string CapturePath = Folder + "/capture.png";
        private const double PollIntervalSeconds = 0.5;

        private static double nextPollTime;
        private static TestRunnerApi testRunner;

        static DevBridge()
        {
            Directory.CreateDirectory(Folder);
            // Rewritten after every script reload, so an outside tool can tell a recompile has finished.
            File.WriteAllText(ReadyPath, DateTime.Now.ToString("O"));
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPollTime)
            {
                return;
            }

            nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            if (!File.Exists(CommandPath))
            {
                return;
            }

            string command = File.ReadAllText(CommandPath).Trim();
            File.Delete(CommandPath);

            try
            {
                Execute(command);
            }
            catch (Exception exception)
            {
                WriteResult($"error: {command}\n{exception}");
            }
        }

        private static void Execute(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0] : string.Empty;

            switch (verb)
            {
                case "refresh":
                    WriteResult("ok: refresh started");
                    AssetDatabase.Refresh();
                    break;

                case "rebuild-scene":
                    SceneBuilder.RebuildScene();
                    WriteResult("ok: scene rebuilt");
                    break;

                case "view":
                    WriteResult(SetFieldView(parts));
                    break;

                case "paint":
                    WriteResult(PaintAtCubeFeature(parts));
                    break;

                case "preset":
                    var simRoot = UnityEngine.Object.FindFirstObjectByType<SimRoot>();
                    if (simRoot == null || parts.Length < 2 || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: usage 'preset index' in Play mode");
                        break;
                    }

                    simRoot.SelectPreset(int.Parse(parts[1]));
                    WriteResult($"ok: preset {simRoot.ActivePreset.label}, field resolution {simRoot.ActivePreset.fieldResolution}");
                    break;

                case "probe":
                    WriteResult(ProbeField(parts));
                    break;

                case "camera":
                    WriteResult(MoveCamera(parts));
                    break;

                case "fx":
                    WriteResult(SetPainterlyToggles(parts));
                    break;

                case "ink":
                    WriteResult(SetInkPass(parts.Length > 1 && parts[1] == "on"));
                    break;

                case "shader-errors":
                    WriteResult(ShaderMessages());
                    break;

                case "reset-config":
                    ProjectSetup.ResetConfigToCodeDefaults();
                    WriteResult("ok: SimConfig reset to code defaults");
                    break;

                case "capture":
                    int width = parts.Length > 2 ? int.Parse(parts[1]) : 1280;
                    int height = parts.Length > 2 ? int.Parse(parts[2]) : 720;
                    WriteResult(Capture(width, height));
                    break;

                case "run-tests":
                    RunEditModeTests();
                    break;

                case "play":
                    EditorApplication.isPlaying = true;
                    WriteResult("ok: entering play mode");
                    break;

                case "stop":
                    EditorApplication.isPlaying = false;
                    WriteResult("ok: leaving play mode");
                    break;

                default:
                    WriteResult($"error: unknown command '{command}'");
                    break;
            }
        }

        /// <summary>
        /// "view on|off [field] [channel] [edges on|off] [grid on|off]": controls the field debug view (Play mode).
        /// field = terrain|climate|soil|trail|brush, channel = 0..4.
        /// </summary>
        private static string SetFieldView(string[] parts)
        {
            var view = UnityEngine.Object.FindFirstObjectByType<Terrarium.Sphere.FieldDebugView>();
            if (view == null || !EditorApplication.isPlaying)
            {
                return "error: needs Play mode and a FieldDebugView";
            }

            view.SetShowing(parts.Length > 1 && parts[1] == "on");
            if (parts.Length > 2 && System.Enum.TryParse(parts[2], true, out Terrarium.Sphere.FieldId field))
            {
                view.SelectField(field);
            }

            if (parts.Length > 3)
            {
                view.SelectChannel(int.Parse(parts[3]));
            }

            for (int i = 4; i + 1 < parts.Length; i += 2)
            {
                bool value = parts[i + 1] == "on";
                if (parts[i] == "edges")
                {
                    view.ShowBoundaries = value;
                }
                else if (parts[i] == "grid")
                {
                    view.ShowGrid = value;
                }
            }

            return $"ok: view showing={view.Showing} field={view.ViewedField} channel={view.ViewedChannel}";
        }

        /// <summary>
        /// "paint corner|edge radiusWorld amount": paints the viewed field at the cube corner (or edge midpoint)
        /// that currently faces the camera most directly, so the blob straddles a seam you can see.
        /// </summary>
        private static string PaintAtCubeFeature(string[] parts)
        {
            var brush = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.DebugBrush>();
            var motion = UnityEngine.Object.FindFirstObjectByType<Terrarium.Climate.PlanetMotion>();
            Camera camera = Camera.main;
            if (brush == null || motion == null || camera == null || !EditorApplication.isPlaying || parts.Length < 4)
            {
                return "error: usage 'paint corner|edge radius amount' in Play mode";
            }

            bool corner = parts[1] == "corner";
            float radius = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            float amount = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);

            // Candidate points in the planet's local frame: the 8 cube corners or the 12 edge midpoints.
            Vector3 towardCamera = (camera.transform.position - motion.transform.position).normalized;
            Quaternion inverse = Quaternion.Inverse(motion.transform.rotation);
            Vector3 localToCamera = inverse * towardCamera;

            Vector3 best = Vector3.up;
            float bestDot = -2f;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int nonZero = (x != 0 ? 1 : 0) + (y != 0 ? 1 : 0) + (z != 0 ? 1 : 0);
                        if (nonZero != (corner ? 3 : 2))
                        {
                            continue;
                        }

                        Vector3 candidate = new Vector3(x, y, z).normalized;
                        float facing = Vector3.Dot(candidate, localToCamera);
                        if (facing > bestDot)
                        {
                            bestDot = facing;
                            best = candidate;
                        }
                    }
                }
            }

            brush.PaintAtLocalDirection(best, radius, amount);
            return $"ok: painted {(corner ? "corner" : "edge")} at local {best}, facing dot {bestDot:0.00}";
        }

        /// <summary>"probe field face x y": reads one texel of a field back from the GPU (Play mode).</summary>
        private static string ProbeField(string[] parts)
        {
            var fieldSet = UnityEngine.Object.FindFirstObjectByType<Terrarium.Sphere.FieldSet>();
            if (fieldSet == null || parts.Length < 5 || !EditorApplication.isPlaying
                || !System.Enum.TryParse(parts[1], true, out Terrarium.Sphere.FieldId id))
            {
                return "error: usage 'probe field face x y' in Play mode";
            }

            RenderTexture texture = fieldSet.Get(id).Current;
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(texture, 0);
            request.WaitForCompletion();
            if (request.hasError)
            {
                return "error: readback failed";
            }

            int face = int.Parse(parts[2]);
            int x = int.Parse(parts[3]);
            int y = int.Parse(parts[4]);
            Unity.Collections.NativeArray<float> slice = request.GetData<float>(face);
            int channels = texture.format == RenderTextureFormat.RFloat ? 1 : 4;
            int index = (y * texture.width + x) * channels;

            var text = new StringBuilder($"ok: {id} face {face} ({x},{y}) =");
            for (int c = 0; c < channels; c++)
            {
                text.Append($" {slice[index + c]:0.0000}");
            }

            return text.ToString();
        }

        /// <summary>"camera yaw pitch distance": jumps the desk camera to a pose (Play mode).</summary>
        private static string MoveCamera(string[] parts)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
            if (rig == null || parts.Length < 4 || !EditorApplication.isPlaying)
            {
                return "error: usage 'camera yaw pitch distance' in Play mode";
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            rig.SnapTo(float.Parse(parts[1], culture), float.Parse(parts[2], culture), float.Parse(parts[3], culture));
            return "ok: camera moved";
        }

        /// <summary>
        /// Sets the painterly effect switches on the scene's PainterlySettings, e.g. "fx bands off hatch off".
        /// Names: bands, hatch, outlines, paper, boil. Works in Play mode (the same switches the F1 panel flips).
        /// </summary>
        private static string SetPainterlyToggles(string[] parts)
        {
            var settings = UnityEngine.Object.FindFirstObjectByType<Terrarium.Rendering.PainterlySettings>();
            if (settings == null)
            {
                return "error: no PainterlySettings in the open scene";
            }

            for (int i = 1; i + 1 < parts.Length; i += 2)
            {
                bool value = parts[i + 1] == "on";
                switch (parts[i])
                {
                    case "bands": settings.BandsEnabled = value; break;
                    case "hatch": settings.HatchingEnabled = value; break;
                    case "outlines": settings.OutlinesEnabled = value; break;
                    case "paper": settings.PaperEnabled = value; break;
                    case "boil": settings.BoilEnabled = value; break;
                    default: return $"error: unknown effect '{parts[i]}'";
                }
            }

            settings.Apply();
            return "ok: painterly toggles set. Shader globals now: " +
                   $"bands={Shader.GetGlobalFloat("_PainterlyBandsEnabled")} hatch={Shader.GetGlobalFloat("_PainterlyHatchEnabled")} " +
                   $"outlines={Shader.GetGlobalFloat("_PainterlyOutlinesEnabled")} paper={Shader.GetGlobalFloat("_PainterlyPaperEnabled")} " +
                   $"boilSeed={Shader.GetGlobalFloat("_PainterlyBoilSeed")}";
        }

        /// <summary>Turns the fullscreen ink + paper renderer feature on or off, to tell it apart from the surface shaders.</summary>
        private static string SetInkPass(bool active)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(ProjectSetup.RendererPath);
            foreach (UnityEngine.Rendering.Universal.ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature is Terrarium.Rendering.InkPaperFeature)
                {
                    feature.SetActive(active);
                    EditorUtility.SetDirty(renderer);
                    return $"ok: ink pass active = {active}";
                }
            }

            return "error: ink pass feature not found in the renderer";
        }

        /// <summary>Lists compile errors and warnings for every shader under Assets/Shaders.</summary>
        private static string ShaderMessages()
        {
            var report = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                report.AppendLine($"{path}: supported={shader.isSupported}, errors={ShaderUtil.ShaderHasError(shader)}");
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                {
                    report.AppendLine($"  {message.severity} {message.file}:{message.line}  {message.message}  [{message.platform}]");
                }
            }

            return report.ToString();
        }

        private static string Capture(int width, int height)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return "error: no camera tagged MainCamera in the open scene";
            }

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                // Ask URP for one standalone render of this camera into our texture. (The older
                // Camera.Render() call only produced the clear colour under URP in edit mode.)
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    return "error: the active render pipeline does not support single-camera render requests";
                }

                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                // The target is an sRGB texture, so the bytes read back are already display-ready.
                // (Check: the background pixel equals SimConfig.backgroundColor * 255.)
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(CapturePath, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }

            return $"ok: captured {width}x{height} from '{camera.name}' (playing: {EditorApplication.isPlaying})";
        }

        private static void RunEditModeTests()
        {
            if (testRunner == null)
            {
                testRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
                testRunner.RegisterCallbacks(new TestSummaryWriter());
            }

            WriteResult("ok: tests started");
            testRunner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private static void WriteResult(string text)
        {
            File.WriteAllText(ResultPath, text);
        }

        /// <summary>Writes a short pass/fail summary when a test run finishes.</summary>
        private sealed class TestSummaryWriter : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var summary = new StringBuilder();
                summary.AppendLine($"tests finished: {result.TestStatus}  passed {result.PassCount}  failed {result.FailCount}  skipped {result.SkipCount}");
                AppendFailures(result, summary);
                WriteResult(summary.ToString());
            }

            private static void AppendFailures(ITestResultAdaptor result, StringBuilder summary)
            {
                if (result.HasChildren)
                {
                    foreach (ITestResultAdaptor child in result.Children)
                    {
                        AppendFailures(child, summary);
                    }
                }
                else if (result.TestStatus == TestStatus.Failed)
                {
                    summary.AppendLine($"FAILED {result.Name}: {result.Message}");
                }
            }
        }
    }
}
