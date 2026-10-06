// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_SURFACE_INCLUDED
#define LIGHTSIDE_SURFACE_INCLUDED

// The vertex contract every LightSide surface writes and every LightSide shader reads.
// One channel plan across text glyphs, range decorations, vector shapes and atlas quads, in both
// Canvas and world space — so one material can draw all of them and a Canvas run of mixed LightSide
// elements collapses into a single draw call.
//
//   POSITION   position
//   NORMAL     world-space face normal (world contexts; unused on Canvas)
//   COLOR      straight tint, premultiplied in the fragment after paint resolves
//   TEXCOORD0  surface geometry — glyphs: (uv.xy, tileId, emHeight); shapes: (local.xy, halfSize.xy)
//   TEXCOORD1  surface params; .w = intra-surface fraction + 2 * surface kind  <- the discriminator
//   TEXCOORD2  draw mode; .x = coverage mode + 16 * family, .yzw = its arguments
//   TEXCOORD3  paint — (u, v, rampRow, paintKind + 8 * spread)
//   TANGENT    .w = the shape's per-kind params as one lane (LightSideShapeParams.hlsl); .xyz stay
//              zero, since a Canvas batch multiplies them by each element's normal matrix
//
// The family in TEXCOORD2.x is the surface's own sub-selector: corner style for glyph fields, shape
// kind for analytic fields. Coverage modes (fill / stroke / shadow / inner-shadow) mean the same thing
// in both, which is why one primitive resolves them all.
//
// The discriminator lives in TEXCOORD1.w rather than a keyword on purpose: a keyword would split the
// material and with it the batch, which is the one thing this contract exists to prevent. Its fractional
// part stays free for a per-surface 0..1 value (glyphs carry the intra-glyph X fraction there).

#define LIGHTSIDE_SURFACE_GLYPH_SDF    0.0
#define LIGHTSIDE_SURFACE_GLYPH_MSDF   1.0
#define LIGHTSIDE_SURFACE_GLYPH_COLOR  2.0
#define LIGHTSIDE_SURFACE_SHAPE        3.0
#define LIGHTSIDE_SURFACE_ATLAS_QUAD   4.0
#define LIGHTSIDE_SURFACE_IMAGE        5.0

/// Decodes the surface kind from the packed TEXCOORD1.w word.
float LightSideSurfaceKind(float packed)
{
    return floor(packed * 0.5);
}

/// Decodes the free 0..1 fraction riding alongside the surface kind.
float LightSideSurfaceFraction(float packed)
{
    return packed - floor(packed * 0.5) * 2.0;
}

#endif // LIGHTSIDE_SURFACE_INCLUDED
