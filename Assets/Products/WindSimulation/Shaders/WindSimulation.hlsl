#ifndef WIND_SIMULATION_HLSL
#define WIND_SIMULATION_HLSL

// ==========================================
// 1. 全局系统参数 (Uniforms)
// ==========================================
uniform int3 ShiftOffset;
uniform int3 VolumeSize;
uniform int3 VolumeSizeMinusOne;
uniform float3 VolumePosOffset;
uniform float DiffusionForce;
uniform float AdvectionForce;
uniform float MaxWindSpeed;
uniform float VorticityScale;

// ==========================================
// 2. 风源数据结构定义
// ==========================================
struct MotorDirectional {
    float3 position; 
    float radiusSq;
    float3 force;
};

struct MotorOmni {
    float3 position;
    float radiusSq;
    float force;
};

struct MotorVortex {
    float3 position;
    float3 axis; 
    float radiusSq;
    float force;
};

struct MotorMoving {
    float3 prePosition;
    float moveLen;
    float3 moveDir; 
    float radiusSq;
    float force;
};

// ==========================================
// 3. 基础数学与边界工具
// ==========================================
float LengthSq(float3 dir) { return dot(dir, dir); }

float DistanceSq(float3 pos1, float3 pos2) { return LengthSq(pos1 - pos2); }

// 通用边界限制函数
uint3 clampBorder(int3 coord)
{
    return (uint3)clamp(coord, int3(0,0,0), VolumeSizeMinusOne);
}

// ==========================================
// 4. 风源受力计算函数 (Motors)
// ==========================================
void ApplyMotorDirectional(float3 cellPosWS, MotorDirectional motor, inout float3 velocityWS)
{
    float distanceSq = DistanceSq(cellPosWS, motor.position);
    if(distanceSq < motor.radiusSq) velocityWS += motor.force;
}

void ApplyMotorOmni(float3 cellPosWS, MotorOmni motor, inout float3 velocityWS)
{
    float3 dir = cellPosWS - motor.position;
    float distanceSq = LengthSq(dir);
    if(distanceSq < motor.radiusSq)
    {
        velocityWS += dir * motor.force * min(rsqrt(distanceSq), 5.0);
    }
}

void ApplyMotorVortex(float3 cellPosWS, MotorVortex motor, inout float3 velocityWS)
{
    float3 dir = cellPosWS - motor.position;
    float distanceSq = LengthSq(dir);
    if (distanceSq < motor.radiusSq)
    {
        velocityWS += motor.force * cross(motor.axis, dir * rsqrt(distanceSq));
    }
}

void ApplyMotorMoving(float3 cellPosWS, MotorMoving motor, inout float3 velocityWS)
{
    float3 dirPre = cellPosWS - motor.prePosition;
    float moveLen = clamp(dot(dirPre, motor.moveDir), 0.0, motor.moveLen);
    
    float3 curPos = moveLen * motor.moveDir + motor.prePosition;
    float3 dirCur = cellPosWS - curPos;
    float distanceSq = LengthSq(dirCur);
    if(distanceSq < motor.radiusSq)
    {
        float3 blowDir = normalize(rsqrt(distanceSq) * dirCur + motor.moveDir);
        velocityWS += blowDir * motor.force;
    }
}

// ==========================================
// 5. 展平缓冲与定点数原子累加 (Scatter核心)
// ==========================================
#define FXDPT_SIZE (float)(1 << 12)
#define FXDPT_SIZE_R (1.0 / (float)(1 << 12))

int PackFloatToInt(float f) { return (int)(f * FXDPT_SIZE); }
float PackIntToFloat(int i) { return (float)(i * FXDPT_SIZE_R); }

int GetFlatIndex(uint3 id)
{
    uint3 volSize = (uint3)VolumeSize;
    return (id.x + id.y * volSize.x + id.z * volSize.x * volSize.y) * 3;
}

void AtomicAdd(RWStructuredBuffer<int> atomicBuffer, uint3 id, float3 velocity)
{
    if (any(id >= (uint3)VolumeSize)) return; 
    
    int baseIdx = GetFlatIndex(id);
    InterlockedAdd(atomicBuffer[baseIdx], PackFloatToInt(velocity.x));
    InterlockedAdd(atomicBuffer[baseIdx + 1], PackFloatToInt(velocity.y));
    InterlockedAdd(atomicBuffer[baseIdx + 2], PackFloatToInt(velocity.z));
}

// ==========================================
// 6. 三线性采样器宏 (MacCormack核心)
// ==========================================
#define DEFINE_SAMPLE_TRILINEAR(FNAME, TEXTYPE) \
float3 FNAME(TEXTYPE tex, float3 uvw) \
{ \
    int3 base = (int3)floor(uvw); \
    float3 frac = uvw - base; \
    float3 samples[8]; \
    for (int i = 0; i < 8; ++i) \
    { \
        int3 off = int3(i & 1, (i >> 1) & 1, (i >> 2) & 1); \
        samples[i] = tex[clamp(base + off, 0, VolumeSizeMinusOne)].xyz; \
    } \
    float3 result = float3(0.0, 0.0, 0.0); \
    for (int i = 0; i < 8; ++i) \
    { \
        float wx = (i & 1) ? frac.x : (1.0 - frac.x); \
        float wy = (i & 2) ? frac.y : (1.0 - frac.y); \
        float wz = (i & 4) ? frac.z : (1.0 - frac.z); \
        result += samples[i] * (wx * wy * wz); \
    } \
    return result; \
}

DEFINE_SAMPLE_TRILINEAR(SampleTrilinear, Texture3D<float4>)
DEFINE_SAMPLE_TRILINEAR(SampleTrilinear, RWTexture3D<float4>)

#endif