// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// CGPROGRAM implementation of the world text/shape surface (LightSideWorld, WorldBatcher meshes).
// Serves every SubShader that runs on the legacy CG stack: the Built-in pipeline SubShaders and the
// HDRP baseline SubShaders (whose untagged pass HDRP draws via SRPDefaultUnlit with the legacy
// matrix globals — the same mechanism that renders uGUI and sprites there).
//
// Pass selector defines (set BEFORE the include; pragmas never travel through includes, so each
// pass declares its own):
//   LIGHTSIDE_LIT_PASS      — lit color program: worldNormal/vertexLight interpolators, VFACE
//                             two-sided normal, ambient+directional+vertex lights blended by
//                             _LightInfluence. Requires "LightMode"="ForwardBase" and
//                             `#pragma multi_compile __ VERTEXLIGHT_ON` — the lighting constants
//                             (_LightColor0, SH, unity_4LightPos*) are only populated there.
//   (none)                  — unlit color program. Legal in any pass group, including untagged.
//   LIGHTSIDE_SHADOW_CASTER — ShadowCaster program (LightSideShadowVert/LightSideShadowFrag):
//                             SDF-cutoff silhouette via the coverage cast resolve. Requires
//                             `#pragma multi_compile_shadowcaster`. Built-in pipeline only —
//                             TRANSFER_SHADOW_CASTER reads bias globals no SRP populates.
//   Color passes take `#pragma dynamic_branch _ LIGHTSIDE_PAINT_TEXTURE` with LIGHTSIDE_PAINT_TEXTURE_DYNAMIC
//   defined, and `#pragma multi_compile_fog` where scene fog exists (Built-in; HDRP never enables FOG_* keywords).

#ifndef LIGHTSIDE_WORLD_CG_INCLUDED
#define LIGHTSIDE_WORLD_CG_INCLUDED

#include "UnityCG.cginc"
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noboth/LightSideGlyphField.cginc"

#ifdef LIGHTSIDE_SHADOW_CASTER

sampler2D _LightSidePaintTexture;
#define LIGHTSIDE_SAMPLE_PAINT(uv) tex2D(_LightSidePaintTexture, uv)
#define LIGHTSIDE_SAMPLE_IMAGE_GRAD(uv, dx, dy) tex2Dgrad(_LightSidePaintTexture, uv, dx, dy)
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noboth/LightSideShadowSurface.hlsl"

half _ShadowCutoff;

struct LightSideShadowVaryings
{
    V2F_SHADOW_CASTER;
    float4 atlasUV : TEXCOORD1;          // V2F_SHADOW_CASTER claims TEXCOORD0 for SHADOWS_CUBE
    float4 cov     : TEXCOORD2;          // coverageMode, p0, p1, softness
    float4 meta    : TEXCOORD3;          // glyphUV.xy, faceDilate (color: vertex alpha), glyphH
    float4 shapeGeom : TEXCOORD4;
    float4 shapeParams : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

LightSideShadowVaryings LightSideShadowVert(LightSideSurfaceVertex v)
{
    LightSideShadowVaryings o;
    UNITY_INITIALIZE_OUTPUT(LightSideShadowVaryings, o);
    UNITY_SETUP_INSTANCE_ID(v);

    // Plain TRANSFER_SHADOW_CASTER (not _NORMALOFFSET): batcher writes world-space
    // normals, the offset variant would re-apply ObjectToWorld to them.
    TRANSFER_SHADOW_CASTER(o)

    float glyphMode  = LightSideSurfaceKind(v.texcoord1.w);
    float metaW      = LightSideSurfaceFraction(v.texcoord1.w);
    float metaZ      = v.color.a;
    float  pageLayer = v.texcoord0.z;
    float2 atlasXY   = v.texcoord0.xy;
#if defined(LIGHTSIDE_GLYPHS)
    if (glyphMode < 1.5)
    {
        metaZ = v.texcoord1.y;
        metaW = v.texcoord0.w;
        float4 t = LightSideLoadGlyphTransform(v.texcoord0.z);
        atlasXY = v.texcoord0.xy * t.x + t.yz;
        pageLayer = t.w;
    }
#endif

    o.atlasUV = float4(atlasXY, pageLayer, glyphMode);
    o.cov     = v.texcoord2;
    o.meta    = float4(v.texcoord0.xy, metaZ, metaW);
    o.shapeGeom = float4(v.texcoord0.zw, v.texcoord1.x, v.texcoord1.z);
    o.shapeParams = LightSideShapeParams(v.tangent.w);
    return o;
}

float4 LightSideShadowFrag(LightSideShadowVaryings i) : SV_Target
{
    clip(LightSideShadowClip(i.atlasUV, i.cov, i.meta, i.shapeGeom, i.shapeParams, _ShadowCutoff));
    SHADOW_CASTER_FRAGMENT(i)
}

#else // color program

sampler2D _LightSideGradientRamp;
float _LightSideGradientRampRows;
sampler2D _LightSidePaintTexture;
sampler2D _LightSideColorMatrixAtlas;
float _LightSideColorMatrixRows;
// Explicit LOD 0: the ramp has no mips — see LightSidePaint.hlsl for why implicit
// derivatives would flatten the gradient branch.
float4 _LightSidePaintTexture_TexelSize;
#define LIGHTSIDE_SAMPLE_RAMP(u, v) tex2Dlod(_LightSideGradientRamp, float4(u, v, 0, 0))
#define LIGHTSIDE_SAMPLE_PAINT(uv)  tex2D(_LightSidePaintTexture, uv)
#define LIGHTSIDE_SAMPLE_IMAGE_GRAD(uv, dx, dy) tex2Dgrad(_LightSidePaintTexture, uv, dx, dy)
#define LIGHTSIDE_SAMPLE_MATRIX(u, v) tex2Dlod(_LightSideColorMatrixAtlas, float4(u, v, 0, 0))

#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noboth/LightSideSurfacePaint.hlsl"


#ifdef LIGHTSIDE_LIT_PASS
float4 _LightColor0;

half _LightInfluence;
half _AmbientStrength;
half _DirectStrength;
#endif

struct pixel_t
{
    float4 vertex      : SV_POSITION;
    float4 atlasUV     : TEXCOORD0;  // xy = atlas UV, z = page layer, w = glyph mode (quad-constant)
    float2 glyphUV     : TEXCOORD1;  // for fwidth AA
    half4  cov         : TEXCOORD2;  // coverageMode, p0, p1, softness (em-scale — half is plenty)
    float4 paint       : TEXCOORD3;  // paintU, paintV, rampRow, paintKind + 8 * spread (tiled coords exceed half range)
    fixed4 color       : TEXCOORD4;  // straight vertex colour (premultiplied in fragment)
    half4  extra       : TEXCOORD5;  // glyphs: faceDilate, glyphH, sdfScale.xy — shapes: .x is the paint scale/fit
#ifdef LIGHTSIDE_LIT_PASS
    float3 worldNormal : TEXCOORD6;  // per-quad world-space normal written by batcher
    half3  vertexLight : TEXCOORD7;  // 4 nearest non-important lights, per-vertex
    float4 shapeGeom   : TEXCOORD9;  // halfSize.xy, aux, trailing mode param — glyphs: .x is the UV0.z lane, .z the deform lane (UV1.x)
    float4 shapeParams : TEXCOORD10; // per-shape params (radii / ratios / counts) — glyphs: the union gate
#else
    float4 shapeGeom   : TEXCOORD6;  // halfSize.xy, aux, trailing mode param — glyphs: .x is the UV0.z lane, .z the deform lane (UV1.x)
    float4 shapeParams : TEXCOORD7;  // per-shape params (radii / ratios / counts) — glyphs: the union gate
#endif
    UNITY_FOG_COORDS(8)
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

pixel_t VertShader(LightSideSurfaceVertex input)
{
    pixel_t output;

    UNITY_INITIALIZE_OUTPUT(pixel_t, output);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 vPosition = UnityObjectToClipPos(input.vertex);

    // A world mesh has no Canvas ahead of it: the colour stream is the authored sRGB bytes.
    fixed4 color = GammaToLinearIfNeeded(input.color, true);

    float glyphMode  = LightSideSurfaceKind(input.texcoord1.w);
    float glyphH     = input.texcoord0.w;
    float faceDilate = input.texcoord1.y;
    float2 sdfScale  = float2(0.0, LightSideSurfaceFraction(input.texcoord1.w));
    float  pageLayer = input.texcoord0.z;
    float2 atlasXY   = input.texcoord0.xy;
    float4 shapeParams = LightSideShapeParams(input.tangent.w);
#if defined(LIGHTSIDE_GLYPHS)
    if (glyphMode < 1.5)
    {
        float4 t = LightSideLoadGlyphTransform(input.texcoord0.z);
        sdfScale = t.xx;
        atlasXY = input.texcoord0.xy * t.x + t.yz;
        pageLayer = t.w;
#if !defined(LIGHTSIDE_NO_GLYPH_UNIONS)
        shapeParams = LightSideGlyphUnionGate(input.texcoord0.z);
#endif
    }
#endif

#ifdef LIGHTSIDE_LIT_PASS
    // WorldBatcher writes per-quad world-space face normal into NORMAL
    // (cross product of actual quad edges — survives per-glyph rotation from modifiers).
    output.worldNormal = input.normal;

    // Per-vertex evaluation of up to 4 nearest non-important point/spot lights.
    // Compiled out when no scene non-important lights affect the object; the uniform
    // branch skips the ~40 ALU when the material is unlit (_LightInfluence == 0).
    output.vertexLight = 0;
    #ifdef VERTEXLIGHT_ON
        UNITY_BRANCH
        if (_LightInfluence > 0)
        {
            float3 worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
            output.vertexLight = Shade4PointLights(
                unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                unity_LightColor[0].rgb, unity_LightColor[1].rgb,
                unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                unity_4LightAtten0,
                worldPos, input.normal);
        }
    #endif
#endif

    output.vertex      = vPosition;
    output.atlasUV     = float4(atlasXY, pageLayer, glyphMode);
    output.glyphUV     = input.texcoord0.xy;
    output.cov         = input.texcoord2;
    output.paint       = input.texcoord3;
    output.color       = color;
    output.extra       = float4(faceDilate, glyphH, sdfScale.x, sdfScale.y);
    output.shapeGeom   = float4(input.texcoord0.zw, input.texcoord1.x, input.texcoord1.z);
    output.shapeParams = shapeParams;
    UNITY_TRANSFER_FOG(output, vPosition);

    return output;
}

#define LIGHTSIDE_SURFACE_VARYINGS pixel_t
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noboth/LightSideSurfaceResolve.hlsl"

#ifdef LIGHTSIDE_LIT_PASS
half3 ComputeLighting(half3 n, half3 vertexLight)
{
    half  NdotL   = max(0.0, dot(n, _WorldSpaceLightPos0.xyz));
    half3 ambient = ShadeSH9(half4(n, 1.0)) * _AmbientStrength;
    half3 direct  = _LightColor0.rgb * NdotL * _DirectStrength;
    return ambient + direct + vertexLight;
}

fixed4 PixShader(pixel_t input, float facing : VFACE) : SV_Target
#else
fixed4 PixShader(pixel_t input) : SV_Target
#endif
{
    UNITY_SETUP_INSTANCE_ID(input);
    half4 result = LightSideResolveSurface(input);

#ifdef LIGHTSIDE_LIT_PASS
    UNITY_BRANCH
    if (_LightInfluence > 0)
    {
        half3 n = normalize(input.worldNormal) * sign(facing);
        half3 lit = ComputeLighting(n, input.vertexLight);
        result.rgb = lerp(result.rgb, result.rgb * lit, _LightInfluence);
    }
#endif

    // Classic fog for premultiplied output: mix toward unity_FogColor premultiplied by alpha,
    // so distant fragments take the scene fog colour while preserving the glyph's alpha shape.
    // UNITY_APPLY_FOG_COLOR handles the per-vertex (mobile: fogCoord = factor) vs per-pixel
    // (desktop SM3+: fogCoord = clip z, factor via UNITY_CALC_FOG_FACTOR) split — raw fogCoord
    // is NOT a usable factor on desktop.
    #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        fixed4 fogCol = unity_FogColor;
        fogCol.rgb *= result.a;
        UNITY_APPLY_FOG_COLOR(input.fogCoord, result, fogCol);
    #endif

    return result;
}

#endif // LIGHTSIDE_SHADOW_CASTER

#endif // LIGHTSIDE_WORLD_CG_INCLUDED
