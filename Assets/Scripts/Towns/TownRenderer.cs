// =============================================================================
// TownRenderer
// -----------------------------------------------------------------------------
// Purpose : Puts the settlements on screen: the houses of every town, and the people
//           walking between them.
// Idea    : Both are drawn by the GPU straight from simulation data, with no CPU copy.
//             Houses  Houses.compute decides, spot by spot, whether a house stands there (from
//                     the town density in the Land field) and appends it to a list; the
//                     houses are then drawn with one indirect instanced call, whose instance
//                     count is copied from the list's own counter. The list is rebuilt a few
//                     times a second, since towns change slowly.
//             People  the people simulation's own buffers (position, heading, alive) are the
//                     instance data, so one instanced call draws the whole pool; the shader
//                     skips the dead and the far away.
// Readback: the house count is read back asynchronously a couple of times a second, for the
//           console and the stability check. Nothing ever waits for the GPU.
// Parameters: houseCandidates, houseThreshold, houseSize, houseMinPixels, personSize,
//           personVisibleDistance (SimConfig "Towns and farms").
// =============================================================================

using Terrarium.Physarum;
using Terrarium.Sphere;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Towns
{
    /// <summary>Draws the houses and people of the planet's towns.</summary>
    public sealed class TownRenderer : MonoBehaviour
    {
        private const int MaxHouses = 24000;
        private const int HouseStride = 48;
        private const float RebuildIntervalSeconds = 0.5f;
        private const int ThreadGroupSize = 64;

        [Tooltip("The simulation: config and preset.")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("The field textures.")]
        [SerializeField] private FieldSet fieldSet;

        [Tooltip("The people simulation, whose buffers are drawn.")]
        [SerializeField] private PhysarumSystem people;

        [Tooltip("The planet. Positions are in its local space.")]
        [SerializeField] private Transform planet;

        [Tooltip("Houses.compute.")]
        [SerializeField] private ComputeShader houseShader;

        [Tooltip("Material using Terrarium/Houses.")]
        [SerializeField] private Material houseMaterial;

        [Tooltip("Material using Terrarium/People.")]
        [SerializeField] private Material personMaterial;

        private Mesh houseMesh;
        private Mesh personMesh;
        private ComputeBuffer houses;
        private ComputeBuffer arguments;
        private MaterialPropertyBlock houseBlock;
        private MaterialPropertyBlock personBlock;
        private readonly uint[] argumentData = new uint[5];
        private float timer;
        private bool readbackPending;

        /// <summary>Houses standing at the last readback.</summary>
        public int HouseCount { get; private set; }

        private void Awake()
        {
            if (simRoot == null || fieldSet == null || people == null || planet == null || houseShader == null || houseMaterial == null || personMaterial == null)
            {
                Debug.LogError("TownRenderer is missing a reference.", this);
                enabled = false;
                return;
            }

            houseBlock = new MaterialPropertyBlock();
            personBlock = new MaterialPropertyBlock();
            houseMesh = TownMeshes.BuildHouse();
            personMesh = TownMeshes.BuildPerson();

            houses = new ComputeBuffer(MaxHouses, HouseStride, ComputeBufferType.Append);
            houses.SetCounterValue(0);
            arguments = new ComputeBuffer(1, argumentData.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argumentData[0] = houseMesh.GetIndexCount(0);
            argumentData[1] = 0;
            arguments.SetData(argumentData);
        }

        private void OnDestroy()
        {
            houses?.Release();
            arguments?.Release();
            if (houseMesh != null)
            {
                Destroy(houseMesh);
            }

            if (personMesh != null)
            {
                Destroy(personMesh);
            }
        }

        private void Update()
        {
            if (fieldSet.Resolution == 0)
            {
                return;
            }

            timer -= Time.unscaledDeltaTime;
            if (timer > 0f)
            {
                return;
            }

            timer = RebuildIntervalSeconds;
            RebuildHouses();

            if (!readbackPending)
            {
                readbackPending = true;
                AsyncGPUReadback.Request(arguments, OnArgumentsRead);
            }
        }

        private void LateUpdate()
        {
            if (fieldSet.Resolution == 0)
            {
                return;
            }

            SimConfig config = simRoot.Config;
            var bounds = new Bounds(planet.position, Vector3.one * 8f);

            houseBlock.SetBuffer("_Houses", houses);
            houseBlock.SetMatrix("_PlanetLocalToWorld", planet.localToWorldMatrix);
            houseBlock.SetFloat("_HouseMinPixels", config.houseMinPixels);
            Graphics.DrawMeshInstancedIndirect(houseMesh, 0, houseMaterial, bounds, arguments, 0, houseBlock, ShadowCastingMode.Off, false);

            PhysarumSimulation simulation = people.Simulation;
            if (simulation != null && simulation.IsAllocated)
            {
                personBlock.SetBuffer("_PersonPosEnergy", simulation.PosEnergy);
                personBlock.SetBuffer("_PersonHeading", simulation.Heading);
                personBlock.SetBuffer("_PersonAlive", simulation.Alive);
                personBlock.SetMatrix("_PlanetLocalToWorld", planet.localToWorldMatrix);
                personBlock.SetFloat("_PersonSize", config.personSize);
                personBlock.SetFloat("_PersonVisibleDistance", config.personVisibleDistance);
                Graphics.DrawMeshInstancedProcedural(personMesh, 0, personMaterial, bounds, simulation.PoolSize, personBlock, ShadowCastingMode.Off, false);
            }
        }

        // Runs Houses.compute: every candidate spot decides whether it holds a house, and the survivors are appended
        // to the list. The list's counter is then copied into the draw call's instance count.
        private void RebuildHouses()
        {
            SimConfig config = simRoot.Config;
            int kernel = houseShader.FindKernel("BuildHouses");
            int candidates = Mathf.Max(1000, config.houseCandidates);

            houses.SetCounterValue(0);
            houseShader.SetBuffer(kernel, "_Houses", houses);
            houseShader.SetTexture(kernel, "_Land", fieldSet.Get(FieldId.Land).Current);
            houseShader.SetTexture(kernel, "_Terrain", fieldSet.Get(FieldId.Terrain).Current);
            houseShader.SetTexture(kernel, "_Climate", fieldSet.Get(FieldId.Climate).Current);
            houseShader.SetInt("_CandidateCount", candidates);
            houseShader.SetInt("_Resolution", fieldSet.Resolution);
            houseShader.SetFloat("_HouseThreshold", config.houseThreshold);
            houseShader.SetFloat("_CoreFull", config.coreFull);
            houseShader.SetFloat("_HouseSize", config.houseSize);
            houseShader.SetFloat("_HeightScale", config.terrainHeightScale);
            houseShader.Dispatch(kernel, (candidates + ThreadGroupSize - 1) / ThreadGroupSize, 1, 1);

            ComputeBuffer.CopyCount(houses, arguments, sizeof(uint));
        }

        private void OnArgumentsRead(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (request.hasError || arguments == null)
            {
                return;
            }

            NativeArray<uint> data = request.GetData<uint>();
            HouseCount = (int)Mathf.Min(data[1], MaxHouses);
        }
    }
}
