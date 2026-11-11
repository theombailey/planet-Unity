// =============================================================================
// TreeMesher
// -----------------------------------------------------------------------------
// Purpose : Turns a tree's branch skeleton (SpaceColonization) into a triangle
//           mesh of branches, and picks where the leaves go.
// Idea    : GENERALISED CYLINDERS. Each branch segment is a short tube from its
//           parent node to its own node: a ring of `sides` vertices at each end,
//           the ring at the child end with the child's radius and the ring at the
//           parent end with (almost) the parent's radius, so branches taper. Tubes
//           of neighbouring segments overlap at joints instead of being welded;
//           with painterly shading that reads fine, and it is far simpler.
//
//           A ring is a circle in the plane at right angles to the segment's
//           direction d. To place its vertices we need two unit vectors u, v at right
//           angles to d and to each other:
//               u = normalize( d x reference ),   v = d x u
//           where `reference` is any vector not parallel to d (up, unless d is nearly up).
//           Vertex k of a ring (k = 0..sides-1) is at angle t = 2 pi k / sides:
//               offset = u cos t + v sin t           (also the outward normal)
//               vertex = node position + offset * radius
//           Two triangles join each pair of neighbouring ring vertices (see Winding).
//
// Growth animation: every vertex also carries when its segment was born, how long it takes to
//           grow, and where the segment starts. The wood shader uses them to slide the child-end
//           ring out from the parent end over that time, so the mesh itself never changes
//           between growth steps and branches visibly grow instead of popping in.
//
// Rebuilding, not growing: a new tip changes the radius of every branch below it
//           (the pipe model), so the whole mesh is rebuilt after a growth step, from
//           scratch, into shared scratch arrays and then copied into the tree's Mesh.
//           The caller spreads rebuilds over frames so one frame never pays for many.
//
// Winding : Unity treats a triangle as front-facing when its vertices run clockwise seen
//           from outside, which makes Cross(b - a, c - a) point outward. With (u, v, d)
//           a right-handed set, the triangles (A_k, A_k+1, B_k) and (A_k+1, B_k+1, B_k)
//           (A = ring at the parent end, B = ring at the child end) satisfy that.
//
// Leaves  : twig nodes (tips, and nodes whose branch is still thin) each get a couple of
//           leaves at a random point near the node. If there would be more than the
//           per-tree limit, an even selection is kept. Leaves are written in the PLANET's
//           frame (see LeafData) so the GPU can draw all trees' leaves with one call.
// =============================================================================

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Terrarium.Trees
{
    /// <summary>One leaf, in the layout the leaf shader reads (40 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LeafData
    {
        /// <summary>Size in bytes of one LeafData, for creating the GPU buffer.</summary>
        public const int Stride = 40;

        /// <summary>Position in the planet's local space.</summary>
        public Vector3 Position;

        /// <summary>Leaf size in world units (0 = unused slot).</summary>
        public float Size;

        /// <summary>The surface normal under the tree (planet frame), used for the leaf's lighting.</summary>
        public Vector3 Normal;

        /// <summary>A random number in [0,1) giving this leaf its own shade and drop time.</summary>
        public float Phase;

        /// <summary>Sim time at which the leaf starts to unfurl (when its branch segment has finished growing).</summary>
        public float Birth;

        /// <summary>Unused; keeps the struct a multiple of 8 bytes.</summary>
        public float Reserved;
    }

    /// <summary>Builds branch meshes and leaf lists from a branch skeleton, using shared scratch memory.</summary>
    public sealed class TreeMesher
    {
        private Vector3[] vertices = new Vector3[4096];
        private Vector3[] normals = new Vector3[4096];
        private int[] triangles = new int[8192];
        private Vector3[] growthA = new Vector3[4096];   // uv0: x = birth time, y = grow seconds, z = 1 for a vertex that grows
        private Vector3[] growthB = new Vector3[4096];   // uv1: where the segment starts (the parent end), so a growing vertex can slide out from it

        /// <summary>
        /// Rebuilds a mesh from the current skeleton. Returns the number of vertices (0 for a tree with no branches yet).
        /// </summary>
        /// <param name="colony">The grown skeleton; <see cref="SpaceColonization.ComputeRadii"/> must have been called.</param>
        /// <param name="sides">Vertices per ring (3 or more).</param>
        /// <param name="mesh">The mesh to overwrite.</param>
        public int Build(SpaceColonization colony, int sides, Mesh mesh)
        {
            int segments = colony.NodeCount - 1;
            if (segments < 1)
            {
                mesh.Clear();
                return 0;
            }

            int vertexCount = 2 * sides * segments;
            int indexCount = 6 * sides * segments;
            EnsureCapacity(vertexCount, indexCount);

            int v = 0;
            int t = 0;
            for (int node = 1; node < colony.NodeCount; node++)
            {
                Vector3 start = colony.PositionOf(colony.ParentOf(node));
                Vector3 end = colony.PositionOf(node);
                float endRadius = colony.RadiusOf(node);

                // The tube starts at (almost) the parent's radius so a limb tapers smoothly; a thin side branch
                // off a thick limb must not start as wide as the limb, so it is capped near its own radius.
                float startRadius = Mathf.Min(colony.RadiusOf(colony.ParentOf(node)), endRadius * 1.25f);

                float birth = colony.BirthOf(node);
                float growSeconds = colony.GrowSecondsOf(node);

                Vector3 direction = (end - start).normalized;
                Vector3 reference = Mathf.Abs(direction.y) < 0.95f ? Vector3.up : Vector3.right;
                Vector3 u = Vector3.Cross(direction, reference).normalized;
                Vector3 w = Vector3.Cross(direction, u);

                int first = v;
                for (int k = 0; k < sides; k++)
                {
                    float angle = 2f * Mathf.PI * k / sides;
                    Vector3 offset = u * Mathf.Cos(angle) + w * Mathf.Sin(angle);

                    vertices[v] = start + offset * startRadius;
                    normals[v] = offset;
                    vertices[v + sides] = end + offset * endRadius;
                    normals[v + sides] = offset;

                    // Growth animation data (see Wood.shader): the ring at the parent end stays put; the ring at
                    // the child end slides out from `start` (and fattens) as the segment grows.
                    growthA[v] = new Vector3(birth, growSeconds, 0f);
                    growthA[v + sides] = new Vector3(birth, growSeconds, 1f);
                    growthB[v] = start;
                    growthB[v + sides] = start;
                    v++;
                }

                for (int k = 0; k < sides; k++)
                {
                    int a0 = first + k;
                    int a1 = first + (k + 1) % sides;
                    int b0 = a0 + sides;
                    int b1 = a1 + sides;

                    triangles[t++] = a0;
                    triangles[t++] = a1;
                    triangles[t++] = b0;
                    triangles[t++] = a1;
                    triangles[t++] = b1;
                    triangles[t++] = b0;
                }

                v += sides;
            }

            mesh.Clear();
            mesh.indexFormat = vertexCount > 60_000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices, 0, vertexCount);
            mesh.SetNormals(normals, 0, vertexCount);
            mesh.SetUVs(0, growthA, 0, vertexCount);
            mesh.SetUVs(1, growthB, 0, vertexCount);
            mesh.SetTriangles(triangles, 0, indexCount, 0, true);
            return vertexCount;
        }

        /// <summary>
        /// Picks leaf positions at the twigs and writes them into <paramref name="leaves"/>, in the planet's frame.
        /// Returns how many were written.
        /// </summary>
        /// <param name="colony">The grown skeleton with radii.</param>
        /// <param name="treeToPlanet">Transforms tree-local points (root at the origin, +y up) into the planet's local space.</param>
        /// <param name="surfaceNormal">The surface normal at the root, in the planet's frame.</param>
        /// <param name="leafSize">Size of one leaf, in world units.</param>
        /// <param name="twigRadius">Branches no thicker than this carry leaves.</param>
        /// <param name="seed">Seed for the random offsets.</param>
        /// <param name="leaves">Output array; its length is the per-tree limit.</param>
        public int BuildLeaves(SpaceColonization colony, Matrix4x4 treeToPlanet, Vector3 surfaceNormal, float leafSize, float twigRadius, int seed, LeafData[] leaves)
        {
            var rng = new Rng(seed);

            // Count candidate nodes so that, if there are too many, an even selection can be kept.
            int candidates = 0;
            for (int i = 1; i < colony.NodeCount; i++)
            {
                if (colony.ChildCountOf(i) == 0 || colony.RadiusOf(i) <= twigRadius)
                {
                    candidates++;
                }
            }

            const int leavesPerNode = 2;
            int wanted = candidates * leavesPerNode;
            float keep = wanted > leaves.Length ? (float)leaves.Length / wanted : 1f;

            int written = 0;
            float credit = 0f;
            for (int i = 1; i < colony.NodeCount && written < leaves.Length; i++)
            {
                if (colony.ChildCountOf(i) != 0 && colony.RadiusOf(i) > twigRadius)
                {
                    continue;
                }

                for (int k = 0; k < leavesPerNode && written < leaves.Length; k++)
                {
                    credit += keep;
                    if (credit < 1f)
                    {
                        continue;
                    }

                    credit -= 1f;
                    Vector3 scatter = new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * (leafSize * 1.2f);
                    leaves[written] = new LeafData
                    {
                        Position = treeToPlanet.MultiplyPoint3x4(colony.PositionOf(i) + scatter),
                        Size = leafSize * rng.Range(0.7f, 1.3f),
                        Normal = surfaceNormal,
                        Phase = rng.NextFloat(),
                        Birth = colony.BirthOf(i) + colony.GrowSecondsOf(i),
                    };
                    written++;
                }
            }

            return written;
        }

        private void EnsureCapacity(int vertexCount, int indexCount)
        {
            if (vertexCount > vertices.Length)
            {
                int size = Mathf.NextPowerOfTwo(vertexCount);
                Array.Resize(ref vertices, size);
                Array.Resize(ref normals, size);
                Array.Resize(ref growthA, size);
                Array.Resize(ref growthB, size);
            }

            if (indexCount > triangles.Length)
            {
                Array.Resize(ref triangles, Mathf.NextPowerOfTwo(indexCount));
            }
        }
    }
}
