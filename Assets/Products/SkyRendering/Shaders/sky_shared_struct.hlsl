#ifndef SHARED_STRUCT_HLSL
#define SHARED_STRUCT_HLSL
//
// #define kPI 3.141592653589793
//
// const float kLog2 = log(2.0);
//
#define kPositionStrip 3
#define kNormalStrip   3
#define kUv0Strip      2
#define kTangentStrip  4
#define kPI 3.14159265359
//
// const float kMaxHalfFloat   = 65504.0f;
// const float kMax11BitsFloat = 65024.0f;
// const float kMax10BitsFloat = 64512.0f;
// const float3  kMax111110BitsFloat3 = float3(kMax11BitsFloat, kMax11BitsFloat, kMax10BitsFloat);
//
// struct CascadeShadowConfig
// {
//     int cascadeCount;
//     int percascadeDimXY;
//     float cascadeSplitLambda;
//     float maxDrawDepthDistance;
//
//     float shadowBiasConst; 
//     float shadowBiasSlope; 
//     float shadowFilterSize;
//     float maxFilterSize;
//
//     float cascadeBorderAdopt;
//     float cascadeEdgeLerpThreshold;
//     float pad0;
//     float pad1;
// };
//

//Volumetric Scattering 
struct AtmosphereParameters
{
	// 地理/高度
	float SeaLevel;
	float PlanetRadius;
	// 光照
	float AtmosphereHeight;
	float SunLightIntensity;

	float3 SunLightColor;
	float SunDiskAngle;

	// Rayleigh
	float RayleighScatteringScale;
	float RayleighScatteringScalarHeight;
	// Mie
	float MieScatteringScale;
	float MieAnisotropy;

	float MieScatteringScalarHeight;
	// Ozone
	float OzoneAbsorptionScale;
	float OzoneLevelCenterHeight;
	float OzoneLevelWidth;

	// Double Henyey-Greenstein Phase Function Parameters
	float4 MiePhaseParams; //xyz: g1, g2, w1
};

// All units in kilometers
struct CloudParameters
{

    // Cloud infos.
    float CloudAreaStartHeight; // km
    float CloudAreaThickness;
    float CloudGodRayScale;
    float CloudShadowExtent; // x4

    float3 CamWorldPos; // cameraworld Position, in atmosphere space unit.
    uint UpdateFaceIndex; // update face index for cloud cubemap capture

    // World space to cloud space view project matrix. Unit also is km.
    float4x4 CloudSpaceViewProject;
    float4x4 CloudSpaceViewProjectInverse;

    // Cloud settings.
    float2  CloudWeatherUVScale;
    float CloudCoverage;
    float CloudDensity;

    float CloudShadingSunLightScale;
    float CloudFogFade; 
    float CloudMaxTraceingDistance; 
    float CloudTracingStartMaxDistance; 

    float3 CloudDirection;
    float CloudSpeed;

    float CloudMultiScatterExtinction;
    float CloudMultiScatterScatter;
    float CloudBasicNoiseScale;
    float CloudDetailNoiseScale;

    float3  CloudAlbedo;
    float CloudPhaseForward;

    float CloudPhaseBackward;
    float CloudPhaseMixFactor;
    float CloudPowderScale;
    float CloudPowderPow;

    float CloudLightStepMul;
    float CloudLightBasicStep;
    int  CloudLightStepNum;
    int CloudEnableGroundContribution;

    int CloudMarchingStepNum;
    int CloudSunLitMapOctave;
    float CloudNoiseScale;
    int CloudGodRay;
};

struct SkyInfo
{
    float3  color;
    float intensity;

    float3  direction;
    int  shadowType; // Shadow type of this sky light.

    int rayTraceShadow; // = 0 is false, = 1 is true;
    int pad0;
    int pad1;
    int pad2;

	AtmosphereParameters atmosphereConfig;
    CloudParameters cloudConfig;
};



struct PerFrameData
{
    // .x is app runtime, .y is sin(.x), .z is cos(.x), .w is pad
    float4 appTime;

    // .x is frame count, .y is frame count % 8, .z is frame count % 16, .w is frame count % 32
    uint4 frameIndex;

    // Camera world space position.
    float4 camWorldPos;

    // .xyz is camera forward.
    float4 camForward;

    // .x fovy, .y aspectRatio, .z nearZ, .w farZ
    float4 camInfo;
    
    // prev-frame's cam info.
    float4 camInfoPrev;

    // Camera matrixs.
    float4x4 camView;
    float4x4 camProj;
    float4x4 camViewProj;

    // Camera inverse matrixs.
    float4x4 camInvertView;
    float4x4 camInvertProj;
    float4x4 camInvertViewProj;

    // Camera matrix remove jitter effects.
    float4x4 camProjNoJitter;
    float4x4 camViewProjNoJitter;

    // Camera invert matrixs no jitter effects.
    float4x4 camInvertProjNoJitter;
    float4x4 camInvertViewProjNoJitter;

    // Prev-frame camera infos.
    float4x4 camViewProjPrev;
    float4x4 camViewProjPrevNoJitter;

    // Camera frustum planes for culling.
    float4 frustumPlanes[6];

    // Halton sequence jitter data, .xy is current frame jitter data, .zw is prev frame jitter data.
    float4 jitterData;
    
    uint  jitterPeriod;        // jitter period for jitter data.
    uint  bEnableJitter;       // Is main camera enable jitter in this frame.
    float basicTextureLODBias; // Lod basic texture bias when render mesh, used when upscale need.
    uint  bCameraCut;          // Camera cut in this frame or not.

    uint skyValid; // sky is valid.
    uint skySDSMValid;
    float fixExposure;
    uint bAutoExposure;

    float renderWidth;
    float renderHeight;
    float displayWidth;
    float displayHeight;

    SkyInfo sky;
};

StructuredBuffer<PerFrameData> SkyFrameData;

#define frameData SkyFrameData[0]

float4x4 _PreviousCameraVP;

#define kMsCount 2 //代表模拟光弹射的次数层级

struct  ParticipatingMedia
{
	float extinctionCoefficients[kMsCount]; //消光系数
	float transmittanceToLight[kMsCount]; //到光源的————光透射率
	float extinctionAcc[kMsCount]; //消光累积
};
struct ParticipatingMediaPhase
{
	float phase[kMsCount];
};


#endif