// =============================================================================
// ToolController
// -----------------------------------------------------------------------------
// Purpose : The player's hands. It lets you hover and pick up the tools on the
//           desk, carries the held tool to the cursor, applies it to the planet,
//           turns the dials, and puts tools back.
// Idea    : Nothing here talks to a creature. Every tool only changes a FIELD
//           (moisture, temperature, nutrients, terrain) through the same brush
//           the debug tools use, or starts / removes agents through the
//           physarum system; the simulation does the rest.
//
// Flow each frame:
//   1. Make a ray from the camera through the mouse; find where it hits the
//      planet (taking the terrain relief into account) and how far away that is.
//   2. Nothing in hand: hover test against the props' colliders. The hovered prop
//      gets a thick ink outline. Left-click picks up a tool or starts turning a dial.
//   3. A dial in hand: sideways mouse movement turns it; the simulation follows.
//   4. A tool in hand: it floats over the cursor (over the surface point if the
//      mouse is on the planet), the tool is applied, and Esc or a short right-click
//      puts it back.
//
// Brush size (world units):  radius = clamp( distanceToSurface * radiusPerDistance, min, max )
//      so a tool covers a wide area from the desk and a small one up close.
//      angularRadius = radius / planetRadius  (arc length on a sphere = radius * angle).
// Continuous tools add (rate * dt) each frame to a field channel, so the effect
//      does not depend on frame rate. One-shot tools (settlers, meteor) act on a click.
//
// Armed    : after picking up a tool the button must be released once before the tool
//            acts, so the click that grabbed it does not also fire it.
// Left-drag: while a tool is held, a dial is turned, or the field view is on, the camera's
//            left-drag pan is switched off (CameraRig.LeftDragBlocked).
// =============================================================================

using Terrarium.Boids;
using Terrarium.Climate;
using Terrarium.Physarum;
using Terrarium.Sphere;
using Terrarium.Trees;
using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>Picks up, carries and applies the desk tools, and turns the dials.</summary>
    public sealed class ToolController : MonoBehaviour
    {
        private const float PickDistance = 60f;
        private const float RightClickMaxSeconds = 0.3f;
        private const float RightClickMaxPixels = 6f;
        private const float DialDegreesPerPixel = 0.5f;
        private const float ReadoutIntervalSeconds = 0.1f;
        private const float MessageSeconds = 3f;
        private const int SplashParticleCount = 90;

        [SerializeField] private SimRoot simRoot;
        [SerializeField] private FieldSet fieldSet;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private Transform planet;
        [SerializeField] private ComputeShader brushShader;
        [SerializeField] private PhysarumSystem physarum;
        [SerializeField] private TreeManager trees;
        [SerializeField] private BoidSystem boids;

        [Tooltip("The generative sound, told when a tool is used (optional).")]
        [SerializeField] private Terrarium.Audio.GenerativeAudio sound;
        [SerializeField] private DebugPanel panel;
        [SerializeField] private FieldDebugView fieldView;
        [SerializeField] private DialProp timeDial;
        [SerializeField] private DialProp spinDial;
        [SerializeField] private DialProp volumeDial;

        [Tooltip("The console: one-shot tool uses leave a line on its paper roll (optional).")]
        [SerializeField] private Terrarium.Workshop.ConsoleReadouts console;

        [Tooltip("Material for the dust that flies up when a meteor strikes.")]
        [SerializeField] private Material splashMaterial;

        private Camera viewCamera;
        private FieldBrush brush;
        private readonly TerrainHeightCache heights = new TerrainHeightCache();
        private bool heightsDirty = true;

        private ToolProp hovered;
        private DialProp hoveredDial;
        private Terrarium.Workshop.ConsoleControl hoveredControl;
        private ToolProp held;
        private DialProp turning;
        private bool armed;

        private bool planetHit;
        private Vector3 hitLocal;
        private Vector3 hitWorld;
        private float hitDistance;

        private float rightDownTime;
        private Vector2 rightDownPosition;
        private float meteorReadyTime;

        private ParticleSystem splash;
        private string message = string.Empty;
        private float messageUntil;

        private float readoutTimer;
        private bool readoutValid;
        private Vector4 terrainValue;
        private Vector4 climateValue;
        private Vector4 soilValue;
        private Vector4 trailValue;
        private Vector4 landValue;
        private bool lensOverlayOn;
        private bool viewWasShowing;
        private GUIStyle inkStyle;

        /// <summary>True while a tool is in the player's hand.</summary>
        public bool HoldingTool => held != null;

        /// <summary>The tool under the mouse (not yet picked up), or null.</summary>
        public ToolProp HoveredTool => hovered;

        /// <summary>The dial under the mouse, or null.</summary>
        public DialProp HoveredDial => hoveredDial;

        /// <summary>True when the mouse is over the planet (a surface point is being picked).</summary>
        public bool HasPlanetHit => planetHit;

        /// <summary>The picked surface point as a unit vector in the planet's local space (valid when <see cref="HasPlanetHit"/>).</summary>
        public Vector3 PlanetHitLocal => hitLocal;

        /// <summary>The tool in hand, or null.</summary>
        public ToolProp HeldTool => held;

        /// <summary>Set by the field debug view's brush and others that need the left button: true while a dial is being turned.</summary>
        public bool TurningDial => turning != null;

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || cameraRig == null || planet == null || brushShader == null || physarum == null)
            {
                Debug.LogError("ToolController is missing a reference.", this);
                enabled = false;
                return;
            }

            fieldSet.WorldReset += MarkTerrainChanged;
        }

        private void OnDestroy()
        {
            if (fieldSet != null)
            {
                fieldSet.WorldReset -= MarkTerrainChanged;
            }
        }

        private void MarkTerrainChanged()
        {
            heightsDirty = true;
        }

        private void Start()
        {
            brush = new FieldBrush(brushShader);
            viewCamera = Camera.main;
            BuildSplash();
        }

        /// <summary>Number of frames the controller has run all the way through (for the editor bridge).</summary>
        public int CompletedFrames { get; private set; }

        private void Update()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                {
                    return;
                }
            }

            RefreshHeightsIfNeeded();
            SyncDials();

            Vector2 pointer = InputReader.PointerPosition;
            bool overPanel = panel != null && panel.IsPointerOver(pointer);

            Ray ray = viewCamera.ScreenPointToRay(pointer);
            FindPlanetHit(ray);

            if (turning != null)
            {
                UpdateDialDrag();
            }
            else if (held == null)
            {
                UpdateHover(ray, overPanel);
            }
            else
            {
                UpdateHeldTool(overPanel);
            }

            cameraRig.LeftDragBlocked = held != null || turning != null || (fieldView != null && fieldView.Showing);
            CompletedFrames++;
        }

        // --------------------------------------------------------------- picking

        private void RefreshHeightsIfNeeded()
        {
            if (heightsDirty && fieldSet.Resolution > 0)
            {
                heightsDirty = false;
                heights.Request(fieldSet.Get(FieldId.Terrain).Current);
            }
        }

        private void FindPlanetHit(Ray ray)
        {
            SimConfig config = simRoot.Config;
            planetHit = SurfacePicker.TryPick(ray, planet, config.planetRadius, config.terrainHeightScale, heights, out hitLocal, out hitDistance);
            if (planetHit)
            {
                hitWorld = ray.origin + ray.direction.normalized * hitDistance;
            }
        }

        // ------------------------------------------------------------ hover / pick

        private void UpdateHover(Ray ray, bool overPanel)
        {
            ToolProp newHover = null;
            DialProp newDial = null;
            Terrarium.Workshop.ConsoleControl newControl = null;

            if (!overPanel && Physics.Raycast(ray, out RaycastHit hit, PickDistance))
            {
                newHover = hit.collider.GetComponentInParent<ToolProp>();
                newDial = hit.collider.GetComponentInParent<DialProp>();
                newControl = hit.collider.GetComponentInParent<Terrarium.Workshop.ConsoleControl>();
            }

            if (hoveredControl != null && hoveredControl != newControl)
            {
                hoveredControl.SetHighlighted(false);
            }

            hoveredControl = newControl;
            if (hoveredControl != null)
            {
                hoveredControl.SetHighlighted(true);
            }

            if (hovered != null && hovered != newHover)
            {
                hovered.SetHighlighted(false);
            }

            hovered = newHover;
            hoveredDial = newDial;
            if (hovered != null)
            {
                hovered.SetHighlighted(true);
            }

            if (!InputReader.PrimaryPressed || overPanel)
            {
                return;
            }

            if (hovered != null)
            {
                PickUp(hovered);
            }
            else if (hoveredDial != null)
            {
                turning = hoveredDial;
                turning.BeginDrag();
            }
            else if (hoveredControl != null)
            {
                hoveredControl.Press();
            }
        }

        private void PickUp(ToolProp tool)
        {
            held = tool;
            held.SetHighlighted(false);
            held.PickUp();
            hovered = null;
            armed = false;
            message = string.Empty;
        }

        private void PutBack()
        {
            if (held == null)
            {
                return;
            }

            SetLensOverlay(false);
            held.PutBack();
            held = null;
        }

        // ------------------------------------------------------------------ dials

        private void UpdateDialDrag()
        {
            if (!InputReader.PaintHeld)
            {
                turning.EndDrag();
                ApplyDial(turning);
                turning = null;
                return;
            }

            turning.Turn(InputReader.PointerDelta.x * DialDegreesPerPixel);
            ApplyDial(turning);
        }

        // Pushes a dial's value into the simulation.
        private void ApplyDial(DialProp dial)
        {
            SimClock clock = simRoot.Clock;
            if (dial.Kind == DialKind.Time)
            {
                clock.SetTimeScale(SimClock.TimeScaleSteps[dial.TimeStopIndex]);
            }
            else if (dial.Kind == DialKind.Volume)
            {
                if (sound != null)
                {
                    sound.MasterVolume = DialProp.VolumeForAngle(dial.Angle);
                }
            }
            else
            {
                float multiplier = DialProp.SpinMultiplier(dial.Angle, simRoot.Config.spinDialRange);
                clock.SetDayLength(simRoot.Config.dayLengthSeconds / multiplier);
            }
        }

        // Keeps the dials showing the simulation's real state (e.g. when Space pauses, or the world is reseeded).
        private void SyncDials()
        {
            SimClock clock = simRoot.Clock;
            if (timeDial != null && !timeDial.IsDragging)
            {
                int stop = System.Array.IndexOf(SimClock.TimeScaleSteps, clock.TimeScale);
                if (stop >= 0)
                {
                    timeDial.SetAngle(DialProp.AngleForTimeStop(stop));
                }
            }

            if (volumeDial != null && !volumeDial.IsDragging && sound != null)
            {
                volumeDial.SetAngle(DialProp.VolumeAngle(sound.MasterVolume));
            }

            if (spinDial != null && !spinDial.IsDragging)
            {
                spinDial.SetAngle(DialProp.SpinAngle(simRoot.Config.dayLengthSeconds / clock.DayLengthSeconds, simRoot.Config.spinDialRange));
            }
        }

        // -------------------------------------------------------------- held tool

        private void UpdateHeldTool(bool overPanel)
        {
            SimConfig config = simRoot.Config;

            // Put it back: Esc, or a short right-click (a right-DRAG is the camera's orbit / look).
            if (InputReader.SecondaryPressed)
            {
                rightDownTime = Time.unscaledTime;
                rightDownPosition = InputReader.PointerPosition;
            }

            bool shortRightClick = InputReader.SecondaryReleased
                && Time.unscaledTime - rightDownTime < RightClickMaxSeconds
                && (InputReader.PointerPosition - rightDownPosition).magnitude < RightClickMaxPixels;

            if (InputReader.CancelPressed || shortRightClick)
            {
                PutBack();
                return;
            }

            // Float over the cursor: above the surface point if the mouse is on the planet, otherwise
            // out in space on the mouse ray at the planet's distance.
            Vector3 target;
            if (planetHit)
            {
                Vector3 normal = (hitWorld - planet.position).normalized;
                target = hitWorld + normal * config.toolHoverHeight * Mathf.Clamp01(hitDistance / 3f + 0.25f);
            }
            else
            {
                Ray ray = viewCamera.ScreenPointToRay(InputReader.PointerPosition);
                float toPlanet = Vector3.Distance(viewCamera.transform.position, planet.position);
                target = ray.origin + ray.direction.normalized * toPlanet;
            }

            held.SetTarget(target);

            if (!InputReader.PaintHeld)
            {
                armed = true;
            }

            ReadLens();
            SetLensOverlay(held.Kind == ToolKind.FieldLens && armed && InputReader.PaintHeld && !overPanel);

            if (!armed || overPanel || !planetHit)
            {
                return;
            }

            ApplyTool(held.Kind, config);
        }

        // Applies the held tool using the mouse: continuous tools while the button is held, one-shot tools on a click.
        private void ApplyTool(ToolKind kind, SimConfig config)
        {
            bool fire = ToolInfo.IsContinuous(kind) ? InputReader.PaintHeld : InputReader.PrimaryPressed;
            if (fire)
            {
                Use(kind, hitLocal, hitWorld, hitDistance, Time.unscaledDeltaTime);
            }
        }

        /// <summary>
        /// Uses a tool at a point on the planet. This is the one place the tools' effects live: the mouse path
        /// above calls it, and so can the editor's DevBridge to exercise the tools without a mouse.
        /// </summary>
        /// <param name="kind">Which tool.</param>
        /// <param name="localDirection">The point, a unit vector in the planet's local space.</param>
        /// <param name="worldPoint">The same point in world space (for the dust effect).</param>
        /// <param name="distanceToSurface">How far the camera is from the point; sets the brush size.</param>
        /// <param name="deltaSeconds">How long a continuous tool has been applied, so the effect does not depend on frame rate.</param>
        public void Use(ToolKind kind, Vector3 localDirection, Vector3 worldPoint, float distanceToSurface, float deltaSeconds)
        {
            SimConfig config = simRoot.Config;
            float radiusWorld = Mathf.Clamp(distanceToSurface * config.toolRadiusPerDistance, config.toolRadiusMin, config.toolRadiusMax);
            float angularRadius = FieldBrush.AngularRadius(radiusWorld, config.planetRadius);
            float dt = deltaSeconds;

            if (sound != null)
            {
                sound.NotifyTool(kind);
                if (console != null)
                {
                    console.NotifyTool(kind);
                }
            }

            switch (kind)
            {
                case ToolKind.WateringCan:
                    brush.Apply(fieldSet.Get(FieldId.Climate).Current, localDirection, angularRadius, new Vector4(0f, config.wateringRate * dt, 0f, 0f), 0f, 1f);
                    break;

                case ToolKind.CompostScoop:
                    brush.Apply(fieldSet.Get(FieldId.Soil).Current, localDirection, angularRadius,
                        new Vector4(config.compostRate * dt, config.compostOrganicRate * dt, 0f, 0f), 0f, 5f);
                    break;

                case ToolKind.HeatLamp:
                    brush.Apply(fieldSet.Get(FieldId.Climate).Current, localDirection, angularRadius, new Vector4(config.heatRate * dt, 0f, 0f, 0f), -80f, 90f);
                    break;

                case ToolKind.FrostSpray:
                    brush.Apply(fieldSet.Get(FieldId.Climate).Current, localDirection, angularRadius, new Vector4(-config.frostRate * dt, 0f, 0f, 0f), -80f, 90f);
                    break;

                case ToolKind.Settlers:
                    physarum.SpawnAgents(localDirection, Mathf.Max(angularRadius, 0.02f), config.settlerCount);
                    break;

                case ToolKind.Meteor:
                    FireMeteor(config, localDirection, worldPoint);
                    break;

                case ToolKind.SeedJar:
                    string problem = trees != null ? trees.PlantSeed(localDirection) : "There is no forest here.";
                    if (problem != null)
                    {
                        Say(problem);
                    }

                    break;

                case ToolKind.BoidCage:
                    if (boids != null)
                    {
                        boids.ReleaseFlock(localDirection, Mathf.Max(angularRadius * 0.5f, 0.01f), config.boidCageCount);
                    }
                    else
                    {
                        Say("There are no birds here.");
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------ meteor

        private void FireMeteor(SimConfig config, Vector3 localDirection, Vector3 worldPoint)
        {
            if (Time.unscaledTime < meteorReadyTime)
            {
                return;
            }

            meteorReadyTime = Time.unscaledTime + config.meteorCooldownSeconds;

            float angularRadius = FieldBrush.AngularRadius(config.meteorRadius, config.planetRadius);
            brush.ApplyCrater(
                fieldSet.Get(FieldId.Terrain).Current,
                fieldSet.Get(FieldId.Soil).Current,
                localDirection, angularRadius, config.meteorDepth, config.meteorRimHeight, config.meteorOrganic);

            // Life inside the blast dies.
            physarum.KillAgents(localDirection, angularRadius * 1.15f);
            if (trees != null)
            {
                trees.KillInRadius(localDirection, angularRadius * 1.15f);
            }

            if (boids != null)
            {
                boids.KillInRadius(localDirection, angularRadius * 1.15f);
            }

            // The CPU's copy of the heights is now out of date; picking needs the crater.
            heightsDirty = true;

            EmitSplash(worldPoint, (worldPoint - planet.position).normalized);
        }

        private void BuildSplash()
        {
            if (splashMaterial == null)
            {
                return;
            }

            var splashObject = new GameObject("MeteorSplash");
            splashObject.transform.SetParent(transform, worldPositionStays: false);
            splash = splashObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = splash.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
            main.startColor = new Color(0.22f, 0.15f, 0.10f, 1f);
            main.gravityModifier = 0f;
            main.maxParticles = 400;

            ParticleSystem.EmissionModule emission = splash.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = splash.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = splash.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            splashObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = splashMaterial;
        }

        // A burst of dark ink-dust flung outward from the impact, in a cone around the surface normal.
        private void EmitSplash(Vector3 worldPoint, Vector3 normal)
        {
            if (splash == null)
            {
                return;
            }

            var rng = new Rng(Time.frameCount);
            var parameters = new ParticleSystem.EmitParams { position = worldPoint };

            for (int i = 0; i < SplashParticleCount; i++)
            {
                Vector3 scatter = new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                parameters.velocity = (normal + scatter * 0.8f).normalized * rng.Range(0.15f, 0.7f);
                splash.Emit(parameters, 1);
            }
        }

        // -------------------------------------------------------------------- lens

        // The field lens: a few times a second, read the ground under the cursor (asynchronously).
        private void ReadLens()
        {
            if (held.Kind != ToolKind.FieldLens || !planetHit)
            {
                readoutValid = false;
                return;
            }

            readoutTimer -= Time.unscaledDeltaTime;
            if (readoutTimer > 0f)
            {
                return;
            }

            readoutTimer = ReadoutIntervalSeconds;
            Texel texel = CubeSphere.DirToTexel(hitLocal, fieldSet.Resolution);
            FieldProbe.Request(fieldSet.Get(FieldId.Terrain).Current, texel, value => { terrainValue = value; readoutValid = true; });
            FieldProbe.Request(fieldSet.Get(FieldId.Climate).Current, texel, value => climateValue = value);
            FieldProbe.Request(fieldSet.Get(FieldId.Soil).Current, texel, value => soilValue = value);
            FieldProbe.Request(fieldSet.Get(FieldId.Trail).Current, texel, value => trailValue = value);
            FieldProbe.Request(fieldSet.Get(FieldId.Land).Current, texel, value => landValue = value);
        }

        // While the lens button is held, show the temperature across the planet; restore the view afterwards.
        private void SetLensOverlay(bool on)
        {
            if (fieldView == null || on == lensOverlayOn)
            {
                return;
            }

            lensOverlayOn = on;
            if (on)
            {
                viewWasShowing = fieldView.Showing;
                fieldView.SelectField(FieldId.Climate);
                fieldView.SelectChannel(0);
                fieldView.SetShowing(true);
            }
            else if (!viewWasShowing)
            {
                fieldView.SetShowing(false);
            }
        }

        private string SeasonAt(Vector3 localDirection)
        {
            // Northern spring begins as the axis lean rises through zero (year phase 0); a southern
            // season is half a year behind. Shifting by an eighth of a year centres each season on its peak.
            float phase = simRoot.Clock.YearPhase + (localDirection.y >= 0f ? 0f : 0.5f) + 0.125f;
            int index = (int)(Mathf.Repeat(phase, 1f) * 4f);
            string[] names = { "spring", "summer", "autumn", "winter" };
            return (localDirection.y >= 0f ? "northern " : "southern ") + names[index];
        }

        private void Say(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + MessageSeconds;
        }

        // --------------------------------------------------------------------- GUI

        private void OnGUI()
        {
            if (viewCamera == null)
            {
                return;
            }

            if (inkStyle == null)
            {
                inkStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Italic,
                    fontSize = 16,
                    wordWrap = false,
                    richText = false,
                };
                inkStyle.normal.textColor = new Color(0.93f, 0.84f, 0.66f, 1f);
            }

            DrawHint();
            DrawReadout();
        }

        private void DrawHint()
        {
            string text = null;
            if (held != null)
            {
                text = $"{ToolInfo.Name(held.Kind)}  -  {ToolInfo.Hint(held.Kind)}  -  Esc or right-click puts it back";
            }
            else if (hovered != null)
            {
                text = $"{ToolInfo.Name(hovered.Kind)}  -  click to pick up";
            }
            else if (hoveredDial != null)
            {
                text = hoveredDial.Kind == DialKind.Time ? "Time dial  -  drag sideways: pause, 0.25x, 1x, 4x, 16x" : (hoveredDial.Kind == DialKind.Volume ? "Volume knob  -  drag sideways: quieter or louder" : "Spin dial  -  drag sideways: slower or faster days and years");
            }
            else if (hoveredControl != null)
            {
                text = hoveredControl.Hint;
            }

            if (Time.unscaledTime < messageUntil)
            {
                text = message;
            }

            if (text != null)
            {
                GUI.Label(new Rect(16f, Screen.height - 36f, Screen.width - 32f, 28f), text, inkStyle);
            }
        }

        // The lens readout: handwritten-looking notes beside the cursor.
        private void DrawReadout()
        {
            if (held == null || held.Kind != ToolKind.FieldLens || !planetHit || !readoutValid)
            {
                return;
            }

            float height = terrainValue.x;
            string ground = height < 0f ? $"sea, depth {-height:0.00}" : $"land, height {height:0.00}";
            string text =
                $"temperature  {climateValue.x:0.0} C\n" +
                $"moisture  {climateValue.y:0.00}\n" +
                $"nutrients  {soilValue.x:0.00}   organic  {soilValue.y:0.00}\n" +
                $"road  {trailValue.x:0.00}   town  {landValue.x:0.00}   farm  {landValue.y:0.00}\n" +
                $"{ground}\n" +
                SeasonAt(hitLocal);

            Vector2 pointer = InputReader.PointerPosition;
            var origin = new Vector2(pointer.x + 28f, Screen.height - pointer.y - 20f);
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(-2f, origin);

            // A dark under-layer first, offset a pixel, so the pale ink reads on any colour.
            Color previous = inkStyle.normal.textColor;
            inkStyle.normal.textColor = new Color(0.05f, 0.03f, 0.02f, 0.9f);
            GUI.Label(new Rect(origin.x + 1.5f, origin.y + 1.5f, 360f, 160f), text, inkStyle);
            inkStyle.normal.textColor = previous;
            GUI.Label(new Rect(origin.x, origin.y, 360f, 160f), text, inkStyle);

            GUI.matrix = saved;
        }
    }
}
