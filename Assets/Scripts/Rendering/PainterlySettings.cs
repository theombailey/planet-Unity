// =============================================================================
// PainterlySettings
// -----------------------------------------------------------------------------
// Purpose : Sends the painterly look's numbers from SimConfig to the shaders,
//           and holds the on/off switches the debug panel flips.
//
// Idea    : Every painterly shader reads the same handful of GLOBAL shader
//           values (ink colour, band count, hatch spacing...). Globals are set
//           once and seen by all materials, so the look is controlled from one
//           place and no material needs its own copy. This component sets them
//           every frame, which is what lets a slider or toggle take effect
//           immediately.
//
// Equations (boil): seed = floor(realTime * boilRateHz)
//           The seed is added to noise lookups in the shaders, so the noise
//           holds still for 1/rate seconds and then jumps: the wobble of a
//           drawing re-traced frame by frame. Real time is used so the boil
//           keeps the same rhythm whatever the simulation speed.
//
// Runs in edit mode too ([ExecuteAlways]) so the scene looks right without
// pressing Play. Property ids are cached once; Update allocates nothing.
// =============================================================================

using UnityEngine;

namespace Terrarium.Rendering
{
    /// <summary>Pushes painterly parameters to global shader values and exposes per-effect toggles.</summary>
    [ExecuteAlways]
    public sealed class PainterlySettings : MonoBehaviour
    {
        [Tooltip("The simulation whose SimConfig holds the painterly parameters.")]
        [SerializeField] private SimRoot simRoot;

        /// <summary>Snap lighting to flat bands (off = smooth lighting).</summary>
        public bool BandsEnabled { get; set; } = true;

        /// <summary>Draw pen hatching in shadow.</summary>
        public bool HatchingEnabled { get; set; } = true;

        /// <summary>Draw ink outlines (inverted hull and fullscreen edges).</summary>
        public bool OutlinesEnabled { get; set; } = true;

        /// <summary>Apply paper grain and vignette.</summary>
        public bool PaperEnabled { get; set; } = true;

        /// <summary>Re-roll line noise a few times a second.</summary>
        public bool BoilEnabled { get; set; } = true;

        private static readonly int AmbientId = Shader.PropertyToID("_PainterlyAmbient");
        private static readonly int InkColorId = Shader.PropertyToID("_PainterlyInkColor");
        private static readonly int ExposureId = Shader.PropertyToID("_PainterlyExposure");
        private static readonly int BandCountId = Shader.PropertyToID("_PainterlyBandCount");
        private static readonly int BandSoftnessId = Shader.PropertyToID("_PainterlyBandSoftness");
        private static readonly int BandWobbleId = Shader.PropertyToID("_PainterlyBandWobble");
        private static readonly int WobbleFrequencyId = Shader.PropertyToID("_PainterlyWobbleFrequency");
        private static readonly int HatchSpacingId = Shader.PropertyToID("_PainterlyHatchSpacing");
        private static readonly int HatchAngleId = Shader.PropertyToID("_PainterlyHatchAngle");
        private static readonly int HatchStrengthId = Shader.PropertyToID("_PainterlyHatchStrength");
        private static readonly int HullWidthId = Shader.PropertyToID("_PainterlyHullWidth");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_PainterlyEdgeWidth");
        private static readonly int EdgeDepthThresholdId = Shader.PropertyToID("_PainterlyEdgeDepthThreshold");
        private static readonly int EdgeNormalThresholdId = Shader.PropertyToID("_PainterlyEdgeNormalThreshold");
        private static readonly int OutlineWobbleId = Shader.PropertyToID("_PainterlyOutlineWobble");
        private static readonly int PaperStrengthId = Shader.PropertyToID("_PainterlyPaperStrength");
        private static readonly int VignetteId = Shader.PropertyToID("_PainterlyVignette");
        private static readonly int BoilSeedId = Shader.PropertyToID("_PainterlyBoilSeed");
        private static readonly int BandsEnabledId = Shader.PropertyToID("_PainterlyBandsEnabled");
        private static readonly int HatchEnabledId = Shader.PropertyToID("_PainterlyHatchEnabled");
        private static readonly int OutlinesEnabledId = Shader.PropertyToID("_PainterlyOutlinesEnabled");
        private static readonly int PaperEnabledId = Shader.PropertyToID("_PainterlyPaperEnabled");

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        /// <summary>Writes every painterly parameter and switch to the global shader values now.</summary>
        public void Apply()
        {
            if (simRoot == null || simRoot.Config == null)
            {
                return;
            }

            SimConfig config = simRoot.Config;

            // Colours are authored as sRGB (what you see in the Inspector); shaders work in linear light.
            Shader.SetGlobalColor(AmbientId, config.ambientColor.linear);
            Shader.SetGlobalColor(InkColorId, config.inkColor.linear);

            Shader.SetGlobalFloat(ExposureId, config.toneExposure);
            Shader.SetGlobalFloat(BandCountId, config.bandCount);
            Shader.SetGlobalFloat(BandSoftnessId, config.bandSoftness);
            Shader.SetGlobalFloat(BandWobbleId, config.bandWobble);
            Shader.SetGlobalFloat(WobbleFrequencyId, config.bandWobbleFrequency);
            Shader.SetGlobalFloat(HatchSpacingId, config.hatchSpacingPixels);
            Shader.SetGlobalFloat(HatchAngleId, config.hatchAngleDegrees * Mathf.Deg2Rad);
            Shader.SetGlobalFloat(HatchStrengthId, config.hatchStrength);
            Shader.SetGlobalFloat(HullWidthId, config.hullOutlinePixels);
            Shader.SetGlobalFloat(EdgeWidthId, config.edgeOutlinePixels);
            Shader.SetGlobalFloat(EdgeDepthThresholdId, config.edgeDepthThreshold);
            Shader.SetGlobalFloat(EdgeNormalThresholdId, config.edgeNormalThreshold);
            Shader.SetGlobalFloat(OutlineWobbleId, config.outlineWobble);
            Shader.SetGlobalFloat(PaperStrengthId, config.paperGrainStrength);
            Shader.SetGlobalFloat(VignetteId, config.vignetteStrength);

            bool boiling = BoilEnabled && Application.isPlaying;
            Shader.SetGlobalFloat(BoilSeedId, boiling ? Mathf.Floor(Time.unscaledTime * config.boilRateHz) : 0f);

            Shader.SetGlobalFloat(BandsEnabledId, BandsEnabled ? 1f : 0f);
            Shader.SetGlobalFloat(HatchEnabledId, HatchingEnabled ? 1f : 0f);
            Shader.SetGlobalFloat(OutlinesEnabledId, OutlinesEnabled ? 1f : 0f);
            Shader.SetGlobalFloat(PaperEnabledId, PaperEnabled ? 1f : 0f);
        }
    }
}
