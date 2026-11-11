// =============================================================================
// LabelPlate
// -----------------------------------------------------------------------------
// Purpose : A small engraved-looking nameplate on the console ("YEAR", "TREES",
//           "PAUSE"...). It makes the texture for its own text, so there are no
//           image files to keep in step with the words.
// Idea    : At start it creates a tiny texture just big enough for its text, fills it
//           with the plate colour, and draws the text in with BitmapFont5x7. The quad
//           the scene builder made for it shows that texture through the
//           Terrarium/ConsoleText shader, which keeps the dots crisp.
// Equations: texture width = text width + 6 (margins), height = 11 (7 dots + 4 margin)
// =============================================================================

using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>A nameplate whose text is drawn by code.</summary>
    public sealed class LabelPlate : MonoBehaviour
    {
        /// <summary>Texture height of every plate, in pixels.</summary>
        public const int HeightPixels = 11;

        [SerializeField] private string text = string.Empty;
        [SerializeField] private Renderer plate;
        [SerializeField] private Color background = new Color(0.12f, 0.11f, 0.09f);
        [SerializeField] private Color ink = new Color(0.92f, 0.82f, 0.60f);

        private Texture2D texture;

        /// <summary>Texture width in pixels for a text.</summary>
        public static int WidthFor(string label)
        {
            return BitmapFont5x7.WidthOf(label) + 6;
        }

        /// <summary>Sets what the plate says (used by the scene builder when it creates the plate in code).</summary>
        public void Configure(string label, Renderer renderer, Color backgroundColour, Color inkColour)
        {
            text = label;
            plate = renderer;
            background = backgroundColour;
            ink = inkColour;
        }

        private void Awake()
        {
            int width = WidthFor(text);
            texture = new Texture2D(width, HeightPixels, TextureFormat.RGBA32, false)
            {
                name = "Label " + text,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[width * HeightPixels];
            Color32 fill = background;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = fill;
            }

            BitmapFont5x7.Draw(pixels, width, HeightPixels, text, 3, 2, ink);
            texture.SetPixels32(pixels);
            texture.Apply(false);

            if (plate != null)
            {
                plate.material.SetTexture("_MainTex", texture);
            }
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
