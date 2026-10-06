#ifndef LIGHTSIDE_COLOR_MATRIX_INCLUDED
#define LIGHTSIDE_COLOR_MATRIX_INCLUDED

#ifdef LIGHTSIDE_SAMPLE_MATRIX
half4 LightSideApplyColorMatrixPremultiplied(half4 pm, float row)
{
    float v = (row + 0.5) / max(_LightSideColorMatrixRows, 1.0);
    half4 r0 = LIGHTSIDE_SAMPLE_MATRIX(0.5 / 3.0, v);
    half4 r1 = LIGHTSIDE_SAMPLE_MATRIX(1.5 / 3.0, v);
    half4 r2 = LIGHTSIDE_SAMPLE_MATRIX(2.5 / 3.0, v);
    half3 rgb = half3(dot(r0.xyz, pm.rgb), dot(r1.xyz, pm.rgb), dot(r2.xyz, pm.rgb))
        + half3(r0.w, r1.w, r2.w) * pm.a;
    pm.rgb = clamp(rgb, 0.0, pm.a);
    return pm;
}

half3 LightSideApplyColorMatrix(half3 rgb, float row)
{
    return LightSideApplyColorMatrixPremultiplied(half4(rgb, 1.0), row).rgb;
}
#endif

#endif
