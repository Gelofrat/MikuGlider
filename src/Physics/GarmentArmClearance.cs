using UnityEngine;

namespace MikuGlider;

internal sealed class GarmentArmClearance
{
    private readonly Transform[] shoulders = new Transform[2];
    private readonly Transform[] wrists = new Transform[2];
    private readonly Quaternion[] original = new Quaternion[2];
    private readonly Quaternion[] applied = new Quaternion[2];
    private readonly Quaternion[] corrections = { Quaternion.identity, Quaternion.identity };
    private readonly bool[] active = new bool[2];
    private readonly float[] widths = new float[128];
    private readonly float top;
    private readonly float bottom;

    internal GarmentArmClearance(Transform[] bones, Vector3[] skirt)
    {
        top = float.NegativeInfinity;
        bottom = float.PositiveInfinity;
        foreach (Vector3 point in skirt)
        {
            top = Mathf.Max(top, point.y);
            bottom = Mathf.Min(bottom, point.y);
        }
        for (int row = 0; row < widths.Length; row++)
        {
            float height = Mathf.Lerp(bottom, top, row / (float)(widths.Length - 1));
            float nearest = float.PositiveInfinity;
            foreach (Vector3 point in skirt)
            {
                float distance = Mathf.Abs(point.y - height);
                if (distance > nearest + .001f) continue;
                if (distance < nearest - .001f) widths[row] = 0;
                nearest = distance;
                widths[row] = Mathf.Max(widths[row], Mathf.Abs(point.x));
            }
        }
        for (int side = 0; side < 2; side++)
        {
            string suffix = side == 0 ? "_L" : "_R";
            foreach (Transform bone in bones)
            {
                if (bone.name == "joint_Shoulder" + suffix) shoulders[side] = bone;
                if (bone.name == "joint_Wrist" + suffix) wrists[side] = bone;
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
    }

    internal void Apply(Matrix4x4 referenceToWorld, float deltaTime)
    {
        Matrix4x4 inverse = referenceToWorld.inverse;
        for (int side = 0; side < 2; side++)
        {
            Transform shoulder = shoulders[side], wrist = wrists[side];
            if (shoulder == null || wrist == null)
                continue;
            Vector3 hand = inverse.MultiplyPoint3x4(wrist.position);
            Vector3 pivot = inverse.MultiplyPoint3x4(shoulder.position);
            float sign = Mathf.Sign(pivot.x);
            int row = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(bottom, top, hand.y) * (widths.Length - 1)), 0, widths.Length - 1);
            float width = widths[row];
            float vertical = Mathf.Clamp01((top + .12f - hand.y) / .10f) * Mathf.Clamp01((hand.y - bottom + .18f) / .10f);
            float lateral = Mathf.Clamp01((Mathf.Abs(hand.x) - .10f) / .10f);
            float depth = Mathf.Clamp01((.30f - Mathf.Abs(hand.z)) / .10f);
            float correction = Mathf.Clamp(width + .060f - sign * hand.x, 0, .090f) * vertical * lateral * depth;
            Vector3 target = hand + Vector3.right * (correction * sign);
            Quaternion turn = Quaternion.RotateTowards(Quaternion.identity,
                Quaternion.FromToRotation(hand - pivot, target - pivot), 9f);
            corrections[side] = Quaternion.Slerp(corrections[side], turn, 1 - Mathf.Exp(-deltaTime * 18));
            if (Quaternion.Angle(Quaternion.identity, corrections[side]) < .001f)
                continue;
            original[side] = shoulder.localRotation;
            Quaternion referenceRotation = referenceToWorld.rotation;
            shoulder.rotation = referenceRotation * corrections[side] * Quaternion.Inverse(referenceRotation) * shoulder.rotation;
            applied[side] = shoulder.localRotation;
            active[side] = true;
        }
    }
}
