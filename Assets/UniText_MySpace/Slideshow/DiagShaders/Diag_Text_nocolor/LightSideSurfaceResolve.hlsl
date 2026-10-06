// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_SURFACE_RESOLVE_INCLUDED
#define LIGHTSIDE_SURFACE_RESOLVE_INCLUDED

#include "LightSideShaderFeatures.hlsl"
#include "LightSideImageColor.hlsl"

// The one fragment surface body every LightSide surface shader resolves through — Canvas, world CG and
// world URP alike: the deform prelude, the derivatives, the analytic shape field, the single paint
// resolve, and the four-way ladder over the surface kind (shape / atlas quad / colour glyph / glyph
// coverage). Everything pass-specific — clipping, lighting, fog — is applied by the caller to what
// this returns. Each branch of the ladder resolves its paint after its coverage: a paint resolved
// ahead of the ladder stays live across every field fetch and costs every fragment of every surface
// the registers it pins.
//
// The including shader declares, before this include:
//   LIGHTSIDE_SURFACE_VARYINGS        — its interpolator struct: atlasUV, glyphUV, cov, paint, color,
//                                        extra, shapeGeom, shapeParams.
//   LIGHTSIDE_SAMPLE_ATLAS_QUAD(uvw)  — one fetch from the global array atlas at (u, v, layer).
//   the file statics lightSideDistanceT / lightSidePatternT behind LIGHTSIDE_PAINT_DISTANCE_T /
//   LIGHTSIDE_PAINT_PATTERN_T, and the glyph coverage, shape surface and paint headers — the order
//   LightSideSurfacePaint.hlsl establishes.
//   LIGHTSIDE_NO_SHAPES (optional) — compiles the analytic shape surface out, for passes whose meshes
//                                    never carry shape quads.

half4 LightSideResolveSurface(LIGHTSIDE_SURFACE_VARYINGS input)
{
    // Deformation displaces the sample point ahead of the derivatives, so the AA ramp widens
    // exactly where the field was stretched; a no-op on every quad no deformation touched.
    float2 glyphUV = input.glyphUV;
    float4 atlasUV = input.atlasUV;
#if defined(LIGHTSIDE_GLYPHS) && !defined(LIGHTSIDE_NO_GLYPH_DEFORM)
    LightSideApplyGlyphDeform(input.shapeGeom.z, input.extra.z, glyphUV, atlasUV);
#endif

    // Derivatives run before the mode branch, so the colour and image fetches inside it can take them
    // explicitly — the branch is quad-constant and never flattens.
    float2 uvDx = ddx(atlasUV.xy);
    float2 uvDy = ddy(atlasUV.xy);
    float2 dUV = fwidth(glyphUV);

    // Analytic shapes evaluate their field first: distance and pattern paint read it back through the
    // hooks, and one resolve then serves every kind — which is what keeps the atlas fetches below
    // explicit instead of forcing the mode branch to flatten.
    float shapeCoverage = 0.0;
#if !defined(LIGHTSIDE_SHAPE_SURFACE) || defined(LIGHTSIDE_NO_SHAPES)
    const bool isShape = false;
#else
    bool isShape = atlasUV.w > 2.5 && atlasUV.w < 3.5;
    float shapeD = 0.0;
    float patternT = 0.0;
    UNITY_BRANCH
    if (isShape)
    {
        float packed = input.cov.x;
        float shapeStyle = floor(packed * 0.001953125);
        packed -= shapeStyle * 512.0;
        float shapeKind = floor(packed * 0.0625);
        shapeCoverage = LightSideShapeCoverage(shapeKind, packed - shapeKind * 16.0,
            input.glyphUV, input.shapeGeom.xy, input.shapeParams, input.shapeGeom.z,
            float4(input.cov.yzw, input.shapeGeom.w), shapeStyle, input.extra.w, input.paint, input.extra.x,
            shapeD, patternT);
        lightSideDistanceT = LightSideShapeDistanceT(shapeD, input.shapeGeom.xy, input.extra.x);
        lightSidePatternT = patternT;
    }
#endif

    half4 result = 0;
    UNITY_BRANCH
    if (isShape)
    {
        result = LightSideComposePaintCoverage(
            LightSideResolvePaint(input.paint, input.color, input.extra.x, _LightSidePaintTexture_TexelSize.xy * 0.5), shapeCoverage);
    }
#if !defined(LIGHTSIDE_NO_IMAGE_SURFACE)
    else if (atlasUV.w > 4.5)
    {
        result = LightSideApplyImageColor(LIGHTSIDE_SAMPLE_IMAGE_GRAD(atlasUV.xy, uvDx, uvDy)) *
            half4(input.color.rgb * input.color.a, input.color.a);
    }
#endif
#if defined(LIGHTSIDE_ATLAS_QUADS)
    else if (atlasUV.w > 3.5)
    {
        // Atlas quad (vector animation): a straight-alpha tile tinted by the vertex colour,
        // premultiplied here because every LightSide surface blends premultiplied.
        half4 c = LIGHTSIDE_SAMPLE_ATLAS_QUAD(atlasUV.xyz) * input.color;
        result = half4(c.rgb * c.a, c.a);
    }
#endif
#if defined(LIGHTSIDE_GLYPHS)
    else
    {
        float aa = max(dUV.x, dUV.y) * input.extra.y;
        float share;
        float coverage = LightSideResolveGlyphCoverage(atlasUV, glyphUV, input.cov, input.extra, aa,
            input.shapeGeom.x, input.shapeParams, share);
        half4 paintCol = LightSideResolvePaint(input.paint, input.color, 1.0, _LightSidePaintTexture_TexelSize.xy * 0.5);
        result = LightSideGlyphUnionApply(LightSideComposePaintCoverage(paintCol, coverage), share);
    }
#endif
    return result;
}

#endif
