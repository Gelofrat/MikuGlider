using UnityEngine;

namespace MikuGlider;

internal sealed class GarmentArmClearance
{
    private const float WristMargin = .060f;
    private const float MaxWristOffset = .090f;
    private const float MaxThumbAllowance = .012f;
    private readonly Transform[] shoulders = new Transform[2];
    private readonly Transform[] wrists = new Transform[2];
    private readonly Transform[] thumbTips = new Transform[2];
    private readonly Quaternion[] original = new Quaternion[2];
    private readonly Quaternion[] applied = new Quaternion[2];
    private readonly Quaternion[] corrections = { Quaternion.identity, Quaternion.identity };
    private readonly bool[] active = new bool[2];
    private readonly GarmentWidthProfile widthProfile;
    private readonly float top;
    private readonly float bottom;
    private bool hasPreviousPose;

    internal GarmentArmClearance(Transform[] bones, Mesh skirt)
    {
        widthProfile = GarmentWidthProfile.ForMesh(skirt);
        top = widthProfile.Top;
        bottom = widthProfile.Bottom;
        for (int side = 0; side < 2; side++)
        {
            string suffix = side == 0 ? "_L" : "_R";
            foreach (Transform bone in bones)
            {
                if (bone.name == "joint_Shoulder" + suffix) shoulders[side] = bone;
                if (bone.name == "joint_Wrist" + suffix) wrists[side] = bone;
                if (bone.name == "joint_Thumb03" + suffix) thumbTips[side] = bone;
            }
        }
    }

    internal void Restore()
    {
        for (int side = 0; side < 2; side++)
        {
            if (active[side] && shoulders[side] != null && Quaternion.Angle(shoulders[side].localRotation, applied[side]) < .001f)
                shoulders[side].localRotation = original[side];
            active[side] = false;
        }
    }

    internal void ResetCorrections()
    {
        Restore();
        corrections[0] = Quaternion.identity;
        corrections[1] = Quaternion.identity;
        hasPreviousPose = false;
    }

    internal bool IsActive => active[0] || active[1];

    internal void Apply(Matrix4x4 referenceToWorld, float deltaTime, float weight = 1)
    {
        // The first visible frame has no previous rendered pose to blend from.
        float blend = hasPreviousPose ? 1 - Mathf.Exp(-deltaTime * 18) : 1;
        hasPreviousPose = true;
        if (weight <= 0 && !IsActive) return;
        Matrix4x4 inverse = referenceToWorld.inverse;
        for (int side = 0; side < 2; side++)
        {
            Transform shoulder = shoulders[side], wrist = wrists[side];
            if (shoulder == null || wrist == null)
                continue;
            Vector3 hand = inverse.MultiplyPoint3x4(wrist.position);
            Vector3 pivot = inverse.MultiplyPoint3x4(shoulder.position);
            float sign = Mathf.Sign(pivot.x);
            float width = widthProfile.AtHeight(hand.y);
            float vertical = Mathf.Clamp01((top + .12f - hand.y) / .10f) * Mathf.Clamp01((hand.y - bottom + .18f) / .10f);
            float lateral = Mathf.Clamp01((Mathf.Abs(hand.x) - .10f) / .10f);
            float depth = Mathf.Clamp01((.30f - Mathf.Abs(hand.z)) / .10f);
            float required = width + WristMargin - sign * hand.x;
            float thumbAllowance = 0;
            if (thumbTips[side] != null)
            {
                float thumbX = inverse.MultiplyPoint3x4(thumbTips[side].position).x;
                float inwardReach = sign * (hand.x - thumbX);
                thumbAllowance = Mathf.Clamp(inwardReach - WristMargin, 0, MaxThumbAllowance);
            }
            float correction = Mathf.Clamp(required, 0, MaxWristOffset + thumbAllowance) * vertical * lateral * depth;
            Vector3 target = hand + Vector3.right * (correction * sign * weight);
            // An inward-pointing thumb can use up to one extra degree of shoulder travel.
            float maxAngle = 9f + thumbAllowance / MaxThumbAllowance;
            Quaternion turn = correction * weight > 0
                ? Quaternion.RotateTowards(Quaternion.identity,
                    Quaternion.FromToRotation(hand - pivot, target - pivot), maxAngle)
                : Quaternion.identity;
            corrections[side] = Quaternion.Slerp(corrections[side], turn, blend);
            active[side] = false;
            if (Quaternion.Angle(Quaternion.identity, corrections[side]) < .001f) continue;
            original[side] = shoulder.localRotation;
            Quaternion referenceRotation = referenceToWorld.rotation;
            shoulder.rotation = referenceRotation * corrections[side] * Quaternion.Inverse(referenceRotation) * shoulder.rotation;
            applied[side] = shoulder.localRotation;
            active[side] = true;
        }
    }
}
