#ifndef LIGHTSIDE_SHADOW_SURFACE_INCLUDED
#define LIGHTSIDE_SHADOW_SURFACE_INCLUDED

#include "LightSideShaderFeatures.hlsl"

#if defined(LIGHTSIDE_GLYPHS)
#include "LightSideGlyphCoverage.hlsl"
#endif
#if defined(LIGHTSIDE_SHAPE_SURFACE) && !defined(LIGHTSIDE_NO_SHAPES)
#include "LightSideShapeSurface.hlsl"
#endif

float LightSideShadowClip(float4 atlasUV, float4 cov, float4 meta, float4 shapeGeom,
    float4 shapeParams, float cutoff)
{
    float result = -1.0;
    float2 uvDx = ddx(atlasUV.xy);
    float2 uvDy = ddy(atlasUV.xy);
#if defined(LIGHTSIDE_GLYPHS)
    float2 dUV = fwidth(meta.xy);
    UNITY_BRANCH
    if (atlasUV.w < 1.5)
    {
        float aa = max(dUV.x, dUV.y) * meta.w;
        result = LightSideResolveGlyphCastCoverage(atlasUV, cov, meta.z, aa, uvDx, uvDy) - 0.5;
    }
    else if (atlasUV.w < 2.5)
    {
        half4 c = LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(atlasUV.xyz, uvDx, uvDy);
        result = c.a * meta.z - cutoff;
    }
#endif
#if defined(LIGHTSIDE_SHAPE_SURFACE) && !defined(LIGHTSIDE_NO_SHAPES)
    UNITY_BRANCH
    if (atlasUV.w > 2.5 && atlasUV.w < 3.5)
    {
        float packed = cov.x;
        float style = floor(packed * 0.001953125);
        packed -= style * 512.0;
        float kind = floor(packed * 0.0625);
        float distance;
        float patternT;
        float coverage = LightSideShapeCoverage(kind, packed - kind * 16.0,
            meta.xy, shapeGeom.xy, shapeParams, shapeGeom.z,
            float4(cov.yzw, shapeGeom.w), style, meta.w, float4(0.0, 0.0, 0.0, 0.0), 1.0, distance, patternT);
        result = coverage * meta.z - cutoff;
    }
#endif
#if defined(LIGHTSIDE_ATLAS_QUADS)
    UNITY_BRANCH
    if (atlasUV.w > 3.5 && atlasUV.w < 4.5)
        result = LIGHTSIDE_SAMPLE_ATLAS_QUAD(atlasUV.xyz).a * meta.z - cutoff;
#endif
    if (atlasUV.w > 4.5)
        result = LIGHTSIDE_SAMPLE_IMAGE_GRAD(atlasUV.xy, uvDx, uvDy).a * meta.z - cutoff;
    return result;
}

#endif
