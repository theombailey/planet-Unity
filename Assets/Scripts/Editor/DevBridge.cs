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
