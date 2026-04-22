// =============================================================================================
// CubicBezier.hlsl
// 用途: 草片(GrassBlade)顶点阶段基于 t(0..1) 沿三次贝塞尔曲线插值获取中心线位置与切线。
// 公式:
//  B(t) = (1-t)^3 * p0 + 3(1-t)^2 t * p1 + 3(1-t)t^2 * p2 + t^3 * p3,  t ∈ [0,1]
//  导数(切线):
//  B'(t) = -3(1-t)^2 p0 + (3(1-t)^2 - 6(1-t)t) p1 + (6(1-t)t - 3t^2) p2 + 3 t^2 p3
//  这里在实现中做了代数合并，见 CubicBezierTangent。
// 性能说明:
//  - 预先计算 (1-t) 及平方可减少乘法。
//  - 结果在顶点着色器中频繁调用，应保持无分支轻量。
// 数值注意:
//  - t 需限制在 [0,1]，若外部传入存在浮点误差，可在调用处 saturate(t)。
//  - 若 p0≈p3 且 p1,p2 重合，曲线退化为短线段，切线仍可正常归一化。
// =============================================================================================
#ifndef CUBIC_BEZIER_INCLUDED
#define CUBIC_BEZIER_INCLUDED

float3 CubicBezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float omt = 1 - t;          // one minus t
    float omt2 = omt * omt;     // (1-t)^2
    float t2 = t * t;           // t^2

    // 按标准三次贝塞尔展开
    return p0 * (omt * omt2) +          // (1-t)^3 p0
            p1 * (3 * omt2 * t) +       // 3(1-t)^2 t p1
            p2 * (3 * omt * t2) +       // 3(1-t) t^2 p2
            p3 * (t * t2);              // t^3 p3
}

float3 CubicBezierTangent(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float omt = 1 - t;
    float omt2 = omt * omt;
    float t2 = t * t;

    // 经过整理后的导数表达式 (见上面注释中的原始形式)，再归一化得到单位切线
    float3 tangent =
            p0 * (-omt2) +
            p1 * (3 * omt2 - 2 * omt) +
            p2 * (-3 * t2 + 2 * t) +
            p3 * (t2);

    return normalize(tangent); // 若长度接近 0，外层使用时应考虑回退到 (0,1,0) 等默认方向
}

#endif // CUBIC_BEZIER_INCLUDED