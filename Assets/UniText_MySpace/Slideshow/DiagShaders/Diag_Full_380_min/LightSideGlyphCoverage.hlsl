// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_GLYPH_COVERAGE_INCLUDED
#define LIGHTSIDE_GLYPH_COVERAGE_INCLUDED

// Glyph application of the shared coverage primitive: samples the glyph field, then composes it
// through LightSideCoverage. The primitive itself — and every mode/corner convention it obeys —
// lives in Core and is shared with analytic shape fields.

#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_380_min/LightSideCoverage.hlsl"

// Full coverage for a painted quad, including the inner-shadow second SDF tap. Shared by every
// SDF shader (Canvas + world) so the contract lives once. Requires SAMPLE_SDF2_MODE_GRAD from
// LightSideSdfSample.hlsl + DILATE_SCALE from the including pipeline header (hence the guard).
// atlasUV = (uv, page, glyphMode) — w selects the SDF vs MSDF decode per glyph; uvDx/uvDy are the
// caller's pre-branch derivatives of atlasUV.xy (every fetch here is gradient-explicit, so the
// quad-constant mode branch and the coverage-mode branch never flatten into extra fetches).
// cov = TEXCOORD2 (mode, p0, p1, softness); extra = (faceDilate, glyphH, sdfScale.x, sdfScale.y).
// The inner-shadow tap offset cov.yz is in em; one glyphUV unit spans glyphH em.
// unionLane is the glyph's UV0.z and unionGate the gate its vertex stage loaded
// (LightSideGlyphUnionGate): a quad of a glyph union resolves the union's field and sets `share` to
// the weight LightSideGlyphUnionApply scales its colour by; any other quad sets it to 1.
#ifdef SAMPLE_SDF2_MODE_GRAD
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_380_min/LightSideGlyphUnion.hlsl"

float LightSideResolveGlyphCoverage(float4 atlasUV, float2 glyphUV, float4 cov, float4 extra, float aa,
    float2 uvDx, float2 uvDy, float unionLane, float4 unionGate, out float share)
{
    aa = LightSideGlyphEdgeAa(atlasUV.w, aa);
    float faceDilate = extra.x;
    float2 sd = SAMPLE_SDF2_MODE_GRAD(atlasUV.w, atlasUV.xyz, uvDx, uvDy) - faceDilate * DILATE_SCALE;
    float mode, corner;
    LightSideDecodeCoverageMode(cov.x, mode, corner);
    bool tap = mode > 2.5;
    float2 tapUv = float2(cov.y, cov.z) / max(extra.y, 1e-6);
    float2 sd2 = sd;
    if (tap)
        sd2 = SAMPLE_SDF2_MODE_GRAD(atlasUV.w, float3(atlasUV.xy - tapUv * float2(extra.z, extra.w), atlasUV.z),
            uvDx, uvDy) - faceDilate * DILATE_SCALE;
    share = 1.0;
    LightSideGlyphUnion(unionLane, unionGate, glyphUV, tapUv, tap, atlasUV.w, extra.z, faceDilate, aa,
        uvDx, uvDy, sd, sd2, share);
    float result = 0.0;
    if (tap)
    {
        float inside = LightSideInsideCorner(sd, 0.0, aa, corner);
        float offsetInside = LightSideInsideCorner(sd2, 0.0, max(aa, cov.w * DILATE_SCALE), corner);
        result = inside * (1.0 - offsetInside);
    }
    else
        result = LightSideCoverage(mode, sd, aa, cov.y, cov.z, cov.w, DILATE_SCALE, corner);
    return result;
}

// Silhouette coverage for shadow casting: inner-shadow (mode 3) casts as the face; stroke/shadow/glow
// cast at their outer extent so the cast shadow matches the visible paint layers, not just the core glyph.
float LightSideResolveGlyphCastCoverage(float4 atlasUV, float4 cov, float faceDilate, float aa, float2 uvDx, float2 uvDy)
{
    aa = LightSideGlyphEdgeAa(atlasUV.w, aa);
    float2 sd = SAMPLE_SDF2_MODE_GRAD(atlasUV.w, atlasUV.xyz, uvDx, uvDy) - faceDilate * DILATE_SCALE;
    float mode, corner;
    LightSideDecodeCoverageMode(cov.x, mode, corner);
    if (mode > 2.5)                        // inner-shadow casts the plain face, not its inset tap params
        return LightSideInsideCorner(sd, 0.0, aa, corner);
    return LightSideCoverage(mode, sd, aa, cov.y, cov.z, cov.w, DILATE_SCALE, corner);
}
#endif

#endif
