using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Horse.MikuPhysicsLab
{
    internal sealed class LabMotionPlayer : IDisposable
    {
        private readonly LabRig rig;
        private readonly PlayableGraph graph;
        private readonly AnimationLayerMixerPlayable layers;
        private readonly AnimationMixerPlayable blend;
        private AnimationClipPlayable selected;
        private AnimationClipPlayable baseline;
        private AnimationClip active;
        private AnimationClip baseClip;
        private bool activeOverlay;
        private readonly bool manual;
        private float lastTimeline;
        private bool sampled;
        internal float Coverage => active != null && selected.IsValid() ? Mathf.Clamp01((float)selected.GetTime() / active.length) : 0;

        internal LabMotionPlayer(LabRig rig)
        {
            this.rig = rig;
            manual = LabController.Argument("-labDriver") == "manual";
            graph = PlayableGraph.Create("Miku native animation test");
            graph.SetTimeUpdateMode(manual ? DirectorUpdateMode.Manual : DirectorUpdateMode.GameTime);
            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            blend = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(blend, 0, layers, 0);
            layers.SetInputWeight(0, 1);
            var output = AnimationPlayableOutput.Create(graph, "Character pose", rig.AnimationRoot.GetComponent<Animator>());
            output.SetSourcePlayable(layers);
            graph.Play();
        }

        internal void Sample(AnimationClip clip, AnimationClip idle, float time, float baseTime, float timeline, float fade, bool overlay, float speed, float baseSpeed, bool advancing, bool additive = false)
        {
            layers.SetLayerAdditive(1, additive);
            bool changed = active != clip || baseClip != idle || activeOverlay != overlay;
            if (changed)
            {
                Disconnect();
                rig.Restore();
                active = clip;
                baseClip = idle ?? clip;
                activeOverlay = overlay;
                baseline = Create(baseClip);
                selected = Create(clip);
                graph.Connect(baseline, 0, blend, 0);
                if (overlay)
                    graph.Connect(selected, 0, layers, 1);
                else
                    graph.Connect(selected, 0, blend, 1);
            }
            float advance = advancing ? Time.deltaTime : 0;
            bool seek = manual || changed || !sampled || Mathf.Abs(timeline - lastTimeline - advance) > .001f;
            if (seek)
            {
                baseline.SetTime(baseTime - (manual ? 0 : advance * baseSpeed));
                selected.SetTime(time - (manual ? 0 : advance * speed));
            }
            baseline.SetSpeed(manual || !advancing ? 0 : baseSpeed);
            selected.SetSpeed(manual || !advancing ? 0 : speed);
            sampled = true;
            lastTimeline = timeline;
            if (overlay)
            {
                blend.SetInputWeight(0, 1);
                blend.SetInputWeight(1, 0);
                layers.SetInputWeight(1, fade);
            }
            else
            {
                blend.SetInputWeight(0, 1 - fade);
                blend.SetInputWeight(1, fade);
                layers.SetInputWeight(1, 0);
            }
            if (manual)
                graph.Evaluate(0);
        }

        private AnimationClipPlayable Create(AnimationClip clip)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0);
            return playable;
        }

        private void Disconnect()
        {
            graph.Disconnect(blend, 0);
            graph.Disconnect(blend, 1);
            graph.Disconnect(layers, 1);
            if (selected.IsValid())
                graph.DestroyPlayable(selected);
            if (baseline.IsValid())
                graph.DestroyPlayable(baseline);
        }

        public void Dispose()
        {
            if (graph.IsValid())
                graph.Destroy();
        }
    }
}
