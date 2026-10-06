// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_GLYPH_PROPERTIES_INCLUDED
#define LIGHTSIDE_GLYPH_PROPERTIES_INCLUDED

#include "LightSideShaderFeatures.hlsl"

// Masking / clipping
uniform float4		_ClipRect;

// Glyph atlases (Texture2DArray — one slice per atlas page). One material serves all three modes;
// the per-glyph mode in UV1.w selects the array. Each keeps its own sampler state from the texture
// asset: SDF/MSDF are linear/bilinear/mip-less, color is sRGB/trilinear with a truncated mip chain.
#if defined(LIGHTSIDE_GLYPHS)
UNITY_DECLARE_TEX2DARRAY(_LightSideGlyphSdf);   // SDF  (RHalf)
UNITY_DECLARE_TEX2DARRAY(_LightSideGlyphMsdf);   // MSDF (RGBAHalf)
UNITY_DECLARE_TEX2DARRAY(_LightSideGlyphColor);  // Color (RGBA32 sRGB + mips)
#endif

#endif // LIGHTSIDE_GLYPH_PROPERTIES_INCLUDED
