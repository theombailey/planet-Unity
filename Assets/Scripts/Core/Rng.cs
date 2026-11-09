// =============================================================================
// Rng
// -----------------------------------------------------------------------------
// Purpose : A small deterministic random number generator for the CPU side.
//           Seeded from the world seed, so the same seed always produces the
//           same world. (Unity's own Random is global shared state, which makes
//           results depend on who else called it; this one is self-contained.)
//
// Idea    : PCG ("permuted congruential generator"). A plain multiply-and-add
//           step advances the state; the output is the state scrambled by
//           xor-shifts and one more multiply, which hides the simple pattern of
//           the raw state. HashUint in Noise.hlsl uses the same scramble, so
//           the CPU and GPU share one definition of "random".
//
// Equations: state  = state * 747796405 + 2891336453          (mod 2^32)
//            word   = ((state >> ((state >> 28) + 4)) ^ state) * 277803737
//            output = (word >> 22) ^ word
//            float  = top 24 bits of output / 2^24             in [0, 1)
//
// Note    : A mutable struct: keep it in a variable or field and call it there.
// =============================================================================

namespace Terrarium
{
    /// <summary>Deterministic PCG random number generator.</summary>
    public struct Rng
    {
        private uint state;

        /// <summary>Creates a generator. The same seed always gives the same sequence.</summary>
        public Rng(int seed)
        {
            state = unchecked((uint)seed);
            NextUInt();
        }

        /// <summary>Next 32 random bits.</summary>
        public uint NextUInt()
        {
            unchecked
            {
                state = state * 747796405u + 2891336453u;
                uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
                return (word >> 22) ^ word;
            }
        }

        /// <summary>Next random float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>Next random float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }
    }
}
