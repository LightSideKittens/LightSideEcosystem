#ifndef LIGHTSIDE_SURFACE_PAINT_INCLUDED
#define LIGHTSIDE_SURFACE_PAINT_INCLUDED

#include "LightSideShaderFeatures.hlsl"

#if defined(LIGHTSIDE_GLYPHS)
#include "LightSideGlyphCoverage.hlsl"
#endif

static float lightSideDistanceT;
static float lightSidePatternT;
#define LIGHTSIDE_PAINT_DISTANCE_T lightSideDistanceT
#if defined(LIGHTSIDE_SHAPES) && !defined(LIGHTSIDE_NO_SHAPES)
#define LIGHTSIDE_PAINT_PATTERN_T lightSidePatternT
#endif

#include "LightSidePaint.hlsl"

#if defined(LIGHTSIDE_SHAPE_SURFACE) && !defined(LIGHTSIDE_NO_SHAPES)
#include "LightSideShapeSurface.hlsl"
#endif

#endif
