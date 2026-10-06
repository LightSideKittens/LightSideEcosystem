// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// Glyph field union — the overlapping glyph quads of one layer drawn as one shape. Each quad of a union
// folds the fields of the neighbours that reach its fragment into its own distance, so fill, stroke,
// shadow and glow all read the union's field; and each draws only its share of the union's coverage,
// so the quads covering a pixel together composite to exactly one draw of it:
//   share_i = (1 - (1 - a)^w_i) / a,  sum of w_i = 1  =>  product of (1 - a * share_i) = 1 - a
// under premultiplied source-over, whatever order they draw in. The weight w_i is the fragment's depth
// inside quad i's field box over the summed depths inside every box covering it, so a draw's share
// fades to nothing at its own box edge and a pixel where two quads' rasterization differs is one whose
// share there is already nothing. Records: see LightSideAtlasDecode.hlsl.
//
// Requires SAMPLE_SDF2_MODE (LightSideSdfSample.hlsl) and DILATE_SCALE from the pipeline header.

#ifndef LIGHTSIDE_GLYPH_UNION_INCLUDED
#define LIGHTSIDE_GLYPH_UNION_INCLUDED

// Depth of a point inside a box, negative outside.
float LightSideGlyphUnionInset(float4 box, float2 p)
{
    float2 inset = min(p - box.xy, box.zw - p);
    return min(inset.x, inset.y);
}

// Minimum of two fields, deepened by a fraction of `aa` where both lie within the edge ramp: neither
// outside by half of `aa` nor inside by all of it. Where two glyphs overlap or abut, their half-covered
// edges would otherwise leave a half-covered seam; a gap between glyphs keeps edges no nearer than two
// separate draws give them, and every level past the ramp keeps the exact minimum. Non-decreasing in
// both fields, like the minimum it deepens.
float2 LightSideGlyphUnionSeal(float2 a, float2 b, float aa)
{
    float inverse = 1.0 / max(aa, 1e-6);
    float2 weight = saturate(0.5 - max(a, b) * inverse) * saturate(1.0 + min(a, b) * inverse);
    return min(a, b) - weight * (0.5 * aa);
}

// Folds the neighbour fields of a union quad into its distances — `sd` at the fragment and `tapSd` at the
// inner-shadow tap `tapUv` behind it — and sets `share` to the weight of this draw; a quad outside a union,
// or a fragment outside its gate, keeps its own field and a weight of 1.
void LightSideGlyphUnion(float lane, float4 gate, float2 glyphUV, float2 tapUv, bool tap, float mode,
    float faceDilate, float aa, inout float2 sd, inout float2 tapSd, inout float share)
{
    float record = LightSideGlyphUnionRecord(lane);
    UNITY_BRANCH
    if (record >= 0.0 && all(glyphUV >= gate.xy) && all(glyphUV <= gate.zw))
    {
        float neighbours = LightSideLoadGlyphUnionTexel(record).y;
        float own = max(LightSideGlyphUnionInset(LightSideLoadGlyphUnionTexel(record + 2.0), glyphUV), 0.0);
        float total = own;
        [loop]
        for (float i = 0.0; i < (tap ? 2.0 * neighbours : neighbours); i += 1.0)
        {
            bool atTap = i >= neighbours;
            float entry = record + 3.0 + 3.0 * (atTap ? i - neighbours : i);
            float2 at = atTap ? glyphUV - tapUv : glyphUV;
            float inset = LightSideGlyphUnionInset(LightSideLoadGlyphUnionTexel(entry), at);
            if (!atTap) total += max(inset, 0.0);
            UNITY_BRANCH
            if (inset > 0.0)
            {
                float4 map = LightSideLoadGlyphUnionTexel(entry + 1.0);
                float2 offset = LightSideLoadGlyphUnionTexel(entry + 2.0).xy;
                float4 t = LightSideLoadGlyphTransformOfHandle(map.x);
                float2 d = SAMPLE_SDF2_MODE(mode, float3((at * map.yz + offset) * t.x + t.yz, t.w)) - faceDilate * DILATE_SCALE;
                float2 into = LightSideGlyphUnionSeal(atTap ? tapSd : sd, d, aa);
                if (atTap) tapSd = into;
                else sd = into;
            }
        }
        share = total > 0.0 ? own / total : 1.0;
    }
}

// This draw's part of the union's premultiplied colour at the fragment, for the weight the fold set.
half4 LightSideGlyphUnionApply(half4 color, float share)
{
    float alpha = color.a;
    float part = share;
    if (share < 1.0 && alpha > 1e-4)
        part = (1.0 - exp2(share * log2(max(1.0 - alpha, 1e-6)))) / alpha;
    return color * part;
}

#endif // LIGHTSIDE_GLYPH_UNION_INCLUDED
