#ifndef CUBIC_BEZIER_INCLUDED
#define CUBIC_BEZIER_INCLUDED

float3 CubicBezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float opt = 1 - t;
    float opt2 = opt * opt;
    float tt = t * t;

    return p0 * (opt * opt2) +
           p1 * (3 * opt2 * t) +
           p2 * (3 * opt * tt) +
           p3 * (  t *tt);
}
float3 CubicBezier2(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float3 a = lerp(p0, p1, t);
    float3 b = lerp(p2, p3, t);
    float3 c = lerp(p1, p2, t);
    float3 d = lerp(a, c, t);
    float3 e = lerp(c, b, t);
    return lerp(d, e, t);
}
float3 CubicBezierTangent(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float opt = 1 - t;
    float opt2 = opt * opt;
    float tt = t * t;

    float tangent =
        p0 * (-opt2) +
        p1 * (3 * opt2 - 2 * opt) +
        p2 * (-3 * tt + 2 * t) +
        p3 * (tt);

    return normalize(tangent);
}
#endif // CUBIC_BEZIER_INCLUDED