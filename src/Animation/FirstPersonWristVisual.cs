using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1200)]
internal sealed class FirstPersonWristVisual : MonoBehaviour
{
    private const int Stations = 9;
    private const float ForearmLength = .12f;
    private const float PalmLength = .045f;
    private SkinnedMeshRenderer surface;
    private Mesh original;
    private Mesh corrected;
    private Transform[] originalBones;
    private readonly Transform[] joints = new Transform[Stations];
    private Matrix4x4 forearmBind;
    private Matrix4x4 palmBind;
    private Vector3 start;
    private Vector3 end;
    private Vector3 axis;
    private Vector3 up;
    private Quaternion restRotation;

    internal static void Attach(SkinnedMeshRenderer renderer, Mesh expected)
    {
        if (renderer == null) return;
        var current = renderer.GetComponent<FirstPersonWristVisual>();
        if (current != null && current.original == expected && expected != null)
        {
            renderer.sharedMesh = current.corrected;
            current.enabled = true;
            current.UpdatePose();
            return;
        }
        if (current != null) current.Release();
        if (expected == null || renderer.sharedMesh != expected) return;
        var visual = current ?? renderer.gameObject.AddComponent<FirstPersonWristVisual>();
        visual.Initialize(renderer, expected);
    }

    private void Initialize(SkinnedMeshRenderer renderer, Mesh source)
    {
        surface = renderer;
        original = source;
        originalBones = renderer.bones;
        Matrix4x4[] binds = source.bindposes;
        if (binds.Length < 4 || originalBones.Length != binds.Length)
            throw new InvalidOperationException("First-person wrist rig does not match its mesh.");
        forearmBind = binds[1];
        palmBind = binds[3];
        Vector3 wrist = binds[2].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 elbow = binds[1].inverse.MultiplyPoint3x4(Vector3.zero);
        axis = (wrist - elbow).normalized;
        up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        restRotation = Quaternion.LookRotation(axis, up);
        start = wrist - axis * ForearmLength;
        end = wrist + axis * PalmLength;
        var extended = new Transform[binds.Length + Stations];
        var poses = new Matrix4x4[extended.Length];
        Array.Copy(originalBones, extended, binds.Length);
        Array.Copy(binds, poses, binds.Length);
        for (int i = 0; i < Stations; i++)
        {
            joints[i] = new GameObject("Miku wrist transition " + i).transform;
            joints[i].SetParent(renderer.transform, false);
            extended[binds.Length + i] = joints[i];
            poses[binds.Length + i] = Matrix4x4.TRS(Vector3.Lerp(start, end, i / (float)(Stations - 1)), restRotation, Vector3.one).inverse;
        }
        corrected = Instantiate(source);
        corrected.name = source.name + " wrist transition";
        corrected.bindposes = poses;
        Vector3[] vertices = source.vertices;
        BoneWeight[] weights = source.boneWeights;
        for (int i = 0; i < weights.Length; i++)
        {
            float distance = Vector3.Dot(vertices[i] - wrist, axis);
            float radius = (vertices[i] - wrist - axis * distance).magnitude;
            if (distance <= -ForearmLength || distance >= PalmLength || radius > .065f) continue;
            float station = (distance + ForearmLength) / (ForearmLength + PalmLength) * (Stations - 1);
            int low = Mathf.Min(Stations - 2, Mathf.FloorToInt(station));
            weights[i] = ReplaceWristWeights(weights[i], binds.Length + low, station - low);
            float support = Mathf.SmoothStep(0, 1, (distance + ForearmLength) / .045f) *
                Mathf.SmoothStep(0, 1, (PalmLength - distance) / .025f);
            if (radius > .00001f && radius < .027f)
                vertices[i] += (vertices[i] - wrist - axis * distance).normalized * ((.027f - radius) * support);
        }
        corrected.vertices = vertices;
        corrected.boneWeights = weights;
        renderer.bones = extended;
        renderer.sharedMesh = corrected;
        enabled = true;
        UpdatePose();
    }

    private static BoneWeight ReplaceWristWeights(BoneWeight source, int joint, float fraction)
    {
        int[] indices = { source.boneIndex0, source.boneIndex1, source.boneIndex2, source.boneIndex3 };
        float[] values = { source.weight0, source.weight1, source.weight2, source.weight3 };
        var remaining = new List<KeyValuePair<int, float>>(6);
        float amount = 0;
        for (int i = 0; i < 4; i++)
            if (indices[i] >= 1 && indices[i] <= 3) amount += values[i];
            else if (values[i] > 0) remaining.Add(new KeyValuePair<int, float>(indices[i], values[i]));
        if (amount <= 0) return source;
        remaining.Add(new KeyValuePair<int, float>(joint, amount * (1 - fraction)));
        remaining.Add(new KeyValuePair<int, float>(joint + 1, amount * fraction));
        remaining.Sort((a, b) => b.Value.CompareTo(a.Value));
        while (remaining.Count < 4) remaining.Add(new KeyValuePair<int, float>(0, 0));
        float total = remaining[0].Value + remaining[1].Value + remaining[2].Value + remaining[3].Value;
        return new BoneWeight
        {
            boneIndex0 = remaining[0].Key, weight0 = remaining[0].Value / total,
            boneIndex1 = remaining[1].Key, weight1 = remaining[1].Value / total,
            boneIndex2 = remaining[2].Key, weight2 = remaining[2].Value / total,
            boneIndex3 = remaining[3].Key, weight3 = remaining[3].Value / total
        };
    }

    private void LateUpdate()
    {
        if (surface == null || surface.sharedMesh != corrected) { Release(); return; }
        UpdatePose();
    }

    internal void UpdatePose()
    {
        if (originalBones == null || originalBones[1] == null || originalBones[3] == null) return;
        Matrix4x4 a = originalBones[1].localToWorldMatrix * forearmBind;
        Matrix4x4 b = originalBones[3].localToWorldMatrix * palmBind;
        Vector3 p0 = a.MultiplyPoint3x4(start), p3 = b.MultiplyPoint3x4(end);
        float length = (end - start).magnitude;
        Vector3 p1 = p0 + a.MultiplyVector(axis) * (length / 3);
        Vector3 p2 = p3 - b.MultiplyVector(axis) * (length / 3);
        for (int i = 0; i < Stations; i++)
        {
            float t = i / (float)(Stations - 1), s = 1 - t;
            Vector3 position = s * s * s * p0 + 3 * s * s * t * p1 + 3 * s * t * t * p2 + t * t * t * p3;
            Vector3 tangent = s * s * (p1 - p0) + 2 * s * t * (p2 - p1) + t * t * (p3 - p2);
            Vector3 normal = Quaternion.Slerp(a.rotation, b.rotation, t) * up;
            joints[i].SetPositionAndRotation(position, Quaternion.LookRotation(tangent, normal));
            joints[i].localScale = Vector3.one;
        }
    }

    private void Release()
    {
        if (surface != null && originalBones != null)
        {
            if (surface.sharedMesh == corrected) surface.sharedMesh = original;
            surface.bones = originalBones;
        }
        foreach (Transform joint in joints) if (joint != null) Destroy(joint.gameObject);
        if (corrected != null) Destroy(corrected);
        original = null;
        corrected = null;
        originalBones = null;
        enabled = false;
    }

    private void OnDestroy() => Release();
}
