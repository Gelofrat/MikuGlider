using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Horse.MikuPhysicsLab
{
    internal sealed class LabCombinationPlayer : IDisposable
    {
        private readonly PlayableGraph graph;
        private readonly AnimationLayerMixerPlayable layers;
        private readonly AnimationMixerPlayable[] mixers = new AnimationMixerPlayable[2];
        private readonly Dictionary<int, AnimationClipPlayable>[] players = { new Dictionary<int, AnimationClipPlayable>(), new Dictionary<int, AnimationClipPlayable>() };
        private readonly Dictionary<int, int>[] ports = { new Dictionary<int, int>(), new Dictionary<int, int>() };
        private readonly CombinationSegment[] stages;
        private readonly float[] starts;
        private readonly LabController lab;

        internal LabCombinationPlayer(LabRig rig, LabController lab, CombinationSegment[] stages)
        {
            this.lab = lab;
            this.stages = stages;
            starts = new float[stages.Length];
            graph = PlayableGraph.Create("Miku gameplay combinations");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            for (int layer = 0; layer < 2; layer++)
            {
                mixers[layer] = AnimationMixerPlayable.Create(graph, stages.Length);
                graph.Connect(mixers[layer], 0, layers, layer);
                layers.SetLayerAdditive((uint)layer, false);
                for (int stage = 0; stage < stages.Length; stage++)
                {
                    int index = Index(stage, layer);
                    if (index < 0 || players[layer].ContainsKey(index)) continue;
                    var playable = AnimationClipPlayable.Create(graph, lab.GetAnimation(index));
                    playable.SetApplyFootIK(false);
                    playable.SetApplyPlayableIK(false);
                    playable.SetSpeed(0);
                    int port = players[layer].Count;
                    players[layer].Add(index, playable);
                    ports[layer].Add(index, port);
                    graph.Connect(playable, 0, mixers[layer], port);
                }
            }
            for (int i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + stages[i - 1].duration;
            var output = AnimationPlayableOutput.Create(graph, "Combination pose", rig.AnimationRoot.GetComponent<Animator>());
            output.SetSourcePlayable(layers);
            graph.Play();
        }

        private int Index(int stage, int layer) => layer == 0 ? stages[stage].movement : stages[stage].overlay;

        internal bool Sample(float time)
        {
            int stage = 0;
            while (stage + 1 < stages.Length && time >= starts[stage + 1]) stage++;
            for (int layer = 0; layer < 2; layer++)
            {
                foreach (int port in ports[layer].Values) mixers[layer].SetInputWeight(port, 0);
                int current = Index(stage, layer);
                int previous = stage > 0 ? Index(stage - 1, layer) : -1;
                float fade = current >= 0 ? lab.Clips[current].fadeDuration : previous >= 0 ? lab.Clips[previous].fadeDuration : 0;
                float alpha = stage == 0 || fade <= 0 ? 1 : Mathf.Clamp01((time - starts[stage]) / fade);
                alpha = alpha < .5f ? 2 * alpha * alpha : 1 - Mathf.Pow(-2 * alpha + 2, 2) / 2;
                if (current == previous) alpha = 1;
                float total = (current >= 0 ? alpha : 0) + (previous >= 0 ? 1 - alpha : 0);
                layers.SetInputWeight(layer, layer == 0 ? 1 : total);
                Apply(layer, stage, current, time, total > 0 ? alpha / total : 0);
                if (previous != current && alpha < 1) Apply(layer, stage - 1, previous, time, total > 0 ? (1 - alpha) / total : 0);
            }
            graph.Evaluate(0);
            return lab.Clips[stages[stage].movement].flight;
        }

        private void Apply(int layer, int stage, int index, float time, float weight)
        {
            if (index < 0 || stage < 0) return;
            while (stage > 0 && Index(stage - 1, layer) == index) stage--;
            ClipEntry entry = lab.Clips[index];
            float position = Mathf.Max(0, time - starts[stage]) * entry.speed;
            float duration = lab.GetAnimation(index).length;
            players[layer][index].SetTime(entry.loop ? Mathf.Repeat(position, duration) : Mathf.Min(position, duration));
            mixers[layer].SetInputWeight(ports[layer][index], weight);
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
