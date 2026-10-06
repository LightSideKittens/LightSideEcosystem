// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
#ifndef LIGHTSIDE_SHAPE_FIELD_INCLUDED
#define LIGHTSIDE_SHAPE_FIELD_INCLUDED

// Analytic 2D signed-distance primitives for every LightSide analytic surface.
// Each returns the exact signed distance from p (shape-local, origin-centred) to the outline:
// negative inside, positive outside, magnitude in p's units. Formulas after Inigo Quilez
// (iquilezles.org/articles/distfunctions2d), ported GLSL -> HLSL. The fragment stage dispatches
// through evalShapePrimitiveSdf from the one evaluation loop in LightSideShapeSurface.hlsl; the kind ids
// mirror ShapeKind.cs.

#define UI_SDF_MAX_POLY_VERTS 64
#define UI_SDF_VERT_TEX_WIDTH 32
#define UI_SDF_MAX_COMPOSITE_ELEMENTS 8
#define UI_SDF_COMPOSITE_STRIDE 4
#define UI_SDF_MAX_EVALUATIONS (2 * UI_SDF_MAX_COMPOSITE_ELEMENTS + 1)

// Polygon outlines are fetched through LIGHTSIDE_SAMPLE_SHAPE_VERTS, defined by the including pipeline
// header against its own sampler — the legacy tex2D intrinsics this used to call do not exist in an
// HLSLPROGRAM, so declaring the texture here would restrict the file to the built-in pipeline.
#ifndef LIGHTSIDE_SAMPLE_SHAPE_VERTS
    #error "Include a LightSide pipeline header (LightSideGlyphField.cginc or LightSideGlyphFieldURP.hlsl) before LightSideShapeField.hlsl."
#endif

#include "LightSideShapeParams.hlsl"

#define UI_SDF_PATTERN_CURVE_SAMPLES 64.0

// GLSL mod(): x - y*floor(x/y). HLSL fmod() truncates toward zero and differs for negative x,
// which the angular folds below depend on.
float sdShapeMod(float a, float b) { return a - b * floor(a / b); }

float sdShapeCircle(float2 p, float r)
{
    return length(p) - r;
}

// Unsigned distance to segment a-b. Capsule = this minus the radius.
float sdShapeSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float lengthSquared = dot(ba, ba);
    float h = lengthSquared > 0.0 ? saturate(dot(pa, ba) / lengthSquared) : 0.0;
    return length(pa - ba * h);
}

float sdShapeCapsule(float2 p, float2 a, float2 b, float r)
{
    return sdShapeSegment(p, a, b) - r;
}

// Axis-aligned ellipse with radii ab. Exact nearest-point solve.
float sdShapeEllipse(float2 p, float2 ab)
{
    if (abs(ab.x - ab.y) < 1e-4) return length(p) - min(ab.x, ab.y);
    p = abs(p);
    if (p.x > p.y) { p = p.yx; ab = ab.yx; }
    float l = ab.y * ab.y - ab.x * ab.x;
    float m = ab.x * p.x / l;
    float m2 = m * m;
    float n = ab.y * p.y / l;
    float n2 = n * n;
    float c = (m2 + n2 - 1.0) / 3.0;
    float c3 = c * c * c;
    float q = c3 + m2 * n2 * 2.0;
    float d = c3 + m2 * n2;
    float g = m + m * n2;
    float co;
    if (d < 0.0)
    {
        float h = acos(q / c3) / 3.0;
        float s = cos(h);
        float t = sin(h) * 1.7320508;
        float rx = sqrt(-c * (s + t + 2.0) + m2);
        float ry = sqrt(-c * (s - t + 2.0) + m2);
        co = (ry + sign(l) * rx + abs(g) / (rx * ry) - m) / 2.0;
    }
    else
    {
        float h = 2.0 * m * n * sqrt(d);
        float s = sign(q + h) * pow(abs(q + h), 1.0 / 3.0);
        float u = sign(q - h) * pow(abs(q - h), 1.0 / 3.0);
        float rx = -s - u - c * 4.0 + 2.0 * m2;
        float ry = (s - u) * 1.7320508;
        float rm = sqrt(rx * rx + ry * ry);
        co = (ry / sqrt(rm - rx) + 2.0 * g / rm - m) / 2.0;
    }
    float2 r = ab * float2(co, sqrt(1.0 - co * co));
    return length(r - p) * sign(p.y - r.y);
}

// Equilateral triangle, apex up. r is half the base: the outline spans 2r x sqrt(3)*r around the
// centroid, so its box centre sits r/(2*sqrt(3)) above the origin.
float sdShapeEquilateralTriangle(float2 p, float r)
{
    float k = 1.7320508;
    p.x = abs(p.x) - r;
    p.y = p.y + r / k;
    if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) * 0.5;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

// Regular pentagon, flat edge up. r is the apothem (centre to edge); the circumradius is r/cos(36deg).
float sdShapePentagon(float2 p, float r)
{
    float3 k = float3(0.809016994, 0.587785252, 0.726542528);
    p.x = abs(p.x);
    p -= 2.0 * min(dot(float2(-k.x, k.y), p), 0.0) * float2(-k.x, k.y);
    p -= 2.0 * min(dot(float2( k.x, k.y), p), 0.0) * float2( k.x, k.y);
    p -= float2(clamp(p.x, -r * k.z, r * k.z), r);
    return length(p) * sign(p.y);
}

// Regular hexagon, flat edges up and down. r is the apothem; the circumradius r/cos(30deg) spans the width.
float sdShapeHexagon(float2 p, float r)
{
    float3 k = float3(-0.866025404, 0.5, 0.577350269);
    p = abs(p);
    p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
    p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
    return length(p) * sign(p.y);
}

// Regular octagon, flat edges up and down. r is the apothem, and the box it spans is exactly 2r x 2r.
float sdShapeOctagon(float2 p, float r)
{
    float3 k = float3(-0.9238795325, 0.3826834323, 0.4142135623);
    p = abs(p);
    p -= 2.0 * min(dot(float2( k.x, k.y), p), 0.0) * float2( k.x, k.y);
    p -= 2.0 * min(dot(float2(-k.x, k.y), p), 0.0) * float2(-k.x, k.y);
    p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
    return length(p) * sign(p.y);
}

// n-pointed star, one point up. r = outer radius, n = point count, m in (1,n] = sharpness (2 = deepest).
float sdShapeStar(float2 p, float r, float n, float m)
{
    float an = 3.1415926 / n;
    float en = 3.1415926 / m;
    float2 acs = float2(cos(an), sin(an));
    float2 ecs = float2(cos(en), sin(en));
    float bn = sdShapeMod(atan2(p.x, p.y), 2.0 * an) - an;
    p = length(p) * float2(cos(bn), abs(sin(bn)));
    p -= r * acs;
    p += ecs * clamp(-dot(p, ecs), 0.0, r * acs.y / ecs.y);
    return length(p) * sign(p.x);
}

// Pie / circular sector. c = (sin,cos) of the half-aperture, r = radius.
float sdShapePie(float2 p, float2 c, float r)
{
    p.x = abs(p.x);
    float l = length(p) - r;
    float m = length(p - c * clamp(dot(p, c), 0.0, r));
    return max(l, m * sign(c.y * p.x - c.x * p.y));
}

// Circular arc band. sc = (sin,cos) of the half-aperture, ra = arc radius, rb = half-thickness.
float sdShapeArc(float2 p, float2 sc, float ra, float rb)
{
    p.x = abs(p.x);
    return ((sc.y * p.x > sc.x * p.y) ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
}

// Arc band cut straight across at both ends: the annulus kept where the sector opens, so each end is a
// radial edge instead of the swept disk's semicircular cap. sc = (sin,cos) of the half-aperture.
float sdShapeArcFlat(float2 p, float2 sc, float ra, float rb)
{
    p.x = abs(p.x);
    float band = abs(length(p) - ra) - rb;
    float m = length(p - sc * max(dot(p, sc), 0.0));
    return max(band, m * sign(sc.y * p.x - sc.x * p.y));
}

// Annulus / ring: circle of radius r with half-thickness w.
float sdShapeRing(float2 p, float r, float w)
{
    return abs(length(p) - r) - w;
}

// Disk of radius r cut by a horizontal chord at height h.
float sdShapeCutDisk(float2 p, float r, float h)
{
    float w = sqrt(r * r - h * h);
    p.x = abs(p.x);
    float s = max((h - r) * p.x * p.x + w * w * (h + r - 2.0 * p.y), h * p.x - w * p.y);
    return (s < 0.0) ? length(p) - r : (p.x < w) ? h - p.y : length(p - float2(w, h));
}

// Parallelogram (iq): wi = half-length of the horizontal edges, he = half-height, sk = how far the centre of the
// top edge sits to the right of the origin (the bottom edge's sits as far to the left).
float sdShapeParallelogram(float2 p, float wi, float he, float sk)
{
    float2 e = float2(sk, he);
    p = (p.y < 0.0) ? -p : p;
    float2 w = p - e; w.x -= clamp(w.x, -wi, wi);
    float2 d = float2(dot(w, w), -w.y);
    float s = p.x * e.y - p.y * e.x;
    p = (s < 0.0) ? -p : p;
    float2 v = p - float2(wi, 0.0); v -= e * clamp(dot(v, e) / max(dot(e, e), 1e-8), -1.0, 1.0);
    d = min(d, float2(dot(v, v), wi * he - abs(s)));
    return sqrt(d.x) * sign(-d.y);
}

// Isosceles trapezoid (iq): r1 = bottom half-width, r2 = top half-width, he = half-height.
float sdShapeTrapezoid(float2 p, float r1, float r2, float he)
{
    float2 k1 = float2(r2, he);
    float2 k2 = float2(r2 - r1, 2.0 * he);
    p.x = abs(p.x);
    float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
    float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / max(dot(k2, k2), 1e-8), 0.0, 1.0);
    float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
    return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
}

// Rhombus (iq): b = half-extents of the box its four points span.
float sdShapeRhombus(float2 p, float2 b)
{
    b.y = -b.y;
    p = abs(p);
    float h = clamp((dot(b, p) + b.y * b.y) / max(dot(b, b), 1e-8), 0.0, 1.0);
    p -= b * float2(h, h - 1.0);
    return length(p) * sign(p.x);
}

// Plus / cross (iq): b = (half-length of an arm, half-thickness of an arm), r shifts the outline, so a
// negative r rounds every corner it turns on — the convex tips and the concave armpits alike. Exact
// outside; inside it is a bound, which a bevel or inner shadow reading the interior distance can show.
float sdShapeCross(float2 p, float2 b, float r)
{
    p = abs(p);
    p = (p.y > p.x) ? p.yx : p.xy;
    float2 q = p - b;
    float k = max(q.y, q.x);
    float2 w = (k > 0.0) ? q : float2(b.y - p.x, -k);
    return sign(k) * length(max(w, 0.0)) + r;
}

// Heart (iq), in its own units: two lobes on circles of radius sqrt(2)/4 about (+-0.25, 0.75) over a V
// closing at the origin, so it spans x in +-0.60355 and y in 0..1.10355 and the origin is its point.
float sdShapeHeart(float2 p)
{
    p.x = abs(p.x);
    if (p.y + p.x > 1.0) return length(p - float2(0.25, 0.75)) - 0.35355339;
    float2 v = p - 0.5 * max(p.x + p.y, 0.0);
    return sqrt(min(dot(p - float2(0.0, 1.0), p - float2(0.0, 1.0)), dot(v, v))) * sign(p.x - p.y);
}

// What a corner style takes out of the corner at C, whose two edges leave it along the unit directions e1
// and e2, for a radius r: the half-plane a bevel cuts, or the disk a scoop bites. Positive outside what is
// left, so a max() against the outline carves it. The bevel meets the edges where a fillet of the same
// radius would touch them, so switching style keeps the corner the same size.
float sdShapeCornerCarve(float2 p, float2 C, float2 e1, float2 e2, float r, float style)
{
    if (style > 1.5) return r - length(p - C);
    float2 sum = e1 + e2;
    float2 u = -sum / max(length(sum), 1e-5);
    float c = -dot(e1, u);
    float s = sqrt(max(1.0 - c * c, 1e-6));
    return dot(p - C, u) + r * c * c / s;
}

// Folds q into the wedge around the nearest corner of a regular n-gon whose first corner stands at `phase`
// radians, leaving that corner on the +x axis — one carve there is the carve at every corner.
float2 sdShapeFoldCorner(float2 q, float n, float phase)
{
    float step = 6.28318531 / n;
    float a = sdShapeMod(atan2(q.y, q.x) - phase + step * 0.5, step) - step * 0.5;
    return length(q) * float2(cos(a), sin(a));
}

// Carves every corner of a regular n-gon of circumradius R, given the outline's distance d at full size.
float sdShapeNgonCarved(float d, float2 q, float R, float n, float phase, float r, float style)
{
    float2 f = sdShapeFoldCorner(q, n, phase);
    float2 next = R * float2(cos(6.28318531 / n), sin(6.28318531 / n));
    float2 e = normalize(next - float2(R, 0.0) + float2(-1e-5, 1e-5));
    return max(d, sdShapeCornerCarve(f, float2(R, 0.0), e, float2(e.x, -e.y), r, style));
}

// Rounded rectangle (the default). b = half-extents; r = per-corner radii in (TR, BR, TL, BL)
// order; smoothing 0 = circular corners, 1 = squircle (superellipse ~L4).
#include "LightSideRoundedRect.hlsl"

// Per-corner radii arrive as (TL, TR, BR, BL); sdRoundedBox wants (TR, BR, TL, BL). A bevel or a scoop
// leaves the rect at full size and carves its corner instead, each corner by its own radius; smoothing is
// the round style's own shape and plays no part in the other two.
float evalRoundedRect(float2 p, float2 b, float4 radii, float smoothing, float style)
{
    float4 r = float4(radii.y, radii.z, radii.x, radii.w);
    if (style < 0.5) return sdRoundedBox(p, b, r, smoothing);

    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    float rc = (p.y > 0.0) ? r.x : r.y;
    float2 q = abs(p) - b;
    float box = min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
    return max(box, sdShapeCornerCarve(abs(p), b, float2(-1.0, 0.0), float2(0.0, -1.0), rc, style));
}

// Stadium filling the box: semicircular caps on the longer axis.
float evalCapsule(float2 p, float2 b)
{
    float radius = min(b.x, b.y);
    return length(max(abs(p) - b + radius, 0.0)) - radius;
}

// Parallelogram filling the box: t = tan of the skew, positive leaning the top to the right. The top edge's offset
// is held to the half-width so the horizontal edges never invert; past that the outline is a needle at the box's
// own slope. Rounding insets the outline exactly — horizontal edges by r, slanted ones by r over the cosine of
// their lean — and takes r off the distance, so every edge stays where it was.
float evalParallelogram(float2 p, float2 b, float t, float r, float style)
{
    float he = b.y;
    float sk = clamp(he * t, -b.x, b.x);
    float te = sk / max(he, 1e-5);
    if (style < 0.5)
    {
        float he2 = max(he - r, 0.0);
        float wi2 = max(b.x - abs(sk) - r * sqrt(1.0 + te * te), 0.0);
        return sdShapeParallelogram(p, wi2, he2, te * he2) - r;
    }

    float wi = max(b.x - abs(sk), 0.0);
    float d = sdShapeParallelogram(p, wi, he, sk);
    float2 q = (p.y < 0.0) ? -p : p;
    float2 e = normalize(float2(-sk, -he) + float2(0.0, -1e-5));
    d = max(d, sdShapeCornerCarve(q, float2(sk + wi, he), float2(-1.0, 0.0), e, r, style));
    return max(d, sdShapeCornerCarve(q, float2(sk - wi, he), float2(1.0, 0.0), e, r, style));
}

// Isosceles trapezoid filling the box: t = tan of the taper, positive narrowing the top, negative the bottom; the
// wide edge spans the box and the narrow one closes into a point once the lean asks for more than the half-width.
// Rounding insets the outline exactly, the way the parallelogram's does; where the inset slanted sides meet
// short of the inset top line (or bottom one) the inner outline is the triangle they close.
float evalTrapezoid(float2 p, float2 b, float t, float r, float style)
{
    float he = b.y;
    float lose = min(2.0 * he * abs(t), b.x);
    float r1 = b.x - ((t < 0.0) ? lose : 0.0);
    float r2 = b.x - ((t > 0.0) ? lose : 0.0);
    float result;
    if (style > 0.5)
    {
        float2 q = float2(abs(p.x), p.y);
        float2 down = normalize(float2(r1 - r2, -2.0 * he) + float2(0.0, -1e-5));
        float2 up = float2(-down.x, -down.y);
        float2 top = (r2 > 1e-4) ? float2(-1.0, 0.0) : float2(-down.x, down.y);
        float2 bottom = (r1 > 1e-4) ? float2(-1.0, 0.0) : float2(-up.x, up.y);
        result = max(max(sdShapeTrapezoid(p, r1, r2, he),
                         sdShapeCornerCarve(q, float2(r2, he), top, down, r, style)),
                     sdShapeCornerCarve(q, float2(r1, -he), bottom, up, r, style));
    }
    else
    {
        float mm = (r2 - r1) / max(2.0 * he, 1e-5);
        float sh = r * sqrt(1.0 + mm * mm);
        float yb = -he + r, yt = he - r;
        float xb = r1 + r * mm - sh;
        float xt = r1 + (2.0 * he - r) * mm - sh;
        float ya = -he + (sh - r1) / (mm + ((mm < 0.0) ? -1e-6 : 1e-6));
        if (xt < 0.0) { yt = ya; xt = 0.0; }
        if (xb < 0.0) { yb = ya; xb = 0.0; }
        float he2 = max(yt - yb, 0.0) * 0.5;
        result = sdShapeTrapezoid(p - float2(0.0, (yb + yt) * 0.5), xb, xt, he2) - r;
    }
    return result;
}

// Rhombus filling the box. Rounding offsets its four edges inward, which moves each half-extent by the
// radius over the sine of that edge's lean, and takes the radius back off the distance.
float evalRhombus(float2 p, float2 b, float r, float style)
{
    float result;
    if (style > 0.5)
    {
        float2 q = abs(p);
        float2 ex = normalize(float2(-b.x, b.y) + float2(-1e-5, 1e-5));
        float2 ey = normalize(float2(b.x, -b.y) + float2(1e-5, -1e-5));
        result = max(max(sdShapeRhombus(p, b),
                         sdShapeCornerCarve(q, float2(b.x, 0.0), ex, float2(ex.x, -ex.y), r, style)),
                     sdShapeCornerCarve(q, float2(0.0, b.y), ey, float2(-ey.x, ey.y), r, style));
    }
    else
    {
        float diag = length(b);
        float2 inner = max(b - r * float2(diag / max(b.y, 1e-5), diag / max(b.x, 1e-5)), 0.0);
        result = sdShapeRhombus(p, inner) - r;
    }
    return result;
}

// Plus filling the box, sized by the inscribed square: b = (half-size, half-size * thickness). Rounding
// shrinks the arms and gives the radius back, which rounds the tips and fillets the armpits together.
float evalCross(float2 p, float2 b, float r, float style)
{
    r = min(r, b.y);
    if (style < 0.5) return sdShapeCross(p, max(b - r, 0.0), -r);
    float2 q = abs(p);
    q = (q.y > q.x) ? q.yx : q.xy;
    float d = sdShapeCross(p, b, 0.0);
    return max(d, sdShapeCornerCarve(q, b, float2(0.0, -1.0), float2(-1.0, 0.0), r, style));
}

// Size of an outline whose box half-extents are `box` per unit size, inside the bounds b. The provider sends b
// as the outline's own bounds, so this returns exactly the size it resolved; after a per-layer inset shrinks b
// the outline refits inside it, aspect kept — an anisotropic fit would stop the field being a true distance and
// warp every stroke, shadow and bevel that measures it. Ratios come from ShapeFit.cs, which owns them.
float sdShapeFit(float2 b, float2 box)
{
    return min(b.x / box.x, b.y / box.y);
}

// Reads vertex i (stored normalized to -1..1) from row rowV of the vertex atlas and scales it to the shape's
// half-extents, so the polygon is sized/positioned from halfSize exactly like the analytic kinds.
float2 sdShapeFetchVert(int i, float rowV, float2 halfSize)
{
    float u = (float((uint)i / 2u) + 0.5) / UI_SDF_VERT_TEX_WIDTH;
    float4 t = LIGHTSIDE_SAMPLE_SHAPE_VERTS(u, rowV);
    float2 nv = ((i & 1) == 0) ? t.xy : t.zw;
    return nv * halfSize;
}

// Exact SDF of a simple polygon (winding-number sign), vertices streamed from the atlas (stored normalized to
// -1..1 and scaled by halfSize here, so per-layer padding / insets shrink the polygon like the analytic kinds).
// After Inigo Quilez sdPolygon. rowV = atlas row; count <= UI_SDF_MAX_POLY_VERTS.
float sdShapePolygon(float2 p, float2 halfSize, float rowV, int count, bool wantAlong, out float along,
                     out float total)
{
    along = -1.0;
    total = 0.0;
    float result;
    if (count < 3) result = length(p) - min(halfSize.x, halfSize.y);
    else
    {
        float2 v0 = sdShapeFetchVert(0, rowV, halfSize);
        float2 vj = v0;
        float d = dot(p - v0, p - v0);
        float s = 1.0;
        float run = 0.0;
        // A real loop, not an unrolled one: the vertex budget is the ceiling, not the count, so unrolling would
        // both compile the ceiling into the shader and make every outline pay it whatever it actually holds.
        // The outline coordinate, when wanted, is the length walked to the nearest segment — vertex order is
        // the walk — plus how far along that segment the nearest point lies.
        [loop] for (int i = 0; i < UI_SDF_MAX_POLY_VERTS; i++)
        {
            if (i >= count) break;
            float2 vi = i + 1 < count ? sdShapeFetchVert(i + 1, rowV, halfSize) : v0;
            float2 e = vj - vi;
            float2 w = p - vi;
            float h = clamp(dot(w, e) / max(dot(e, e), 1e-12), 0.0, 1.0);
            float2 bb = w - e * h;
            float dd = dot(bb, bb);
            if (wantAlong)
            {
                float len = sqrt(dot(e, e));
                if (dd <= d) along = run + (1.0 - h) * len;
                run += len;
            }
            d = min(d, dd);
            bool c1 = p.y >= vi.y;
            bool c2 = p.y <  vj.y;
            bool c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            vj = vi;
        }
        if (wantAlong) total = run;
        result = s * sqrt(d);
    }
    return result;
}

// One baked half-width multiplier from the profile row: four to a texel, in units of the widest the band
// gets, which the caller scales by.
float sdShapeFetchWidth(int i, float rowV)
{
    float4 t = LIGHTSIDE_SAMPLE_SHAPE_VERTS((float((uint)i / 4u) + 0.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
    uint c = (uint)i & 3u;
    return c == 0u ? t.x : (c == 1u ? t.y : (c == 2u ? t.z : t.w));
}

// Uneven capsule (iq): exact distance to the cone between a disk of radius r1 at the origin and one of
// radius r2 at (0, h). A taper steeper than the segment is long has no cone — one disk swallows the other —
// so the slope is held just inside that, which is the shape the geometry degenerates to anyway.
float sdShapeUnevenCapsule(float2 p, float r1, float r2, float h)
{
    p.x = abs(p.x);
    float b = clamp((r1 - r2) / max(h, 1e-6), -0.999, 0.999);
    float a = sqrt(1.0 - b * b);
    float k = dot(p, float2(-b, a));
    if (k < 0.0) return length(p) - r1;
    if (k > a * h) return length(p - float2(0.0, h)) - r2;
    return dot(p, float2(a, b)) - r1;
}

// An open chain of vertices stroked to a band of half-width w: the distance to the nearest segment, less that
// width. Round caps and round joins fall out of the minimum itself; a flat cap squares the two outer ends off
// against their own segment (a local cut, so a curl passing near an end is left alone), and a square cap runs
// those ends half a width past instead, along the taper's own slope. Each segment is the cone between its
// two half-widths, so a band that changes thickness stays an exact field; profileV below zero means one
// thickness the whole way and every cone is a capsule. Vertices are stored normalized to the band's own
// extent, so halfSize arrives with the widest half-width already taken back off.
float sdShapePolyline(float2 p, float2 halfSize, float rowV, int count, float w, float cap, float profileV)
{
    float w0 = w * ((profileV < 0.0) ? 1.0 : sdShapeFetchWidth(0, profileV));
    float2 vj = sdShapeFetchVert(0, rowV, halfSize);
    if (count < 2) return length(p - vj) - w0;

    int last = count - 1;
    float d = 1e6;
    [loop] for (int i = 1; i < UI_SDF_MAX_POLY_VERTS; i++)
    {
        if (i > last) break;
        float2 vi = sdShapeFetchVert(i, rowV, halfSize);
        float w1 = w * ((profileV < 0.0) ? 1.0 : sdShapeFetchWidth(i, profileV));
        float2 e = vi - vj;
        float len = max(length(e), 1e-6);
        float2 n = e / len;
        float2 q = float2(dot(p - vj, float2(-n.y, n.x)), dot(p - vj, n));

        float r0 = w0, r1 = w1, h = len;
        if (cap > 1.5)
        {
            float slope = (w1 - w0) / len;
            if (i == 1)    { r0 = max(w0 - slope * w0, 0.0); h += w0; q.y += w0; }
            if (i == last) { r1 = max(w1 + slope * w1, 0.0); h += w1; }
        }

        float di = sdShapeUnevenCapsule(q, r0, r1, h);
        if (cap > 0.5 && cap < 1.5)
        {
            if (i == 1)    di = max(di, -q.y);
            if (i == last) di = max(di, q.y - len);
        }
        d = min(d, di);
        vj = vi;
        w0 = w1;
    }
    return d;
}

// Primitive dispatch by ShapeKind (ids mirror ShapeKind.cs) — every kind except Composite, which is one of
// these per element and so cannot appear inside itself. Analytic kinds size themselves from the half-extents b, so
// Inset only has to shrink b. prm/aux are per-kind (PrimitiveShapeProvider.Resolve): a kind whose box is not square
// carries that box's ratios in prm, and with them how far the box rides above the primitive's origin — for the
// star, whose four slots are spoken for, the rise follows from the box height because its top spike reaches the
// full radius.
//
// aux carries the corner radius for every kind that has corners (the rounded rect reads it as its own corner
// smoothing instead). Rounding a field is subtracting: the outline is built one radius smaller and the radius is
// taken off its distance, which grows every corner into an arc of exactly that radius and leaves the outline
// where it was. A cornered kind was given that room by its fit, so it comes back out of b; one built on a circle
// spends it inward, off the radius the circle was drawn at; the parallelogram and trapezoid, which fill the box
// outright, inset their own edges.
float evalShapeAnalyticSdf(float kind, float2 p, float2 b, float4 prm, float aux, float style)
{
    float rmin = min(b.x, b.y);

    // A carving style leaves the outline at full size, so the fit reserved it no room to grow into and the
    // corner is cut out of the size it already has; the round style keeps building it smaller and dilating.
    float carve = (style > 0.5) ? 0.0 : aux;
    float2 bc = max(b - carve, 0.0);
    float rc = max(rmin - carve, 0.0);
    float result;

    if (kind < 0.5)       result = evalRoundedRect(p, b, prm, aux, style);
    else if (kind < 1.5)  result = sdShapeCircle(p, rmin);
    else if (kind < 2.5)  result = sdShapeEllipse(p, b);
    else if (kind < 3.5)  result = evalCapsule(p, b);
    else if (kind < 4.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float2 q = float2(p.x, p.y + prm.z * r);
        float d = sdShapeEquilateralTriangle(q, r) - carve;
        result = (style > 0.5) ? sdShapeNgonCarved(d, q, 1.15470054 * r, 3.0, 1.57079633, aux, style) : d;
    }
    else if (kind < 5.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float2 q = float2(p.x, p.y + prm.z * r);
        float d = sdShapePentagon(float2(q.x, -q.y), r) - carve;
        result = (style > 0.5) ? sdShapeNgonCarved(d, q, 1.23606798 * r, 5.0, 1.57079633, aux, style) : d;
    }
    else if (kind < 6.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float d = sdShapeHexagon(p, r) - carve;
        result = (style > 0.5) ? sdShapeNgonCarved(d, p, 1.15470054 * r, 6.0, 0.0, aux, style) : d;
    }
    else if (kind < 7.5)
    {
        float d = sdShapeOctagon(p, rc) - carve;
        result = (style > 0.5) ? sdShapeNgonCarved(d, p, 1.08239220 * rmin, 8.0, 0.39269908, aux, style) : d;
    }
    else if (kind < 8.5)  { float r = sdShapeFit(bc, prm.zw); result = sdShapeStar(float2(p.x, p.y + (1.0 - prm.w) * r), r, prm.x, prm.y) - aux; }
    // A wedge of no aperture encloses nothing: report a distance no falloff can reach rather than the seam a
    // zero-angle sector would otherwise leave along its own axis.
    else if (kind < 9.5)  result = prm.x <= 0.0 ? 1e6 : sdShapePie(p, float2(sin(prm.x), cos(prm.x)), rc) - aux;
    // The arc band is a swept disk, so its caps are semicircles of its own half-thickness already: it turns on
    // no corner a rounding could work on, and aux is left out the way the ring leaves it out.
    else if (kind < 10.5)
    {
        float ra = rmin * (1.0 - prm.y * 0.5), rb = rmin * prm.y * 0.5;
        // A square cap runs the band half a thickness past each end, which on an arc is the angle that arc
        // length subtends at its own radius; the sweep is held to the full turn it would otherwise pass.
        float half = min(prm.x + ((style > 1.5) ? rb / max(ra, 1e-5) : 0.0), 3.14159265);
        float2 sc = float2(sin(half), cos(half));
        result = (style > 0.5) ? sdShapeArcFlat(p, sc, ra, rb)
                               : sdShapeArc(p, float2(sin(prm.x), cos(prm.x)), ra, rb);
        if (prm.x <= 0.0) result = 1e6;
    }
    else if (kind < 11.5) result = sdShapeRing(p, rmin * (1.0 - prm.x * 0.5), rmin * prm.x * 0.5);
    else if (kind < 12.5) { float h = clamp((prm.x * 2.0 - 1.0) * rmin - aux, -rc, rc); result = sdShapeCutDisk(p, rc, h) - aux; }
    else if (kind < 13.5) result = evalParallelogram(p, b, prm.x, aux, style);
    else if (kind < 14.5) result = evalTrapezoid(p, b, prm.x, aux, style);
    else if (kind < 15.5) result = evalRhombus(p, b, aux, style);
    else if (kind < 16.5) result = evalCross(p, float2(rmin, rmin * prm.x), aux, style);
    else
    {
        float r = sdShapeFit(b, prm.xy);                  // heart
        result = sdShapeHeart(float2(p.x, p.y + prm.z * r) / max(r, 1e-5)) * r;
    }
    return result;
}

// ---- The outline coordinate ------------------------------------------------------------------------------
// Arc length along an outline to the point of it nearest p — clockwise on screen from the outline's topmost
// point on its own vertical axis — and the outline's whole length. The rounded rectangle, the capsule and the
// circular kinds are exact. The regular polygons, the star, the cross and the quadrilaterals measure a rounded
// corner as if it were sharp, which shifts a dash there by less than the corner's own radius; the ellipse takes
// the parameter of the point at p's own eccentric angle, exact on the outline itself. A vector outline measures
// from its first vertex, in its own winding. Read only by a stroke that dashes or trims, through the same call
// the field is read through, so the library is instantiated once.

// The helpers take p's clockwise angle from the top as `theta`, signed in (-pi, pi], computed once by the
// dispatcher; a helper measuring about another centre takes its own.

// The closed quadrilateral a -> b -> c -> d -> a walked from a: the nearest edge's coordinate.
float sdAlongQuad(float2 p, float2 a, float2 b, float2 c, float2 d, out float total)
{
    float best = 1e30, along = 0.0, run = 0.0;
    float2 v0 = a, v1 = b;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float2 e = v1 - v0;
        float len = max(length(e), 1e-6);
        float h = clamp(dot(p - v0, e) / (len * len), 0.0, 1.0);
        float2 q = p - v0 - e * h;
        float dd = dot(q, q);
        if (dd < best) { best = dd; along = run + h * len; }
        run += len;
        v0 = v1;
        v1 = (i == 0) ? c : ((i == 1) ? d : a);
    }
    total = run;
    return along;
}

// A regular polygon of `count` vertices on circumradius R, the first `phase` radians clockwise from the top,
// its corners rounded by `round`, walked clockwise from the middle of the first corner's arc; `shift` moves
// the origin along the walk. A rounded corner is an arc of that radius joining two edges, so a point beside
// one is placed by how far its corner has already turned and the coordinate keeps running through the corner
// instead of standing still across it.
float sdAlongNgon(float2 p, float theta, float R, float count, float phase, float shift, float round,
                  out float total)
{
    float stride = 6.28318531 / count;
    float edge = 2.0 * R * sin(stride * 0.5);
    float period = edge + round * stride;
    float k = floor(sdShapeMod(theta - phase, 6.28318531) / stride);
    float a0 = phase + k * stride, a1 = a0 + stride;
    float2 A = R * float2(sin(a0), cos(a0));
    float2 e = R * float2(sin(a1), cos(a1)) - A;
    float len = max(length(e), 1e-6);
    float2 dir = e / len;
    float h = clamp(dot(p - A, dir) / len, 0.0, 1.0);
    float2 off = p - (A + e * h);
    float turn = (round > 0.0) ? atan2(dot(off, dir), abs(dot(off, float2(dir.y, -dir.x)))) : 0.0;
    total = count * period;
    return sdShapeMod(k * period + round * (stride * 0.5 + turn) + h * edge + shift, total);
}

// The star of n points: outer vertices on R at even multiples of pi/n from the top, inner ones on Ri between,
// walked from the middle of the top point. Rounding by `round` offsets the outline: a vertex the outline turns
// out at becomes an arc of that radius, one it turns in at pulls its two edges together instead and loses the
// length an arc would have added. Both vertices are read by the same signed turn, so a blunt star — whose
// notches open outward like any polygon's corner — walks correctly as well.
float sdAlongStar(float2 p, float theta, float R, float Ri, float n, float round, out float total)
{
    float stride = 3.14159265 / n;
    float k = floor(sdShapeMod(theta, 6.28318531) / stride);
    float a0 = k * stride, a1 = a0 + stride;
    bool outerFirst = sdShapeMod(k, 2.0) < 0.5;
    float2 A = (outerFirst ? R : Ri) * float2(sin(a0), cos(a0));
    float2 e = (outerFirst ? Ri : R) * float2(sin(a1), cos(a1)) - A;
    float edge = max(length(e), 1e-6);
    float2 dir = e / edge;

    // One edge, the centre and the two vertices it joins make a triangle: the angle it subtends at the point
    // fixes how far the outline turns there, and the period owes the rest of its turn to the notch.
    float turnTip = 3.14159265 - 2.0 * asin(clamp(Ri * sin(stride) / edge, -1.0, 1.0));
    float turnNotch = 2.0 * stride - turnTip;
    // At most one of the two can turn inward, so one tangent covers whichever does.
    float bite = round * tan(-min(min(turnTip, turnNotch), 0.0) * 0.5);
    float arcTip = round * max(turnTip, 0.0);
    float arcNotch = round * max(turnNotch, 0.0);
    float run = max(edge - bite, 1e-6);
    float period = 2.0 * run + arcTip + arcNotch;
    total = n * period;

    float head = floor(k * 0.5) * period;
    float startMid = outerFirst ? head : head + arcTip * 0.5 + run + arcNotch * 0.5;
    float runStart = startMid + (outerFirst ? arcTip : arcNotch) * 0.5;
    float endMid = runStart + run + (outerFirst ? arcNotch : arcTip) * 0.5;

    float t = dot(p - A, dir);
    float2 off = p - (A + dir * clamp(t, 0.0, edge));
    float turn = atan2(dot(off, dir), max(abs(dot(off, float2(dir.y, -dir.x))), 1e-6));
    float turnStart = outerFirst ? turnTip : turnNotch;
    float along;
    if (t < 0.0)         along = startMid + ((turnStart > 0.0) ? round : 0.0) * turn;
    else if (t > edge)   along = endMid + (((outerFirst ? turnNotch : turnTip) > 0.0) ? round : 0.0) * turn;
    else                 along = runStart + clamp(t - ((turnStart < 0.0) ? bite : 0.0), 0.0, run);
    return sdShapeMod(along, total);
}

// The rounded rectangle: four edges and four corner arcs of radii r = (TL, TR, BR, BL), walked from the top
// edge's middle.
float sdAlongRoundedRect(float2 p, float2 b, float4 r, out float total)
{
    r = min(r, min(b.x, b.y));
    const float quarterTurn = 1.57079633;
    float lenT2 = b.x - r.y, lenR = 2.0 * b.y - r.y - r.z, lenB = 2.0 * b.x - r.z - r.w;
    float lenL = 2.0 * b.y - r.w - r.x, lenT1 = b.x - r.x;
    float sATR = lenT2, sR = sATR + quarterTurn * r.y, sABR = sR + lenR, sB = sABR + quarterTurn * r.z;
    float sABL = sB + lenB, sL = sABL + quarterTurn * r.w, sATL = sL + lenL, sT1 = sATL + quarterTurn * r.x;
    total = sT1 + lenT1;

    bool right = p.x >= 0.0, top = p.y >= 0.0;
    float rc = right ? (top ? r.y : r.z) : (top ? r.x : r.w);
    float2 d = abs(p) - (b - rc);
    float along;
    if (d.x > 0.0 && d.y > 0.0)
    {
        float phi = atan2(d.y, d.x);
        along = right ? (top ? sATR + rc * (quarterTurn - phi) : sABR + rc * phi)
                      : (top ? sATL + rc * phi : sABL + rc * (quarterTurn - phi));
    }
    else if (d.x > d.y)
        along = right ? sR + clamp(b.y - r.y - p.y, 0.0, lenR) : sL + clamp(p.y + b.y - r.w, 0.0, lenL);
    else if (top)
        along = right ? clamp(p.x, 0.0, lenT2) : sT1 + clamp(p.x + b.x - r.x, 0.0, lenT1);
    else
        along = sB + clamp(b.x - r.z - p.x, 0.0, lenB);
    return along;
}

// Arc length of the ellipse with semi-axes ab from its top, clockwise, to the parameter f in [0, pi/2]:
// Simpson over four intervals of the speed along the parameter.
float sdAlongEllipseQuarter(float2 ab, float f)
{
    float h = f * 0.25;
    float sum = 0.0;
    [unroll] for (int i = 0; i <= 4; i++)
    {
        float u = h * i;
        float c = cos(u), s = sin(u);
        float w = (i == 0 || i == 4) ? 1.0 : (((i & 1) == 1) ? 4.0 : 2.0);
        sum += w * sqrt(ab.x * ab.x * c * c + ab.y * ab.y * s * s);
    }
    return sum * h / 3.0;
}

// The whole ellipse is Ramanujan's second approximation, which the quarter integral agrees with to well
// under a thousandth, so a dash crossing a quadrant boundary meets a seam far below a pixel.
float sdAlongEllipse(float2 p, float2 ab, out float total)
{
    const float quarterTurn = 1.57079633;
    float a = ab.x, b = ab.y;
    total = 3.14159265 * (3.0 * (a + b) - sqrt((3.0 * a + b) * (a + 3.0 * b)));
    float quarter = total * 0.25;
    float phi = sdShapeMod(atan2(p.x / max(ab.x, 1e-5), p.y / max(ab.y, 1e-5)), 6.28318531);
    float q = floor(phi / quarterTurn);
    float f = phi - q * quarterTurn;
    bool odd = sdShapeMod(q, 2.0) > 0.5;
    return odd ? (q + 1.0) * quarter - sdAlongEllipseQuarter(ab, quarterTurn - f)
               : q * quarter + sdAlongEllipseQuarter(ab, f);
}

// The stadium: two straight edges and two semicircles, walked from the top of its box.
float sdAlongCapsule(float2 p, float2 b, out float total)
{
    const float halfTurn = 3.14159265;
    float along;
    if (b.x >= b.y)
    {
        float R = b.y, e = b.x - b.y;
        total = 4.0 * e + 2.0 * halfTurn * R;
        if (abs(p.x) <= e)
            along = (p.y >= 0.0) ? ((p.x >= 0.0) ? p.x : total + p.x) : e + halfTurn * R + (e - p.x);
        else
        {
            float ang = atan2(p.x - sign(p.x) * e, p.y);
            along = (p.x > 0.0) ? e + R * ang : 3.0 * e + halfTurn * R + R * (halfTurn + ang);
        }
    }
    else
    {
        float R = b.x, e = b.y - b.x;
        total = 4.0 * e + 2.0 * halfTurn * R;
        if (abs(p.y) <= e)
            along = (p.x >= 0.0) ? halfTurn * R * 0.5 + (e - p.y) : halfTurn * R * 1.5 + 3.0 * e + p.y;
        else if (p.y > 0.0)
        {
            float ang = atan2(p.x, p.y - e);
            along = (ang >= 0.0) ? R * ang : total + R * ang;
        }
        else
        {
            float ang = atan2(p.x, p.y + e);
            if (ang < 0.0) ang += 2.0 * halfTurn;
            along = halfTurn * R * 0.5 + 2.0 * e + R * (ang - halfTurn * 0.5);
        }
    }
    return along;
}

// The pie of half-aperture ap on radius R, its bisector up, walked from the arc's middle: arc, one edge in
// to the centre, the other edge out, the rest of the arc.
float sdAlongPie(float2 p, float theta, float R, float ap, out float total)
{
    total = 2.0 * R * (ap + 1.0);
    float2 dir = float2(sin(ap), cos(ap));
    float2 q = float2(abs(p.x), p.y);
    float t = clamp(dot(q, dir), 0.0, R);
    bool arc = abs(theta) <= ap && abs(length(p) - R) <= length(q - dir * t);
    return arc ? ((theta >= 0.0) ? R * theta : total + R * theta)
               : ((theta > 0.0) ? R * ap + (R - t) : R * ap + R + t);
}

// The arc band of mid radius ra, half-thickness rb and half-aperture ap, walked from the outer arc's middle:
// outer arc, one cap, inner arc back, the other cap. cap: 0 round, 1 flat, 2 square — a flat cap on a sweep run
// half a thickness past its end.
float sdAlongArc(float2 p, float theta, float ra, float rb, float ap, float cap, out float total)
{
    const float halfTurn = 3.14159265;
    if (cap > 1.5) ap = min(ap + rb / max(ra, 1e-5), halfTurn);
    float Ro = ra + rb, Ri = max(ra - rb, 0.0);
    float capLen = (cap > 0.5) ? 2.0 * rb : halfTurn * rb;
    total = 2.0 * ap * (Ro + Ri) + 2.0 * capLen;
    float along;
    if (abs(theta) <= ap)
    {
        if (length(p) >= ra) along = (theta >= 0.0) ? Ro * theta : total + Ro * theta;
        else along = Ro * ap + capLen + Ri * (ap - theta);
    }
    else
    {
        float2 q = float2(abs(p.x), p.y);
        float2 rad = float2(sin(ap), cos(ap));
        float within;
        if (cap > 0.5) within = clamp(Ro - dot(q, rad), 0.0, 2.0 * rb);
        else
        {
            float2 v = q - rad * ra;
            within = rb * clamp(atan2(dot(v, float2(rad.y, -rad.x)), dot(v, rad)), 0.0, halfTurn);
        }
        along = (theta > 0.0) ? Ro * ap + within : total - Ro * ap - within;
    }
    return along;
}

// The cut disk of radius R kept above the chord at height h, walked from the arc's top.
float sdAlongCutDisk(float2 p, float theta, float R, float h, out float total)
{
    float w = sqrt(max(R * R - h * h, 0.0));
    float phiE = atan2(w, h);
    total = 2.0 * R * phiE + 2.0 * w;
    float dChord = (abs(p.x) <= w) ? abs(p.y - h) : length(float2(abs(p.x) - w, p.y - h));
    bool arc = abs(theta) <= phiE && abs(length(p) - R) <= dChord;
    return arc ? ((theta >= 0.0) ? R * theta : total + R * theta) : R * phiE + (w - clamp(p.x, -w, w));
}

// The plus of arm half-length b.x and half-thickness b.y: eight wedges of one walk, every other one reversed.
float sdAlongCross(float2 p, float theta, float2 b, out float total)
{
    total = 8.0 * b.x;
    float w = floor(sdShapeMod(theta, 6.28318531) / 0.78539816);
    float2 q = abs(p);
    if (q.y > q.x) q = q.yx;
    float dSide = sdShapeSegment(q, float2(b.y, b.y), float2(b.x, b.y));
    float dEnd = sdShapeSegment(q, float2(b.x, b.y), float2(b.x, 0.0));
    float sw = (dSide <= dEnd) ? clamp(q.x - b.y, 0.0, b.x - b.y) : (b.x - b.y) + clamp(b.y - q.y, 0.0, b.y);
    return w * b.x + ((sdShapeMod(w, 2.0) < 0.5) ? b.x - sw : sw);
}

// The heart in its own units, walked from the notch between the lobes: right lobe, right edge down to the
// point, left edge, left lobe.
float sdAlongHeart(float2 p, out float total)
{
    const float lobe = 0.35355339, halfTurn = 3.14159265, edge = 0.70710678;
    total = 2.0 * halfTurn * lobe + 2.0 * edge;
    float2 q = float2(abs(p.x), p.y);
    float along;
    if (q.x + q.y > 1.0)
    {
        float2 v = q - float2(0.25, 0.75);
        along = lobe * clamp(2.35619449 - atan2(v.y, v.x), 0.0, halfTurn);
    }
    else
    {
        float t = clamp(dot(q - float2(0.5, 0.5), float2(-edge, -edge)), 0.0, edge);
        float2 onEdge = float2(0.5, 0.5) - float2(edge, edge) * t;
        along = (length(q - float2(0.0, 1.0)) < length(q - onEdge)) ? 0.0 : halfTurn * lobe + t;
    }
    return (p.x < 0.0) ? total - along : along;
}

// The outline coordinate of every analytic kind, on the same fits and frames evalShapeAnalyticSdf builds them
// in; a round corner style dilates the polygon it was inset for back out, the other styles leave it at size.
float evalShapeAnalyticAlong(float kind, float2 p, float2 b, float4 prm, float aux, float style, out float total)
{
    float rmin = min(b.x, b.y);
    float grow = (style > 0.5) ? 0.0 : aux;
    float2 bc = max(b - grow, 0.0);
    float theta = atan2(p.x, p.y);
    float along;
    if (kind < 0.5)       along = sdAlongRoundedRect(p, b, prm, total);
    else if (kind < 1.5)  { total = 6.28318531 * rmin; along = rmin * sdShapeMod(theta, 6.28318531); }
    else if (kind < 2.5)  along = sdAlongEllipse(p, b, total);
    else if (kind < 3.5)  along = sdAlongCapsule(p, b, total);
    else if (kind < 4.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float2 q = float2(p.x, p.y + prm.z * r);
        along = sdAlongNgon(q, atan2(q.x, q.y), 1.15470054 * r, 3.0, 0.0, 0.0, grow, total);
    }
    else if (kind < 5.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float2 q = float2(p.x, p.y + prm.z * r);
        along = sdAlongNgon(q, atan2(q.x, q.y), r / 0.80901699, 5.0, 0.0, 0.0, grow, total);
    }
    else if (kind < 6.5)
    {
        float R = sdShapeFit(bc, prm.xy) / 0.86602540;
        along = sdAlongNgon(p, theta, R, 6.0, -0.52359878, -(R * 0.5 + grow * 0.52359878), grow, total);
    }
    else if (kind < 7.5)
    {
        float R = max(rmin - grow, 0.0) / 0.92387953;
        along = sdAlongNgon(p, theta, R, 8.0, -0.39269908, -(R * 0.38268343 + grow * 0.39269908), grow,
                            total);
    }
    else if (kind < 8.5)
    {
        float r = sdShapeFit(bc, prm.zw);
        float an = 3.14159265 / prm.x, en = 3.14159265 / prm.y;
        float edge = r * sin(an) / max(sin(en), 1e-5);
        float2 q = float2(p.x, p.y + (1.0 - prm.w) * r);
        along = sdAlongStar(q, atan2(q.x, q.y), r, r * cos(an) - edge * cos(en), prm.x, grow, total);
    }
    else if (kind < 9.5)  along = sdAlongPie(p, theta, rmin, prm.x, total);
    else if (kind < 10.5) along = sdAlongArc(p, theta, rmin * (1.0 - prm.y * 0.5), rmin * prm.y * 0.5, prm.x, style, total);
    else if (kind < 11.5)
    {
        float R = (length(p) >= rmin * (1.0 - prm.x * 0.5)) ? rmin : rmin * (1.0 - prm.x);
        total = 6.28318531 * R;
        along = R * sdShapeMod(theta, 6.28318531);
    }
    else if (kind < 12.5) along = sdAlongCutDisk(p, theta, rmin, clamp((prm.x * 2.0 - 1.0) * rmin, -rmin, rmin), total);
    else if (kind < 13.5)
    {
        float he = b.y, sk = clamp(he * prm.x, -b.x, b.x), wi = max(b.x - abs(sk), 0.0);
        along = sdShapeMod(sdAlongQuad(p, float2(sk - wi, he), float2(sk + wi, he), float2(wi - sk, -he),
                                       float2(-sk - wi, -he), total) - wi, total);
    }
    else if (kind < 14.5)
    {
        float he = b.y, lose = min(2.0 * he * abs(prm.x), b.x);
        float r1 = b.x - ((prm.x < 0.0) ? lose : 0.0), r2 = b.x - ((prm.x > 0.0) ? lose : 0.0);
        along = sdShapeMod(sdAlongQuad(p, float2(-r2, he), float2(r2, he), float2(r1, -he), float2(-r1, -he),
                                       total) - r2, total);
    }
    else if (kind < 15.5) along = sdAlongQuad(p, float2(0.0, b.y), float2(b.x, 0.0), float2(0.0, -b.y),
                                              float2(-b.x, 0.0), total);
    else if (kind < 16.5) along = sdAlongCross(p, theta, float2(rmin, rmin * prm.x), total);
    else
    {
        float r = sdShapeFit(b, prm.xy);
        along = sdAlongHeart(float2(p.x, p.y + prm.z * r) / max(r, 1e-5), total) * r;
        total *= r;
    }
    return along;
}

// The outlines that stream their vertices from the atlas, over the analytic ones.
float evalShapePrimitiveSdf(float kind, float2 p, float2 b, float4 prm, float aux, float style,
                            bool wantAlong, out float along, out float total)
{
    float carve = (style > 0.5) ? 0.0 : aux;
    float result;
    along = -1.0;
    total = 0.0;
    if (kind < 17.5)
    {
        result = evalShapeAnalyticSdf(kind, p, b, prm, aux, style);
        [branch]
        if (wantAlong) along = evalShapeAnalyticAlong(kind, p, b, prm, aux, style, total);
    }
    else if (kind < 18.5)                                 // normalized verts scaled by b less the rounding
        result = sdShapePolygon(p, max(b - carve, 0.0), LightSideShapeRowV(prm.x),
                                (int)(prm.y + 0.5), wantAlong, along, total) - carve;
    else
        result = sdShapePolyline(p, max(b - prm.z, 0.0), LightSideShapeRowV(prm.x), (int)(prm.y + 0.5),
                                 prm.z, style, prm.w < 0.0 ? -1.0 : LightSideShapeRowV(prm.w));
    return result;
}

// The repeating pattern cell a fragment at `coord` (measured in frame units) stands in, from the description
// packed into row `rowV` of the shape-vertex atlas by ShapePattern.Pack: the lattice the element repeats on, the
// turn each cell gives it, and the unit axis its curve is measured along. The element itself — its outline in
// texel 0, its kind's params in texel 1, its half-extents and offset in texel 2 — is evaluated through the same
// primitive library the shape itself uses, so it is exact and antialiased across one pixel whatever the cell
// count; softness spreads that edge, it does not create it. The antialiasing width takes derivatives, so a cell
// is resolved in quad-uniform control flow, outside any loop.
struct LightSidePatternCell
{
    float2 turned;
    float2 axis;
    float rowV;
    float aa;
    float empty;
};

LightSidePatternCell evalShapePatternCell(float2 coord, float rowV)
{
    LightSidePatternCell cell;
    float4 t3 = LIGHTSIDE_SAMPLE_SHAPE_VERTS(3.5 / UI_SDF_VERT_TEX_WIDTH, rowV);
    float4 t4 = LIGHTSIDE_SAMPLE_SHAPE_VERTS(4.5 / UI_SDF_VERT_TEX_WIDTH, rowV);

    float2 lattice = coord * t4.xy;
    float row = floor(lattice.y);
    if (t3.w > 0.5 && t3.w < 1.5) lattice.x += 0.5 * fmod(abs(row), 2.0);   // brick
    float2 id = float2(floor(lattice.x), row);
    float2 local = frac(lattice) - 0.5;

    cell.turned = float2(t3.x * local.x + t3.y * local.y, -t3.y * local.x + t3.x * local.y);
    cell.axis = t4.zw;
    cell.rowV = rowV;
    cell.aa = max(max(fwidth(lattice.x), fwidth(lattice.y)), 1e-6) * 0.5 + t3.z;
    // An alternating lattice leaves every other cell empty.
    cell.empty = (t3.w > 1.5 && frac((id.x + id.y) * 0.5) > 0.25) ? 1.0 : 0.0;
    return cell;
}

// The size a pattern's curve gives an element standing at `driveT`. The curve is baked as four samples per texel
// over the drive's domain, which the caller chose, measured and folded; a drive of None baked it flat, so what
// arrives there does not matter.
float evalShapePatternSize(float rowV, float driveT)
{
    float u = saturate(driveT) * (UI_SDF_PATTERN_CURVE_SAMPLES - 1.0);
    float lo = floor(u);
    float4 c0 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((5.0 + floor(lo * 0.25) + 0.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
    float4 c1 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((5.0 + floor((lo + 1.0) * 0.25) + 0.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
    float a = dot(c0, float4(fmod(lo, 4.0) < 0.5, abs(fmod(lo, 4.0) - 1.0) < 0.5,
                             abs(fmod(lo, 4.0) - 2.0) < 0.5, fmod(lo, 4.0) > 2.5));
    float nx = fmod(lo + 1.0, 4.0);
    float b = dot(c1, float4(nx < 0.5, abs(nx - 1.0) < 0.5, abs(nx - 2.0) < 0.5, nx > 2.5));
    return max(lerp(a, b, u - lo), 1e-4);
}

// Polynomial smooth minimum (iq): min(a, b) with the crease between them worked over |k| — filleted outward by a
// positive k, grooved inward by a negative one, so two outlines reach for each other or recoil from each other by
// the same knob. Exact min away from the crease, so a vanishing k is a plain union; callers keep k off zero.
float sdShapeSmoothUnion(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / abs(k));
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// The seam two elements leave where they meet, worked over |k|: rounded by the polynomial smooth minimum, or cut
// straight across by the chamfer union (Mercury hg_sdf). A negative k inverts the seam into a groove, and a groove
// is always round — a negative offset moves the cutting plane past the join, so the seam style shapes the fillet
// alone. Every boolean op is built from this one join, so a seam style and its sign reach all four the same way.
float sdShapeSeam(float a, float b, float k, float chamfer)
{
    return (chamfer > 0.5 && k > 0.0) ? min(min(a, b), (a - k + b) * 0.70710678)
                                      : sdShapeSmoothUnion(a, b, k);
}

float sdShapeFold(float d, float di, float op, float blend, float morph, float seam)
{
    float k = (abs(blend) < 1e-4) ? 1e-4 : blend;
    float result;
    if (op < 0.5)       result = sdShapeSeam(d, di, k, seam);
    else if (op < 2.5)  result = -sdShapeSeam(-d, op < 1.5 ? di : -di, k, seam);
    else if (op < 3.5)
    {
        float u = sdShapeSeam(d, di, k, seam);
        float x = -sdShapeSeam(-d, -di, k, seam);
        result = -sdShapeSeam(-u, x, k, seam);
    }
    else                result = lerp(d, di, morph);
    return result;
}

// Value noise in [0,1] for the Noise effect layer (smooth-interpolated hash grid).
float uiSdfHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float uiSdfValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = uiSdfHash(i);
    float b = uiSdfHash(i + float2(1.0, 0.0));
    float c = uiSdfHash(i + float2(0.0, 1.0));
    float d = uiSdfHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

#endif
