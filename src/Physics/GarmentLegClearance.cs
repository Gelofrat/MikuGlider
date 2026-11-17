using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal sealed class GarmentLegClearance
{
    private const int Samples = 6;
    private readonly float[] radii = new float[Samples];
    private Vector3 leftHip;
    private Vector3 leftAxis;
    private Vector3 rightHip;
    private Vector3 rightAxis;
    private float leftLength;
    private float rightLength;

    internal GarmentLegClearance(Mesh bodyMesh, Transform[] bones)
    {
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

    internal Vector3 Project(Vector3 point, float padding)
    {
        point = Project(point, leftHip, leftAxis, leftLength, padding);
        return Project(point, rightHip, rightAxis, rightLength, padding);
    }

    private Vector3 Project(Vector3 point, Vector3 hip, Vector3 axis, float length, float padding)
    {
        if (length < .001f) return point;
        float along = Vector3.Dot(point - hip, axis);
        // The hip joint is inside the pelvis, not a round cap on the visible
        // thigh. Fade its proxy in below the waist instead of inflating it.
        float proximal = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .35f, along / length));
        if (proximal <= 0) return point;
        float distance = Mathf.Clamp(along, 0, length);
        float sample = Mathf.Clamp(distance / length * Samples - .5f, 0, Samples - 1);
        int first = Mathf.FloorToInt(sample);
        float radius = (Mathf.Lerp(radii[first], radii[Mathf.Min(first + 1, Samples - 1)], sample - first) + padding) * proximal;
        Vector3 center = hip + axis * distance;
        Vector3 offset = point - center;
        float square = offset.sqrMagnitude;
        if (square >= radius * radius) return point;
        Vector3 normal = square > 1e-10f ? offset / Mathf.Sqrt(square) : Vector3.up;
        return center + normal * radius;
    }
}
