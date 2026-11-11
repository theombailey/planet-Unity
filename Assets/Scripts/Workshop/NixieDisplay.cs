// =============================================================================
// NixieDisplay
// -----------------------------------------------------------------------------
// Purpose : A row of nixie tubes that shows one number. The console has one for
//           each key reading (year, temperature, trees, birds, fungus).
// Idea    : The number is turned into one digit per tube (Format, a plain function so
//           it can be tested), and each tube's shader is told its digit, whether its
//           decimal point is lit, and how brightly to glow. Tubes are only told when
//           something changes, so a steady reading costs nothing.
// Formatting rules (Format):
//     * the value is rounded to `decimals` places and written right-aligned,
//     * leading zeros are blank (like a real nixie readout), but the digit just left
//       of the decimal point is always shown (0.4 not .4),
//     * a negative number puts a minus sign (the middle bar) just left of its digits,
//     * a number too big for the tubes shows all nines rather than a wrong number.
// Equations: scaled = round(|value| * 10^decimals);  digit_i = (scaled / 10^i) mod 10
// =============================================================================

using UnityEngine;

namespace Terrarium.Workshop
{
    /// <summary>Shows a number on a row of nixie tubes.</summary>
    public sealed class NixieDisplay : MonoBehaviour
    {
        /// <summary>Digit code for a dark tube.</summary>
        public const int Blank = -1;

        /// <summary>Digit code for a minus sign.</summary>
        public const int Minus = 10;

        private static readonly int DigitId = Shader.PropertyToID("_Digit");
        private static readonly int PointId = Shader.PropertyToID("_Point");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        [Tooltip("The tubes, left to right.")]
        [SerializeField] private Renderer[] tubes;

        [Tooltip("How many places after the decimal point.")]
        [SerializeField] private int decimals;

        [Tooltip("Brightness of the glow (0 = tubes off).")]
        [SerializeField] private float glow = 1f;

        private MaterialPropertyBlock block;
        private int[] digits;
        private bool[] points;
        private int[] shownDigits;
        private bool[] shownPoints;
        private float shownGlow = -1f;

        /// <summary>Sets the tubes and decimal places (used by the scene builder when it creates the display in code).</summary>
        public void Configure(Renderer[] tubeRenderers, int decimalPlaces)
        {
            tubes = tubeRenderers;
            decimals = decimalPlaces;
            Allocate();
        }

        private void Awake()
        {
            Allocate();
        }

        private void Allocate()
        {
            int count = tubes != null ? tubes.Length : 0;
            block = new MaterialPropertyBlock();
            digits = new int[count];
            points = new bool[count];
            shownDigits = new int[count];
            shownPoints = new bool[count];
            for (int i = 0; i < count; i++)
            {
                shownDigits[i] = int.MinValue;
            }

            shownGlow = -1f;
        }

        /// <summary>Writes a number to the tubes.</summary>
        public void Show(float value)
        {
            if (tubes == null || tubes.Length == 0)
            {
                return;
            }

            if (digits == null || digits.Length != tubes.Length)
            {
                Allocate();
            }

            Format(value, tubes.Length, decimals, digits, points);

            for (int i = 0; i < tubes.Length; i++)
            {
                if (tubes[i] == null || (shownDigits[i] == digits[i] && shownPoints[i] == points[i] && Mathf.Approximately(shownGlow, glow)))
                {
                    continue;
                }

                shownDigits[i] = digits[i];
                shownPoints[i] = points[i];
                tubes[i].GetPropertyBlock(block);
                block.SetFloat(DigitId, digits[i]);
                block.SetFloat(PointId, points[i] ? 1f : 0f);
                block.SetFloat(GlowId, glow);
                block.SetFloat(SeedId, i + 1);
                tubes[i].SetPropertyBlock(block);
            }

            shownGlow = glow;
        }

        /// <summary>
        /// Turns a number into one digit code per tube (<see cref="Blank"/>, 0..9 or <see cref="Minus"/>) and which tube's
        /// decimal point is lit. <paramref name="digits"/> and <paramref name="points"/> must each have <paramref name="tubeCount"/> entries.
        /// </summary>
        public static void Format(double value, int tubeCount, int decimals, int[] digits, bool[] points)
        {
            for (int i = 0; i < tubeCount; i++)
            {
                digits[i] = Blank;
                points[i] = false;
            }

            if (tubeCount <= 0)
            {
                return;
            }

            decimals = Mathf.Clamp(decimals, 0, tubeCount - 1);
            if (decimals > 0)
            {
                points[tubeCount - 1 - decimals] = true;
            }

            bool negative = value < 0.0;
            double scaledValue = System.Math.Round(System.Math.Abs(value) * System.Math.Pow(10.0, decimals));
            long scaled = scaledValue > 9e15 ? long.MaxValue : (long)scaledValue;

            // Digits are written from the right. The first digit left of the decimal point is always shown.
            int mostSignificant = tubeCount - 1 - decimals;
            int tube = tubeCount - 1;
            while (tube >= 0)
            {
                bool mustShow = tube >= mostSignificant;
                if (scaled == 0 && !mustShow)
                {
                    break;
                }

                digits[tube] = (int)(scaled % 10);
                scaled /= 10;
                tube--;
            }

            if (scaled > 0)
            {
                // Too big for the tubes: all nines.
                for (int i = 0; i < tubeCount; i++)
                {
                    digits[i] = 9;
                }

                return;
            }

            if (negative && tube >= 0)
            {
                digits[tube] = Minus;
            }
        }
    }
}
