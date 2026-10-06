#ifndef LIGHTSIDE_IMAGE_COLOR_INCLUDED
#define LIGHTSIDE_IMAGE_COLOR_INCLUDED

#include "LightSideColorMatrix.hlsl"

float4 _ImageColors[16];
int _ImageColorCount;

half4 LightSideApplyImageColor(half4 value)
{
    [loop] for (int i = 0; i < _ImageColorCount; i++)
    {
        float4 operation = _ImageColors[i];
        if (operation.x >= 0 && operation.y != 0)
            value = lerp(value, LightSideApplyColorMatrixPremultiplied(value, operation.x), operation.y);
    }
    return value;
}

#endif
