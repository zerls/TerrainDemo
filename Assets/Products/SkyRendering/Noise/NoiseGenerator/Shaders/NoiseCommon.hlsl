#pragma once

// 改进的随机函数
float random(uint seed) {
    return frac(sin(seed * 127.1) * 43758.5453);
}

float2 random2(float2 seed) {
    return frac(
        sin(float2(
            dot(seed, float2(127.1, 311.7)),
            dot(seed, float2(269.5, 183.3))
        )) * 43758.5453
    );
}

float3 random3(float3 seed) {
    return frac(
        sin(float3(
            dot(seed, float3(127.1, 311.7, 74.7)),
            dot(seed, float3(269.5, 183.3, 246.1)),
            dot(seed, float3(113.5, 271.9, 124.6))
        )) * 43758.5453
    );
}

// Halton序列生成器
float Halton(int index, int base) {
    float result = 0.0;
    float f = 1.0;
    while (index > 0) {
        f /= base;
        result += f * (index % base);
        index = index / base;
    }
    return result;
}

float perlinNoise(float2 uv) {
    // Perlin噪声实现
    float2 i = floor(uv);
    float2 f = frac(uv);
    
    float a = dot(random2(i), f);
    float b = dot(random2(i + float2(1,0)), f - float2(1,0));
    float c = dot(random2(i + float2(0,1)), f - float2(0,1));
    float d = dot(random2(i + float2(1,1)), f - float2(1,1));
    
    float2 u = smoothstep(0,1,f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float3 fade(float3 t) {
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float3 gradient3(float3 seed) {
    float3 gradient = random3(seed) * 2.0 - 1.0;
    return normalize(gradient + 1e-5);
}

float perlinNoise(float3 uv) {
    float3 cell = floor(uv);
    float3 local = frac(uv);
    float3 smooth = fade(local);

    float n000 = dot(gradient3(cell + float3(0, 0, 0)), local - float3(0, 0, 0));
    float n100 = dot(gradient3(cell + float3(1, 0, 0)), local - float3(1, 0, 0));
    float n010 = dot(gradient3(cell + float3(0, 1, 0)), local - float3(0, 1, 0));
    float n110 = dot(gradient3(cell + float3(1, 1, 0)), local - float3(1, 1, 0));
    float n001 = dot(gradient3(cell + float3(0, 0, 1)), local - float3(0, 0, 1));
    float n101 = dot(gradient3(cell + float3(1, 0, 1)), local - float3(1, 0, 1));
    float n011 = dot(gradient3(cell + float3(0, 1, 1)), local - float3(0, 1, 1));
    float n111 = dot(gradient3(cell + float3(1, 1, 1)), local - float3(1, 1, 1));

    float nx00 = lerp(n000, n100, smooth.x);
    float nx10 = lerp(n010, n110, smooth.x);
    float nx01 = lerp(n001, n101, smooth.x);
    float nx11 = lerp(n011, n111, smooth.x);
    float nxy0 = lerp(nx00, nx10, smooth.y);
    float nxy1 = lerp(nx01, nx11, smooth.y);
    return lerp(nxy0, nxy1, smooth.z);
}

float fractalNoise(float2 uv, int octaves, float persistence, float lacunarity) {
    float total = 0;
    float frequency = 1;
    float amplitude = 1;
    float maxValue = 0;
    
    for(int i=0; i<octaves; i++) {
        total += perlinNoise(uv * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }
    
    return saturate(total / maxValue * 0.5 + 0.5);
}

float fractalNoise(float3 uv, int octaves, float persistence, float lacunarity) {
    float total = 0;
    float frequency = 1;
    float amplitude = 1;
    float maxValue = 0;

    for(int i = 0; i < octaves; i++) {
        total += perlinNoise(uv * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return saturate(total / maxValue * 0.5 + 0.5);
}

float2 hash22(float2 seed) {
    return random2(seed);
}

float3 hash33(float3 seed) {
    return random3(seed);
}

float2 wrapDelta(float2 delta) {
    return frac(delta + 0.5) - 0.5;
}

float3 wrapDelta(float3 delta) {
    return frac(delta + 0.5) - 0.5;
}

void cellularNoise(float2 uv, out float nearest, out float secondNearest) {
    float2 cell = floor(uv);
    float2 local = frac(uv);

    nearest = 8.0;
    secondNearest = 8.0;

    [unroll]
    for (int y = -1; y <= 1; y++) {
        [unroll]
        for (int x = -1; x <= 1; x++) {
            float2 neighbor = float2(x, y);
            float2 featurePoint = hash22(cell + neighbor);
            float distanceToPoint = length(neighbor + featurePoint - local);

            if (distanceToPoint < nearest) {
                secondNearest = nearest;
                nearest = distanceToPoint;
            }
            else if (distanceToPoint < secondNearest) {
                secondNearest = distanceToPoint;
            }
        }
    }
}

void cellularNoise(float3 uv, out float nearest, out float secondNearest) {
    float3 cell = floor(uv);
    float3 local = frac(uv);

    nearest = 8.0;
    secondNearest = 8.0;

    [unroll]
    for (int z = -1; z <= 1; z++) {
        [unroll]
        for (int y = -1; y <= 1; y++) {
            [unroll]
            for (int x = -1; x <= 1; x++) {
                float3 neighbor = float3(x, y, z);
                float3 featurePoint = hash33(cell + neighbor);
                float distanceToPoint = length(neighbor + featurePoint - local);

                if (distanceToPoint < nearest) {
                    secondNearest = nearest;
                    nearest = distanceToPoint;
                }
                else if (distanceToPoint < secondNearest) {
                    secondNearest = distanceToPoint;
                }
            }
        }
    }
}

float2 gradient2(float2 seed) {
    float2 gradient = hash22(seed) * 2.0 - 1.0;
    return normalize(gradient + 1e-5);
}

float worleyNoise(float2 uv) {
    float nearest;
    float secondNearest;
    cellularNoise(uv, nearest, secondNearest);
    return 1.0 - saturate(nearest / 1.41421356);
}

float worleyNoise(float3 uv) {
    float nearest;
    float secondNearest;
    cellularNoise(uv, nearest, secondNearest);
    return 1.0 - saturate(nearest / 1.7320508);
}

float voronoiNoise(float2 uv) {
    float nearest;
    float secondNearest;
    cellularNoise(uv, nearest, secondNearest);
    return 1.0 - saturate((secondNearest - nearest) * 2.0);
}

float voronoiNoise(float3 uv) {
    float nearest;
    float secondNearest;
    cellularNoise(uv, nearest, secondNearest);
    return 1.0 - saturate((secondNearest - nearest) * 2.5);
}

float simplexNoise(float2 uv) {
    const float F2 = 0.3660254037844386;
    const float G2 = 0.2113248654051871;

    float2 skewedCell = floor(uv + (uv.x + uv.y) * F2);
    float2 unskew = skewedCell - (skewedCell.x + skewedCell.y) * G2;
    float2 x0 = uv - unskew;

    float2 offset = x0.x > x0.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float2 x1 = x0 - offset + G2;
    float2 x2 = x0 - 1.0 + 2.0 * G2;

    float3 attenuation = max(0.5 - float3(dot(x0, x0), dot(x1, x1), dot(x2, x2)), 0.0);
    float3 attenuation2 = attenuation * attenuation;
    float3 attenuation4 = attenuation2 * attenuation2;

    float n0 = attenuation4.x * dot(gradient2(skewedCell), x0);
    float n1 = attenuation4.y * dot(gradient2(skewedCell + offset), x1);
    float n2 = attenuation4.z * dot(gradient2(skewedCell + 1.0), x2);

    return saturate(0.5 + 35.0 * (n0 + n1 + n2));
}

float4 permute(float4 value) {
    return fmod((value * 34.0 + 1.0) * value, 289.0);
}

float4 taylorInvSqrt(float4 value) {
    return 1.79284291400159 - 0.85373472095314 * value;
}

float simplexNoise(float3 uv) {
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);

    float3 i = floor(uv + dot(uv, C.yyy));
    float3 x0 = uv - i + dot(i, C.xxx);

    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);

    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;

    i = fmod(i, 289.0);
    float4 p = permute(permute(permute(
        i.z + float4(0.0, i1.z, i2.z, 1.0))
        + i.y + float4(0.0, i1.y, i2.y, 1.0))
        + i.x + float4(0.0, i1.x, i2.x, 1.0));

    float4 j = p - 49.0 * floor(p / 49.0);
    float4 x_ = floor(j / 7.0);
    float4 y_ = floor(j - 7.0 * x_);

    float4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
    float4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;
    float4 h = 1.0 - abs(x) - abs(y);

    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);

    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    float3 g0 = float3(a0.xy, h.x);
    float3 g1 = float3(a0.zw, h.y);
    float3 g2 = float3(a1.xy, h.z);
    float3 g3 = float3(a1.zw, h.w);

    float4 norm = taylorInvSqrt(float4(dot(g0, g0), dot(g1, g1), dot(g2, g2), dot(g3, g3)));
    g0 *= norm.x;
    g1 *= norm.y;
    g2 *= norm.z;
    g3 *= norm.w;

    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m *= m;

    return saturate(0.5 + 21.0 * dot(m * m, float4(dot(g0, x0), dot(g1, x1), dot(g2, x2), dot(g3, x3))));
}



