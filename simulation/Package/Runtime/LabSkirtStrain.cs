using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal sealed class LabSkirtStrain
    {
        private readonly int[] edges;
        private readonly float[] lengths;
        private readonly float[] ratios;
        private readonly int[][] meridians;
        private readonly float[] meridianLengths;

        internal LabSkirtStrain(Mesh mesh)
        {
            Vector3[] points = mesh.vertices;
            int[] triangles = mesh.triangles;
            var selected = new List<int>();
            var baseline = new List<float>();
            var seen = new HashSet<Tuple<Vector3, Vector3>>();
            for (int index = 0; index < triangles.Length; index += 3)
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = triangles[index + corner];
                    int b = triangles[index + (corner + 1) % 3];
                    Vector3 first = points[a];
                    Vector3 second = points[b];
                    var key = Tuple.Create(first, second);
                    if (seen.Contains(key) || seen.Contains(Tuple.Create(second, first)))
                        continue;
                    seen.Add(key);
                    float length = Vector3.Distance(first, second);
                    if (length < .003f)
                        continue;
                    selected.Add(a);
                    selected.Add(b);
                    baseline.Add(length);
                }
            edges = selected.ToArray();
            lengths = baseline.ToArray();
            ratios = new float[lengths.Length];
            Vector3 center = new Vector3((points.Min(p => p.x) + points.Max(p => p.x)) * .5f, 0,
                (points.Min(p => p.z) + points.Max(p => p.z)) * .5f);
            int[][] rings = Enumerable.Range(0, points.Length).GroupBy(vertex => Mathf.RoundToInt(points[vertex].y * 100000))
                .OrderByDescending(group => group.Key).Select(group => group.GroupBy(vertex => points[vertex]).Select(duplicates => duplicates.First())
                    .OrderBy(vertex => Mathf.Repeat(Mathf.Atan2(points[vertex].z - center.z, points[vertex].x - center.x) * Mathf.Rad2Deg, 360)).ToArray()).ToArray();
            if (rings.Length < 3 || rings.Any(ring => ring.Length != rings[0].Length))
            {
                meridians = SurfacePaths(points, triangles);
                meridianLengths = meridians.Select(column => Length(points, column)).ToArray();
                return;
            }
            int count = rings[0].Length;
            for (int row = 1; row < rings.Length; row++)
            {
                int offset = 0;
                float best = float.PositiveInfinity;
                for (int shift = 0; shift < count; shift++)
                {
                    float cost = 0;
                    for (int column = 0; column < count; column++)
                        cost += (points[rings[row - 1][column]] - points[rings[row][(column + shift) % count]]).sqrMagnitude;
                    if (cost >= best)
                        continue;
                    best = cost;
                    offset = shift;
                }
                int[] original = rings[row];
                rings[row] = Enumerable.Range(0, count).Select(column => original[(column + offset) % count]).ToArray();
            }
            meridians = Enumerable.Range(0, count).Select(column => rings.Select(ring => ring[column]).ToArray()).ToArray();
            meridianLengths = meridians.Select(column => Length(points, column)).ToArray();
        }

        internal float LengthStretch(Vector3[] world, float scale)
        {
            float maximum = 1;
            for (int column = 0; column < meridians.Length; column++)
                maximum = Mathf.Max(maximum, Length(world, meridians[column]) / (meridianLengths[column] * scale));
            return maximum;
        }

        private static int[][] SurfacePaths(Vector3[] points, int[] triangles)
        {
            var groups = Enumerable.Range(0, points.Length).GroupBy(index => new Vector3Int(
                Mathf.RoundToInt(points[index].x * 100000), Mathf.RoundToInt(points[index].y * 100000),
                Mathf.RoundToInt(points[index].z * 100000))).ToArray();
            int[] representatives = groups.Select(group => group.First()).ToArray();
            var welded = new int[points.Length];
            for (int index = 0; index < groups.Length; index++)
                foreach (int vertex in groups[index]) welded[vertex] = index;
            var neighbors = representatives.Select(_ => new HashSet<int>()).ToArray();
            for (int face = 0; face < triangles.Length; face += 3)
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = welded[triangles[face + corner]], b = welded[triangles[face + (corner + 1) % 3]];
                    if (a == b) continue;
                    neighbors[a].Add(b);
                    neighbors[b].Add(a);
                }
            float top = points.Max(point => point.y), bottom = points.Min(point => point.y);
            float band = (top - bottom) * .12f;
            float[] distance = representatives.Select(index => points[index].y >= top - band ? 0f : float.PositiveInfinity).ToArray();
            int[] previous = representatives.Select(_ => -1).ToArray();
            var visited = new bool[representatives.Length];
            for (int step = 0; step < representatives.Length; step++)
            {
                int current = -1;
                float nearest = float.PositiveInfinity;
                for (int index = 0; index < distance.Length; index++)
                    if (!visited[index] && distance[index] < nearest) { nearest = distance[index]; current = index; }
                if (current < 0) break;
                visited[current] = true;
                foreach (int next in neighbors[current])
                {
                    float candidate = nearest + Vector3.Distance(points[representatives[current]], points[representatives[next]]);
                    if (candidate >= distance[next]) continue;
                    distance[next] = candidate;
                    previous[next] = current;
                }
            }
            var paths = new List<int[]>();
            foreach (int end in Enumerable.Range(0, representatives.Length).Where(index =>
                points[representatives[index]].y <= bottom + band && !float.IsInfinity(distance[index]))
                .GroupBy(index => Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(points[representatives[index]].x,
                    points[representatives[index]].z) / (2 * Mathf.PI), 1) * 32))
                .Select(group => group.OrderBy(index => points[representatives[index]].y).First()))
            {
                var path = new List<int>();
                for (int current = end; current >= 0; current = previous[current]) path.Add(representatives[current]);
                if (path.Count >= 3) paths.Add(path.ToArray());
            }
            if (paths.Count < 3) throw new InvalidOperationException("Skirt surface has no connected waist-to-hem paths.");
            return paths.ToArray();
        }

        private static float Length(Vector3[] points, int[] indices)
        {
            float length = 0;
            for (int row = 1; row < indices.Length; row++)
                length += Vector3.Distance(points[indices[row - 1]], points[indices[row]]);
            return length;
        }

        internal float Measure(Vector3[] world, float scale, out float percentile)
        {
            float maximum = 1;
            for (int index = 0; index < lengths.Length; index++)
            {
                float ratio = Vector3.Distance(world[edges[index * 2]], world[edges[index * 2 + 1]]) / (lengths[index] * scale);
                ratios[index] = ratio;
                maximum = Mathf.Max(maximum, ratio);
            }
            Array.Sort(ratios);
            percentile = ratios.Length > 0 ? ratios[Math.Min(ratios.Length - 1, (int)(ratios.Length * .95f))] : 1;
            return maximum;
        }
    }
}
