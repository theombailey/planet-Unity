// =============================================================================
// PaperRoll
// -----------------------------------------------------------------------------
// Purpose : The console's paper strip: lines of text print at the bottom and the
//           paper feeds upward, like a small dot-matrix printer or a chart recorder.
//           It is where events and slow facts go (a new season, a population
//           milestone, a meteor strike) instead of a screen full of numbers.
// Idea    : The paper is one small texture drawn by code (BitmapFont5x7 puts the
//           letters in). New lines wait in a queue and are printed one at a time:
//             1. everything already on the paper moves up one line in the texture,
//             2. the new line is drawn in the bottom row,
//             3. the visible window is set one line too high (so the new line is
//                still hidden) and then eases down, which looks like the paper
//                feeding out of the printer slot.
//           The shader shows only a window of the texture (ConsoleText.shader) and
//           curls and shades the paper.
// Equations: texture height = window height + 1 line;  window offset s eases from 1 line to 0;
//            uv' = ( u, v * window / texture + s / texture )
// Parameters: line width in characters (derived from the texture width), seconds per printed line.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>A strip of paper that prints lines of text and feeds upward.</summary>
    public sealed class PaperRoll : MonoBehaviour
    {
        private const int WindowLines = 8;
        private const int WidthPixels = 156;
        private const int Margin = 3;
        private const float FeedSeconds = 0.4f;
        private const int MaxQueued = 40;

        private static readonly int WindowId = Shader.PropertyToID("_Window");
        private static readonly int ScrollId = Shader.PropertyToID("_Scroll");

        [Tooltip("The renderer showing the paper (its material uses Terrarium/ConsoleText with Paper = 1).")]
        [SerializeField] private Renderer paperRenderer;

        private readonly Queue<string> pending = new Queue<string>();
        private Texture2D texture;
        private Color32[] pixels;
        private MaterialPropertyBlock block;
        private int heightPixels;
        private float feedProgress = 1f;   // 0 = new line just drawn (hidden), 1 = fully fed

        /// <summary>Most characters that fit on one line.</summary>
        public static int MaxCharacters => (WidthPixels - 2 * Margin + 1) / BitmapFont5x7.CellWidth;

        /// <summary>Lines waiting to be printed.</summary>
        public int PendingCount => pending.Count;

        /// <summary>Sets the renderer (used by the scene builder when it creates the roll in code).</summary>
        public void Configure(Renderer renderer)
        {
            paperRenderer = renderer;
        }

        private void Awake()
        {
            heightPixels = WindowLines * BitmapFont5x7.LineHeight + BitmapFont5x7.LineHeight;
            texture = new Texture2D(WidthPixels, heightPixels, TextureFormat.RGBA32, false)
            {
                name = "PaperRoll",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            pixels = new Color32[WidthPixels * heightPixels];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);

            block = new MaterialPropertyBlock();
            if (paperRenderer != null)
            {
                // An instance of the material, so the runtime texture is never written into the material asset.
                paperRenderer.material.SetTexture("_MainTex", texture);
            }

            ApplyWindow(1f);
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        /// <summary>Queues a line of text to be printed. Long lines are cut to fit.</summary>
        public void Print(string line)
        {
            if (pending.Count >= MaxQueued)
            {
                pending.Dequeue();
            }

            pending.Enqueue(line ?? string.Empty);
        }

        private void Update()
        {
            if (texture == null)
            {
                return;
            }

            if (feedProgress < 1f)
            {
                feedProgress = Mathf.Min(1f, feedProgress + Time.unscaledDeltaTime / FeedSeconds);
                ApplyWindow(feedProgress);
            }
            else if (pending.Count > 0)
            {
                PrintOne(pending.Dequeue());
            }
        }

        private void PrintOne(string line)
        {
            int lineHeight = BitmapFont5x7.LineHeight;

            // 1. Move everything up one line (row 0 is the bottom of the texture).
            for (int row = heightPixels - 1; row >= lineHeight; row--)
            {
                System.Array.Copy(pixels, (row - lineHeight) * WidthPixels, pixels, row * WidthPixels, WidthPixels);
            }

            // 2. Blank the bottom line and draw the new text on it.
            for (int i = 0; i < lineHeight * WidthPixels; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            if (line.Length > MaxCharacters)
            {
                line = line.Substring(0, MaxCharacters);
            }

            // The text's top dot sits `1` pixel below the top of the bottom line cell.
            BitmapFont5x7.Draw(pixels, WidthPixels, heightPixels, line, Margin, heightPixels - lineHeight + 1, new Color32(34, 30, 44, 255));
            texture.SetPixels32(pixels);
            texture.Apply(false);

            // 3. Start the feed with the new line hidden below the window.
            feedProgress = 0f;
            ApplyWindow(0f);
        }

        // progress 0 = window one line too high (new line hidden), 1 = window at the bottom (new line fully shown).
        private void ApplyWindow(float progress)
        {
            if (paperRenderer == null)
            {
                return;
            }

            float lineShare = (float)BitmapFont5x7.LineHeight / heightPixels;
            float eased = 1f - (1f - progress) * (1f - progress);
            paperRenderer.GetPropertyBlock(block);
            block.SetFloat(WindowId, (float)(heightPixels - BitmapFont5x7.LineHeight) / heightPixels);
            block.SetFloat(ScrollId, (1f - eased) * lineShare);
            paperRenderer.SetPropertyBlock(block);
        }
    }
}
