// =============================================================================
// SpaceColonization
// -----------------------------------------------------------------------------
// Purpose : Grows the branching skeleton of ONE tree with the space colonization
//           algorithm (Runions, Lane and Prusinkiewicz, 2007). Pure maths and
//           data: no Unity scene, no GPU. It works in the tree's own frame:
//           the root is the origin and +y is "up" (away from the planet).
//
// Idea    : A tree is a graph of NODES (points joined by branch segments). Fill
//           the volume where the crown should be with random ATTRACTOR points,
//           "bits of free space the tree wants to grow into". Then repeat:
//             1. Every attractor looks for the nearest node within the
//                INFLUENCE radius. That node is the one that will reach for it.
//             2. Every node that was chosen by at least one attractor grows one
//                new segment toward the average direction of its attractors.
//             3. Attractors that now have a node within the KILL distance are
//                used up and removed.
//           The tree stops when no attractor can be reached. Branches appear
//           where attractors are, so the same rules give a different tree for
//           a different attractor cloud: no tree is scripted.
//
//           On top of the pure rule, each new segment's direction is bent by
//           three small forces, which is what makes the trees respond to the world:
//             GRAVITROPISM  a pull toward local up (branches reach for the sky)
//             PHOTOTROPISM  a pull toward the lamp (trees lean toward the light)
//             WOBBLE        a little randomness (so branches are not ruler-straight)
//
// Equations:
//   direction to attractor a from node n:   d_a = (a - n) / |a - n|
//   new segment from node n:                dir = normalize( mean(d_a) + g up + p light + w random )
//                                           child = n + D * dir              (D = segment length)
//   PIPE MODEL radii (da Vinci's rule: the cross-sections of the branches above a point add up
//   to the cross-section of the branch below it):
//       r_parent^e = SUM over children of r_child^e        (e = pipe exponent, about 2.5)
//       tips have r = r_tip.   Computed from the tips down: a child's index is always greater than
//       its parent's, so one backwards pass over the nodes does it.
//
// Speed   : "nearest node within the influence radius" would be a loop over every node for every
//           attractor. A uniform 3D grid (cell size = influence radius) means only the 27 cells around
//           an attractor are searched. Nodes never move, so the grid only ever grows. The grid is a hash
//           table of linked lists in plain arrays (no allocation per query); two points whose cells hash
//           to the same slot are simply both tested, which costs a little time but never gives a wrong answer.
//
// Determinism: all randomness comes from one Rng seeded in the constructor, so the same seed and the
//           same calls always give the same tree.
//
// Parameters (Settings): see the field comments below.
// =============================================================================

using System;
using UnityEngine;

namespace Terrarium.Trees
{
    /// <summary>Grows one tree's branch skeleton with the space colonization algorithm.</summary>
    public sealed class SpaceColonization
    {
        private const int GridSlots = 4096;

        /// <summary>The tunable numbers of the algorithm.</summary>
        public struct Settings
        {
            /// <summary>Length of one branch segment, in tree units.</summary>
            public float SegmentLength;

            /// <summary>How far an attractor reaches to find a node that will grow toward it.</summary>
            public float InfluenceRadius;

            /// <summary>An attractor this close to any node is used up.</summary>
            public float KillRadius;

            /// <summary>Strength of the pull toward local up, relative to the pull toward attractors (which counts 1).</summary>
            public float Gravitropism;

            /// <summary>Strength of the pull toward the lamp.</summary>
            public float Phototropism;

            /// <summary>Strength of the random wobble.</summary>
            public float Wobble;

            /// <summary>The most nodes the tree may have.</summary>
            public int MaxNodes;

            /// <summary>Radius of a twig tip, in tree units.</summary>
            public float TipRadius;

            /// <summary>The pipe-model exponent (about 2.5).</summary>
            public float PipeExponent;

            /// <summary>Sim seconds a new branch segment takes to grow to full length (0 = it appears at once). Only used for drawing.</summary>
            public float SegmentGrowSeconds;

            /// <summary>Sim seconds the trunk takes to grow when the tree starts (0 = at once). Only used for drawing.</summary>
            public float TrunkGrowSeconds;
        }

        private readonly Settings settings;
        private Rng rng;

        private readonly Vector3[] positions;
        private readonly int[] parents;
        private readonly int[] childCounts;
        private readonly float[] radii;
        private readonly float[] births;
        private readonly float[] growSeconds;
        private float nodeGrowSeconds;

        private readonly int[] cellHead = new int[GridSlots];
        private readonly int[] nodeNext;

        private Vector3[] attractors = new Vector3[256];
        private Vector3[] pull;
        private int[] pullCounts;

        /// <summary>Creates an empty tree. Call <see cref="AddRoot"/> or <see cref="AddTrunk"/> next.</summary>
        /// <param name="seed">Seed for all of this tree's randomness.</param>
        /// <param name="treeSettings">Algorithm parameters.</param>
        public SpaceColonization(int seed, Settings treeSettings)
        {
            settings = treeSettings;
            nodeGrowSeconds = treeSettings.SegmentGrowSeconds;
            rng = new Rng(seed);

            positions = new Vector3[settings.MaxNodes];
            parents = new int[settings.MaxNodes];
            childCounts = new int[settings.MaxNodes];
            radii = new float[settings.MaxNodes];
            births = new float[settings.MaxNodes];
            growSeconds = new float[settings.MaxNodes];
            nodeNext = new int[settings.MaxNodes];
            pull = new Vector3[settings.MaxNodes];
            pullCounts = new int[settings.MaxNodes];

            for (int i = 0; i < GridSlots; i++)
            {
                cellHead[i] = -1;
            }
        }

        /// <summary>Number of nodes so far.</summary>
        public int NodeCount { get; private set; }

        /// <summary>Number of attractors still waiting to be reached.</summary>
        public int AttractorCount { get; private set; }

        /// <summary>True once the node limit is reached.</summary>
        public bool IsFull => NodeCount >= settings.MaxNodes;

        /// <summary>Position of a node, in tree units (root at the origin, +y up).</summary>
        public Vector3 PositionOf(int node) => positions[node];

        /// <summary>Index of a node's parent, or -1 for the root.</summary>
        public int ParentOf(int node) => parents[node];

        /// <summary>How many children a node has.</summary>
        public int ChildCountOf(int node) => childCounts[node];

        /// <summary>Branch radius at a node (valid after <see cref="ComputeRadii"/>).</summary>
        public float RadiusOf(int node) => radii[node];

        /// <summary>Sim time at which a node was added (set from <see cref="CurrentTime"/>); used to animate growth.</summary>
        public float BirthOf(int node) => births[node];

        /// <summary>Sim seconds the segment ending at a node takes to grow out to full length.</summary>
        public float GrowSecondsOf(int node) => growSeconds[node];

        /// <summary>The sim time stamped on nodes as they are added. The tree's owner sets it before each growth step.</summary>
        public float CurrentTime { get; set; }

        /// <summary>Position of an attractor still waiting (for tests and debugging).</summary>
        public Vector3 AttractorAt(int index) => attractors[index];

        /// <summary>Adds the root node at the origin.</summary>
        public void AddRoot()
        {
            AddNode(Vector3.zero, -1);
        }

        /// <summary>
        /// Adds the root and a straight trunk of the given height, in whole segments. A tree grows its crown
        /// from the top of the trunk.
        /// </summary>
        public void AddTrunk(float height)
        {
            nodeGrowSeconds = settings.TrunkGrowSeconds;
            AddRoot();
            int segments = Mathf.Max(1, Mathf.RoundToInt(height / settings.SegmentLength));
            for (int i = 1; i <= segments && !IsFull; i++)
            {
                AddNode(new Vector3(0f, i * settings.SegmentLength, 0f), NodeCount - 1);
            }

            nodeGrowSeconds = settings.SegmentGrowSeconds;
        }

        /// <summary>
        /// Scatters attractors uniformly inside an ellipsoid. Called repeatedly as the tree thrives, so the crown
        /// fills gradually and a tree on rich ground gets more of them.
        /// </summary>
        /// <param name="centre">Centre of the ellipsoid, in tree units.</param>
        /// <param name="radii">Half-widths of the ellipsoid along x, y and z.</param>
        /// <param name="count">How many to add.</param>
        public void AddAttractors(Vector3 centre, Vector3 radii, int count)
        {
            for (int added = 0; added < count; added++)
            {
                // A uniform point in the unit ball by rejection: a point in the cube is kept only if it is inside the ball.
                Vector3 p;
                do
                {
                    p = new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                }
                while (p.sqrMagnitude > 1f);

                if (AttractorCount == attractors.Length)
                {
                    Array.Resize(ref attractors, attractors.Length * 2);
                }

                attractors[AttractorCount++] = centre + Vector3.Scale(p, radii);
            }
        }

        /// <summary>
        /// One iteration of the algorithm. Returns the number of new nodes (0 means nothing could be reached:
        /// the crown is finished for now, or more attractors are needed).
        /// </summary>
        /// <param name="up">Unit vector for local up (gravitropism).</param>
        /// <param name="toLight">Unit vector toward the lamp in tree units (phototropism).</param>
        public int Grow(Vector3 up, Vector3 toLight)
        {
            if (NodeCount == 0 || AttractorCount == 0)
            {
                return 0;
            }

            int existing = NodeCount;
            Array.Clear(pull, 0, existing);
            Array.Clear(pullCounts, 0, existing);

            // 1. Each attractor chooses the nearest node within the influence radius.
            float influenceSquared = settings.InfluenceRadius * settings.InfluenceRadius;
            for (int a = 0; a < AttractorCount; a++)
            {
                int nearest = NearestNode(attractors[a], influenceSquared);
                if (nearest >= 0)
                {
                    Vector3 toward = attractors[a] - positions[nearest];
                    pull[nearest] += toward / Mathf.Max(toward.magnitude, 1e-6f);
                    pullCounts[nearest]++;
                }
            }

            // 2. Each chosen node grows one segment toward the mean pull, bent by the tropisms.
            int added = 0;
            for (int n = 0; n < existing && !IsFull; n++)
            {
                if (pullCounts[n] == 0)
                {
                    continue;
                }

                Vector3 direction = pull[n] / pullCounts[n];
                direction += up * settings.Gravitropism + toLight * settings.Phototropism;
                direction += new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * settings.Wobble;

                if (direction.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                Vector3 child = positions[n] + direction.normalized * settings.SegmentLength;

                // Do not stack a new node on top of one that is already there.
                if (NearestNode(child, 0.09f * settings.SegmentLength * settings.SegmentLength * 4f) >= 0)
                {
                    continue;
                }

                AddNode(child, n);
                added++;
            }

            // 3. Remove the attractors that have been reached.
            float killSquared = settings.KillRadius * settings.KillRadius;
            for (int a = AttractorCount - 1; a >= 0; a--)
            {
                if (NearestNode(attractors[a], killSquared) >= 0)
                {
                    attractors[a] = attractors[--AttractorCount];
                }
            }

            return added;
        }

        /// <summary>
        /// Works out every branch radius with the pipe model: tips get the tip radius, and each parent's radius
        /// is chosen so its cross-section equals the sum of its children's.
        /// </summary>
        public void ComputeRadii()
        {
            float exponent = settings.PipeExponent;
            Array.Clear(pull, 0, NodeCount);   // reused as scratch: x holds the running sum of child radius^e

            for (int i = NodeCount - 1; i >= 0; i--)
            {
                radii[i] = childCounts[i] == 0 ? settings.TipRadius : Mathf.Pow(pull[i].x, 1f / exponent);
                radii[i] = Mathf.Max(radii[i], settings.TipRadius);

                if (parents[i] >= 0)
                {
                    pull[parents[i]].x += Mathf.Pow(radii[i], exponent);
                }
            }
        }

        // ------------------------------------------------------------------ internals

        private void AddNode(Vector3 position, int parent)
        {
            int index = NodeCount++;
            positions[index] = position;
            parents[index] = parent;
            births[index] = CurrentTime;
            growSeconds[index] = nodeGrowSeconds;
            childCounts[index] = 0;
            if (parent >= 0)
            {
                childCounts[parent]++;
            }

            int slot = CellSlot(CellOf(position));
            nodeNext[index] = cellHead[slot];
            cellHead[slot] = index;
        }

        // The nearest node within sqrt(maxDistanceSquared) of a point, or -1. Only the 27 cells around the point are searched.
        private int NearestNode(Vector3 point, float maxDistanceSquared)
        {
            Vector3Int centre = CellOf(point);
            int best = -1;
            float bestDistance = maxDistanceSquared;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int slot = CellSlot(new Vector3Int(centre.x + dx, centre.y + dy, centre.z + dz));
                        for (int node = cellHead[slot]; node >= 0; node = nodeNext[node])
                        {
                            float distance = (positions[node] - point).sqrMagnitude;
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                best = node;
                            }
                        }
                    }
                }
            }

            return best;
        }

        private Vector3Int CellOf(Vector3 point)
        {
            float inverse = 1f / settings.InfluenceRadius;
            return new Vector3Int(Mathf.FloorToInt(point.x * inverse), Mathf.FloorToInt(point.y * inverse), Mathf.FloorToInt(point.z * inverse));
        }

        private static int CellSlot(Vector3Int cell)
        {
            // Three large primes mix the coordinates; the result wraps into the table.
            unchecked
            {
                uint hash = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663) ^ (uint)(cell.z * 83492791);
                return (int)(hash % GridSlots);
            }
        }
    }
}
