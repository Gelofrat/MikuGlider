using UnityEngine;

namespace MikuGlider;

internal sealed class CharacterBodyBones
{
    internal Transform Pelvis { get; }
    internal Transform Spine { get; }
    internal Transform Chest { get; }
    internal Transform LeftThigh { get; }
    internal Transform LeftKnee { get; }
    internal Transform RightThigh { get; }
    internal Transform RightKnee { get; }
    private readonly SimulationCapsule[] capsules = new SimulationCapsule[3];

    internal CharacterBodyBones(Transform[] bones)
    {
        Pelvis = Find(bones, "joint_Pelvis");
        Spine = Find(bones, "joint_Spine02");
        Chest = Find(bones, "joint_Spine03");
        LeftThigh = Find(bones, "joint_Thigh_L");
        LeftKnee = Find(bones, "joint_Knee_L");
        RightThigh = Find(bones, "joint_Thigh_R");
        RightKnee = Find(bones, "joint_Knee_R");
    }

    internal bool HasLegs => LeftThigh != null && LeftKnee != null && RightThigh != null && RightKnee != null;

    internal SimulationCapsule[] UpdateHairCollisions(float scale)
    {
        if (Pelvis == null || Chest == null || !HasLegs)
            return System.Array.Empty<SimulationCapsule>();

        capsules[0] = new SimulationCapsule(Pelvis.position, Chest.position, 0.145f * scale);
        capsules[1] = new SimulationCapsule(LeftThigh.position, LeftKnee.position, 0.12f * scale);
        capsules[2] = new SimulationCapsule(RightThigh.position, RightKnee.position, 0.12f * scale);
        return capsules;
    }

    internal static float GetScale(Transform transform)
    {
        Vector3 scale = transform.lossyScale;
        return Mathf.Max(0.001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private static Transform Find(Transform[] bones, string name)
    {
        foreach (Transform bone in bones)
        {
            if (bone != null && bone.name == name)
                return bone;
        }

        return null;
    }
}
