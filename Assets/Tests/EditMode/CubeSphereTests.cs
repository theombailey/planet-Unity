// =============================================================================
// CubeSphereTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the cube-sphere maths on the CPU:
//           (a) direction -> face coords -> direction round-trips,
//           (b) all six faces are reachable,
//           (c) every texel centre maps back to its own texel,
//           (d) stepping off a face edge lands on the geometrically adjacent
//               face, close by, and the neighbour relation is symmetric,
//           plus bilinear sampling continuity across seams.
//           (e) C# agrees with the HLSL version: see CubeSphereGpuTests.
// =============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class CubeSphereTests
    {
        private static Vector3 RandomDirection(System.Random random)
        {
            // Normalised Gaussian triple: uniform on the sphere.
            while (true)
            {
                var v = new Vector3(Gaussian(random), Gaussian(random), Gaussian(random));
                if (v.sqrMagnitude > 1e-6f)
                {
                    return v.normalized;
                }
            }
        }

        private static float Gaussian(System.Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        [Test]
        public void CubeSphere_RoundTrip_IsStable()
        {
            var random = new System.Random(12345);

            for (int i = 0; i < 100_000; i++)
            {
                Vector3 direction = RandomDirection(random);

                CubeSphere.DirToFaceUV(direction, out int face, out float u, out float v);
                Vector3 back = CubeSphere.FaceUVToDir(face, u, v);

                Assert.Less(Vector3.Distance(direction, back), 1e-5f, $"Direction {direction} came back as {back} (face {face}, u {u}, v {v}).");
                Assert.That(u, Is.InRange(-1e-6f, 1f + 1e-6f));
                Assert.That(v, Is.InRange(-1e-6f, 1f + 1e-6f));
            }
        }

        [Test]
        public void CubeSphere_AllSixFaces_AreReachable()
        {
            var reached = new HashSet<int>();
            var random = new System.Random(7);

            for (int i = 0; i < 1000; i++)
            {
                reached.Add(CubeSphere.FaceOf(RandomDirection(random)));
            }

            Assert.AreEqual(6, reached.Count);

            // And each face's own normal lands on that face's centre.
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                CubeSphere.DirToFaceUV(CubeSphere.FaceNormal(face), out int found, out float u, out float v);
                Assert.AreEqual(face, found);
                Assert.AreEqual(0.5f, u, 1e-6f);
                Assert.AreEqual(0.5f, v, 1e-6f);
            }
        }

        [Test]
        public void CubeSphere_FaceFrames_AreRightHanded()
        {
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                Vector3 normal = CubeSphere.FaceNormal(face);
                Vector3 right = CubeSphere.FaceRight(face);
                Vector3 up = CubeSphere.FaceUp(face);

                Assert.Less(Vector3.Distance(normal, Vector3.Cross(right, up)), 1e-6f, $"Face {face}: right x up must equal the outward normal.");
                Assert.AreEqual(0f, Vector3.Dot(normal, right), 1e-6f);
                Assert.AreEqual(0f, Vector3.Dot(normal, up), 1e-6f);
            }
        }

        [TestCase(8)]
        [TestCase(64)]
        [TestCase(256)]
        public void CubeSphere_TexelCentre_MapsBackToOwnTexel(int n)
        {
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        Vector3 centre = CubeSphere.TexelCentreDir(face, x, y, n);
                        Texel texel = CubeSphere.DirToTexel(centre, n);

                        if (texel.Face != face || texel.X != x || texel.Y != y)
                        {
                            Assert.Fail($"Centre of face {face} ({x}, {y}) mapped to {texel} at N = {n}.");
                        }
                    }
                }
            }
        }

        [Test]
        public void CubeSphere_NeighbourOffEdge_LandsOnGeometricallyAdjacentFace()
        {
            const int n = 32;

            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int i = 1; i < n - 1; i++)   // skip corner texels, whose diagonal neighbours touch a third face
                {
                    // +s edge borders the face lying in the +right direction, and so on.
                    AssertAdjacent(face, n - 1, i, +1, 0, CubeSphere.FaceRight(face), n);
                    AssertAdjacent(face, 0, i, -1, 0, -CubeSphere.FaceRight(face), n);
                    AssertAdjacent(face, i, n - 1, 0, +1, CubeSphere.FaceUp(face), n);
                    AssertAdjacent(face, i, 0, 0, -1, -CubeSphere.FaceUp(face), n);
                }
            }
        }

        private static void AssertAdjacent(int face, int x, int y, int dx, int dy, Vector3 towardEdge, int n)
        {
            int expectedFace = CubeSphere.FaceOf(towardEdge);
            Texel neighbour = CubeSphere.NeighbourTexel(face, x, y, dx, dy, n);

            Assert.AreEqual(expectedFace, neighbour.Face, $"Stepping ({dx},{dy}) off face {face} ({x},{y}) should reach face {expectedFace}, got {neighbour}.");
        }

        [Test]
        public void CubeSphere_Neighbours_AreCloseAndDistinct_IncludingCornersAndSeams()
        {
            const int n = 32;

            // Texel centres are about (pi/2)/n radians apart along a face axis; allow generous room for the diagonal.
            float nominal = Mathf.PI * 0.5f / n;
            float maxAngle = 2.4f * nominal;

            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        Vector3 here = CubeSphere.TexelCentreDir(face, x, y, n);

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0)
                                {
                                    continue;
                                }

                                Texel neighbour = CubeSphere.NeighbourTexel(face, x, y, dx, dy, n);
                                Vector3 there = CubeSphere.TexelCentreDir(neighbour.Face, neighbour.X, neighbour.Y, n);
                                float angle = Vector3.Angle(here, there) * Mathf.Deg2Rad;

                                Assert.Less(angle, maxAngle, $"Neighbour ({dx},{dy}) of face {face} ({x},{y}) is {neighbour}, {angle / nominal:0.0} texels away.");
                                Assert.IsFalse(neighbour.SameAs(new Texel(face, x, y)), $"Neighbour ({dx},{dy}) of face {face} ({x},{y}) is the texel itself.");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void CubeSphere_AxisNeighbours_AreSymmetric_AwayFromCorners()
        {
            // If B is A's neighbour, A must be among B's four axis neighbours. Edge-adjacent texels
            // at the very corners are excluded: three faces meet there and no 4-neighbour rule is symmetric.
            const int n = 32;
            var offsets = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            int checkedPairs = 0;
            int asymmetric = 0;

            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        bool onEdge = x == 0 || x == n - 1 || y == 0 || y == n - 1;
                        bool nearCorner = (x < 2 || x >= n - 2) && (y < 2 || y >= n - 2);
                        if (!onEdge || nearCorner)
                        {
                            continue;   // interior steps never cross a seam; corners are excluded (see above)
                        }

                        var here = new Texel(face, x, y);
                        foreach ((int dx, int dy) in offsets)
                        {
                            Texel there = CubeSphere.NeighbourTexel(face, x, y, dx, dy, n);
                            bool found = false;
                            foreach ((int bx, int by) in offsets)
                            {
                                if (CubeSphere.NeighbourTexel(there.Face, there.X, there.Y, bx, by, n).SameAs(here))
                                {
                                    found = true;
                                    break;
                                }
                            }

                            checkedPairs++;
                            if (!found)
                            {
                                asymmetric++;
                            }
                        }
                    }
                }
            }

            Assert.Greater(checkedPairs, 0);
            Assert.AreEqual(0, asymmetric, $"{asymmetric} of {checkedPairs} neighbour pairs near edges were not mutual.");
        }

        [Test]
        public void CubeSphere_BilinearTaps_WeightsAreWithinRange_AndCentreHasFullWeight()
        {
            const int n = 64;
            var random = new System.Random(99);

            for (int i = 0; i < 20_000; i++)
            {
                BilinearTaps taps = CubeSphere.BilinearAt(RandomDirection(random), n);
                Assert.That(taps.Wx, Is.InRange(-1e-5f, 1f + 1e-5f));
                Assert.That(taps.Wy, Is.InRange(-1e-5f, 1f + 1e-5f));
            }

            // Sampling exactly at a texel centre must give that texel all the weight.
            Vector3 centre = CubeSphere.TexelCentreDir(3, 10, 20, n);
            BilinearTaps atCentre = CubeSphere.BilinearAt(centre, n);
            Assert.IsTrue(atCentre.T00.SameAs(new Texel(3, 10, 20)));
            Assert.AreEqual(0f, atCentre.Wx, 1e-3f);
            Assert.AreEqual(0f, atCentre.Wy, 1e-3f);
        }

        [Test]
        public void CubeSphere_BilinearSample_ReproducesASmoothFieldEverywhere_IncludingSeams()
        {
            // Store a smooth function of direction at every texel centre, sample it back at random
            // directions, and compare with the exact function. A broken seam shows up as a large error.
            const int n = 64;
            var field = new float[CubeSphere.FaceCount * n * n];

            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        field[(face * n + y) * n + x] = Smooth(CubeSphere.TexelCentreDir(face, x, y, n));
                    }
                }
            }

            var random = new System.Random(2024);
            float worstInterior = 0f;
            float worstNearSeam = 0f;

            for (int i = 0; i < 50_000; i++)
            {
                Vector3 direction = RandomDirection(random);
                BilinearTaps taps = CubeSphere.BilinearAt(direction, n);

                float Value(Texel t) => field[(t.Face * n + t.Y) * n + t.X];
                float sampled = Mathf.Lerp(
                    Mathf.Lerp(Value(taps.T00), Value(taps.T10), taps.Wx),
                    Mathf.Lerp(Value(taps.T01), Value(taps.T11), taps.Wx),
                    taps.Wy);

                float error = Mathf.Abs(sampled - Smooth(direction));

                CubeSphere.DirToFaceUV(direction, out _, out float u, out float v);
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * n;   // distance to a face edge, in texels
                if (edge > 2f)
                {
                    worstInterior = Mathf.Max(worstInterior, error);
                }
                else
                {
                    worstNearSeam = Mathf.Max(worstNearSeam, error);
                }
            }

            Assert.Less(worstInterior, 2e-3f, "Interior bilinear error is too large.");

            // Near a seam the taps come from the neighbouring face's texel centres, up to about half a
            // texel off the exact position (see the CubeSphere header), so a larger error is expected,
            // but it must stay small: a broken seam would give errors near the full range of the function.
            Assert.Less(worstNearSeam, 3e-2f, "Bilinear sampling is not continuous across a face seam.");
        }

        private static float Smooth(Vector3 d)
        {
            return 0.5f + 0.25f * d.x + 0.2f * d.y - 0.15f * d.z + 0.1f * Mathf.Sin(3f * d.x + 2f * d.z);
        }
    }
}
