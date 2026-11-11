// =============================================================================
// WorkshopVisibility
// -----------------------------------------------------------------------------
// Purpose : On the planet's surface you should see only the planet and its sun. This
//           hides the whole workshop (desk, shelf, console, lamp, dials, tools) while
//           the camera is down on the ground, and shows it again as you rise away.
//           Exceptions: the glowing bulb (it is the sun) stays, and so does a tool
//           you are holding, so you can still use tools from the surface.
// Idea    : It follows the same "how much of the planet's sky am I in" number the sky
//           uses (AtmosphereController.PlanetSkyWeight), so the desk is gone exactly
//           when the blue sky is in. When hidden, the colliders are switched off too,
//           so you cannot click an invisible tool, and the shelf light is switched off.
//           It only does work when something changes (visible or not, which tool is held).
// Parameters: roots (everything that belongs to the workshop), the bulb to keep,
//             the threshold (sky weight above which the workshop is hidden).
// =============================================================================

using System.Collections.Generic;
using Terrarium.Interaction;
using Terrarium.Rendering;
using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>Hides the desk, shelf, console, lamp and tools while the camera is on the planet's surface.</summary>
    public sealed class WorkshopVisibility : MonoBehaviour
    {
        [Tooltip("Gives how much of the planet's sky the camera is in.")]
        [SerializeField] private AtmosphereController atmosphere;

        [Tooltip("For the tool in hand, which stays visible.")]
        [SerializeField] private ToolController tools;

        [Tooltip("Everything that belongs to the workshop.")]
        [SerializeField] private Transform[] roots;

        [Tooltip("Parts that stay visible always (the glowing bulb: it is the sun seen from the surface).")]
        [SerializeField] private Transform[] keepVisible;

        [Tooltip("Sky weight above which the workshop is hidden.")]
        [Range(0f, 1f)]
        [SerializeField] private float hideAbove = 0.5f;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<Collider> colliders = new List<Collider>();
        private readonly List<Light> lights = new List<Light>();
        private ToolProp[] toolProps;
        private bool shown = true;
        private ToolProp lastHeld;
        private bool applied;

        /// <summary>True while the workshop is visible.</summary>
        public bool WorkshopShown => shown;

        private void Awake()
        {
            var keep = new HashSet<Transform>();
            if (keepVisible != null)
            {
                foreach (Transform t in keepVisible)
                {
                    if (t != null)
                    {
                        keep.Add(t);
                    }
                }
            }

            foreach (Transform root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!IsUnder(r.transform, keep))
                    {
                        renderers.Add(r);
                    }
                }

                colliders.AddRange(root.GetComponentsInChildren<Collider>(true));
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (!IsUnder(light.transform, keep))
                    {
                        lights.Add(light);
                    }
                }
            }

            toolProps = FindObjectsByType<ToolProp>(FindObjectsSortMode.None);
        }

        private static bool IsUnder(Transform t, HashSet<Transform> parents)
        {
            for (Transform walk = t; walk != null; walk = walk.parent)
            {
                if (parents.Contains(walk))
                {
                    return true;
                }
            }

            return false;
        }

        private void LateUpdate()
        {
            if (atmosphere == null)
            {
                return;
            }

            bool show = atmosphere.PlanetSkyWeight <= hideAbove;
            ToolProp held = tools != null ? tools.HeldTool : null;
            if (applied && show == shown && held == lastHeld)
            {
                return;
            }

            applied = true;
            shown = show;
            lastHeld = held;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = show;
                }
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = show;
                }
            }

            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].enabled = show;
                }
            }

            // The tool in hand stays visible wherever you are.
            if (held != null)
            {
                foreach (Renderer r in held.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                }
            }
        }
    }
}
