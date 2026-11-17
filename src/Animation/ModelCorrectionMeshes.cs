using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MikuGlider;

// Mesh-wide preparation is separate from renderer-specific skeleton binding.
internal static class ModelCorrectionMeshes
{
    private static readonly ConditionalWeakTable<Mesh, AppliedMesh> Applied = new ConditionalWeakTable<Mesh, AppliedMesh>();
    private static readonly Action<Mesh, string, float, Vector3[], Vector3[], Vector3[]> AddShape =
        (Action<Mesh, string, float, Vector3[], Vector3[], Vector3[]>)Delegate.CreateDelegate(
            typeof(Action<Mesh, string, float, Vector3[], Vector3[], Vector3[]>),
            typeof(Mesh).GetMethod("AddBlendShapeFrame", new[] { typeof(string), typeof(float), typeof(Vector3[]), typeof(Vector3[]), typeof(Vector3[]) }));
    internal sealed class AppliedMesh
    {
        internal MeshCorrection Correction;
        internal BoneWeight[] OriginalWeights;
        internal int[] Shapes;
    }

    internal static AppliedMesh GetApplied(Mesh mesh) => mesh != null && Applied.TryGetValue(mesh, out var state) ? state : null;

    private static readonly ConditionalWeakTable<Mesh, ConditionalWeakTable<MeshCorrection, object>> Rejected =
        new ConditionalWeakTable<Mesh, ConditionalWeakTable<MeshCorrection, object>>();

    internal static bool Validate(Mesh mesh, MeshCorrection correction)
    {
        if (GetApplied(mesh) != null) return true;
        var failures = Rejected.GetValue(mesh, _ => new ConditionalWeakTable<MeshCorrection, object>());
        if (failures.TryGetValue(correction, out _)) return false;
        try { ModelCorrections.Validate(mesh, correction); return true; }
        catch (InvalidDataException error)
        {
            failures.Add(correction, new object());
            Debug.LogWarning("Miku Glider skipped incompatible correction asset for " + mesh.name + ": " + error.Message);
            return false;
        }
    }

    internal static AppliedMesh Prepare(Mesh mesh, MeshCorrection correction)
    {
        if (!Applied.TryGetValue(mesh, out var applied))
        {
            applied = new AppliedMesh { Correction = correction, OriginalWeights = mesh.boneWeights, Shapes = new int[correction.poses.Length] };
            var weights = (BoneWeight[])applied.OriginalWeights.Clone();
            foreach (VertexWeightEdit edit in correction.weights) weights[edit.vertex] = edit.value;
            for (int i = 0; i < correction.poses.Length; i++)
            {
                PoseShapeEdit pose = correction.poses[i];
                var deltas = new Vector3[mesh.vertexCount];
                for (int v = 0; v < pose.vertices.Length; v++) deltas[pose.vertices[v]] = pose.deltas[v];
                Vector3[] basis = mesh.vertices;
                var points = (Vector3[])basis.Clone();
                for (int v = 0; v < points.Length; v++) points[v] += deltas[v];
                Vector3[] normalDeltas = ModelPoseCorrections.CalculateNormalDeltas(basis, points, mesh.normals, mesh.triangles);
                applied.Shapes[i] = mesh.blendShapeCount;
                AddShape(mesh, "MikuManual:" + pose.name, 100, deltas, normalDeltas, null);
            }
            mesh.boneWeights = weights;
            Applied.Add(mesh, applied);
        }
        return applied;
    }
}
