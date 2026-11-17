using System;
using System.Collections.Generic;
using System.Linq;
using MikuGlider;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal static class LabCorrectionMath
    {
        internal static Matrix4x4 Skin(BoneWeight weight, Matrix4x4[] matrices)
        {
            Matrix4x4 value = Matrix4x4.zero;
            for (int i = 0; i < 16; i++)
                value[i] = matrices[weight.boneIndex0][i] * weight.weight0 + matrices[weight.boneIndex1][i] * weight.weight1 +
                    matrices[weight.boneIndex2][i] * weight.weight2 + matrices[weight.boneIndex3][i] * weight.weight3;
            return value;
        }

        internal static Vector3[] PoseDeltas(Mesh mesh, Transform[] original, Transform[] edited)
        {
            Matrix4x4[] binds = mesh.bindposes;
            Matrix4x4[] before = original.Select((bone, i) => bone.localToWorldMatrix * binds[i]).ToArray();
            Matrix4x4[] after = edited.Select((bone, i) => bone.localToWorldMatrix * binds[i]).ToArray();
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            var deltas = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Matrix4x4 native = Skin(weights[i], before);
                if (Mathf.Abs(native.determinant) < 1e-9f)
                    throw new InvalidOperationException("Singular skin transform at vertex " + i + ". Use a less extreme pose.");
                deltas[i] = native.inverse.MultiplyPoint3x4(Skin(weights[i], after).MultiplyPoint3x4(vertices[i])) - vertices[i];
            }
            return deltas;
        }

        internal static BoneWeight SetWeight(BoneWeight source, int bone, float amount)
        {
            if (bone < 0 || float.IsNaN(amount) || amount < 0 || amount > 1) throw new ArgumentOutOfRangeException();
            var values = new Dictionary<int, float>();
            Add(values, source.boneIndex0, source.weight0);
            Add(values, source.boneIndex1, source.weight1);
            Add(values, source.boneIndex2, source.weight2);
            Add(values, source.boneIndex3, source.weight3);
            values.Remove(bone);
            var remaining = values.OrderByDescending(pair => pair.Value).Take(amount > 0 ? 3 : 4).ToArray();
            float sum = remaining.Sum(pair => pair.Value);
            if (sum <= 1e-8f)
            {
                if (amount < .9999f) throw new InvalidOperationException("Select another bone first; the remaining weight needs an influence.");
                return new BoneWeight { boneIndex0 = bone, weight0 = 1 };
            }
            var result = remaining.Select(pair => new KeyValuePair<int, float>(pair.Key, pair.Value / sum * (1 - amount))).ToList();
            if (amount > 0) result.Add(new KeyValuePair<int, float>(bone, amount));
            result = result.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).ToList();
            while (result.Count < 4) result.Add(new KeyValuePair<int, float>(0, 0));
            return new BoneWeight { boneIndex0 = result[0].Key, weight0 = result[0].Value,
                boneIndex1 = result[1].Key, weight1 = result[1].Value, boneIndex2 = result[2].Key, weight2 = result[2].Value,
                boneIndex3 = result[3].Key, weight3 = result[3].Value };
        }

        private static void Add(Dictionary<int, float> values, int bone, float value)
        {
            if (value <= 0) return;
            values.TryGetValue(bone, out float previous);
            values[bone] = previous + value;
        }

        internal static float Influence(BoneWeight w, int bone) =>
            (w.boneIndex0 == bone ? w.weight0 : 0) + (w.boneIndex1 == bone ? w.weight1 : 0) +
            (w.boneIndex2 == bone ? w.weight2 : 0) + (w.boneIndex3 == bone ? w.weight3 : 0);
    }
}
