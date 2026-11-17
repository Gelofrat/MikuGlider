using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class SurfaceGeometry
{
    private readonly TriangleIndex bodyIndex;
    private readonly TriangleIndex selfIndex;
    private readonly int[] logical;
    private readonly int[] edges;
    private readonly int[] skirtTriangles;
    private readonly int exampleLimit;

    internal SurfaceGeometry(Vector3[] skirtRest, int[] skirtTriangles, Vector3[] bodyRest, int[] bodyTriangles, int exampleLimit = 12)
    {
        this.exampleLimit = exampleLimit;
        this.skirtTriangles = skirtTriangles;
        bodyIndex = new TriangleIndex(bodyRest, bodyTriangles);
        logical = LogicalVertices(skirtRest);
        edges = UniqueEdges(skirtTriangles, logical);
        selfIndex = new TriangleIndex(skirtRest, skirtTriangles, logical);
    }

    internal void Analyze(Vector3[] surface, Vector3[] reference, Vector3[] bodyWorld, int budget, GeometryData result)
    {
        selfIndex.Update(surface);
        bodyIndex.Update(bodyWorld);
        var examples = new List<Vector3>();
        int edgeCount = edges.Length / 2;
        result.totalEdges = edgeCount;
        int step = Math.Max(1, (int)Math.Ceiling((double)edgeCount / budget));
        for (int edge = 0; edge < edgeCount; edge += step)
        {
            int a = edges[edge * 2];
            int b = edges[edge * 2 + 1];
            result.checkedEdges++;
            if (bodyIndex.Intersect(surface[a], surface[b], -1, -1, out Vector3 hit))
            {
                result.bodyCrossings++;
                if (examples.Count < exampleLimit)
                    examples.Add(hit);
            }
            if (selfIndex.Intersect(surface[a], surface[b], logical[a], logical[b], out hit))
            {
                result.selfCrossings++;
                if (examples.Count < exampleLimit)
                    examples.Add(hit);
            }
            float baseline = Vector3.Distance(reference[a], reference[b]);
            if (baseline < 0.003f)
                continue;
            float stretch = Vector3.Distance(surface[a], surface[b]) / baseline;
            result.maximumStretch = Math.Max(result.maximumStretch, stretch);
            if (stretch > 1.6f)
                result.stretchedEdges++;
        }
        for (int indexOffset = 0; indexOffset < skirtTriangles.Length; indexOffset += 3)
        {
            int a = skirtTriangles[indexOffset];
            int b = skirtTriangles[indexOffset + 1];
            int c = skirtTriangles[indexOffset + 2];
            float baseline = Vector3.Cross(reference[b] - reference[a], reference[c] - reference[a]).sqrMagnitude;
            float actual = Vector3.Cross(surface[b] - surface[a], surface[c] - surface[a]).sqrMagnitude;
            if (baseline > 1e-10f && actual < baseline * 0.01f)
                result.collapsedTriangles++;
        }
        result.crossingPoints = examples.ToArray();
        result.limitation = "Segment tests exclude coplanar overlap and endpoint contacts; checkedEdges records sampling coverage.";
    }

    private static int[] LogicalVertices(Vector3[] points)
    {
        var map = new Dictionary<Tuple<int, int, int>, int>();
        var logical = new int[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            Vector3 point = points[index] * 100000f;
            var key = Tuple.Create((int)Math.Round(point.x), (int)Math.Round(point.y), (int)Math.Round(point.z));
            if (!map.TryGetValue(key, out int value))
            {
                value = map.Count;
                map.Add(key, value);
            }
            logical[index] = value;
        }
        return logical;
    }

    private static int[] UniqueEdges(int[] triangles, int[] logical)
    {
        var seen = new HashSet<long>();
        var edges = new List<int>();
        for (int index = 0; index < triangles.Length; index += 3)
            for (int corner = 0; corner < 3; corner++)
            {
                int a = triangles[index + corner];
                int b = triangles[index + (corner + 1) % 3];
                int x = logical[a];
                int y = logical[b];
                long key = ((long)Math.Min(x, y) << 32) | (uint)Math.Max(x, y);
                if (x != y && seen.Add(key))
                {
                    edges.Add(a);
                    edges.Add(b);
                }
            }
        return edges.ToArray();
    }
}
