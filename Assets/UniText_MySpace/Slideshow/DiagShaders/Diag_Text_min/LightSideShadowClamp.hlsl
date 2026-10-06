// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// Near-plane depth clamp for URP shadow casters. URP's own ApplyShadowClamping helper exists only
// in URP 17+ (Unity 6); URP 12-16 inline this exact clamp in ShadowCasterPass.hlsl — so a package
// supporting URP 12-17 must carry its own copy. Include after the URP Core.hlsl include
// (UNITY_REVERSED_Z / UNITY_NEAR_CLIP_VALUE come from there).

#ifndef LIGHTSIDE_SHADOW_CLAMP_INCLUDED
#define LIGHTSIDE_SHADOW_CLAMP_INCLUDED

float4 LightSideApplyShadowClamping(float4 positionCS)
{
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return positionCS;
}

#endif
