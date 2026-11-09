// =============================================================================
// FieldSet
// -----------------------------------------------------------------------------
// Purpose : Owns every simulation field: the GPU textures that hold terrain,
//           climate, soil, the physarum trail and the transient brush layer.
//
// Idea    : Each field is a Texture2DArray with 6 slices (one per cube face,
//           see CubeSphere) of N x N texels, N from the active quality preset.
//           A compute shader can write to it (enableRandomWrite).
//
//           PING-PONG: a step that reads neighbours (diffusion, for example)
//           cannot write into the texture it is reading, because a texel
//           would see a mix of old and new values depending on which thread
//           ran first. So such fields keep TWO textures: the step READS
//           `Current` and WRITES `Next`, then `Swap()` exchanges their roles.
//           Fields updated texel-by-texel with no neighbour reads (terrain,
//           the brush) are updated in place and keep one.
//
// Fields  (channels; formats are 32-bit float so nutrients stay accurate):
//   Terrain  RGBA  R height (-1..1, 0 = sea level), G rock hardness
//   Climate  RGBA  R temperature, G moisture 0..1, B insolation 0..1     ping-pong
//   Soil     RGBA  R nutrients, G organic matter, B canopy density       ping-pong
//   Trail    R     physarum trail strength                               ping-pong
//   Brush    RGBA  transient tool paint
//
// Lifetime: textures are created in Start (after SimRoot has chosen a preset),
//           rebuilt if the preset's resolution changes, re-initialised when
//           the seed changes, and released in OnDisable so entering and
//           leaving Play mode never leaks GPU memory.
// =============================================================================

using System;
using UnityEngine;

namespace Terrarium.Sphere
{
    /// <summary>Identifies one simulation field.</summary>
    public enum FieldId
    {
        /// <summary>Height and rock hardness.</summary>
        Terrain,

        /// <summary>Temperature, moisture, insolation.</summary>
        Climate,

        /// <summary>Nutrients, organic matter, canopy density.</summary>
        Soil,

        /// <summary>Physarum trail strength.</summary>
        Trail,

        /// <summary>Transient tool paint.</summary>
        Brush,
    }

    /// <summary>One field: a six-slice float texture array, or a ping-pong pair of them.</summary>
    public sealed class Field
    {
        private readonly RenderTexture[] buffers;
        private int readIndex;

        /// <summary>Creates the textures for a field.</summary>
        public Field(FieldId id, int resolution, RenderTextureFormat format, bool pingPong)
        {
            Id = id;
            Resolution = resolution;
            buffers = new RenderTexture[pingPong ? 2 : 1];

            for (int i = 0; i < buffers.Length; i++)
            {
                var texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
                {
                    name = $"Field_{id}_{i}",
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                    volumeDepth = CubeSphere.FaceCount,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                };
                texture.Create();
                buffers[i] = texture;
            }
        }

        /// <summary>Which field this is.</summary>
        public FieldId Id { get; }

        /// <summary>Texels along one face edge.</summary>
        public int Resolution { get; }

        /// <summary>True when the field keeps two textures that swap each step.</summary>
        public bool IsPingPong => buffers.Length > 1;

        /// <summary>The up-to-date texture: read it, and paint into it for in-place edits.</summary>
        public RenderTexture Current => buffers[readIndex];

        /// <summary>The texture a step should write into (the same as Current for single-buffer fields).</summary>
        public RenderTexture Next => buffers.Length > 1 ? buffers[1 - readIndex] : buffers[0];

        /// <summary>Exchanges the roles of Current and Next after a step. Does nothing for single-buffer fields.</summary>
        public void Swap()
        {
            if (buffers.Length > 1)
            {
                readIndex = 1 - readIndex;
            }
        }

        /// <summary>Every texture of the field, for initialisation and cleanup.</summary>
        public RenderTexture[] Buffers => buffers;

        /// <summary>Frees the GPU memory.</summary>
        public void Release()
        {
            foreach (RenderTexture texture in buffers)
            {
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }
    }

    /// <summary>Creates, initialises and owns all simulation fields.</summary>
    public sealed class FieldSet : MonoBehaviour
    {
        private const int ThreadGroupSize = 8;

        [Tooltip("The simulation: gives the quality preset (field resolution) and the seed.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("FieldInit.compute: clears and initialises fields.")]
        [SerializeField] private ComputeShader fieldInit;

        private Field[] fields = Array.Empty<Field>();
        private bool started;

        /// <summary>Raised after the fields have been (re)created, so users re-fetch their textures.</summary>
        public event Action Rebuilt;

        /// <summary>Texels along one face edge, from the active quality preset.</summary>
        public int Resolution { get; private set; }

        /// <summary>The field with the given id. Only valid after Start.</summary>
        public Field Get(FieldId id)
        {
            return fields[(int)id];
        }

        private void Start()
        {
            started = true;
            if (simRoot == null || !simRoot.enabled || fieldInit == null)
            {
                Debug.LogError("FieldSet needs a SimRoot and the FieldInit compute shader.", this);
                enabled = false;
                return;
            }

            Subscribe();
            Build();
        }

        private void OnEnable()
        {
            // Re-enabling after Start: Start will not run again, so rebuild here.
            if (started)
            {
                Subscribe();
                Build();
            }
        }

        private void OnDisable()
        {
            if (simRoot != null)
            {
                simRoot.PresetChanged -= HandlePresetChanged;
                simRoot.SeedChanged -= HandleSeedChanged;
            }

            ReleaseAll();
        }

        private void Subscribe()
        {
            simRoot.PresetChanged -= HandlePresetChanged;
            simRoot.SeedChanged -= HandleSeedChanged;
            simRoot.PresetChanged += HandlePresetChanged;
            simRoot.SeedChanged += HandleSeedChanged;
        }

        private void HandlePresetChanged(QualityPreset preset)
        {
            Build();
        }

        private void HandleSeedChanged(int seed)
        {
            Initialise(seed);
        }

        private void Build()
        {
            ReleaseAll();

            Resolution = simRoot.ActivePreset.fieldResolution;
            fields = new[]
            {
                new Field(FieldId.Terrain, Resolution, RenderTextureFormat.ARGBFloat, pingPong: false),
                new Field(FieldId.Climate, Resolution, RenderTextureFormat.ARGBFloat, pingPong: true),
                new Field(FieldId.Soil, Resolution, RenderTextureFormat.ARGBFloat, pingPong: true),
                new Field(FieldId.Trail, Resolution, RenderTextureFormat.RFloat, pingPong: true),
                new Field(FieldId.Brush, Resolution, RenderTextureFormat.ARGBFloat, pingPong: false),
            };

            Initialise(simRoot.ActiveSeed);
            Rebuilt?.Invoke();
        }

        /// <summary>Clears every field, then writes the test pattern into Terrain.</summary>
        private void Initialise(int seed)
        {
            int clearKernel = fieldInit.FindKernel("Clear");
            int patternKernel = fieldInit.FindKernel("InitTestPattern");
            int groups = (Resolution + ThreadGroupSize - 1) / ThreadGroupSize;

            fieldInit.SetInt("_Resolution", Resolution);
            fieldInit.SetFloat("_Seed", seed);

            foreach (Field field in fields)
            {
                foreach (RenderTexture texture in field.Buffers)
                {
                    fieldInit.SetTexture(clearKernel, "_Target", texture);
                    fieldInit.Dispatch(clearKernel, groups, groups, CubeSphere.FaceCount);
                }
            }

            fieldInit.SetTexture(patternKernel, "_Target", Get(FieldId.Terrain).Current);
            fieldInit.Dispatch(patternKernel, groups, groups, CubeSphere.FaceCount);
        }

        private void ReleaseAll()
        {
            foreach (Field field in fields)
            {
                field.Release();
            }

            fields = Array.Empty<Field>();
        }
    }
}
