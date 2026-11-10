using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MikuGlider;

internal sealed class MeshData
{
    internal string Name { get; }
    internal Matrix4x4[] BindPoses { get; }
    internal Vector3[] Vertices { get; }
    internal Vector3[] Normals { get; }
    internal Vector2[] UV { get; }
    internal BoneWeight[] Weights { get; }
    internal int[][] Submeshes { get; }

    internal MeshData(string name, Matrix4x4[] bindPoses, Vector3[] vertices, Vector3[] normals,
        Vector2[] uv, BoneWeight[] weights, int[][] submeshes)
    {
        Name = name;
        BindPoses = bindPoses;
        Vertices = vertices;
        Normals = normals;
        UV = uv;
        Weights = weights;
        Submeshes = submeshes;
    }
}

internal static class MeshResourceReader
{
    internal const int MaximumBytes = 128 * 1024 * 1024;
    private const int MaximumMeshes = 64;
    private const int MaximumVerticesPerMesh = 250000;
    private const int MaximumTotalVertices = 1000000;
    private const int MaximumIndicesPerMesh = 3000000;
    private const int MaximumTotalIndices = 6000000;
    private const int MaximumSubmeshes = 16;
    private const int MaximumBindPoses = 256;

    internal static IReadOnlyList<MeshData> Read(Stream stream)
    {
        using (var reader = new AssetBinaryReader(stream))
        {
            reader.ExpectHeader("MIKUMSH1");
            int meshCount = reader.ReadCount(1, MaximumMeshes, "mesh count");
            var meshes = new List<MeshData>(meshCount);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalVertices = 0;
            int totalIndices = 0;

            for (int index = 0; index < meshCount; index++)
            {
                string name = reader.ReadName();
                if (!names.Add(name))
                    throw new InvalidDataException("Duplicate embedded mesh: " + name);

                MeshData mesh = ReadMesh(reader, name, ref totalVertices, ref totalIndices);
                meshes.Add(mesh);
            }

            reader.RequireEnd();
            return meshes;
        }
    }

    private static MeshData ReadMesh(AssetBinaryReader reader, string name, ref int totalVertices, ref int totalIndices)
    {
        int vertexCount = reader.ReadCount(1, MaximumVerticesPerMesh, "vertex count");
        int submeshCount = reader.ReadCount(1, MaximumSubmeshes, "submesh count");
        int bindPoseCount = reader.ReadCount(0, MaximumBindPoses, "bind pose count");
        totalVertices = checked(totalVertices + vertexCount);
        if (totalVertices > MaximumTotalVertices)
            throw new InvalidDataException("The embedded model exceeds the total vertex limit.");

        reader.RequireRemaining(bindPoseCount * 64L + vertexCount * 64L + submeshCount * 4L);
        var bindPoses = new Matrix4x4[bindPoseCount];
        for (int index = 0; index < bindPoses.Length; index++)
            bindPoses[index] = reader.ReadMatrix();

        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        var weights = bindPoseCount > 0 ? new BoneWeight[vertexCount] : null;
        for (int index = 0; index < vertexCount; index++)
        {
            vertices[index] = reader.ReadVector3();
            normals[index] = reader.ReadVector3();
            uv[index] = reader.ReadVector2();
            BoneWeight weight = ReadWeight(reader);
            ValidateWeight(weight, bindPoseCount, name);
            if (weights != null)
                weights[index] = weight;
        }

        int[][] submeshes = ReadSubmeshes(reader, submeshCount, vertexCount, name, ref totalIndices);
        return new MeshData(name, bindPoses, vertices, normals, uv, weights, submeshes);
    }

    private static BoneWeight ReadWeight(AssetBinaryReader reader)
    {
        return new BoneWeight
        {
            weight0 = reader.ReadFinite(),
            weight1 = reader.ReadFinite(),
            weight2 = reader.ReadFinite(),
            weight3 = reader.ReadFinite(),
            boneIndex0 = reader.ReadInt32(),
            boneIndex1 = reader.ReadInt32(),
            boneIndex2 = reader.ReadInt32(),
            boneIndex3 = reader.ReadInt32()
        };
    }

    private static void ValidateWeight(BoneWeight weight, int bindPoseCount, string name)
    {
        ValidateInfluence(weight.boneIndex0, weight.weight0, bindPoseCount, name);
        ValidateInfluence(weight.boneIndex1, weight.weight1, bindPoseCount, name);
        ValidateInfluence(weight.boneIndex2, weight.weight2, bindPoseCount, name);
        ValidateInfluence(weight.boneIndex3, weight.weight3, bindPoseCount, name);
        float sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
        if ((bindPoseCount > 0 && Math.Abs(sum - 1f) > 0.01f) || (bindPoseCount == 0 && sum != 0f))
            throw new InvalidDataException("Invalid vertex weight sum in mesh " + name + ".");
    }

    private static void ValidateInfluence(int index, float weight, int bindPoseCount, string name)
    {
        if (weight < 0f || weight > 1f || index < 0 ||
            (bindPoseCount > 0 && index >= bindPoseCount) || (bindPoseCount == 0 && weight != 0f))
            throw new InvalidDataException("Invalid bone index or weight in mesh " + name + ".");
    }

    private static int[][] ReadSubmeshes(AssetBinaryReader reader, int count, int vertexCount,
        string name, ref int totalIndices)
    {
        var submeshes = new int[count][];
        int meshIndices = 0;
        for (int submesh = 0; submesh < count; submesh++)
        {
            int indexCount = reader.ReadCount(0, MaximumIndicesPerMesh, "triangle index count");
            meshIndices = checked(meshIndices + indexCount);
            totalIndices = checked(totalIndices + indexCount);
            if (indexCount % 3 != 0 || meshIndices > MaximumIndicesPerMesh || totalIndices > MaximumTotalIndices)
                throw new InvalidDataException("Invalid triangle count in mesh " + name + ".");

            reader.RequireRemaining(indexCount * 4L);
            var indices = new int[indexCount];
            for (int index = 0; index < indices.Length; index++)
            {
                int vertex = reader.ReadInt32();
                if (vertex < 0 || vertex >= vertexCount)
                    throw new InvalidDataException("Triangle index is outside mesh " + name + ".");

                indices[index] = vertex;
            }

            submeshes[submesh] = indices;
        }

        if (meshIndices == 0)
            throw new InvalidDataException("Embedded mesh has no triangles: " + name);

        return submeshes;
    }
}
