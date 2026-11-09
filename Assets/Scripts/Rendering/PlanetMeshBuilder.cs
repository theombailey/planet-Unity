// =============================================================================
// PlanetMeshBuilder
// -----------------------------------------------------------------------------
// Purpose : Builds the planet's surface mesh as a cube-sphere: six square grids
//           of vertices projected onto a unit sphere. It uses the SAME face
//           layout and equiangular warp as the field textures (CubeSphere), so
//           a vertex's (u, v) on its face is also its coordinate in the field
//           textures.
//
// Idea    : For each face, lay an S x S grid of quads. Vertex (i, j) sits at
//           face coordinates s = -1 + 2i/S, t = -1 + 2j/S; its position is the
//           unit direction for that (face, s, t). Joining neighbouring vertices
//           with two triangles per quad gives the surface.
//
//           Vertices on a shared edge between two faces are duplicated (one copy
//           per face) because each copy carries a different face id and uv. They
//           sit at identical positions, so the surface has no gaps.
//
// Vertex data:
//     position : unit direction (the planet's transform scales it to the radius)
//     normal   : same as position (a sphere's normal is its position)
//     uv0      : (u, v) on the vertex's own face, each 0..1
//     uv1      : (face index, 0)
//
// Winding : Unity treats a triangle as front-facing when its vertices run
//           clockwise seen from outside. With right x up = outward normal, the
//           triangles (a, b, c) and (b, d, c) below, where a is the lower-left
//           vertex of a quad, b = +right, c = +up, d = both, satisfy that.
//
// Parameters: subdivisions - quads along one face edge (SimConfig, default 128).
//             Vertices = 6 (S+1)^2, triangles = 12 S^2. At S = 128 that is
//             ~99k vertices and ~197k triangles, so 32-bit indices are used.
// =============================================================================

using Terrarium.Sphere;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrarium.Rendering
{
    /// <summary>Generates the cube-sphere planet mesh.</summary>
    public static class PlanetMeshBuilder
    {
        /// <summary>Builds a unit-radius cube-sphere mesh with the given quads per face edge.</summary>
        public static Mesh Build(int subdivisions)
        {
            int quadsPerEdge = Mathf.Max(1, subdivisions);
            int vertsPerEdge = quadsPerEdge + 1;
            int vertsPerFace = vertsPerEdge * vertsPerEdge;

            var positions = new Vector3[CubeSphere.FaceCount * vertsPerFace];
            var uv0 = new Vector2[positions.Length];
            var uv1 = new Vector2[positions.Length];
            var triangles = new int[CubeSphere.FaceCount * quadsPerEdge * quadsPerEdge * 6];

            int triangleIndex = 0;
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                int faceStart = face * vertsPerFace;

                for (int j = 0; j < vertsPerEdge; j++)
                {
                    for (int i = 0; i < vertsPerEdge; i++)
                    {
                        float u = (float)i / quadsPerEdge;
                        float v = (float)j / quadsPerEdge;
                        int vertex = faceStart + j * vertsPerEdge + i;

                        positions[vertex] = CubeSphere.FaceUVToDir(face, u, v);
                        uv0[vertex] = new Vector2(u, v);
                        uv1[vertex] = new Vector2(face, 0f);
                    }
                }

                for (int j = 0; j < quadsPerEdge; j++)
                {
                    for (int i = 0; i < quadsPerEdge; i++)
                    {
                        int a = faceStart + j * vertsPerEdge + i;   // lower-left
                        int b = a + 1;                              // +right
                        int c = a + vertsPerEdge;                   // +up
                        int d = c + 1;                              // +right +up

                        triangles[triangleIndex++] = a;
                        triangles[triangleIndex++] = b;
                        triangles[triangleIndex++] = c;
                        triangles[triangleIndex++] = b;
                        triangles[triangleIndex++] = d;
                        triangles[triangleIndex++] = c;
                    }
                }
            }

            var mesh = new Mesh
            {
                name = "PlanetCubeSphere",
                indexFormat = IndexFormat.UInt32,
                vertices = positions,
                normals = positions,
                uv = uv0,
                uv2 = uv1,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
