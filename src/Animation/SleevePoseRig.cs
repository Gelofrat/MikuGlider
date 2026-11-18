using System;
using UnityEngine;

namespace MikuGlider;

internal sealed class SleevePoseRig : IDisposable
{
    private const int Stations = 12;
    private readonly Transform[] sourceBones;
    private readonly Matrix4x4[] sourceBind;
    private readonly Transform[,] joints = new Transform[2, Stations];
    private readonly int[] elbows = new int[2];
    private readonly int[] hands = new int[2];
    private readonly Vector3[] starts = new Vector3[2];
    private readonly Vector3[] ends = new Vector3[2];
    private readonly Vector3[] axes = new Vector3[2];
    private readonly Vector3[] normals = new Vector3[2];
    internal Mesh Mesh { get; }
    internal Transform[] Bones { get; }

    internal SleevePoseRig(Mesh source, Transform[] bones, Transform host)
    {
        sourceBones = bones;
        sourceBind = source.bindposes;
        Bones = new Transform[bones.Length + Stations * 2];
        Array.Copy(bones, Bones, bones.Length);
        var binds = new Matrix4x4[Bones.Length];
        Array.Copy(sourceBind, binds, sourceBind.Length);
        Vector3[] vertices = source.vertices;
        BoneWeight[] weights = source.boneWeights;
        for (int side = 0; side < 2; side++)
        {
            string suffix = side == 0 ? "_L" : "_R";
            elbows[side] = Array.FindIndex(bones, bone => bone.name == "joint_Elbow" + suffix);
            hands[side] = Array.FindIndex(bones, bone => bone.name == "joint_Hand" + suffix);
            int wristIndex = Array.FindIndex(bones, bone => bone.name == "joint_Wrist" + suffix);
            if (elbows[side] < 0 || hands[side] < 0 || wristIndex < 0)
                throw new InvalidOperationException("Sleeve pose requires elbow, wrist and hand bones.");
            Vector3 elbow = sourceBind[elbows[side]].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 wrist = sourceBind[wristIndex].inverse.MultiplyPoint3x4(Vector3.zero);
            axes[side] = (wrist - elbow).normalized;
            normals[side] = Vector3.ProjectOnPlane(Vector3.up, axes[side]).normalized;
            starts[side] = wrist - axes[side] * .18f;
            ends[side] = wrist + axes[side] * .10f;
            Quaternion rotation = Quaternion.LookRotation(axes[side], normals[side]);
            int offset = bones.Length + side * Stations;
            for (int station = 0; station < Stations; station++)
            {
                Transform joint = new GameObject("Miku sleeve bend " + side + " " + station).transform;
                joint.SetParent(host, false);
                joints[side, station] = joint;
                Bones[offset + station] = joint;
                binds[offset + station] = Matrix4x4.TRS(Vector3.Lerp(starts[side], ends[side],
                    station / (float)(Stations - 1)), rotation, Vector3.one).inverse;
            }
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                if (Mathf.Sign(vertices[vertex].x) != Mathf.Sign(wrist.x)) continue;
                float along = Vector3.Dot(vertices[vertex] - starts[side], axes[side]);
                if (along <= 0 || along >= .28f) continue;
                float station = along / .28f * (Stations - 1);
                int low = Mathf.Min(Stations - 2, Mathf.FloorToInt(station));
                float fraction = station - low;
                weights[vertex] = BendWeights(weights[vertex], elbows[side], wristIndex,
                    offset + low, fraction);
            }
        }
        Mesh = UnityEngine.Object.Instantiate(source);
        Mesh.name = source.name + " curved cuffs";
        Mesh.bindposes = binds;
        Mesh.boneWeights = weights;
        UpdatePose();
    }

    // Palm and finger weights remain anatomical; cuff smoothing must not bend skin.
    private static BoneWeight BendWeights(BoneWeight source, int elbow, int wrist,
        int joint, float fraction) => SkinWeightBlend.Replace(source, elbow, wrist, -1, joint, fraction);

    internal void UpdatePose()
    {
        for (int side = 0; side < 2; side++)
        {
            Matrix4x4 forearm = sourceBones[elbows[side]].localToWorldMatrix * sourceBind[elbows[side]];
            Matrix4x4 palm = sourceBones[hands[side]].localToWorldMatrix * sourceBind[hands[side]];
            Vector3 first = forearm.MultiplyPoint3x4(starts[side]), last = palm.MultiplyPoint3x4(ends[side]);
            float tangentLength = Vector3.Distance(first, last) / 3;
            Vector3 firstControl = first + forearm.MultiplyVector(axes[side]).normalized * tangentLength;
            Vector3 lastControl = last - palm.MultiplyVector(axes[side]).normalized * tangentLength;
            for (int station = 0; station < Stations; station++)
            {
                float t = station / (float)(Stations - 1), s = 1 - t;
                Vector3 point = s * s * s * first + 3 * s * s * t * firstControl +
                    3 * s * t * t * lastControl + t * t * t * last;
                Vector3 tangent = s * s * (firstControl - first) + 2 * s * t * (lastControl - firstControl) +
                    t * t * (last - lastControl);
                Vector3 normal = Quaternion.Slerp(forearm.rotation, palm.rotation, t) * normals[side];
                joints[side, station].SetPositionAndRotation(point, Quaternion.LookRotation(tangent, normal));
                joints[side, station].localScale = Vector3.one;
            }
        }
    }

    public void Dispose()
    {
        foreach (Transform joint in joints) if (joint != null) UnityEngine.Object.Destroy(joint.gameObject);
        if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
    }
}
