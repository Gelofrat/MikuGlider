using System;
using UnityEngine;

namespace MikuGlider;

// A render-only arm chain. Animation, weapon anchors and subsequent frames
// always retain the game's pose; contact never writes to their transforms.
internal sealed class SeatedArmRig : IDisposable
{
    private readonly SkinnedMeshRenderer renderer;
    private readonly Transform[] source, copies;
    private readonly int[] order;
    private readonly SeatedHandClearance contact;
    internal Transform[] Bones { get; }

    internal SeatedArmRig(SkinnedMeshRenderer renderer, Transform[] source, Transform[] garmentBones, Mesh skirt, Mesh coat)
    {
        this.renderer = renderer;
        this.source = source;
        copies = new Transform[source.Length];
        Bones = (Transform[])source.Clone();
        for (int i = 0; i < source.Length; i++)
        {
            bool arm = source[i].name == "joint_Shoulder_L" || source[i].name == "joint_Shoulder_R";
            for (Transform p = source[i].parent; p != null && !arm; p = p.parent)
                arm = p.name == "joint_Shoulder_L" || p.name == "joint_Shoulder_R";
            if (!arm) continue;
            copies[i] = Bones[i] = new GameObject(source[i].name).transform;
            copies[i].gameObject.hideFlags = HideFlags.DontSave;
        }
        for (int i = 0; i < source.Length; i++) if (copies[i] != null)
        {
            int parent = Array.IndexOf(source, source[i].parent);
            copies[i].SetParent(parent >= 0 && copies[parent] != null ? copies[parent] : source[i].parent, false);
        }
        var indices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < copies.Length; i++) if (copies[i] != null) indices.Add(i);
        indices.Sort((a, b) => Depth(source[a]).CompareTo(Depth(source[b])));
        order = indices.ToArray();
        CopyPose(source);
        contact = new SeatedHandClearance(Bones, garmentBones, skirt, coat, true);
    }

    private void CopyPose(Transform[] input)
    {
        foreach (int i in order)
        {
            if (copies[i] == null || input[i] == null) continue;
            copies[i].SetPositionAndRotation(input[i].position, input[i].rotation);
            Vector3 scale = input[i].lossyScale, parent = copies[i].parent.lossyScale;
            copies[i].localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, scale.z / parent.z);
        }
    }

    private static int Depth(Transform bone)
    {
        int depth = 0;
        for (Transform parent = bone.parent; parent != null; parent = parent.parent) depth++;
        return depth;
    }

    internal bool Matches(Transform[] bones) => RendererBoneBinding.SameBones(source, bones);

    internal void Apply(Transform reference, float weight, float deltaTime)
    {
        CopyPose(RendererBoneBinding.BeforeSeated(renderer, source));
        contact.Apply(reference, weight, deltaTime);
        if (weight > 0) RendererBoneBinding.For(renderer).SetSeated(Bones);
        else RendererBoneBinding.ReleaseSeated(renderer);
    }

    internal void Reset()
    {
        contact.Apply(null, 0, 0);
        RendererBoneBinding.ReleaseSeated(renderer);
        CopyPose(source);
    }

    public void Dispose()
    {
        RendererBoneBinding.ReleaseSeated(renderer);
        foreach (Transform copy in copies) if (copy != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(copy.gameObject);
            else UnityEngine.Object.DestroyImmediate(copy.gameObject);
        }
    }
}
