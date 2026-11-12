// =============================================================================
// SceneBuilder (workshop part)
// -----------------------------------------------------------------------------
// Purpose : Builds the workshop furniture the player uses: a SHELF at the back right
//           with its own light, holding the nine tools; and a CONSOLE at the front left
//           with the nixie-tube readouts, the paper roll, the three dials (time, spin,
//           volume) and the push buttons (pause, new world, three climate presets).
// Idea    : Everything is a primitive cube, cylinder or quad, or a small mesh made by code,
//           with materials from the same painterly shaders as the rest of the scene, so
//           the room still reads as one drawing. The console's controls sit on a sloping
//           panel; they are built in the panel's own frame (x across, z up the slope,
//           y out of the face), so each control is placed with simple numbers.
// Layout (desk top is y = 0, camera looks along +z from -z):
//           shelf   centre x = 2.0, z = 1.35, 2.4 wide, 1.6 tall, two boards of tools, light on top
//           console centre x = -2.0, z = -1.25, 2.35 wide, face sloping up toward the back
//           Row layout on the console face (z across the slope: + is toward the back / top of screen):
//             z = +0.33  nixie tubes (year, temperature, trees, birds, people), plates at z = +0.215
//             z = -0.02  paper roll (left) and the three dials (right), plates at z = -0.17
//             z = -0.38  push buttons, plates at z = -0.47
// =============================================================================

using Terrarium.Interaction;
using Terrarium.Rendering;
using Terrarium.Workshop;
using UnityEditor;
using UnityEngine;

namespace Terrarium.EditorTools
{
    public static partial class SceneBuilder
    {
        private const string NixieShaderName = "Terrarium/Nixie";
        private const string ConsoleTextShaderName = "Terrarium/ConsoleText";

        private const float PlateTexel = 0.0055f;   // world size of one pixel of a nameplate
        private const float NixieWidth = 0.08f;
        private const float NixieHeight = 0.144f;
        private const float NixiePitch = 0.088f;

        private static readonly Vector3 ShelfCentre = new Vector3(2.0f, 0f, 1.35f);
        private static readonly Vector3 ConsoleCentre = new Vector3(-2.0f, 0f, -1.25f);

        // Where each tool stands on the shelf: which board (0 = lower, 1 = upper) and x across the shelf.
        private static readonly (string Name, int Board, float X)[] ShelfSlots =
        {
            ("Tool_WateringCan", 1, -0.88f),
            ("Tool_CompostScoop", 1, -0.44f),
            ("Tool_SeedJar", 1, 0.00f),
            ("Tool_Settlers", 1, 0.40f),
            ("Tool_BoidCage", 1, 0.86f),
            ("Tool_HeatLamp", 0, -0.80f),
            ("Tool_FrostSpray", 0, -0.35f),
            ("Tool_Meteor", 0, 0.15f),
            ("Tool_FieldLens", 0, 0.70f),
        };

        // -------------------------------------------------------------- helpers

        // A primitive placed in its parent's own frame (so rotated parents, like the console panel, work).
        private static GameObject AddPart(string name, PrimitiveType shape, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, worldPositionStays: false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        // A flat quad lying on the console face (its normal points out of the face, its top toward the back).
        private static GameObject AddFaceQuad(string name, Transform panel, Vector3 localPosition, Vector2 size, Material material)
        {
            return AddPart(name, PrimitiveType.Quad, panel, localPosition, Quaternion.Euler(90f, 0f, 0f), new Vector3(size.x, size.y, 1f), material);
        }

        private static Material GetTextMaterial(string name, Color tint, bool paper)
        {
            Material material = GetOrCreateMaterial(name, ConsoleTextShaderName);
            material.SetColor("_Tint", tint);
            material.SetFloat("_Paper", paper ? 1f : 0f);
            material.SetFloat("_Window", 1f);
            material.SetFloat("_Scroll", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // A nameplate under or beside a control.
        private static void AddPlate(Transform panel, string text, Vector3 localPosition, Material plateMaterial)
        {
            var size = new Vector2(LabelPlate.WidthFor(text) * PlateTexel, LabelPlate.HeightPixels * PlateTexel);
            GameObject quad = AddFaceQuad("Plate_" + text, panel, localPosition, size, plateMaterial);
            quad.AddComponent<LabelPlate>().Configure(text, quad.GetComponent<MeshRenderer>(), new Color(0.07f, 0.07f, 0.06f), new Color(0.86f, 0.76f, 0.54f));
        }

        // ------------------------------------------------------------ workshop

        private static void BuildWorkshop(SimConfig config, SimRoot simRoot)
        {
            var workshop = new GameObject("Workshop").transform;
            var toolsRoot = new GameObject("Tools").transform;
            toolsRoot.SetParent(workshop, worldPositionStays: true);

            BuildShelf(workshop, toolsRoot);
            BuildConsole(workshop, simRoot);
        }

        // ---------------------------------------------------------------- shelf

        private static void BuildShelf(Transform workshop, Transform toolsRoot)
        {
            var shelf = new GameObject("Shelf").transform;
            shelf.SetParent(workshop, worldPositionStays: false);
            shelf.position = ShelfCentre;

            Material wood = GetMaterial("ShelfWood", new Color(0.36f, 0.23f, 0.13f));
            Material back = GetMaterial("ShelfBack", new Color(0.20f, 0.14f, 0.09f));
            Material lightBar = GetMaterial("ShelfLightBar", new Color(1.0f, 0.90f, 0.70f), glow: 1f, outlineScale: 0.4f);
            Quaternion none = Quaternion.identity;

            AddPart("BackPanel", PrimitiveType.Cube, shelf, new Vector3(0f, 0.8f, 0.27f), none, new Vector3(2.4f, 1.6f, 0.04f), back);
            AddPart("PostLeft", PrimitiveType.Cube, shelf, new Vector3(-1.17f, 0.8f, 0f), none, new Vector3(0.06f, 1.6f, 0.5f), wood);
            AddPart("PostRight", PrimitiveType.Cube, shelf, new Vector3(1.17f, 0.8f, 0f), none, new Vector3(0.06f, 1.6f, 0.5f), wood);
            AddPart("BoardLower", PrimitiveType.Cube, shelf, new Vector3(0f, 0.45f, 0f), none, new Vector3(2.4f, 0.04f, 0.5f), wood);
            AddPart("BoardUpper", PrimitiveType.Cube, shelf, new Vector3(0f, 1.0f, 0f), none, new Vector3(2.4f, 0.04f, 0.5f), wood);
            AddPart("Cap", PrimitiveType.Cube, shelf, new Vector3(0f, 1.62f, 0f), none, new Vector3(2.46f, 0.06f, 0.56f), wood);

            // The shelf's own light: a glowing bar under the front of the cap and a spot light shining down on the tools.
            AddPart("LightBar", PrimitiveType.Cube, shelf, new Vector3(0f, 1.57f, -0.2f), none, new Vector3(2.0f, 0.03f, 0.07f), lightBar);

            var lightObject = new GameObject("ShelfLight");
            lightObject.transform.SetParent(shelf, worldPositionStays: false);
            lightObject.transform.localPosition = new Vector3(0f, 1.52f, -0.18f);
            lightObject.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1f, 0.45f));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 120f;
            light.innerSpotAngle = 70f;
            light.color = new Color(1.0f, 0.88f, 0.64f);
            light.intensity = 10f;
            light.range = 4f;
            light.shadows = LightShadows.Soft;

            // The tools stand on the boards. Board tops are at y = 0.47 (lower) and 1.02 (upper).
            foreach (var slot in ShelfSlots)
            {
                PropSpec spec = System.Array.Find(PropSpecs, p => p.Name == slot.Name);
                if (spec.Name == null)
                {
                    continue;
                }

                float boardTop = slot.Board == 1 ? 1.02f : 0.47f;
                Vector3 position = ShelfCentre + new Vector3(slot.X, boardTop + RestHeight(spec.Shape, spec.Scale), -0.02f);
                GameObject prop = CreatePrimitive(spec.Name, spec.Shape, toolsRoot, position, Quaternion.identity, spec.Scale, GetMaterial(spec.Name, spec.Color));
                prop.AddComponent<BoxCollider>();
                if (spec.Tool.HasValue)
                {
                    SetEnum(prop.AddComponent<ToolProp>(), "kind", (int)spec.Tool.Value);
                }
            }
        }

        // -------------------------------------------------------------- console

        private static void BuildConsole(Transform workshop, SimRoot simRoot)
        {
            const float width = 2.35f;
            const float depth = 0.95f;
            const float frontHeight = 0.18f;
            const float backHeight = 0.62f;

            var console = new GameObject("Console").transform;
            console.SetParent(workshop, worldPositionStays: false);
            console.position = ConsoleCentre;

            // The wedge-shaped body.
            Mesh wedge = SaveGeneratedMesh("ConsoleWedge", ProceduralMeshes.BuildWedge(width, depth, frontHeight, backHeight));
            var body = new GameObject("Body");
            body.transform.SetParent(console, worldPositionStays: false);
            body.AddComponent<MeshFilter>().sharedMesh = wedge;
            body.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial("ConsoleBody", new Color(0.17f, 0.20f, 0.18f));

            // The face: a frame whose x runs across, z runs up the slope and y points out of the face.
            float rise = backHeight - frontHeight;
            Vector3 along = new Vector3(0f, rise, depth).normalized;
            Vector3 normal = new Vector3(0f, depth, -rise).normalized;
            float faceLength = new Vector2(depth, rise).magnitude;

            var panel = new GameObject("Panel").transform;
            panel.SetParent(console, worldPositionStays: false);
            panel.localPosition = new Vector3(0f, (frontHeight + backHeight) * 0.5f, 0f);
            panel.localRotation = Quaternion.LookRotation(along, normal);

            Material faceMaterial = GetMaterial("ConsolePanel", new Color(0.09f, 0.10f, 0.09f), outlineScale: 0.6f);
            AddPart("Face", PrimitiveType.Cube, panel, new Vector3(0f, 0.004f, 0f), Quaternion.identity, new Vector3(width - 0.06f, 0.008f, faceLength - 0.06f), faceMaterial);

            Material plateMaterial = GetTextMaterial("ConsolePlate", Color.white, paper: false);
            Material nixieMaterial = GetOrCreateMaterial("NixieTube", NixieShaderName);

            // ---- Nixie readouts across the top of the face.
            var groups = new (string Label, int Tubes, int Decimals)[]
            {
                ("YEAR", 3, 0), ("TEMP C", 4, 1), ("TREES", 4, 0), ("BIRDS", 5, 0), ("PEOPLE", 4, 0),
            };

            var displays = new NixieDisplay[groups.Length];
            float cursor = -1.04f;
            for (int g = 0; g < groups.Length; g++)
            {
                var groupObject = new GameObject("Nixie_" + groups[g].Label);
                groupObject.transform.SetParent(panel, worldPositionStays: false);
                var renderers = new Renderer[groups[g].Tubes];
                for (int t = 0; t < groups[g].Tubes; t++)
                {
                    var position = new Vector3(cursor + (t + 0.5f) * NixiePitch, 0.012f, 0.33f);
                    GameObject tube = AddFaceQuad("Tube" + t, panel, position, new Vector2(NixieWidth, NixieHeight), nixieMaterial);
                    tube.transform.SetParent(groupObject.transform, worldPositionStays: true);
                    renderers[t] = tube.GetComponent<MeshRenderer>();
                }

                float groupWidth = groups[g].Tubes * NixiePitch;
                displays[g] = groupObject.AddComponent<NixieDisplay>();
                displays[g].Configure(renderers, groups[g].Decimals);
                AddPlate(panel, groups[g].Label, new Vector3(cursor + groupWidth * 0.5f, 0.012f, 0.215f), plateMaterial);
                cursor += groupWidth + 0.08f;
            }

            // ---- The paper roll, left of the middle row.
            Material paperMaterial = GetTextMaterial("ConsolePaper", new Color(0.94f, 0.91f, 0.80f), paper: true);
            AddPart("PaperFrame", PrimitiveType.Cube, panel, new Vector3(-0.62f, 0.009f, -0.02f), Quaternion.identity, new Vector3(0.98f, 0.01f, 0.44f), GetMaterial("PaperFrame", new Color(0.05f, 0.05f, 0.05f), outlineScale: 0.5f));
            GameObject paper = AddFaceQuad("Paper", panel, new Vector3(-0.62f, 0.016f, -0.02f), new Vector2(0.90f, 0.36f), paperMaterial);
            var roll = paper.AddComponent<PaperRoll>();
            roll.Configure(paper.GetComponent<MeshRenderer>());
            AddPart("PaperSlot", PrimitiveType.Cube, panel, new Vector3(-0.62f, 0.02f, -0.225f), Quaternion.identity, new Vector3(0.98f, 0.03f, 0.04f), GetMaterial("PaperSlot", new Color(0.12f, 0.12f, 0.13f), outlineScale: 0.5f));

            // ---- The three dials, right of the middle row.
            Material dialMaterial = GetMaterial("Dial_Console", new Color(0.55f, 0.45f, 0.25f));
            Material pointerMaterial = GetMaterial("DialPointer", new Color(0.85f, 0.25f, 0.15f));
            var dials = new (string Name, DialKind Kind, float X, string Label)[]
            {
                ("Dial_Time", DialKind.Time, 0.22f, "TIME"),
                ("Dial_Spin", DialKind.Spin, 0.58f, "SPIN"),
                ("Dial_Volume", DialKind.Volume, 0.94f, "VOLUME"),
            };

            foreach (var dial in dials)
            {
                Vector3 local = new Vector3(dial.X, 0.036f, -0.02f);
                GameObject prop = AddPart(dial.Name, PrimitiveType.Cylinder, panel, local, Quaternion.identity, new Vector3(0.22f, 0.03f, 0.22f), dialMaterial);
                prop.AddComponent<BoxCollider>();
                SetEnum(prop.AddComponent<DialProp>(), "kind", (int)dial.Kind);

                // A small peg near the rim so the turn can be seen. It is made at the right world spot, then parented.
                Vector3 rim = panel.TransformPoint(local + Vector3.right * 0.084f + Vector3.up * 0.042f);
                CreatePrimitive("Pointer", PrimitiveType.Cube, prop.transform, rim, panel.rotation, new Vector3(0.05f, 0.025f, 0.02f), pointerMaterial);

                AddPlate(panel, dial.Label, new Vector3(dial.X, 0.012f, -0.17f), plateMaterial);
            }

            // ---- The push buttons along the bottom.
            var buttons = new (string Name, ConsoleControlKind Kind, float X, string Label, Color Colour)[]
            {
                ("Button_Pause", ConsoleControlKind.Pause, -0.95f, "PAUSE", new Color(0.80f, 0.55f, 0.15f)),
                ("Button_NewWorld", ConsoleControlKind.NewWorld, -0.55f, "NEW", new Color(0.75f, 0.75f, 0.72f)),
                ("Button_Lush", ConsoleControlKind.PresetLush, 0.12f, "LUSH", new Color(0.30f, 0.62f, 0.28f)),
                ("Button_Fresh", ConsoleControlKind.PresetFresh, 0.50f, "FRESH", new Color(0.62f, 0.64f, 0.60f)),
                ("Button_Harsh", ConsoleControlKind.PresetHarsh, 0.88f, "HARSH", new Color(0.35f, 0.50f, 0.72f)),
            };

            foreach (var button in buttons)
            {
                var root = new GameObject(button.Name);
                root.transform.SetParent(panel, worldPositionStays: false);
                root.transform.localPosition = new Vector3(button.X, 0f, -0.38f);

                AddPart("Bezel", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.014f, 0f), Quaternion.identity, new Vector3(0.115f, 0.006f, 0.115f), GetMaterial("ButtonBezel", new Color(0.05f, 0.05f, 0.05f), outlineScale: 0.5f));
                GameObject cap = AddPart("Cap", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.030f, 0f), Quaternion.identity, new Vector3(0.088f, 0.012f, 0.088f), GetMaterial(button.Name, button.Colour));
                cap.AddComponent<BoxCollider>();

                Color ledColour = button.Kind == ConsoleControlKind.Pause ? new Color(1.0f, 0.2f, 0.12f) : button.Colour;
                GameObject led = AddPart("Indicator", PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.026f, 0.085f), Quaternion.identity, Vector3.one * 0.026f, GetMaterial("Led_" + button.Name, ledColour, glow: 0f, outlineScale: 0.4f));

                root.AddComponent<ConsoleControl>().Configure(button.Kind, simRoot, cap.transform, led.GetComponent<MeshRenderer>());
                AddPlate(panel, button.Label, new Vector3(button.X, 0.012f, -0.47f), plateMaterial);
            }

            // ---- Connect the readouts to the simulation.
            var readouts = console.gameObject.AddComponent<ConsoleReadouts>();
            SetReference(readouts, "stats", simRoot.GetComponent<StatsSystem>());
            SetReference(readouts, "simRoot", simRoot);
            SetReference(readouts, "year", displays[0]);
            SetReference(readouts, "temperature", displays[1]);
            SetReference(readouts, "trees", displays[2]);
            SetReference(readouts, "birds", displays[3]);
            SetReference(readouts, "people", displays[4]);
            SetReference(readouts, "paper", roll);
        }

        // Saves a code-made mesh as an asset (replacing the old one) so the scene can refer to it.
        private static Mesh SaveGeneratedMesh(string name, Mesh mesh)
        {
            EnsureFolder(MeshFolder);
            string path = $"{MeshFolder}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // -------------------------------------------------------- visibility

        // Hides the workshop while the camera is on the surface; the lamp's bulb and a held tool stay.
        private static void BuildWorkshopVisibility(SimRoot simRoot, AtmosphereController atmosphere)
        {
            GameObject host = simRoot.gameObject;
            var visibility = host.AddComponent<WorkshopVisibility>();
            SetReference(visibility, "atmosphere", atmosphere);
            SetReference(visibility, "tools", host.GetComponent<ToolController>());

            string[] names = { "Desk", "LevitationRing", "Lamp", "Workshop" };
            var serialized = new SerializedObject(visibility);
            SerializedProperty rootsProperty = serialized.FindProperty("roots");
            rootsProperty.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                GameObject found = GameObject.Find(names[i]);
                if (found == null)
                {
                    throw new System.InvalidOperationException($"The workshop part '{names[i]}' was not found.");
                }

                rootsProperty.GetArrayElementAtIndex(i).objectReferenceValue = found.transform;
            }

            SerializedProperty keepProperty = serialized.FindProperty("keepVisible");
            keepProperty.arraySize = 1;
            keepProperty.GetArrayElementAtIndex(0).objectReferenceValue = GameObject.Find("Lamp").transform.Find("Bulb");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
