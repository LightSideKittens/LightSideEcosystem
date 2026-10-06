// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// Field deformation — the ONE copy of the sample-point displacement every glyph surface shares.
// A deformed quad samples its distance field at a coordinate displaced by travelling value noise, so
// the outline bends per pixel while staying a distance field: the edge keeps its analytic
// anti-aliasing, and every layer derived from the same field (fill, stroke, shadow, glow,
// inner-shadow) bends with it for free.
//
// Where it runs: BEFORE the fragment's derivative preamble, so ddx/ddy see the displaced coordinate
// and the AA ramp widens exactly where the field was stretched. The displacement itself takes no
// derivative — one Load and one fetch at an explicit level, plus ALU — so it is safe inside the
// quad-constant gate below.
//
// Row contract (_LightSideDeformTable, written by DeformFieldTable): one texel,
// (amplitude, scale, offsetX, offsetY). Amplitude is in glyph-height units and scale is noise cells
// per glyph height, so a deformation looks identical at every font size; the offset is where the
// field has travelled to, which is what carries the animation.
//
// Reach: the displaced coordinate leaves the glyph's ink box, and the distance field is only valid
// within the tile's rasterized rim. Validity is guaranteed UPSTREAM — the writer declares its
// amplitude as the glyph's outward extent, which grows the pad tier and expands the quad exactly as
// a stroke or glow does. There is deliberately no clamp here: a clamp would smear the outermost ring
// of every deformed glyph to hide a reach the writer is required to have reserved.

#ifndef LIGHTSIDE_DEFORM_INCLUDED
#define LIGHTSIDE_DEFORM_INCLUDED

// Lane step of the deform row inside UV1.x, which carries `aspect + LIGHTSIDE_DEFORM_ROW_STEP *
// (row + 1)`. A power of two so the float interpolator recovers both halves exactly, and two orders
// of magnitude above any real glyph's width/height ratio so the aspect half never carries into the
// row half — the decode's half-step bias spends that headroom to survive interpolation rounding at
// aspect 0.
// SYNC: mirrors DeformFieldTable.RowLaneStep.
#define LIGHTSIDE_DEFORM_ROW_STEP 1024.0

// Deform parameter rows — global (Shader.SetGlobalTexture), RGBA32F, FilterMode.Point, Load-only.
// Same storage contract as _LightSideGlyphTable: float32 textures are non-filterable on the
// GLES3/WebGL2 floor, and Load needs no sampler state, no derivative rules and no uniform control flow.
Texture2D<float4> _LightSideDeformTable;

// The deform row carried by UV1.x, or −1 when the lane carries the glyph aspect alone (every quad no
// deformation touched, so plain text decodes to "none" for free).
float LightSideDeformRow(float lane)
{
    return floor(lane * (1.0 / LIGHTSIDE_DEFORM_ROW_STEP) + 0.5 / LIGHTSIDE_DEFORM_ROW_STEP) - 1.0;
}

// Two uncorrelated channels per call — one lattice corner yields both displacement axes. Trig-free:
// a sin-based hash costs four transcendentals per corner on a path that samples four of them.
float2 LightSideDeformHash2(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// SYNC: mirrors DeformFieldTable.NoisePeriod.
#define LIGHTSIDE_DEFORM_NOISE_PERIOD 64.0

// Deform noise — global (Shader.SetGlobalTexture), written once by DeformFieldTable: LightSideDeformHash2 at
// every lattice point of one period, two channels, bilinear, repeating.
Texture2D<float2> _LightSideDeformNoise;
SamplerState sampler_LightSideDeformNoise;

// Smooth two-channel value noise in [0,1]², one lattice cell per unit of p, repeating every
// LIGHTSIDE_DEFORM_NOISE_PERIOD cells: one bilinear fetch between the four lattice values of the cell, at the
// smoothstepped fraction.
float2 LightSideDeformNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    float2 at = (i + f * f * (3.0 - 2.0 * f) + 0.5) * (1.0 / LIGHTSIDE_DEFORM_NOISE_PERIOD);
    return _LightSideDeformNoise.SampleLevel(sampler_LightSideDeformNoise, at, 0);
}

float2 LightSideDeformDisplacement(float2 coord, float amplitude)
{
    return (LightSideDeformNoise2(coord) * 2.0 - 1.0) * amplitude;
}

// Displaces the coordinates a glyph fragment samples its field at. A no-op unless this quad is an
// SDF/MSDF glyph carrying a deform row — the gate reads a lane already interpolated and is
// quad-constant, so undeformed text pays one comparison and no fetch. The gate compares the lane with
// one row step less half a step, exactly where LightSideDeformRow reaches 0; the two change together.
//
// `lane` is the glyph's UV1.x as the fragment received it (the surfaces forward it verbatim through
// their shape-geometry interpolator, which glyph quads leave otherwise unread — a float lane, since
// the packed row does not survive the half precision the glyph interpolator carries). `sdfScale` is
// the glyph's isotropic atlas scale, and `atlasUV.w` the glyph mode.
//
// The atlas coordinate takes the same displacement through that scale, keeping the two coordinates
// one deformed sample point rather than two. The noise field is offset per glyph by its atlas tile
// origin, which the fragment already holds as the difference of its two coordinates, so one
// deformation desynchronizes between letters without a per-glyph parameter row — and an atlas
// relocation re-seeds that glyph's pattern.
void LightSideApplyGlyphDeform(float lane, float sdfScale, inout float2 glyphUV, inout float4 atlasUV)
{
    UNITY_BRANCH
    if (lane >= LIGHTSIDE_DEFORM_ROW_STEP - 0.5 && atlasUV.w < 1.5)
    {
        float4 p = _LightSideDeformTable.Load(int3(0, (int)LightSideDeformRow(lane), 0));

        float2 tileOrigin = (atlasUV.xy - glyphUV * sdfScale) * 512.0;
        float2 coord = glyphUV * p.y + tileOrigin + p.zw;
        float2 delta = LightSideDeformDisplacement(coord, p.x);

        glyphUV += delta;
        atlasUV.xy += delta * sdfScale;
    }
}

#endif // LIGHTSIDE_DEFORM_INCLUDED
