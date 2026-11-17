using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

// Contact belongs to the garment instance. Never translate the waist or the
// native skeleton to clear a leg; move only the cloth that touches it.
internal sealed class GarmentSurfaceClearance : IDisposable
{
    private readonly Vector3[] rest, guide, corrected, output, offsets;
    private readonly BoneWeight[] weights;
    private readonly Matrix4x4[] bindposes, matrices;
    private readonly int[] vertexMap;
    private readonly float[] mobility;
    private readonly Edge[] edges;
    private readonly Transform reference;
    private readonly Transform[] bones;
    private readonly GarmentLegClearance legs;
    private readonly float padding;
    private readonly Stencil[] stencils;
    private readonly bool coat;
    internal Mesh Mesh { get; }

    internal GarmentSurfaceClearance(Mesh source, Transform reference, Transform[] bones,
        GarmentLegClearance legs, float hinge, bool coat)
    {
        this.reference = reference; this.bones = bones; this.legs = legs;
        this.coat = coat;
        padding = coat ? .012f : 0;

        Mesh = UnityEngine.Object.Instantiate(source);
        Mesh.name = source.name + " local contact";
        Mesh.MarkDynamic();
        output = source.vertices;
        bindposes = source.bindposes;
        matrices = new Matrix4x4[bindposes.Length];
        vertexMap = new int[output.Length];
        var unique = new Dictionary<(Vector3, BoneWeight), int>();
        int[] triangles = source.triangles;
        var used = new bool[output.Length];
        foreach (int vertex in triangles) used[vertex] = true;
        var points = new List<Vector3>();
        var bindings = new List<BoneWeight>();
        BoneWeight[] sourceWeights = source.boneWeights;
        for (int i = 0; i < output.Length; i++)
        {
            if (!used[i]) { vertexMap[i] = -1; continue; }
            if (!unique.TryGetValue((output[i], sourceWeights[i]), out int index))
            {
                index = points.Count; unique.Add((output[i], sourceWeights[i]), index);
                points.Add(output[i]); bindings.Add(sourceWeights[i]);
            }
            vertexMap[i] = index;
        }
        rest = points.ToArray(); weights = bindings.ToArray();
        guide = new Vector3[rest.Length];
        corrected = new Vector3[rest.Length]; mobility = new float[rest.Length];
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (Vector3 point in rest) { bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y); }
        const int sectors = 32;
        offsets = new Vector3[(Mathf.CeilToInt((top - bottom) / .04f) + 2) * sectors];
        stencils = new Stencil[rest.Length];
        for (int i = 0; i < rest.Length; i++)
        {
            mobility[i] = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hinge, hinge - .05f, rest[i].y));
            float angle = Mathf.Repeat(Mathf.Atan2(rest[i].x, rest[i].z) / (2f * Mathf.PI), 1f) * sectors;
            float height = (rest[i].y - bottom) / .04f;
            int a = Mathf.FloorToInt(angle), h = Mathf.FloorToInt(height);
            float x = angle - a, y = height - h;
            stencils[i] = new Stencil { A = h * sectors + a, B = h * sectors + (a + 1) % sectors,
                C = (h + 1) * sectors + a, D = (h + 1) * sectors + (a + 1) % sectors,
                Weights = new Vector4((1-x)*(1-y),x*(1-y),(1-x)*y,x*y) };
        }
        var links = new List<Edge>(); var keys = new HashSet<ulong>();
        for (int i = 0; i < triangles.Length; i += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = vertexMap[triangles[i + k]], b = vertexMap[triangles[i + (k + 1) % 3]];
                if (a > b) { int swap = a; a = b; b = swap; }
                if (a != b && mobility[a] + mobility[b] > 0 && keys.Add(((ulong)(uint)a << 32) | (uint)b))
                    links.Add(new Edge { A = a, B = b });
            }
        edges = links.ToArray();
    }

    internal void Apply()
    {
        // Rebuild from the current native pose each frame. No accumulated
        // correction survives a transition, disabled renderer or skin switch.
        Matrix4x4 inverse = reference.worldToLocalMatrix;
        for (int i = 0; i < matrices.Length; i++) matrices[i] = inverse * bones[i].localToWorldMatrix * bindposes[i];
        for (int i = 0; i < rest.Length; i++)
        {
            BoneWeight w = weights[i];
            guide[i] = matrices[w.boneIndex0].MultiplyPoint3x4(rest[i]) * w.weight0 +
                matrices[w.boneIndex1].MultiplyPoint3x4(rest[i]) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(rest[i]) * w.weight2 +
                matrices[w.boneIndex3].MultiplyPoint3x4(rest[i]) * w.weight3;
        }
        // A shared local displacement field keeps pleats and inner/outer
        // surfaces together. Independent projections collapse thick cloth.
        Array.Clear(offsets, 0, offsets.Length);
        for (int iteration = 0; iteration < (coat ? 2 : 4); iteration++)
        {
            for (int j = 0; j < rest.Length; j++)
            {
                int i = iteration % 2 == 0 ? j : rest.Length - 1 - j;
                if (mobility[i] <= 0) continue;
                Vector3 point = guide[i] + Offset(i);
                Vector3 delta = (legs.Project(point, padding) - point) * mobility[i];
                if (delta.sqrMagnitude > 1e-12f) Move(i, delta / stencils[i].Weights.sqrMagnitude);
            }
            if (!coat) foreach (Edge edge in edges)
            {
                int a = edge.A, b = edge.B;
                Vector3 point = (guide[a] + Offset(a) + guide[b] + Offset(b)) * .5f;
                Vector3 delta = legs.Project(point, padding) - point;
                if (delta.sqrMagnitude < 1e-12f) continue;
                Move(a, delta * .5f * mobility[a] / stencils[a].Weights.sqrMagnitude);
                Move(b, delta * .5f * mobility[b] / stencils[b].Weights.sqrMagnitude);
            }
        }
        for (int i = 0; i < rest.Length; i++)
        {
            Vector3 delta = Offset(i);
            corrected[i] = rest[i];
            if (delta.sqrMagnitude < 1e-12f) continue;
            BoneWeight w = weights[i];
            Vector3 x = Axis(w, Vector3.right), y = Axis(w, Vector3.up), z = Axis(w, Vector3.forward);
            float determinant = Vector3.Dot(x, Vector3.Cross(y, z));
            if (Mathf.Abs(determinant) > 1e-6f)
                corrected[i] += new Vector3(Vector3.Dot(delta, Vector3.Cross(y, z)),
                    Vector3.Dot(delta, Vector3.Cross(z, x)), Vector3.Dot(delta, Vector3.Cross(x, y))) / determinant;
        }
        for (int i = 0; i < output.Length; i++)
            if (vertexMap[i] >= 0) output[i] = corrected[vertexMap[i]];
        Mesh.vertices = output;
    }

    private Vector3 Offset(int i)
    {
        Stencil s = stencils[i]; Vector4 w = s.Weights;
        return (offsets[s.A] * w.x + offsets[s.B] * w.y + offsets[s.C] * w.z + offsets[s.D] * w.w) * mobility[i];
    }

    private void Move(int i, Vector3 delta)
    {
        Stencil s = stencils[i]; Vector4 w = s.Weights;
        offsets[s.A] += delta * w.x; offsets[s.B] += delta * w.y;
        offsets[s.C] += delta * w.z; offsets[s.D] += delta * w.w;
    }

    private struct Stencil { internal int A, B, C, D; internal Vector4 Weights; }

    private Vector3 Axis(BoneWeight w, Vector3 axis) => matrices[w.boneIndex0].MultiplyVector(axis) * w.weight0 +
        matrices[w.boneIndex1].MultiplyVector(axis) * w.weight1 + matrices[w.boneIndex2].MultiplyVector(axis) * w.weight2 +
        matrices[w.boneIndex3].MultiplyVector(axis) * w.weight3;

    public void Dispose() { if (Mesh != null) UnityEngine.Object.Destroy(Mesh); }
    private struct Edge { internal int A, B; }
}
