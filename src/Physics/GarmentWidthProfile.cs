using System.Runtime.CompilerServices;
using UnityEngine;

namespace MikuGlider;

internal sealed class GarmentWidthProfile
{
    private static readonly ConditionalWeakTable<Mesh, GarmentWidthProfile> Cache = new ConditionalWeakTable<Mesh, GarmentWidthProfile>();
    private readonly float[] widths = new float[128];
    internal float Top { get; }
    internal float Bottom { get; }

    internal static GarmentWidthProfile ForMesh(Mesh mesh) => Cache.GetValue(mesh, Create);

    private static GarmentWidthProfile Create(Mesh mesh) => new GarmentWidthProfile(mesh.vertices, mesh.triangles);

    internal GarmentWidthProfile(Vector3[] vertices, int[] triangles)
    {
        float top = float.NegativeInfinity;
        float bottom = float.PositiveInfinity;
        foreach (Vector3 point in vertices)
        {
            top = Mathf.Max(top, point.y);
            bottom = Mathf.Min(bottom, point.y);
        }
        Top = top;
        Bottom = bottom;
        for (int row = 0; row < widths.Length; row++)
        {
            float height = Mathf.Lerp(bottom, top, row / (float)(widths.Length - 1));
            float width = 0;
            for (int face = 0; face < triangles.Length; face += 3)
            {
                Vector3 a = vertices[triangles[face]];
                Vector3 b = vertices[triangles[face + 1]];
                Vector3 c = vertices[triangles[face + 2]];
                width = IncludeEdge(width, a, b, height);
                width = IncludeEdge(width, b, c, height);
                width = IncludeEdge(width, c, a, height);
            }
            widths[row] = width;
        }
    }

    internal float AtHeight(float height)
    {
        float coordinate = Mathf.InverseLerp(Bottom, Top, height) * (widths.Length - 1);
        int lower = Mathf.FloorToInt(coordinate);
        return Mathf.Lerp(widths[lower], widths[Mathf.Min(lower + 1, widths.Length - 1)], coordinate - lower);
    }

    private static float IncludeEdge(float width, Vector3 a, Vector3 b, float height)
    {
        if (height < Mathf.Min(a.y, b.y) || height > Mathf.Max(a.y, b.y)) return width;
        float span = b.y - a.y;
        if (Mathf.Abs(span) < .000001f)
            return Mathf.Max(width, Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(b.x)));
        return Mathf.Max(width, Mathf.Abs(Mathf.Lerp(a.x, b.x, (height - a.y) / span)));
    }
}
