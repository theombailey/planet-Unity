// =============================================================================
// TownMeshes
// -----------------------------------------------------------------------------
// Purpose : The two tiny meshes of the settlements, built from code: a house (a box
//           with a pitched roof) and a person (a little peg with a round head).
// Idea    : Both are flat-shaded and drawn with an ink outline by their shaders, so
//           each triangle gets its own three vertices and carries, in uv0:
//               xyz  barycentric coordinates of the corner (the shader's distance to the
//                    nearest edge draws the outline),
//               w    which part of the model this is (house: 0 wall, 1 roof; person: 0 body, 1 head).
//           The edge between the two triangles of a quad is not a real edge, so its
//           barycentric component is set to 1 at every corner of both triangles, which
//           stops the outline ever being drawn along it:
//               corner k of a triangle has barycentric e_k (a one in slot k);
//               to hide the edge opposite corner h, slot h is set to 1 at all three corners.
//           The shader can then use  edge = min(b.x, b.y, b.z)  as the distance to a visible edge.
// Winding : each face is wound so its normal points away from the model's centre.
// Units   : the house is 1 wide and 1 deep (x and z from -0.5 to 0.5), 0.6 high at the eaves and
//           1.1 high at the ridge, which runs along x. The shader scales the roof's height for the
//           climate. The person is 1 unit tall, 0.5 wide at the feet.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace Terrarium.Towns
{
    /// <summary>Builds the house and person meshes.</summary>
    public static class TownMeshes
    {
        /// <summary>Height of the house's walls (the shader scales the roof above this).</summary>
        public const float HouseWallHeight = 0.6f;

        /// <summary>Height of the house's ridge before the shader scales the roof.</summary>
        public const float HouseRidgeHeight = 1.1f;

        private sealed class FlatMeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector4> barycentrics = new List<Vector4>();
            private readonly List<int> triangles = new List<int>();
            private readonly Vector3 centre;

            public FlatMeshBuilder(Vector3 modelCentre)
            {
                centre = modelCentre;
            }

            // One triangle; `hidden` is the corner (0, 1, 2) whose opposite edge is not drawn, or -1 to draw all three.
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, float part, int hidden)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 faceCentre = (a + b + c) / 3f;

                // Unity treats clockwise (seen from the front) as front-facing; flip the order if the normal faces inward.
                if (Vector3.Dot(normal, faceCentre - centre) < 0f)
                {
                    (b, c) = (c, b);
                    normal = -normal;
                    hidden = hidden == 1 ? 2 : (hidden == 2 ? 1 : hidden);
                }

                normal.Normalize();
                int first = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                for (int i = 0; i < 3; i++)
                {
                    normals.Add(normal);
                    var bary = new Vector4(i == 0 ? 1f : 0f, i == 1 ? 1f : 0f, i == 2 ? 1f : 0f, part);
                    if (hidden >= 0)
                    {
                        bary[hidden] = 1f;
                    }

                    barycentrics.Add(bary);
                }

                // With the normal fixed to point outward above, this order is the one Unity treats as front-facing.
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }

            // A quad given in order round its edge; its diagonal (a to c) is hidden.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float part)
            {
                Triangle(a, b, c, part, 1);
                Triangle(a, c, d, part, 2);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, barycentrics);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        /// A house: four walls, two roof slopes and two gable ends. The roof's vertices carry part 1 so the shader can
        /// give it its own colour and height; everything else is part 0.
        /// </summary>
        public static Mesh BuildHouse()
        {
            float wall = HouseWallHeight;
            float ridge = HouseRidgeHeight;
            var builder = new FlatMeshBuilder(new Vector3(0f, 0.45f, 0f));

            var a = new Vector3(-0.5f, 0f, -0.5f);
            var b = new Vector3(0.5f, 0f, -0.5f);
            var c = new Vector3(0.5f, 0f, 0.5f);
            var d = new Vector3(-0.5f, 0f, 0.5f);
            var a2 = new Vector3(-0.5f, wall, -0.5f);
            var b2 = new Vector3(0.5f, wall, -0.5f);
            var c2 = new Vector3(0.5f, wall, 0.5f);
            var d2 = new Vector3(-0.5f, wall, 0.5f);
            var ridgeLeft = new Vector3(-0.5f, ridge, 0f);
            var ridgeRight = new Vector3(0.5f, ridge, 0f);

            builder.Quad(a, b, b2, a2, 0f);                  // front wall (z = -0.5)
            builder.Quad(c, d, d2, c2, 0f);                  // back wall (z = +0.5)
            builder.Quad(d, a, a2, d2, 0f);                  // left wall (x = -0.5), up to the eaves
            builder.Quad(b, c, c2, b2, 0f);                  // right wall (x = +0.5), up to the eaves
            builder.Triangle(a2, d2, ridgeLeft, 0f, -1);     // left gable
            builder.Triangle(b2, c2, ridgeRight, 0f, -1);    // right gable
            builder.Quad(a2, b2, ridgeRight, ridgeLeft, 1f); // front roof slope
            builder.Quad(c2, d2, ridgeLeft, ridgeRight, 1f); // back roof slope

            return builder.Build("House");
        }

        /// <summary>
        /// A person: a four-sided tapering body with a small diamond head on top. The head carries part 1 (skin), the
        /// body part 0 (clothes).
        /// </summary>
        public static Mesh BuildPerson()
        {
            const float bodyHeight = 0.78f;
            const float headRadius = 0.16f;
            var builder = new FlatMeshBuilder(new Vector3(0f, 0.5f, 0f));

            var footA = new Vector3(-0.25f, 0f, -0.25f);
            var footB = new Vector3(0.25f, 0f, -0.25f);
            var footC = new Vector3(0.25f, 0f, 0.25f);
            var footD = new Vector3(-0.25f, 0f, 0.25f);
            var neck = new Vector3(0f, bodyHeight, 0f);

            builder.Triangle(footA, footB, neck, 0f, -1);
            builder.Triangle(footB, footC, neck, 0f, -1);
            builder.Triangle(footC, footD, neck, 0f, -1);
            builder.Triangle(footD, footA, neck, 0f, -1);

            // The head: an octahedron centred above the neck.
            var headCentre = new Vector3(0f, bodyHeight + headRadius * 0.9f, 0f);
            Vector3 top = headCentre + Vector3.up * headRadius;
            Vector3 bottom = headCentre - Vector3.up * headRadius;
            Vector3 east = headCentre + Vector3.right * headRadius;
            Vector3 west = headCentre - Vector3.right * headRadius;
            Vector3 north = headCentre + Vector3.forward * headRadius;
            Vector3 south = headCentre - Vector3.forward * headRadius;

            builder.Triangle(top, east, north, 1f, -1);
            builder.Triangle(top, north, west, 1f, -1);
            builder.Triangle(top, west, south, 1f, -1);
            builder.Triangle(top, south, east, 1f, -1);
            builder.Triangle(bottom, north, east, 1f, -1);
            builder.Triangle(bottom, west, north, 1f, -1);
            builder.Triangle(bottom, south, west, 1f, -1);
            builder.Triangle(bottom, east, south, 1f, -1);

            return builder.Build("Person");
        }
    }
}
