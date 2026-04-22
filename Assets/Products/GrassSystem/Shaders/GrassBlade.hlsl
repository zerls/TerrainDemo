#ifndef GRASS_BLADE_HLSL
#define GRASS_BLADE_HLSL

#include "CubicBezier.hlsl"
#include "Assets/Core/Rendering/ShaderLibrary/WindSystem.hlsl"

TEXTURE2D(_GrassAlbedo);        SAMPLER(sampler_GrassAlbedo);
TEXTURE2D(_GrassGloss);         SAMPLER(sampler_GrassGloss);

struct GrassBlade
{
    float3 position; // 世界空间根部位置 (Compute 已投射到地形)
    float rotAngle; // 绕 Y 轴旋转角 (朝向风/簇)
    float hash; // 随机种子 (驱动顶端风相位)
    float height; // 草高度 (影响贝塞尔末端 p3)
    float width; // 宽度 (与顶点 color.g 组合决定左右扩展)
    float tilt; // 顶端朝向倾斜角 (影响 p3 XY 分解)
    float bend; // 主体弯曲系数 (控制 p1 / p2 偏移)
    float3 surfaceNorm; // 地表法线 (备用: 可用于与地形对齐或 AO)
    float windForce; // 局部风强度 (缩放风动画)
    float sideBend; // 额外侧向弯曲 (面向相机优化 Billboard 观感)
};


StructuredBuffer<GrassBlade> _GrassBlades;
StructuredBuffer<int> Triangles;
StructuredBuffer<float4> Colors;
StructuredBuffer<float2> Uvs;

float _TaperAmount;
float _CurvedNormalAmount;
float _p1Offset;
float _p2Offset;
float4 _TopColor;
float4 _BottomColor;
float _WaveAmplitude;
float _WaveSpeed;
float _SinOffsetRange;
float _PushTipForward;
float _ShadowIntensity;
float _Cutoff;
float _Gloss;

struct Attributes
{
    uint vertexID : SV_VertexID;
    uint instanceID : SV_InstanceID;
};

float3 GetP0() { return float3(0, 0, 0); } // 根部锚点 (局部空间原点)

float3 GetP3(float height, float tilt)
{
    // 根据高度与倾斜角构建末端点: 在局部 X-Y 平面内分解, 保持总长度为 height
    float p3y = tilt * height; // Y 向提升 (倾斜)
    float p3x = sqrt(height * height - p3y * p3y); // 勾股保证长度
    return float3(-p3x, p3y, 0); // 使用 -X 方向作为默认向前方向 (可与 rotAngle 旋转)
}

float3x3 RotAxis3x3(float angle, float3 axis)
{
    // 任意轴旋转矩阵 (罗德里格公式)，用于朝向/侧弯旋转
    axis = normalize(axis);
    float s, c;
    sincos(angle, s, c);
    float t = 1.0 - c;
    float x = axis.x, y = axis.y, z = axis.z;
    float xy = x * y, xz = x * z, yz = y * z;
    float xs = x * s, ys = y * s, zs = z * s;
    return float3x3(
        t * x * x + c, t * xy - zs, t * xz + ys,
        t * xy + zs, t * y * y + c, t * yz - xs,
        t * xz - ys, t * yz + xs, t * z * z + c);
}

void GetP1P2P3(
    float3 p0,  // 贝塞尔起点
    inout float3 p3,  // 末端点 (会被风影响侧向偏移)
    float bend,  // 主弯曲强度
    float hash,  // 随机种子 (决定风相位)
    float windForce, // 风强度 (缩放风幅度)
    float3 windFinalLS, // 传入局部空间的 3D 物理风向量
    out float3 p1,  // 输出控制点 1
    out float3 p2)  // 输出控制点 2
{
    // 1. 提取基础生长方向与长度
    float bladeLength = length(p3 - p0);
    float3 upDir = normalize(p3 - p0);
    float3 bezCtrlOffsetDir = normalize(cross(upDir, float3(0, 0, 1)));

    // ==========================================================
    // 2. 物理 3D 风场球面弯曲
    // ==========================================================
    #if _WIND_SIMULATION
    float windStrength = length(windFinalLS);
    if(windStrength > 0.001)
    {
        // 限制最大弯曲度接近 90 度 (-1.5 到 1.5 弧度)，防止被狂风拉扯变形
        float rad = clamp(windStrength * PI * 0.9, -1.5, 1.5) / 2.0; 
        float x, y;
        sincos(rad, x, y);

        // 提取风力在正交面上的推力方向
        float3 projWind = windFinalLS - dot(windFinalLS, upDir) * upDir;
        float3 windDir = length(projWind) > 0.0001 ? normalize(projWind) : float3(1,0,0);

        // 球面旋转算出受风后的 p3 朝向，并严格保持草的长度
        float3 bentDir = x * windDir + y * upDir;
        p3 = p0 + bentDir * bladeLength;
    }
    #endif

    // ==========================================================
    // 3. 高频扰动与静态弯曲
    // ==========================================================
    // 顶端前倾与高频颤振 (Noise) 依然叠加在最终的 p3 上，模拟草叶边缘的抖动
    float tipFlutter = sin((_Time.y + hash * 2 * PI) * _WaveSpeed + 1.0 * 2 * PI * _SinOffsetRange) * windForce;
    p3 += bezCtrlOffsetDir * (tipFlutter * _WaveAmplitude + _PushTipForward * (1 - bend));

    // 4. 生成 p1, p2 控制点，自动适应弯曲后的 p3
    p1 = lerp(p0, p3, 0.33);
    p2 = lerp(p0, p3, 0.66);

    // 中段的基础静态弯曲和轻微颤振
    float p2Flutter = sin((_Time.y + hash * 2 * PI) * _WaveSpeed + 0.66 * 2 * PI * _SinOffsetRange) * windForce;
    p1 += bezCtrlOffsetDir * bend * _p1Offset;
    p2 += bezCtrlOffsetDir * (bend * _p2Offset + p2Flutter * 0.66 * _WaveAmplitude);
}

void CalculateGrassVertexData(Attributes IN, out float3 positionWS, out float3 originalNorm, out float3 curvedNormOut, out float2 uvOut, out float tOut)
{
    // 1. 读取实例数据
    GrassBlade blade = _GrassBlades[IN.instanceID];
    float bend = blade.bend;
    float height = blade.height;
    float tilt = blade.tilt;
    float hash = blade.hash;
    float windForce = blade.windForce;
    
    // ==========================================================
    // 使用草根的绝对世界坐标 (blade.position) 采样，同一个 Instance 所有的顶点
    // 利用 GPU 纹理缓存,都在此处采样出同一个风力值
    // ==========================================================
    float3 windFinalWS = float3(0, 0, 0);
    #if _WIND_SIMULATION
        float3 uvw = (blade.position - VolumePosOffset.xyz) / VolumeSize.xyz;
        float3 distToBox = max(0, abs(uvw - 0.5) - 0.5);
        half fadeDis = saturate(1.0 - length(distToBox) * 10.0);
        
        if (fadeDis > 0.0) 
        {
            float3 windData = SAMPLE_TEXTURE3D_LOD(WindVelocityData, sampler_WindVelocityData, uvw, 0).xyz;
            windFinalWS = windData * fadeDis * OverallPower;
        }
    #endif

    // ==========================================================
    // 贝塞尔曲线的控制点是在局部空间生成的。由于最终的顶点会绕 Y 轴旋转 rotAngle，
    // 我们必须将世界空间的风向量，逆方向旋转转回局部空间。
    // ==========================================================
    float3x3 invRotMat = RotAxis3x3(blade.rotAngle, float3(0, 1, 0));
    float3 windFinalLS = mul(invRotMat, windFinalWS);

    // ==========================================================
    // 将 windFinalLS 丢给函数，让它去控制物理弯曲
    // ==========================================================
    float3 p0 = GetP0();
    float3 p3 = GetP3(height, tilt);
    float3 p1, p2;
    GetP1P2P3(p0, p3, bend, hash, windForce, windFinalLS, p1, p2);

    // 3. 根据索引获取基础顶点属性
    int positionIndex = Triangles[IN.vertexID];
    float4 vertColor = Colors[positionIndex];
    uvOut = Uvs[positionIndex];
    tOut = vertColor.r;
    float side = vertColor.g * 2 - 1;
    float isPlane = vertColor.b; 

    // 4. 计算弯曲后的曲线位置
    float3 centerPos = CubicBezier(p0, p1, p2, p3, tOut);
    float width = blade.width * (1 - _TaperAmount * tOut * (1.0 - isPlane));
    height = lerp(height, 0.01 * height, isPlane);
    width = lerp(width, 7.0 * width, isPlane);

    // 5. 曲线切线 & 法线 (因为控制点 p3 物理位移了，这里算出的法线天生就是正确的迎风法线)
    float3 baseBitangent = lerp(float3(0, 0, 1), float3(1, 0, 0), isPlane);
    float3 position = centerPos + baseBitangent * (side * width);
    float3 tangent = CubicBezierTangent(p0, p1, p2, p3, tOut);
    float3 normal = normalize(cross(tangent, baseBitangent));

    // 6. 增强法线 
    float3 curvedNorm = normal;
    curvedNorm += baseBitangent * (side * _CurvedNormalAmount);
    curvedNorm = normalize(curvedNorm);

    // 7. 旋转与矩阵恢复
    float angle = blade.rotAngle;
    float sideBend = blade.sideBend;
    float3x3 rotMat = RotAxis3x3(-angle, float3(0, 1, 0));
    float3x3 sideRot = RotAxis3x3(sideBend, normalize(tangent));

    position -= centerPos; 
    normal = mul(sideRot, normal);
    curvedNorm = mul(sideRot, curvedNorm);
    position = mul(sideRot, position);
    position += centerPos; 

    normal = mul(rotMat, normal); 
    curvedNorm = mul(rotMat, curvedNorm);
    position = mul(rotMat, position);

    // 8. 放置回世界坐标
    positionWS = position + blade.position;
    originalNorm = normal;
    curvedNormOut = curvedNorm;
}

float InterleavedGradientNoise(float2 screenPos)
{
    // 魔法数字，用于生成分布极其均匀的伪随机梯度
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(screenPos, magic.xy)));
}

// 4x4 Bayer 矩阵算法 (优化版，避免使用数组)
float GetBayer4x4(float2 screenPos)
{
    // 获取当前像素在 4x4 矩阵中的相对位置
    uint x = (uint)screenPos.x % 4;
    uint y = (uint)screenPos.y % 4;
                
    // 展开的 4x4 拜耳矩阵权重
    const float4x4 bayerMatrix = float4x4(
        0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
        12.0/16.0, 4.0/16.0, 14.0/16.0,  6.0/16.0,
        3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
        15.0/16.0, 7.0/16.0, 13.0/16.0,  5.0/16.0
    );
                
    return bayerMatrix[x][y];
}


#endif
