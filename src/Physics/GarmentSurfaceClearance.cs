using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

// Fit the existing pose bones against the final blended surface, after draping.
// Meshes stay immutable; each actor owns its solver and reusable buffers.
internal sealed class GarmentSurfaceClearance
{
    private readonly Probe[] probes;
    private readonly Matrix4x4[] matrices;
    private readonly float[] offsets;
    private readonly float[] lifts;
    private readonly int sectors;

    internal GarmentSurfaceClearance(Mesh mesh, int first, int sectors)
    {
        this.sectors = sectors;
        matrices = new Matrix4x4[first + sectors * 2];
        offsets = new float[sectors * 2];
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        var samples = new List<Probe>();
        var unique = new HashSet<Vector3>();
        int[] triangles = mesh.triangles;
        foreach (int index in triangles)
            if (unique.Add(vertices[index])) Add(samples, vertices[index], weights[index], first);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // An edge can cross a knee even with clear endpoints.
            for (int edge = 0; edge < 3; edge++)
            {
                int a = triangles[i + edge], b = triangles[i + (edge + 1) % 3];
                if (unique.Add((vertices[a] + vertices[b]) * .5f))
                    samples.Add(new Probe(vertices[a], weights[a], vertices[b], weights[b], first));
            }
        }
        probes = samples.FindAll(p => p.Free).ToArray();
        lifts = new float[probes.Length];
    }

    private static void Add(List<Probe> probes, Vector3 point, BoneWeight weight, int first)
        => probes.Add(new Probe(point, weight, point, weight, first));

    internal void Apply(Transform reference, Transform[] bones, Matrix4x4[] bindposes,
        int first, GarmentLegClearance legs)
    {
        Array.Clear(offsets, 0, offsets.Length);
        Matrix4x4 inverse = reference.worldToLocalMatrix;
        for (int i = 0; i < bones.Length; i++) matrices[i] = inverse * bones[i].localToWorldMatrix * bindposes[i];
        for (int i = 0; i < probes.Length; i++)
        {
            Vector3 point = probes[i].Pose(matrices);
            lifts[i] = Mathf.Max(0, legs.SupportHeight(point) + .005f - point.y);
        }
        // Positive lifts cannot invalidate another support constraint.
        for (int i = 0; i < probes.Length; i++) probes[i].Solve(offsets, lifts[i]);
        for (int i = 0; i < sectors * 2; i++)
            for (int j = 0; j < sectors; j++)
            {
                int distance = Math.Abs(i % sectors - j);
                distance = Math.Min(distance, sectors - distance);
                offsets[i] = Mathf.Max(offsets[i], offsets[i / sectors * sectors + j] - distance * .07f);
            }
        for (int i = 0; i < sectors; i++)
        {
            bones[first + i].localPosition += Vector3.up * offsets[i];
            bones[first + sectors + i].localPosition += Quaternion.Inverse(bones[first + i].localRotation) *
                (Vector3.up * (offsets[sectors + i] - offsets[i]));
        }
    }

    private sealed class Probe
    {
        private readonly Vector3 a, b;
        private readonly BoneWeight wa, wb;
        private readonly int[] indices;
        internal readonly bool Free;

        internal Probe(Vector3 a, BoneWeight wa, Vector3 b, BoneWeight wb, int first)
        {
            this.a = a; this.b = b; this.wa = wa; this.wb = wb;
            var values = new Dictionary<int, float>();
            Collect(values, wa, first); Collect(values, wb, first);
            indices = new int[values.Count];
            int i = 0; float total = 0;
            foreach (var pair in values)
            {
                indices[i++] = pair.Key;
                total += pair.Value;
            }
            Free = total > .99f;
        }

        private static void Collect(Dictionary<int, float> values, BoneWeight w, int first)
        {
            Add(values, w.boneIndex0 - first, w.weight0 * .5f);
            Add(values, w.boneIndex1 - first, w.weight1 * .5f);
            Add(values, w.boneIndex2 - first, w.weight2 * .5f);
            Add(values, w.boneIndex3 - first, w.weight3 * .5f);
        }

        private static void Add(Dictionary<int, float> values, int index, float weight)
        { if (index >= 0 && weight > 0) { values.TryGetValue(index, out float old); values[index] = old + weight; } }

        internal Vector3 Pose(Matrix4x4[] m) => (Skin(a, wa, m) + Skin(b, wb, m)) * .5f;
        private static Vector3 Skin(Vector3 p, BoneWeight w, Matrix4x4[] m) =>
            m[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 + m[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1 +
            m[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 + m[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;

        internal void Solve(float[] offsets, float required)
        {
            for (int i = 0; i < indices.Length; i++) offsets[indices[i]] = Mathf.Max(offsets[indices[i]], required / .99f);
        }
    }
}
