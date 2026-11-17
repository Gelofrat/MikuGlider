using System;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal static class GeometryMath
{
    internal static bool Finite(Vector3 point)
    {
        return Finite(point.x) && Finite(point.y) && Finite(point.z);
    }

    internal static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal static float CapsuleDistance(Vector3 point, Vector3 start, Vector3 end, float startRadius, float endRadius)
    {
        Vector3 axis = end - start;
        float length = axis.magnitude;
        if (length < 0.000001f || Math.Abs(endRadius - startRadius) >= length)
            return startRadius >= endRadius ? (point - start).magnitude - startRadius : (point - end).magnitude - endRadius;
        axis /= length;
        Vector3 relative = point - start;
        float axial = Vector3.Dot(relative, axis);
        float radial = (relative - axis * axial).magnitude;
        float slope = (endRadius - startRadius) / length;
        float position = Math.Max(0f, Math.Min(length, axial + slope * radial / (float)Math.Sqrt(1f - slope * slope)));
        return (float)Math.Sqrt((position - axial) * (position - axial) + radial * radial) - startRadius - slope * position;
    }

    internal static bool SegmentTriangle(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c,
        float tolerance, out Vector3 hit)
    {
        hit = Vector3.zero;
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= tolerance * 2f)
            return false;
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 p = Vector3.Cross(direction, ac);
        float determinant = Vector3.Dot(ab, p);
        if (Math.Abs(determinant) < 1e-12f)
            return false;
        float inverse = 1f / determinant;
        Vector3 origin = start - a;
        float u = Vector3.Dot(origin, p) * inverse;
        if (u < -0.000001f || u > 1.000001f)
            return false;
        Vector3 q = Vector3.Cross(origin, ab);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < -0.000001f || u + v > 1.000001f)
            return false;
        float t = Vector3.Dot(ac, q) * inverse;
        if (t <= tolerance / length || t >= 1f - tolerance / length)
            return false;
        hit = start + direction * t;
        return true;
    }
}
