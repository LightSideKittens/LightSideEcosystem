// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// Shape modifiers — the field transforms a quad's modifier chain applies before its outline is read. Each
// link of the chain is a row of the shape-vertex atlas written by ShapeDeformation.Fold, the highest modifier
// at the head:
//   texel 0 = (aux the chain replaced, modifier table row, next link + 1, ShapeModifierKind)
//   texel 1 = (cos, sin, x, y) — the turn and offset carrying the layer's own point into the modifier's frame
//   texel 2 = (centre.xy, half.xy) — the box the modifier stands on, in its own frame
// The modifier's parameters live in _LightSideShapeModifierTable, two texels a row, written by
// ShapeModifierTable; a parameter an animation moves rewrites that row and nothing else. Every kind except
// Offset and ZigZag moves the point the outline is sampled at — the inverse of how it reshapes the outline —
// so every layer derived from the field bends with it. Offset and ZigZag move the distance itself, after the
// outline is read. An element of a combination is read through a prefix of the chain: every link, or only
// the links of the modifiers standing above a cut, so a modifier below a cut never reshapes the cut.
// ShapeModifierField.cs mirrors every formula here for the pointer; the two must agree.
#ifndef LIGHTSIDE_SHAPE_MODIFIERS_INCLUDED
#define LIGHTSIDE_SHAPE_MODIFIERS_INCLUDED

// SYNC: ShapeModifierKind.
#define LIGHTSIDE_MODIFIER_OFFSET 1.0
#define LIGHTSIDE_MODIFIER_ZIGZAG 5.0

Texture2D<float4> _LightSideShapeModifierTable;

float2 LightSideTurn(float2 v, float c, float s)
{
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// Travelling two-channel value noise; `jagged` 1 interpolates the lattice linearly, which leaves a crease
// along every cell edge instead of a smooth swell.
float2 LightSideModifierNoise(float2 p, float jagged)
{
    float2 i = floor(p);
    float2 f = p - i;
    f = lerp(f * f * (3.0 - 2.0 * f), f, jagged);

    float2 a = LightSideDeformHash2(i);
    float2 b = LightSideDeformHash2(i + float2(1.0, 0.0));
    float2 c = LightSideDeformHash2(i + float2(0.0, 1.0));
    float2 d = LightSideDeformHash2(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// iq's inverse bilinear: where in the unit square the point q of the quad a-b-c-d (counter-clockwise from
// the lower left) came from. Of the two roots the one nearer the square is kept, so a point just outside the
// quad extrapolates continuously; a point no root reaches is sent far outside.
float2 LightSideInverseBilinear(float2 q, float2 a, float2 b, float2 c, float2 d)
{
    float2 e = b - a;
    float2 f = d - a;
    float2 g = a - b + c - d;
    float2 h = q - a;
    float k2 = g.x * f.y - g.y * f.x;
    float k1 = e.x * f.y - e.y * f.x + h.x * g.y - h.y * g.x;
    float k0 = h.x * e.y - h.y * e.x;

    float2 result = float2(1e3, 1e3);
    float disc = k1 * k1 - 4.0 * k0 * k2;
    if (abs(k2) < 1e-5)
    {
        float v = -k0 / ((abs(k1) < 1e-6) ? 1e-6 : k1);
        float2 den = e + g * v;
        float u = (abs(den.x) > abs(den.y)) ? (h.x - f.x * v) / den.x : (h.y - f.y * v) / den.y;
        result = float2(u, v);
    }
    else if (disc >= 0.0)
    {
        float root = sqrt(disc);
        float v1 = (-k1 - root) * 0.5 / k2;
        float v2 = (-k1 + root) * 0.5 / k2;
        float2 den1 = e + g * v1;
        float2 den2 = e + g * v2;
        float u1 = (abs(den1.x) > abs(den1.y)) ? (h.x - f.x * v1) / den1.x : (h.y - f.y * v1) / den1.y;
        float u2 = (abs(den2.x) > abs(den2.y)) ? (h.x - f.x * v2) / den2.x : (h.y - f.y * v2) / den2.y;
        float2 o1 = max(abs(float2(u1, v1) - 0.5) - 0.5, 0.0);
        float2 o2 = max(abs(float2(u2, v2) - 0.5) - 0.5, 0.0);
        result = (dot(o1, o1) <= dot(o2, o2)) ? float2(u1, v1) : float2(u2, v2);
    }
    return result;
}

// The fifteen warp styles, in the box's normalized frame n (each axis -1..1, the bend acting across x; a
// vertical warp hands the axes over swapped): where the point n of the warped outline was before the warp.
// `bend` is -1..1; the distortion, which keeps its own axes either way, was undone before this. The fisheye's
// lens reaches the box's corners, and inflate and squeeze weigh each axis by a falloff that stays smooth across
// the box's edges, where the outline runs.
float2 LightSideWarpStyle(float style, float bend, float2 n, float2 box)
{
    float k = bend * saturate(1.0 - n.x * n.x);
    float m = 0.5 * bend * n.x * n.x;
    float2 across = saturate(1.0 - 0.5 * n * n);
    across *= across;
    float2 result = n;
    if (style < 0.5)
    {
        float phi = bend * 1.57079633;
        if (abs(phi) > 1e-4)
        {
            float sgn = (phi > 0.0) ? 1.0 : -1.0;
            float radius = 1.0 / abs(phi);
            float aspect = box.y / max(box.x, 1e-4);
            float2 v = float2(n.x + 1e-6, n.y * aspect * sgn + radius);
            float theta = atan2(v.x, v.y);
            result = float2(theta * radius, (length(v) - radius) * sgn / max(aspect, 1e-4));
        }
    }
    else if (style < 1.5)  result.y = (n.y + 0.5 * k) / max(1.0 + 0.5 * k, 0.05);
    else if (style < 2.5)  result.y = (n.y - 0.5 * k) / max(1.0 + 0.5 * k, 0.05);
    else if (style < 3.5)  result.y = n.y - k;
    else if (style < 4.5)  result.y = n.y / max(1.0 + k, 0.05);
    else if (style < 5.5)  result.y = (n.y - m) / max(1.0 - m, 0.05);
    else if (style < 6.5)  result.y = (n.y + m) / max(1.0 - m, 0.05);
    else if (style < 7.5)  result.y = n.y - bend * 0.5 * (n.x + 1.0) * sin(3.14159265 * (n.x + 1.0)) * 0.75;
    else if (style < 8.5)  result.y = n.y - 0.35 * bend * sin(4.71238898 * n.x);
    else if (style < 9.5)  result.y = n.y / max(1.0 + 0.5 * bend * cos(2.35619449 * (n.x + 1.0)), 0.05);
    else if (style < 10.5) result.y = n.y - 0.5 * bend * sin(1.57079633 * n.x);
    else if (style < 11.5)
    {
        float r = length(n) * 0.70710678;
        if (r < 1.0) result = n * pow(max(r, 1e-5), clamp(bend, -0.95, 4.0));
    }
    else if (style < 12.5)
    {
        result.x = n.x / max(1.0 + 0.5 * bend * across.y, 0.05);
        result.y = n.y / max(1.0 + 0.5 * bend * across.x, 0.05);
    }
    else if (style < 13.5)
    {
        result.x = n.x / max(1.0 + 0.25 * bend * across.y, 0.05);
        result.y = n.y / max(1.0 - 0.5 * bend * across.x, 0.05);
    }
    else
    {
        float2 w = n * box / max(length(box), 1e-4);
        float r = length(w);
        float turn = -bend * 3.14159265 * (1.0 - saturate(r)) * (1.0 - saturate(r));
        w = LightSideTurn(w, cos(turn), sin(turn));
        result = w * max(length(box), 1e-4) / max(box, 1e-4);
    }
    return result;
}

// Where the point q of the modified outline, in the modifier's own frame, was sampled from. `kind` is one of
// the kinds that move the point; a = texel 0 and b = texel 1 of the modifier's row, box = (centre, half), and
// anchor is where the layer's own outline stands in the frame: a line or grid of copies counts from it, and a
// ring of copies turns about the origin from the angle it stands at.
float2 LightSideModifyPoint(float kind, float2 q, float4 a, float4 b, float4 box, float2 anchor)
{
    float2 centre = box.xy;
    float2 extent = max(box.zw, 1e-4);
    float2 result = q;
    if (kind < 0.5)
    {
        result = q + (LightSideModifierNoise(q * a.y + a.zw, b.x) * 2.0 - 1.0) * a.x;
    }
    else if (kind < 2.5)
    {
        bool vertical = b.x > 0.5;
        float2 n = (q - centre) / extent;
        n.x = n.x / max(1.0 + 0.5 * a.w * n.y, 0.05);
        n.y = n.y / max(1.0 + 0.5 * a.z * n.x, 0.05);
        n = LightSideWarpStyle(a.x, a.y, vertical ? n.yx : n, vertical ? extent.yx : extent);
        result = centre + (vertical ? n.yx : n) * extent;
    }
    else if (kind < 3.5)
    {
        float2 v = q - centre;
        float r = saturate(length(v) / length(extent));
        float turn = -a.x * (1.0 - r) * (1.0 - r);
        result = centre + LightSideTurn(v, cos(turn), sin(turn));
    }
    else if (kind < 4.5)
    {
        float2 n = (q - centre) / extent;
        result = centre + n * pow(max(length(n), 1e-5), a.x - 1.0) * extent;
    }
    else if (kind < 6.5)
    {
        float2 n = (q - centre) / extent;
        float2 uv = LightSideInverseBilinear(n, float2(-1.0, -1.0) + a.xy, float2(1.0, -1.0) + a.zw,
                                             float2(1.0, 1.0) + b.xy, float2(-1.0, 1.0) + b.zw);
        result = centre + (uv * 2.0 - 1.0) * extent;
    }
    else if (kind < 7.5)
    {
        float2 v = q - centre;
        float det = 1.0 - a.x * a.y;
        det = (abs(det) < 1e-3) ? 1e-3 : det;
        result = centre + float2(v.x - a.x * v.y, v.y - a.y * v.x) / det;
    }
    else
    {
        if (a.x < 0.5)
        {
            float t = dot(q - anchor, b.xy) / max(dot(b.xy, b.xy), 1e-6);
            result = q - clamp(round(t), 0.0, max(a.y - 1.0, 0.0)) * b.xy;
        }
        else if (a.x < 1.5)
        {
            float2 cells = float2(abs(b.x) > 1e-4 ? round((q.x - anchor.x) / b.x) : 0.0,
                                  abs(b.y) > 1e-4 ? round((q.y - anchor.y) / b.y) : 0.0);
            result = q - clamp(cells, 0.0, max(a.yz - 1.0, 0.0)) * b.xy;
        }
        else
        {
            float sector = 6.28318531 / max(a.y, 1.0);
            float heading = atan2(q.y, q.x + 1e-6) - atan2(anchor.y, anchor.x + 1e-6);
            float turn = -round(heading / sector) * sector;
            result = LightSideTurn(q, cos(turn), sin(turn));
        }
    }
    return result;
}

// Walks the prefix of a quad's modifier chain an element reads — every link when `prefix` is zero, the first
// prefix - 1 links otherwise — moving the point p to where the outline is sampled. Returns the moved point,
// the distance the Offset links of the prefix grow the outline by, and whether a ZigZag link of the prefix
// waits for the outline coordinate.
float2 LightSideApplyShapeModifiers(float head, float prefix, float2 p, out float grow, out bool zigzag)
{
    grow = 0.0;
    zigzag = false;
    float link = head;
    float left = (prefix > 0.5) ? prefix - 1.0 : 1e6;
    [loop] while (link > 0.0 && left > 0.5)
    {
        float rowV = LightSideShapeRowV(link - 1.0);
        float4 entry = LIGHTSIDE_SAMPLE_SHAPE_VERTS(0.5 / 32.0, rowV);
        float kind = entry.w;
        int row = (int)(entry.y + 0.5);
        float4 a = _LightSideShapeModifierTable.Load(int3(0, row, 0));
        if (abs(kind - LIGHTSIDE_MODIFIER_OFFSET) < 0.5)
        {
            grow += a.x;
        }
        else if (abs(kind - LIGHTSIDE_MODIFIER_ZIGZAG) < 0.5)
        {
            zigzag = true;
        }
        else
        {
            float4 frame = LIGHTSIDE_SAMPLE_SHAPE_VERTS(1.5 / 32.0, rowV);
            float4 box = LIGHTSIDE_SAMPLE_SHAPE_VERTS(2.5 / 32.0, rowV);
            float4 b = _LightSideShapeModifierTable.Load(int3(1, row, 0));
            float2 q = LightSideTurn(p, frame.x, frame.y) + frame.zw;
            q = LightSideModifyPoint(kind, q, a, b, box, frame.zw);
            p = LightSideTurn(q - frame.zw, frame.x, -frame.y);
        }
        left -= 1.0;
        link = entry.z;
    }
    return p;
}

// The distance the ZigZag links of the same prefix move an outline at its coordinate `along`, of an outline
// `total` long. A coordinate below zero has no outline to ridge and moves nothing.
float LightSideShapeZigZag(float head, float prefix, float along, float total)
{
    float result = 0.0;
    float link = head;
    float left = (prefix > 0.5) ? prefix - 1.0 : 1e6;
    [loop] while (link > 0.0 && left > 0.5)
    {
        float4 entry = LIGHTSIDE_SAMPLE_SHAPE_VERTS(0.5 / 32.0, LightSideShapeRowV(link - 1.0));
        if (abs(entry.w - LIGHTSIDE_MODIFIER_ZIGZAG) < 0.5)
        {
            float4 a = _LightSideShapeModifierTable.Load(int3(0, (int)(entry.y + 0.5), 0));
            float s = along / max(total, 1e-4) * a.y + a.w;
            float tri = 1.0 - 4.0 * abs(frac(s + 0.5) - 0.5);
            result += a.x * ((a.z > 0.5) ? cos(6.28318531 * s) : tri);
        }
        left -= 1.0;
        link = entry.z;
    }
    return (along >= 0.0 && total > 0.0) ? result : 0.0;
}

#endif // LIGHTSIDE_SHAPE_MODIFIERS_INCLUDED
