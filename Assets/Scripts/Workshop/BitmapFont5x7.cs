// =============================================================================
// BitmapFont5x7
// -----------------------------------------------------------------------------
// Purpose : A tiny built-in pixel font, so the console can print text (the paper
//           roll's lines, the label plates) without any font asset or text package.
// Idea    : Every character is a 5 x 7 grid of dots. A glyph is stored as 5 bytes, one
//           per COLUMN, with bit 0 the top dot and bit 6 the bottom dot (this is the
//           classic "5x7" layout used by small displays). Drawing a string is then
//           just walking along the characters and setting a pixel for each lit bit,
//           leaving one blank column between characters.
//           Lower-case letters are drawn as capitals; unknown characters as a hollow
//           box so a typo is visible rather than silent.
// Equations: character cell width = 5 + 1 (spacing) = 6 pixels, height = 7 (+1 line gap = 8)
//            pixel (x, y) of glyph g is lit when (g[x] >> y) & 1 == 1
// =============================================================================

using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>Draws text into a pixel buffer using a 5 x 7 dot font.</summary>
    public static class BitmapFont5x7
    {
        /// <summary>Width of one character cell in pixels (5 dots plus one blank column).</summary>
        public const int CellWidth = 6;

        /// <summary>Height of one line in pixels (7 dots plus one blank row).</summary>
        public const int LineHeight = 8;

        // Glyph columns for ASCII 32 (space) to 90 ('Z'): 59 glyphs x 5 bytes.
        private static readonly byte[] Glyphs =
        {
            0x00, 0x00, 0x00, 0x00, 0x00, // space
            0x00, 0x00, 0x5F, 0x00, 0x00, // !
            0x00, 0x07, 0x00, 0x07, 0x00, // "
            0x14, 0x7F, 0x14, 0x7F, 0x14, // #
            0x24, 0x2A, 0x7F, 0x2A, 0x12, // $
            0x23, 0x13, 0x08, 0x64, 0x62, // %
            0x36, 0x49, 0x56, 0x20, 0x50, // &
            0x00, 0x08, 0x07, 0x03, 0x00, // '
            0x00, 0x1C, 0x22, 0x41, 0x00, // (
            0x00, 0x41, 0x22, 0x1C, 0x00, // )
            0x2A, 0x1C, 0x7F, 0x1C, 0x2A, // *
            0x08, 0x08, 0x3E, 0x08, 0x08, // +
            0x00, 0x40, 0x30, 0x10, 0x00, // ,
            0x08, 0x08, 0x08, 0x08, 0x08, // -
            0x00, 0x00, 0x60, 0x60, 0x00, // .
            0x20, 0x10, 0x08, 0x04, 0x02, // /
            0x3E, 0x51, 0x49, 0x45, 0x3E, // 0
            0x00, 0x42, 0x7F, 0x40, 0x00, // 1
            0x72, 0x49, 0x49, 0x49, 0x46, // 2
            0x21, 0x41, 0x49, 0x4D, 0x33, // 3
            0x18, 0x14, 0x12, 0x7F, 0x10, // 4
            0x27, 0x45, 0x45, 0x45, 0x39, // 5
            0x3C, 0x4A, 0x49, 0x49, 0x31, // 6
            0x41, 0x21, 0x11, 0x09, 0x07, // 7
            0x36, 0x49, 0x49, 0x49, 0x36, // 8
            0x46, 0x49, 0x49, 0x29, 0x1E, // 9
            0x00, 0x00, 0x14, 0x00, 0x00, // :
            0x00, 0x40, 0x34, 0x00, 0x00, // ;
            0x00, 0x08, 0x14, 0x22, 0x41, // <
            0x14, 0x14, 0x14, 0x14, 0x14, // =
            0x00, 0x41, 0x22, 0x14, 0x08, // >
            0x02, 0x01, 0x59, 0x09, 0x06, // ?
            0x3E, 0x41, 0x5D, 0x59, 0x4E, // @
            0x7C, 0x12, 0x11, 0x12, 0x7C, // A
            0x7F, 0x49, 0x49, 0x49, 0x36, // B
            0x3E, 0x41, 0x41, 0x41, 0x22, // C
            0x7F, 0x41, 0x41, 0x41, 0x3E, // D
            0x7F, 0x49, 0x49, 0x49, 0x41, // E
            0x7F, 0x09, 0x09, 0x09, 0x01, // F
            0x3E, 0x41, 0x41, 0x51, 0x73, // G
            0x7F, 0x08, 0x08, 0x08, 0x7F, // H
            0x00, 0x41, 0x7F, 0x41, 0x00, // I
            0x20, 0x40, 0x41, 0x3F, 0x01, // J
            0x7F, 0x08, 0x14, 0x22, 0x41, // K
            0x7F, 0x40, 0x40, 0x40, 0x40, // L
            0x7F, 0x02, 0x1C, 0x02, 0x7F, // M
            0x7F, 0x04, 0x08, 0x10, 0x7F, // N
            0x3E, 0x41, 0x41, 0x41, 0x3E, // O
            0x7F, 0x09, 0x09, 0x09, 0x06, // P
            0x3E, 0x41, 0x51, 0x21, 0x5E, // Q
            0x7F, 0x09, 0x19, 0x29, 0x46, // R
            0x26, 0x49, 0x49, 0x49, 0x32, // S
            0x03, 0x01, 0x7F, 0x01, 0x03, // T
            0x3F, 0x40, 0x40, 0x40, 0x3F, // U
            0x1F, 0x20, 0x40, 0x20, 0x1F, // V
            0x3F, 0x40, 0x38, 0x40, 0x3F, // W
            0x63, 0x14, 0x08, 0x14, 0x63, // X
            0x03, 0x04, 0x78, 0x04, 0x03, // Y
            0x61, 0x59, 0x49, 0x4D, 0x43, // Z
        };

        /// <summary>The glyph set covers ASCII 32 to 90; this is the number of characters it can draw (lower case is folded to capitals).</summary>
        public const int GlyphCount = 59;

        /// <summary>True when the font has a real glyph for this character (after folding lower case to capitals).</summary>
        public static bool HasGlyph(char character)
        {
            int code = Fold(character);
            return code >= 32 && code < 32 + GlyphCount;
        }

        /// <summary>The 5 column bytes of a character's glyph (bit 0 = top dot). An unknown character gives a hollow box.</summary>
        public static byte Column(char character, int column)
        {
            int code = Fold(character);
            if (code < 32 || code >= 32 + GlyphCount)
            {
                return column == 0 || column == 4 ? (byte)0x7F : (byte)0x41;
            }

            return Glyphs[(code - 32) * 5 + column];
        }

        /// <summary>The width in pixels of a string.</summary>
        public static int WidthOf(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : text.Length * CellWidth - 1;
        }

        /// <summary>
        /// Draws a string into a pixel buffer. (0,0) is the TOP-left of the text; the buffer's row 0 is its BOTTOM row
        /// (like a Unity texture), so rows are flipped on the way in.
        /// </summary>
        /// <param name="pixels">The buffer, width x height, row 0 at the bottom.</param>
        /// <param name="width">Buffer width in pixels.</param>
        /// <param name="height">Buffer height in pixels.</param>
        /// <param name="text">What to draw.</param>
        /// <param name="x">Left edge of the first character.</param>
        /// <param name="topY">Distance from the TOP of the buffer to the top dot of the text.</param>
        /// <param name="ink">Colour of lit dots.</param>
        public static void Draw(Color32[] pixels, int width, int height, string text, int x, int topY, Color32 ink)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            for (int c = 0; c < text.Length; c++)
            {
                for (int column = 0; column < 5; column++)
                {
                    int px = x + c * CellWidth + column;
                    if (px < 0 || px >= width)
                    {
                        continue;
                    }

                    byte bits = Column(text[c], column);
                    for (int row = 0; row < 7; row++)
                    {
                        if (((bits >> row) & 1) == 0)
                        {
                            continue;
                        }

                        int py = height - 1 - (topY + row);
                        if (py >= 0 && py < height)
                        {
                            pixels[py * width + px] = ink;
                        }
                    }
                }
            }
        }

        private static int Fold(char character)
        {
            return character >= 'a' && character <= 'z' ? character - 32 : character;
        }
    }
}
