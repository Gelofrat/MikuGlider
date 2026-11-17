using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class TriangleIndex
{
    private sealed class Node
    {
        internal int start;
        internal int count;
        internal int left = -1;
        internal int right = -1;
        internal Vector3 minimum;
        internal Vector3 maximum;
    }

    private readonly int[] triangles;
    private readonly int[] order;
    private readonly List<Node> nodes = new List<Node>();
    private Vector3[] points;
    private readonly int[] logical;

    internal TriangleIndex(Vector3[] rest, int[] triangles, int[] logical = null)
    {
        this.triangles = triangles;
        this.logical = logical;
        points = rest;
        order = new int[triangles.Length / 3];
        for (int index = 0; index < order.Length; index++)
            order[index] = index;
        if (order.Length != 0)
            Build(0, order.Length, 0);
    }

    internal void Update(Vector3[] world)
    {
        points = world;
        if (nodes.Count > 0)
            Refit(0);
    }

    internal bool Intersect(Vector3 start, Vector3 end, int ignoreA, int ignoreB, out Vector3 hit)
    {
        hit = Vector3.zero;
        return nodes.Count > 0 && Search(0, start, end, ignoreA, ignoreB, ref hit);
    }

    private int Build(int start, int count, int depth)
    {
        int result = nodes.Count;
        var node = new Node { start = start, count = count };
        nodes.Add(node);
        if (count > 8)
        {
            int axis = depth % 3;
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) => Center(a, axis).CompareTo(Center(b, axis))));
            int half = count / 2;
            node.left = Build(start, half, depth + 1);
            node.right = Build(start + half, count - half, depth + 1);
        }
        Refit(result);
        return result;
    }

    private float Center(int triangle, int axis)
    {
        int offset = triangle * 3;
        return points[triangles[offset]][axis] + points[triangles[offset + 1]][axis] + points[triangles[offset + 2]][axis];
    }

    private void Refit(int index)
    {
        Node node = nodes[index];
        if (node.left >= 0)
        {
            Refit(node.left);
            Refit(node.right);
            node.minimum = Vector3.Min(nodes[node.left].minimum, nodes[node.right].minimum);
            node.maximum = Vector3.Max(nodes[node.left].maximum, nodes[node.right].maximum);
            return;
        }
        node.minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        node.maximum = -node.minimum;
        for (int item = node.start; item < node.start + node.count; item++)
            for (int corner = 0; corner < 3; corner++)
            {
                Vector3 point = points[triangles[order[item] * 3 + corner]];
                node.minimum = Vector3.Min(node.minimum, point);
                node.maximum = Vector3.Max(node.maximum, point);
            }
    }

    private bool Search(int index, Vector3 start, Vector3 end, int ignoreA, int ignoreB, ref Vector3 hit)
    {
        Node node = nodes[index];
        if (!IntersectsBox(start, end, node.minimum, node.maximum))
            return false;
        if (node.left >= 0)
            return Search(node.left, start, end, ignoreA, ignoreB, ref hit) ||
                Search(node.right, start, end, ignoreA, ignoreB, ref hit);
        for (int item = node.start; item < node.start + node.count; item++)
        {
            int offset = order[item] * 3;
            int a = triangles[offset];
            int b = triangles[offset + 1];
            int c = triangles[offset + 2];
            if (logical != null && (Adjacent(a, ignoreA, ignoreB) || Adjacent(b, ignoreA, ignoreB) || Adjacent(c, ignoreA, ignoreB)))
                continue;
            if (GeometryMath.SegmentTriangle(start, end, points[a], points[b], points[c], 0.0015f, out hit))
                return true;
        }
        return false;
    }

    private bool Adjacent(int vertex, int a, int b)
    {
        return logical[vertex] == a || logical[vertex] == b;
    }

    private static bool IntersectsBox(Vector3 start, Vector3 end, Vector3 minimum, Vector3 maximum)
    {
        Vector3 direction = end - start;
        float enter = 0f;
        float exit = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(direction[axis]) < 1e-9f)
            {
                if (start[axis] < minimum[axis] || start[axis] > maximum[axis])
                    return false;
                continue;
            }
            float a = (minimum[axis] - start[axis]) / direction[axis];
            float b = (maximum[axis] - start[axis]) / direction[axis];
            enter = Math.Max(enter, Math.Min(a, b));
            exit = Math.Min(exit, Math.Max(a, b));
            if (enter > exit)
                return false;
        }
        return true;
    }
}
