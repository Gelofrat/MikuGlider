using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal sealed class GarmentProbeSet
{
    private readonly ProbeCluster[] clusters;

    internal GarmentProbeSet(Vector3[] points)
    {
        var groups = new Dictionary<Vector3Int, List<Vector3>>();
        foreach (Vector3 point in points)
        {
            Vector3Int cell = points.Length < 32 ? Vector3Int.zero : Vector3Int.FloorToInt(point / .06f);
            if (!groups.TryGetValue(cell, out List<Vector3> group))
            {
                group = new List<Vector3>();
                groups.Add(cell, group);
            }
            group.Add(point);
        }
        clusters = new ProbeCluster[groups.Count];
        int index = 0;
        foreach (List<Vector3> group in groups.Values) clusters[index++] = new ProbeCluster(group.ToArray());
    }

    internal bool Intersects(GarmentCollisionCapsule left, GarmentCollisionCapsule right)
    {
        foreach (ProbeCluster cluster in clusters)
        {
            bool testLeft = left.IntersectsSphere(cluster.Center, cluster.Radius);
            bool testRight = right.IntersectsSphere(cluster.Center, cluster.Radius);
            if (!testLeft && !testRight) continue;
            foreach (Vector3 point in cluster.Points)
                if ((testLeft && left.Contains(point)) || (testRight && right.Contains(point))) return true;
        }
        return false;
    }

    private sealed class ProbeCluster
    {
        internal readonly Vector3[] Points;
        internal readonly Vector3 Center;
        internal readonly float Radius;

        internal ProbeCluster(Vector3[] points)
        {
            Points = points;
            Vector3 minimum = points[0], maximum = points[0];
            foreach (Vector3 point in points)
            {
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }
            Center = (minimum + maximum) * .5f;
            float squared = 0;
            foreach (Vector3 point in points) squared = Mathf.Max(squared, (point - Center).sqrMagnitude);
            Radius = Mathf.Sqrt(squared) + .00001f;
        }
    }
}

internal readonly struct GarmentCollisionCapsule
{
    private readonly Vector3 hip;
    private readonly Vector3 axis;
    private readonly float length;
    private readonly float sampleScale;
    private readonly float[] radii;
    private readonly float maximumRadius;

    internal GarmentCollisionCapsule(Vector3 hip, Vector3 axis, float length, float[] radii, float maximumRadius)
    {
        this.hip = hip;
        this.axis = axis;
        this.length = length;
        this.radii = radii;
        this.maximumRadius = maximumRadius;
        sampleScale = radii.Length / Mathf.Max(.001f, length);
    }

    internal bool IntersectsSphere(Vector3 center, float radius)
    {
        Vector3 offset = center - hip;
        float distance = Mathf.Clamp(Vector3.Dot(offset, axis), 0, length);
        float extent = maximumRadius + radius;
        return (offset - axis * distance).sqrMagnitude <= extent * extent;
    }

    internal bool Contains(Vector3 point)
    {
        Vector3 offset = point - hip;
        float distance = Mathf.Clamp(Vector3.Dot(offset, axis), 0, length);
        float sample = Mathf.Clamp(distance * sampleScale - .5f, 0, radii.Length - 1);
        int index = Mathf.FloorToInt(sample);
        float radius = Mathf.Lerp(radii[index], radii[Mathf.Min(index + 1, radii.Length - 1)], sample - index);
        return (offset - axis * distance).sqrMagnitude < radius * radius;
    }
}
