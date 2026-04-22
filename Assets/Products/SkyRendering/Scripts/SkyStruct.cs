using UnityEngine;
using System.Runtime.InteropServices;
using Unity.Mathematics;

// ==========================================
// 1. C# 内存映射结构体定义 (严格保持 16 字节对齐)
// ==========================================

[StructLayout(LayoutKind.Sequential)]
public struct AtmosphereParameters
{
    public float SeaLevel;
    public float PlanetRadius;
    public float AtmosphereHeight;
    public float SunLightIntensity;

    public Vector3 SunLightColor;
    public float SunDiskAngle;

    public float RayleighScatteringScale;
    public float RayleighScatteringScalarHeight;
    public float MieScatteringScale;
    public float MieAnisotropy;

    public float MieScatteringScalarHeight;
    public float OzoneAbsorptionScale;
    public float OzoneLevelCenterHeight;
    public float OzoneLevelWidth;

    public Vector4 MiePhaseParams;
}

[StructLayout(LayoutKind.Sequential)]
public struct CloudParameters
{
    public float CloudAreaStartHeight;
    public float CloudAreaThickness;
    public float CloudGodRayScale;
    public float CloudShadowExtent;

    public Vector3 CamWorldPos;
    public uint UpdateFaceIndex;

    public Matrix4x4 CloudSpaceViewProject;
    public Matrix4x4 CloudSpaceViewProjectInverse;

    public Vector2 CloudWeatherUVScale;
    public float CloudCoverage;
    public float CloudDensity;

    public float CloudShadingSunLightScale;
    public float CloudFogFade;
    public float CloudMaxTraceingDistance;
    public float CloudTracingStartMaxDistance;

    public Vector3 CloudDirection;
    public float CloudSpeed;

    public float CloudMultiScatterExtinction;
    public float CloudMultiScatterScatter;
    public float CloudBasicNoiseScale;
    public float CloudDetailNoiseScale;

    public Vector3 CloudAlbedo;
    public float CloudPhaseForward;

    public float CloudPhaseBackward;
    public float CloudPhaseMixFactor;
    public float CloudPowderScale;
    public float CloudPowderPow;

    public float CloudLightStepMul;
    public float CloudLightBasicStep;
    public int CloudLightStepNum;
    public int CloudEnableGroundContribution;

    public int CloudMarchingStepNum;
    public int CloudSunLitMapOctave;
    public float CloudNoiseScale;
    public int CloudGodRay;
}

[StructLayout(LayoutKind.Sequential)]
public struct SkyInfo
{
    public Vector3 color;
    public float intensity;

    public Vector3 direction;
    public int shadowType;

    public int rayTraceShadow;
    public int pad0;
    public int pad1;
    public int pad2;

    public AtmosphereParameters atmosphereConfig;
    public CloudParameters cloudConfig;
}
[StructLayout(LayoutKind.Sequential)]
public struct PerFrameData
{
    public Vector4 appTime;
    public uint4 frameIndex;
    public Vector4 camWorldPos;
    public Vector4 camForward;
    public Vector4 camInfo;
    public Vector4 camInfoPrev;

    public Matrix4x4 camView;
    public Matrix4x4 camProj;
    public Matrix4x4 camViewProj;

    public Matrix4x4 camInvertView;
    public Matrix4x4 camInvertProj;
    public Matrix4x4 camInvertViewProj;

    public Matrix4x4 camProjNoJitter;
    public Matrix4x4 camViewProjNoJitter;

    public Matrix4x4 camInvertProjNoJitter;
    public Matrix4x4 camInvertViewProjNoJitter;

    public Matrix4x4 camViewProjPrev;
    public Matrix4x4 camViewProjPrevNoJitter;

    // 展开数组以兼容 StructLayout
    public Vector4 frustumPlane0;
    public Vector4 frustumPlane1;
    public Vector4 frustumPlane2;
    public Vector4 frustumPlane3;
    public Vector4 frustumPlane4;
    public Vector4 frustumPlane5;

    public Vector4 jitterData;

    public uint jitterPeriod;
    public uint bEnableJitter;
    public float basicTextureLODBias;
    public uint bCameraCut;

    public uint skyValid;
    public uint skySDSMValid;
    public float fixExposure;
    public uint bAutoExposure;

    public float renderWidth;
    public float renderHeight;
    public float displayWidth;
    public float displayHeight;

    public SkyInfo sky;
}
