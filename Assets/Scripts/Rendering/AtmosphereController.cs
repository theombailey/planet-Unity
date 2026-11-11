// =============================================================================
// AtmosphereController
// -----------------------------------------------------------------------------
// Purpose : Runs the sky, the glowing air round the planet and the cloud layer.
//           It does no drawing itself: it places three shapes (a sky dome, an
//           atmosphere shell, a cloud shell) and tells their shaders where the
//           camera, the lamp and the planet are.
//
// Idea    : Three layers, each a sphere drawn by its own shader:
//             SKY DOME   follows the camera and paints the background. Desk
//                        view: dark and mysterious. Surface: blue by day, a
//                        sunset glow when the lamp is low, stars at night.
//             ATMOSPHERE hugs the planet and glows at its edge (desk view).
//             CLOUDS     a layer a little above the ground, thicker over wet
//                        land and sea, carried round by the wind.
//           RAIN       the climate simulation leaves the rain strength in the alpha
//                      channel of the climate field. This controller reads it under
//                      the surface camera a few times a second (async, one texel),
//                      eases toward it, and hands it to the screen-space rain
//                      strokes (InkPaper.shader) and to the sound.
//           The sky and the glow trade places through the camera's own
//           desk-to-surface blend, so flying down turns the dark room into day.
//
// Equations (b = camera surface blend 0..1, c = camera, P = planet centre, R = planet radius):
//     up (for the sky)    = normalize( slerp( worldUp, normalize(c - P), skyWeight ) )
//     lamp direction      = normalize( lampPosition - c )
//     horizon dip         = -sqrt(1 - (R / |c - P|)^2)   (the planet is small, so its horizon is far below level)
//     sky weight          = 1 - smoothstep(skyFadeStart, skyFadeEnd, camera height above the ground)
//                           (how much of the planet's sky you are in; the sky, glow and cloud layer all use it)
//     glow fade           = 1 - skyWeight
//     cloud shell scale   = 1 + cloudHeight / R         (it is a child of the planet, scaled by R)
//     cloud time          = sim time (so the wind stops when the sim is paused)
//
// Runs after the camera and the planet have moved this frame (execution order 110),
// so the shaders never see a frame-old pose. Property ids are cached once; the
// frame allocates nothing.
//
// Parameters (SimConfig): atmosphere*, sky*, nightSkyColor, starDensity, deskHazeStrength,
//     cloud* (see the two headers in SimConfig).
// =============================================================================

using System;
using UnityEngine;
using Terrarium.Interaction;
using Terrarium.Sphere;

namespace Terrarium.Rendering
{
    /// <summary>Feeds the sky, atmosphere and cloud shaders every frame.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class AtmosphereController : MonoBehaviour
    {
        private const float CloudTimeWrapSeconds = 100000f;

        private static readonly int AtmoCentreId = Shader.PropertyToID("_AtmoCentre");
        private static readonly int LampPositionId = Shader.PropertyToID("_TerrariumLampPosition");
        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SkyUpId = Shader.PropertyToID("_SkyUp");
        private static readonly int SkyLampDirId = Shader.PropertyToID("_SkyLampDir");
        private static readonly int SkyBlendId = Shader.PropertyToID("_SkyBlend");
        private static readonly int SkyHorizonDipId = Shader.PropertyToID("_SkyHorizonDip");
        private static readonly int SkyZenithId = Shader.PropertyToID("_SkyZenith");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_SkyHorizon");
        private static readonly int SkySunsetId = Shader.PropertyToID("_SkySunset");
        private static readonly int SkyNightId = Shader.PropertyToID("_SkyNight");
        private static readonly int SkyDeskBaseId = Shader.PropertyToID("_SkyDeskBase");
        private static readonly int SkyStarsId = Shader.PropertyToID("_SkyStars");
        private static readonly int SkyDeskHazeId = Shader.PropertyToID("_SkyDeskHaze");
        private static readonly int AtmoParamsId = Shader.PropertyToID("_AtmoParams");
        private static readonly int AtmoColorId = Shader.PropertyToID("_AtmoColor");
        private static readonly int CloudTimeId = Shader.PropertyToID("_CloudTime");
        private static readonly int CloudParamsId = Shader.PropertyToID("_CloudParams");
        private static readonly int CloudLayerId = Shader.PropertyToID("_CloudLayer");
        private static readonly int CloudWeatherId = Shader.PropertyToID("_CloudWeather");
        private static readonly int RainAmountId = Shader.PropertyToID("_RainAmount");
        private static readonly int CloudShellFadeId = Shader.PropertyToID("_CloudShellFade");
        private static readonly int SkyCloudParamsId = Shader.PropertyToID("_SkyCloudParams");
        private static readonly int PlanetWorldToLocalId = Shader.PropertyToID("_PlanetWorldToLocal");

        [Tooltip("The simulation (SimConfig values and the clock).")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The camera rig; its desk-to-surface blend mixes the dark room into the planet's sky.")]
        [SerializeField] private CameraRig cameraRig;

        [Tooltip("The planet.")]
        [SerializeField] private Transform planet;

        [Tooltip("The lamp's light (the sun).")]
        [SerializeField] private Transform lamp;

        [Tooltip("Sky dome: a big sphere that follows the camera.")]
        [SerializeField] private Transform skyDome;

        [Tooltip("Atmosphere shell: a sphere round the planet that the glow is drawn on.")]
        [SerializeField] private Transform atmosphereShell;

        [Tooltip("Cloud shell: a sphere, child of the planet, a little above the ground.")]
        [SerializeField] private Transform cloudShell;

        [Tooltip("The simulation's fields; the rain under the surface camera is read from the climate field (optional).")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("The sound, told how hard it is raining so it can hiss (optional).")]
        [SerializeField] private Terrarium.Audio.GenerativeAudio sound;

        private const float RainProbeSeconds = 0.25f;
        private const float RainFadePerSecond = 0.8f;

        private Action<Vector4> onRainValue;
        private float rainTarget;
        private float rainShown;
        private float rainProbeTimer;

        /// <summary>How hard it is raining where the surface camera stands, 0..1 (0 in desk view).</summary>
        public float RainHere => rainShown;

        private void Awake()
        {
            // Made once, so asking for the rain a few times a second allocates nothing.
            onRainValue = value => rainTarget = Mathf.Clamp01(value.w);
        }

        /// <summary>Draw the glowing air round the planet.</summary>
        public bool AtmosphereEnabled { get; set; } = true;

        /// <summary>Draw the clouds (and their shadows).</summary>
        public bool CloudsEnabled { get; set; } = true;

        /// <summary>How much of the planet's sky the camera is in: 1 on the surface, 0 once clear of the ground (fades with camera height).</summary>
        public float PlanetSkyWeight { get; private set; }

        /// <summary>Draw the sky (off = plain dark background).</summary>
        public bool SkyEnabled { get; set; } = true;

        private void LateUpdate()
        {
            if (simRoot == null || simRoot.Config == null || simRoot.Clock == null || cameraRig == null || planet == null || lamp == null)
            {
                return;
            }

            SimConfig config = simRoot.Config;
            Vector3 cameraPosition = cameraRig.transform.position;
            Vector3 centre = planet.position;
            float radius = planet.lossyScale.x;
            float blend = cameraRig.SurfaceBlend;

            // How much of the PLANET's sky you are in. It depends on how high the camera is above the ground, not on the
            // camera blend, so the blue sky is gone by the time you have risen clear of the planet and you see the
            // workshop, instead of the sky lingering all the way back to the desk.
            float cameraHeight = Mathf.Max(0f, (cameraPosition - centre).magnitude - radius);
            float skyWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(config.skyFadeStartHeight, Mathf.Max(config.skyFadeEndHeight, config.skyFadeStartHeight + 0.01f), cameraHeight));

            // Where "up" is for the sky: world up from the desk, the planet's local up from the surface.
            Vector3 surfaceUp = (cameraPosition - centre).normalized;
            PlanetSkyWeight = skyWeight;
            Vector3 up = Vector3.Slerp(Vector3.up, surfaceUp, skyWeight);
            Vector3 lampDirection = (lamp.position - cameraPosition).normalized;

            Shader.SetGlobalVector(AtmoCentreId, new Vector4(centre.x, centre.y, centre.z, radius));
            Shader.SetGlobalVector(LampPositionId, lamp.position);

            // The sun: the lamp seen from the planet's centre, as parallel light, so exactly half the planet is lit.
            Vector3 towardSun = (lamp.position - centre).normalized;
            Shader.SetGlobalVector(SunDirectionId, towardSun);
            Color sunColour = config.lampColor.linear * config.sunIntensity;
            Shader.SetGlobalVector(SunColorId, new Vector4(sunColour.r, sunColour.g, sunColour.b, 1f));

            // Sky.
            Shader.SetGlobalVector(SkyUpId, up);
            Shader.SetGlobalVector(SkyLampDirId, lampDirection);
            Shader.SetGlobalFloat(SkyBlendId, skyWeight);

            // How far below level the planet's horizon is for this camera: -sin(acos(R / r)) = -sqrt(1 - (R/r)^2).
            float distanceToCentre = Mathf.Max((cameraPosition - centre).magnitude, radius);
            float ratio = radius / distanceToCentre;
            Shader.SetGlobalFloat(SkyHorizonDipId, -Mathf.Sqrt(Mathf.Max(0f, 1f - ratio * ratio)));
            Shader.SetGlobalColor(SkyZenithId, config.skyZenithColor.linear);
            Shader.SetGlobalColor(SkyHorizonId, config.skyHorizonColor.linear);
            Shader.SetGlobalColor(SkySunsetId, config.sunsetColor.linear);
            Shader.SetGlobalColor(SkyNightId, config.nightSkyColor.linear);
            Shader.SetGlobalColor(SkyDeskBaseId, config.backgroundColor.linear);
            Shader.SetGlobalFloat(SkyStarsId, config.starDensity);
            Shader.SetGlobalFloat(SkyDeskHazeId, config.deskHazeStrength);

            if (skyDome != null)
            {
                skyDome.position = cameraPosition;
                skyDome.gameObject.SetActive(SkyEnabled);
            }

            // Atmosphere glow. The shell is only a bounding shape, a little larger than the real shell of air.
            Shader.SetGlobalVector(AtmoParamsId, new Vector4(config.atmosphereHeight, config.atmosphereStrength, 1f - skyWeight, 0f));
            Shader.SetGlobalColor(AtmoColorId, config.atmosphereColor.linear);
            if (atmosphereShell != null)
            {
                atmosphereShell.position = centre;
                atmosphereShell.localScale = Vector3.one * ((radius + config.atmosphereHeight) * 1.25f);
                atmosphereShell.gameObject.SetActive(AtmosphereEnabled);
            }

            // Clouds.
            float shellScale = 1f + config.cloudHeight / Mathf.Max(radius, 1e-4f);
            Shader.SetGlobalFloat(CloudTimeId, (float)(simRoot.Clock.SimTimeSeconds % CloudTimeWrapSeconds));
            Shader.SetGlobalVector(CloudParamsId, new Vector4(config.cloudScale, config.cloudDriftRadiansPerSecond, config.cloudCoverage, config.cloudMoistureInfluence));
            Shader.SetGlobalVector(CloudLayerId, new Vector4(shellScale, config.cloudOpacity, config.cloudShadowStrength, CloudsEnabled ? 1f : 0f));
            Shader.SetGlobalVector(CloudWeatherId, new Vector4(config.cloudMorphRate, config.cloudTemperatureInfluence, config.stormCloudBrightness, config.rainRate));

            UpdateRain(config, blend);

            // The planet's cloud layer fades out as the camera dives; the sky then draws its own clouds from the
            // same weather. Both use the planet's own frame, so the sky shader is given a rotation to get there.
            float shellFade = 1f - skyWeight;
            Shader.SetGlobalFloat(CloudShellFadeId, shellFade);
            Shader.SetGlobalVector(SkyCloudParamsId, new Vector4(config.skyCloudHeight, config.skyCloudScaleMultiplier, CloudsEnabled ? 1f : 0f, 0f));
            Shader.SetGlobalMatrix(PlanetWorldToLocalId, Matrix4x4.Rotate(Quaternion.Inverse(planet.rotation)));
            if (cloudShell != null)
            {
                cloudShell.localScale = Vector3.one * shellScale;
                cloudShell.gameObject.SetActive(CloudsEnabled && shellFade > 0.01f);
            }
        }

        // Reads the rain value (alpha channel of the climate field) under the surface camera a few times a second,
        // eases toward it, and hands it to the screen-space rain streaks and to the sound.
        private void UpdateRain(SimConfig config, float blend)
        {
            bool onSurface = blend > 0.5f && fieldSet != null && fieldSet.Resolution > 0;
            if (onSurface)
            {
                rainProbeTimer -= Time.unscaledDeltaTime;
                if (rainProbeTimer <= 0f)
                {
                    rainProbeTimer = RainProbeSeconds;
                    Texel texel = CubeSphere.DirToTexel(cameraRig.SurfaceDirection, fieldSet.Resolution);
                    FieldProbe.Request(fieldSet.Get(FieldId.Climate).Current, texel, onRainValue);
                }
            }
            else
            {
                rainTarget = 0f;
            }

            rainShown = Mathf.MoveTowards(rainShown, rainTarget, RainFadePerSecond * Time.unscaledDeltaTime);
            Shader.SetGlobalFloat(RainAmountId, rainShown * config.rainStreakStrength * Mathf.Clamp01(blend));
            if (sound != null)
            {
                sound.SetRain(rainShown * Mathf.Clamp01(blend));
            }
        }

        /// <summary>Panel toggles.</summary>
        public void OnDebugGui()
        {
            GUILayout.BeginHorizontal();
            SkyEnabled = GUILayout.Toggle(SkyEnabled, "Sky");
            AtmosphereEnabled = GUILayout.Toggle(AtmosphereEnabled, "Atmosphere");
            CloudsEnabled = GUILayout.Toggle(CloudsEnabled, "Clouds");
            GUILayout.EndHorizontal();
        }
    }
}
