using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal sealed class SeatedArmPlayback
{
    private enum ClipKind { Other, GenericSeat, Enter, Loop, Idle, Exit }
    private readonly Dictionary<AnimationClip, ClipKind> clips = new Dictionary<AnimationClip, ClipKind>();
    private readonly SeatedArmProfile profile;
    private readonly Transform[] arms;
    private readonly Transform idleBone;
    private readonly Quaternion[] pose;
    private float poseWeight, idleWeight, exitAmount;
    private float genericWeight;
    private ClipKind destination;
    internal bool HasPose => poseWeight > 0 || idleWeight > 0;

    private SeatedArmPlayback(SeatedArmProfile profile, Transform[] arms, Transform idleBone)
    {
        this.profile = profile;
        this.arms = arms;
        this.idleBone = idleBone;
        pose = new Quaternion[arms.Length];
    }

    internal static SeatedArmPlayback Bind(SeatedArmProfile profile, Transform[] copies)
    {
        if (profile == null) return null;
        var byName = new Dictionary<string, Transform>();
        foreach (var bone in copies)
        {
            if (bone == null) continue;
            if (byName.ContainsKey(bone.name)) return null;
            byName.Add(bone.name, bone);
        }
        var arms = new Transform[profile.Bones.Length];
        for (int i = 0; i < arms.Length; i++)
            if (!byName.TryGetValue(profile.Bones[i], out arms[i])) return null;
        return byName.TryGetValue(profile.IdleBone, out var idle) ? new SeatedArmPlayback(profile, arms, idle) : null;
    }

    private ClipKind Kind(AnimationClip clip)
    {
        if (clip == null) return ClipKind.Other;
        if (clips.TryGetValue(clip, out var kind)) return kind;
        string name = clip.name;
        if (name == SeatedArmProfile.EnterClip) kind = ClipKind.Enter;
        else if (name == SeatedArmProfile.LoopClip) kind = ClipKind.Loop;
        else if (name == profile.IdleClip) kind = ClipKind.Idle;
        else if (name == profile.ExitClip) kind = ClipKind.Exit;
        else if (SeatedArmProfile.IsSeatedClip(name)) kind = ClipKind.GenericSeat;
        clips.Add(clip, kind);
        return kind;
    }

    internal void BeginFrame(AnimationClip current)
    {
        Clear();
        genericWeight = 0;
        destination = Kind(current);
    }

    internal void SampleClip(AnimationClip clip, float normalizedTime, float weight)
    {
        if (weight <= 0) return;
        switch (Kind(clip))
        {
            case ClipKind.Enter:
                Add(profile.Enter, Mathf.Clamp01(normalizedTime), weight);
                break;
            case ClipKind.Loop:
                Add(profile.Loop, Mathf.Repeat(normalizedTime, 1), weight);
                break;
            case ClipKind.Idle:
                // The closed thumb belongs to the outgoing idle during entry.
                if (destination == ClipKind.Enter) idleWeight += weight;
                break;
            case ClipKind.GenericSeat:
                genericWeight += weight;
                break;
        }
    }

    internal float FinishFrame(float currentWeight, float layerWeight)
    {
        if (destination == ClipKind.Exit && poseWeight > 0 && currentWeight > 0)
        {
            // The quadratic crossfade carries both hands across the lap.
            // Sample the fitted shoulder arc at the actual progress of this pair.
            float total = poseWeight + currentWeight * layerWeight;
            float outgoing = Mathf.Clamp01(poseWeight / total);
            float half = Mathf.Sqrt(.5f * Mathf.Min(outgoing, 1 - outgoing));
            exitAmount = Mathf.Clamp01(total) * Mathf.Sin(Mathf.PI * half);
        }
        float seated = poseWeight > 0 ? Mathf.Clamp01(poseWeight + genericWeight)
            : destination == ClipKind.GenericSeat ? Mathf.Clamp01(currentWeight) : 0;
        if (genericWeight > 0) Clear();
        return seated;
    }

    private void Add(SeatedArmProfile.Track track, float phase, float weight)
    {
        poseWeight += weight;
        track.Sample(phase, weight / poseWeight, pose);
    }

    internal void Apply()
    {
        if (poseWeight > 0)
            for (int i = 0; i < arms.Length; i++)
                arms[i].localRotation *= Quaternion.Slerp(Quaternion.identity, pose[i], poseWeight);
        if (idleWeight > 0)
            idleBone.localRotation *= Quaternion.Slerp(Quaternion.identity, profile.IdleRotation, idleWeight);
        if (exitAmount > 0)
            for (int i = 0; i < profile.ExitBones.Length; i++)
                arms[profile.ExitBones[i]].localRotation *= Quaternion.Slerp(Quaternion.identity, profile.ExitRotations[i], exitAmount);
    }

    internal void Clear() { poseWeight = 0; idleWeight = 0; exitAmount = 0; }
}
