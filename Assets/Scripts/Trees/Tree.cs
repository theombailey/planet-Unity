// =============================================================================
// Tree
// -----------------------------------------------------------------------------
// Purpose : The state of ONE tree: where it stands, its branch skeleton, its
//           mesh, and its condition (health, leaves, age). A plain data class;
//           TreeManager owns the trees and TreeLife holds the rules.
// Idea    : A tree has its own little coordinate frame. The ROOT is the origin
//           and +y points straight up from the ground. The skeleton and mesh
//           live in that frame. To put the tree on the planet we need two things:
//             Direction   where the root is on the sphere (a unit vector in the
//                         planet's frame; also the local up direction there)
//             Frame       a rotation taking tree-up to Direction, with a random spin
//                         about it so neighbours do not all face the same way
//           LocalToPlanet then places the tree: translate to the root point
//           (Direction * rootRadius, rootRadius = the terrain's radius there),
//           rotate by Frame. Drawing multiplies by the planet's own transform.
//
// Growth  : GrowStep() adds a layer of branches. A step first adds a few attractors
//           (so a tree on rich ground gets many, and a poor one few), then runs one
//           iteration of the space colonization algorithm. The tree is "mature" when
//           it has had all the attractors it will get and nothing is left to reach.
//
// Crown   : an ellipsoid above the trunk. Its size scales with `Richness` (a slow
//           average of how good the soil and water have been), so the same species
//           grows into a fuller crown on good ground and a scraggly one on bad.
// =============================================================================

using UnityEngine;

namespace Terrarium.Trees
{
    /// <summary>One tree's position, skeleton, mesh and condition.</summary>
    public sealed class Tree
    {
        private const int IdleStepsToMature = 3;

        private readonly SimConfig config;
        private int attractorsAdded;
        private int idleSteps;

        /// <summary>Creates a tree standing at a point on the planet.</summary>
        /// <param name="slot">Index in the manager's slot arrays (also its leaf and state slot).</param>
        /// <param name="seed">Seed for the tree's shape.</param>
        /// <param name="direction">Root position as a unit vector in the planet's frame.</param>
        /// <param name="rootRadius">Distance of the root from the planet centre, in planet units.</param>
        /// <param name="matureHeight">Height at maturity, in planet units.</param>
        /// <param name="settings">Space colonization settings for this tree.</param>
        /// <param name="simConfig">Tuning parameters.</param>
        /// <param name="rng">Random source for the tree's spin and lifespan.</param>
        public Tree(int slot, int seed, Vector3 direction, float rootRadius, float matureHeight, SpaceColonization.Settings settings, SimConfig simConfig, ref Rng rng)
        {
            config = simConfig;
            Slot = slot;
            Direction = direction.normalized;
            RootRadius = rootRadius;
            MatureHeight = matureHeight;
            Lifespan = rng.Range(simConfig.treeLifespanMin, simConfig.treeLifespanMax);

            Quaternion toUp = Quaternion.FromToRotation(Vector3.up, Direction);
            Frame = toUp * Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up);

            Colony = new SpaceColonization(seed, settings);
            Colony.AddTrunk(matureHeight * simConfig.treeTrunkFraction);

            Mesh = new Mesh { name = "Tree" };
            LeafCount = 0;
            MeshDirty = true;
        }

        /// <summary>Index of this tree in the manager's slot arrays.</summary>
        public int Slot { get; }

        /// <summary>Root position, a unit vector in the planet's frame (also the local up direction).</summary>
        public Vector3 Direction { get; }

        /// <summary>Distance of the root from the planet centre, in planet units.</summary>
        public float RootRadius { get; }

        /// <summary>Rotation from tree space (+y up) to the planet's frame.</summary>
        public Quaternion Frame { get; }

        /// <summary>Height at maturity, in planet units.</summary>
        public float MatureHeight { get; }

        /// <summary>The branch skeleton.</summary>
        public SpaceColonization Colony { get; }

        /// <summary>The branch mesh (tree space).</summary>
        public Mesh Mesh { get; }

        /// <summary>Age in sim seconds.</summary>
        public float Age { get; set; }

        /// <summary>Age at which the tree dies of old age, sim seconds.</summary>
        public float Lifespan { get; }

        /// <summary>Health 0..1; the tree dies at 0.</summary>
        public float Health { get; set; } = 1f;

        /// <summary>Fraction of full leaf cover, 0..1.</summary>
        public float LeafAmount { get; set; }

        /// <summary>Autumn tint, 0..1.</summary>
        public float Autumn { get; set; }

        /// <summary>Slow average of how good the soil and water are (0..1); sets crown size and attractor budget.</summary>
        public float Richness { get; set; } = 0.5f;

        /// <summary>The growth rate last computed from the tree's surroundings, 0..1.</summary>
        public float GrowthRate { get; set; }

        /// <summary>Growth accumulated toward the next growth step.</summary>
        public float GrowthProgress { get; set; }

        /// <summary>Growth steps earned but not yet run (the manager runs a few per frame).</summary>
        public int PendingSteps { get; set; }

        /// <summary>The seed clock (see TreeLife.SeedsDue).</summary>
        public float SeedClock;

        /// <summary>The canopy strength this tree contributed at the last GPU pass (its own shade at its root).</summary>
        public float LastCanopyStrength { get; set; }

        /// <summary>True once the tree has stopped growing branches.</summary>
        public bool Mature { get; private set; }

        /// <summary>True when the tree has died and is standing grey until it collapses.</summary>
        public bool Dead { get; set; }

        /// <summary>Sim seconds since death.</summary>
        public float DeadTime { get; set; }

        /// <summary>True once the tree has been taken out of the world (its slot is free).</summary>
        public bool Removed { get; set; }

        /// <summary>True when the mesh and leaves are out of date.</summary>
        public bool MeshDirty { get; set; }

        /// <summary>True while the tree is waiting in the manager's rebuild queue.</summary>
        public bool Queued { get; set; }

        /// <summary>Number of leaves written to the leaf buffer.</summary>
        public int LeafCount { get; set; }

        /// <summary>How big the tree is compared with its mature size, 0..1 (used for uptake and shade).</summary>
        public float SizeFraction => Mature ? 1f : Mathf.Clamp01(Colony.NodeCount / 150f);

        /// <summary>How decayed a dead tree is, 0 (just died) to 1 (about to collapse).</summary>
        public float Decay => Dead ? Mathf.Clamp01(DeadTime / config.treeDeadSeconds) : 0f;

        /// <summary>Crown half-width in planet units now (it grows with the tree and with richness).</summary>
        public float CrownRadius => Mathf.Max(0.004f, config.treeCrownWidthFraction * MatureHeight * (0.55f + 0.45f * Richness) * Mathf.Max(0.25f, SizeFraction));

        /// <summary>Tree space to planet frame.</summary>
        public Matrix4x4 LocalToPlanet => Matrix4x4.TRS(Direction * RootRadius, Frame, Vector3.one);

        /// <summary>
        /// Adds a layer of branches: first more attractors (as many as the tree's richness allows), then one iteration
        /// of the algorithm. Returns true if the tree grew.
        /// </summary>
        /// <param name="toLightTreeSpace">Direction toward the lamp in tree space, already weighted by how high the lamp is (zero at night).</param>
        public bool GrowStep(Vector3 toLightTreeSpace)
        {
            if (Mature || Dead)
            {
                return false;
            }

            float scale = 0.55f + 0.45f * Richness;
            int budget = Mathf.RoundToInt(config.treeAttractorsMax * (0.15f + 0.85f * Richness));

            if (attractorsAdded < budget)
            {
                int count = Mathf.Min(budget - attractorsAdded, Mathf.Max(3, Mathf.RoundToInt(config.treeAttractorsPerStep * (0.3f + 0.7f * Richness))));
                float height = MatureHeight;
                float radiusX = config.treeCrownWidthFraction * height * scale;
                float radiusY = config.treeCrownHeightFraction * height * scale;
                // The crown shifts toward the light (toLight is zero at night, so only daytime light counts). The
                // branch-bending force alone cannot do this: every attractor is eventually reached, so a symmetric
                // cloud always fills the same shape. Moving the cloud is what makes a tree lean.
                Vector3 lean = toLightTreeSpace * (radiusX * 0.5f * config.treePhototropism);
                var centre = new Vector3(lean.x, height * config.treeTrunkFraction + radiusY * 0.85f, lean.z);
                Colony.AddAttractors(centre, new Vector3(radiusX, radiusY, radiusX), count);
                attractorsAdded += count;
            }

            int added = Colony.Grow(Vector3.up, toLightTreeSpace);
            if (added > 0)
            {
                idleSteps = 0;
                MeshDirty = true;
                return true;
            }

            idleSteps++;
            if (idleSteps >= IdleStepsToMature && (attractorsAdded >= budget || Colony.IsFull))
            {
                Mature = true;
                MeshDirty = true;
            }

            return false;
        }

        /// <summary>Releases the mesh.</summary>
        public void Destroy()
        {
            Removed = true;
            if (Mesh != null)
            {
                Object.Destroy(Mesh);
            }
        }
    }
}
