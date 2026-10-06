#ifndef LIGHTSIDE_IMAGE_INCLUDED
#define LIGHTSIDE_IMAGE_INCLUDED

#include "UnityCG.cginc"

sampler2D _ImageSource;
float4x4 _ImagePlaneToUv;
float4 _ImageSourceUvRect;
float4 _ImageOutputBounds;

struct LightSideImageVertex
{
    float4 vertex : POSITION;
};

struct LightSideImageVaryings
{
    float4 vertex : SV_POSITION;
    float2 plane : TEXCOORD0;
};

LightSideImageVaryings LightSideImageVert(LightSideImageVertex input)
{
    LightSideImageVaryings output;
    output.vertex = UnityObjectToClipPos(float4(input.vertex.xy, 0, 1));
    output.plane = _ImageOutputBounds.xy + input.vertex.xy * _ImageOutputBounds.zw;
    return output;
}

half4 LightSideImageSample(float2 plane)
{
    float2 uv = mul(_ImagePlaneToUv, float4(plane, 0, 1)).xy;
    half inside = step(_ImageSourceUvRect.x, uv.x) * step(_ImageSourceUvRect.y, uv.y) *
        step(uv.x, _ImageSourceUvRect.z) * step(uv.y, _ImageSourceUvRect.w);
    return tex2D(_ImageSource, uv) * inside;
}

#endif
