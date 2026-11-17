using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MikuGlider;

internal sealed class GarmentLegClearance
{
    private const int Samples = 6;
    private readonly GarmentProbeSet[] probes;
    private readonly GarmentProbeSet[] surfaceProbes;
    private readonly float hinge;
    private readonly float[] radii = new float[Samples];
    private Vector3 leftHip;
    private Vector3 leftAxis;
    private Vector3 rightHip;
    private Vector3 rightAxis;
    private float leftLength;
    private float rightLength;
    private readonly float maximumRadius;

    internal GarmentLegClearance(Mesh bodyMesh, Transform[] bones, float hinge, int sectors)
    {
        this.hinge = hinge;
        probes = new GarmentProbeSet[sectors];
        surfaceProbes = new GarmentProbeSet[sectors];
        var measurements = new List<float>[Samples];
        for (int i = 0; i < Samples; i++) measurements[i] = new List<float>();
        var body = new CharacterBodyBones(bones);
        Vector3[] vertices = bodyMesh.vertices;
        BoneWeight[] weights = bodyMesh.boneWeights;
        Matrix4x4[] poses = bodyMesh.bindposes;
        MeasureLeg(Array.IndexOf(bones, body.LeftThigh), Array.IndexOf(bones, body.LeftKnee), vertices, weights, poses, measurements);
        MeasureLeg(Array.IndexOf(bones, body.RightThigh), Array.IndexOf(bones, body.RightKnee), vertices, weights, poses, measurements);
        for (int i = 0; i < Samples; i++)
        {
            measurements[i].Sort();
            radii[i] = measurements[i].Count > 0 ? measurements[i][Mathf.FloorToInt((measurements[i].Count - 1) * .98f)] : .10f;
            radii[i] += .018f;
        }
        radii[0] = Mathf.Min(radii[0], radii[1] + .01f);
        foreach (float radius in radii) maximumRadius = Mathf.Max(maximumRadius, radius);
    }

    internal void PrepareSurface(Mesh skirt)
    {
        var points = new HashSet<Vector3>[probes.Length];
        for (int index = 0; index < points.Length; index++) points[index] = new HashSet<Vector3>();
        Vector3[] vertices = skirt.vertices;
        float bottom = vertices.Min(point => point.y);
        foreach (int vertex in skirt.triangles)
        {
            Vector3 point = vertices[vertex];
            if (point.y > hinge - .04f) continue;
            float angle = Mathf.Repeat(Mathf.Atan2(point.x, point.z) / (2f * Mathf.PI), 1f) * probes.Length;
            int sector = Mathf.FloorToInt(angle) % probes.Length;
            points[sector].Add(point);
            points[(sector + 1) % probes.Length].Add(point);
        }
        for (int index = 0; index < points.Length; index++)
        {
            float angle = index * Mathf.PI * 2f / probes.Length;
            Vector3 radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            Vector3[] extremes = SelectExtremes(points[index], radial, bottom);
            Vector3[] surface = points[index].ToArray();
            PrepareCoordinates(extremes, radial);
            PrepareCoordinates(surface, radial);
            probes[index] = new GarmentProbeSet(extremes);
            surfaceProbes[index] = new GarmentProbeSet(surface);
        }
    }

    private void PrepareCoordinates(Vector3[] points, Vector3 radial)
    {
        for (int point = 0; point < points.Length; point++)
        {
            Vector3 value = points[point];
            points[point] = new Vector3(Vector3.Dot(value, radial) - .14f,
                value.y - hinge, value.x * radial.z - value.z * radial.x);
        }
    }

    private Vector3[] SelectExtremes(IEnumerable<Vector3> points, Vector3 radial, float bottom)
    {
        var selected = new HashSet<Vector3>();
        foreach (var ring in points.GroupBy(point => Mathf.Clamp(Mathf.FloorToInt(
            Mathf.InverseLerp(hinge - .04f, bottom, point.y) * Samples), 0, Samples - 1)))
        {
            Vector3[] depth = ring.OrderBy(point => Vector3.Dot(point, radial)).ToArray();
            Vector3[] width = ring.OrderBy(point => point.x * radial.z - point.z * radial.x).ToArray();
            selected.Add(depth[0]);
            selected.Add(depth[depth.Length - 1]);
            selected.Add(width[0]);
            selected.Add(width[width.Length - 1]);
        }
        return selected.ToArray();
    }

    private static void MeasureLeg(int hipBone, int kneeBone, Vector3[] vertices, BoneWeight[] weights,
        Matrix4x4[] poses, List<float>[] measurements)
    {
        if (hipBone < 0 || kneeBone < 0) return;
        Vector3 hip = poses[hipBone].inverse.GetColumn(3);
        Vector3 axis = (Vector3)poses[kneeBone].inverse.GetColumn(3) - hip;
        float length = axis.magnitude;
        if (length < .001f) return;
        axis /= length;
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight weight = weights[i];
            float influence = (weight.boneIndex0 == hipBone ? weight.weight0 : 0) +
                (weight.boneIndex1 == hipBone ? weight.weight1 : 0) +
                (weight.boneIndex2 == hipBone ? weight.weight2 : 0) +
                (weight.boneIndex3 == hipBone ? weight.weight3 : 0);
            if (influence < .5f) continue;
            Vector3 offset = vertices[i] - hip;
            float distance = Vector3.Dot(offset, axis);
            if (distance < 0 || distance > length) continue;
            float radius = (offset - axis * distance).magnitude;
            int sample = Mathf.Clamp(Mathf.FloorToInt(distance / length * Samples), 0, Samples - 1);
            measurements[sample].Add(radius);
        }
    }

    internal void UpdateLegs(Vector3 leftStart, Vector3 leftEnd, Vector3 rightStart, Vector3 rightEnd)
    {
        leftHip = leftStart;
        leftAxis = leftEnd - leftStart;
        leftLength = leftAxis.magnitude;
        leftAxis /= Mathf.Max(.001f, leftLength);
        rightHip = rightStart;
        rightAxis = rightEnd - rightStart;
        rightLength = rightAxis.magnitude;
        rightAxis /= Mathf.Max(.001f, rightLength);
    }

    internal float Opening(int sector, Vector3 radial, Vector3 pivot, float minimum)
    {
        float angle = Solve(probes[sector], radial, pivot, minimum);
        return angle >= 1.5f ? angle : Solve(surfaceProbes[sector], radial, pivot, angle);
    }

    internal float SupportHeight(Vector3 point)
    {
        return Mathf.Max(SupportHeight(point, leftHip, leftAxis, leftLength),
            SupportHeight(point, rightHip, rightAxis, rightLength));
    }

    private float SupportHeight(Vector3 point, Vector3 hip, Vector3 axis, float length)
    {
        float horizontalAxis = axis.x * axis.x + axis.z * axis.z;
        if (horizontalAxis < .05f || (point.x - hip.x) * axis.x + (point.z - hip.z) * axis.z < length * horizontalAxis * .25f)
            return float.NegativeInfinity;
        float height = float.NegativeInfinity;
        for (int i = 0; i < Samples; i++)
        {
            // Exact upper intersection of a vertical ray with a capsule. Use
            // short, overlapping segments to retain the measured leg taper.
            Vector3 start = hip + axis * (length * i / Samples);
            float radius = Mathf.Max(radii[i], radii[Mathf.Min(i + 1, Samples - 1)]);
            float x = point.x - start.x, z = point.z - start.z;
            float horizontal = axis.x * axis.x + axis.z * axis.z;
            float along = horizontal > 1e-6f ? (x * axis.x + z * axis.z) / horizontal : 0;
            float perpendicular = x * x + z * z - along * along * horizontal;
            float available = radius * radius - perpendicular;
            if (available <= 0) continue;
            float peak = horizontal > 1e-6f ? along + axis.y * Mathf.Sqrt(available / horizontal) :
                (axis.y > 0 ? length / Samples : 0);
            Vector3 center = start + axis * Mathf.Clamp(peak, 0, length / Samples);
            x = point.x - center.x; z = point.z - center.z;
            float square = radius * radius - x * x - z * z;
            if (square > 0) height = Mathf.Max(height, center.y + Mathf.Sqrt(square));
        }
        return height;
    }

    private float Solve(GarmentProbeSet points, Vector3 radial, Vector3 pivot, float minimum)
    {
        if (Clear(points, radial, pivot, minimum)) return minimum;
        float low = minimum;
        float high = Mathf.Max(minimum, 1.5f);
        for (int iteration = 0; iteration < 6; iteration++)
        {
            float middle = (low + high) * .5f;
            if (Clear(points, radial, pivot, middle)) high = middle;
            else low = middle;
        }
        return high;
    }

    private bool Clear(GarmentProbeSet points, Vector3 radial, Vector3 pivot, float angle)
    {
        float cosine = Mathf.Cos(angle);
        float sine = Mathf.Sin(angle);
        var left = new GarmentCollisionCapsule(ToProbeSpace(leftHip - pivot, radial, cosine, sine),
            ToProbeSpace(leftAxis, radial, cosine, sine), leftLength, radii, maximumRadius);
        var right = new GarmentCollisionCapsule(ToProbeSpace(rightHip - pivot, radial, cosine, sine),
            ToProbeSpace(rightAxis, radial, cosine, sine), rightLength, radii, maximumRadius);
        return !points.Intersects(left, right);
    }

    private static Vector3 ToProbeSpace(Vector3 value, Vector3 radial, float cosine, float sine)
    {
        float outward = value.x * radial.x + value.z * radial.z;
        return new Vector3(outward * cosine + value.y * sine, value.y * cosine - outward * sine,
            value.x * radial.z - value.z * radial.x);
    }
}
