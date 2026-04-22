#ifndef __PIXEL_DEPTH_OFFSET_HLSL__
#define __PIXEL_DEPTH_OFFSET_HLSL__

// #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_G_PDO_AlbedoTexture);
SAMPLER(sampler_G_PDO_AlbedoTexture);

TEXTURE2D(_G_PDO_NormalTexture);
SAMPLER(sampler_G_PDO_NormalTexture);


TEXTURE2D(_G_PDO_DepthTexture);
SAMPLER(sampler_G_PDO_DepthTexture);

inline void MixPixelDepthOffset(float3 positionWS, half differ,float3 originAlbedo,float3 originNormalWS,  out float3 albedo, out float3 normalWS)
{   
    // 由于使用同一个相机,可用裁剪空间坐标做为UV
    float4 positionCS = TransformWorldToHClip(positionWS);
    half2 uv = 0.5 * (positionCS.xy / positionCS.w) + 0.5;
    #if UNITY_UV_STARTS_AT_TOP
    uv.y = 1 - uv.y;
    #endif
    
    // Albedo
    const float4 albedoColor = SAMPLE_TEXTURE2D(_G_PDO_AlbedoTexture, sampler_G_PDO_AlbedoTexture, uv);
    
    // Scene depth at current screen UV
    const float rawSceneDepth = SAMPLE_TEXTURE2D(_G_PDO_DepthTexture, sampler_G_PDO_DepthTexture, uv).r;
    const float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
    // Current fragment eye depth
    const float thisEyeDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());

    // Positive when terrain is in front of the scene surface
    float dz = sceneEyeDepth - thisEyeDepth;

    // Base blend by depth gap with a soft threshold
    half t = smoothstep(0.0, differ, dz);

    // Backface/behind fix: if terrain is behind, keep original surface
    t *= step(0.0, dz);
    t+= 0.0001; // 避免完全一样时出现问题

    //增加dither
    // float2 screenUV = positionCS.xy / positionCS.w;
    // #if UNITY_UV_STARTS_AT_TOP
    // screenUV.y = 1 - screenUV.y;
    // #endif
    // float dither = frac(sin(dot(screenUV ,float2(12.9898,78.233))) * 43758.5453);
    // t = saturate(t + (dither - 0.5) * 0.1);

    albedo = lerp(albedoColor.rgb,originAlbedo, t);
    

    // nromal
    normalWS = SAMPLE_TEXTURE2D(_G_PDO_NormalTexture, sampler_G_PDO_NormalTexture, uv).rgb;
    normalWS = 2.0 * normalWS - 1.0;
    normalWS = lerp(normalWS, originNormalWS, t);
    
}

void MixPixelDepthOffset_float(float3 positionWS, half differ, float3 originAlbedo,float3 originNormalWS,out float3 albedo,out float3 normalWS)
{   
    MixPixelDepthOffset(positionWS,differ,originAlbedo.xyz,originNormalWS,albedo,normalWS);
}

#endif
