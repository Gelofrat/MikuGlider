using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class PointIndex
{
    private readonly Vector3[] points;
    private readonly int[] order;
    private readonly int[] left;
    private readonly int[] right;
    private readonly int root;
    private readonly IComparer<int>[] comparers;

    internal PointIndex(Vector3[] points)
    {
        this.points = points;
        order = new int[points.Length];
        left = new int[points.Length];
        right = new int[points.Length];
        comparers = new IComparer<int>[]
        {
            Comparer<int>.Create((a, b) => points[a].x.CompareTo(points[b].x)),
            Comparer<int>.Create((a, b) => points[a].y.CompareTo(points[b].y)),
            Comparer<int>.Create((a, b) => points[a].z.CompareTo(points[b].z))
        };
        for (int index = 0; index < points.Length; index++)
            order[index] = index;
        root = Build(0, order.Length, 0);
    }

    internal float DistanceSquared(Vector3 point)
    {
        float distance = float.PositiveInfinity;
        int nearest = -1;
        Search(root, point, 0, ref distance, ref nearest);
        return distance;
    }

    internal int Nearest(Vector3 point)
    {
        float distance = float.PositiveInfinity;
        int nearest = -1;
        Search(root, point, 0, ref distance, ref nearest);
        return nearest;
    }

    private int Build(int start, int count, int depth)
    {
        if (count == 0)
            return -1;
        int axis = depth % 3;
        if (count > 1)
            Array.Sort(order, start, count, comparers[axis]);
        int middle = start + count / 2;
        int node = order[middle];
        left[node] = Build(start, middle - start, depth + 1);
        right[node] = Build(middle + 1, start + count - middle - 1, depth + 1);
        return node;
    }

    private void Search(int node, Vector3 point, int depth, ref float distance, ref int nearest)
    {
        if (node < 0 || distance == 0f)
            return;
        float candidate = (point - points[node]).sqrMagnitude;
        if (candidate < distance)
        {
            distance = candidate;
            nearest = node;
        }
        int axis = depth % 3;
        float delta = point[axis] - points[node][axis];
        Search(delta < 0f ? left[node] : right[node], point, depth + 1, ref distance, ref nearest);
        if (delta * delta < distance)
            Search(delta < 0f ? right[node] : left[node], point, depth + 1, ref distance, ref nearest);
    }
}
