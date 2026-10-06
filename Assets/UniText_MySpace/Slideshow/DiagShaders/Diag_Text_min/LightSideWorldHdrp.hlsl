// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// Custom Function entry points for the LightSide World HDRP Shader Graphs.
// Wraps the shared SRP surface stage (LightSideWorldURP.hlsl — SRP-Core-only with no pass
// selector defined) so the graph's vertex stage packs the LightSide vertex stream into custom
// interpolators and its fragment stage resolves them through the one shared surface path.
//
// Shader Graph file contract: functions carry the _float precision suffix, parameters in slot order.

#ifndef LIGHTSIDE_WORLD_HDRP_INCLUDED
#define LIGHTSIDE_WORLD_HDRP_INCLUDED

// The shared stage's structs carry instancing/stereo macro slots. Pipeline passes define them
// via UnityInstancing.hlsl; Shader Graph node previews compile without it, so make them inert
// when absent (the same empty-expansion UnityInstancing itself uses with instancing off).
#ifndef UNITY_VERTEX_INPUT_INSTANCE_ID
#define UNITY_VERTEX_INPUT_INSTANCE_ID
#endif
#ifndef UNITY_VERTEX_OUTPUT_STEREO
#define UNITY_VERTEX_OUTPUT_STEREO
#endif

// Shape quads never ride a lit world mesh (decorations and vector animation always use the unlit
// world material), so the shape surface is compiled out — with constant zero shape inputs it would
// otherwise constant-fold into division-by-zero compiler warnings.
#define LIGHTSIDE_NO_SHAPES
#define LIGHTSIDE_SHADER_VARIANTS
#define LIGHTSIDE_GLYPHS
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Text_min/LightSideWorldURP.hlsl"

// Unpacks the LightSide world vertex stream (UV0-UV3 + color) for the interpolator blocks.
// Same unpack as LightSideWorldVertex; position and normal stay with the master stack.
void LightSideWorldLitHdrpVertex_float(
    float4 UV0, float4 UV1, float4 UV2, float4 UV3, float4 Color,
    out float4 AtlasUV, out float2 GlyphUV, out float4 Cov,
    out float4 Paint, out float4 ColorOut, out float4 Extra)
{
    float glyphMode = LightSideSurfaceKind(UV1.w);
    float2 sdfScale = 0;
    float pageLayer = UV0.z;
    float2 atlasXY = UV0.xy;
#if defined(LIGHTSIDE_GLYPHS)
    if (glyphMode < 1.5)
    {
        float4 glyphTransform = LightSideLoadGlyphTransform(UV0.z);
        sdfScale = glyphTransform.xx;
        atlasXY = UV0.xy * glyphTransform.x + glyphTransform.yz;
        pageLayer = glyphTransform.w;
    }
#endif

    AtlasUV  = float4(atlasXY, pageLayer, glyphMode);
    GlyphUV  = UV0.xy;
    Cov      = UV2;
    Paint    = UV3;
    ColorOut = GammaToLinearIfNeeded(Color);
    Extra    = float4(UV1.y, UV0.w, sdfScale.x, sdfScale.y);
}

// Resolves the surface and splits it for HDRP's premultiplied Lit path:
// BaseColor = rgb * t and Emission = rgb * (1 - t) reproduce lerp(rgb, rgb * light, t)
// under the light loop — at influence 0 the output is exactly the unlit surface.
// All values stay premultiplied; HDRP's Premultiply blend mode passes diffuse lighting
// through unscaled (One / OneMinusSrcAlpha), so no un-premultiply round trip exists.
void LightSideWorldLitHdrpFragment_float(
    float4 AtlasUV, float2 GlyphUV, float4 Cov, float4 Paint, float4 Color, float4 Extra,
    float LightInfluence,
    out float3 BaseColor, out float3 Emission, out float Alpha)
{
    LightSideWorldVaryings varyings = (LightSideWorldVaryings)0;
    varyings.atlasUV = AtlasUV;
    varyings.glyphUV = GlyphUV;
    varyings.cov     = Cov;
    varyings.paint   = Paint;
    varyings.color   = Color;
    varyings.extra   = Extra;

    half4 result = LightSideResolveSurface(varyings);
    half influence = saturate(LightInfluence);
    BaseColor = result.rgb * influence;
    Emission  = result.rgb * (1.0 - influence);
    Alpha     = result.a;
}

#endif
