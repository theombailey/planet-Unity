// =============================================================================
// InputReader
// -----------------------------------------------------------------------------
// Purpose : The only file that talks to Unity's input API.
// Idea    : The project uses the new Input System package (the old Input class
//           is disabled in Player Settings). Every other script asks questions
//           like "was the debug key pressed?" through this class, so swapping
//           the input backend later means editing this one file.
// Note    : Stateless. Each property reads the device directly; if no keyboard
//           is connected the answer is simply false.
// =============================================================================

using UnityEngine.InputSystem;

namespace Terrarium.Interaction
{
    /// <summary>Stateless queries for the game's keys, isolating the input backend.</summary>
    public static class InputReader
    {
        /// <summary>True on the frame F1 goes down (toggle the debug panel).</summary>
        public static bool DebugPanelTogglePressed => Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;

        /// <summary>True on the frame Space goes down (pause / resume the sim).</summary>
        public static bool PausePressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        /// <summary>True on the frame R goes down (re-seed the world).</summary>
        public static bool ReseedPressed => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
    }
}
