#ifndef LIGHTSIDE_WORLD_SHADOW_URP_INCLUDED
#define LIGHTSIDE_WORLD_SHADOW_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_380_min/LightSideGlyphFieldURP.hlsl"
TEXTURE2D(_LightSidePaintTexture); SAMPLER(sampler_LightSidePaintTexture);
#define LIGHTSIDE_SAMPLE_PAINT(uv) SAMPLE_TEXTURE2D(_LightSidePaintTexture, sampler_LightSidePaintTexture, uv)
#define LIGHTSIDE_SAMPLE_IMAGE_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_GRAD(_LightSidePaintTexture, sampler_LightSidePaintTexture, uv, dx, dy)
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_380_min/LightSideShadowSurface.hlsl"
#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_380_min/LightSideShadowClamp.hlsl"

float3 _LightDirection;
float3 _LightPosition;
half   _ShadowCutoff;

struct ShadowVaryings
{
    float4 positionCS : SV_POSITION;
    float4 atlasUV    : TEXCOORD0;  // xy = atlas UV, z = page layer, w = glyph mode
    float4 cov        : TEXCOORD1;  // coverageMode, p0, p1, softness
    float4 meta       : TEXCOORD2;  // glyphUV.xy, faceDilate (color: vertex alpha), glyphH
    float4 shapeGeom : TEXCOORD3;
    float4 shapeParams : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float4 GetShadowPositionHClip(LightSideSurfaceVertex v)
{
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    // Batcher already writes a world-space face normal — use it directly.
    float3 normalWS   = v.normalOS;

    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    positionCS = LightSideApplyShadowClamping(positionCS);
    return positionCS;
}

ShadowVaryings ShadowVert(LightSideSurfaceVertex v)
{
    ShadowVaryings o = (ShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);

    o.positionCS = GetShadowPositionHClip(v);

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

half4 ShadowFrag(ShadowVaryings i) : SV_Target
{
    clip(LightSideShadowClip(i.atlasUV, i.cov, i.meta, i.shapeGeom, i.shapeParams, _ShadowCutoff));
    return 0;
}

#endif
