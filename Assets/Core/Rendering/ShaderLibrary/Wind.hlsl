#ifndef CUSTOM_WIND_HLSL
#define CUSTOM_WIND_HLSL

float3 RotateAboutAxis(float3 NormalizedRotationAxis, float RotationAngle, float3 PivotPoint, float3 Position)
{
    // float3 NormalizedRotationAxis =normalize(RotationAxis);
    float cosAngle = cos(RotationAngle); //输入弧度值
    float sinAngle = sin(RotationAngle);
    float3 dir = Position - PivotPoint;
    float3 rotatedDir = dir * cosAngle +
        cross(NormalizedRotationAxis, dir) * sinAngle +
        NormalizedRotationAxis * dot(NormalizedRotationAxis, dir) * (1.0 - cosAngle);
    return PivotPoint + rotatedDir;
}

void Wind(float4 windActor,out float WindStrength,out float3 NormalizedWindVector,out float WindSpeed,out float4 WindActor)
{
    WindStrength = distance(windActor.xyz,0.0);
    NormalizedWindVector =normalize(windActor.xyz);
    WindSpeed = windActor.a *_Time.y;
    WindActor = windActor;
}

// 使用平滑阶梯函数（smoothstep-like）生成风浪效果
float3 WindWave(float3 x)
{
    // 创建一个在 [0, 1] 范围内的三角波
    float3 t = abs(frac(0.5 + x) * 2.0 - 1.0);
    // 应用平滑函数 (3t^2 - 2t^3) 以获得更平滑的波形
    return t * t * (3.0 - 2.0 * t);
}

// 计算简单的草地风效果
// 修正版：简单草地风向位移计算
// 参数说明：
// AbsPostionWS: 当前顶点的绝对世界坐标
// RootPositionWS: 该草地网格底部中心的世界坐标 (或利用 AbsPostionWS.y - Local.y 估算)
// WindWeight: 顶点色权重 (底部为0，草尖为1，保证根部不脱离地面)
float3 SimpleGrassWind_UE(float3 AbsPostionWS, float3 RootPositionWS, float WindIntensity, float WindWeight, float WindSpeed)
{
    // 1. 定义水平风向 (去掉 Y 轴影响，草主要是被水平风吹弯的)
    float2 windDir2D = normalize(float2(1.0, 1.0)); // 例如风向为 XZ 平面的对角线
    float3 windDirection = float3(windDir2D.x, 0.0, windDir2D.y);
    
    // 2. 计算绝对水平的旋转轴
    float3 worldUpAxis = float3(0.0, 1.0, 0.0);
    float3 rotationAxis = cross(windDirection, worldUpAxis); // 必定在 XZ 平面上

    // 3. 计算基于时间和空间坐标的波形 (Noise)
    // 采样必须用 Root 坐标，防止同一根草的上下部分波形脱节而被拉扯变形！
    float timeBasedSpeed = -WindSpeed * _Time.y;
    float3 pos1 = RootPositionWS * 0.001 + timeBasedSpeed * windDirection;
    float3 pos2 = RootPositionWS * 0.005 + timeBasedSpeed;

    float3 windOffset1 = WindWave(pos1); 
    float3 windOffset2 = WindWave(pos2); 

    // 4. 计算最终的旋转角度
    float rotationAngle = length(windOffset2) + dot(windOffset1, windDirection);
    // 限制最大弯曲角度，防止 360 度打圈
    rotationAngle = clamp(rotationAngle * WindIntensity, -1.5, 1.5); 

    // 5. 核心修复：绕着草的【根部】旋转
    float3 rotatedPos = RotateAboutAxis(rotationAxis, rotationAngle, RootPositionWS, AbsPostionWS);

    // 6. 核心修复：先求出 Delta 偏移量，再乘上顶点的高度权重，最后加回原坐标
    float3 posDelta = rotatedPos - AbsPostionWS;
    
    return AbsPostionWS + (posDelta * WindWeight);
}

// 计算简单的草地风效果
float3 SimpleGrassWind(float3 AbsPostionWS,float3 AdditionalWPO, float WindIntensity,float WindWeight,float WindSpeed)
{
    // 假设：Y轴为向上，风向和风速应作为外部参数传入
    
    // **修正 1: 定义常量和轴向**
    float4 windDirectionAndStrength = float4(0.0, 1.0, 0.0, 1.0); // 略微向X和Z方向倾斜
    float3 windDirection = normalize(windDirectionAndStrength.xyz);
    float3 worldUpAxis = float3(0.0, 1.0, 0.0); // 世界Y轴为向上
    
    // 旋转轴：垂直于风向和地面的向量 (用于左右摆动)
    float3 rotationAxis = cross(windDirection, worldUpAxis);

    // **修正 2: 统一时间变量 (避免线性增长)**
    float time = _Time.y * WindSpeed * windDirectionAndStrength.w;

    // 使用两个不同频率的波
    float3 pos1 = AbsPostionWS * rcp(1024.0) + time * windDirection * 0.5; // 低频
    float3 pos2 = AbsPostionWS * rcp(200.0) + time; // 高频

    float3 windOffset1 = WindWave(pos1);
    float3 windOffset2 = WindWave(pos2);

    // 计算旋转角度 (合并两个波)
    float rotationAngle = (distance(windOffset2, 0.0) + dot(windOffset1, windDirection)); // 0.1 限制角度

    // **修正 3: 确定枢轴点 (草的根部)**
    // 将顶点位置投影到XZ平面，即草的根部位置
    float3 pivotPoint = AbsPostionWS * float3(1.0, 0.0, 1.0); 

    // **修正 4: 旋转原始位置**
    float3 rotatedPos = RotateAboutAxis(rotationAxis, rotationAngle, pivotPoint, AbsPostionWS);

    // **修正 5: 计算最终的WPO偏移量**
    float3 finalWPO = (rotatedPos - AbsPostionWS); // 这是旋转产生的偏移

    // WPO 通常在草的顶部最大，底部为零。使用 AdditionalWPO (例如 UV.y) 作为高度权重
    // float heightWeight = AdditionalWPO.y; // 假设 AdditionalWPO 传入的是高度或权重
    
    float3 finalOffset = finalWPO  * WindWeight * WindIntensity;
    
    // **返回偏移量** (在 Shader Graph/WPO 材质设置中，这个值会加到原始位置上)
    return finalOffset; 
}

//============================================================
//ASE && Shader Graph 中使用的接口
//============================================================
void RotateAboutAxis_float(float3 NormalizedRotationAxis, float RotationAngle, float3 PivotPoint, float3 Position, out float3 Result)
{
    Result = RotateAboutAxis(NormalizedRotationAxis, RotationAngle, PivotPoint, Position);
}

void Wind_float(float4 windActor,out float WindStrength,out float3 NormalizedWindVector,out float WindSpeed,out float4 WindActor)
{
    Wind(windActor, WindStrength, NormalizedWindVector, WindSpeed, WindActor);
}

void SimpleGrassWind_float(float3 AbsPostionWS,float3 AdditionalWPO, float WindIntensity,float WindWeight,float WindSpeed,out float3 Result)
{
    Result =  SimpleGrassWind(AbsPostionWS, AdditionalWPO, WindIntensity, WindWeight, WindSpeed);
    // Result = AbsPostionWS;
}

#endif // CUSTOM_WIND_HLSL

