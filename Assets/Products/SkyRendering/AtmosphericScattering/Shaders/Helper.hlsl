#ifndef ATMOSPHERE_HELPER_HLSL
#define ATMOSPHERE_HELPER_HLSL

#ifndef PI
#define PI 3.14159265359
#endif

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

    //避免 self-shadow / self-intersection
    // const float EPS = 1e-4;
    // if (t0 > EPS) return t0;
    // if (t1 > EPS) return t1;

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

#endif