using System;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class ParticleSpace
{
    private int[][] anchors;
    private int[] pinned;
    private bool locked;
    internal int Selected { get; private set; }
    internal float Error { get; private set; }
    internal bool Reliable => anchors != null && pinned.Length >= 3 && Error <= 0.003f;

    internal bool Resolve(Vector3[] raw, Vector3[] reference, float[] distances, Matrix4x4[] spaces, int[] meshToParticle = null)
    {
        int before = Selected;
        if (anchors == null || anchors.Length != spaces.Length)
        {
            var indices = new System.Collections.Generic.List<int>();
            for (int index = 0; index < distances.Length; index++)
                if (distances[index] == 0f && GeometryMath.Finite(raw[index]))
                    indices.Add(index);
            pinned = indices.ToArray();
            anchors = new int[spaces.Length][];
            var tree = meshToParticle == null ? new PointIndex(reference) : null;
            int[] source = null;
            if (meshToParticle != null)
            {
                source = new int[raw.Length];
                for (int index = 0; index < source.Length; index++)
                    source[index] = -1;
                for (int vertex = 0; vertex < meshToParticle.Length; vertex++)
                {
                    int particle = meshToParticle[vertex];
                    if (particle >= 0 && particle < source.Length && source[particle] < 0)
                        source[particle] = vertex;
                }
            }
            for (int space = 0; space < spaces.Length; space++)
            {
                anchors[space] = new int[pinned.Length];
                for (int index = 0; index < pinned.Length; index++)
                    anchors[space][index] = source != null ? source[pinned[index]] :
                        tree.Nearest(spaces[space].MultiplyPoint3x4(raw[pinned[index]]));
            }
        }

        float best = float.PositiveInfinity;
        Error = float.MaxValue;
        for (int space = 0; space < spaces.Length; space++)
        {
            if (locked && space != Selected)
                continue;
            float sum = 0f;
            float maximum = 0f;
            for (int index = 0; index < pinned.Length; index++)
            {
                int vertex = anchors[space][index];
                if (vertex < 0 || vertex >= reference.Length)
                {
                    sum = maximum = float.MaxValue;
                    break;
                }
                float squared = (spaces[space].MultiplyPoint3x4(raw[pinned[index]]) - reference[vertex]).sqrMagnitude;
                if (float.IsNaN(squared) || float.IsInfinity(squared))
                    squared = float.MaxValue;
                sum += squared;
                maximum = Math.Max(maximum, squared);
            }
            if (sum < best)
            {
                best = sum;
                Error = (float)Math.Sqrt(maximum);
                Selected = space;
            }
        }
        if (locked && Error > .003f)
        {
            locked = false;
            Resolve(raw, reference, distances, spaces, meshToParticle);
            return before != Selected;
        }
        if (pinned.Length >= 3 && Error <= 0.002f)
            locked = true;
        return before != Selected;
    }

    internal static Matrix4x4[] Candidates(SkinnedMeshRenderer renderer)
    {
        Transform transform = renderer.transform;
        Transform root = renderer.rootBone != null ? renderer.rootBone : transform;
        return new[]
        {
            transform.localToWorldMatrix,
            Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one),
            root.localToWorldMatrix,
            Matrix4x4.TRS(root.position, root.rotation, Vector3.one),
            Matrix4x4.identity,
            Matrix4x4.TRS(transform.position, Quaternion.identity, Vector3.one)
        };
    }

    internal static string Name(int index)
    {
        return new[] { "renderer_local", "renderer_unscaled", "root_local", "root_unscaled", "world", "translation_only" }[index];
    }
}
