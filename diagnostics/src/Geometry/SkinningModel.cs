using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class SkinningModel
{
    private readonly SkinnedMeshRenderer renderer;
    private readonly BoneWeight[] weights;
    private readonly Matrix4x4[] bindposes;
    private readonly int maximumInfluences;
    private int[] usedVertices;
    private readonly HashSet<int> lowerBones;
    internal Mesh Mesh { get; }
    internal Vector3[] Rest { get; private set; }
    internal int[] Triangles { get; private set; }

    internal SkinningModel(SkinnedMeshRenderer renderer, bool lowerBodyOnly = false, bool legsOnly = false)
    {
        this.renderer = renderer;
        Mesh = renderer.sharedMesh;
        Rest = Mesh.vertices;
        weights = Mesh.boneWeights;
        bindposes = Mesh.bindposes;
        maximumInfluences = renderer.quality == SkinQuality.Auto ? Math.Min(4, (int)QualitySettings.skinWeights) : (int)renderer.quality;
        if (Rest.Length > 250000 || weights.Length != Rest.Length)
            throw new InvalidOperationException("Unexpected skinned-mesh layout.");
        if (!lowerBodyOnly)
        {
            Triangles = Mesh.GetTriangles(0);
            return;
        }
        Transform[] bones = renderer.bones;
        lowerBones = new HashSet<int>();
        for (int index = 0; index < bones.Length; index++)
        {
            string name = bones[index] != null ? bones[index].name : string.Empty;
            if (name.Contains("Thigh") || name.Contains("Knee") || name.Contains("Foot") || name.Contains("Toe") ||
                (!legsOnly && (name.Contains("Pelvis") || name.Contains("Wrist") || name.Contains("Hand") ||
                name.Contains("Elbow") || name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") ||
                name.Contains("Pinky") || name.Contains("Thumb"))))
                lowerBones.Add(index);
        }
        Triangles = Mesh.triangles;
    }

    internal void PrepareLowerBody()
    {
        if (lowerBones == null || usedVertices != null)
            return;
        var selected = new List<int>();
        var vertices = new HashSet<int>();
        for (int index = 0; index < Triangles.Length; index += 3)
        {
            if (!Lower(weights[Triangles[index]], lowerBones) && !Lower(weights[Triangles[index + 1]], lowerBones) &&
                !Lower(weights[Triangles[index + 2]], lowerBones))
                continue;
            for (int corner = 0; corner < 3; corner++)
            {
                selected.Add(Triangles[index + corner]);
                vertices.Add(Triangles[index + corner]);
            }
        }
        Triangles = selected.ToArray();
        usedVertices = new int[vertices.Count];
        vertices.CopyTo(usedVertices);
    }

    internal Matrix4x4[] CaptureMatrices()
    {
        Transform[] bones = renderer.bones;
        if (bones.Length != bindposes.Length)
            throw new InvalidOperationException("The renderer bone layout changed.");
        var matrices = new Matrix4x4[bones.Length];
        for (int index = 0; index < matrices.Length; index++)
            matrices[index] = bones[index].localToWorldMatrix * bindposes[index];
        return matrices;
    }

    internal void RefreshVertices() => Rest = Mesh.vertices;

    internal Vector3[] Pose(Vector3[] buffer = null)
    {
        return Pose(CaptureMatrices(), buffer);
    }

    internal Vector3[] Pose(Matrix4x4[] matrices, Vector3[] buffer = null)
    {
        Vector3[] world = buffer != null && buffer.Length == Rest.Length ? buffer : new Vector3[Rest.Length];
        if (usedVertices == null)
        {
            for (int index = 0; index < world.Length; index++)
                world[index] = PoseVertex(index, matrices);
        }
        else
        {
            foreach (int index in usedVertices)
                world[index] = PoseVertex(index, matrices);
        }
        return world;
    }

    private Vector3 PoseVertex(int index, Matrix4x4[] matrices)
    {
        BoneWeight weight = weights[index];
        Vector3 point = Rest[index];
        if (maximumInfluences == 1)
            return matrices[weight.boneIndex0].MultiplyPoint3x4(point);
        if (maximumInfluences == 2)
        {
            float total = weight.weight0 + weight.weight1;
            return (matrices[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0 +
                matrices[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1) / total;
        }
        return matrices[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0 +
            matrices[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1 +
            matrices[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2 +
            matrices[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
    }

    private static bool Lower(BoneWeight weight, HashSet<int> allowed)
    {
        float total = (allowed.Contains(weight.boneIndex0) ? weight.weight0 : 0f) +
            (allowed.Contains(weight.boneIndex1) ? weight.weight1 : 0f) +
            (allowed.Contains(weight.boneIndex2) ? weight.weight2 : 0f) +
            (allowed.Contains(weight.boneIndex3) ? weight.weight3 : 0f);
        return total > 0.4f;
    }
}
