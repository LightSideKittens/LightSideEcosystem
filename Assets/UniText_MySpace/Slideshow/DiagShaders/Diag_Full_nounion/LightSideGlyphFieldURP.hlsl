// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// URP-flavored counterpart of LightSideGlyphField.cginc.
// Same SDF / MSDF atlas math, but uses URP/Core HLSL API (TEXTURE2D_ARRAY, SAMPLE_TEXTURE2D_ARRAY)
// instead of legacy UNITY_* macros. Encoding (signed distance in EM-space [-0.5, 0.5]
// mapped to R16F [0, 1], shelf layout, page stride) is identical to the built-in path —
// CPU pipeline emits the same atlas for both renderers.

#ifndef LIGHTSIDE_GLYPH_FIELD_URP_INCLUDED
#define LIGHTSIDE_GLYPH_FIELD_URP_INCLUDED

#include "LightSideShaderFeatures.hlsl"
#include "LightSideSurface.hlsl"


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#if defined(LIGHTSIDE_GLYPHS)
#include "LightSideAtlasDecode.hlsl"
#endif

#if defined(LIGHTSIDE_ATLAS_QUADS)
TEXTURE2D_ARRAY(_LightSideLottieAtlas); SAMPLER(sampler_LightSideLottieAtlas);
#define LIGHTSIDE_SAMPLE_ATLAS_QUAD(uvw) SAMPLE_TEXTURE2D_ARRAY(_LightSideLottieAtlas, sampler_LightSideLottieAtlas, (uvw).xy, (uvw).z)
#endif

#define SDF_PAD       LIGHTSIDE_SDF_PAD
#define DILATE_SCALE  SDF_PAD

// Glyph atlases — one material binds all three; the per-glyph mode in UV1.w selects the array.
// Each keeps its own sampler state from the texture asset (SDF/MSDF linear/bilinear/mip-less,
// color sRGB/trilinear with a truncated mip chain).
#if defined(LIGHTSIDE_GLYPHS)
TEXTURE2D_ARRAY(_LightSideGlyphSdf);   SAMPLER(sampler_LightSideGlyphSdf);
TEXTURE2D_ARRAY(_LightSideGlyphMsdf);   SAMPLER(sampler_LightSideGlyphMsdf);
TEXTURE2D_ARRAY(_LightSideGlyphColor);  SAMPLER(sampler_LightSideGlyphColor);  // Color (RGBA32 sRGB + mips)
#endif

// Every quad = coverage(mode) × paint(kind). texcoord2 = (coverageMode, p0, p1, softness),
// texcoord3 = (paintU, paintV, rampRow, paintKind). `color` is the straight quad colour,
// premultiplied in the fragment after paint resolves. Same layout as the built-in path.
struct LightSideSurfaceVertex
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;     // per-quad world-space normal written by WorldBatcher
    float4 color      : COLOR;      // straight vertex colour; premultiplied in fragment after paint
    float4 texcoord0  : TEXCOORD0;  // xy = glyph UV (color: normalized atlas UV), z = encoded tile id (color: page layer), w = glyphH (color: 0)
    float4 texcoord1  : TEXCOORD1;  // x = aspect + 1024*(deformRow+1) (see LightSideDeform.hlsl), y = faceDilate, z = cluster index, w = intra-glyph X fraction (0..1) + 2*glyphMode
    float4 texcoord2  : TEXCOORD2;  // coverageMode, p0, p1, softness
    float4 texcoord3  : TEXCOORD3;
    float4 tangent    : TANGENT;    // w = shape params lane (see LightSideShapeParams.hlsl); xyz unused
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// A world mesh has no Canvas ahead of it: the colour stream is the authored sRGB bytes. A
// linear-space project decodes them here, in the vertex stage (URP output is linear light); a
// gamma-space project compiles the call away.
half4 GammaToLinearIfNeeded(half4 color)
{
#ifndef UNITY_COLORSPACE_GAMMA
    color.rgb = SRGBToLinear(color.rgb);
#endif
    return color;
}

// Glyph sampling — the SDF/MSDF decode lives once in LightSideSdfSample.hlsl, selected per glyph
// by the vertex-stream mode; the distance fetches read level 0 by explicit LOD (see that header for why).
#include "LightSideShapeParams.hlsl"

#if defined(LIGHTSIDE_GLYPHS)
#define LIGHTSIDE_SAMPLE_ATLAS_TEXEL(uv) SAMPLE_TEXTURE2D_ARRAY_LOD(_LightSideGlyphSdf, sampler_LightSideGlyphSdf, (uv).xy, (uv).z, 0)
#define LIGHTSIDE_SAMPLE_MSDF_TEXEL(uv) SAMPLE_TEXTURE2D_ARRAY_LOD(_LightSideGlyphMsdf, sampler_LightSideGlyphMsdf, (uv).xy, (uv).z, 0)
#define LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_LOD(_LightSideGlyphColor, sampler_LightSideGlyphColor, (uv).xy, (uv).z, LightSideColorLod((dx), (dy)))
#include "LightSideSdfSample.hlsl"
#endif
#if defined(LIGHTSIDE_GLYPHS) || defined(LIGHTSIDE_SHAPES)
#include "LightSideDeform.hlsl"
#endif

#endif // LIGHTSIDE_GLYPH_FIELD_URP_INCLUDED
