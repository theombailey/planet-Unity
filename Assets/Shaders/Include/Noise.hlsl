// =============================================================================
// Noise.hlsl
// -----------------------------------------------------------------------------
// Purpose : Hand-written random and noise functions shared by every shader.
//           No textures: everything is computed from arithmetic on the input.
//
// Idea    : 1. HASH     - scramble a number so nearby inputs give unrelated
//                         outputs. This is the only source of "randomness".
//           2. VALUE NOISE - put a random value on every corner of an integer
//                         grid and blend smoothly between the 8 corners of the
//                         cell the point is in. Gives soft blobs about one
//                         grid cell across.
//           3. fBm      - "fractal Brownian motion": add several layers
//                         (octaves) of noise, each twice as fine and half as
//                         strong as the last. Big shapes plus small detail.
//
// Equations:
//     Hash        : integer mixing by multiply / xor-shift (PCG-style).
//     Fade curve  : f(t) = t*t*t*(t*(6t - 15) + 10)   (Perlin's quintic; its
//                   1st and 2nd derivatives are 0 at t = 0 and 1, so there
//                   are no visible creases on cell borders)
//     Value noise : trilinear blend of the 8 corner hashes using f(frac(p))
//     fBm         : sum over i of  0.5^i * noise(p * 2^i),  normalised to 0..1
//
// Parameters: p        - sample position; one noise "blob" per unit of p.
//             octaves  - number of fBm layers (more = finer detail, more cost).
//
// Why 3D  : sampling 3D noise at points on a sphere gives a pattern with no
//           seams and no pole pinching, which the planet needs from Phase 4.
// =============================================================================

#ifndef TERRARIUM_NOISE_INCLUDED
#define TERRARIUM_NOISE_INCLUDED

// Scrambles one 32-bit integer (PCG output permutation).
uint HashUint(uint value)
{
    uint state = value * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// Random float in [0,1) for an integer grid point.
float Hash3(int3 cell)
{
    uint h = HashUint((uint)cell.x + HashUint((uint)cell.y + HashUint((uint)cell.z)));
    return (float)(h >> 8u) * (1.0 / 16777216.0);   // top 24 bits -> [0,1)
}

// Smooth value noise in [0,1]. One blob per unit of p.
float ValueNoise(float3 p)
{
    float3 cellFloat = floor(p);
    int3 cell = (int3)cellFloat;
    float3 t = p - cellFloat;
    float3 f = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    float c000 = Hash3(cell + int3(0, 0, 0));
    float c100 = Hash3(cell + int3(1, 0, 0));
    float c010 = Hash3(cell + int3(0, 1, 0));
    float c110 = Hash3(cell + int3(1, 1, 0));
    float c001 = Hash3(cell + int3(0, 0, 1));
    float c101 = Hash3(cell + int3(1, 0, 1));
    float c011 = Hash3(cell + int3(0, 1, 1));
    float c111 = Hash3(cell + int3(1, 1, 1));

    float x00 = lerp(c000, c100, f.x);
    float x10 = lerp(c010, c110, f.x);
    float x01 = lerp(c001, c101, f.x);
    float x11 = lerp(c011, c111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

// Fractal noise in [0,1]: `octaves` layers, each twice as fine and half as strong.
float Fbm(float3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float total = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        sum += amplitude * ValueNoise(p);
        total += amplitude;
        amplitude *= 0.5;
        // Offset each octave so their grids do not line up at the origin.
        p = p * 2.0 + 17.31;
    }

    return sum / total;
}

// Ridged fractal noise in [0,1]: sharp crests, like mountain ridges.
// Idea: value noise is a smooth blob with its middle value 0.5 running along winding contour
// lines. r = 1 - |2n - 1| is 1 exactly on those contours and falls away linearly either side,
// which turns each contour into a sharp crest. Squaring narrows the crest. Octaves are summed
// like fBm.
float RidgedFbm(float3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float total = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        float ridge = 1.0 - abs(2.0 * ValueNoise(p) - 1.0);
        sum += amplitude * ridge * ridge;
        total += amplitude;
        amplitude *= 0.5;
        p = p * 2.0 + 31.7;
    }

    return sum / total;
}

#endif
