#ifndef UNIVERSAL_GTAO_INCLUDED
#define UNIVERSAL_GTAO_INCLUDED

//-------------------------------------------------------------------------------------
// HLSL 实现说明 25.0628
// 参考 https://github.com/bladesero/GTAO_URP/
// 本文件包含 Ground-Truth Ambient Occlusion (GTAO) 的核心实现。
//
// 主要函数:
// 1. GTAOFrag(): 实现 GTAO 算法。它基于地平线遮蔽的原理，对每个像素，在多个
//    方向上进行切片（slice），并在每个切片上向外步进采样以检测遮挡物，
//    最终积分计算出环境光遮蔽值。
//
// 2. ReconstructViewPos() / ReconstructNormal(): 从深度图重建视图空间位置
//    和表面法线。这是当没有法线贴图可用时的关键步骤。
//
// 3. Blur() / BlurSmall(): 实现一个具有几何感知能力的双边模糊。它使用法线
//    信息来保留边缘，避免将 AO 效果错误地模糊到不相关的表面上。
//-------------------------------------------------------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

CBUFFER_START(UnityPerMaterial)
    // Params
    half4 _SSAO_UVToView;
    half4 _SSAOParams;
    half4 _CameraViewTopLeftCorner[2];
    half4x4 _CameraViewProjections[2];
    // This is different from UNITY_MATRIX_VP (platform-agnostic projection matrix is used). Handle both non-XR and XR modes.

    float4 _SourceSize;
    float4 _ProjectionParams2;
    float4 _CameraViewXExtent[2];
    float4 _CameraViewYExtent[2];
    float4 _CameraViewZExtent[2];
CBUFFER_END

// Textures & Samplers
TEXTURE2D_X(_BaseMap);
TEXTURE2D_X(_ScreenSpaceOcclusionTexture);

SAMPLER(sampler_BaseMap);
SAMPLER(sampler_ScreenSpaceOcclusionTexture);

#define SLICE          8

// SSAO Settings
#define INTENSITY       _SSAOParams.x
#define RADIUS          _SSAOParams.y
#define DOWNSAMPLE      _SSAOParams.z

// GLES2: In many cases, dynamic looping is not supported.
#if defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
    #define SAMPLE_COUNT 3
#else
    #define SAMPLE_COUNT int(_SSAOParams.w)
#endif

// Function defines
#define SCREEN_PARAMS        GetScaledScreenParams()
#define SAMPLE_BASEMAP(uv)   SAMPLE_TEXTURE2D_X(_BaseMap, sampler_BaseMap, UnityStereoTransformScreenSpaceTex(uv));

// The constant below controls the geometry-awareness of the bilateral
// filter. The higher value, the more sensitive it is.
static const half kGeometryCoeff = half(0.8);

#if defined(USING_STEREO_MATRICES)
    #define unity_eyeIndex unity_StereoEyeIndex
#else
    #define unity_eyeIndex 0
#endif

half4 PackAONormal(half ao, half3 n)
{
    return half4(ao, n * half(0.5) + half(0.5));
}

half3 GetPackedNormal(half4 p)
{
    return p.gba * half(2.0) - half(1.0);
}

half GetPackedAO(half4 p)
{
    return p.r;
}

half EncodeAO(half x)
{
    #if UNITY_COLORSPACE_GAMMA
        return half(1.0 - max(LinearToSRGB(1.0 - saturate(x)), 0.0));
    #else
        return x;
    #endif
}

half CompareNormal(half3 d1, half3 d2)
{
    return smoothstep(kGeometryCoeff, half(1.0), dot(d1, d2));
}


float SampleAndGetLinearEyeDepth(float2 uv)
{
    float rawDepth = SampleSceneDepth(uv.xy);
    #if defined(_ORTHOGRAPHIC)
        return LinearDepthToEyeDepth(rawDepth);
    #else
        return LinearEyeDepth(rawDepth, _ZBufferParams);
    #endif
}

//从 UV 和深度值重建视图空间位置 (View-Space Position) 
// This returns a vector in world unit (not a position), from camera to the given point described by uv screen coordinate and depth (in absolute world unit).
half3 ReconstructViewPos(float2 uv, float depth)
{
    // Screen is y-inverted.
    uv.y = 1.0 - uv.y;

    // view pos in world space
    #if defined(_ORTHOGRAPHIC)
        float zScale = depth * _ProjectionParams.w; // divide by far plane
        float3 viewPos = _CameraViewTopLeftCorner[unity_eyeIndex].xyz
                            + _CameraViewXExtent[unity_eyeIndex].xyz * uv.x
                            + _CameraViewYExtent[unity_eyeIndex].xyz * uv.y
                            + _CameraViewZExtent[unity_eyeIndex].xyz * zScale;
    #else
        float zScale = depth * _ProjectionParams2.x; // divide by near plane
        float3 viewPos = _CameraViewTopLeftCorner[unity_eyeIndex].xyz
                            + _CameraViewXExtent[unity_eyeIndex].xyz * uv.x
                            + _CameraViewYExtent[unity_eyeIndex].xyz * uv.y;
        viewPos *= zScale;
    #endif

    return half3(viewPos);
}
//=================================从深度图重建法线==========================================================
// Try reconstructing normal accurately from depth buffer.
// Low:    DDX/DDY on the current pixel
// Medium: 3 taps on each direction | x | * | y |
// High:   5 taps on each direction: | z | x | * | y | w |
// https://atyuwen.github.io/posts/normal-reconstruction/
// https://wickedengine.net/2019/09/22/improved-normal-reconstruction-from-depth/
half3 ReconstructNormal(float2 uv, float depth, float3 vpos)
{
    #if defined(_RECONSTRUCT_NORMAL_LOW)
        return half3(normalize(cross(ddy(vpos), ddx(vpos))));
    #else
        float2 delta = float2(_SourceSize.zw * 2.0);

        // Sample the neighbour fragments
        float2 lUV = float2(-delta.x, 0.0);
        float2 rUV = float2( delta.x, 0.0);
        float2 uUV = float2(0.0,  delta.y);
        float2 dUV = float2(0.0, -delta.y);

        float3 l1 = float3(uv + lUV, 0.0); l1.z = SampleAndGetLinearEyeDepth(l1.xy); // Left1
        float3 r1 = float3(uv + rUV, 0.0); r1.z = SampleAndGetLinearEyeDepth(r1.xy); // Right1
        float3 u1 = float3(uv + uUV, 0.0); u1.z = SampleAndGetLinearEyeDepth(u1.xy); // Up1
        float3 d1 = float3(uv + dUV, 0.0); d1.z = SampleAndGetLinearEyeDepth(d1.xy); // Down1

        // Determine the closest horizontal and vertical pixels...
        // horizontal: left = 0.0 right = 1.0
        // vertical  : down = 0.0    up = 1.0
        #if defined(_RECONSTRUCT_NORMAL_MEDIUM)
             uint closest_horizontal = l1.z > r1.z ? 0 : 1;
             uint closest_vertical   = d1.z > u1.z ? 0 : 1;
        #else
            float3 l2 = float3(uv + lUV * 2.0, 0.0); l2.z = SampleAndGetLinearEyeDepth(l2.xy); // Left2
            float3 r2 = float3(uv + rUV * 2.0, 0.0); r2.z = SampleAndGetLinearEyeDepth(r2.xy); // Right2
            float3 u2 = float3(uv + uUV * 2.0, 0.0); u2.z = SampleAndGetLinearEyeDepth(u2.xy); // Up2
            float3 d2 = float3(uv + dUV * 2.0, 0.0); d2.z = SampleAndGetLinearEyeDepth(d2.xy); // Down2

            const uint closest_horizontal = abs( (2.0 * l1.z - l2.z) - depth) < abs( (2.0 * r1.z - r2.z) - depth) ? 0 : 1;
            const uint closest_vertical   = abs( (2.0 * d1.z - d2.z) - depth) < abs( (2.0 * u1.z - u2.z) - depth) ? 0 : 1;
        #endif


        // Calculate the triangle, in a counter-clockwize order, to
        // use based on the closest horizontal and vertical depths.
        // h == 0.0 && v == 0.0: p1 = left,  p2 = down
        // h == 1.0 && v == 0.0: p1 = down,  p2 = right
        // h == 1.0 && v == 1.0: p1 = right, p2 = up
        // h == 0.0 && v == 1.0: p1 = up,    p2 = left
        // Calculate the view space positions for the three points...
        float3 P1;
        float3 P2;
        if (closest_vertical == 0)
        {
            P1 = closest_horizontal == 0 ? l1 : d1;
            P2 = closest_horizontal == 0 ? d1 : r1;
        }
        else
        {
            P1 = closest_horizontal == 0 ? u1 : r1;
            P2 = closest_horizontal == 0 ? l1 : u1;
        }

        // Use the cross product to calculate the normal...
        return half3(normalize(cross(ReconstructViewPos(P2.xy, P2.z) - vpos, ReconstructViewPos(P1.xy, P1.z) - vpos)));
    #endif
}

// For when we don't need to output the depth or view position
// Used in the blur passes
half3 SampleNormal(float2 uv)
{
    #if defined(_SOURCE_DEPTH_NORMALS)
        return half3(SampleSceneNormals(uv));
    #else
        float depth = SampleAndGetLinearEyeDepth(uv);
        half3 vpos = ReconstructViewPos(uv, depth);
        return ReconstructNormal(uv, depth, vpos);
    #endif
}

inline half3 SampleNormalView(half2 uv)
{
    half3 norm = normalize(SampleNormal(uv));
    half3 view_Normal = normalize(mul((half3x3) unity_WorldToCamera, norm));
    return half3(view_Normal.xy, view_Normal.z);
}

// 统一接口，根据宏定义获取法线
// 如果有法线纹理，直接采样；否则从深度重建。
void SampleDepthNormalView(float2 uv, out float depth, out half3 normal, out half3 vpos)
{
    depth  = SampleAndGetLinearEyeDepth(uv);
    vpos   = ReconstructViewPos(uv, depth);

    #if defined(_SOURCE_DEPTH_NORMALS)
        normal = half3(SampleSceneNormals(uv));
    #else
        normal = ReconstructNormal(uv, depth, vpos);
    #endif
}

inline half3 GetPosition(half2 uv)
{
    half depth = SampleAndGetLinearEyeDepth(uv);
    half3 pos= ReconstructViewPos(uv, depth);
    //pos = mul((half3x3) unity_CameraToWorld, pos);
    return pos;

}

//fov 坐标还原方法 
inline half3 GetPositionVS(half2 uv)
{
    half depth = SampleAndGetLinearEyeDepth(uv);
    return half3((uv * _SSAO_UVToView.xy + _SSAO_UVToView.zw) * depth, depth);
}

half IntegrateArc_CosWeight(half2 h, half n)
{
    half2 Arc = -cos(2 * h - n) + cos(n) + 2 * h * sin(n);
    return 0.25 * (Arc.x + Arc.y);
}

inline half GTAO_Noise(half2 position)
{
    return frac(52.9829189 * frac(dot(position, half2(0.06711056, 0.00583715))));
}

inline half GTAO_Offsets(half2 uv)
{
    int2 position = (int2) (uv * SCREEN_PARAMS.xy);
    return 0.25 * (half) ((position.y - position.x) & 3);
}

inline half ComputeDistanceFade(const half distance)
{
    return saturate(max(0, distance - 0.5) * 0);
}




//===========================================================
// GTAO Ground Truth Ambient Occlusion
// https://blog.selfshadow.com/publications/s2016-shading-course/karis/s2016_pbs_epic_hair.pptx
// https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
//===========================================================
half4 GTAOFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 uv = input.uv; //screenUV
    
    // half BentAngle, wallDarkeningCorrection;
    // half2 slideDir_TexelSize;
    // half3 BentNormal;

    // 1. 获取当前像素的法线和视图位置
    half3 posCenterView = GetPositionVS(uv);
    half3 viewDir = normalize(0 - posCenterView);
    half3 normalView = SampleNormalView(uv);
    half ao = 0.0;

    // 2. 初始化参数
    
    half2 radius_thickness = lerp(half2(RADIUS, 1), half2(0, 0), ComputeDistanceFade(posCenterView.b).xx);
    half radius = radius_thickness.x;
    half thickness = radius_thickness.y;
    
    half noiseDirection = GTAO_Noise(uv * SCREEN_PARAMS.xy * DOWNSAMPLE);
    half noiseOffset = frac(GTAO_Offsets(uv));
    half stepRadius = (max(min((radius*31) / posCenterView.b, 512), (half) SLICE)) / ((half) SLICE + 1);
    
    // This was added to avoid a NVIDIA driver issue.
    const half rcpSampleCount = half(rcp(SAMPLE_COUNT));
    
    //if (SampleSceneDepth(uv.xy).r <= 1e-7)
    //    return PackAONormal(1, norm_o);
    
    // 3. 主循环：在多个方向上进行采样
    [loop]
    for (int i = 0; i < SAMPLE_COUNT; i++)
    {
        // a. 确定当前采样方向 ("slice")
        half angle = (i + 30 * noiseDirection) * (PI * rcpSampleCount); 
        half3 sliceDir = half3(half2(cos(angle), sin(angle)), 0); //directionV
        
        half3 normalSlicePlane = normalize(cross(sliceDir, viewDir)); //axisV Sn
        half3 tangentSlicePlane = cross(viewDir, normalSlicePlane); //X 不是 orthoDirectionV

        // 用施密特正交化，得到view空间的法向量n在slice plane的投射法线np
        //PPT - Slide 47
        half3 normalProjected = normalView - normalSlicePlane * dot(normalView, normalSlicePlane); //projNormalV
        half projectLength = length(normalProjected);

        half sgnN = -sign(dot(normalProjected, tangentSlicePlane));
        half cosN = clamp(dot(normalize(normalProjected), viewDir), -1, 1); //float clamp(float value, float min, float max);
        // 法线和View向量的夹角gamma
        //Range[0,PI/2]
        half gamma = sgnN * acos(cosN);
        
        // 初始化地平线角度为最大值 h1 horizonViewCos.x  h2  horizonViewCos.y
        half2 horizonCos = -1;

        // b. 内循环：沿着切片方向步进采样,找到最高的遮挡点 左右 h1 h2
        //SLICE 步进 8 次
        [loop]
        for (int j = 0; j < SLICE; j++)
        {
            // i. 计算采样点的 UV
            //scaling - (rcp(SCREEN_PARAMS.xy * DOWNSAMPLE)))
            half2 uvOffset = (sliceDir.xy * (rcp(SCREEN_PARAMS.xy * DOWNSAMPLE))) * max(stepRadius * (j + noiseOffset), j + 1);
            half4 uvSlice = uv.xyxy + float4(uvOffset.xy, -uvOffset);

            // ii. 获取采样点的视图位置，并计算与当前点的向量
            half3 dir_s = GetPositionVS(uvSlice.xy) - posCenterView;   //Ps -Pc  HorizonViewS
            half3 dir_t = GetPositionVS(uvSlice.zw) - posCenterView;  //Pt -Pc   HorizonViewT
            
            // iii. 计算地平线角度 (Horizon Angle)
            //normalized
            half2 ditDot = half2(dot(dir_s, dir_s), dot(dir_t, dir_t));
            half2 dirLength = rsqrt(ditDot);

            //衰减
            half2 falloff = saturate(ditDot * (2 * rcp(radius * radius)));
            
            half2 HorizonCos = half2(dot(dir_s, viewDir), dot(dir_t, viewDir)) * dirLength;

            // iv. 更新地平线，找到最高的遮挡点
            horizonCos = (HorizonCos > horizonCos)
                                    ? lerp(HorizonCos, horizonCos, falloff)
                                    : lerp(HorizonCos, horizonCos, thickness);
        }
        
        // c. 计算并累加当前切片的遮蔽贡献
        //h1 = horizonCos.x h2 = horizonCos.y
        //这里用于把水平角 h 截取在法线半球内 PPT - Slide 58
        half2 horizonAngle = acos(clamp(horizonCos, -1, 1));
        horizonAngle.x = gamma + max(-horizonAngle.x - gamma, -PI * 0.5);
        horizonAngle.y = gamma + min(horizonAngle.y - gamma, PI * 0.5);

        //将 每个 Slice 的遮蔽值 * 投影法线的模长  PPT - Slide 62
        //Range[-1,1];
        ao += projectLength * IntegrateArc_CosWeight(horizonAngle, gamma) * rcpSampleCount;
    }

    // 4. 应用最终强度并打包输出
    ao = 1 - ao;
    ao = PositivePow(ao * INTENSITY, 2.5); // 应用强度

    // Apply contrast
    return PackAONormal(ao, normalView);
}


 float3 GTAOMultiBounceFunc( float visibility, float3 albedo )
 {
    float3 a =  2.0404 * albedo - 0.3324;
    float3 b =  -4.7951 * albedo + 0.6417;
    float3 c =  2.7552 * albedo + 0.6903;

    float x = visibility;
    return max( x, ( ( x * a + b ) * x + c ) * x );
}




//================================================================================
// 模糊函数
//================================================================================
// 具有几何感知能力的分离式双边模糊
// Geometry-aware separable bilateral filter
half4 Blur(float2 uv, float2 delta) : SV_Target
{
    half4 p0 =  (half4) SAMPLE_BASEMAP(uv                 );
    half4 p1a = (half4) SAMPLE_BASEMAP(uv - delta * 1.3846153846);
    half4 p1b = (half4) SAMPLE_BASEMAP(uv + delta * 1.3846153846);
    half4 p2a = (half4) SAMPLE_BASEMAP(uv - delta * 3.2307692308);
    half4 p2b = (half4) SAMPLE_BASEMAP(uv + delta * 3.2307692308);

    #if defined(BLUR_SAMPLE_CENTER_NORMAL)
        #if defined(_SOURCE_DEPTH_NORMALS)
            half3 n0 = half3(SampleSceneNormals(uv));
        #else
            half3 n0 = SampleNormal(uv);
        #endif
    #else
        half3 n0 = GetPackedNormal(p0);
    #endif

    half w0  =                                           half(0.2270270270);
    half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * half(0.3162162162);
    half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * half(0.3162162162);
    half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * half(0.0702702703);
    half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * half(0.0702702703);

    half s = half(0.0);
    s += GetPackedAO(p0)  * w0;
    s += GetPackedAO(p1a) * w1a;
    s += GetPackedAO(p1b) * w1b;
    s += GetPackedAO(p2a) * w2a;
    s += GetPackedAO(p2b) * w2b;
    s *= rcp(w0 + w1a + w1b + w2a + w2b);

    return PackAONormal(s, n0);
}

// Geometry-aware bilateral filter (single pass/small kernel)
half BlurSmall(float2 uv, float2 delta)
{
    half4 p0 = (half4) SAMPLE_BASEMAP(uv                            );
    half4 p1 = (half4) SAMPLE_BASEMAP(uv + float2(-delta.x, -delta.y));
    half4 p2 = (half4) SAMPLE_BASEMAP(uv + float2( delta.x, -delta.y));
    half4 p3 = (half4) SAMPLE_BASEMAP(uv + float2(-delta.x,  delta.y));
    half4 p4 = (half4) SAMPLE_BASEMAP(uv + float2( delta.x,  delta.y));

    half3 n0 = GetPackedNormal(p0);

    half w0 = half(1.0);
    half w1 = CompareNormal(n0, GetPackedNormal(p1));
    half w2 = CompareNormal(n0, GetPackedNormal(p2));
    half w3 = CompareNormal(n0, GetPackedNormal(p3));
    half w4 = CompareNormal(n0, GetPackedNormal(p4));

    half s = half(0.0);
    s += GetPackedAO(p0) * w0;
    s += GetPackedAO(p1) * w1;
    s += GetPackedAO(p2) * w2;
    s += GetPackedAO(p3) * w3;
    s += GetPackedAO(p4) * w4;

    return s *= rcp(w0 + w1 + w2 + w3 + w4);
}

half4 HorizontalBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.uv;
    const float2 delta = float2(_SourceSize.z, 0.0);
    return Blur(uv, delta);
}

half4 VerticalBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.uv;
    const float2 delta = float2(0.0, _SourceSize.w * rcp(DOWNSAMPLE));
    return Blur(uv, delta);
}

half4 FinalBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.uv;
    const float2 delta = _SourceSize.zw;
    return half(1.0) - BlurSmall(uv, delta);
    //return BlurSmall(uv, delta );
}

#endif //UNIVERSAL_SSAO_INCLUDED
