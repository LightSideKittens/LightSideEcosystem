// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_COVERAGE_INCLUDED
#define LIGHTSIDE_COVERAGE_INCLUDED

// Coverage primitive shared by every LightSide surface — glyph fields and analytic shape fields alike.
// One signed-distance threshold, composed.
// SDF convention: sd < 0 inside, sd > 0 outside, edge at 0.
// Edge / width / spread params are field-normalized; multiply by dilateScale to reach sd units.
//
// Two distances ride together (MTSDF): sd.x = sharp (perpendicular / median), sd.y = round
// (true Euclidean, alpha channel). Single-channel fields supply sd.x == sd.y.
// TEXCOORD2.x packs mode + 16 x cornerCode (see LightSideCore.PackGlyphCoverage):
//   code 0    — legacy: pure perpendicular field (plain fill / undecorated surface);
//   code 0.25 — Round: Euclidean field, artifact-free offsets, round joins;
//   code >= 1 — Sharp: mitered joins clipped at this miter limit. The perpendicular field lies
//   at its Voronoi-owner seams (cracks where it overshoots, horns/islands where it undershoots);
//   min(sharp, round) kills overshoots — perpendicular distance can never exceed Euclidean —
//   and for outward offsets the Euclidean field bounds the miter (limit) and backstops
//   coverage (core), so seam garbage cannot survive.

// Inside-ness of an edge: ~1 where sd is inside `edge`, ~0 outside, soft-ramped across `soft`.
float LightSideInside(float sd, float edge, float soft)
{
    return saturate((edge - sd) / max(soft, 1e-5) + 0.5);
}

float LightSideInsideCorner(float2 sd, float edge, float soft, float corner)
{
    if (corner < 0.2)
        return LightSideInside(sd.x, edge, soft);
    if (corner < 0.5)
        return LightSideInside(sd.y, edge, soft);

    float m = LightSideInside(min(sd.x, sd.y), edge, soft);
    if (edge > 0.0)
    {
        m = min(m, LightSideInside(sd.y, edge * corner, soft));
        m = max(m, LightSideInside(sd.y, edge, soft));
    }
    return m;
}

// coverageMode: 0 = fill, 1 = stroke, 2 = shadow/glow.
// inner-shadow (mode 3) is composed by the caller — it needs a second, offset field tap.
//   fill        : p0 = dilate, soft = edge softness
//   stroke      : p0 = halfWidth, p1 = align (-1 inside / 0 center / +1 outside), soft = edge softness
//   shadow/glow : p0 = spread, soft = blur
// Fill and shadow/glow share the single p0-offset-edge formula by design; only stroke differs.
float LightSideCoverage(float mode, float2 sd, float aa, float p0, float p1, float soft, float dilateScale, float corner)
{
    float edge = max(aa, soft * dilateScale);
    float result = 0.0;

    if (mode >= 0.5 && mode < 1.5)
    {
        float w = p0 * dilateScale;
        float center = p1 * w;
        result = saturate(LightSideInsideCorner(sd, center + w, edge, corner)
                        - LightSideInsideCorner(sd, center - w, edge, corner));
    }
    else
        result = LightSideInsideCorner(sd, p0 * dilateScale, edge, corner);
    return result;
}

// Single-distance overload — the pre-MTSDF custom-shader contract, kept source-compatible.
float LightSideCoverage(float mode, float sd, float aa, float p0, float p1, float soft, float dilateScale)
{
    return LightSideCoverage(mode, float2(sd, sd), aa, p0, p1, soft, dilateScale, 0.0);
}

/// Splits the packed TEXCOORD2.x word into its corner code and draw mode.
void LightSideDecodeCoverageMode(float packed, out float mode, out float corner)
{
    float rounded = floor(packed + 0.5);
    corner = floor(rounded * 0.25) * 0.25;
    mode = rounded - 16.0 * corner;
}

#endif // LIGHTSIDE_COVERAGE_INCLUDED
