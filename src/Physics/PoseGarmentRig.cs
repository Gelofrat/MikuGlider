using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal sealed class PoseGarmentRig : IDisposable
{
    private const int Sectors = 16;
    private static readonly List<CachedBinding> Cache = new List<CachedBinding>();
    private readonly Mesh restSkirt;
    private readonly CharacterBodyBones body;
    private readonly int pelvis;
    private readonly int firstSector;
    private readonly Transform[] sectors = new Transform[Sectors];
    private readonly Transform[] hems = new Transform[Sectors];
    private readonly Vector3[] directions = new Vector3[Sectors];
    private readonly Vector3[] restDirections = new Vector3[Sectors];
    private readonly float[] flareAngles = new float[Sectors];
    private readonly Matrix4x4[] bindposes;
    private readonly float top;
    private readonly float bottom;
    private readonly float hinge;
    internal Transform Reference { get; }
    internal Transform[] Bones { get; }
    internal float SeatedBlend { get; private set; }

    internal PoseGarmentRig(Transform[] bones, Mesh skirt)
    {
        body = new CharacterBodyBones(bones);
        restSkirt = skirt;
        if (!body.HasLegs || body.Pelvis == null)
            throw new InvalidOperationException("Garment rig requires pelvis and both legs.");
        pelvis = Array.IndexOf(bones, body.Pelvis);
        firstSector = bones.Length;
        Bones = new Transform[firstSector + Sectors * 2];
        Array.Copy(bones, Bones, bones.Length);
        bindposes = new Matrix4x4[Bones.Length];
        Array.Copy(skirt.bindposes, bindposes, bones.Length);
        top = float.NegativeInfinity;
        bottom = float.PositiveInfinity;
        Vector3[] skirtVertices = skirt.vertices;
        foreach (Vector3 vertex in skirtVertices)
        {
            top = Mathf.Max(top, vertex.y);
            bottom = Mathf.Min(bottom, vertex.y);
        }
        hinge = top - .065f;
        var flareCounts = new int[Sectors];
        foreach (Vector3 vertex in skirtVertices)
        {
            if (vertex.y > Mathf.Lerp(bottom, hinge, .35f)) continue;
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(vertex.x, vertex.z) / (2f * Mathf.PI), 1f) * Sectors) % Sectors;
            flareAngles[sector] += Mathf.Atan2(Mathf.Max(0, new Vector2(vertex.x, vertex.z).magnitude - .14f), Mathf.Max(.01f, hinge - vertex.y));
            flareCounts[sector]++;
        }
        Reference = new GameObject("Miku garment pose frame").transform;
        Reference.SetParent(body.Pelvis, false);
        Matrix4x4 frame = bindposes[pelvis];
        Reference.localPosition = frame.GetColumn(3);
        Reference.localRotation = frame.rotation;
        Reference.localScale = frame.lossyScale;
        for (int index = 0; index < Sectors; index++)
        {
            float angle = index * Mathf.PI * 2f / Sectors;
            directions[index] = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            flareAngles[index] = flareCounts[index] > 0 ? flareAngles[index] / flareCounts[index] : .5f;
            restDirections[index] = Vector3.down * Mathf.Cos(flareAngles[index]) + directions[index] * Mathf.Sin(flareAngles[index]);
            Transform bone = new GameObject("Miku skirt sector " + index).transform;
            bone.SetParent(Reference, false);
            bone.localPosition = directions[index] * .14f + new Vector3(0, hinge, 0);
            sectors[index] = bone;
            Bones[firstSector + index] = bone;
            bindposes[firstSector + index] = Matrix4x4.Translate(-bone.localPosition);
            Transform hem = new GameObject("Miku skirt hem " + index).transform;
            hem.SetParent(bone, false);
            hem.localPosition = Vector3.down * (hinge - bottom) * .45f;
            hems[index] = hem;
            Bones[firstSector + Sectors + index] = hem;
            bindposes[firstSector + Sectors + index] = Matrix4x4.Translate(-(bone.localPosition + hem.localPosition));
        }
    }

    internal Mesh Bind(Mesh source, bool coat, Mesh outer = null)
    {
        foreach (CachedBinding entry in Cache)
            if (entry.Source == source && entry.Skirt == restSkirt && entry.Outer == outer &&
                entry.Coat == coat && entry.Pelvis == pelvis && entry.BoneCount == firstSector && entry.Mesh != null)
                return entry.Mesh;
        Mesh mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = source.name + " pose bound";
        Vector3[] vertices = mesh.vertices;
        Vector3[] outerVertices = outer != null ? outer.vertices : null;
        int[] outerTriangles = outer != null ? outer.triangles : null;
        List<int>[] heightBuckets = BuildHeightBuckets(outerVertices, outerTriangles);
        var covered = new bool[vertices.Length];
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] poses = (Matrix4x4[])bindposes.Clone();
        Array.Copy(source.bindposes, poses, source.bindposes.Length);
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 point = vertices[index];
            if (coat && point.y > hinge)
            {
                weights[index] = BlendWaist(weights[index], 1f - Mathf.SmoothStep(0, 1, (point.y - hinge) / .12f));
                continue;
            }
            float influence = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hinge, hinge - .04f, point.y));
            float angle = Mathf.Repeat(Mathf.Atan2(point.x, point.z) / (2f * Mathf.PI), 1f) * Sectors;
            int sector = Mathf.FloorToInt(angle) % Sectors;
            float fraction = angle - Mathf.Floor(angle);
            weights[index] = new BoneWeight
            {
                boneIndex0 = pelvis, weight0 = 1f - influence,
                boneIndex1 = firstSector + sector, weight1 = influence * (1f - fraction),
                boneIndex2 = firstSector + (sector + 1) % Sectors, weight2 = influence * fraction
            };
            float hemInfluence = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hinge - (hinge - bottom) * .3f,
                hinge - (hinge - bottom) * .85f, point.y));
            if (influence >= .9999f && hemInfluence > 0)
                weights[index] = new BoneWeight
                {
                    boneIndex0 = firstSector + sector, weight0 = (1f - hemInfluence) * (1f - fraction),
                    boneIndex1 = firstSector + (sector + 1) % Sectors, weight1 = (1f - hemInfluence) * fraction,
                    boneIndex2 = firstSector + Sectors + sector, weight2 = hemInfluence * (1f - fraction),
                    boneIndex3 = firstSector + Sectors + (sector + 1) % Sectors, weight3 = hemInfluence * fraction
                };
            if (!coat)
            {
                float tuck = .012f * (1f - Mathf.SmoothStep(0, 1, (top - point.y) / .10f));
                Vector3 radial = new Vector3(point.x, 0, point.z).normalized;
                float radius = new Vector2(point.x, point.z).magnitude;
                List<int> faces = heightBuckets != null ? heightBuckets[HeightBucket(point.y)] : null;
                float fitted = FitRadius(point.y, radial, radius - tuck, outerVertices, outerTriangles, faces, out covered[index]);
                vertices[index] = new Vector3(radial.x * fitted, point.y, radial.z * fitted);
            }
        }
        if (!coat && outer != null)
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                var visible = new System.Collections.Generic.List<int>(triangles.Length);
                for (int face = 0; face < triangles.Length; face += 3)
                {
                    int a = triangles[face], b = triangles[face + 1], c = triangles[face + 2];
                    bool hidden = covered[a] && covered[b] && covered[c] &&
                        Mathf.Min(vertices[a].y, Mathf.Min(vertices[b].y, vertices[c].y)) > top - .10f;
                    if (hidden) continue;
                    visible.Add(a); visible.Add(b); visible.Add(c);
                }
                mesh.SetTriangles(visible, submesh);
            }
        mesh.vertices = vertices;
        mesh.boneWeights = weights;
        mesh.bindposes = poses;
        mesh.RecalculateBounds();
        Cache.Add(new CachedBinding { Source = source, Skirt = restSkirt, Outer = outer, Coat = coat,
            Pelvis = pelvis, BoneCount = firstSector, Mesh = mesh });
        return mesh;
    }

    private int HeightBucket(float height) => Mathf.Clamp(Mathf.FloorToInt((height - bottom) / Mathf.Max(.001f, top - bottom) * 64f), 0, 63);

    private List<int>[] BuildHeightBuckets(Vector3[] vertices, int[] triangles)
    {
        if (vertices == null) return null;
        var buckets = new List<int>[64];
        for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>();
        for (int face = 0; face < triangles.Length; face += 3)
        {
            float a = vertices[triangles[face]].y, b = vertices[triangles[face + 1]].y, c = vertices[triangles[face + 2]].y;
            float min = Mathf.Min(a, Mathf.Min(b, c)), max = Mathf.Max(a, Mathf.Max(b, c));
            if (max < bottom || min > top) continue;
            for (int i = HeightBucket(min); i <= HeightBucket(max); i++) buckets[i].Add(face);
        }
        return buckets;
    }

    private static float FitRadius(float height, Vector3 direction, float radius, Vector3[] vertices, int[] triangles,
        List<int> faces, out bool covered)
    {
        covered = false;
        if (vertices == null) return radius;
        Vector3 origin = new Vector3(0, height, 0);
        float limit = radius;
        foreach (int face in faces)
        {
            Vector3 a = vertices[triangles[face]], b = vertices[triangles[face + 1]], c = vertices[triangles[face + 2]];
            if (height < Mathf.Min(a.y, Mathf.Min(b.y, c.y)) || height > Mathf.Max(a.y, Mathf.Max(b.y, c.y))) continue;
            Vector3 edge = b - a, other = c - a;
            Vector3 cross = Vector3.Cross(direction, other);
            float determinant = Vector3.Dot(edge, cross);
            if (Mathf.Abs(determinant) < 1e-9f) continue;
            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < -.0001f || u > 1.0001f) continue;
            Vector3 q = Vector3.Cross(offset, edge);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -.0001f || u + v > 1.0001f) continue;
            float distance = Vector3.Dot(other, q) * inverse;
            if (distance > .10f)
            {
                if (distance < radius + .08f) covered = true;
                limit = Mathf.Min(limit, distance - .012f);
            }
        }
        return Mathf.Max(radius - .035f, limit);
    }

    private BoneWeight BlendWaist(BoneWeight original, float influence)
    {
        if (influence <= 0) return original;
        var values = new System.Collections.Generic.Dictionary<int, float>();
        int[] indices = { original.boneIndex0, original.boneIndex1, original.boneIndex2, original.boneIndex3, pelvis };
        float[] amounts = { original.weight0 * (1 - influence), original.weight1 * (1 - influence),
            original.weight2 * (1 - influence), original.weight3 * (1 - influence), influence };
        for (int i = 0; i < indices.Length; i++)
            values[indices[i]] = (values.TryGetValue(indices[i], out float existing) ? existing : 0) + amounts[i];
        var ordered = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, float>>(values);
        ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        while (ordered.Count < 4) ordered.Add(new System.Collections.Generic.KeyValuePair<int, float>(pelvis, 0));
        float total = ordered[0].Value + ordered[1].Value + ordered[2].Value + ordered[3].Value;
        return new BoneWeight { boneIndex0 = ordered[0].Key, weight0 = ordered[0].Value / total,
            boneIndex1 = ordered[1].Key, weight1 = ordered[1].Value / total,
            boneIndex2 = ordered[2].Key, weight2 = ordered[2].Value / total,
            boneIndex3 = ordered[3].Key, weight3 = ordered[3].Value / total };
    }

    internal void UpdatePose(bool flying = false)
    {
        Matrix4x4 inverse = Reference.worldToLocalMatrix;
        Vector3 left = inverse.MultiplyVector(body.LeftKnee.position - body.LeftThigh.position).normalized;
        Vector3 right = inverse.MultiplyVector(body.RightKnee.position - body.RightThigh.position).normalized;
        Vector3 gravity = inverse.MultiplyVector(Vector3.down).normalized;
        float leftSign = Mathf.Sign(inverse.MultiplyPoint3x4(body.LeftThigh.position).x);
        float seated = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, .15f, Mathf.Max(-left.y, -right.y))) *
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.2f, .8f, Vector3.Dot(left, right)));
        float pelvisTilt = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.85f, .35f, Vector3.Dot(gravity, Vector3.down)));
        seated = flying ? 0f : Mathf.Max(seated, pelvisTilt);
        SeatedBlend = seated;
        for (int index = 0; index < Sectors; index++)
        {
            Vector3 radial = directions[index];
            float leftWeight = Mathf.SmoothStep(0, 1, (radial.x * leftSign + .35f) / .7f);
            float rightWeight = 1f - leftWeight;
            float leftAngle = Opening(left, radial), rightAngle = Opening(right, radial);
            float angle = Mathf.Lerp(Mathf.Max(leftAngle * leftWeight, rightAngle * rightWeight),
                leftAngle * leftWeight + rightAngle * rightWeight, seated);
            float seatedLimit = Mathf.Max(0, Mathf.PI * .47f - flareAngles[index]);
            angle = Mathf.Lerp(angle, Mathf.Min(angle, seatedLimit), seated);
            Vector3 down = Vector3.down * Mathf.Cos(angle) + radial * Mathf.Sin(angle);
            sectors[index].localRotation = Quaternion.FromToRotation(Vector3.down, down);
            Vector3 hemDirection = Quaternion.Inverse(sectors[index].localRotation) * gravity;
            hems[index].localRotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(restDirections[index], hemDirection), seated * .85f);
        }
    }

    private static float Opening(Vector3 leg, Vector3 radial)
    {
        float projected = Vector3.Dot(leg, radial);
        float angle = Mathf.Atan2(Mathf.Max(0, projected), Mathf.Max(.05f, -leg.y));
        return Mathf.Clamp(angle - .25f, 0, .95f);
    }

    public void Dispose()
    {
        if (Reference != null) UnityEngine.Object.Destroy(Reference.gameObject);
    }

    private sealed class CachedBinding
    {
        internal Mesh Source;
        internal Mesh Skirt;
        internal Mesh Outer;
        internal Mesh Mesh;
        internal bool Coat;
        internal int Pelvis;
        internal int BoneCount;
    }
}
