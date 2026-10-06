// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_SHAPE_SURFACE_INCLUDED
#define LIGHTSIDE_SHAPE_SURFACE_INCLUDED

#include "LightSideShaderFeatures.hlsl"

#if defined(LIGHTSIDE_SHAPES)
#include "LightSideShapeField.hlsl"
#include "LightSideDeform.hlsl"
#include "LightSideShapeModifiers.hlsl"
#include "LightSidePaint.hlsl"

// Dash rows — global (Shader.SetGlobalTexture), RGBA32F, FilterMode.Point, Load-only, written by
// ShapeDashTable: texel 0 = (dash, gap, offset, cap), texel 1 = (trim start, trim end, exact, -). The same storage
// contract as the deform table: float32 textures are non-filterable on the GLES3/WebGL2 floor, and Load needs
// no sampler state, no derivative rules and no uniform control flow.
Texture2D<float4> _LightSideDashTable;

// The error function, to within about 1e-4 (Winitzki): the edge of a Gaussian-blurred half-plane, which is
// what a soft edge measured by the distance field approximates.
float LightSideErf(float x)
{
    float x2 = x * x;
    float t = x2 * (1.27323954 + 0.147 * x2) / (1.0 + 0.147 * x2);
    return ((x < 0.0) ? -1.0 : 1.0) * sqrt(max(1.0 - exp(-t), 0.0));
}

// Coverage of a silhouette blurred by a Gaussian of standard deviation `sigma`, at signed distance d from its
// outline: a half at the outline, falling to nothing outside.
float LightSideGaussianEdge(float d, float sigma)
{
    return 0.5 - 0.5 * LightSideErf(d * 0.70710678 / sigma);
}

// The outward unit normal of the field at the fragment, in the quad's local units: the field's screen-space
// gradient carried back through the inverse transpose of the screen-to-local Jacobian, so it reads the same
// whichever way the quad is rotated, scaled or flipped on the target. Quad-uniform callers only — it takes
// derivatives.
float2 LightSideFieldNormal(float d, float2 p)
{
    float2 g = float2(ddx(d), ddy(d));
    float2 px = ddx(p), py = ddy(p);
    float2 n = float2(py.y * g.x - px.y * g.y, px.x * g.y - py.x * g.x) * sign(px.x * py.y - px.y * py.x);
    float len = length(n);
    return len > 1e-6 ? n / len : float2(0.0, 1.0);
}

// The stroke's transverse field `sd` cut down along the outline: by a dash pattern of period dash + gap,
// carried `offset` of its own units along the outline, and by the trim that keeps the outline between two
// fractions of its length (a start past the end keeps the part wrapping through the origin). `along` is arc
// length to the nearest point of the outline and `total` the outline's length, in the units of `sd`. The
// period is stretched to the nearest whole number of periods that fills the outline, so the pattern closes on
// itself where the outline does rather than leaving a fragment there, and the offset is stretched with it, so a
// whole number of periods carries the pattern onto itself; `exact` keeps the given lengths and that fragment
// with them. A round end fillets the corner where the cut meets the band over the band's
// half-width, so a dash ends in a semicircle; a square end runs the dash half a width past its cut.
float LightSideStrokeDashField(float sd, float along, float total, float halfW, float4 dash, float4 trim)
{
    float cut = -1e6;
    float period = dash.x + dash.y;
    if (dash.x > 0.0 && period > 0.0)
    {
        float dashLen = dash.x;
        float shift = dash.z;
        if (trim.z < 0.5 && total > 0.0)
        {
            float fit = total / (max(1.0, round(total / period)) * period);
            period *= fit;
            dashLen *= fit;
            shift *= fit;
        }
        float t = sdShapeMod(along + shift, period);
        cut = (t < dashLen) ? -min(t, dashLen - t) : min(t - dashLen, period - t);
        if (dash.w > 1.5) cut -= halfW;
    }
    if (trim.x > 0.0 || trim.y < 1.0)
    {
        float s0 = trim.x * total, s1 = trim.y * total;
        float at = (total > 0.0) ? sdShapeMod(along - trim.w * total, total) : along;
        float keep = (trim.x <= trim.y) ? max(s0 - at, at - s1) : min(s0 - at, at - s1);
        cut = max(cut, keep);
    }
    float capRadius = dash.w < 0.5 ? halfW : 0.0;
    float2 q = float2(cut, sd) + capRadius;
    float result = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - capRadius;
    return result;
}

#else
#include "LightSideRoundedRect.hlsl"
#endif

// The distance-paint parameter for a shape: the field measured inward from the outline, clamped there
// because outside is not part of the interior domain — a repeating ramp must not fold the antialiased
// fringe onto its far end.
float LightSideShapeDistanceT(float d, float2 b, float scale)
{
    return max(-d / max(min(b.x, b.y) * scale, 1e-3), 0.0);
}

// No analytic field: coverage rides the vertex stream and is resolved to a screen-space antialiased edge here.
// Emitters of arbitrary triangles have no box to describe them, and a zero-sized box would read as distance 0
// across the whole surface — half coverage everywhere.
float LightSideVertexCoverage(float coverage)
{
    float edge = max(fwidth(coverage), 1e-4);
    return saturate((coverage - 0.5) / edge + 0.5);
}

// Coverage of one analytic shape quad, dispatched by the draw mode packed in TEXCOORD2.x — the ladder
// CoverageMode.cs mirrors: 0 fill, 1 stroke, 2 shadow, 3 inner shadow, 4 bevel, 5 noise,
// 6 vertex coverage.
//   p   local position relative to the shape centre       b    half-size
//   prm per-shape params (radii / ratios / counts)        aux  corner smoothing, generic aux or modifier chain head
//   style the kind's own style variant — corner style for a cornered kind, cap style for the arc
//   mp  mode params — fill (width of the stroke owning its outline, feather), stroke (width, align, -, dash
//       row or -1), shadow (offset.xy, blur, spread), inner shadow (offset.xy, blur), bevel (lightAngle,
//       width, strength, side), noise (frequency, seed, contrast), vertex coverage (coverage). A blur is twice
//       the standard deviation of the Gaussian it softens the edge by, as a CSS box-shadow reads it; a feather
//       fades the fill from opaque, a feather's width inside the outline, to nothing at it.
//   deformation  above 0.5 when aux heads the quad's modifier chain (LightSideShapeModifiers.hlsl)
//   paint, paintScale  the TEXCOORD3 paint payload and the paint's scale/fit; a pattern paint is evaluated here
//                      and returned as its coverage in patternT, 0 for every other paint
// Every field this reads — the layer's own point, the inner shadow's offset point, each composite element and a
// pattern's element — goes through the one call site of evalShapePrimitiveSdf inside the loop below, so the
// program holds a single copy of the primitive library. A dashed or trimmed stroke, and a ZigZag modifier, read
// the outline coordinate through that same call. The field the layer's coverage was resolved from comes back
// through `d`, so a distance paint reuses this evaluation instead of paying for a second one.
// A composite element's op lane carries op + 8 * prefix: the prefix of the modifier chain it is read through,
// zero for the whole chain and n + 1 for its first n links, which is how a cut escapes the modifiers standing
// below it. Consecutive elements reading one prefix from one point share a walk of the chain. An Offset grows
// what is folded below it and nothing above it, so the growth one element's prefix reads and the next one's
// does not comes off the fold before the next element joins it, and the last element's after the loop; a
// subtracted element enters the fold negated, so a ZigZag ridge moves its outline the opposite way. The
// antialias width follows the field's own gradient, which a warp steepens or flattens, but is held to a few
// pixels: a repeat's copies meet at seams where the field jumps, and the jump must not smear into a line.
float LightSideShapeCoverage(float kind, float mode, float2 p, float2 b, float4 prm, float aux,
                             float4 mp, float style, float deformation, float4 paint, float paintScale,
                             out float d, out float patternT)
{
    float result;
    patternT = 0.0;
#if defined(LIGHTSIDE_SHAPES)
    bool fieldless = mode > 5.5;
    bool shadow = mode > 1.5 && mode < 2.5;
    bool inner  = mode > 2.5 && mode < 3.5;
    bool dashed = mode > 0.5 && mode < 1.5 && mp.w > -0.5;
    bool composite = kind > 19.5;
    bool pattern = fmod(paint.w, 16.0) > 6.5;

    // The drop shadow is the field carried by its offset, so it reads the field there and nowhere else.
    float2 p0 = shadow ? p - mp.xy : p;
    float2 p1 = p - mp.xy;
    bool modified = deformation > 0.5;
    float fieldAux = aux;
    UNITY_BRANCH
    if (modified)
        fieldAux = LIGHTSIDE_SAMPLE_SHAPE_VERTS(0.5 / UI_SDF_VERT_TEX_WIDTH, LightSideShapeRowV(aux - 1.0)).x;

    LightSidePatternCell cell = (LightSidePatternCell)0;
    UNITY_BRANCH
    if (pattern) cell = evalShapePatternCell(paint.xy, LightSideShapeRowV(floor(paint.w * 0.00390625)));

    float2 s = composite ? max(b, 1e-4) / max(prm.zw, 1e-4) : float2(1.0, 1.0);
    float scale = min(s.x, s.y);
    float rowV = LightSideShapeRowV(prm.x);
    int elements = fieldless ? 0 : (composite ? min((int)(prm.y + 0.5), UI_SDF_MAX_COMPOSITE_ELEMENTS) : 1);
    int patternSlot = inner ? 2 * elements : elements;
    int evaluations = patternSlot + ((pattern && cell.empty < 0.5) ? 1 : 0);
    float shadowSpread = shadow ? mp.w : 0.0;

    float d0 = 0.0;
    float d1 = 0.0;
    float grow0 = 0.0;
    float grow1 = 0.0;
    float dPattern = 0.0;
    float along = -1.0;
    float total = 0.0;
    float nearest = 1e30;
    float walked = -1.0;
    float2 walkPoint = float2(0.0, 0.0);
    float walkGrow = 0.0;
    bool walkRidged = false;
    [loop] for (int i = 0; i < UI_SDF_MAX_EVALUATIONS; i++)
    {
        if (i >= evaluations) break;
        bool patternEval = i >= patternSlot;
        bool innerEval = !patternEval && i >= elements;
        int element = innerEval ? i - elements : i;
        bool reads = modified && !patternEval;

        float elementKind = kind;
        float2 elementPoint = innerEval ? p1 : p0;
        float2 elementBounds = b;
        float4 elementParams = prm;
        float elementAux = fieldAux;
        float elementStyle = style;
        float elementScale = 1.0;
        float op = 0.0;
        float blend = 0.0;
        float morph = 0.0;
        float seam = 0.0;
        float prefix = 0.0;
        float uBase = float(element * UI_SDF_COMPOSITE_STRIDE);
        float4 t0 = float4(0.0, 0.0, 0.0, 0.0);
        if (composite && !patternEval)
        {
            t0 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 0.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
            prefix = floor(t0.y * 0.125);
        }
        float key = innerEval ? prefix + 4096.0 : prefix;
        UNITY_BRANCH
        if (reads && key != walked)
        {
            walkPoint = LightSideApplyShapeModifiers(aux, prefix, elementPoint, walkGrow, walkRidged);
            walked = key;
        }
        bool ridged = reads && walkRidged;
        float elementGrow = reads ? walkGrow : 0.0;
        if (reads) elementPoint = walkPoint;

        if (patternEval)
        {
            // The curve is measured on the surface itself, not in the paint's frame: the frame's offset and
            // scale carry the lattice, so the elements travel while the falloff they pass through stands still.
            // Its angle is the exception — the curve faces the way the paint was turned, which is why the axis
            // travels with the pattern.
            float2 anchor = p / max(b, 1e-4);
            float drive = fmod(floor(paint.w * 0.015625), 4.0);
            float driveT = LightSideShapeDistanceT(d0 * scale - grow0 - shadowSpread, b, paintScale);
            if (drive < 0.5) driveT = length(anchor);
            else if (drive < 1.5) driveT = dot(anchor, cell.axis) * 0.5 + 0.5;
            driveT = LightSideSpreadWrap(fmod(floor(paint.w * 0.0625), 4.0), driveT);
            elementScale = evalShapePatternSize(cell.rowV, driveT);
            float4 cell0 = LIGHTSIDE_SAMPLE_SHAPE_VERTS(0.5 / UI_SDF_VERT_TEX_WIDTH, cell.rowV);
            float4 cell2 = LIGHTSIDE_SAMPLE_SHAPE_VERTS(2.5 / UI_SDF_VERT_TEX_WIDTH, cell.rowV);
            elementKind = min(cell0.x, 17.0);
            elementPoint = cell.turned / elementScale - cell2.zw;
            elementBounds = cell2.xy;
            elementParams = LIGHTSIDE_SAMPLE_SHAPE_VERTS(1.5 / UI_SDF_VERT_TEX_WIDTH, cell.rowV);
            elementAux = cell0.y;
            elementStyle = cell0.z;
        }
        else if (composite)
        {
            float4 t1 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 1.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
            float4 t2 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 2.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
            float4 t3 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 3.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
            float2 q = elementPoint / s;
            seam = floor(t2.w * 0.25);
            elementPoint = float2(t1.z * q.x + t1.w * q.y, -t1.w * q.x + t1.z * q.y) + t1.xy;
            elementKind = t0.x;
            op = t0.y - prefix * 8.0;
            blend = t0.z;
            elementAux = t0.w;
            elementBounds = t2.xy;
            morph = t2.z;
            elementStyle = t2.w - seam * 4.0;
            elementParams = t3;
        }

        float alongI, totalI;
        float di = evalShapePrimitiveSdf(elementKind, elementPoint, elementBounds, elementParams, elementAux,
                                         elementStyle, ((dashed && !innerEval) || ridged) && !patternEval,
                                         alongI, totalI);
        if (patternEval)
            dPattern = di * elementScale;
        else
        {
            float facing = (element > 0 && abs(op - 1.0) < 0.5) ? -1.0 : 1.0;
            UNITY_BRANCH
            if (ridged) di -= facing * LightSideShapeZigZag(aux, prefix, alongI, totalI) / scale;
            float folded = di;
            float grownBelow = ((innerEval ? grow1 : grow0) - elementGrow) / scale;
            UNITY_BRANCH
            if (element > 0) folded = sdShapeFold((innerEval ? d1 : d0) - grownBelow, di, op, blend, morph, seam);
            if (innerEval)
            {
                d1 = folded;
                grow1 = elementGrow;
            }
            else
            {
                d0 = folded;
                grow0 = elementGrow;
                if (abs(di) <= nearest)
                {
                    nearest = abs(di);
                    along = alongI;
                    total = totalI;
                }
            }
        }
    }

    d0 = d0 * scale - grow0;
    d1 = d1 * scale - grow1;
    if (along >= 0.0)
    {
        along *= scale;
        total *= scale;
    }
    d = d0 - shadowSpread;
    if (evaluations > patternSlot) patternT = saturate(0.5 - dPattern / max(cell.aa, 1e-6));

    if (fieldless)
        result = LightSideVertexCoverage(mp.x);
    else
    {
        float aa = clamp(fwidth(d0), 1e-4, 4.0 * (fwidth(p.x) + fwidth(p.y)) + 1e-4);

        if (mode < 0.5)
        {
            // mp.x carries the width of an opaque stroke that lies fully inside this fill's outline and
            // so draws that outline itself; zero where there is none. Both antialias the one edge, and
            // source-over composites their ramps to 1 - (1 - s)(1 - f), more coverage than either alone,
            // which reads as a fringe of the fill's colour around a stroke darker than it. Dividing the
            // stroke's coverage back out of the fill's leaves the two compositing to exactly the fill's
            // own: the stroke takes the whole of the edge and the fill only what the stroke does not
            // reach. It is exact at every width and every scale, a stroke thinner than its pixel included,
            // because it reconstructs the stroke's ramp rather than assuming the stroke saturates.
            float halfW = mp.x * 0.5;
            float over  = mp.x > 0.0 ? saturate(0.5 - (abs(d0 + halfW) - halfW) / aa) : 0.0;
            float edge  = mp.y > 0.0 ? LightSideGaussianEdge(d0 + mp.y * 0.5, max(mp.y * 0.25, aa * 0.4))
                                     : saturate(0.5 - d0 / aa);
            result = saturate((edge - over) / max(1.0 - over, 1e-4));
        }
        else if (mode < 1.5)
        {
            float halfW = mp.x * 0.5;
            float off   = mp.y * halfW;

            // A stroke aligned fully outside lands its inner bound on the outline of whatever it rings,
            // which is antialiased at the very same place. Composited, the two ramps leave
            // 1 - (1 - s)(1 - f) of coverage, and the shortfall reads as a hairline of background. It
            // vanishes only where one ramp is already saturated, so the bound moves by a whole pixel
            // rather than half of one. The move is inward, under the layer it rings, so the stroke's
            // own outline does not grow; mp.z marks that there is such a layer to hide it. The move is
            // measured in pixels and so holds at any distance, and it fades out as the bound leaves the
            // outline — a centred stroke moves by nothing.
            float grow = mp.z * saturate(mp.y) * max(0.0, aa - abs(off - halfW));
            off   -= grow * 0.5;
            halfW += grow * 0.5;

            float sd = abs(d0 - off) - halfW;
            UNITY_BRANCH
            if (dashed && along >= 0.0)
            {
                int row = (int)(mp.w + 0.5);
                float4 dash = _LightSideDashTable.Load(int3(0, row, 0));
                float4 trim = _LightSideDashTable.Load(int3(1, row, 0));
                sd = LightSideStrokeDashField(sd, along, total, halfW, dash, trim);
            }
            result = saturate(0.5 - sd / aa);
        }
        else if (shadow)
        {
            result = LightSideGaussianEdge(d, max(mp.z * 0.5, aa * 0.4));
        }
        else if (inner)
        {
            float inside    = saturate(0.5 - d0 / aa);
            float shadowAmt = 1.0 - LightSideGaussianEdge(d1, max(mp.z * 0.5, aa * 0.4));
            result = inside * shadowAmt;
        }
        else if (mode < 4.5)
        {
            // Bevel: rim light from the field's local-space normal against the light direction.
            // side +1 lights toward the light, -1 away.
            float2 g = LightSideFieldNormal(d0, p);
            float2 L = float2(cos(mp.x), sin(mp.x));
            float lit = dot(g, L) * mp.w;
            float profile = saturate(1.0 + d0 / max(mp.y, aa));   // 1 at edge -> 0 at width inside
            float shapeMask = saturate(0.5 - d0 / aa);
            result = shapeMask * profile * saturate(lit) * mp.z;
        }
        else
        {
            // Noise: procedural value noise masked by the shape.
            float nz = uiSdfValueNoise(p * mp.x + mp.y);
            nz = saturate((nz - 0.5) * mp.z + 0.5);
            result = saturate(0.5 - d0 / aa) * nz;
        }
    }
#else
    if (mode > 5.5)
    {
        d = 0.0;
        result = LightSideVertexCoverage(mp.x);
    }
    else
    {
        d = sdRoundedBox(p, b, prm.yzxw, 0.0);
        result = saturate(0.5 - d / max(fwidth(d), 1e-4));
    }
#endif
    return result;
}

#endif // LIGHTSIDE_SHAPE_SURFACE_INCLUDED
