// =============================================================================
// RngTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the deterministic random number generator: the same seed
//           always gives the same sequence, different seeds differ, floats stay
//           in [0, 1) and are evenly spread, and Range respects its bounds.
// =============================================================================

using NUnit.Framework;

namespace Terrarium.Tests
{
    public sealed class RngTests
    {
        [Test]
        public void Rng_SameSeed_GivesTheSameSequence()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void Rng_DifferentSeeds_GiveDifferentSequences()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            int same = 0;

            for (int i = 0; i < 1000; i++)
            {
                if (a.NextUInt() == b.NextUInt())
                {
                    same++;
                }
            }

            Assert.Less(same, 3);
        }

        [Test]
        public void Rng_Floats_StayInUnitRange_AndAreEvenlySpread()
        {
            var rng = new Rng(7);
            var buckets = new int[10];
            const int count = 100_000;

            for (int i = 0; i < count; i++)
            {
                float value = rng.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                buckets[(int)(value * 10f)]++;
            }

            foreach (int bucket in buckets)
            {
                Assert.AreEqual(count / 10, bucket, count / 10 * 0.05, "A tenth of the values should fall in each tenth of the range.");
            }
        }

        [Test]
        public void Rng_Range_RespectsItsBounds()
        {
            var rng = new Rng(99);

            for (int i = 0; i < 10_000; i++)
            {
                Assert.That(rng.Range(-3f, 8f), Is.GreaterThanOrEqualTo(-3f).And.LessThan(8f));
            }
        }
    }
}
