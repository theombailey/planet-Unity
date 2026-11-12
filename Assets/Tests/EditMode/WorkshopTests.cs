// =============================================================================
// WorkshopTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the pure parts of the console and workshop:
//           - nixie tube formatting (right-aligned, blank leading zeros, decimal point,
//             minus sign, overflow, rounding),
//           - the pixel font covers the characters the console prints and draws dots where
//             a glyph has them,
//           - the paper log prints the right lines at the right times (first snapshot,
//             a new season, milestones going up and down, people fading and recovering),
//           - pausing the clock remembers the speed it had,
//           - the volume dial maps its angle to a volume and back,
//           - the console's wedge mesh has outward-facing faces.
// =============================================================================

using System.Collections.Generic;
using NUnit.Framework;
using Terrarium.Interaction;
using Terrarium.Rendering;
using Terrarium.Workshop;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class WorkshopTests
    {
        // ------------------------------------------------------------- nixie

        private static int[] Digits(double value, int tubes, int decimals, out bool[] points)
        {
            var digits = new int[tubes];
            points = new bool[tubes];
            NixieDisplay.Format(value, tubes, decimals, digits, points);
            return digits;
        }

        [Test]
        public void Nixie_RightAligned_LeadingBlanks_DecimalPointInTheRightPlace()
        {
            int[] digits = Digits(17.6, 4, 1, out bool[] points);

            Assert.AreEqual(new[] { NixieDisplay.Blank, 1, 7, 6 }, digits);
            Assert.AreEqual(new[] { false, false, true, false }, points, "The point sits after the second tube: 17.6");
        }

        [Test]
        public void Nixie_ZeroAndFractions_AlwaysShowTheDigitBeforeThePoint()
        {
            Assert.AreEqual(new[] { NixieDisplay.Blank, NixieDisplay.Blank, 0 }, Digits(0.0, 3, 0, out _), "A zero year shows 0, not nothing.");
            Assert.AreEqual(new[] { NixieDisplay.Blank, NixieDisplay.Blank, 0, 4 }, Digits(0.4, 4, 1, out _), "0.4 not .4");
        }

        [Test]
        public void Nixie_NegativeNumbers_PutAMinusJustLeftOfTheDigits()
        {
            int[] digits = Digits(-5.2, 4, 1, out _);

            Assert.AreEqual(new[] { NixieDisplay.Blank, NixieDisplay.Minus, 5, 2 }, digits);
        }

        [Test]
        public void Nixie_TooBigForTheTubes_ShowsAllNines()
        {
            Assert.AreEqual(new[] { 9, 9, 9, 9 }, Digits(123456, 4, 0, out _));
        }

        [Test]
        public void Nixie_RoundsToTheShownPlaces()
        {
            Assert.AreEqual(new[] { NixieDisplay.Blank, 3, 0 }, Digits(2.96, 3, 1, out _), "2.96 shown to one place is 3.0");
        }

        // -------------------------------------------------------------- font

        [Test]
        public void Font_CoversTheCharactersTheConsolePrints()
        {
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .:-+/%()'!?,=<>abcxyz")
            {
                Assert.IsTrue(BitmapFont5x7.HasGlyph(c), $"No glyph for '{c}'.");
            }

            Assert.IsFalse(BitmapFont5x7.HasGlyph('~'), "An unsupported character should be reported, not silently blank.");
        }

        [Test]
        public void Font_DrawsDotsWhereAGlyphHasThem_AndNothingForASpace()
        {
            const int width = 8;
            const int height = 8;
            var pixels = new Color32[width * height];
            var ink = new Color32(10, 20, 30, 255);

            // 'I' is a vertical bar in the middle column (bit pattern 0x7F): all seven rows are lit there.
            BitmapFont5x7.Draw(pixels, width, height, "I", 0, 0, ink);
            int lit = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 255)
                {
                    lit++;
                }
            }

            Assert.AreEqual(2 + 7 + 2, lit, "The letter I is a bar of 7 dots with a 2-dot serif at the top and bottom.");

            var blank = new Color32[width * height];
            BitmapFont5x7.Draw(blank, width, height, " ", 0, 0, ink);
            foreach (Color32 pixel in blank)
            {
                Assert.AreEqual(0, pixel.a);
            }
        }

        // ---------------------------------------------------------- paper log

        private static EcosystemStats Stats(int year, float phase, int trees, int birds, int people, int houses = 0)
        {
            return new EcosystemStats { Valid = true, Year = year, YearPhase = phase, TreesAlive = trees, BoidsAlive = birds, PeopleAlive = people, Houses = houses, MeanTemperature = 15f };
        }

        [Test]
        public void PaperLog_FirstSnapshot_OnlyRecordsWhereThingsStand()
        {
            var log = new PaperLog();
            var lines = new List<string>();

            log.Poll(Stats(3, 0.1f, 400, 1500, 5000), lines);

            Assert.AreEqual(1, lines.Count, "It must not print a milestone for every level the world started above.");
            StringAssert.Contains("YEAR 3 SPRING", lines[0]);
        }

        [Test]
        public void PaperLog_NewSeason_PrintsAThreeLineBlock_OnceOnly()
        {
            var log = new PaperLog();
            var lines = new List<string>();
            log.Poll(Stats(1, 0.10f, 400, 1500, 5000), lines);
            lines.Clear();

            log.Poll(Stats(1, 0.30f, 400, 1500, 5000), lines);
            Assert.AreEqual(3, lines.Count);
            StringAssert.Contains("SUMMER", lines[0]);

            lines.Clear();
            log.Poll(Stats(1, 0.32f, 400, 1500, 5000), lines);
            Assert.AreEqual(0, lines.Count, "The same season is not printed again.");
        }

        [Test]
        public void PaperLog_Milestones_PrintGoingUp_AndOnlyPrintFallingOnceWellBelow()
        {
            var log = new PaperLog();
            var lines = new List<string>();
            log.Poll(Stats(1, 0.10f, 40, 800, 5000), lines);
            lines.Clear();

            log.Poll(Stats(1, 0.11f, 120, 800, 5000), lines);
            CollectionAssert.Contains(lines, "TREES PASS 50");
            CollectionAssert.Contains(lines, "TREES PASS 100");

            lines.Clear();
            log.Poll(Stats(1, 0.12f, 95, 800, 5000), lines);
            Assert.AreEqual(0, lines.Count, "A wobble just under a milestone prints nothing.");

            log.Poll(Stats(1, 0.13f, 85, 800, 5000), lines);
            CollectionAssert.Contains(lines, "TREES UNDER 100");
        }

        [Test]
        public void PaperLog_PeopleDyingOut_ThenRecovering()
        {
            var log = new PaperLog();
            var lines = new List<string>();
            log.Poll(Stats(1, 0.10f, 100, 800, 500), lines);
            lines.Clear();

            log.Poll(Stats(1, 0.11f, 100, 800, 5), lines);
            CollectionAssert.Contains(lines, "FAMILIES DYING OUT");

            lines.Clear();
            log.Poll(Stats(1, 0.12f, 100, 800, 100), lines);
            CollectionAssert.Contains(lines, "FAMILIES RECOVER");
        }

        [Test]
        public void PaperLog_Houses_PrintAsATownGrows()
        {
            var log = new PaperLog();
            var lines = new List<string>();
            log.Poll(Stats(1, 0.10f, 100, 800, 500, houses: 0), lines);
            lines.Clear();

            log.Poll(Stats(1, 0.11f, 100, 800, 500, houses: 60), lines);
            CollectionAssert.Contains(lines, "HOUSES PASS 10");
            CollectionAssert.Contains(lines, "HOUSES PASS 50");
        }

        [Test]
        public void PaperLog_ShortCounts_AreReadable()
        {
            Assert.AreEqual("950", PaperLog.Short(950));
            Assert.AreEqual("12.3K", PaperLog.Short(12_300));
            Assert.AreEqual("1.2M", PaperLog.Short(1_200_000));
        }

        // ------------------------------------------------------------- clock

        [Test]
        public void Clock_TogglePause_ResumesAtTheSpeedItHad()
        {
            var config = ScriptableObject.CreateInstance<SimConfig>();
            try
            {
                var clock = new SimClock(config);
                clock.SetTimeScale(4f);

                clock.TogglePause();
                Assert.IsTrue(clock.IsPaused);
                Assert.AreEqual(0f, clock.TimeScale);

                clock.TogglePause();
                Assert.AreEqual(4f, clock.TimeScale);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // -------------------------------------------------------------- dial

        [Test]
        public void VolumeDial_MapsAngleToVolumeAndBack()
        {
            Assert.AreEqual(0f, DialProp.VolumeForAngle(-90f), 1e-5f);
            Assert.AreEqual(0.5f, DialProp.VolumeForAngle(0f), 1e-5f);
            Assert.AreEqual(1f, DialProp.VolumeForAngle(90f), 1e-5f);
            Assert.AreEqual(1f, DialProp.VolumeForAngle(300f), 1e-5f, "Beyond the end it stays at full volume.");

            foreach (float volume in new[] { 0f, 0.25f, 0.6f, 1f })
            {
                Assert.AreEqual(volume, DialProp.VolumeForAngle(DialProp.VolumeAngle(volume)), 1e-5f);
            }
        }

        // -------------------------------------------------------------- mesh

        [Test]
        public void Wedge_HasOutwardFacingFaces_AndTheRightSlope()
        {
            Mesh mesh = ProceduralMeshes.BuildWedge(2f, 1f, 0.2f, 0.6f);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            Vector3 centre = mesh.bounds.center;
            Assert.AreEqual(12, triangles.Length / 3, "Six quad faces make twelve triangles.");

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 outward = (a + b + c) / 3f - centre;
                Assert.Greater(Vector3.Dot(normal, outward), 0f, $"Triangle {i / 3} faces inward.");
            }

            float frontTop = 0f;
            float backTop = 0f;
            foreach (Vector3 v in vertices)
            {
                if (Mathf.Approximately(v.z, -0.5f)) { frontTop = Mathf.Max(frontTop, v.y); }
                if (Mathf.Approximately(v.z, 0.5f)) { backTop = Mathf.Max(backTop, v.y); }
            }

            Assert.AreEqual(0.2f, frontTop, 1e-5f);
            Assert.AreEqual(0.6f, backTop, 1e-5f);
            Object.DestroyImmediate(mesh);
        }
    }
}
