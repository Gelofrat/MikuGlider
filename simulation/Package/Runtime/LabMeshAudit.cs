using System;
using System.Collections.Generic;
using MikuGlider;
using MikuGlider.Diagnostics;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    [Serializable] public sealed class LabMeshAuditResult
    {
        public string variant;
        public int meshes;
        public int vertices;
        public int triangles;
        public int invalidValues;
        public int invalidWeights;
        public int invalidIndices;
        public bool passed;
    }

    internal static class LabMeshAudit
    {
        internal static LabMeshAuditResult Run(LabVariant variant)
        {
            var result = new LabMeshAuditResult { variant = variant.key };
            using (var assets = new SkinAssets(variant.key))
            {
                foreach (string name in new[] { "body", "body-hat", "arm-left", "arm-right", "skirt", "hair-l", "hair-r", "coat-flaps", "sleeves" })
                {
                    if (!assets.TryGetMesh(name, out Mesh mesh))
                        continue;
                    result.meshes++;
                    Vector3[] vertices = mesh.vertices;
                    BoneWeight[] weights = mesh.boneWeights;
                    Matrix4x4[] bindposes = mesh.bindposes;
                    result.vertices += vertices.Length;
                    result.triangles += mesh.triangles.Length / 3;
                    foreach (Vector3 vertex in vertices)
                        if (!GeometryMath.Finite(vertex))
                            result.invalidValues++;
                    foreach (Vector3 normal in mesh.normals)
                        if (!GeometryMath.Finite(normal))
                            result.invalidValues++;
                    foreach (int index in mesh.triangles)
                        if (index < 0 || index >= vertices.Length)
                            result.invalidIndices++;
                    if (weights.Length != vertices.Length || bindposes.Length == 0)
                    {
                        result.invalidWeights++;
                        continue;
                    }
                    foreach (BoneWeight weight in weights)
                    {
                        float sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
                        if (!float.IsFinite(sum) || Mathf.Abs(sum - 1f) > .002f ||
                            !Valid(weight.boneIndex0, weight.weight0, bindposes.Length) || !Valid(weight.boneIndex1, weight.weight1, bindposes.Length) ||
                            !Valid(weight.boneIndex2, weight.weight2, bindposes.Length) || !Valid(weight.boneIndex3, weight.weight3, bindposes.Length))
                            result.invalidWeights++;
                    }
                    foreach (Matrix4x4 bind in bindposes)
                        for (int cell = 0; cell < 16; cell++)
                            if (!float.IsFinite(bind[cell]))
                                result.invalidValues++;
                }
            }
            result.passed = result.meshes >= 7 && result.invalidIndices == 0 && result.invalidWeights == 0 && result.invalidValues == 0;
            return result;
        }

        private static bool Valid(int bone, float weight, int count) => weight >= 0 && (weight == 0 || (bone >= 0 && bone < count));
    }
}
