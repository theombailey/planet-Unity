// =============================================================================
// PlanetMeshBuilderTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the cube-sphere planet mesh: the vertex and triangle counts,
//           that every vertex lies on the unit sphere, that its face uv matches
//           the CubeSphere mapping (so mesh and field textures line up), that
//           every triangle faces outward (correct winding), and that the surface
//           area is close to that of a sphere.
// =============================================================================

using NUnit.Framework;
using Terrarium.Rendering;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class PlanetMeshBuilderTests
    {
        private const int Subdivisions = 24;

        private Mesh mesh;

        [SetUp]
        public void BuildMesh()
        {
            mesh = PlanetMeshBuilder.Build(Subdivisions);
        }

        [TearDown]
        public void DestroyMesh()
        {
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void PlanetMesh_HasExpectedCounts()
        {
            int vertsPerEdge = Subdivisions + 1;

            Assert.AreEqual(6 * vertsPerEdge * vertsPerEdge, mesh.vertexCount);
            Assert.AreEqual(12 * Subdivisions * Subdivisions, mesh.triangles.Length / 3);
        }

        [Test]
        public void PlanetMesh_AllVerticesLieOnTheUnitSphere()
        {
            foreach (Vector3 vertex in mesh.vertices)
            {
                Assert.AreEqual(1f, vertex.magnitude, 1e-5f);
            }
        }

        [Test]
        public void PlanetMesh_VertexFaceUV_MatchesCubeSphereMapping()
        {
            Vector3[] vertices = mesh.vertices;
            var uv0 = new System.Collections.Generic.List<Vector2>();
            var uv1 = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(0, uv0);
            mesh.GetUVs(1, uv1);

            for (int i = 0; i < vertices.Length; i++)
            {
                int face = (int)uv1[i].x;
                Vector3 expected = CubeSphere.FaceUVToDir(face, uv0[i].x, uv0[i].y);
                Assert.Less(Vector3.Distance(vertices[i], expected), 1e-5f, $"Vertex {i} is not where its (face, u, v) says.");
            }
        }

        [Test]
        public void PlanetMesh_AllTrianglesFaceOutward()
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                Vector3 b = vertices[triangles[t + 1]];
                Vector3 c = vertices[triangles[t + 2]];

                // Unity's front face is clockwise seen from outside, which makes Cross(b - a, c - a) point outward.
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 centroid = (a + b + c) / 3f;

                Assert.Greater(Vector3.Dot(normal, centroid), 0f, $"Triangle {t / 3} is wound inward.");
            }
        }

        [Test]
        public void PlanetMesh_SurfaceAreaIsCloseToASphere()
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            double area = 0.0;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                area += 0.5 * Vector3.Cross(vertices[triangles[t + 1]] - a, vertices[triangles[t + 2]] - a).magnitude;
            }

            // Flat triangles inside a sphere have slightly less area than the sphere itself.
            double sphere = 4.0 * System.Math.PI;
            Assert.Less(area, sphere);
            Assert.Greater(area, sphere * 0.99);
        }

        [Test]
        public void PlanetMesh_TexelsAreSimilarSizeEverywhere()
        {
            // Compares the largest and smallest quads. The equiangular warp should keep them within ~1.5x
            // (without the warp the ratio would be about 3x).
            Vector3[] vertices = mesh.vertices;
            int vertsPerEdge = Subdivisions + 1;
            float smallest = float.MaxValue;
            float largest = 0f;

            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int j = 0; j < Subdivisions; j++)
                {
                    for (int i = 0; i < Subdivisions; i++)
                    {
                        int a = face * vertsPerEdge * vertsPerEdge + j * vertsPerEdge + i;
                        float quadArea = Vector3.Cross(vertices[a + 1] - vertices[a], vertices[a + vertsPerEdge] - vertices[a]).magnitude;
                        smallest = Mathf.Min(smallest, quadArea);
                        largest = Mathf.Max(largest, quadArea);
                    }
                }
            }

            Assert.Less(largest / smallest, 1.6f);
        }
    }
}
