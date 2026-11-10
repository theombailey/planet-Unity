// =============================================================================
// SurfacePicker
// -----------------------------------------------------------------------------
// Purpose : Finds the point on the planet under the mouse and converts it to a
//           direction in the planet's own frame, which is what the fields and
//           brushes use.
// Idea    : A mouse click is a ray (origin + t * direction). The planet is a
//           sphere, so the hit has a closed form, no physics colliders needed.
//
// Equations (ray o + t d with |d| = 1, sphere centre c, radius R; w = o - c):
//     |w + t d|^2 = R^2   ->   t^2 + 2 (w.d) t + (w.w - R^2) = 0
//     b = w.d,  disc = b^2 - (w.w - R^2)          (no hit when disc < 0)
//     t = -b - sqrt(disc)                          (the near hit)
//     if that is behind the origin (the origin is inside), use -b + sqrt(disc)
//     hit point  = o + t d
//     local dir  = inverse(planetRotation) * normalize(hit - c)
//
// Why local: the planet spins and tilts, but fields are stored in its own
//     frame. Converting the world-space hit with the inverse rotation gives the
//     direction that is the same texel whatever the planet's current spin.
// Phase 6 extends this to account for terrain height.
// =============================================================================

using Terrarium.Sphere;
using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>Ray-sphere picking against the planet.</summary>
    public static class SurfacePicker
    {
        /// <summary>
        /// Intersects a ray with a sphere. Returns false on a miss or when the sphere is entirely behind the ray.
        /// </summary>
        /// <param name="distance">Distance along the ray to the first hit in front of the origin.</param>
        public static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 centre, float radius, out float distance)
        {
            distance = 0f;
            Vector3 unit = direction.normalized;
            Vector3 offset = origin - centre;
            float b = Vector3.Dot(offset, unit);
            float discriminant = b * b - (Vector3.Dot(offset, offset) - radius * radius);

            if (discriminant < 0f)
            {
                return false;
            }

            float root = Mathf.Sqrt(discriminant);
            float near = -b - root;
            float far = -b + root;
            distance = near >= 0f ? near : far;
            return distance >= 0f;
        }

        /// <summary>
        /// Picks the planet with a world-space ray, taking the terrain relief into account, and returns the hit as a
        /// unit direction in the planet's local frame and its distance along the ray.
        /// </summary>
        /// <remarks>
        /// The ground is not a perfect sphere: land stands up to <c>heightScale</c> (a fraction of the radius) above
        /// the sea. A plain sphere test would pick a point that is not what the player sees on a mountain slope.
        /// So: hit the outer sphere that bounds the highest peak; read the ground's real radius at that spot
        /// r = radius (1 + heightScale max(height, 0)); intersect the ray with a sphere of that radius; repeat. Each pass
        /// lands closer to the true surface and a few passes are plenty for gentle slopes.
        /// </remarks>
        /// <param name="ray">World-space ray, e.g. from the camera through the mouse.</param>
        /// <param name="planet">The planet's transform.</param>
        /// <param name="radius">Planet (sea-level) radius in world units.</param>
        /// <param name="heightScale">Height of the highest peak above sea level, as a fraction of the radius.</param>
        /// <param name="heights">The terrain heights; null means a perfectly round planet.</param>
        /// <param name="localDirection">The hit as a unit vector in the planet's local space.</param>
        /// <param name="distance">Distance along the ray to the hit.</param>
        public static bool TryPick(Ray ray, Transform planet, float radius, float heightScale, ITerrainHeights heights, out Vector3 localDirection, out float distance)
        {
            localDirection = Vector3.up;
            distance = 0f;

            float surfaceRadius = radius * (1f + (heights != null ? heightScale : 0f));
            if (!RaySphere(ray.origin, ray.direction, planet.position, surfaceRadius, out distance))
            {
                return false;
            }

            Quaternion inverse = Quaternion.Inverse(planet.rotation);
            Vector3 unit = ray.direction.normalized;

            for (int pass = 0; pass < (heights != null ? 4 : 1); pass++)
            {
                Vector3 hit = ray.origin + unit * distance;
                localDirection = (inverse * (hit - planet.position)).normalized;

                if (heights == null)
                {
                    break;
                }

                float groundRadius = radius * (1f + heightScale * Mathf.Max(heights.HeightAt(localDirection), 0f));
                if (!RaySphere(ray.origin, unit, planet.position, groundRadius, out float nextDistance))
                {
                    // The ray skims past the ground's real surface (it only clipped the bounding sphere above a lowland).
                    return false;
                }

                distance = nextDistance;
            }

            localDirection = (inverse * (ray.origin + unit * distance - planet.position)).normalized;
            return true;
        }

        /// <summary>
        /// Picks the planet with a world-space ray and returns the hit as a unit direction in the planet's local frame.
        /// </summary>
        /// <param name="ray">World-space ray, e.g. from the camera through the mouse.</param>
        /// <param name="planet">The planet's transform (position = centre, rotation = current spin and tilt).</param>
        /// <param name="radius">Planet radius in world units.</param>
        /// <param name="localDirection">The hit as a unit vector in the planet's local space.</param>
        public static bool TryPickLocalDirection(Ray ray, Transform planet, float radius, out Vector3 localDirection)
        {
            localDirection = Vector3.up;
            if (!RaySphere(ray.origin, ray.direction, planet.position, radius, out float distance))
            {
                return false;
            }

            Vector3 hit = ray.origin + ray.direction.normalized * distance;
            localDirection = (Quaternion.Inverse(planet.rotation) * (hit - planet.position)).normalized;
            return true;
        }
    }
}
