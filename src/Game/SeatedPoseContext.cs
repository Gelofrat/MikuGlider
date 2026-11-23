using System;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class SeatedPoseContext
{
    private AnimationClip clip;
    private bool sitting;

    // Sample this renderer's animation before SeatedArmRig applies the prepared rotations.
    internal float PrepareFrame(Player owner, SkinnedMeshRenderer body, SeatedArmPlayback playback)
    {
        if (owner == null || !owner.IsAlive || owner.Animator == null || GliderEquipment.FindWingsuit(owner)?.IsFlying == true)
        {
            playback?.Clear();
            return 0;
        }
        var animation = owner.Animator;
        var self = animation.SelfThirdPersonAnimator;
        var state = self != null && self.Rig != null && self.Rig.SkinRenderers != null &&
            Array.IndexOf(self.Rig.SkinRenderers, body) >= 0 ? self.CurrentState : animation.GetCurrentState(0);
        return PrepareState(state, playback);
    }

    internal float PrepareState(Animancer.AnimancerState state, SeatedArmPlayback playback)
    {
        if (playback != null)
        {
            var layer = state?.Layer;
            if (layer == null || layer.Weight <= 0)
            {
                playback.Clear();
                return 0;
            }
            playback.BeginFrame(state.Clip);
            for (int i = 0; i < layer.ChildCount; i++)
            {
                var child = layer.GetChild(i);
                float weight = child.Weight * layer.Weight;
                if (weight > 0 && child.Clip != null)
                    playback.SampleClip(child.Clip, child.NormalizedTime, weight);
            }
            return playback.FinishFrame(state.Weight, layer.Weight);
        }
        if (state == null || state.Clip == null) return 0;
        if (clip != state.Clip)
        {
            clip = state.Clip;
            // "Glider Sitting" is the lowering transition before "Glider Sit Idle".
            // Contact must already be active while the hand approaches the lap.
            sitting = SeatedArmProfile.IsSeatedClip(clip.name);
        }
        return sitting ? Mathf.Clamp01(state.Weight) : 0;
    }
}
