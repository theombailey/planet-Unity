// =============================================================================
// CubeSphere.hlsl
// -----------------------------------------------------------------------------
// Purpose : GPU twin of Assets/Scripts/Sphere/CubeSphere.cs. Maps between
//           directions on a sphere and texels of the six-face cube-sphere grid
//           that every simulation field is stored in. Read the header of the C#
//           file for the full idea and equations; this file repeats only the
//           short version. A GPU test compares the two, so keep them in step.
//
// Idea    : Six square faces (+X -X +Y -Y +Z -Z) projected onto a sphere, with
//           an "equiangular" warp so texels are similar in size everywhere.
//           No poles to pinch; seams are handled by "extend the grid past the
//           edge, turn it into a direction, find the texel that contains it".
//
// Equations (s, t in [-1,1] on a face; n = outward normal, r = right, u = up):
//     a = dot(d, r) / dot(d, n)            s = atan(a) * 4/pi
//     d = normalize( n + tan(s*pi/4) * r + tan(t*pi/4) * u )
//     texel (x, y) of N has centre  s = (x + 0.5) / N * 2 - 1
//
// Conventions: a texel is an int3 (x, y, face). Texel arrays are indexed
//     [x, y, face] i.e. Texture2DArray slice = face.
//
// Parameters: n - texels along one face edge (the field resolution).
// =============================================================================

#ifndef TERRARIUM_CUBESPHERE_INCLUDED
#define TERRARIUM_CUBESPHERE_INCLUDED

static const float CS_QUARTER_PI = 0.78539816339;

// Order +X -X +Y -Y +Z -Z. For every face right x up = normal.
static const float3 CS_NORMALS[6] =
{
    float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0),
    float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1)
};

static const float3 CS_RIGHTS[6] =
{
    float3(0, 0, -1), float3(0, 0, 1), float3(1, 0, 0),
    float3(1, 0, 0), float3(1, 0, 0), float3(-1, 0, 0)
};

static const float3 CS_UPS[6] =
{
    float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, -1),
    float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0)
};

// Largest |component| wins; ties go X, then Y, then Z; positive before negative.
int CS_FaceOf(float3 direction)
{
    float3 a = abs(direction);

    if (a.x >= a.y && a.x >= a.z)
    {
        return direction.x >= 0.0 ? 0 : 1;
    }

    if (a.y >= a.z)
    {
        return direction.y >= 0.0 ? 2 : 3;
    }

    return direction.z >= 0.0 ? 4 : 5;
}

// Direction -> face and warped face coordinates (s, t) in [-1, 1].
void CS_DirToFaceST(float3 direction, out int face, out float2 st)
{
    face = CS_FaceOf(direction);
    float depth = dot(direction, CS_NORMALS[face]);
    float2 cubePoint = float2(dot(direction, CS_RIGHTS[face]), dot(direction, CS_UPS[face])) / depth;
    st = atan(cubePoint) / CS_QUARTER_PI;
}

// Face coordinates -> unit direction. Also valid just beyond [-1, 1] (used for neighbours).
float3 CS_FaceSTToDir(int face, float2 st)
{
    float2 cubePoint = tan(st * CS_QUARTER_PI);
    return normalize(CS_NORMALS[face] + cubePoint.x * CS_RIGHTS[face] + cubePoint.y * CS_UPS[face]);
}

// Direction -> texel (x, y, face) of an n x n face.
int3 CS_DirToTexel(float3 direction, int n)
{
    int face;
    float2 st;
    CS_DirToFaceST(direction, face, st);
    int2 xy = clamp((int2)floor((st + 1.0) * 0.5 * n), 0, n - 1);
    return int3(xy, face);
}

float2 CS_TexelCentreST(int2 xy, int n)
{
    return ((float2)xy + 0.5) / n * 2.0 - 1.0;
}

// Unit direction through the centre of texel (x, y, face).
float3 CS_TexelCentreDir(int3 texel, int n)
{
    return CS_FaceSTToDir(texel.z, CS_TexelCentreST(texel.xy, n));
}

// Brings a texel address back onto the sphere: unchanged if inside its face, otherwise the
// texel containing the extended-grid point on the neighbouring face.
int3 CS_ResolveTexel(int3 texel, int n)
{
    if (all(texel.xy >= 0) && all(texel.xy < n))
    {
        return texel;
    }

    return CS_DirToTexel(CS_FaceSTToDir(texel.z, CS_TexelCentreST(texel.xy, n)), n);
}

// Seam-aware neighbour at offset (dx, dy), each -1, 0 or +1.
int3 CS_NeighbourTexel(int3 texel, int2 delta, int n)
{
    return CS_ResolveTexel(int3(texel.xy + delta, texel.z), n);
}

// Four texels and blend weights for a bilinear sample at a direction.
struct CSTaps
{
    int3 t00;
    int3 t10;
    int3 t01;
    int3 t11;
    float2 w;
};

CSTaps CS_BilinearTaps(float3 direction, int n)
{
    int face;
    float2 st;
    CS_DirToFaceST(direction, face, st);

    float2 f = (st + 1.0) * 0.5 * n - 0.5;
    int2 base = (int2)floor(f);

    CSTaps taps;
    taps.t00 = CS_ResolveTexel(int3(base + int2(0, 0), face), n);
    taps.t10 = CS_ResolveTexel(int3(base + int2(1, 0), face), n);
    taps.t01 = CS_ResolveTexel(int3(base + int2(0, 1), face), n);
    taps.t11 = CS_ResolveTexel(int3(base + int2(1, 1), face), n);
    taps.w = f - (float2)base;
    return taps;
}

// Defines a seam-correct bilinear sampling function for any texture-array type.
//   functionName - name of the function to create
//   textureType  - e.g. Texture2DArray<float4> or RWTexture2DArray<float4>
//   valueType    - float, float2, float3 or float4, matching the texture
// Usage: CS_DEFINE_SAMPLER(CS_SampleField4, Texture2DArray<float4>, float4)
//        float4 value = CS_SampleField4(_Field, direction, resolution);
// Hardware filtering is not used: it would blend across the unrelated texels
// at a face edge instead of the true neighbours.
#define CS_DEFINE_SAMPLER(functionName, textureType, valueType) \
valueType functionName(textureType tex, float3 direction, int n) \
{ \
    CSTaps taps = CS_BilinearTaps(direction, n); \
    valueType v00 = tex[uint3(taps.t00)]; \
    valueType v10 = tex[uint3(taps.t10)]; \
    valueType v01 = tex[uint3(taps.t01)]; \
    valueType v11 = tex[uint3(taps.t11)]; \
    return lerp(lerp(v00, v10, taps.w.x), lerp(v01, v11, taps.w.x), taps.w.y); \
}

#endif
