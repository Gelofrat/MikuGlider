using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal sealed class SeatedHandClearance
{
    private readonly Transform[,] arms = new Transform[2, 3];
    private readonly Transform[,] probes = new Transform[2, 3];
    private readonly Quaternion[,] original = new Quaternion[2, 3];
    private readonly Quaternion[,] applied = new Quaternion[2, 3];
    private readonly bool[] changed = new bool[2];
    private readonly float[] lifts = new float[2];
    private readonly Transform[] bones;
    private readonly Matrix4x4[] bindposes;
    private readonly Matrix4x4[] matrices;
    private readonly Vector3[] vertices;
    private readonly Vector3[] world;
    private readonly BoneWeight[] weights;
    private readonly int[] triangles;

    internal SeatedHandClearance(Transform[] body, Transform[] garmentBones, Mesh skirt, Mesh coat)
    {
        bones = garmentBones;
        bindposes = skirt.bindposes;
        matrices = new Matrix4x4[bones.Length];
        BuildSurface(skirt, coat, out vertices, out weights, out triangles);
        world = new Vector3[vertices.Length];
        for (int side = 0; side < 2; side++)
        {
            string suffix = side == 0 ? "_L" : "_R";
            foreach (Transform bone in body)
            {
                if (bone.name == "joint_Shoulder" + suffix) arms[side, 0] = bone;
                if (bone.name == "joint_Elbow" + suffix) arms[side, 1] = bone;
                if (bone.name == "joint_Wrist" + suffix) arms[side, 2] = bone;
                if (bone.name == "joint_Hand" + suffix) probes[side, 0] = bone;
                if (bone.name == "joint_Middle01" + suffix) probes[side, 1] = bone;
                if (bone.name == "joint_Middle03" + suffix) probes[side, 2] = bone;
            }
        }
    }

    private static void BuildSurface(Mesh skirt, Mesh coat, out Vector3[] points, out BoneWeight[] skin, out int[] faces)
    {
        var positions = new List<Vector3>();
        var weights = new List<BoneWeight>();
        var triangles = new List<int>();
        foreach (Mesh source in new[] { skirt, coat })
        {
            if (source == null) continue;
            Vector3[] vertices = source.vertices;
            BoneWeight[] binding = source.boneWeights;
            int[] indices = source.triangles;
            var cells = new Dictionary<Vector3Int, int>();
            var used = new HashSet<Vector3Int>();
            var map = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                map[i] = -1;
                Vector3Int cell = Vector3Int.RoundToInt(p / .02f);
                if (!cells.TryGetValue(cell, out int index))
                {
                    index = positions.Count;
                    cells.Add(cell, index);
                    positions.Add(p);
                    weights.Add(binding[i]);
                }
                map[i] = index;
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = map[indices[i]], b = map[indices[i + 1]], c = map[indices[i + 2]];
                if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a) continue;
                int low = Mathf.Min(a, Mathf.Min(b, c)), high = Mathf.Max(a, Mathf.Max(b, c));
                if (!used.Add(new Vector3Int(low, a + b + c - low - high, high))) continue;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
        }
        points = positions.ToArray();
        skin = weights.ToArray();
        faces = triangles.ToArray();
    }

    internal void Restore()
    {
        for (int side = 0; side < 2; side++)
        {
            if (!changed[side]) continue;
            for (int joint = 0; joint < 3; joint++)
                if (arms[side, joint] != null && Quaternion.Angle(arms[side, joint].localRotation, applied[side, joint]) < .01f)
                    arms[side, joint].localRotation = original[side, joint];
            changed[side] = false;
        }
    }

    internal void Apply(Transform reference, float seated, float deltaTime)
    {
        if (seated < .15f) { lifts[0] = lifts[1] = 0; return; }
        float scale = reference.lossyScale.x;
        for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight w = weights[i];
            Vector3 p = vertices[i];
            world[i] = matrices[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 +
                matrices[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 +
                matrices[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
        }
        for (int side = 0; side < 2; side++)
        {
            Transform wrist = arms[side, 2];
            if (wrist == null || arms[side, 0] == null || arms[side, 1] == null) continue;
            Vector3 local = reference.InverseTransformPoint(wrist.position);
            if (local.z < .1f) { lifts[side] = 0; continue; }
            Vector3 forward = reference.TransformVector(Vector3.forward * (.075f * seated));
            for (int joint = 0; joint < 3; joint++) original[side, joint] = arms[side, joint].localRotation;
            if (probes[side, 2] != null)
            {
                Vector3 direction = probes[side, 2].position - wrist.position;
                Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
                if (flat.sqrMagnitude > .000001f)
                    wrist.rotation = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(direction, flat), 30f * seated) * wrist.rotation;
            }
            float lift = 0;
            for (int probe = -1; probe < 3; probe++)
            {
                Transform contact = probe < 0 ? wrist : probes[side, probe];
                if (contact == null) continue;
                Vector3 point = contact.position + forward;
                float height = SurfaceHeight(point, .04f * scale);
                if (height > point.y - .04f * scale && height < point.y + .32f * scale)
                    lift = Mathf.Max(lift, height + .055f * scale - point.y);
            }
            lift = Mathf.Clamp(lift, .08f * scale, .32f * scale) * Mathf.SmoothStep(0, 1, seated);
            lifts[side] = Mathf.Lerp(lifts[side], lift, 1 - Mathf.Exp(-deltaTime * 18));
            MoveWrist(arms[side, 0], arms[side, 1], wrist, wrist.position + forward + Vector3.up * lifts[side]);
            for (int joint = 0; joint < 3; joint++) applied[side, joint] = arms[side, joint].localRotation;
            changed[side] = true;
        }
    }

    private float SurfaceHeight(Vector3 point, float radius)
    {
        float height = float.NegativeInfinity;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = world[triangles[i]], b = world[triangles[i + 1]], c = world[triangles[i + 2]];
            if (point.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - radius ||
                point.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + radius ||
                point.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - radius ||
                point.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + radius) continue;
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float determinant = ab.x * ac.z - ab.z * ac.x;
            if (Mathf.Abs(determinant) < 1e-9f) continue;
            float u = (ap.x * ac.z - ap.z * ac.x) / determinant;
            float v = (ab.x * ap.z - ab.z * ap.x) / determinant;
            if (u >= 0 && v >= 0 && u + v <= 1)
                height = Mathf.Max(height, a.y + u * ab.y + v * ac.y);
            height = Mathf.Max(height, EdgeHeight(point, a, b, radius));
            height = Mathf.Max(height, EdgeHeight(point, b, c, radius));
            height = Mathf.Max(height, EdgeHeight(point, c, a, radius));
        }
        return height;
    }

    private static float EdgeHeight(Vector3 point, Vector3 a, Vector3 b, float radius)
    {
        Vector2 edge = new Vector2(b.x - a.x, b.z - a.z);
        Vector2 offset = new Vector2(point.x - a.x, point.z - a.z);
        float t = edge.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(offset, edge) / edge.sqrMagnitude) : 0;
        return (offset - edge * t).sqrMagnitude <= radius * radius
            ? Mathf.Lerp(a.y, b.y, t) : float.NegativeInfinity;
    }

    private static void MoveWrist(Transform shoulder, Transform elbow, Transform wrist, Vector3 target)
    {
        Quaternion handRotation = wrist.rotation;
        Vector3 origin = shoulder.position;
        float upper = Vector3.Distance(origin, elbow.position), lower = Vector3.Distance(elbow.position, wrist.position);
        Vector3 axis = (target - origin).normalized;
        float distance = Mathf.Clamp(Vector3.Distance(target, origin), Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        float altitude = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(elbow.position - origin, axis).normalized;
        Vector3 joint = origin + axis * along + bend * altitude;
        shoulder.rotation = Quaternion.FromToRotation(elbow.position - origin, joint - origin) * shoulder.rotation;
        elbow.rotation = Quaternion.FromToRotation(wrist.position - elbow.position, origin + axis * distance - elbow.position) * elbow.rotation;
        wrist.rotation = handRotation;
    }
}
