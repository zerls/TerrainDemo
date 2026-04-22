#ifndef CLOUD_COMMON_HLSL
#define CLOUD_COMMON_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "sky_shared_struct.hlsl"
#include "shared_functions.hlsl"
#include "atmosphere_helper.hlsl"

#define CLOUD_SHAPE 0
#define CLOUD_PHYSICS 1
// Min max sample count define.
#define kGroundOcc 0.5
#define kSkyMsExition frameData.sky.cloudConfig.CloudMultiScatterExtinction


//===================== 输入资源 ==============================
// 每帧天空参数常量缓冲，包含云层配置、时间等全局数据

Texture2D _inCloudColorTex;
Texture2D _inCloudFogTex;
Texture2D _inCloudDepthTex;

Texture3D inBasicNoise; // 低频 3D Perlin-Worley 基础噪声，决定云的整体形状
Texture3D inDetailNoise; // 高频 3D Worley 细节噪声，为云边缘添加蓬松感
Texture2D inWeatherTexture; // 天气纹理：R 通道存储覆盖率，用于控制云层分布
Texture2D inCloudCurlNoise; // Curl 噪声纹理：用于局部扰动与 FBM 模式采样
Texture2D inBlueNoise;

// Texture2D inHiz;
Texture2D inSkyViewLut;
Texture2D inSkyIrradiance;
Texture2D inTransmittanceLut;
Texture3D inFroxelScatter;
Texture2D _aerialPerspectiveLut;

SamplerState linearClampEdgeSampler; // 线性过滤 + 边缘钳制
SamplerState linearRepeatSampler; // 线性过滤 + 重复平铺（噪声采样主要使用此采样器）
SamplerState pointClampEdgeSampler; // 点采样 + 边缘钳制
SamplerState pointRepeatSampler; // 点采样 + 重复平铺
SamplerState sampler_TrilinearRepeat;

float _DensityThreshold;
float _DensityPower;
float _AerialPerspectiveDistance;
float4 _AerialPerspectiveVoxelSize;

TEXTURE2D_X(_CameraOpaqueTexture);              SAMPLER(sampler_CameraOpaqueTexture);

//=============================================================

ParticipatingMediaPhase getParticipatingMediaPhase(float basePhase, float baseMsPhaseFactor)
{
    ParticipatingMediaPhase participatingMediaPhase;
    participatingMediaPhase.phase[0] = basePhase;

    const float uniformPhase = getUniformPhase();
    float MsPhaseFactor = baseMsPhaseFactor;

    for (int ms = 1; ms < kMsCount; ms++)
    {
        participatingMediaPhase.phase[ms] = lerp(uniformPhase, participatingMediaPhase.phase[0], MsPhaseFactor);
        MsPhaseFactor *= MsPhaseFactor;
    }

    return participatingMediaPhase;
}


float SampleCloudDensity(float3 worldPos, float normalizeHeight)
{
    // normalizeHeight =0.5f;
    const float kCoverage = frameData.sky.cloudConfig.CloudCoverage; // 全局云覆盖率 [0,1]
    const float kDensity = frameData.sky.cloudConfig.CloudDensity; // 全局云密度缩放
    const float kCloudNoiseScale = frameData.sky.cloudConfig.CloudNoiseScale;

    const float3 windDirection = normalize(frameData.sky.cloudConfig.CloudDirection); // 风向（归一化）
    const float cloudSpeed = frameData.sky.cloudConfig.CloudSpeed; // 风速

    float3 posMeter = worldPos + windDirection * normalizeHeight * 500.0f;

    float3 windOffset = (windDirection + float3(0.0, 0.1, 0.0)) * _Time.y * cloudSpeed;
    float3 posKm = posMeter * 0.001; // 转换为千米，便于全局噪声尺度采样      

    float2 sampleUv = kCloudNoiseScale * posKm.xz * frameData.sky.cloudConfig.CloudWeatherUVScale * 0.01 + windOffset.xz * 0.001; // 天气图采样UV，加入风速偏移
    float4 weatherValue = inWeatherTexture.SampleLevel(linearRepeatSampler, sampleUv, 0);

    // --- 局部覆盖率扰动 ---
    // 用大尺度 Curl 噪声产生云团疏密变化，避免覆盖率过于均匀
    float localCoverage = inCloudCurlNoise.SampleLevel(linearRepeatSampler,(kCloudNoiseScale * _Time.y * cloudSpeed * 50.0 + posMeter.xz) * 0.000001 + 0.5, 0).x;
    localCoverage = saturate(localCoverage * 3.0 - 0.75) * 0.2; // 压缩到小幅扰动范围
    // 最终覆盖率 = 全局覆盖率 × (局部扰动 + 天气图覆盖率)
    float coverage = saturate(kCoverage * (localCoverage + weatherValue.x));

    // --- 高度梯度遮罩 ---
    // 云底（0~10%）由薄到厚，云顶（10%~80%）由厚渐薄，使云层呈典型砧状或积云轮廓
    float gradienShape = remap(normalizeHeight, 0.00, 0.10, 0.1, 1.0) * remap(normalizeHeight, 0.10, 0.80, 1.0, 0.2);
    // --- 基础噪声采样 ---
    float cloudBasicNoiseScale = frameData.sky.cloudConfig.CloudBasicNoiseScale;
    float basicNoise = inBasicNoise.SampleLevel(sampler_TrilinearRepeat, kCloudNoiseScale * posKm * cloudBasicNoiseScale + windOffset, 0).r;
    float basicCloudNoise = gradienShape * basicNoise; // 高度梯度调制基础噪声
    // basicCloudNoise =basicNoise;
    // 使用 remap 将基础噪声在覆盖率阈值以下裁掉，实现硬边界云团
    float basicCloudWithCoverage = coverage * remap(basicCloudNoise, 1.0 - coverage, 1, 0, 1);

    // --- 细节噪声采样 ---
    // 细节噪声偏移方向与基础噪声相反，速度更慢，模拟云边缘的细碎蓬松感
    float3 sampleDetailNoise = posKm - windOffset * 0.15;
    float cloudDetailNoiseScale = frameData.sky.cloudConfig.CloudDetailNoiseScale;
    float detailNoiseComposite = inDetailNoise.SampleLevel(linearRepeatSampler, kCloudNoiseScale * sampleDetailNoise * cloudDetailNoiseScale, 0).r;
    // float detailNoiseComposite =SAMPLE_TEXTURE2D_X(inDetailNoise,linearRepeatSampler,kCloudNoiseScale * sampleDetailNoise * cloudDetailNoiseScale).r;

    // 低高度处用正向细节（增加蓬松），高处翻转（产生卷云/薄云效果）
    float detailNoiseMixByHeight = 0.2 * lerp(detailNoiseComposite, 1 - detailNoiseComposite, saturate(normalizeHeight * 10.0));

    // --- 密度形状遮罩 ---
    // 云底（0~10%）和云顶（80%~100%）平滑淡出，中间层密度最高
    float densityShape = saturate(0.01 + normalizeHeight * 1.15) * kDensity *
        remap(normalizeHeight, 0.0, 0.1, 0.0, 1.0) *
        remap(normalizeHeight, 0.8, 1.0, 1.0, 0.0);

    float cloudDensity = remap(basicCloudWithCoverage, detailNoiseMixByHeight, 1.0, 0.0, 1.0);

    float density = saturate(pow(cloudDensity * densityShape, 2.0));

    density = pow((saturate(density - _DensityThreshold)), _DensityPower);
    return density;
}

float SampleCloudDensityKm(float3 samplePosKmrWS)
{
    float3 samplePosMeterWS = samplePosKmrWS * 1000.0f; // 转换回米单位
    float sampleHeight = length(samplePosMeterWS) - frameData.sky.atmosphereConfig.PlanetRadius;
    float normalizeHeight = saturate((sampleHeight - frameData.sky.cloudConfig.CloudAreaStartHeight)
        / max(HALF_EPS, frameData.sky.cloudConfig.CloudAreaThickness));
    return SampleCloudDensity(samplePosMeterWS, normalizeHeight);
}

float SampleCloudDensityKm(float3 samplePosKmrWS, float normalizeHeight, float distanceKm)
{
    float3 samplePosMeterWS = samplePosKmrWS * 1000.0f; // 转换回米单位
    // 将千米距离映射为 LOD (例如：每 10 公里增加 1 级，最大限制到 5 级)
    //TODO 这里的 0.1 和 5.0 最好可以提升到 frameData.sky.cloudConfig 中作为全局可调参数
    float mipLod = clamp(distanceKm * 0.1, 0.0, 5.0);
    return SampleCloudDensity(samplePosMeterWS, normalizeHeight);
}

// 光照步进（Light Marching）
// 计算当前点向太阳方向的体积阴影（透射率），支持多重散射近似
// posKm: 当前采样点在世界空间的位置（千米）
// sunDirection: 太阳光线方向
// fixNum: 强制指定的步进次数（如果 > 0，则覆盖配置表中的默认次数）
// msExtinctionFactor: 多重散射的衰减因子（控制光在云中多次弹射后损失的能量比例）
ParticipatingMedia GetLightTransmittance(float3 posKm, float3 sunDirection, int fixNum, float msExtinctionFactor)
{
    ParticipatingMedia participatingMedia;

    int ms = 0;

    // 分别记录不同多重散射层级（Octaves）的累积衰减量和当前步的衰减系数
    // kMsCount 通常为 2 或 3，代表模拟光弹射的次数层级
    float extinctionAccumulation[kMsCount];
    float extinctionCoefficients[kMsCount];

    // 初始化数组
    for (ms = 0; ms < kMsCount; ms++)
    {
        extinctionAccumulation[ms] = 0.0f;
        extinctionCoefficients[ms] = 0.0f;
    }

    // 步长倍增器：为了优化性能，向太阳步进时通常采用非均匀步长。
    // 离当前点越近，步长越小（精度高）；离得越远，步长成倍增加（节省性能）。
    const float kStepLMul = frameData.sky.cloudConfig.CloudLightStepMul;
    // 确定向太阳步进的总次数
     const uint kStepLight = fixNum > 0 ? (uint)fixNum : frameData.sky.cloudConfig.CloudLightStepNum;
    
    // if (fixNum == 0)
    // {
    //     for (uint m = 0; m < kMsCount; m++)
    //     {
    //         participatingMedia.transmittanceToLight[ms] =0.0f;
    //         participatingMedia.extinctionCoefficients[ms] = 99999.0f;
    //     }
    //     return participatingMedia;
    // } //如果强制步进为 0，直接返回全黑（0透射率，极大衰减）
    
    // 初始的基础步长（千米）
    float stepL = frameData.sky.cloudConfig.CloudLightBasicStep;

    // 初始采样偏移量，通常偏移半个步长以进行中心点采样
    float d = stepL * 0.5;

    // 沿着光线方向收集总的云层密度（光学厚度）
    for (uint j = 0; j < kStepLight; j++)
    {
        // 计算当前光照采样点的世界坐标（千米）
        float3 samplePosKm = posKm + sunDirection * d;

        // 坐标转为米，用于采样云层 3D Noise 纹理
        float3 samplePosMeter = samplePosKm * 1000.0f;

        // 计算相对于球心的高度，用于获取归一化高度 [0, 1]
        float sampleHeight = length(samplePosMeter - frameData.sky.atmosphereConfig.PlanetRadius);
        float sampleDt = sampleHeight - frameData.sky.cloudConfig.CloudAreaStartHeight;
        float normalizeHeight = sampleDt / frameData.sky.cloudConfig.CloudAreaThickness;

        // 【单次散射层级 ms = 0】
        // 采样当前点的云密度，并将其作为基础衰减系数
        extinctionCoefficients[0] = SampleCloudDensity(samplePosMeter, normalizeHeight);
        // 累加当前步的光学厚度 (密度 * 步长)
        extinctionAccumulation[0] += extinctionCoefficients[0] * stepL;

        // 【多重散射层级 ms > 0】
        // 核心思想：光在云里弹射次数越多（层级越高），它感受到的"阻力(衰减)"就越小，
        // 这解释了为什么厚云的边缘由于光线多次内部折射而显得更加透亮。
        float MsExtinctionFactor = msExtinctionFactor;
        for (ms = 1; ms < kMsCount; ms++)
        {
            // 高阶层级的衰减系数等于上一阶乘以多重散射衰减因子 (因子通常 < 1)
            extinctionCoefficients[ms] = extinctionCoefficients[ms - 1] * MsExtinctionFactor;

            // 下一阶的衰减因子会继续呈指数减小 (Frostbite 引擎的多重散射经验公式)
            MsExtinctionFactor *= MsExtinctionFactor;

            // 累加高阶层级的光学厚度
            extinctionAccumulation[ms] += extinctionCoefficients[ms] * stepL;
        }

        // 步进距离增加
        d += stepL;
        // 步长成倍增加（非均匀步进），加速跳出云层
        stepL *= kStepLMul;
    }

    // 将累积的光学厚度转换为透射率（Transmittance）
    for (ms = 0; ms < kMsCount; ms++)
    {
        // 根据比尔-朗伯定律 (Beer-Lambert Law)：透射率 = exp(-光学厚度)
        // 注意：前面的距离单位是千米(km)，这里乘以 1000 转成米(m)，以匹配真实物理世界的光学衰减率
        participatingMedia.transmittanceToLight[ms] = exp(-extinctionAccumulation[ms] * 1000.0);

        // 保存累积的衰减值供外部使用（转换为米）
        participatingMedia.extinctionAcc[ms] = extinctionAccumulation[ms] * 1000.0;
    }

    return participatingMedia;
}

//Based on code from DMEville https://www.youtube.com/watch?v=0G8CVQZhMXw
//Uses 3D texture and lighting 
float4 cloudColorCompute(float3 worldPos, float3 cloudSampleOffset, float2 uv, float blueNoise,inout float cloudZ)
{
    float3 viewDir =  worldPos - _WorldSpaceCameraPos;
    float3 worldDir = normalize(viewDir);

    // 地球中心坐标（假设地球在原点下方 PlanetRadius 处）
    // 提取星球和云层的半径范围
    float earthRadius = WorldToAtmosphereUnit(frameData.sky.atmosphereConfig.PlanetRadius);
    float radiusCloudStart = earthRadius + WorldToAtmosphereUnit(frameData.sky.cloudConfig.CloudAreaStartHeight);
    float radiusCloudEnd = radiusCloudStart + WorldToAtmosphereUnit(frameData.sky.cloudConfig.CloudAreaThickness);

    float3 CameraPosWSEarthKm = convertToAtmosphereUnit(_WorldSpaceCameraPos) + float3(0.0, earthRadius, 0.0); // 转换为千米单位，并调整到以地心为原点的坐标系
    float viewHeight = length(CameraPosWSEarthKm); // 当前位置距离地心的高度（大气单位）

    float3 rayOrigin = CameraPosWSEarthKm;
    float3 rayDirection = worldDir;

    // ==========================================
    // 第一阶段：射线与云层球体包围盒求交 (Ray-Sphere Intersection)
    // ==========================================
    float tMin; // 射线进入云层的距离
    float tMax; // 射线穿出云层的距离
    bool bEarlyOutCloud = false; // 是否可以直接跳过云层渲染
    {
        if (viewHeight < radiusCloudStart)
        {
            // 1. 相机在云层下方 (地面上)
            if (hitSphereNearest(CameraPosWSEarthKm, worldDir, earthRadius) > 0.0)
            {
                // 射线打到了地面，提前结束（比如低头看地）
                bEarlyOutCloud = true;
            }
            // 计算与云层下边缘和上边缘的交点
            tMin = hitSphereInside(CameraPosWSEarthKm, worldDir, radiusCloudStart);
            tMax = hitSphereInside(CameraPosWSEarthKm, worldDir, radiusCloudEnd);
        }
        else if (viewHeight > radiusCloudEnd)
        {
            // 2. 相机在云层上方 (太空中)
            float2 tEnd = float2(0.0, 0.0);
            if (!hitSphereOutside(CameraPosWSEarthKm, worldDir, radiusCloudEnd, tEnd))
            {
                // 射线完全没碰到云层外边缘，提前结束（比如抬头看宇宙）
                bEarlyOutCloud = true;
            }
            else
            {
                float2 tStartOuter = float2(0.0, 0.0);
                if (hitSphereOutside(CameraPosWSEarthKm, worldDir, radiusCloudStart, tStartOuter))
                {
                    // 射线穿过了整个云层（从上往下看，穿透上下边缘）
                    tMin = tEnd.x;
                    tMax = tStartOuter.x;
                }
                else
                {
                    // 射线只是擦过了云层外边缘，没有触及内边缘（视线掠角）
                    tMin = tEnd.x;
                    tMax = tEnd.y;
                }
            }
        }
        else
        {
            // 3. 相机就在云层内部
            float tHitStart = hitSphereNearest(CameraPosWSEarthKm, worldDir, radiusCloudStart);
            if (tHitStart > 0.0)
            {
                // 视线向下看，打到云底
                tMax = tHitStart;
            }
            else
            {
                // 视线向上看，打到云顶
                tMax = hitSphereInside(CameraPosWSEarthKm, worldDir, radiusCloudEnd);
            }
            tMin = 0.0f; // 起点就是相机位置
        }
        // 钳制负值
        tMin = max(tMin, 0.0);
        tMax = max(tMax, 0.0);

        // 如果交点错误，或者云层离相机太远超过了最大追踪距离，则跳过
        if (tMax <= tMin || tMin > (frameData.sky.cloudConfig.CloudTracingStartMaxDistance))
        {
            bEarlyOutCloud = true;
        }
    }
        // ================ Test
        if (bEarlyOutCloud == true)
            return half4(0.0, 0.0, 0.0, 1.0);

    // ==========================================
    // 第二阶段：步进参数准备
    // ==========================================
    // 限制最大步进距离，优化性能 
    const float marchingDistance = min((frameData.sky.cloudConfig.CloudMaxTraceingDistance), tMax - tMin);
    tMax = tMin + marchingDistance;

    const uint stepCountUnit = frameData.sky.cloudConfig.CloudMarchingStepNum;
    const float stepCount = float(stepCountUnit);
    float stepT = (tMax - tMin) / stepCount + 0.001; // 计算单步步长
    float sampleT = tMin + 0.001 * stepT; // 加上一个微小的偏移，避免自相交
    // 结合蓝噪声进行起点抖动，把严重的 Banding 变成细微的 Noise
    sampleT += stepT * blueNoise;

    int numSteps = frameData.sky.cloudConfig.CloudMarchingStepNum;

    float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
    Light mainLight = GetMainLight(shadowCoord);
    float3 lightDir = mainLight.direction;
    // lightDir =float3(0,1,0);
    float3 sunColor = mainLight.color * mainLight.shadowAttenuation;
    float3 sunDirection = normalize(lightDir); // 太阳光方向
    float VoL = dot(rayDirection, sunDirection); // 视线与太阳方向的点积，用于后面计算相函数

    //===========================================
    //---- 第三阶段：光线步进核心循环 (Ray Marching)-----
    float transmittance = 1.0; // 初始透射率为 1 (完全透明)
    float3 scatteredLight = float3(0.0, 0.0, 0.0); // 累积的云层散射光
    // ---------------------------------------

    // 使用双叶 Henyey-Greenstein 相函数，混合前向散射(银边效果)和后向散射
    float phase = dualLobPhase(frameData.sky.cloudConfig.CloudPhaseForward, frameData.sky.cloudConfig.CloudPhaseBackward,
                               frameData.sky.cloudConfig.CloudPhaseMixFactor, - VoL);

    ParticipatingMediaPhase participatingMediaPhase = getParticipatingMediaPhase(phase, 0.5);

    float3 rayHitPos = float3(0.0, 0.0, 0.0);
    float rayHitPosWeight = 0.0;
    
    // 优化点：不在每一步都采样大气透射率LUT，而是只采样起点(tMin)和终点(tMax)，然后在步进中做线性插值
    float3 atmosphereTransmittance0;
    {
        float3 samplePos = CameraPosWSEarthKm + sampleT * rayDirection; // 乘加指令 (MAD) 优化
        float sampleHeight = length(samplePos);
        const float3 upVector = samplePos / sampleHeight;
        float viewZenithCosAngle = dot(sunDirection, upVector);
        float2 sampleUv;
        lutTransmittanceParamsToUv(viewHeight, viewZenithCosAngle, sampleUv);
        atmosphereTransmittance0 = inTransmittanceLut.SampleLevel(linearClampEdgeSampler, sampleUv, 0).rgb;
    }
    float3 atmosphereTransmittance1;
    {
        float3 samplePos = CameraPosWSEarthKm + tMax * rayDirection;
        float sampleHeight = length(samplePos);
        const float3 upVector = samplePos / sampleHeight;
        float viewZenithCosAngle = dot(sunDirection, upVector);
        float2 sampleUv;
        lutTransmittanceParamsToUv(viewHeight, viewZenithCosAngle, sampleUv);
        atmosphereTransmittance1 = inTransmittanceLut.SampleLevel(linearClampEdgeSampler, sampleUv, 0.0).rgb;
    }

    const float3 upScaleColor = inSkyIrradiance.Sample(linearClampEdgeSampler, float3(0, 1, 0)).rgb;
    
    for (int i = 0; i < numSteps; i++)
    {
        float3 samplePos = (cloudSampleOffset + rayOrigin) + sampleT * rayDirection; // 乘加指令 (MAD) 优化

        float3 atmosphereTransmittance = lerp(atmosphereTransmittance0, atmosphereTransmittance1, saturate(sampleT / marchingDistance)); //大气-透射率

        float sampleHeight = length(samplePos * 1000.0f) - frameData.sky.atmosphereConfig.PlanetRadius;
        float normalizeHeight = saturate((sampleHeight - frameData.sky.cloudConfig.CloudAreaStartHeight) / max(HALF_EPS, frameData.sky.cloudConfig.CloudAreaThickness));

        float stepCloudDensity = SampleCloudDensityKm(samplePos, normalizeHeight,sampleT);

        // 记录加权平均命中位置，用于后续计算深度和空气透视雾
        rayHitPos += samplePos * transmittance;
        rayHitPosWeight += transmittance;

        
        if (stepCloudDensity > 0.0)
        {
            // density += stepCloudDensity * frameData.sky.cloudConfig.CloudDensity;
            float opticalDepth = stepCloudDensity * stepT * 1000.0;
            // density += opticalDepth;

            // Siggraph 2017 Decima 引擎改良的步进透射率公式 (Beer's Law 变体，改善边缘过渡)
            float stepTransmittance = max(exp(-opticalDepth), exp(-opticalDepth * 0.25) * 0.7);

            // 向着太阳步进采样，计算体积阴影（即太阳光穿透云层到达该点剩余的光量）
            ParticipatingMedia participatingMedia = GetLightTransmittance(samplePos, sunDirection, -1,
                                                                          frameData.sky.cloudConfig.CloudMultiScatterExtinction);

            // 可选：计算地面的反射/遮蔽
            ParticipatingMedia participatingMediaAmbient;
            if (frameData.sky.cloudConfig.CloudEnableGroundContribution != 0)
            {
                participatingMediaAmbient = GetLightTransmittance(samplePos, float3(0, 1, 0), -1, kSkyMsExition);
            }
            // 粉末效应 (Powder Effect)：云层边缘因为多重散射较弱，反而看起来发黑的物理现象 
            float powderEffect = 1.0;
            {
                float depthProbability = pow(
                    clamp(stepCloudDensity * 8.0 * frameData.sky.cloudConfig.CloudPowderPow, 0.0, frameData.sky.cloudConfig.CloudPowderScale),
                    remap(normalizeHeight, 0.3, 0.85, 0.5, 2.0));
                depthProbability += 0.05;
                float verticalProbability = pow(remap(normalizeHeight, 0.07, 0.22, 0.1, 1.0), 0.8);
                powderEffect = powderEffectNew(depthProbability, verticalProbability, - VoL);
                
            }

            // 最终太阳光到达此处的能量
            float3 sunlightTerm = atmosphereTransmittance * frameData.sky.cloudConfig.CloudShadingSunLightScale * sunColor;
            // 天空环境光补光
            float3 ambientLit = upScaleColor * powderEffect * (1.0 - - sunDirection.y) * atmosphereTransmittance;

            // ==========================================
            // 3.2 多重散射近似 (Multiple Scattering)
            // ==========================================

            float sigmaS = stepCloudDensity; // 散射系数
            float sigmaE = max(sigmaS, 1e-8f); // 衰减系数 (由于假定云没有吸收，衰减=散射)

            float3 scatteringCoefficients[kMsCount];
            float extinctionCoefficients[kMsCount];
            float3 albedo = frameData.sky.cloudConfig.CloudAlbedo; // 反照率，决定云的颜色，通常是白色

            scatteringCoefficients[0] = sigmaS * albedo;
            extinctionCoefficients[0] = sigmaE;

            float MsExtinctionFactor = frameData.sky.cloudConfig.CloudMultiScatterExtinction;
            float MsScatterFactor = frameData.sky.cloudConfig.CloudMultiScatterScatter;

            // 预计算多个 Octave 的散射和衰减参数（模拟光在云中弹射多次）
            int ms;
            for (ms = 1; ms < kMsCount; ms++)
            {
                extinctionCoefficients[ms] = extinctionCoefficients[ms - 1] * MsExtinctionFactor;
                scatteringCoefficients[ms] = scatteringCoefficients[ms - 1] * MsScatterFactor;

                MsExtinctionFactor *= MsExtinctionFactor;
                MsScatterFactor *= MsScatterFactor;
            }
            for (ms = kMsCount - 1; ms >= 0; ms--)
            {

                
                float sunVisibilityTerm = participatingMedia.transmittanceToLight[ms];
                float3 sunSkyLuminance = sunVisibilityTerm * sunlightTerm * participatingMediaPhase.phase[ms] * powderEffect;

                // 加上天空和地面的环境光贡献
                if (frameData.sky.cloudConfig.CloudEnableGroundContribution != 0)
                {
                    float skyVisibilityTerm = participatingMediaAmbient.transmittanceToLight[ms];
                    sunSkyLuminance += skyVisibilityTerm * ambientLit;
                }
                // if (ms == 0)
                // {
                //     sunSkyLuminance += groundLit;
                // }

                float3 sactterLitStep = sunSkyLuminance * scatteringCoefficients[ms];

                // Frostbite 引擎 2017 年提出的能量守恒积分公式，用于替代简单的步进累加，可以大幅消除斑点瑕疵
                float3 stepScatter = transmittance * (sactterLitStep - sactterLitStep * stepTransmittance) / max(1e-4f, extinctionCoefficients[ms]);
                scatteredLight += stepScatter;

                // 根据比尔-朗伯定律更新主光线的透射率
                if (ms == 0)
                {
                    transmittance *= stepTransmittance;
                }
            }
            //=====================================================================
        }

        // 提前退出优化 (Early Exit): 如果云已经变得不透明（透射率极低），阻挡了背后的所有光，直接停止步进
        if (transmittance <= 0.001)
        {
            break;
        }
        sampleT += stepT;
    }

    // ==========================================
    // 第四阶段：后处理与空气透视 (Aerial Perspective)
    // ==========================================
    if (rayHitPosWeight > 0.0f)
    {
        rayHitPos /= rayHitPosWeight; // 计算加权平均云深度位置
        //
        //     // 将命中位置转换回相机空间以求取屏幕 Z 值，方便与其他对象正确遮挡
        float3 rayHitInRender = convertToCameraUnit(rayHitPos - float3(0.0, earthRadius, 0.0));
        float4 rayInH = mul(_PreviousCameraVP, float4(rayHitInRender, 1.0)); // 矩阵乘法改为 mul()
        cloudZ = rayInH.z / rayInH.w ;
        // cloudZ =1.0;

        rayHitPos -= worldPos;
        float rayHitHeight = length(rayHitPos);
        {
            float dis = length(rayHitPos *1000.0f - _WorldSpaceCameraPos);
            // 体素 slice 计算
            float dis01 = saturate(dis / _AerialPerspectiveDistance);
            float dis0Z = dis01 * (_AerialPerspectiveVoxelSize.z - 1); // [0 ~ SizeZ-1]
            float slice = floor(dis0Z);

            
            // float weight =1.0;
            // if (slice < 0.5)
            // {
            //     weight = saturate(slice *2.0);
            //     slice =0.5;
            // }
            float nextSlice = min(slice + 1, _AerialPerspectiveVoxelSize.z - 1);
            float lerpFactor = dis0Z - floor(dis0Z);

            uv.x /= _AerialPerspectiveVoxelSize.x;

            // 采样 AerialPerspectiveVoxel
            float2 uv1 = float2(uv.x + slice / _AerialPerspectiveVoxelSize.z, uv.y);
            float2 uv2 = float2(uv.x + nextSlice / _AerialPerspectiveVoxelSize.z, uv.y);

            float4 data1 = SAMPLE_TEXTURE2D(_aerialPerspectiveLut, sampler_LinearClamp, uv1);
            float4 data2 = SAMPLE_TEXTURE2D(_aerialPerspectiveLut, sampler_LinearClamp, uv2);
            float4 airPerspective = lerp(data1, data2, lerpFactor);
            // 将大气雾的颜色与云的颜色进行混合
            airPerspective.a =airPerspective.a -0.6f;
            // scatteredLight = scatteredLight * ( dis01) + airPerspective.rgb * (1.0 - transmittance);
            scatteredLight =  airPerspective.rgb   * (1.0 - airPerspective.a) + scatteredLight * (1.0 - transmittance);
        }
        
        // // 采样预计算的散射 3D Lut (Froxel Scatter) 来给远处的云覆盖一层大气雾（空气透视）
        // {
        // float slice = aerialPerspectiveDepthToSlice(rayHitHeight);
        //     float weight = 1.0;
        //     if (slice < 0.5)
        //     {
        //         weight = saturate(slice * 2.0);
        //         slice = 0.5;
        //     }
        //
        //     // HLSL 中获取 3D Texture 大小的标准写法
        //     uint lutW, lutH, lutD, lutLevels;
        //     inFroxelScatter.GetDimensions(0, lutW, lutH, lutD, lutLevels);
        //     float w = sqrt(slice / float(lutD));
        //
        //     float4 airPerspective = weight * inFroxelScatter.Sample(linearClampEdgeSampler, float3(uv, w));
        //     // 将大气雾的颜色与云的颜色进行混合
        //     scatteredLight = scatteredLight * (1.0 - airPerspective.a) + airPerspective.rgb * (1.0 - transmittance);
        // }

        
    }
    
    // 组合输出：xyz为最终散射出的云光泽，w为透射率 (用于后续与背景如天空盒混合)
    float4 result = float4(scatteredLight, transmittance);

    // 安全检查，避免画面出现黑块或亮斑 (NaN / Inf)
    if (any(isnan(result)) || any(isinf(result)))
    {
        result = float4(0.0, 0.0, 0.0, 1.0);
    }
    return result;
}

#endif
