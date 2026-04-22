#ifndef SHARED_FUNCTIONS_HLSL
#define SHARED_FUNCTIONS_HLSL

#define kPI 3.14159265359


float3 mod(float3 x, float3 y)
{
	return x - y * floor(x / y);
}

float mod(float x, float y)
{
	return x - y * floor(x / y);
}

// 4x4 bayer filter, use for cloud reconstruction.
const int kBayerMatrix16[16] = 
{
    0,  8,  2, 10, 
   12,  4, 14,  6, 
    3, 11,  1,  9, 
   15,  7, 13,  5
};

float bayerDither(float grayscale, int2 pixelCoord)
{    
    int pixelIndex16 = (pixelCoord.x % 4) + (pixelCoord.y % 4) * 4;
    return grayscale > (float(kBayerMatrix16[pixelIndex16]) + 0.5) / 16.0 ? 1.0 : 0.0;
}

float bayer2(float2 a) {
    a = floor(a);

    return frac(dot(a, float2(0.5, a.y * 0.75)));
}

float bayer4(const float2 a)   { return bayer2 (0.5   * a) * 0.25     + bayer2(a); }
float bayer8(const float2 a)   { return bayer4 (0.5   * a) * 0.25     + bayer2(a); }
float bayer16(const float2 a)  { return bayer4 (0.25  * a) * 0.0625   + bayer4(a); }
float bayer32(const float2 a)  { return bayer8 (0.25  * a) * 0.0625   + bayer4(a); }
float bayer64(const float2 a)  { return bayer8 (0.125 * a) * 0.015625 + bayer8(a); }
float bayer128(const float2 a) { return bayer16(0.125 * a) * 0.015625 + bayer8(a); }

// On range, [minV, maxV]
bool onRange(float x, float minV, float maxV) { return x >= minV && x <= maxV;}
bool onRange( float2 x,  float2 minV,  float2 maxV) { return onRange(x.x, minV.x, maxV.x) && onRange(x.y, minV.y, maxV.y);}
bool onRange( float3 x,  float3 minV,  float3 maxV) { return onRange(x.x, minV.x, maxV.x) && onRange(x.y, minV.y, maxV.y) && onRange(x.z, minV.z, maxV.z);}
bool onRange( float4 x,  float4 minV,  float4 maxV) { return onRange(x.x, minV.x, maxV.x) && onRange(x.y, minV.y, maxV.y) && onRange(x.z, minV.z, maxV.z) && onRange(x.w, minV.w, maxV.w);}

float3 cubeSmooth(float3 x)
{
    return x * x * (3.0 - 2.0 * x);
}

float remap(float value, float orignalMin, float orignalMax, float newMin, float newMax)
{
	return newMin + (saturate((value - orignalMin) / (orignalMax - orignalMin)) * (newMax - newMin));
}
// float remap(float x, float a, float b, float c, float d)
// {
// 	return (((x - a) / (b - a)) * (d - c)) + c;
// }

//================================================================================
// =======================Ray sphere intersection.======================================== 
// https://zhuanlan.zhihu.com/p/136763389
// https://www.scratchapixel.com/lessons/3d-basic-rendering/minimal-ray-tracer-rendering-simple-shapes/ray-sphere-intersection
// Returns distance from r0 to first intersecion with sphere, or -1.0 if no intersection.
//获取最近的正向交点 (无交点返回 -1.0)
float raySphereIntersectNearest(
      float3  r0  // ray origin
    , float3  rd  // normalized ray direction
    , float3  s0  // sphere center
    , float sR) // sphere radius
{
	float a = dot(rd, rd);

	float3 s02r0 = r0 - s0;
	float b = 2.0 * dot(rd, s02r0);

	float c = dot(s02r0, s02r0) - (sR * sR);
	float delta = b * b - 4.0 * a * c;

    // No intersection state.
	if (delta < 0.0 || a == 0.0)
	{
		return -1.0;
	}

	float sol0 = (-b - sqrt(delta)) / (2.0 * a);
	float sol1 = (-b + sqrt(delta)) / (2.0 * a);
	// sol1 > sol0


    // Intersection on negative direction, no suitable for ray.
	if (sol1 < 0.0) // When sol1 < 0.0, sol0 < 0.0 too.
	{
		return -1.0;
	}

    // Maybe exist one positive intersection.
	if (sol0 < 0.0)
	{
		return max(0.0, sol1);
	}

    // Two positive intersection, return nearest one.
	return max(0.0, min(sol0, sol1));
}

// When ensure r0 is inside of sphere.
// Only exist one positive result, use it.
// 2. 射线起点在球体内部，必然只有一个正交点
float raySphereIntersectInside(
      float3  r0  // ray origin
    , float3  rd  // normalized ray direction
    , float3  s0  // sphere center
    , float sR) // sphere radius
{
	float a = dot(rd, rd);

	float3 s02r0 = r0 - s0;
	float b = 2.0 * dot(rd, s02r0);

	float c = dot(s02r0, s02r0) - (sR * sR);
	float delta = b * b - 4.0 * a * c;

	// float sol0 = (-b - sqrt(delta)) / (2.0 * a);
	float sol1 = (-b + sqrt(delta)) / (2.0 * a);

	// sol1 > sol0, so just return sol1
	return sol1;
}

// Ray intersection from outside of sphere.
// Return true if exist intersect. don't care about tangent case.
// 3. 射线从球体外部求交，如果有交点，返回两个交点距离
bool raySphereIntersectOutSide(
      float3  r0  // ray origin
    , float3  rd  // normalized ray direction
    , float3  s0  // sphere center
    , float sR  // sphere radius
	, out float2 t0t1) 
{
	float a = dot(rd, rd);

	float3 s02r0 = r0 - s0;
	float b = 2.0 * dot(rd, s02r0);

	float c = dot(s02r0, s02r0) - (sR * sR);
	float delta = b * b - 4.0 * a * c;

    // No intersection state.
	if (delta < 0.0 || a == 0.0)
	{
		return false;
	}

	float sol0 = (-b - sqrt(delta)) / (2.0 * a);
	float sol1 = (-b + sqrt(delta)) / (2.0 * a);
	

    // Intersection on negative direction, no suitable for ray.
	if (sol1 <= 0.0 || sol0 <= 0.0)
	{
		return false;
	}

    // Two positive intersection, return nearest one.
	t0t1 = float2(sol0, sol1); // sol1 > sol0
	return true; 
}

// ==========================================
// 优化版本原点球体求交函数 (大幅缩减 ALU 指令)
// 前提: 射线 rd 已归一化，且球心在 (0,0,0)
// 球心在 (0,0,0),所以标准的求交方程可以化简为 $t^2 + 2Bt + C = 0$（其中 $B = \mathbf{r_d} \cdot \mathbf{r_0}$，$C = \mathbf{r_0} \cdot \mathbf{r_0} - R^2$）。

// 1. 获取最近的正向交点 (无交点返回 -1.0)
float hitSphereNearest(
	float3 r0, // ray origin
	float3 rd, // normalized ray direction
	float sR // sphere radius
	) 
{
	float B = dot(r0, rd);
	float C = dot(r0, r0) - (sR * sR);
	float det = B * B - C;

	if (det < 0.0) return -1.0;

	float sqrtDet = sqrt(det);
	float t0 = -B - sqrtDet;
	if (t0 >= 0.0) return t0;
    
	float t1 = -B + sqrtDet;
	return t1 >= 0.0 ? t1 : -1.0;
}

// 2. 射线起点在球体内部，必然只有一个正交点
float hitSphereInside(float3 r0, float3 rd, float sR) 
{
	float B = dot(r0, rd);
	float C = dot(r0, r0) - (sR * sR);
	// 内部发射必然有交点，直接求 t1 (较大的那个根)
	return -B + sqrt(B * B - C); 
}

// 3. 射线从球体外部求交，如果有交点，返回两个交点距离
bool hitSphereOutside(float3 r0, float3 rd, float sR, out float2 t0t1) 
{
	float B = dot(r0, rd);
	float C = dot(r0, r0) - (sR * sR);
	float det = B * B - C;

	if (det < 0.0) return false;

	float sqrtDet = sqrt(det);
	float t0 = -B - sqrtDet;
	float t1 = -B + sqrtDet;

	// 如果 t0 <= 0，说明在球内或背向球体，不符合 Outside 定义
	if (t0 <= 0.0) return false;

	t0t1 = float2(t0, t1);
	return true;
}

//================================================================================
//================================================================================

float fromUnitToSubUvs(float u, float resolution) { return (u + 0.5f / resolution) * (resolution / (resolution + 1.0f)); }
float fromSubUvsToUnit(float u, float resolution) { return (u - 0.5f / resolution) * (resolution / (resolution - 1.0f)); }


float getUniformPhase()
{
	return 1.0f / (4.0f * kPI);
}

// https://www.shadertoy.com/view/Mtc3Ds
// rayleigh phase function.
float rayleighPhase(float cosTheta)
{
	const float factor = 3.0f / (16.0f * kPI);
	return factor * (1.0f + cosTheta * cosTheta);
}

// Schlick approximation
float cornetteShanksMiePhaseFunction(float g, float cosTheta)
{
	float k = 3.0 / (8.0 * kPI) * (1.0 - g * g) / (2.0 + g * g);
	return k * (1.0 + cosTheta * cosTheta) / pow(1.0 + g * g - 2.0 * g * -cosTheta, 1.5);
}

// Crazy light intensity.
float henyeyGreenstein(float cosTheta, float g) 
{
	float gg = g * g;
	return (1. - gg) / pow(1. + gg - 2. * g * cosTheta, 1.5);
}

// See http://www.pbr-book.org/3ed-2018/Volume_Scattering/Phase_Functions.html
float hgPhase(float g, float cosTheta)
{
	float numer = 1.0f - g * g;
	float denom = 1.0f + g * g + 2.0f * g * cosTheta;
	return numer / (4.0f * kPI * denom * sqrt(denom));
}

float dualLobPhase(float g0, float g1, float w, float cosTheta)
{
	return lerp(hgPhase(g0, cosTheta), hgPhase(g1, cosTheta), w);
}

float beersLaw(float density, float stepLength, float densityScale)
{
	return exp(-density * stepLength * densityScale);
}

float powderEffectNew(float depth, float height, float VoL)
{
	// float r = VoL * 0.5 + 0.5;
	float r = -abs(VoL) * 0.5 + 0.5;
	r = r * r;

	height = height * (1.0 - r) + r;
	return depth * height;
}

#endif