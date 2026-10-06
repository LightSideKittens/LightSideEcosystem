#ifndef LIGHTSIDE_SHADER_FEATURES_INCLUDED
#define LIGHTSIDE_SHADER_FEATURES_INCLUDED

// A material's surface tier (a local keyword, LightSideSurfaceTier) compiles only the surfaces and features its
// content draws: a register footprint is static, so one feature compiled in slows every fragment of the program,
// drawn or not. A tier names its surfaces and excludes the features they leave unused. Without a tier the program
// carries every capability of the project profile (the global keywords); a shader that does not define
// LIGHTSIDE_SHADER_VARIANTS carries all of them. A feature is compiled unless excluded, so a program that never
// includes this file, such as a custom-material prelude, keeps every feature of the headers it includes:
//   LIGHTSIDE_NO_GLYPH_EFFECTS — stroke, shadow, glow and inner shadow coverage, corner styles, fill softness
//   LIGHTSIDE_NO_GLYPH_UNIONS  — overlapping glyphs drawn as one shape (LightSideGlyphUnion.hlsl)
//   LIGHTSIDE_NO_GLYPH_DEFORM  — the deformed sample point (LightSideDeform.hlsl)
//   LIGHTSIDE_NO_PAINT_LAYERS  — gradient, distance, pattern and texture paint and the colour-matrix filter
//   LIGHTSIDE_NO_IMAGE_SURFACE — image quads (LightSideSurfaceKind.Image)

#if defined(LIGHTSIDE_TIER_TEXT) || defined(LIGHTSIDE_TIER_STYLED_TEXT) || defined(LIGHTSIDE_TIER_UNITED_TEXT) || \
    defined(LIGHTSIDE_TIER_SHAPES) || defined(LIGHTSIDE_TIER_ATLAS_QUADS)
    #undef LIGHTSIDE_GLYPHS
    #undef LIGHTSIDE_ROUNDED_RECTANGLES
    #undef LIGHTSIDE_SHAPES
    #undef LIGHTSIDE_ATLAS_QUADS
    #define LIGHTSIDE_NO_GLYPH_DEFORM
    #define LIGHTSIDE_NO_IMAGE_SURFACE
#endif

#if defined(LIGHTSIDE_TIER_TEXT)
    #define LIGHTSIDE_GLYPHS
    #define LIGHTSIDE_NO_GLYPH_EFFECTS
    #define LIGHTSIDE_NO_GLYPH_UNIONS
    #define LIGHTSIDE_NO_PAINT_LAYERS
#elif defined(LIGHTSIDE_TIER_STYLED_TEXT) || defined(LIGHTSIDE_TIER_UNITED_TEXT)
    #define LIGHTSIDE_GLYPHS
    #define LIGHTSIDE_ROUNDED_RECTANGLES
    #if defined(LIGHTSIDE_TIER_STYLED_TEXT)
        #define LIGHTSIDE_NO_GLYPH_UNIONS
    #endif
#elif defined(LIGHTSIDE_TIER_SHAPES)
    #define LIGHTSIDE_SHAPES
#elif defined(LIGHTSIDE_TIER_ATLAS_QUADS)
    #define LIGHTSIDE_ATLAS_QUADS
    #define LIGHTSIDE_NO_PAINT_LAYERS
#elif !defined(LIGHTSIDE_SHADER_VARIANTS)
    #define LIGHTSIDE_GLYPHS
    #define LIGHTSIDE_SHAPES
    #define LIGHTSIDE_ATLAS_QUADS
#endif

#if defined(LIGHTSIDE_SHAPES) || defined(LIGHTSIDE_ROUNDED_RECTANGLES)
#define LIGHTSIDE_SHAPE_SURFACE
#endif

#endif
