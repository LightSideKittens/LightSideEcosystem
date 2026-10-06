// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// The per-kind parameters of a shape quad, as the vertex stage reads them from TANGENT.w. A Canvas batch
// multiplies TANGENT.xyz by each element's normal matrix, as it does a normal, so a rotated, scaled or
// mirrored element would hand the fragment parameters turned with it; the w lane passes through every host
// unchanged. It carries the corners of a rounded rectangle whose rounded corners share one radius inline, and
// the row of the shape-vertex atlas holding the parameters otherwise. Its sign carries nothing. Written by
// ShapeParameterLane.cs.
#ifndef LIGHTSIDE_SHAPE_PARAMS_INCLUDED
#define LIGHTSIDE_SHAPE_PARAMS_INCLUDED

// SYNC: ShapeParameterLane.InlineBase and ShapeParameterLane.RadiusSteps.
#define LIGHTSIDE_SHAPE_LANE_INLINE 4194304.0
#define LIGHTSIDE_SHAPE_LANE_RADIUS_STEPS 16.0

#if defined(LIGHTSIDE_SHAPES)
Texture2D<float4> _LightSideShapeVertices;

float4 LightSideShapeVertex(int texel, float atlasRow)
{
    return _LightSideShapeVertices.Load(int3(texel, (int)(atlasRow + 0.5), 0));
}
#endif

// Zero, the four corner radii of an inline rounded rectangle — radius steps above the base, times sixteen,
// plus a mask whose bit i rounds corner i of (TL, TR, BR, BL) — or texel 0 of an atlas row plus one. Only a
// profile with shapes reads a row: a profile without shapes writes inline lanes alone.
float4 LightSideShapeParams(float lane)
{
    float v = abs(lane);
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    if (v >= LIGHTSIDE_SHAPE_LANE_INLINE)
    {
        float packed = v - LIGHTSIDE_SHAPE_LANE_INLINE;
        float steps = floor(packed * 0.0625);
        float4 bits = floor((packed - steps * 16.0) * float4(1.0, 0.5, 0.25, 0.125));
        result = (steps / LIGHTSIDE_SHAPE_LANE_RADIUS_STEPS) * (bits - 2.0 * floor(bits * 0.5));
    }
#if defined(LIGHTSIDE_SHAPES)
    else if (v > 0.5)
    {
        result = LightSideShapeVertex(0, v - 1.0);
    }
#endif
    return result;
}

#endif // LIGHTSIDE_SHAPE_PARAMS_INCLUDED
