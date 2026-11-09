// =============================================================================
// CubeSphere
// -----------------------------------------------------------------------------
// Purpose : Maps between directions on a sphere and texels on a "cube-sphere"
//           grid: six square faces (the faces of a cube) projected out onto a
//           sphere. Every simulation field (nutrients, temperature, physarum
//           trail...) is stored as six N x N images, one per face, and this
//           class is how the rest of the code finds the texel for a direction.
//           HLSL twin: Assets/Shaders/Include/CubeSphere.hlsl. The two files
//           must stay in step; a GPU test checks that they agree.
//
// Idea    : A latitude/longitude grid pinches to a point at each pole, so
//           texels there are tiny and a diffusion step there is unstable. A
//           cube has no poles: all six faces are alike, and the only awkward
//           places are the 12 face edges and 8 corners, which the
//           neighbour-lookup below handles with one trick (see ResolveTexel).
//
// Face layout:
//           index  0   1   2   3   4   5
//           face  +X  -X  +Y  -Y  +Z  -Z
//           Each face has an outward normal n and two in-plane axes, "right"
//           (the s direction) and "up" (the t direction), chosen so that
//           right x up = n. That keeps every face's triangles wound the same
//           way (see PlanetMeshBuilder).
//
// Equations (s, t are face coordinates in [-1, 1]; (0,0) is the face centre):
//     Direction -> face : the face whose axis has the largest |component|.
//                         (ties: X beats Y beats Z; positive beats negative)
//     Cube-plane point  : a = dot(d, right) / dot(d, n)      b likewise with up
//     EQUIANGULAR WARP  : s = atan(a) * 4/pi                 t likewise
//                         inverse:   a = tan(s * pi/4)
//     Face -> direction : d = normalize( n + a*right + b*up )
//
//     Why warp? Without it (s = a) a texel near a face edge covers about a
//     third of the area of one at the centre. With it, angles along each face
//     axis are exactly uniform (angle = s * pi/4), which evens texel sizes to
//     within a factor of ~1.4.
//
//     Texel (x, y) of an N x N face has centre   s = (x + 0.5) / N * 2 - 1.
//
//     Neighbour trick: to find the texel at (x+dx, y+dy) even when that is off
//     the face, extend the face's own grid past its edge (the formula above
//     works for |s| > 1), turn that point into a direction, and ask which
//     texel contains the direction. No adjacency tables are needed.
//     A step across exactly ONE edge is done a little more carefully: the
//     position along the edge is kept exactly (the point is pushed only a hair
//     beyond the edge, not a whole texel). Along a shared edge the two faces'
//     texel rows line up one-to-one, so this pairing is MUTUAL (if B is A's
//     neighbour across the edge, A is B's), which diffusion needs in order to
//     conserve what it moves. Steps past a corner (both coordinates off the
//     face) use the plain extended-grid lookup, where three faces meet and no
//     four-neighbour rule can be perfectly mutual.
//
// Parameters: n - texels along one face edge (the field resolution).
// =============================================================================

using UnityEngine;

namespace Terrarium.Sphere
{
    /// <summary>Address of one texel: face index 0..5 and integer coordinates 0..N-1.</summary>
    public readonly struct Texel
    {
        /// <summary>Face index: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z.</summary>
        public readonly int Face;

        /// <summary>Column along the face's "right" axis, 0..N-1.</summary>
        public readonly int X;

        /// <summary>Row along the face's "up" axis, 0..N-1.</summary>
        public readonly int Y;

        /// <summary>Creates a texel address.</summary>
        public Texel(int face, int x, int y)
        {
            Face = face;
            X = x;
            Y = y;
        }

        /// <summary>True when both texels have the same face and coordinates.</summary>
        public bool SameAs(Texel other)
        {
            return Face == other.Face && X == other.X && Y == other.Y;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"face {Face} ({X}, {Y})";
        }
    }

    /// <summary>The four texels and two blend weights for a bilinear sample.</summary>
    public readonly struct BilinearTaps
    {
        /// <summary>Texel at (x0, y0): lower-left of the sample point.</summary>
        public readonly Texel T00;

        /// <summary>Texel at (x0+1, y0).</summary>
        public readonly Texel T10;

        /// <summary>Texel at (x0, y0+1).</summary>
        public readonly Texel T01;

        /// <summary>Texel at (x0+1, y0+1).</summary>
        public readonly Texel T11;

        /// <summary>Blend weight toward the +x taps, 0..1.</summary>
        public readonly float Wx;

        /// <summary>Blend weight toward the +y taps, 0..1.</summary>
        public readonly float Wy;

        /// <summary>Creates a tap set.</summary>
        public BilinearTaps(Texel t00, Texel t10, Texel t01, Texel t11, float wx, float wy)
        {
            T00 = t00;
            T10 = t10;
            T01 = t01;
            T11 = t11;
            Wx = wx;
            Wy = wy;
        }
    }

    /// <summary>Pure maths for the six-face equiangular cube-sphere (see file header).</summary>
    public static class CubeSphere
    {
        /// <summary>Number of cube faces.</summary>
        public const int FaceCount = 6;

        private const float QuarterPi = Mathf.PI * 0.25f;

        // How far past a face edge (in s or t units) a single-edge neighbour lookup reaches. It must be far
        // smaller than one texel (2/N, down to 0.004 at N = 512) but large enough to be clear of rounding.
        private const float EdgeOvershoot = 1e-4f;

        // Per face: outward normal, "right" (s) axis, "up" (t) axis. Order +X -X +Y -Y +Z -Z.
        // For every face right x up = normal.
        private static readonly Vector3[] Normals =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        };

        private static readonly Vector3[] Rights =
        {
            new Vector3(0, 0, -1), new Vector3(0, 0, 1), new Vector3(1, 0, 0),
            new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
        };

        private static readonly Vector3[] Ups =
        {
            new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1),
            new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(0, 1, 0),
        };

        /// <summary>Outward normal of a face.</summary>
        public static Vector3 FaceNormal(int face)
        {
            return Normals[face];
        }

        /// <summary>The face's "right" axis: the direction in which s (and texel x) increases.</summary>
        public static Vector3 FaceRight(int face)
        {
            return Rights[face];
        }

        /// <summary>The face's "up" axis: the direction in which t (and texel y) increases.</summary>
        public static Vector3 FaceUp(int face)
        {
            return Ups[face];
        }

        /// <summary>The face a direction points through: largest |component| wins; ties go X, then Y, then Z.</summary>
        public static int FaceOf(Vector3 direction)
        {
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);

            if (ax >= ay && ax >= az)
            {
                return direction.x >= 0f ? 0 : 1;
            }

            if (ay >= az)
            {
                return direction.y >= 0f ? 2 : 3;
            }

            return direction.z >= 0f ? 4 : 5;
        }

        /// <summary>Direction -> face and warped face coordinates s, t in [-1, 1].</summary>
        public static void DirToFaceST(Vector3 direction, out int face, out float s, out float t)
        {
            face = FaceOf(direction);
            float depth = Vector3.Dot(direction, Normals[face]);
            float a = Vector3.Dot(direction, Rights[face]) / depth;
            float b = Vector3.Dot(direction, Ups[face]) / depth;
            s = Mathf.Atan(a) / QuarterPi;
            t = Mathf.Atan(b) / QuarterPi;
        }

        /// <summary>Direction -> face and texture coordinates u, v in [0, 1] (u = (s + 1) / 2).</summary>
        public static void DirToFaceUV(Vector3 direction, out int face, out float u, out float v)
        {
            DirToFaceST(direction, out face, out float s, out float t);
            u = (s + 1f) * 0.5f;
            v = (t + 1f) * 0.5f;
        }

        /// <summary>Face coordinates s, t -> unit direction. Also valid just beyond [-1, 1] (used for neighbours).</summary>
        public static Vector3 FaceSTToDir(int face, float s, float t)
        {
            float a = Mathf.Tan(s * QuarterPi);
            float b = Mathf.Tan(t * QuarterPi);
            return (Normals[face] + a * Rights[face] + b * Ups[face]).normalized;
        }

        /// <summary>Face texture coordinates u, v in [0, 1] -> unit direction.</summary>
        public static Vector3 FaceUVToDir(int face, float u, float v)
        {
            return FaceSTToDir(face, u * 2f - 1f, v * 2f - 1f);
        }

        /// <summary>The texel of an N x N face that contains the direction.</summary>
        public static Texel DirToTexel(Vector3 direction, int n)
        {
            DirToFaceST(direction, out int face, out float s, out float t);
            return new Texel(face, TexelIndex(s, n), TexelIndex(t, n));
        }

        /// <summary>Unit direction through the centre of a texel.</summary>
        public static Vector3 TexelCentreDir(int face, int x, int y, int n)
        {
            return FaceSTToDir(face, TexelCentreST(x, n), TexelCentreST(y, n));
        }

        /// <summary>
        /// Brings a texel address back onto the sphere. If (x, y) is inside the face it is returned
        /// unchanged; if it is off an edge (or a corner) the face's grid is extended past the edge
        /// and the texel that contains that point on the neighbouring face is returned.
        /// </summary>
        public static Texel ResolveTexel(int face, int x, int y, int n)
        {
            bool outsideX = x < 0 || x >= n;
            bool outsideY = y < 0 || y >= n;

            if (!outsideX && !outsideY)
            {
                return new Texel(face, x, y);
            }

            if (outsideX != outsideY)
            {
                // Across exactly one edge: keep the position along the edge, and go only a hair past it.
                float beyond = EdgeOvershoot;
                if (outsideX)
                {
                    float s = (x < 0 ? -1f : 1f) * (1f + beyond);
                    return DirToTexel(FaceSTToDir(face, s, TexelCentreST(y, n)), n);
                }

                float t = (y < 0 ? -1f : 1f) * (1f + beyond);
                return DirToTexel(FaceSTToDir(face, TexelCentreST(x, n), t), n);
            }

            return DirToTexel(FaceSTToDir(face, TexelCentreST(x, n), TexelCentreST(y, n)), n);
        }

        /// <summary>
        /// The texel at offset (dx, dy) from (face, x, y), seam-aware: correct across face edges
        /// and corners with no adjacency table. Offsets of -1, 0 or +1 are expected.
        /// </summary>
        public static Texel NeighbourTexel(int face, int x, int y, int dx, int dy, int n)
        {
            return ResolveTexel(face, x + dx, y + dy, n);
        }

        /// <summary>
        /// The four texels and blend weights for a bilinear sample at a direction. Taps that fall
        /// off the face are resolved onto the neighbouring face, so the sample is continuous over seams.
        /// </summary>
        public static BilinearTaps BilinearAt(Vector3 direction, int n)
        {
            DirToFaceST(direction, out int face, out float s, out float t);

            // Position in texel units where texel centres sit at integers.
            float fx = (s + 1f) * 0.5f * n - 0.5f;
            float fy = (t + 1f) * 0.5f * n - 0.5f;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);

            return new BilinearTaps(
                ResolveTexel(face, x0, y0, n),
                ResolveTexel(face, x0 + 1, y0, n),
                ResolveTexel(face, x0, y0 + 1, n),
                ResolveTexel(face, x0 + 1, y0 + 1, n),
                fx - x0,
                fy - y0);
        }

        private static float TexelCentreST(int index, int n)
        {
            return (index + 0.5f) / n * 2f - 1f;
        }

        private static int TexelIndex(float st, int n)
        {
            return Mathf.Clamp(Mathf.FloorToInt((st + 1f) * 0.5f * n), 0, n - 1);
        }
    }
}
