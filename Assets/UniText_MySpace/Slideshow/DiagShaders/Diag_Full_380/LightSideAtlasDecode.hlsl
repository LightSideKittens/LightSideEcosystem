// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// The ONE copy of the glyph-atlas addressing, shared by every pipeline (built-in + URP) and every
// consumer (base SDF shaders + custom-effect preludes). Pipeline-agnostic: Texture2D.Load is plain
// HLSL, compiles identically in CGPROGRAM and HLSLPROGRAM and cross-compiles to texelFetch on
// GLSL ES 3.00 (core in the vertex stage).
//
// Addressing contract:
//   UV0.z = stable glyph HANDLE (allocated once per atlas entry, unchanged across pad-tier upgrades,
//   tile-size upgrades and compaction). The handle indexes _LightSideGlyphTable — one RGBA32F texel per
//   glyph holding the atlas transform (isotropic scale, offset.xy, page layer), produced by
//   GlyphAtlas.WriteTransformRow (the single owner of the placement math). Relocations rewrite the
//   texel; meshes are never patched. Color quads bypass the table: normalized atlas UV in UV0.xy,
//   page layer in UV0.z, UV0.w == 0.
//   A negative UV0.z names a union record instead, -(first texel + 1): the glyph draws as part of the
//   union of the overlapping fields of its layer (LightSideGlyphUnion.hlsl), and the record's first
//   texel holds its handle, so every reader resolving a handle through LightSideLoadGlyphTransform
//   renders such a quad as its own glyph.

#ifndef LIGHTSIDE_ATLAS_DECODE_INCLUDED
#define LIGHTSIDE_ATLAS_DECODE_INCLUDED

#include "LightSideSurface.hlsl"

#define LIGHTSIDE_SDF_PAD 0.5

// Glyph transform table — global (Shader.SetGlobalTexture), RGBA32F, FilterMode.Point (a hard
// requirement: float32 textures are non-filterable on the GLES3/WebGL2 floor and any other filter
// state makes the texture incomplete). Load-only — no SamplerState, no derivative rules, safe in
// the vertex stage.
// SYNC: width mirrors GlyphTransformTable.Width.
#define LIGHTSIDE_GLYPH_TABLE_WIDTH 1024.0
Texture2D<float4> _LightSideGlyphTable;
float4 _LightSideGlyphSdf_TexelSize;
float4 _LightSideGlyphMsdf_TexelSize;
float4 _LightSideGlyphColor_TexelSize;

// Per-glyph render mode, packed by the mesh generator into UV1.w = intraX + 2*mode:
//   0 = SDF (_LightSideGlyphSdf), 1 = MSDF (_LightSideGlyphMsdf), 2 = color (_LightSideGlyphColor, RGBA32 sRGB + mips),
//   -1 = pixel SDF, -2 = pixel MSDF: the atlas of mode 0 / 1, holding one texel per font pixel of a
//   pixel-art font, fetched at the texel centre with no edge ramp (LightSideGlyphIsPixel).
// Quad-constant by construction (all four corners carry the same mode), so interpolation of UV1.w
// stays inside [2*mode, 2*mode + 1] and both halves decode exactly at any pixel.
float LightSideGlyphMode(float uv1w)
{
    return LightSideSurfaceKind(uv1w);
}

// The intra-glyph X fraction (0 left edge, 1 right edge) that shares UV1.w with the mode.
float LightSideIntraX(float uv1w)
{
    return LightSideSurfaceFraction(uv1w);
}

// The pixel-grid modes (-1 / -2): nearest-texel sampling and a hard edge.
bool LightSideGlyphIsPixel(float mode)
{
    return mode < -0.5;
}

// The modes reading the MSDF atlas (1 and -2); every other table-mapped glyph reads the SDF atlas.
bool LightSideGlyphIsMsdf(float mode)
{
    return (mode > 0.5 && mode < 1.5) || mode < -1.5;
}

// Edge anti-aliasing width for a glyph: none on the pixel grid, where every screen pixel reads one
// whole font pixel and a ramp would only smear the step between two of them.
float LightSideGlyphEdgeAa(float mode, float aa)
{
    return LightSideGlyphIsPixel(mode) ? 0.0 : aa;
}

// Glyph union records — global (Shader.SetGlobalTexture), RGBA32F, FilterMode.Point, Load-only: the
// storage contract of _LightSideGlyphTable. Record layout, from its first texel:
//   0      handle, neighbour count, 0, 0
//   1      gate — the box of this glyph's field any neighbour field reaches, in its glyph UV
//   2      this glyph's field box, in its glyph UV
//   3 + 3n neighbour n's field box, in this glyph's UV
//   4 + 3n neighbour n's handle, the per-axis scale taking this glyph's UV to the neighbour's, 0
//   5 + 3n the offset completing that map, 0, 0
// A field box bounds where a field can cover anything; outside it the field is empty for its layer.
// SYNC: width mirrors GlyphUnionTable.Width.
#define LIGHTSIDE_GLYPH_UNION_WIDTH 1024.0
Texture2D<float4> _LightSideGlyphUnion;

float4 LightSideLoadGlyphUnionTexel(float index)
{
    float row = floor(index / LIGHTSIDE_GLYPH_UNION_WIDTH);
    float col = index - row * LIGHTSIDE_GLYPH_UNION_WIDTH;
    return _LightSideGlyphUnion.Load(int3(col, row, 0));
}

// The first texel of the union record a glyph's UV0.z lane names, or -1 for a lane holding a handle.
// Rounded, so the lane survives interpolation.
float LightSideGlyphUnionRecord(float lane)
{
    return lane < -0.5 ? floor(-lane - 0.5) : -1.0;
}

// The gate of the union record a glyph's UV0.z lane names; an empty box for a lane holding a handle.
float4 LightSideGlyphUnionGate(float lane)
{
    float record = LightSideGlyphUnionRecord(lane);
    float4 gate = float4(1.0, 1.0, -1.0, -1.0);
    if (record >= 0.0)
        gate = LightSideLoadGlyphUnionTexel(record + 1.0);
    return gate;
}

// The stable handle a glyph's UV0.z lane carries: the lane itself, or the handle its union record holds.
float LightSideGlyphHandle(float lane)
{
    float record = LightSideGlyphUnionRecord(lane);
    float handle = lane;
    if (record >= 0.0)
        handle = LightSideLoadGlyphUnionTexel(record).x;
    return handle;
}

// The glyph's atlas transform: atlasUV = glyphUV * x + yz on page layer w. Vertex-stage fetch;
// the handle is fp32-exact, and a union record's lane resolves to the handle it holds.
float4 LightSideLoadGlyphTransform(float lane)
{
    float handle = LightSideGlyphHandle(lane);
    float row = floor(handle / LIGHTSIDE_GLYPH_TABLE_WIDTH);
    float col = handle - row * LIGHTSIDE_GLYPH_TABLE_WIDTH;
    return _LightSideGlyphTable.Load(int3(col, row, 0));
}

// Color-atlas mip selection — computed explicitly and clamped to the written mip window.
// The color atlas carries a truncated, tile-aligned mip chain; on Vulkan/Metal the physical
// image holds a full chain whose tail levels are never written, and the sampler's own LOD
// clamp cannot be relied on across backends — an unclamped minified fetch blends unwritten
// (transparent-black) levels and fades the glyph. _LightSideGlyphColorMaxLod = written mips - 1,
// set globally by the color GlyphAtlas when its storage materializes. The -0.5 sharpen bias lives
// here (explicit-LOD sampling bypasses the texture's own mipMapBias).
float _LightSideGlyphColorMaxLod;

float LightSideColorLod(float2 dx, float2 dy)
{
    float2 px = dx * _LightSideGlyphColor_TexelSize.zw;
    float2 py = dy * _LightSideGlyphColor_TexelSize.zw;
    float d = max(dot(px, px), dot(py, py));
    return min(0.5 * log2(max(d, 1e-12)) - 0.5, _LightSideGlyphColorMaxLod);
}

// Uniform atlas-UV entry for the base shaders and custom preludes, driven by the per-glyph mode
// (LightSideGlyphMode of UV1.w): SDF/MSDF glyphs resolve through the transform table, color quads
// carry normalized UV + page layer directly in UV0.xyz. Vertex-stage only.
float3 LightSideComputeAtlasUV(float4 texcoord0, float4 texcoord1)
{
    if (LightSideGlyphMode(texcoord1.w) > 1.5)
        return texcoord0.xyz;
    float4 t = LightSideLoadGlyphTransform(texcoord0.z);
    return float3(texcoord0.xy * t.x + t.yz, t.w);
}

#endif
