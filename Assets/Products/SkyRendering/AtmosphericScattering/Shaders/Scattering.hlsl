#ifndef  VOLUMETRIC_SCATTERING_INCLUDED
#define  VOLUMETRIC_SCATTERING_INCLUDED
//Volumetric Scattering 

static const float INV_4PI = 1.0 / (4.0 * PI);
static const float EPS_DENOM = 1e-6; // 可调：越小越接近物理，但稳定性弱

struct AtmosphereParameter
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

//========================= Scattering Coefficient =========================

float ExpHeight(float h, float H) { return exp(-h * rcp(H)); }

float3 RayleighCoefficient(in AtmosphereParameter param, float h)
{
    return float3(5.802e-6, 13.558e-6, 33.1e-6) * ExpHeight(h, param.RayleighScatteringScalarHeight);
}

float3 MieCoefficient(in AtmosphereParameter param, float h)
{
    return (3.996e-6).xxx * ExpHeight(h, param.MieScatteringScalarHeight);
}

// float3 RayleighCoefficient(in AtmosphereParameter param, float h)
// {
//     const float3 sigma = float3(5.802, 13.558, 33.1) * 1e-6;
//     float H_R = param.RayleighScatteringScalarHeight;
//     float rho_h = exp(-(h / H_R));
//     return sigma * rho_h;
// }
//
// float3 MieCoefficient(in AtmosphereParameter param, float h)
// {
//     const float3 sigma = (3.996 * 1e-6).xxx;
//     float H_M = param.MieScatteringScalarHeight;
//     float rho_h = exp(-(h / H_M));
//     return sigma * rho_h;
// }


//======================Phase Functions======================
//-----------Isotropic Phase Function-----------
float Phase_Isotropic() // No angular dependence
{
    return INV_4PI;
}


//---------------HG Henyey-Greenstein Phase Function--------------

float Phase_HG(float cosTheta, float g)
{
    //HG_Phase = 1/(4π) * (1 - g²) / (1 + g² - 2gcosθ)^(3/2)

    float g2 = g * g;

    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    denom = max(denom, EPS_DENOM); // 避免浮点炸裂
    float denom_32 = denom * sqrt(denom); // denom^(1.5)

    return INV_4PI * (1.0 - g2) * rcp(denom_32);
}

float Phase_SingleHG(float cosTheta, float g)
{
    g = clamp(g, -0.99999, 0.99999);
    return Phase_HG(cosTheta, g);
}

float Phase_DoubleHG(float cosTheta, float g1, float g2, float w1)
{
    float w2 = 1.0 - w1;
    return w1 * Phase_SingleHG(cosTheta, g1) + w2 * Phase_SingleHG(cosTheta, g2);
}

float Phase_DoubleHG(float cosTheta, float3 params)
{
    return Phase_DoubleHG(cosTheta, params.x, params.y, params.z);
}

//---------Rayleigh Phase Function------------
float Phase_Rayleigh(float cosTheta)
{
    return (3.0 / (16.0 * PI)) * (1.0 + cosTheta * cosTheta);
}

//-----------Mie Phase Function--------------
float Phase_Mie(float cosTheta, float g)
{
    // Mie_phase(cosTheta,g) = 3/(8π) * (1 - g²)/(2 + g²) * (1 + cos²θ) / (1 + g² - 2gcosθ)^(3/2)
    // =  A * B * C * (1/D)

    const float A = 3.0 / (8.0 * PI);

    float g2 = g * g;
    float B = (1.0 - g2) * rcp(2.0 + g2);

    float C = 1.0 + cosTheta * cosTheta;

    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    denom = max(denom, EPS_DENOM); // 避免浮点炸裂
    float D = denom * sqrt(denom); // denom^(1.5)

    return A * B * C * rcp(D);
}

//========================= Scattering Functions ========================

float3 Scattering(in AtmosphereParameter param, float3 p, float3 inDir, float3 outDir)
{
    float cosTheta = dot(inDir, outDir);

    float h = length(p) - param.PlanetRadius;

    float3 rayleigh = RayleighCoefficient(param, h) * Phase_Rayleigh(cosTheta);
    // float3 mie = MieCoefficient(param, h) * Phase_Mie(cosTheta, param.MieAnisotropy);
    // Alternatively, using Double Henyey-Greenstein Phase Function
    // 使用双HG相函数 g1=0.76, g2=-0.4, w1=0.9
    float3 mie =  MieCoefficient(param, h) * Phase_DoubleHG(cosTheta, param.MiePhaseParams.xyz);
    
    return rayleigh + mie;
}

//===================== Transmittance ============================

float3 MieAbsorption(in AtmosphereParameter param, float h)
{
    const float3 sigma = (4.4e-6).xxx;
    return sigma * ExpHeight(h, param.MieScatteringScalarHeight);
}

float3 OzoneAbsorption(in AtmosphereParameter param, float h)
{
    const float3 sigma_lambda = (float3(0.650f, 1.881f, 0.085f)) * 1e-6;
    float center = param.OzoneLevelCenterHeight;
    float width = param.OzoneLevelWidth;
    float rho = max(0.0, (1.0 - (abs(h - center) * rcp(width))));
    return sigma_lambda * rho;
}

float3 ExtinctionCoefficient(in AtmosphereParameter param, float h)
{
    float3 scattering = RayleighCoefficient(param, h) + MieCoefficient(param, h); // scattering
    float3 absorption = OzoneAbsorption(param, h) + MieAbsorption(param, h); // absorption
    float3 extinction = scattering + absorption; // extinction
    return extinction; // extinction
}

float3 Transmittance(in AtmosphereParameter param, float3 p, float3 dir, float distance)
{
    const int N_SAMPLE = 32;

    float ds = distance * rcp(float(N_SAMPLE));
    float3 sum = 0.0;
    p = p + (dir * ds) * 0.5; // mid-point rule 对湮灭系数的估计是通过采样路径的中点值来代表

    for (int i = 0; i < N_SAMPLE; i++)
    {
        float h = length(p) - param.PlanetRadius;

        float3 extinction = ExtinctionCoefficient(param, h);

        sum += extinction * ds;
        p += dir * ds;
    }
    return exp(-sum);
}

float3 Transmittance(in AtmosphereParameter param, float3 p1, float3 p2)
{
    return Transmittance(param, p1, normalize(p2 - p1), length(p2 - p1));
}

#endif
