using System;
using UnityEngine;

namespace MikuGlider;

internal sealed class InventoryWaistPose
{
    private readonly InventoryWaistPoseDefinition fit;
    private readonly Transform shoulder, elbow, wrist, middleTip;
    private readonly Quaternion shoulderTurn, elbowTurn, wristTurn;

    private InventoryWaistPose(InventoryWaistPoseDefinition fit, Transform shoulder, Transform elbow,
        Transform wrist, Transform middleTip)
    {
        this.fit = fit;
        this.shoulder = shoulder;
        this.elbow = elbow;
        this.wrist = wrist;
        this.middleTip = middleTip;
        shoulderTurn = Rotation(fit.shoulderDegrees);
        elbowTurn = Rotation(fit.elbowDegrees);
        wristTurn = Rotation(fit.wristDegrees);
    }

    internal static InventoryWaistPose Bind(InventoryWaistPoseDefinition fit, Transform[] copies)
    {
        if (fit == null) return null;
        Transform Find(string name) => Array.Find(copies, bone => bone != null && bone.name == name);
        Transform shoulder = Find("joint_Shoulder_R"), elbow = Find("joint_Elbow_R");
        Transform wrist = Find("joint_Wrist_R"), tip = Find("joint_Middle04_R");
        return shoulder != null && elbow != null && wrist != null && tip != null
            ? new InventoryWaistPose(fit, shoulder, elbow, wrist, tip) : null;
    }

    private static Quaternion Rotation(Vector3 degrees) =>
        degrees == Vector3.zero ? Quaternion.identity : Quaternion.AngleAxis(degrees.magnitude, degrees.normalized);

    internal bool Apply(Transform reference)
    {
        // Sample the fresh pose in pelvis space so rotating the preview or pausing time preserves contact.
        float distance = Mathf.Max(Vector3.Distance(reference.InverseTransformPoint(wrist.position), fit.wristPosition),
            Vector3.Distance(reference.InverseTransformPoint(middleTip.position), fit.middleTipPosition));
        float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.025f, .085f, distance));
        if (weight <= 0) return false;
        shoulder.localRotation *= Quaternion.SlerpUnclamped(Quaternion.identity, shoulderTurn, weight);
        elbow.localRotation *= Quaternion.SlerpUnclamped(Quaternion.identity, elbowTurn, weight);
        wrist.localRotation *= Quaternion.SlerpUnclamped(Quaternion.identity, wristTurn, weight);
        return true;
    }
}
