using System;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class SeatedPoseContext
{
    private AnimationClip clip;
    private bool sitting;

    internal float Weight(Player owner, SkinnedMeshRenderer body)
    {
        if (owner == null || !owner.IsAlive || owner.Animator == null || GliderEquipment.FindWingsuit(owner)?.IsFlying == true)
            return 0;
        var animation = owner.Animator;
        var self = animation.SelfThirdPersonAnimator;
        var state = self != null && self.Rig != null && self.Rig.SkinRenderers != null &&
            Array.IndexOf(self.Rig.SkinRenderers, body) >= 0 ? self.CurrentState : animation.GetCurrentState(0);
        if (state == null || state.Clip == null) return 0;
        if (clip != state.Clip)
        {
            clip = state.Clip;
            string name = clip.name;
            sitting = name.StartsWith("Sit ", StringComparison.Ordinal) || name.StartsWith("Glider Sit ", StringComparison.Ordinal);
        }
        return sitting ? Mathf.Clamp01(state.Weight) : 0;
    }
}
