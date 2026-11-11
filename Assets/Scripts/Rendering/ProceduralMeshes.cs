// =============================================================================
// ProceduralMeshes
// -----------------------------------------------------------------------------
// Purpose : Small meshes built from code instead of imported art.
// Contents: BuildRing - a flat ring (annulus) lying in the XZ plane, facing up.
//           Used for the decorative levitation ring on the desk.
//           BuildUnitSphere - a smooth radius-1 sphere for the sky dome, the
//           atmosphere shell and the cloud layer.
// Idea    : Ring: walk round the circle in `segments` steps. At each step place one
//           vertex on the inner radius and one on the outer radius, then join
//           neighbouring pairs with two triangles.
//           Sphere: a grid of rows (latitude) and columns (longitude), joined
//           with two triangles per cell.
// Equations:
//     angle_i = 2*pi * i / segments
//     inner_i = (cos angle_i, 0, sin angle_i) * innerRadius
//     outer_i = (cos angle_i, 0, sin angle_i) * outerRadius
//     sphere vertex = (sin(polar) cos(azimuth), cos(polar), sin(polar) sin(azimuth)),
//                     polar = pi * row / rows, azimuth = 2*pi * column / columns
// =============================================================================

using UnityEngine;

namespace Terrarium.Rendering
{
    /// <summary>Builds simple meshes from code.</summary>
    public static class ProceduralMeshes
    {
        /// <summary>Builds a flat ring in the XZ plane with its normal pointing up (+Y).</summary>
        /// <param name="innerRadius">Radius of the hole, in mesh units.</param>
        /// <param name="outerRadius">Radius of the outer edge, in mesh units.</param>
        /// <param name="segments">Number of steps round the circle (more = rounder).</param>
        public static Mesh BuildRing(float innerRadius, float outerRadius, int segments)
        {
            // One extra pair closes the loop with its own UVs.
            int pairCount = segments + 1;
            var vertices = new Vector3[pairCount * 2];
            var normals = new Vector3[pairCount * 2];
            var uvs = new Vector2[pairCount * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < pairCount; i++)
            {
                float fraction = (float)i / segments;
                float angle = 2f * Mathf.PI * fraction;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices[i * 2] = direction * innerRadius;
                vertices[i * 2 + 1] = direction * outerRadius;
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
                uvs[i * 2] = new Vector2(fraction, 0f);
                uvs[i * 2 + 1] = new Vector2(fraction, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int inner = i * 2;
                int outer = inner + 1;
                int nextInner = inner + 2;
                int nextOuter = inner + 3;

                // Unity treats clockwise triangles (seen from the front) as front-facing; these face +Y.
                int t = i * 6;
                triangles[t] = inner;
                triangles[t + 1] = nextInner;
                triangles[t + 2] = outer;
                triangles[t + 3] = outer;
                triangles[t + 4] = nextInner;
                triangles[t + 5] = nextOuter;
            }

            var mesh = new Mesh { name = "Ring" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Builds a wedge: a box whose top slopes from a low front edge to a higher back edge (the console body).
        /// It stands on y = 0, is centred in x and z, and has flat-shaded faces.
        /// </summary>
        /// <param name="width">Size along x.</param>
        /// <param name="depth">Size along z (front is -z, back is +z).</param>
        /// <param name="frontHeight">Height at the front (-z) edge.</param>
        /// <param name="backHeight">Height at the back (+z) edge.</param>
        public static Mesh BuildWedge(float width, float depth, float frontHeight, float backHeight)
        {
            float x = width * 0.5f;
            float z = depth * 0.5f;
            var frontBottomLeft = new Vector3(-x, 0f, -z);
            var frontBottomRight = new Vector3(x, 0f, -z);
            var frontTopLeft = new Vector3(-x, frontHeight, -z);
            var frontTopRight = new Vector3(x, frontHeight, -z);
            var backBottomLeft = new Vector3(-x, 0f, z);
            var backBottomRight = new Vector3(x, 0f, z);
            var backTopLeft = new Vector3(-x, backHeight, z);
            var backTopRight = new Vector3(x, backHeight, z);

            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();

            // Each face is a quad given in any order round its edge; the winding is fixed so it faces `outward`.
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, outward) < 0f)
                {
                    (b, d) = (d, b);
                    normal = Vector3.Cross(b - a, c - a);
                }

                normal.Normalize();
                int first = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                for (int i = 0; i < 4; i++)
                {
                    normals.Add(normal);
                }

                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            Vector3 slopeNormal = Vector3.Cross(backTopLeft - frontTopLeft, frontTopRight - frontTopLeft);
            if (slopeNormal.y < 0f)
            {
                slopeNormal = -slopeNormal;
            }

            Face(frontTopLeft, frontTopRight, backTopRight, backTopLeft, slopeNormal);                  // the sloping top
            Face(frontBottomLeft, frontBottomRight, frontTopRight, frontTopLeft, Vector3.back);         // front
            Face(backBottomLeft, backBottomRight, backTopRight, backTopLeft, Vector3.forward);          // back
            Face(frontBottomLeft, frontBottomRight, backBottomRight, backBottomLeft, Vector3.down);     // bottom
            Face(frontBottomLeft, frontTopLeft, backTopLeft, backBottomLeft, Vector3.left);             // left side
            Face(frontBottomRight, frontTopRight, backTopRight, backBottomRight, Vector3.right);        // right side

            var mesh = new Mesh { name = "Wedge" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Builds a smooth sphere of radius 1 (a latitude-longitude grid) with outward normals. Used for the sky
        /// dome, the atmosphere shell and the cloud layer, which are all drawn by their shaders, so only the
        /// outline of the mesh matters and it just has to be round.
        /// </summary>
        /// <param name="longitudeSegments">Steps round the equator.</param>
        /// <param name="latitudeSegments">Steps from pole to pole.</param>
        public static Mesh BuildUnitSphere(int longitudeSegments, int latitudeSegments)
        {
            int columns = longitudeSegments + 1;
            int rows = latitudeSegments + 1;
            var vertices = new Vector3[columns * rows];
            var uvs = new Vector2[columns * rows];
            var triangles = new int[longitudeSegments * latitudeSegments * 6];

            for (int row = 0; row < rows; row++)
            {
                float v = (float)row / latitudeSegments;
                float polar = Mathf.PI * v;                    // 0 at the north pole, pi at the south
                float ring = Mathf.Sin(polar);
                float y = Mathf.Cos(polar);

                for (int column = 0; column < columns; column++)
                {
                    float u = (float)column / longitudeSegments;
                    float azimuth = 2f * Mathf.PI * u;
                    int i = row * columns + column;
                    vertices[i] = new Vector3(ring * Mathf.Cos(azimuth), y, ring * Mathf.Sin(azimuth));
                    uvs[i] = new Vector2(u, 1f - v);
                }
            }

            int t = 0;
            for (int row = 0; row < latitudeSegments; row++)
            {
                for (int column = 0; column < longitudeSegments; column++)
                {
                    int a = row * columns + column;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;

                    // Clockwise seen from outside, which Unity treats as front-facing.
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = d;
                    triangles[t++] = c;
                }
            }

            var mesh = new Mesh { name = "UnitSphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = vertices;     // on a unit sphere the normal is the position
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
