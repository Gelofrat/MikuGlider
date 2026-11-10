using UnityEngine;

namespace MikuGlider;

internal readonly struct SimulationCapsule
{
    internal Vector3 Start { get; }
    internal Vector3 End { get; }
    internal float Radius { get; }
    internal float EndRadius { get; }

    internal SimulationCapsule(Vector3 start, Vector3 end, float radius) : this(start, end, radius, radius)
    {
    }

    internal SimulationCapsule(Vector3 start, Vector3 end, float radius, float endRadius)
    {
        Start = start;
        End = end;
        Radius = radius;
        EndRadius = endRadius;
    }

    internal Vector3 ProjectOutside(Vector3 point, Vector3 fallback)
    {
        Vector3 axis = End - Start;
        float fraction = axis.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(point - Start, axis) / axis.sqrMagnitude)
            : 0f;
        Vector3 center = Start + axis * fraction;
        float radius = Mathf.Lerp(Radius, EndRadius, fraction);
        Vector3 offset = point - center;
        if (offset.sqrMagnitude >= radius * radius)
            return point;

        if (offset.sqrMagnitude < 0.000001f)
            offset = fallback - center;
        if (offset.sqrMagnitude < 0.000001f)
            offset = Vector3.back;

        return center + offset.normalized * radius;
    }

    internal float Penetration(Vector3 point)
    {
        Vector3 axis = End - Start;
        float fraction = axis.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(point - Start, axis) / axis.sqrMagnitude)
            : 0f;
        return Mathf.Max(0f, Mathf.Lerp(Radius, EndRadius, fraction) - (point - Start - axis * fraction).magnitude);
    }
}
