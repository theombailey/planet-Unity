// =============================================================================
// InputReader
// -----------------------------------------------------------------------------
// Purpose : The only file that talks to Unity's input API.
// Idea    : The project uses the new Input System package (the old Input class
//           is disabled in Player Settings). Every other script asks questions
//           like "was the debug key pressed?" through this class, so swapping
//           the input backend later means editing this one file.
// Note    : Stateless. Each property reads the device directly; if no keyboard
//           or mouse is connected the answer is simply false / zero.
// =============================================================================

using UnityEngine;
using UnityEngine.InputSystem;

namespace Terrarium.Interaction
{
    /// <summary>Stateless queries for the game's keys and mouse, isolating the input backend.</summary>
    public static class InputReader
    {
        /// <summary>True on the frame F1 goes down (toggle the debug panel).</summary>
        public static bool DebugPanelTogglePressed => Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;

        /// <summary>True on the frame Space goes down (pause / resume the sim).</summary>
        public static bool PausePressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        /// <summary>True on the frame R goes down (re-seed the world).</summary>
        public static bool ReseedPressed => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

        /// <summary>True while the right mouse button is held (orbit in desk view).</summary>
        public static bool OrbitHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>Mouse movement since last frame, in pixels (x right, y up).</summary>
        public static Vector2 PointerDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        /// <summary>
        /// Scroll wheel movement this frame as -1, 0 or +1 (positive = wheel pushed away = zoom in).
        /// Only the direction is used because the raw size of one wheel click differs between
        /// platforms and Input System versions.
        /// </summary>
        public static int ScrollNotches
        {
            get
            {
                if (Mouse.current == null)
                {
                    return 0;
                }

                float scroll = Mouse.current.scroll.ReadValue().y;
                return scroll > 0f ? 1 : scroll < 0f ? -1 : 0;
            }
        }
    }
}
