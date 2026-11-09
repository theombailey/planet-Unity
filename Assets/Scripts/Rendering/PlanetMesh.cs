// =============================================================================
// PlanetMesh
// -----------------------------------------------------------------------------
// Purpose : Builds the planet's cube-sphere mesh when the object is enabled and
//           assigns it to the MeshFilter, in the editor as well as in Play mode.
// Idea    : The mesh is generated from SimConfig.planetMeshSubdivisions rather
//           than stored as an asset: it is ~100k vertices of pure maths, so
//           storing it would only bloat the project. HideAndDontSave keeps it
//           out of the saved scene; it is rebuilt every time the object wakes.
// =============================================================================

using UnityEngine;

namespace Terrarium.Rendering
{
    /// <summary>Generates and assigns the planet's cube-sphere mesh.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    public sealed class PlanetMesh : MonoBehaviour
    {
        [Tooltip("The simulation whose SimConfig gives the mesh subdivisions.")]
        [SerializeField] private SimRoot simRoot;

        private Mesh mesh;

        private void OnEnable()
        {
            if (simRoot == null || simRoot.Config == null)
            {
                return;
            }

            mesh = PlanetMeshBuilder.Build(simRoot.Config.planetMeshSubdivisions);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void OnDisable()
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
            }
        }
    }
}
