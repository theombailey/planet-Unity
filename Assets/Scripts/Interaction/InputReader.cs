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

        /// <summary>True on the frame F2 goes down (toggle the field debug view).</summary>
        public static bool FieldViewTogglePressed => Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;

        /// <summary>True while either Shift key is held.</summary>
        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        /// <summary>True while the left mouse button is held.</summary>
        public static bool PaintHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;

        /// <summary>Mouse position in screen pixels (origin bottom-left).</summary>
        public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        /// <summary>True while the right mouse button is held (orbit in desk view).</summary>
        public static bool OrbitHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>True on the frame the left mouse button goes down.</summary>
        public static bool PrimaryPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        /// <summary>True on the frame the left mouse button is released.</summary>
        public static bool PrimaryReleased => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;

        /// <summary>True on the frame the right mouse button goes down.</summary>
        public static bool SecondaryPressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        /// <summary>True on the frame the right mouse button is released.</summary>
        public static bool SecondaryReleased => Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;

        /// <summary>True on the frame Escape goes down (put a held tool back).</summary>
        public static bool CancelPressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        /// <summary>WASD as a vector: x = D minus A (right), y = W minus S (forward), each -1, 0 or +1.</summary>
        public static Vector2 MoveAxis
        {
            get
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard == null)
                {
                    return Vector2.zero;
                }

                float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

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
