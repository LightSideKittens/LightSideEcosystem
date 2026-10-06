// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// The ONE copy of the SDF/MSDF distance decode, shared by every pipeline header — the same move
// LightSideAtlasDecode.hlsl makes for the shelf/tile decode. The format is selected per glyph by the
// vertex-stream mode (LightSideGlyphMode of UV1.w), not by a material keyword. Before including, the
// pipeline header defines the texel fetches, which read level 0 by explicit LOD: the SDF and MSDF atlases
// hold one level, an explicit level keeps every fetch legal inside quad-constant branches without
// derivatives, and the fetch stays bilinear whatever anisotropy a project forces on its textures.
//   LIGHTSIDE_SAMPLE_ATLAS_TEXEL(uv) — SDF atlas (_LightSideGlyphSdf)
//   LIGHTSIDE_SAMPLE_MSDF_TEXEL(uv)  — MSDF atlas (_LightSideGlyphMsdf)
//
// Two distances ride together (MTSDF): x = sharp (perpendicular / median of RGB), y = round
// (true Euclidean, alpha channel). Single-channel SDF supplies x == y.

#ifndef LIGHTSIDE_SDF_SAMPLE_INCLUDED
#define LIGHTSIDE_SDF_SAMPLE_INCLUDED

#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Text_novtf/LightSideAtlasDecode.hlsl"

float LightSideMedian3(float3 v)
{
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

float2 LightSideSdf2FromSdf(float4 texel)  { return texel.rr - 0.5; }
float2 LightSideSdf2FromMsdf(float4 texel) { return float2(LightSideMedian3(texel.rgb), texel.a) - 0.5; }

// The atlas position a glyph field is read at. A pixel-grid glyph (LightSideGlyphIsPixel) holds one
// distance per font pixel, so the fetch moves to the centre of the texel the sample lands in: on the
// atlas' bilinear sampler that returns the texel unblended — nearest-neighbour without a second sampler.
float3 LightSideSdfSampleUV(float mode, float3 uv)
{
    float4 texelSize = LightSideGlyphIsMsdf(mode) ? _LightSideGlyphMsdf_TexelSize : _LightSideGlyphSdf_TexelSize;
    return LightSideGlyphIsPixel(mode) ? float3((floor(uv.xy * texelSize.zw) + 0.5) * texelSize.xy, uv.z) : uv;
}

float2 LightSideSampleSdf2(float mode, float3 uv)
{
    uv = LightSideSdfSampleUV(mode, uv);
    float2 result;
    [branch]
    if (LightSideGlyphIsMsdf(mode))
        result = LightSideSdf2FromMsdf(LIGHTSIDE_SAMPLE_MSDF_TEXEL(uv));
    else
        result = LightSideSdf2FromSdf(LIGHTSIDE_SAMPLE_ATLAS_TEXEL(uv));
    return result;
}

#define SAMPLE_SDF2_MODE(mode, uv) LightSideSampleSdf2(mode, uv)

#endif
