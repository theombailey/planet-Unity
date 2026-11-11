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
//           build            make the standalone Windows player (Builds/Terrarium)
//           reset-config     overwrite the SimConfig asset with the code defaults
//           view on|off [field] [channel] [edges on|off] [grid on|off] [range min max]   field debug view
//           paint corner|edge radius amount   paint at the cube corner/edge facing the camera
//           mouse on name|planet [down|up]    move the synthetic mouse onto an object, optionally pressing the button
//           mouse at x y [down|up]            move the synthetic mouse to pixel coordinates
//           key escape                        press Escape
//           hand                              what the tool controller sees: hover, held tool, picked point
//           tool kind land|x y z [seconds]    use a tool at a point (Play mode)
//           fly surface|desk [land|x y z]     smooth camera flight to / from the surface
//           record frames                     record the camera path to check a flight is smooth
//           camera yaw pitch distance         jump the desk camera
//           physarum                          living agent count (Play mode)
//           status                            play / pause state, frame count and frame time
//           timescale 0|0.25|1|4|16          set the sim speed (Play mode)
//           preset <index>                  switch quality preset (0 Low, 1 Medium, 2 High)
//           probe <field> <face> <x> <y>      read one field texel back from the GPU
//           facelamp height pitch             turn the surface camera to face the lamp (the sun, from the surface)
//           press Button_Name                 press a console button directly (Play mode)
//           pickup Tool_Name                  put a tool in the hand directly (Play mode)
//           blend 0..1                        hold the camera between desk view (0) and the surface (1), to inspect the flight
//           rain                              how much of the planet is being rained on, and where it rains hardest
//           fx <name> on|off ...   painterly switches: bands hatch outlines paper boil
//           ink on|off      enable / disable the fullscreen ink + paper pass
//           shader-errors    list compile errors / warnings of every shader in Assets/Shaders
//           capture [w h]    render the main camera to Logs/DevBridge/capture.png
//           run-tests [name] run all EditMode tests (or those whose name contains [name]),
//                            write a pass/fail summary
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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
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

                case "physarum":
                    var physarumSystem = UnityEngine.Object.FindFirstObjectByType<Terrarium.Physarum.PhysarumSystem>();
                    WriteResult(physarumSystem == null
                        ? "error: no PhysarumSystem in the open scene"
                        : $"ok: physarum alive {physarumSystem.AliveCount} of {physarumSystem.PoolSize}, born {physarumSystem.BornLastStep}, died {physarumSystem.DiedLastStep} (last step)");
                    break;

                case "boids":
                    WriteResult(BoidsReport(parts.Length > 1 && parts[1] == "detail"));
                    break;

                case "eco":
                    var statsSystem = UnityEngine.Object.FindFirstObjectByType<StatsSystem>();
                    if (statsSystem == null || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: needs Play mode and a StatsSystem");
                        break;
                    }

                    EcosystemStats e = statsSystem.Latest;
                    WriteResult($"eco year={e.Year} phase={e.YearPhase:0.00} fungus={e.PhysarumAlive} trees={e.TreesAlive}/{e.TreesStanding} birds={e.BoidsAlive} " +
                                $"leaf={e.MeanLeafCover:0.00} canopy={e.MeanCanopy:0.000} temp={e.MeanTemperature:0.0} nutrients={e.MeanNutrients:0.000} " +
                                $"organic={e.MeanOrganic:0.000} trail={e.MeanTrail:0.000} birdEnergy={e.BoidMeanEnergy:0.0} valid={e.Valid}");
                    break;

                case "stability":
                    var runner = UnityEngine.Object.FindFirstObjectByType<StabilityRunner>();
                    if (runner == null || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: needs Play mode and a StabilityRunner");
                        break;
                    }

                    if (parts.Length > 1 && parts[1] == "start")
                    {
                        runner.Begin();
                    }

                    WriteResult(runner.Summary + "\n" + runner.Report);
                    break;

                case "status":
                    WriteResult($"playing={EditorApplication.isPlaying} paused={EditorApplication.isPaused} frame={Time.frameCount} " +
                                $"realtime={Time.realtimeSinceStartup:0.0}s unscaledDelta={Time.unscaledDeltaTime * 1000f:0.0}ms focused={UnityEditorInternal.InternalEditorUtility.isApplicationActive}");
                    break;

                case "timescale":
                    var clockOwner = UnityEngine.Object.FindFirstObjectByType<SimRoot>();
                    if (clockOwner == null || parts.Length < 2 || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: usage 'timescale 0|1|4|16' in Play mode");
                        break;
                    }

                    clockOwner.Clock.SetTimeScale(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                    WriteResult($"ok: time scale {clockOwner.Clock.TimeScale}, sim time {clockOwner.Clock.SimTimeSeconds:0.0} s, year {clockOwner.Clock.YearIndex} phase {clockOwner.Clock.YearPhase:0.00}");
                    break;

                case "tool":
                    WriteResult(UseTool(parts));
                    break;

                case "fly":
                    WriteResult(FlyCamera(parts));
                    break;

                case "record":
                    WriteResult(StartCameraRecording(parts));
                    break;

                case "surface":
                    var surfaceRig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
                    if (surfaceRig == null || parts.Length < 6 || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: usage 'surface x y z height pitch' in Play mode");
                        break;
                    }

                    var invariant = System.Globalization.CultureInfo.InvariantCulture;
                    surfaceRig.SnapToSurface(
                        new Vector3(float.Parse(parts[1], invariant), float.Parse(parts[2], invariant), float.Parse(parts[3], invariant)),
                        float.Parse(parts[4], invariant), float.Parse(parts[5], invariant));
                    WriteResult("ok: camera placed on the surface");
                    break;

                case "look":
                    var lookRig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
                    if (lookRig == null || parts.Length < 7 || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: usage 'look tx ty tz distanceRadians height pitch' in Play mode");
                        break;
                    }

                    var lookCulture = System.Globalization.CultureInfo.InvariantCulture;
                    Vector3 target = new Vector3(float.Parse(parts[1], lookCulture), float.Parse(parts[2], lookCulture), float.Parse(parts[3], lookCulture)).normalized;
                    float back = float.Parse(parts[4], lookCulture);
                    Vector3 along = Terrarium.Interaction.SurfaceNavigation.HeadingNear(target, Vector3.up);
                    Vector3 stand = (target * Mathf.Cos(back) - along * Mathf.Sin(back)).normalized;
                    lookRig.SnapToSurface(stand, target, float.Parse(parts[5], lookCulture), float.Parse(parts[6], lookCulture));
                    WriteResult("ok: camera standing near the target, facing it");
                    break;

                case "trees":
                    WriteResult(TreesReport());
                    break;

                case "mouse":
                    WriteResult(MouseCommand(parts));
                    break;

                case "key":
                    WriteResult(KeyCommand(parts));
                    break;

                case "hand":
                    WriteResult(HandState());
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

                case "rain":
                    WriteResult(RainReport());
                    break;

                case "press":
                    WriteResult(PressButton(parts));
                    break;

                case "pickup":
                    WriteResult(PickUpTool(parts));
                    break;

                case "facelamp":
                    WriteResult(FaceLamp(parts));
                    break;

                case "blend":
                    var blendRig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
                    if (blendRig == null || parts.Length < 2 || !EditorApplication.isPlaying)
                    {
                        WriteResult("error: usage 'blend 0..1' in Play mode (hold the camera between desk view and the surface)");
                        break;
                    }

                    blendRig.SnapBlend(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                    WriteResult("ok: camera blend held");
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
                    RunEditModeTests(parts.Length > 1 ? parts[1] : null);
                    break;

                case "play":
                    EditorApplication.isPlaying = true;
                    WriteResult("ok: entering play mode");
                    break;

                case "build":
                    WriteResult(BuildTool.BuildWindows());
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

            // Optional trailing "range min max" overrides the default colour range.
            for (int i = 4; i + 2 < parts.Length; i++)
            {
                if (parts[i] == "range")
                {
                    var culture = System.Globalization.CultureInfo.InvariantCulture;
                    view.SetRange(float.Parse(parts[i + 1], culture), float.Parse(parts[i + 2], culture));
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

        /// <summary>
        /// "tool name land|x y z [seconds]": uses a tool at a point as if the mouse held it there. Continuous
        /// tools run for the given seconds (default 1) in 0.05 s slices; one-shot tools fire once. "land" picks a
        /// highland point (a fixed, repeatable one). Prints the point so field probes can be aimed at it.
        /// </summary>
        private static string UseTool(string[] parts)
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.ToolController>();
            var fieldSet = UnityEngine.Object.FindFirstObjectByType<Terrarium.Sphere.FieldSet>();
            var planetMotion = UnityEngine.Object.FindFirstObjectByType<Terrarium.Climate.PlanetMotion>();
            if (controller == null || fieldSet == null || planetMotion == null || parts.Length < 3 || !EditorApplication.isPlaying
                || !System.Enum.TryParse(parts[1], true, out Terrarium.Interaction.ToolKind kind))
            {
                return "error: usage 'tool kind land|x y z [seconds]' in Play mode (kinds: WateringCan CompostScoop HeatLamp FrostSpray SporeDish Meteor SeedJar BoidCage)";
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            int next = 3;
            Vector3 local;
            if (parts[2] == "land")
            {
                local = FindHighland(fieldSet);
            }
            else
            {
                local = new Vector3(float.Parse(parts[2], culture), float.Parse(parts[3], culture), float.Parse(parts[4], culture)).normalized;
                next = 5;
            }

            float seconds = parts.Length > next ? float.Parse(parts[next], culture) : 1f;
            Vector3 world = planetMotion.transform.position + planetMotion.transform.rotation * local;
            const float slice = 0.05f;

            if (Terrarium.Interaction.ToolInfo.IsContinuous(kind))
            {
                for (float t = 0f; t < seconds - 1e-4f; t += slice)
                {
                    controller.Use(kind, local, world, 3f, slice);
                }
            }
            else
            {
                controller.Use(kind, local, world, 3f, slice);
            }

            Terrarium.Sphere.Texel texel = Terrarium.Sphere.CubeSphere.DirToTexel(local, fieldSet.Resolution);
            return $"ok: used {kind} at local ({local.x:0.000}, {local.y:0.000}, {local.z:0.000}) = texel face {texel.Face} x {texel.X} y {texel.Y}, for {seconds}s";
        }

        // A repeatable high-ground point: the first texel (scanning faces in order) with a height between 0.25 and 0.5.
        private static Vector3 FindHighland(Terrarium.Sphere.FieldSet fieldSet)
        {
            RenderTexture terrain = fieldSet.Get(Terrarium.Sphere.FieldId.Terrain).Current;
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(terrain, 0);
            request.WaitForCompletion();
            int n = terrain.width;

            for (int face = 0; face < 6; face++)
            {
                Unity.Collections.NativeArray<float> slice = request.GetData<float>(face);
                for (int y = n / 4; y < n * 3 / 4; y += 3)
                {
                    for (int x = n / 4; x < n * 3 / 4; x += 3)
                    {
                        float h = slice[(y * n + x) * 4];
                        if (h > 0.25f && h < 0.5f)
                        {
                            return Terrarium.Sphere.CubeSphere.TexelCentreDir(face, x, y, n);
                        }
                    }
                }
            }

            return Vector3.right;
        }

        /// <summary>"fly surface|desk [land | x y z]": starts the smooth camera flight (Play mode).</summary>
        private static string FlyCamera(string[] parts)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
            var fieldSet = UnityEngine.Object.FindFirstObjectByType<Terrarium.Sphere.FieldSet>();
            if (rig == null || fieldSet == null || parts.Length < 2 || !EditorApplication.isPlaying)
            {
                return "error: usage 'fly surface|desk [land|x y z]' in Play mode";
            }

            if (parts[1] == "desk")
            {
                rig.FlyToDesk();
                return "ok: flying to the desk view";
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 local = parts.Length > 2 && parts[2] != "land"
                ? new Vector3(float.Parse(parts[2], culture), float.Parse(parts[3], culture), float.Parse(parts[4], culture)).normalized
                : FindHighland(fieldSet);
            rig.FlyToSurface(local);
            return $"ok: flying to the surface over local ({local.x:0.000}, {local.y:0.000}, {local.z:0.000})";
        }

        private static readonly System.Collections.Generic.List<Vector3> RecordedPositions = new System.Collections.Generic.List<Vector3>();
        private static int recordFramesLeft;

        /// <summary>
        /// "record frames": records the camera position for that many editor frames, then reports the path length,
        /// the largest single-frame jump and how many frames were seen. A smooth flight has a small largest jump.
        /// </summary>
        private static string StartCameraRecording(string[] parts)
        {
            if (!EditorApplication.isPlaying || Camera.main == null || parts.Length < 2)
            {
                return "error: usage 'record frames' in Play mode";
            }

            RecordedPositions.Clear();
            recordFramesLeft = int.Parse(parts[1]);
            EditorApplication.update += RecordFrame;
            return "ok: recording";
        }

        private static void RecordFrame()
        {
            if (!EditorApplication.isPlaying || Camera.main == null)
            {
                EditorApplication.update -= RecordFrame;
                return;
            }

            RecordedPositions.Add(Camera.main.transform.position);
            if (--recordFramesLeft > 0)
            {
                return;
            }

            EditorApplication.update -= RecordFrame;
            float path = 0f;
            float largest = 0f;
            for (int i = 1; i < RecordedPositions.Count; i++)
            {
                float step = Vector3.Distance(RecordedPositions[i], RecordedPositions[i - 1]);
                path += step;
                largest = Mathf.Max(largest, step);
            }

            WriteResult($"ok: recorded {RecordedPositions.Count} frames, path {path:0.000} units, largest single-frame jump {largest:0.0000} " +
                        $"({(path > 0f ? largest / path : 0f):P1} of the path), start {RecordedPositions[0]}, end {RecordedPositions[RecordedPositions.Count - 1]}");
        }

        /// <summary>
        /// "boids [detail]": the boid counters from the last readback; with "detail", also reads the boid buffers right now
        /// (a blocking read, fine for an editor tool) and reports energies, heights and the number of broken (NaN) boids.
        /// </summary>
        private static string BoidsReport(bool detail)
        {
            var system = UnityEngine.Object.FindFirstObjectByType<Terrarium.Boids.BoidSystem>();
            var manager = UnityEngine.Object.FindFirstObjectByType<Terrarium.Trees.TreeManager>();
            if (system == null || !EditorApplication.isPlaying)
            {
                return "error: needs Play mode and a BoidSystem";
            }

            string summary = $"ok: boids alive {system.AliveCount} of {system.PoolSize}, born {system.BornLastStep}, died {system.DiedLastStep} (last step), " +
                             $"seeds dropped {system.SeedsDropped}, trees {(manager != null ? manager.TreeCount : 0)}";
            if (!detail || system.Simulation == null || !system.Simulation.IsAllocated)
            {
                return summary;
            }

            Terrarium.Boids.BoidSimulation simulation = system.Simulation;
            var posEnergy = new Vector4[simulation.PoolSize];
            var velSeed = new Vector4[simulation.PoolSize];
            var alive = new uint[simulation.PoolSize];
            simulation.PosEnergy.GetData(posEnergy);
            simulation.VelSeed.GetData(velSeed);
            simulation.Alive.GetData(alive);

            int live = 0;
            int broken = 0;
            int carrying = 0;
            Vector3 firstDirection = Vector3.up;
            float energySum = 0f;
            float energyMin = float.MaxValue;
            float energyMax = float.MinValue;
            float radiusMin = float.MaxValue;
            float radiusMax = float.MinValue;
            float speedSum = 0f;
            for (int i = 0; i < alive.Length; i++)
            {
                if (alive[i] == 0u)
                {
                    continue;
                }

                Vector4 pe = posEnergy[i];
                if (float.IsNaN(pe.x) || float.IsNaN(pe.w))
                {
                    broken++;
                    continue;
                }

                if (live == 0)
                {
                    firstDirection = new Vector3(pe.x, pe.y, pe.z).normalized;
                }

                live++;
                energySum += pe.w;
                energyMin = Mathf.Min(energyMin, pe.w);
                energyMax = Mathf.Max(energyMax, pe.w);
                float radius = new Vector3(pe.x, pe.y, pe.z).magnitude;
                radiusMin = Mathf.Min(radiusMin, radius);
                radiusMax = Mathf.Max(radiusMax, radius);
                speedSum += new Vector3(velSeed[i].x, velSeed[i].y, velSeed[i].z).magnitude;
                if (velSeed[i].w > 0f)
                {
                    carrying++;
                }
            }

            return live == 0
                ? summary + $"\\n  buffers: no live boids, {broken} broken"
                : summary + $"\\n  buffers: {live} live ({broken} broken), energy mean {energySum / live:0.000} min {energyMin:0.000} max {energyMax:0.000}, " +
                  $"radius {radiusMin:0.000}..{radiusMax:0.000}, mean speed {speedSum / live:0.0000}, carrying seeds {carrying}, " +
                  $"first live boid over ({firstDirection.x:0.000} {firstDirection.y:0.000} {firstDirection.z:0.000})";
        }

        /// <summary>"trees": how many trees stand, how many are alive, and a few details of each of the first ones.</summary>
        private static string TreesReport()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<Terrarium.Trees.TreeManager>();
            if (manager == null || !EditorApplication.isPlaying)
            {
                return "error: needs Play mode and a TreeManager";
            }

            var report = new StringBuilder($"ok: trees {manager.TreeCount} standing, {manager.LiveCount} alive, capacity {manager.Capacity}\\n");
            Terrarium.Trees.TreeDiversity diversity = manager.MeasureDiversity();
            report.AppendLine($"  kinds of tree {diversity.Groups}: optimum {diversity.MinOptimum:0}..{diversity.MaxOptimum:0} C (mean {diversity.MeanOptimum:0}), cones {100f * diversity.ConeShare:0}%, evergreen {100f * diversity.EvergreenShare:0}%, optimum vs latitude r {diversity.OptimumVsLatitude:0.00}");
            int shown = 0;
            for (int slot = 0; slot < manager.Capacity && shown < 8; slot++)
            {
                Terrarium.Trees.Tree tree = manager.TreeAt(slot);
                if (tree == null)
                {
                    continue;
                }

                report.AppendLine($"  slot {slot} at ({tree.Direction.x:0.000} {tree.Direction.y:0.000} {tree.Direction.z:0.000}): nodes {tree.Colony.NodeCount}, leaves {tree.LeafCount}, age {tree.Age:0}s, health {tree.Health:0.00}, growth {tree.GrowthRate:0.00}, " +
                                  $"richness {tree.Richness:0.00}, leaf {tree.LeafAmount:0.00}, autumn {tree.Autumn:0.00}, mature {tree.Mature}, dead {tree.Dead}");
                shown++;
            }

            return report.ToString();
        }

        private static bool leftDown;
        private static Vector2 pointerPosition;

        /// <summary>
        /// "mouse on &lt;ObjectName|planet&gt; [down|up]" or "mouse at x y [down|up]": moves the (synthetic) mouse pointer onto an
        /// object's screen position or to pixel coordinates, optionally pressing or releasing the left button. Uses the
        /// Input System's event queue, so the game sees it exactly like real input.
        /// </summary>
        private static string MouseCommand(string[] parts)
        {
            Camera camera = Camera.main;
            if (!EditorApplication.isPlaying || camera == null || parts.Length < 2)
            {
                return "error: usage 'mouse on name|planet [down|up]' or 'mouse at x y [down|up]' in Play mode";
            }

            if (parts[1] == "scroll" && parts.Length >= 3)
            {
                // One wheel step per call; the wheel is released again a moment later so it counts as a single notch.
                Mouse wheelMouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                float notches = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                InputSystem.QueueStateEvent(wheelMouse, new MouseState { position = pointerPosition, scroll = new Vector2(0f, 120f * notches) });
                EditorApplication.delayCall += () => InputSystem.QueueStateEvent(wheelMouse, new MouseState { position = pointerPosition });
                return $"ok: scrolled {notches} notch(es)";
            }

            Vector2 previousPosition = pointerPosition;
            int next;
            if (parts[1] == "on" && parts.Length >= 3)
            {
                Transform target = parts[2] == "planet"
                    ? UnityEngine.Object.FindFirstObjectByType<Terrarium.Climate.PlanetMotion>().transform
                    : GameObject.Find(parts[2])?.transform;
                if (target == null)
                {
                    return $"error: no object named '{parts[2]}'";
                }

                Vector3 screen = camera.WorldToScreenPoint(target.position);
                pointerPosition = new Vector2(screen.x, screen.y);
                next = 3;
            }
            else if (parts[1] == "at" && parts.Length >= 4)
            {
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                pointerPosition = new Vector2(float.Parse(parts[2], culture), float.Parse(parts[3], culture));
                next = 4;
            }
            else
            {
                return "error: usage 'mouse on name|planet [down|up]' or 'mouse at x y [down|up]'";
            }

            if (parts.Length > next)
            {
                leftDown = parts[next] == "down";
            }

            Mouse mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            var state = new MouseState { position = pointerPosition, delta = pointerPosition - previousPosition };
            if (leftDown)
            {
                state = state.WithButton(MouseButton.Left);
            }

            InputSystem.QueueStateEvent(mouse, state);
            return $"ok: pointer at ({pointerPosition.x:0}, {pointerPosition.y:0}) of {camera.pixelWidth}x{camera.pixelHeight}, left button {(leftDown ? "down" : "up")}";
        }

        /// <summary>"key escape": presses and releases a key through the Input System (only Escape is supported).</summary>
        private static string KeyCommand(string[] parts)
        {
            if (!EditorApplication.isPlaying || parts.Length < 2 || parts[1] != "escape")
            {
                return "error: usage 'key escape' in Play mode";
            }

            Keyboard keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            EditorApplication.delayCall += () => InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            return "ok: Escape pressed";
        }

        /// <summary>"hand": what the tool controller sees: hovered / held tool and the picked surface point.</summary>
        private static string HandState()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.ToolController>();
            var fieldSet = UnityEngine.Object.FindFirstObjectByType<Terrarium.Sphere.FieldSet>();
            if (controller == null || !EditorApplication.isPlaying)
            {
                return "error: needs Play mode and a ToolController";
            }

            string hover = controller.HoveredTool != null ? controller.HoveredTool.Kind.ToString()
                : controller.HoveredDial != null ? controller.HoveredDial.Kind + " dial" : "nothing";
            string held = controller.HeldTool != null ? controller.HeldTool.Kind.ToString() : "nothing";
            string hit = "no";
            if (controller.HasPlanetHit)
            {
                Vector3 local = controller.PlanetHitLocal;
                Terrarium.Sphere.Texel texel = Terrarium.Sphere.CubeSphere.DirToTexel(local, fieldSet.Resolution);
                hit = $"yes, local ({local.x:0.000}, {local.y:0.000}, {local.z:0.000}) = texel face {texel.Face} x {texel.X} y {texel.Y}";
            }

            var rig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
            var simRoot = UnityEngine.Object.FindFirstObjectByType<SimRoot>();
            string mode = rig != null && rig.InSurfaceMode ? "SURFACE" : "desk";
            string clock = simRoot != null ? $"time scale {simRoot.Clock.TimeScale}x, day length {simRoot.Clock.DayLengthSeconds:0.0} s" : string.Empty;

            return $"ok: hovering {hover}; holding {held}; mouse over planet: {hit}; camera: {mode}; {clock}; controller enabled {controller.enabled}, pointer {Terrarium.Interaction.InputReader.PointerPosition}, ray hits {RayHitName()}, controller frames {controller.CompletedFrames}, controllers {UnityEngine.Object.FindObjectsByType<Terrarium.Interaction.ToolController>(FindObjectsSortMode.None).Length}, planetHit {controller.HasPlanetHit}";
        }

        /// <summary>"press Button_Name": presses a console button directly (Play mode), as a click on it would.</summary>
        private static string PressButton(string[] parts)
        {
            if (parts.Length < 2 || !EditorApplication.isPlaying)
            {
                return "error: usage 'press Button_Name' in Play mode";
            }

            foreach (Terrarium.Workshop.ConsoleControl control in UnityEngine.Object.FindObjectsByType<Terrarium.Workshop.ConsoleControl>(FindObjectsSortMode.None))
            {
                if (control.name == parts[1])
                {
                    control.Press();
                    return $"ok: pressed {control.name} ({control.Kind})";
                }
            }

            return $"error: no button called {parts[1]}";
        }
        /// <summary>"pickup Tool_Name": puts a tool in the player's hand directly (Play mode), for testing what a held tool looks like.</summary>
        private static string PickUpTool(string[] parts)
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.ToolController>();
            if (controller == null || parts.Length < 2 || !EditorApplication.isPlaying)
            {
                return "error: usage 'pickup Tool_Name' in Play mode";
            }

            foreach (Terrarium.Interaction.ToolProp prop in UnityEngine.Object.FindObjectsByType<Terrarium.Interaction.ToolProp>(FindObjectsSortMode.None))
            {
                if (prop.name == parts[1])
                {
                    typeof(Terrarium.Interaction.ToolController)
                        .GetMethod("PickUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(controller, new object[] { prop });
                    return $"ok: holding {prop.name}";
                }
            }

            return $"error: no tool called {parts[1]}";
        }
        // For 'hand': what a physics ray from the camera through the pointer hits first.
        private static string RayHitName()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return "no camera";
            }

            Ray ray = camera.ScreenPointToRay(Terrarium.Interaction.InputReader.PointerPosition);
            return Physics.Raycast(ray, out RaycastHit hit, 60f) ? hit.collider.name : "nothing";
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

        /// <summary>
        /// "facelamp height pitch": keeps the surface camera where it stands and turns it to face the direction of the
        /// lamp (the sun, from the surface). Used to check the sky against the sun's position (Play mode).
        /// </summary>
        private static string FaceLamp(string[] parts)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<Terrarium.Interaction.CameraRig>();
            var motion = UnityEngine.Object.FindFirstObjectByType<Terrarium.Climate.PlanetMotion>();
            GameObject lamp = GameObject.Find("LampLight");
            if (rig == null || motion == null || lamp == null || parts.Length < 3 || !EditorApplication.isPlaying)
            {
                return "error: usage 'facelamp height pitch' in Play mode, after 'surface ...'";
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 lampLocal = (Quaternion.Inverse(motion.transform.rotation) * (lamp.transform.position - motion.transform.position)).normalized;
            rig.SnapToSurface(rig.SurfaceDirection, lampLocal, float.Parse(parts[1], culture), float.Parse(parts[2], culture));
            return "ok: facing the lamp";
        }

        /// <summary>
        /// "rain": reads the whole climate field back and reports how much of the planet is being rained on,
        /// the mean moisture of the land, and the direction of the wettest rain (usable with 'surface x y z ...').
        /// </summary>
        private static string RainReport()
        {
            var fieldSet = UnityEngine.Object.FindFirstObjectByType<Terrarium.Sphere.FieldSet>();
            if (fieldSet == null || !EditorApplication.isPlaying)
            {
                return "error: usage 'rain' in Play mode";
            }

            RenderTexture texture = fieldSet.Get(Terrarium.Sphere.FieldId.Climate).Current;
            RenderTexture terrain = fieldSet.Get(Terrarium.Sphere.FieldId.Terrain).Current;
            var request = AsyncGPUReadback.Request(texture, 0);
            var terrainRequest = AsyncGPUReadback.Request(terrain, 0);
            request.WaitForCompletion();
            terrainRequest.WaitForCompletion();
            if (request.hasError || terrainRequest.hasError)
            {
                return "error: readback failed";
            }

            int n = texture.width;
            int rainy = 0;
            int total = 0;
            double rainSum = 0.0;
            double landMoisture = 0.0;
            int landTexels = 0;
            float best = -1f;
            Vector3 bestDirection = Vector3.up;

            for (int face = 0; face < 6; face++)
            {
                Unity.Collections.NativeArray<float> climate = request.GetData<float>(face);
                Unity.Collections.NativeArray<float> ground = terrainRequest.GetData<float>(face);
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        int index = (y * n + x) * 4;
                        float rain = climate[index + 3];
                        total++;
                        rainSum += rain;
                        if (rain > 0.05f)
                        {
                            rainy++;
                        }

                        if (ground[index] >= 0f)
                        {
                            landTexels++;
                            landMoisture += climate[index + 1];
                            if (rain > best)
                            {
                                best = rain;
                                bestDirection = Terrarium.Sphere.CubeSphere.TexelCentreDir(face, x, y, n);
                            }
                        }
                    }
                }
            }

            return $"ok: raining on {100.0 * rainy / total:0.0}% of the planet, mean rain {rainSum / total:0.000}, "
                 + $"land moisture {landMoisture / Math.Max(landTexels, 1):0.000}, heaviest land rain {best:0.00} at "
                 + $"{bestDirection.x:0.000} {bestDirection.y:0.000} {bestDirection.z:0.000}";
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

        private static void RunEditModeTests(string nameFilter)
        {
            if (testRunner == null)
            {
                testRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
                testRunner.RegisterCallbacks(new TestSummaryWriter());
            }

            // With a name, only tests whose full name contains it are run (it is used as a regex).
            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(nameFilter))
            {
                filter.groupNames = new[] { ".*" + nameFilter + ".*" };
            }

            WriteResult("ok: tests started");
            testRunner.Execute(new ExecutionSettings(filter));
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
