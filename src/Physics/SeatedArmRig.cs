using System;
using System.Collections.Generic;
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
    private readonly GarmentArmClearance standingContact;
    private readonly InventoryWaistPose waistPose;
    internal SeatedArmPlayback Playback { get; }
    internal Transform[] Bones { get; }

    internal SeatedArmRig(SkinnedMeshRenderer renderer, Transform[] source, Transform[] garmentBones,
        Mesh skirt, Mesh coat, Mesh handMesh, SkinModelDefinition model, SeatedArmProfile profile,
        InventoryWaistPoseDefinition waist)
    {
        this.renderer = renderer;
        this.source = source;
        copies = new Transform[source.Length];
        Bones = (Transform[])source.Clone();
        try
        {
            order = CreateArmCopies();
            CopyPose(source);
            Playback = SeatedArmPlayback.Bind(profile, copies);
            waistPose = InventoryWaistPose.Bind(waist, copies);
            if (model.standingArmClearance) standingContact = new GarmentArmClearance(Bones, skirt);
            contact = new SeatedHandClearance(Bones, garmentBones, skirt, coat,
                model.preserveSeatedHandHeading, handMesh ?? renderer.sharedMesh, renderer.sharedMesh);
        }
        catch
        {
            DestroyCopies();
            throw;
        }
    }

    private int[] CreateArmCopies()
    {
        var indices = new List<int>();
        for (int i = 0; i < source.Length; i++)
        {
            if (!IsArmBone(source[i])) continue;
            copies[i] = Bones[i] = new GameObject(source[i].name).transform;
            copies[i].gameObject.hideFlags = HideFlags.DontSave;
            indices.Add(i);
        }
        foreach (int i in indices)
        {
            int parent = Array.IndexOf(source, source[i].parent);
            copies[i].SetParent(parent >= 0 && copies[parent] != null ? copies[parent] : source[i].parent, false);
        }
        indices.Sort((a, b) => Depth(source[a]).CompareTo(Depth(source[b])));
        return indices.ToArray();
    }

    private static bool IsArmBone(Transform bone)
    {
        for (; bone != null; bone = bone.parent)
            if (bone.name == "joint_Shoulder_L" || bone.name == "joint_Shoulder_R")
                return true;
        return false;
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

    internal void BindSleeves(Mesh sourceMesh, SleevePoseRig sleeves) => contact.BindSleeves(Bones, sourceMesh, sleeves);

    internal void Apply(Transform reference, float weight, float deltaTime, float standingWeight = 0)
    {
        CopyPose(RendererBoneBinding.BeforeSeated(renderer, source));
        standingContact?.Apply(reference.localToWorldMatrix, deltaTime, standingWeight);
        bool hasAuthoredPose = Playback?.HasPose == true;
        if (hasAuthoredPose)
        {
            contact.ResetContact();
            Playback.Apply();
        }
        else
        {
            contact.Apply(reference, weight, deltaTime);
        }
        bool handOnWaist = waistPose?.Apply(reference) == true;
        if (weight > 0 || hasAuthoredPose || standingContact?.IsActive == true || handOnWaist)
            RendererBoneBinding.For(renderer).SetSeated(Bones);
        else
            RendererBoneBinding.ReleaseSeated(renderer);
    }

    internal void Reset()
    {
        Playback?.Clear();
        standingContact?.ResetCorrections();
        contact.ResetContact();
        RendererBoneBinding.ReleaseSeated(renderer);
        CopyPose(source);
    }

    public void Dispose()
    {
        RendererBoneBinding.ReleaseSeated(renderer);
        DestroyCopies();
    }

    private void DestroyCopies()
    {
        foreach (Transform copy in copies)
        {
            if (copy == null) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(copy.gameObject);
            else UnityEngine.Object.DestroyImmediate(copy.gameObject);
        }
    }
}
