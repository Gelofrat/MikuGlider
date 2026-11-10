using System;
using System.IO;
using UnityEngine;

namespace MikuGlider;

internal static class SkinMeshValidator
{
    internal static Mesh RequireMesh(SkinAssets assets, string name, int submeshes = -1)
    {
        Mesh mesh = assets.GetMesh(name);
        if (mesh.vertexCount == 0 || (submeshes > 0 && mesh.subMeshCount != submeshes))
            throw new InvalidDataException("Invalid mesh or material layout: " + name);

        ValidateGeometry(mesh);
        return mesh;
    }

    internal static void ValidateSkinned(Mesh mesh, SkinnedMeshRenderer reference, int materialCount = -1)
    {
        ValidateGeometry(mesh);
        Matrix4x4[] expected = reference.sharedMesh.bindposes;
        Matrix4x4[] actual = mesh.bindposes;
        int expectedMaterials = materialCount < 0 ? reference.sharedMaterials.Length : materialCount;
        if (mesh.vertexCount == 0 || mesh.subMeshCount != expectedMaterials || actual.Length != expected.Length)
            throw new InvalidDataException("Mesh skeleton or material layout does not match Glider: " + mesh.name);

        for (int index = 0; index < actual.Length; index++)
        {
            for (int component = 0; component < 16; component++)
            {
                float value = actual[index][component];
                if (!IsFinite(value) || Math.Abs(value - expected[index][component]) > 0.001f)
                    throw new InvalidDataException("Bind pose differs from Glider at index " + index + ": " + mesh.name);
            }
        }

        BoneWeight[] weights = mesh.boneWeights;
        if (weights.Length != mesh.vertexCount)
            throw new InvalidDataException("Expected four-weight skinning data for " + mesh.name);

        foreach (BoneWeight weight in weights)
        {
            ValidateInfluence(weight.boneIndex0, weight.weight0, actual.Length, mesh.name);
            ValidateInfluence(weight.boneIndex1, weight.weight1, actual.Length, mesh.name);
            ValidateInfluence(weight.boneIndex2, weight.weight2, actual.Length, mesh.name);
            ValidateInfluence(weight.boneIndex3, weight.weight3, actual.Length, mesh.name);
            float sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
            if (Math.Abs(sum - 1f) > 0.01f)
                throw new InvalidDataException("Vertex weights must sum to 1 in " + mesh.name);
        }
    }

    internal static void ValidateVector(Vector3 value)
    {
        if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
            throw new InvalidDataException("Non-finite mesh coordinate or hold transform.");
    }

    private static void ValidateGeometry(Mesh mesh)
    {
        if (!mesh.isReadable)
            throw new InvalidDataException("Enable Read/Write for mesh " + mesh.name);
        if (mesh.subMeshCount == 0 || mesh.normals.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount)
            throw new InvalidDataException("Mesh needs submeshes, normals and UV0: " + mesh.name);

        foreach (Vector3 vertex in mesh.vertices)
            ValidateVector(vertex);
        foreach (Vector3 normal in mesh.normals)
            ValidateVector(normal);
        foreach (Vector2 uv in mesh.uv)
            ValidateVector(new Vector3(uv.x, uv.y, 0f));

        ValidateVector(mesh.bounds.center);
        ValidateVector(mesh.bounds.size);
    }

    private static void ValidateInfluence(int index, float weight, int count, string name)
    {
        if (!IsFinite(weight) || weight < 0f || weight > 1f || (weight > 0f && (index < 0 || index >= count)))
            throw new InvalidDataException("Invalid bone weight in " + name);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
