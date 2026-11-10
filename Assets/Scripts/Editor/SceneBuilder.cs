// =============================================================================
// SceneBuilder (editor only)
// -----------------------------------------------------------------------------
// Purpose : Builds Assets/Scenes/Terrarium.unity entirely from code: the desk,
//           the lamp and its light, the floating planet, the tool props, the
//           camera and the simulation root.
// Idea    : The scene is a build product, like a compiled program. Nothing in
//           it is placed by hand, so the layout is readable here as code, is
//           driven by SimConfig, and can be regenerated at any time with
//           Terrarium > Rebuild Scene. Do not hand-edit the scene: change this
//           file or SimConfig and rebuild.
// Layout  : The desk top is the plane y = 0. The planet floats above the
//           origin. The camera looks from -z toward +z. The lamp stands to the
//           left (-x) and a little toward the camera, so the planet shows a
//           lit side, a shadowed side and the terminator between them.
// Shapes  : The desk, lamp and tools are still a "greybox" built from Unity's
//           primitive shapes. The planet uses the generated cube-sphere mesh. Materials use our own painterly shaders (Terrarium/Props and
//           Terrarium/Desk); the materials themselves are generated here too.
// =============================================================================

using Terrarium.Climate;
using Terrarium.Interaction;
using Terrarium.Physarum;
using Terrarium.Rendering;
using Terrarium.Sphere;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Terrarium.EditorTools
{
    /// <summary>Generates the Terrarium scene from SimConfig.</summary>
    public static class SceneBuilder
    {
        /// <summary>Asset path of the generated scene.</summary>
        public const string ScenePath = "Assets/Scenes/Terrarium.unity";

        private const string MaterialFolder = "Assets/Materials/Greybox";
        private const string MeshFolder = "Assets/Meshes/Generated";
        private const string PropsShaderName = "Terrarium/Props";
        private const string DeskShaderName = "Terrarium/Desk";
        private const string PlanetShaderName = "Terrarium/PlanetSurface";

        /// <summary>Describes one placeholder prop on the desk: a tool or a dial.</summary>
        private readonly struct PropSpec
        {
            public readonly string Name;
            public readonly PrimitiveType Shape;
            public readonly Vector3 Scale;
            public readonly Color Color;
            public readonly ToolKind? Tool;
            public readonly DialKind? Dial;

            public PropSpec(string name, PrimitiveType shape, Vector3 scale, Color color, ToolKind? tool = null, DialKind? dial = null)
            {
                Name = name;
                Shape = shape;
                Scale = scale;
                Color = color;
                Tool = tool;
                Dial = dial;
            }
        }

        // Left to right along the front of the desk. Sizes are in world units (planet radius = 1).
        private static readonly PropSpec[] PropSpecs =
        {
            new PropSpec("Dial_Time", PrimitiveType.Cylinder, new Vector3(0.22f, 0.03f, 0.22f), new Color(0.55f, 0.45f, 0.25f), dial: DialKind.Time),
            new PropSpec("Tool_WateringCan", PrimitiveType.Cylinder, new Vector3(0.20f, 0.14f, 0.20f), new Color(0.30f, 0.45f, 0.60f), ToolKind.WateringCan),
            new PropSpec("Tool_CompostScoop", PrimitiveType.Cube, new Vector3(0.28f, 0.10f, 0.16f), new Color(0.35f, 0.25f, 0.15f), ToolKind.CompostScoop),
            new PropSpec("Tool_SeedJar", PrimitiveType.Cylinder, new Vector3(0.15f, 0.13f, 0.15f), new Color(0.60f, 0.65f, 0.45f), ToolKind.SeedJar),
            new PropSpec("Tool_SporeDish", PrimitiveType.Cylinder, new Vector3(0.22f, 0.02f, 0.22f), new Color(0.75f, 0.72f, 0.60f), ToolKind.SporeDish),
            new PropSpec("Tool_BoidCage", PrimitiveType.Cube, new Vector3(0.22f, 0.24f, 0.22f), new Color(0.50f, 0.50f, 0.52f), ToolKind.BoidCage),
            new PropSpec("Tool_HeatLamp", PrimitiveType.Capsule, new Vector3(0.12f, 0.14f, 0.12f), new Color(0.80f, 0.40f, 0.20f), ToolKind.HeatLamp),
            new PropSpec("Tool_FrostSpray", PrimitiveType.Capsule, new Vector3(0.11f, 0.15f, 0.11f), new Color(0.65f, 0.80f, 0.90f), ToolKind.FrostSpray),
            new PropSpec("Tool_Meteor", PrimitiveType.Sphere, new Vector3(0.20f, 0.17f, 0.19f), new Color(0.28f, 0.26f, 0.25f), ToolKind.Meteor),
            new PropSpec("Tool_FieldLens", PrimitiveType.Cylinder, new Vector3(0.20f, 0.015f, 0.20f), new Color(0.70f, 0.80f, 0.80f), ToolKind.FieldLens),
            new PropSpec("Dial_Spin", PrimitiveType.Cylinder, new Vector3(0.22f, 0.03f, 0.22f), new Color(0.55f, 0.45f, 0.25f), dial: DialKind.Spin),
        };

        /// <summary>Builds the Terrarium scene from scratch and makes it the first scene in Build Settings.</summary>
        [MenuItem("Terrarium/Rebuild Scene")]
        public static void RebuildScene()
        {
            ProjectSetup.CreateDefaultAssets();

            // The generated scene is safe to throw away. Any OTHER scene with unsaved work is the
            // user's, so offer to save it before replacing it.
            Scene open = SceneManager.GetActiveScene();
            bool openSceneIsGenerated = open.path == ScenePath;
            if (!openSceneIsGenerated && open.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load the config only AFTER the new scene exists. Replacing the open scene makes Unity
            // unload every asset nothing references, which would leave an earlier-loaded config as a
            // dead object: its numbers still readable, but saved into the scene as "None".
            SimConfig config = AssetDatabase.LoadAssetAtPath<SimConfig>(ProjectSetup.ConfigPath);

            ApplyLighting(config);
            BuildDesk(config);
            Transform lampLight = BuildLamp(config);
            SimRoot simRoot = BuildSimulationRoot(config);
            GameObject planet = BuildPlanet(config, simRoot, lampLight);
            BuildFields(simRoot, planet);
            BuildTools(config);
            CameraRig cameraRig = BuildCamera(config, simRoot, planet);
            BuildInteraction(simRoot, planet, cameraRig);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, enabled: true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"Terrarium: built {ScenePath}.");
        }

        // ---------------------------------------------------------------- lighting

        private static void ApplyLighting(SimConfig config)
        {
            // One flat, dim, cool ambient colour; no skybox. The lamp does everything else.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = config.ambientColor;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.fog = false;
        }

        // -------------------------------------------------------------------- desk

        private static void BuildDesk(SimConfig config)
        {
            // A cube primitive is 1 unit tall and centred, so lowering it by half its thickness puts its top at y = 0.
            GameObject desk = CreatePrimitive("Desk", PrimitiveType.Cube, null,
                new Vector3(0f, -config.deskSize.y * 0.5f, 0f), Quaternion.identity, config.deskSize,
                GetDeskMaterial());
            desk.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Decorative ring on the desk directly under the planet.
            Mesh ringMesh = GetRingMesh();
            var ring = new GameObject("LevitationRing");
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = GetMaterial("LevitationRing", new Color(0.10f, 0.06f, 0.04f), outlineScale: 0f);
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ring.transform.position = new Vector3(0f, 0.002f, 0f);
            ring.transform.localScale = Vector3.one * config.levitationRingRadius;
        }

        // -------------------------------------------------------------------- lamp

        /// <summary>Builds the lamp model and its light. Returns the light's transform.</summary>
        private static Transform BuildLamp(SimConfig config)
        {
            Vector3 bulb = config.lampLightPosition;
            Vector3 aim = (config.PlanetRestCentre - bulb).normalized;
            Material metal = GetMaterial("LampMetal", new Color(0.12f, 0.13f, 0.14f));
            var lamp = new GameObject("Lamp").transform;

            // Base on the desk, behind the bulb relative to the planet; a two-part arm; a shade round the bulb.
            Vector3 basePoint = new Vector3(bulb.x - 0.55f, 0f, bulb.z + 0.15f);
            Vector3 elbow = new Vector3(basePoint.x - 0.15f, bulb.y * 0.6f, basePoint.z);
            Vector3 shadeBack = bulb - aim * 0.30f;

            CreatePrimitive("Base", PrimitiveType.Cylinder, lamp, basePoint + Vector3.up * 0.03f, Quaternion.identity, new Vector3(0.45f, 0.03f, 0.45f), metal);
            CreateRod("LowerArm", lamp, basePoint, elbow, 0.05f, metal);
            CreateRod("UpperArm", lamp, elbow, shadeBack, 0.05f, metal);
            GameObject shade = CreateRod("Shade", lamp, shadeBack, bulb - aim * 0.02f, 0.34f, metal);

            // The shade surrounds the bulb, so it must not cast a shadow or it would block the light.
            shade.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // A small glowing sphere so the lamp visibly contains a light. It ignores lighting
            // (glow = 1) and casts no shadow, since the real light sits inside it.
            GameObject bulbModel = CreatePrimitive("Bulb", PrimitiveType.Sphere, lamp, bulb, Quaternion.identity, Vector3.one * 0.16f,
                GetMaterial("LampBulb", new Color(1.0f, 0.93f, 0.72f), glow: 1f));
            bulbModel.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lightObject = new GameObject("LampLight");
            lightObject.transform.SetParent(lamp);
            lightObject.transform.SetPositionAndRotation(bulb, Quaternion.LookRotation(aim));

            // A point light: it shines in every direction, so the same bulb lights one hemisphere
            // of the planet AND throws a pool of light on the desk that fades with distance.
            // (A spot light narrow enough to look like a lamp beam left the desk under it dark.)
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = config.lampColor;
            light.intensity = config.lampIntensity;
            light.range = config.lampRange;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = 0.1f;

            return lightObject.transform;
        }

        // ------------------------------------------------------------------ planet

        /// <summary>Builds the planet and returns it. The mesh is generated at runtime by PlanetMesh.</summary>
        private static GameObject BuildPlanet(SimConfig config, SimRoot simRoot, Transform lampLight)
        {
            // The cube-sphere mesh has unit radius, so scaling by the planet radius sizes it.
            var planet = new GameObject("Planet");
            planet.transform.SetPositionAndRotation(config.PlanetRestCentre, Quaternion.identity);
            planet.transform.localScale = Vector3.one * config.planetRadius;

            planet.AddComponent<MeshFilter>();
            planet.AddComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("Planet", PlanetShaderName);
            SetReference(planet.AddComponent<PlanetMesh>(), "simRoot", simRoot);

            var motion = planet.AddComponent<PlanetMotion>();
            SetReference(motion, "simRoot", simRoot);
            SetReference(motion, "lamp", lampLight);
            return planet;
        }

        /// <summary>Adds the field storage, climate, sim loop, field debug view and debug brush to the Simulation object.</summary>
        private static void BuildFields(SimRoot simRoot, GameObject planet)
        {
            GameObject host = simRoot.gameObject;

            var fieldSet = host.AddComponent<FieldSet>();
            SetReference(fieldSet, "simRoot", simRoot);
            SetReference(fieldSet, "fieldInit", LoadAsset<ComputeShader>("Assets/Shaders/Compute/FieldInit.compute"));

            // Climate: needs the fields, the lamp (for insolation) and the two compute shaders.
            var climate = host.AddComponent<ClimateController>();
            SetReference(climate, "simRoot", simRoot);
            SetReference(climate, "fieldSet", fieldSet);
            SetReference(climate, "lamp", GameObject.Find("LampLight").transform);
            SetReference(climate, "fieldInit", LoadAsset<ComputeShader>("Assets/Shaders/Compute/FieldInit.compute"));
            SetReference(climate, "climateShader", LoadAsset<ComputeShader>("Assets/Shaders/Compute/Climate.compute"));

            var physarum = host.AddComponent<PhysarumSystem>();
            SetReference(physarum, "simRoot", simRoot);
            SetReference(physarum, "fieldSet", fieldSet);
            SetReference(physarum, "physarumShader", LoadAsset<ComputeShader>("Assets/Shaders/Compute/Physarum.compute"));
            SetReference(physarum, "nutrientShader", LoadAsset<ComputeShader>("Assets/Shaders/Compute/Nutrients.compute"));

            var loop = host.AddComponent<SimLoop>();
            SetReference(loop, "simRoot", simRoot);
            SetReference(loop, "climate", climate);
            SetReference(loop, "physarum", physarum);

            var view = host.AddComponent<FieldDebugView>();
            SetReference(view, "fieldSet", fieldSet);
            SetReference(view, "planetRenderer", planet.GetComponent<MeshRenderer>());
            SetReference(view, "debugShader", Shader.Find("Terrarium/FieldDebug"));

            var panel = host.GetComponent<DebugPanel>();
            var brush = host.AddComponent<DebugBrush>();
            SetReference(brush, "fieldSet", fieldSet);
            SetReference(brush, "view", view);
            SetReference(brush, "planet", planet.transform);
            SetReference(brush, "simRoot", simRoot);
            SetReference(brush, "brushShader", LoadAsset<ComputeShader>("Assets/Shaders/Compute/Brush.compute"));
            SetReference(brush, "panel", panel);

            SetReference(panel, "climate", climate);
            SetReference(panel, "physarum", physarum);
            SetReference(panel, "fieldView", view);
            SetReference(panel, "debugBrush", brush);
        }

        private static T LoadAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new System.InvalidOperationException($"Asset not found: {path}");
            }

            return asset;
        }

        // ------------------------------------------------------------------- tools

        private static void BuildTools(SimConfig config)
        {
            var tools = new GameObject("Tools").transform;
            Material pointerMaterial = GetMaterial("DialPointer", new Color(0.85f, 0.25f, 0.15f));

            // Spread the props evenly along the front arc of an ellipse centred under the planet.
            // Angles are measured in the desk plane from +x; 270 degrees is straight toward the camera.
            const float startDegrees = 224f;
            const float endDegrees = 316f;

            for (int i = 0; i < PropSpecs.Length; i++)
            {
                PropSpec spec = PropSpecs[i];
                float angle = Mathf.Lerp(startDegrees, endDegrees, (float)i / (PropSpecs.Length - 1)) * Mathf.Deg2Rad;
                var position = new Vector3(
                    Mathf.Cos(angle) * config.toolArcRadii.x,
                    RestHeight(spec.Shape, spec.Scale),
                    Mathf.Sin(angle) * config.toolArcRadii.y);

                GameObject prop = CreatePrimitive(spec.Name, spec.Shape, tools, position, Quaternion.identity, spec.Scale, GetMaterial(spec.Name, spec.Color));

                // A collider lets the mouse ray find the prop. A box is cheap and close enough.
                prop.AddComponent<BoxCollider>();

                if (spec.Tool.HasValue)
                {
                    SetEnum(prop.AddComponent<ToolProp>(), "kind", (int)spec.Tool.Value);
                }

                if (spec.Dial.HasValue)
                {
                    SetEnum(prop.AddComponent<DialProp>(), "kind", (int)spec.Dial.Value);

                    // A small peg near the rim, so the turn of the dial can be seen.
                    Vector3 rim = position + Vector3.right * (spec.Scale.x * 0.38f) + Vector3.up * (spec.Scale.y + 0.012f);
                    CreatePrimitive("Pointer", PrimitiveType.Cube, prop.transform, rim, Quaternion.identity, new Vector3(0.05f, 0.025f, 0.02f), pointerMaterial);
                }
            }
        }

        /// <summary>Height of a primitive's centre when it rests on the desk (y = 0).</summary>
        private static float RestHeight(PrimitiveType shape, Vector3 scale)
        {
            // Unity's cylinder and capsule primitives are 2 units tall; cube and sphere are 1 unit tall.
            bool twoUnitsTall = shape == PrimitiveType.Cylinder || shape == PrimitiveType.Capsule;
            return twoUnitsTall ? scale.y : scale.y * 0.5f;
        }

        // ------------------------------------------------------- camera + sim root

        private static CameraRig BuildCamera(SimConfig config, SimRoot simRoot, GameObject planet)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();

            camera.fieldOfView = config.cameraFieldOfView;
            camera.nearClipPlane = config.cameraNearClip;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = config.backgroundColor;

            CameraRig.DeskPose(config, 0f, config.deskPitchDefaultDegrees, config.deskDistanceDefault, out Vector3 position, out Quaternion rotation);
            cameraObject.transform.SetPositionAndRotation(position, rotation);

            var rig = cameraObject.AddComponent<CameraRig>();
            SetReference(rig, "simRoot", simRoot);
            SetReference(rig, "planet", planet.transform);
            return rig;
        }

        private static SimRoot BuildSimulationRoot(SimConfig config)
        {
            var rootObject = new GameObject("Simulation");
            var simRoot = rootObject.AddComponent<SimRoot>();
            var debugPanel = rootObject.AddComponent<DebugPanel>();

            SetReference(simRoot, "config", config);

            var serializedRoot = new SerializedObject(simRoot);
            SerializedProperty presets = serializedRoot.FindProperty("presets");
            presets.arraySize = ProjectSetup.PresetPaths.Length;
            for (int i = 0; i < ProjectSetup.PresetPaths.Length; i++)
            {
                presets.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<QualityPreset>(ProjectSetup.PresetPaths[i]);
            }
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            var painterly = rootObject.AddComponent<PainterlySettings>();
            SetReference(painterly, "simRoot", simRoot);

            SetReference(debugPanel, "simRoot", simRoot);
            SetReference(debugPanel, "painterly", painterly);
            return simRoot;
        }

        /// <summary>Adds the tool controller, which ties the props, dials, camera and fields together.</summary>
        private static void BuildInteraction(SimRoot simRoot, GameObject planet, CameraRig cameraRig)
        {
            GameObject host = simRoot.gameObject;
            var controller = host.AddComponent<ToolController>();

            SetReference(controller, "simRoot", simRoot);
            SetReference(controller, "fieldSet", host.GetComponent<FieldSet>());
            SetReference(controller, "cameraRig", cameraRig);
            SetReference(controller, "planet", planet.transform);
            SetReference(controller, "brushShader", LoadAsset<ComputeShader>("Assets/Shaders/Compute/Brush.compute"));
            SetReference(controller, "physarum", host.GetComponent<PhysarumSystem>());
            SetReference(controller, "panel", host.GetComponent<DebugPanel>());
            SetReference(controller, "fieldView", host.GetComponent<FieldDebugView>());
            SetReference(controller, "splashMaterial", GetSplashMaterial());

            foreach (DialProp dial in Object.FindObjectsByType<DialProp>(FindObjectsSortMode.None))
            {
                string field = dial.Kind == DialKind.Time ? "timeDial" : "spinDial";
                SetReference(controller, field, dial);
            }

            // The debug brush must stand aside while a tool is in hand.
            SetReference(host.GetComponent<DebugBrush>(), "tools", controller);
        }

        /// <summary>Creates the material for the dust a meteor throws up (URP's unlit particle shader).</summary>
        private static Material GetSplashMaterial()
        {
            Material material = GetOrCreateMaterial("MeteorSplash", "Universal Render Pipeline/Particles/Unlit");
            material.SetColor("_BaseColor", new Color(0.22f, 0.15f, 0.10f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetEnum(Object component, string fieldName, int value)
        {
            var serialized = new SerializedObject(component);
            serialized.FindProperty(fieldName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ----------------------------------------------------------------- helpers

        /// <summary>
        /// Sets a private [SerializeField] object reference by name, the same route the Inspector uses.
        /// </summary>
        private static void SetReference(Object component, string fieldName, Object value)
        {
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                throw new System.InvalidOperationException($"{component.GetType().Name} has no serialized field '{fieldName}'.");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Fail here, at build time, rather than with a confusing null at Play time.
            if (value == null || property.objectReferenceValue != value)
            {
                throw new System.InvalidOperationException($"Could not set {component.GetType().Name}.{fieldName}: the value is missing or cannot be saved in a scene.");
            }
        }

        private static GameObject CreatePrimitive(string name, PrimitiveType shape, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(shape);
            primitive.name = name;

            // Primitives arrive with a physics collider. Nothing here uses physics; picking the
            // planet is done with our own ray-sphere maths, so remove it.
            Object.DestroyImmediate(primitive.GetComponent<Collider>());

            // Scale is set before parenting and the world pose after, so `position`, `rotation`
            // and `scale` always mean world values even under a scaled parent.
            primitive.transform.localScale = scale;
            primitive.transform.SetPositionAndRotation(position, rotation);
            if (parent != null)
            {
                primitive.transform.SetParent(parent, worldPositionStays: true);
            }

            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
            return primitive;
        }

        /// <summary>Creates a cylinder running from one world point to another.</summary>
        private static GameObject CreateRod(string name, Transform parent, Vector3 from, Vector3 to, float diameter, Material material)
        {
            Vector3 span = to - from;

            // The cylinder primitive is 2 units tall along its local Y, so half the length is its Y scale.
            return CreatePrimitive(name, PrimitiveType.Cylinder, parent,
                (from + to) * 0.5f,
                Quaternion.FromToRotation(Vector3.up, span.normalized),
                new Vector3(diameter, span.magnitude * 0.5f, diameter),
                material);
        }

        /// <summary>Creates or updates a Terrarium/Props material asset.</summary>
        /// <param name="glow">0 = lit normally, 1 = flat unlit colour.</param>
        /// <param name="outlineScale">Multiplier on the hull outline width (0 = no outline).</param>
        private static Material GetMaterial(string name, Color color, float glow = 0f, float outlineScale = 1f)
        {
            Material material = GetOrCreateMaterial(name, PropsShaderName);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Glow", glow);
            material.SetFloat("_OutlineScale", outlineScale);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Creates or updates the desk's wood material. Its look is set by the shader's defaults.</summary>
        private static Material GetDeskMaterial()
        {
            Material material = GetOrCreateMaterial("Desk", DeskShaderName);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateMaterial(string name, string shaderName)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"Shader '{shaderName}' was not found. Check the Console for shader compile errors.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        private static Mesh GetRingMesh()
        {
            EnsureFolder(MeshFolder);
            string path = $"{MeshFolder}/LevitationRing.asset";

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                // Unit outer radius; the scene object's scale sets the real size.
                mesh = ProceduralMeshes.BuildRing(innerRadius: 0.93f, outerRadius: 1f, segments: 96);
                AssetDatabase.CreateAsset(mesh, path);
            }

            return mesh;
        }

        /// <summary>Creates an Assets/... folder (and any missing parents) through the AssetDatabase.</summary>
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
