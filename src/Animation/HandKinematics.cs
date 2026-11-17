using UnityEngine;

namespace MikuGlider;

internal static class HandKinematics
{
    internal static Quaternion Retarget(HandJoint joint, Quaternion source) =>
        joint.rotation * joint.axisMap * Quaternion.Inverse(joint.sourceRotation) * source * Quaternion.Inverse(joint.axisMap);

    internal static float Curl(Quaternion rest, Quaternion closed, Quaternion current, Vector3 axis)
    {
        float reference = SignedTwist(Quaternion.Inverse(rest)*closed, axis);
        if (Mathf.Abs(reference) < 1) return 0;
        float angle = SignedTwist(Quaternion.Inverse(rest)*current, axis);
        angle = reference + Mathf.DeltaAngle(reference, angle);
        return Mathf.Clamp01(angle/reference);
    }

    // Closed-form, no translation/scale edits. Unreachable targets are projected
    // onto the arm's reach sphere; shoulder and segment lengths stay fixed.
    internal static void SolveArm(Transform upper, Transform lower, Transform wrist, Vector3 target, Vector3 hint)
    {
        Vector3 root = upper.position, elbow = lower.position, end = wrist.position;
        float a = Vector3.Distance(root, elbow), b = Vector3.Distance(elbow, end);
        Vector3 direction = target - root;
        if (a < 1e-6f || b < 1e-6f || direction.sqrMagnitude < 1e-12f) return;
        float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(a - b) + 1e-6f, a + b - 1e-6f);
        direction.Normalize();
        Vector3 bend = Vector3.ProjectOnPlane(hint - root, direction);
        if (bend.sqrMagnitude < 1e-10f) bend = Vector3.ProjectOnPlane(elbow - root, direction);
        if (bend.sqrMagnitude < 1e-10f)
            bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < .9f ? Vector3.up : Vector3.right);
        bend.Normalize();
        float along = (a*a - b*b + distance*distance) / (2*distance);
        Vector3 joint = root + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a*a - along*along));
        upper.rotation = Quaternion.FromToRotation(elbow - root, joint - root) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(wrist.position - lower.position,
            root + direction*distance - lower.position) * lower.rotation;
    }

    // Scalar hinge CCD around a fitted pose. Absolute accumulated angle limits
    // prevent repeated iterations or frames from creeping beyond the envelope.
    internal static void SolveFinger(Transform[] joints, FingerContact contact, Vector3 target,
        Quaternion[] seed, Vector3[] axes, float weight)
    {
        Transform tip = joints[contact.tip];
        for (int iteration = 0; iteration < 4; iteration++)
            for (int i = contact.tip; i >= contact.root; i--)
            {
                Transform joint = joints[i];
                Vector3 axis = joint.TransformDirection(axes[i]);
                Vector3 from = Vector3.ProjectOnPlane(tip.TransformPoint(contact.localPoint) - joint.position, axis);
                Vector3 to = Vector3.ProjectOnPlane(target - joint.position, axis);
                if (from.sqrMagnitude < 1e-12f || to.sqrMagnitude < 1e-12f) continue;
                Quaternion relative = Quaternion.Inverse(seed[i]) * joint.localRotation;
                float current = SignedTwist(relative, axes[i]);
                float angle = Mathf.Clamp(current + Vector3.SignedAngle(from, to, axis) * weight,
                    -contact.maxAngle * weight, contact.maxAngle * weight);
                joint.localRotation = seed[i] * Quaternion.AngleAxis(angle, axes[i]);
            }
    }

    internal static float SignedTwist(Quaternion rotation, Vector3 axis)
    {
        float projection = rotation.x*axis.x + rotation.y*axis.y + rotation.z*axis.z;
        return Mathf.DeltaAngle(0, 2*Mathf.Atan2(projection, rotation.w)*Mathf.Rad2Deg);
    }
}
