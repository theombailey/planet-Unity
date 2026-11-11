// =============================================================================
// ProceduralMeshesTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the code-built sphere used for the sky dome, the atmosphere
//           shell and the cloud layer: every vertex is on the unit sphere, the
//           counts are right, and every real triangle faces outward (Unity draws
//           clockwise-from-outside triangles as front faces).
// =============================================================================

using NUnit.Framework;
using Terrarium.Rendering;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class ProceduralMeshesTests
    {
        [Test]
        public void BuildUnitSphere_HasTheRightCounts_AndEveryVertexOnTheUnitSphere()
        {
            Mesh mesh = ProceduralMeshes.BuildUnitSphere(24, 16);

            Assert.AreEqual((24 + 1) * (16 + 1), mesh.vertexCount);
            Assert.AreEqual(24 * 16 * 6, mesh.triangles.Length);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Assert.AreEqual(1f, vertex.magnitude, 1e-5f);
            }

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BuildUnitSphere_EveryTriangleFacesOutward()
        {
            Mesh mesh = ProceduralMeshes.BuildUnitSphere(24, 16);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int checkedTriangles = 0;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);

                // The triangles that touch a pole have a zero-length edge and no area: nothing to check.
                if (normal.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                Vector3 centroid = (a + b + c) / 3f;
                Assert.Greater(Vector3.Dot(normal, centroid), 0f, $"Triangle {i / 3} faces inward.");
                checkedTriangles++;
            }

            Assert.Greater(checkedTriangles, 500);
            Object.DestroyImmediate(mesh);
        }
    }
}
