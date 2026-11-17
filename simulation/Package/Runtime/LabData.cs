using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    [Serializable] public sealed class LabVariant
    {
        public string key;
        public string label;
        public float hipRadius;
        public float thighRadius;
    }

    [Serializable] public sealed class LabCatalog
    {
        public LabVariant[] variants;
    }

    [Serializable] public sealed class RigNode
    {
        public string name;
        public int parent;
        public float[] position;
        public float[] rotation;
        public float[] scale;
    }

    [Serializable] public sealed class RigRenderer
    {
        public int node;
        public string name;
        public int root;
        public int[] bones;
        public int quality;
    }

    [Serializable] public sealed class RigData
    {
        public RigNode[] nodes;
        public RigRenderer[] renderers;
    }

    [Serializable] public sealed class ClipEntry
    {
        public string key;
        public string label;
        public float speed;
        public string resource;
        public string category;
        public string rig;
        public string source;
        public string sourceGuid;
        public string sourceHash;
        public float fadeDuration;
        public bool loop;
        public bool overlay;
        public bool additive;
        public string weaponKey;
        public string baseKey;
        public bool flight;
        public float authoredDuration;
        public int matchedBindings;
        public string[] missingPaths;
        public bool inferredSettings;
        public string[] aliases;
        public CombinationSegment[] segments;
    }

    [Serializable] public sealed class CombinationSegment
    {
        public int movement;
        public int overlay = -1;
        public float duration;
    }

    [Serializable] public sealed class ClipCatalog
    {
        public ClipEntry[] clips;
    }

    [Serializable] public sealed class ReplayEntry
    {
        public string key;
        public string label;
        public int variant;
        public int frames;
        public float duration;
        public float samplesPerSecond;
        public string[] boneNames;
    }

    [Serializable] public sealed class ReplayCatalog
    {
        public ReplayEntry[] tracks;
    }

    internal sealed class ReplayTrack
    {
        internal readonly float[] Times;
        internal readonly Vector3[][] Positions;
        internal readonly Quaternion[][] Rotations;
        internal readonly uint[] Flags;
        internal readonly Quaternion OriginRotation;
        internal readonly Vector3 Origin;
        private bool localPoses;
        internal float Duration => Times[Times.Length - 1];

        internal ReplayTrack(string key)
        {
            var file = Resources.Load<TextAsset>("MikuPhysicsLab/Replays/" + key);
            using (var stream = new BinaryReader(new MemoryStream(file.bytes)))
            {
                int count = stream.ReadInt32();
                int bones = stream.ReadInt32();
                Times = new float[count];
                Flags = new uint[count];
                Positions = new Vector3[count][];
                Rotations = new Quaternion[count][];
                for (int frame = 0; frame < count; frame++)
                {
                    Times[frame] = stream.ReadSingle();
                    Flags[frame] = stream.ReadUInt32();
                    Positions[frame] = new Vector3[bones];
                    Rotations[frame] = new Quaternion[bones];
                    for (int bone = 0; bone < bones; bone++)
                    {
                        Positions[frame][bone] = new Vector3(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
                        Rotations[frame][bone] = new Quaternion(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
                    }
                }
            }
            Origin = Positions[0][0];
            OriginRotation = Quaternion.Inverse(Rotations[0][0]);
        }

        internal uint Sample(float time, Transform[] bones, int[] order, Transform stage)
        {
            if (!localPoses)
            {
                var parents = new int[bones.Length];
                var bridges = new Matrix4x4[bones.Length];
                for (int index = 0; index < bones.Length; index++)
                {
                    Transform parent = bones[index].parent;
                    while (parent != null && Array.IndexOf(bones, parent) < 0) parent = parent.parent;
                    parents[index] = Array.IndexOf(bones, parent);
                    bridges[index] = parent != null
                        ? bones[index].parent.worldToLocalMatrix * parent.localToWorldMatrix
                        : Matrix4x4.identity;
                }
                for (int sample = 0; sample < Times.Length; sample++)
                    for (int position = order.Length - 1; position >= 0; position--)
                    {
                        int index = order[position];
                        int parent = parents[index];
                        if (parent < 0) continue;
                        Quaternion inverse = Quaternion.Inverse(Rotations[sample][parent]);
                        Positions[sample][index] = bridges[index].MultiplyPoint3x4(inverse * (Positions[sample][index] - Positions[sample][parent]));
                        Rotations[sample][index] = bridges[index].rotation * inverse * Rotations[sample][index];
                    }
                localPoses = true;
            }
            time = Mathf.Clamp(time, 0f, Duration);
            int frame = Array.BinarySearch(Times, time);
            if (frame < 0)
                frame = Mathf.Max(0, ~frame - 1);
            int next = Mathf.Min(frame + 1, Times.Length - 1);
            float blend = next == frame ? 0f : Mathf.InverseLerp(Times[frame], Times[next], time);
            Vector3 rootPosition = Vector3.Lerp(Positions[frame][0], Positions[next][0], blend);
            Quaternion rootRotation = Quaternion.Slerp(Rotations[frame][0], Rotations[next][0], blend);
            stage.SetPositionAndRotation(OriginRotation * (rootPosition - Origin), OriginRotation * rootRotation);
            foreach (int index in order)
            {
                if (index >= Positions[frame].Length)
                    continue;
                Vector3 position = Vector3.Lerp(Positions[frame][index], Positions[next][index], blend);
                Quaternion rotation = Quaternion.Slerp(Rotations[frame][index], Rotations[next][index], blend);
                if (index == 0)
                    bones[index].SetPositionAndRotation(OriginRotation * (position - Origin), OriginRotation * rotation);
                else
                    bones[index].SetLocalPositionAndRotation(position, rotation);
            }
            return Flags[frame];
        }
    }

    internal sealed class LabRig
    {
        internal readonly GameObject Root;
        internal readonly Transform[] Nodes;
        internal readonly RigData Data;
        internal readonly Transform[] Bones;
        internal readonly int[] BoneOrder;

        internal LabRig(string resource, Transform parent, int renderer = 0)
        {
            Data = JsonUtility.FromJson<RigData>(Resources.Load<TextAsset>("MikuPhysicsLab/" + resource).text);
            Root = new GameObject(resource);
            Root.transform.SetParent(parent, false);
            Nodes = Data.nodes.Select(n => new GameObject(n.name).transform).ToArray();
            for (int index = 0; index < Nodes.Length; index++)
                Nodes[index].SetParent(Data.nodes[index].parent < 0 ? Root.transform : Nodes[Data.nodes[index].parent], false);
            Restore();
            Bones = Data.renderers[renderer].bones.Select(i => Nodes[i]).ToArray();
            BoneOrder = Enumerable.Range(0, Bones.Length).OrderBy(i => Depth(Bones[i])).ToArray();
            var animator = AnimationRoot.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
        }

        internal void Restore()
        {
            for (int index = 0; index < Nodes.Length; index++)
            {
                RigNode node = Data.nodes[index];
                Nodes[index].localPosition = V(node.position);
                Nodes[index].localRotation = new Quaternion(node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3]);
                Nodes[index].localScale = V(node.scale);
            }
        }

        internal GameObject AnimationRoot => Nodes[Array.FindIndex(Data.nodes, n => n.parent < 0)].gameObject;
        internal static Vector3 V(float[] values) => new Vector3(values[0], values[1], values[2]);
        private static int Depth(Transform value)
        {
            int depth = 0;
            for (; value != null; value = value.parent)
                depth++;
            return depth;
        }
    }
}
