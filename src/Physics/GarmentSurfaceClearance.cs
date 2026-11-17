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
    private readonly int[] activePoints, skinPoints, usedBones, outputVertices;
    private bool deformed;
    private readonly int[] nodeVersions, pointVersions, edgeVersions;
    private int version;
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
        if (!coat) for (int i = 0; i < triangles.Length; i += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = vertexMap[triangles[i + k]], b = vertexMap[triangles[i + (k + 1) % 3]];
                if (a > b) { int swap = a; a = b; b = swap; }
                if (a != b && mobility[a] + mobility[b] > 0 && keys.Add(((ulong)(uint)a << 32) | (uint)b))
                    links.Add(new Edge { A = a, B = b });
            }
        edges = links.ToArray();
        nodeVersions = new int[offsets.Length];
        pointVersions = new int[rest.Length];
        edgeVersions = new int[edges.Length];
        var active = new List<int>(); var needed = new HashSet<int>();
        for (int i = 0; i < rest.Length; i++) if (mobility[i] > 0) { active.Add(i); needed.Add(i); }
        foreach (Edge edge in edges) { needed.Add(edge.A); needed.Add(edge.B); }
        activePoints = active.ToArray();
        var skin = new List<int>(needed); skin.Sort(); skinPoints = skin.ToArray();
        var boneSet = new HashSet<int>();
        foreach (int i in skinPoints)
        {
            BoneWeight w = weights[i];
            if (w.weight0 > 0) boneSet.Add(w.boneIndex0);
            if (w.weight1 > 0) boneSet.Add(w.boneIndex1);
            if (w.weight2 > 0) boneSet.Add(w.boneIndex2);
            if (w.weight3 > 0) boneSet.Add(w.boneIndex3);
        }
        var indices = new List<int>(boneSet); indices.Sort(); usedBones = indices.ToArray();
        indices.Clear();
        for (int i = 0; i < output.Length; i++) if (vertexMap[i] >= 0 && mobility[vertexMap[i]] > 0) indices.Add(i);
        outputVertices = indices.ToArray();
    }

    internal void Apply()
    {
        // Rebuild from the current native pose each frame. No accumulated
        // correction survives a transition, disabled renderer or skin switch.
        Matrix4x4 inverse = reference.worldToLocalMatrix;
        foreach (int i in usedBones) matrices[i] = inverse * bones[i].localToWorldMatrix * bindposes[i];
        foreach (int i in skinPoints)
        {
            Vector3 point = rest[i], value = default;
            BoneWeight w = weights[i];
            Accumulate(ref value, point, w.boneIndex0, w.weight0);
            Accumulate(ref value, point, w.boneIndex1, w.weight1);
            Accumulate(ref value, point, w.boneIndex2, w.weight2);
            Accumulate(ref value, point, w.boneIndex3, w.weight3);
            guide[i] = value;
        }
        // A shared local displacement field keeps pleats and inner/outer
        // surfaces together. Independent projections collapse thick cloth.
        Array.Clear(offsets, 0, offsets.Length);
        Array.Clear(nodeVersions, 0, nodeVersions.Length);
        version = 0;
        bool changed = false;
        for (int iteration = 0; iteration < (coat ? 2 : 4); iteration++)
        {
            bool moved = false;
            for (int j = 0; j < activePoints.Length; j++)
            {
                int i = activePoints[iteration % 2 == 0 ? j : activePoints.Length - 1 - j];
                // Within this frame, unchanged field nodes imply the same contact result.
                int stamp = SampleVersion(i);
                if (iteration > 0 && pointVersions[i] == stamp) continue;
                pointVersions[i] = stamp;
                Vector3 point = guide[i] + Offset(i);
                Vector3 delta = (legs.Project(point, padding) - point) * mobility[i];
                if (delta.sqrMagnitude > 1e-12f)
                {
                    Move(i, delta / stencils[i].Weights.sqrMagnitude);
                    moved = true;
                }
            }
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                Edge edge = edges[edgeIndex];
                int a = edge.A, b = edge.B;
                int stamp = Math.Max(SampleVersion(a), SampleVersion(b));
                if (iteration > 0 && edgeVersions[edgeIndex] == stamp) continue;
                edgeVersions[edgeIndex] = stamp;
                Vector3 point = (guide[a] + Offset(a) + guide[b] + Offset(b)) * .5f;
                Vector3 delta = legs.Project(point, padding) - point;
                if (delta.sqrMagnitude < 1e-12f) continue;
                moved = true;
                Move(a, delta * .5f * mobility[a] / stencils[a].Weights.sqrMagnitude);
                Move(b, delta * .5f * mobility[b] / stencils[b].Weights.sqrMagnitude);
            }
            changed |= moved;
            if (!moved) break;
        }
        if (!changed)
        {
            if (deformed)
            {
                foreach (int vertex in outputVertices) output[vertex] = rest[vertexMap[vertex]];
                Mesh.vertices = output;
                deformed = false;
            }
            return;
        }
        foreach (int i in activePoints)
        {
            Vector3 delta = Offset(i);
            corrected[i] = rest[i];
            if (delta.sqrMagnitude < 1e-12f) continue;
            corrected[i] += Unskin(delta, weights[i]);
        }
        foreach (int i in outputVertices) output[i] = corrected[vertexMap[i]];
        Mesh.vertices = output;
        deformed = true;
    }

    private Vector3 Offset(int i)
    {
        Stencil s = stencils[i]; Vector4 w = s.Weights;
        ref Vector3 a = ref offsets[s.A], b = ref offsets[s.B], c = ref offsets[s.C], d = ref offsets[s.D];
        float amount = mobility[i];
        return new Vector3((a.x * w.x + b.x * w.y + c.x * w.z + d.x * w.w) * amount,
            (a.y * w.x + b.y * w.y + c.y * w.z + d.y * w.w) * amount,
            (a.z * w.x + b.z * w.y + c.z * w.z + d.z * w.w) * amount);
    }

    private int SampleVersion(int i)
    {
        Stencil s = stencils[i];
        return Math.Max(Math.Max(nodeVersions[s.A], nodeVersions[s.B]), Math.Max(nodeVersions[s.C], nodeVersions[s.D]));
    }

    private void Move(int i, Vector3 delta)
    {
        Stencil s = stencils[i]; Vector4 w = s.Weights;
        int stamp = ++version;
        AddOffset(s.A, delta, w.x, stamp); AddOffset(s.B, delta, w.y, stamp);
        AddOffset(s.C, delta, w.z, stamp); AddOffset(s.D, delta, w.w, stamp);
    }

    private void AddOffset(int i, Vector3 delta, float weight, int stamp)
    {
        if (weight == 0) return;
        ref Vector3 value = ref offsets[i];
        value.x += delta.x * weight; value.y += delta.y * weight; value.z += delta.z * weight;
        nodeVersions[i] = stamp;
    }

    private struct Stencil { internal int A, B, C, D; internal Vector4 Weights; }

    private void Accumulate(ref Vector3 value, Vector3 p, int bone, float weight)
    {
        if (weight <= 0) return;
        ref Matrix4x4 m = ref matrices[bone];
        value.x += (m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03) * weight;
        value.y += (m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13) * weight;
        value.z += (m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23) * weight;
    }

    private Vector3 Unskin(Vector3 delta, BoneWeight w)
    {
        // Invert the blended linear transform directly for this displacement.
        ref Matrix4x4 a = ref matrices[w.boneIndex0], b = ref matrices[w.boneIndex1],
            c = ref matrices[w.boneIndex2], d = ref matrices[w.boneIndex3];
        float m00 = a.m00*w.weight0 + b.m00*w.weight1 + c.m00*w.weight2 + d.m00*w.weight3;
        float m01 = a.m01*w.weight0 + b.m01*w.weight1 + c.m01*w.weight2 + d.m01*w.weight3;
        float m02 = a.m02*w.weight0 + b.m02*w.weight1 + c.m02*w.weight2 + d.m02*w.weight3;
        float m10 = a.m10*w.weight0 + b.m10*w.weight1 + c.m10*w.weight2 + d.m10*w.weight3;
        float m11 = a.m11*w.weight0 + b.m11*w.weight1 + c.m11*w.weight2 + d.m11*w.weight3;
        float m12 = a.m12*w.weight0 + b.m12*w.weight1 + c.m12*w.weight2 + d.m12*w.weight3;
        float m20 = a.m20*w.weight0 + b.m20*w.weight1 + c.m20*w.weight2 + d.m20*w.weight3;
        float m21 = a.m21*w.weight0 + b.m21*w.weight1 + c.m21*w.weight2 + d.m21*w.weight3;
        float m22 = a.m22*w.weight0 + b.m22*w.weight1 + c.m22*w.weight2 + d.m22*w.weight3;
        float c00 = m11*m22-m21*m12, c01 = m21*m02-m01*m22, c02 = m01*m12-m11*m02;
        float determinant = m00*c00 + m10*c01 + m20*c02;
        if (Mathf.Abs(determinant) <= 1e-6f) return Vector3.zero;
        return new Vector3((delta.x*c00 + delta.y*c01 + delta.z*c02) / determinant,
            (delta.x*(m12*m20-m22*m10) + delta.y*(m22*m00-m02*m20) + delta.z*(m02*m10-m12*m00)) / determinant,
            (delta.x*(m10*m21-m20*m11) + delta.y*(m20*m01-m00*m21) + delta.z*(m00*m11-m10*m01)) / determinant);
    }

    public void Dispose() { if (Mesh != null) UnityEngine.Object.Destroy(Mesh); }
    private struct Edge { internal int A, B; }
}
