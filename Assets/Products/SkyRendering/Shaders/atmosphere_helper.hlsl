#ifndef ATMOSPHERE_HELPER_HLSL
#define ATMOSPHERE_HELPER_HLSL

#include "sky_shared_struct.hlsl"

#ifndef PI
#define PI 3.14159265359
#endif

#define kAtmosphereAirPerspectiveKmPerSlice  4.0f // Total 32 * 4 = 128 km.
#define kAtmosphereUnitScale           1000.0f   // Km to meter.

float3 UVToViewDir(float2 uv)
{
    float theta = (1.0 - uv.y) * PI;
    float phi = (uv.x * 2 - 1) * PI;
    
    float x = sin(theta) * cos(phi);
    float z = sin(theta) * sin(phi);
    float y = cos(theta);

    return float3(x, y, z);
}

float2 ViewDirToUV(float3 v)
{
    float2 uv = float2(atan2(v.z, v.x), asin(v.y));
    uv /= float2(2.0 * PI, PI);
    uv += float2(0.5, 0.5);

    return uv; 
}

float RayIntersectSphere(float3 center, float radius, float3 rayStart, float3 rayDir)
{
    // L = vector from ray start to sphere center
    float3 L = center - rayStart;

    // tca = projection of L onto rayDir
    float tca = dot(L, rayDir);

    // d2 = squared distance from sphere center to ray
    float d2 = dot(L, L) - tca * tca;
    float radius2 = radius * radius;

    // no intersection
    if (d2 > radius2) return -1.0;

    // thc = distance from closest approach to intersection point
    float thc = sqrt(radius2 - d2);

    // compute distances along ray
    float t0 = tca - thc;
    float t1 = tca + thc;

    // choose nearest positive intersection
    if (t0 > 0.0) return t0;
    if (t1 >= 0.0) return t1;

    // both intersections are behind ray
    return -1.0;
}

void UvToTransmittanceLutParams(float bottomRadius, float topRadius, float2 uv, out float mu, out float r)
{
    float x_mu = uv.x;
    float x_r = uv.y;
    float H = sqrt(max(0.0f, topRadius * topRadius - bottomRadius * bottomRadius));

    float rho = H * x_r;
    float rho2 = rho * rho;
    r = sqrt(max(0.0f, rho2 + bottomRadius * bottomRadius));

    float d_min = topRadius - r;
    float d_max = rho + H;
    float d = d_min + x_mu * (d_max - d_min);

    float denom = 2.0 * r * d + 1e-6;          // 防止除零
    float mu_raw = (H*H - rho2 - d*d) *rcp(denom); // 公式处理 d==0 时也稳定
    
    mu = saturate(mu_raw * 0.5 + 0.5) * 2.0 - 1.0; // 将 mu 映射回 [-1, 1]
}

float2 GetTransmittanceLutUV(float bottomRadius, float topRadius, float mu, float r)
{
    float H =sqrt(topRadius *topRadius -bottomRadius *bottomRadius);
    
    float rho2 = r*r - bottomRadius*bottomRadius;
    float rho  = sqrt(max(rho2, 0.0));

    float discriminant =  r * r *(mu * mu -1.0f) + topRadius * topRadius;
    float d =max(0.0f,(-r *mu +sqrt(discriminant)));

    float d_min = topRadius - r;
    float d_max = rho + H;
    
    float x_mu = saturate((d - d_min) * rcp(d_max - d_min));
    float x_r = saturate(rho * rcp(H));

    return  float2( x_mu, x_r );
}

float aerialPerspectiveDepthToSlice(float depth) { return depth * 0.25f;}  // { return depth * (1.0f / kAtmosphereAirPerspectiveKmPerSlice); }
float aerialPerspectiveSliceToDepth(float slice) { return slice * kAtmosphereAirPerspectiveKmPerSlice; }

// https://github.com/sebh/UnrealEngineSkyAtmosphere
// Transmittance LUT function parameterisation from Bruneton 2017 https://github.com/ebruneton/precomputed_atmospheric_scattering
// Detail also in video https://www.youtube.com/watch?v=y-oBGzDCZKI at 08:35.
void lutTransmittanceParamsToUv(
    in float viewHeight, // [bottomRadius, topRadius]
    in float viewZenithCosAngle, // [-1,1]
    out float2 uv) // [0,1]
{

    float bottomRadius =frameData.sky.atmosphereConfig.PlanetRadius;
    float topRadius =bottomRadius +frameData.sky.atmosphereConfig.AtmosphereHeight;
    
    float H = sqrt(max(0.0f, topRadius *topRadius - bottomRadius * bottomRadius));
    float rho = sqrt(max(0.0f, viewHeight * viewHeight - bottomRadius * bottomRadius));

    uv.y = rho / H;

    // Distance to atmosphere boundary
    float discriminant = viewHeight * viewHeight * (viewZenithCosAngle * viewZenithCosAngle - 1.0) + topRadius * topRadius;
    float d = max(0.0, (-viewHeight * viewZenithCosAngle + sqrt(discriminant))); 

    float dMin = topRadius - viewHeight;
    float dMax = rho + H;

    uv.x = (d - dMin) / (dMax - dMin);
}


// Camera unit to atmosphere unit convert. meter -> kilometers.
float3 convertToAtmosphereUnit(float3 o)
{
    const float cameraOffset = 0.5f;
    return o * 0.001f + float3(0.0, cameraOffset, 0.0);
}


// atmosphere unit to camera unit convert. kilometers -> meter.
float3 convertToCameraUnit(float3 o)
{
    const float cameraOffset = 0.5f;
    return (o - float3(0.0, cameraOffset, 0.0)) * 1000.0f;
}  
//meter -> kilometers.
float WorldToAtmosphereUnit(float o)
{
    return o * 0.001f;
}
//kilometers ->  meter.
float AtmosphereToWorldUnit(float o)
{
    return o * 1000.0f;
}  
void skyViewLutParamsToUv(
    in bool  bIntersectGround,
    in float bottomRadius,
    in float viewZenithCosAngle, 
    in float lightViewCosAngle, 
    in float viewHeight, 
    in float2 lutSize,
    out float2 uv)
{
    float vHorizon = sqrt(viewHeight * viewHeight - bottomRadius * bottomRadius);

    // Ground to horizon cos.
    float cosBeta = vHorizon / viewHeight;		

    float beta = acos(cosBeta);
    float zenithHorizonAngle = kPI - beta;

    if (!bIntersectGround)
    {
        float coord = acos(viewZenithCosAngle) / zenithHorizonAngle;
        coord = 1.0 - coord;
        coord = sqrt(coord); // Non-linear sky view lut.

        coord = 1.0 - coord;
        uv.y = coord * 0.5f;
    }
    else
    {
        float coord = (acos(viewZenithCosAngle) - zenithHorizonAngle) / beta;
        coord = sqrt(coord); // Non-linear sky view lut.

        uv.y = coord * 0.5f + 0.5f;
    }

    // UV x remap.
    {
        float coord = -lightViewCosAngle * 0.5f + 0.5f;
        coord = sqrt(coord);
        uv.x = coord;
    }

    // Constrain uvs to valid sub texel range (avoid zenith derivative issue making LUT usage visible)
    uv = float2(fromUnitToSubUvs(uv.x, lutSize.x), fromUnitToSubUvs(uv.y, lutSize.y));
}

#endif