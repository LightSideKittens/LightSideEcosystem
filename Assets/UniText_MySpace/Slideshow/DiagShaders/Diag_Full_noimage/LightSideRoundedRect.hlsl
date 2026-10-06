#ifndef LIGHTSIDE_ROUNDED_RECT_INCLUDED
#define LIGHTSIDE_ROUNDED_RECT_INCLUDED

float sdRoundedBox(float2 p, float2 b, float4 r, float smoothing)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    float2 qp = max(q, 0.0);
    float corner = length(qp);
    [branch] if (smoothing > 0.0)
    {
        float n = lerp(2.0, 4.0, saturate(smoothing));
        corner = pow(pow(qp.x, n) + pow(qp.y, n), 1.0 / n);
    }
    return min(max(q.x, q.y), 0.0) + corner - r.x;
}

#endif
