using System;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1200)]
internal sealed class FirstPersonWristVisual : MonoBehaviour
{
    private SkinnedMeshRenderer surface;
    private Mesh original;
    private Mesh corrected;
    private Transform[] originalBones;
    private const int Stations = 9;
    private const float ForearmBlendLength = .09f;
    private const float PalmBlendLength = .035f;
    private readonly Transform[] joints = new Transform[Stations];
    private Matrix4x4 forearmBind;
    private Matrix4x4 palmBind;
    private Vector3 axis;
    private Vector3 restElbow;
    private Vector3 restStart;
    private Vector3 restEnd;
    private Quaternion forearmRestRotation;

    internal static void Attach(SkinnedMeshRenderer renderer, Mesh expected)
    {
        if (renderer == null) return;
        if (expected != null && !expected.name.EndsWith(".arm-left", StringComparison.Ordinal) &&
            !expected.name.EndsWith(".arm-right", StringComparison.Ordinal)) expected = null;
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
            throw new InvalidOperationException("First-person arm rig does not match its mesh.");
        forearmBind = binds[1];
        palmBind = binds[3];
        Vector3 restWrist = binds[2].inverse.MultiplyPoint3x4(Vector3.zero);
        restElbow = forearmBind.inverse.MultiplyPoint3x4(Vector3.zero);
        forearmRestRotation = forearmBind.inverse.rotation;
        axis = (restWrist - restElbow).normalized;
        restStart = restWrist - axis * ForearmBlendLength;
        restEnd = restWrist + axis * PalmBlendLength;
        var extended = new Transform[binds.Length + joints.Length];
        var poses = new Matrix4x4[extended.Length];
        Array.Copy(originalBones, extended, binds.Length);
        Array.Copy(binds, poses, binds.Length);
        for (int i = 0; i < joints.Length; i++)
        {
            joints[i] = new GameObject("Miku wrist transition " + i).transform;
            joints[i].SetParent(renderer.transform, false);
            extended[binds.Length + i] = joints[i];
            poses[binds.Length + i] = forearmBind;
        }
        BoneWeight[] weights = source.boneWeights;
        Vector3[] vertices = source.vertices;
        float length = ForearmBlendLength + PalmBlendLength;
        for (int i = 0; i < weights.Length; i++)
        {
            BoneWeight weight = weights[i];
            float along = Mathf.Clamp01(Vector3.Dot(vertices[i] - restStart, axis) / length);
            float station = along * (Stations - 1);
            int low = Mathf.Min(Stations - 2, Mathf.FloorToInt(station));
            float fraction = station - low;
            int retained = RetainedInfluence(weight.boneIndex0, weight.weight0)
                + RetainedInfluence(weight.boneIndex1, weight.weight1)
                + RetainedInfluence(weight.boneIndex2, weight.weight2)
                + RetainedInfluence(weight.boneIndex3, weight.weight3);
            if (retained > 2) fraction = Mathf.Round(fraction);
            weights[i] = SkinWeightBlend.Replace(weight, 1, 2, 3, binds.Length + low, fraction);
        }
        corrected = Instantiate(source);
        corrected.name = source.name + " wrist transition";
        corrected.bindposes = poses;
        corrected.boneWeights = weights;
        renderer.bones = extended;
        renderer.sharedMesh = corrected;
        enabled = true;
        UpdatePose();
    }

    private static int RetainedInfluence(int bone, float weight) =>
        weight > 0 && (bone < 1 || bone > 3) ? 1 : 0;

    private void LateUpdate()
    {
        if (surface == null || surface.sharedMesh != corrected) { Release(); return; }
        UpdatePose();
    }

    internal void UpdatePose()
    {
        if (originalBones == null || originalBones[0] == null || originalBones[1] == null ||
            originalBones[2] == null || originalBones[3] == null) return;
        Matrix4x4 forearm = originalBones[1].localToWorldMatrix * forearmBind;
        Matrix4x4 palm = originalBones[3].localToWorldMatrix * palmBind;
        Vector3 startPoint = forearm.MultiplyPoint3x4(restStart);
        Vector3 endPoint = palm.MultiplyPoint3x4(restEnd);
        float length = ForearmBlendLength + PalmBlendLength;
        Vector3 startTangent = forearm.MultiplyVector(axis) * length;
        Vector3 endTangent = palm.MultiplyVector(axis) * length;
        Quaternion start = forearm.rotation;
        Quaternion end = palm.rotation;
        Vector3 scale = originalBones[1].lossyScale;
        Vector3 parentScale = surface.transform.lossyScale;
        Vector3 localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        for (int i = 0; i < Stations; i++)
        {
            float t = i / (float)(Stations - 1);
            float square = t * t;
            float cube = square * t;
            Vector3 center = (2f * cube - 3f * square + 1f) * startPoint
                + (cube - 2f * square + t) * startTangent
                + (-2f * cube + 3f * square) * endPoint
                + (cube - square) * endTangent;
            Quaternion rotation = Quaternion.Slerp(start, end, t);
            Vector3 restCenter = Vector3.Lerp(restStart, restEnd, t);
            Vector3 position = center + rotation * Vector3.Scale(restElbow - restCenter, scale);
            joints[i].SetPositionAndRotation(position, rotation * forearmRestRotation);
            joints[i].localScale = localScale;
        }
    }

    private void Release()
    {
        if (surface != null && originalBones != null)
        {
            if (surface.sharedMesh == corrected) surface.sharedMesh = original;
            surface.bones = originalBones;
        }
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null) Destroy(joints[i].gameObject);
            joints[i] = null;
        }
        if (corrected != null) Destroy(corrected);
        original = null;
        corrected = null;
        originalBones = null;
        enabled = false;
    }

    private void OnDestroy() => Release();
}
