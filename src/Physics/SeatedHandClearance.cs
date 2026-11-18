using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal sealed class SeatedHandClearance
{
    private readonly Transform[,] arms = new Transform[2, 3];
    private readonly Transform[,] probes = new Transform[2, 9];
    private readonly Transform[] knees = new Transform[2];
    private readonly Quaternion[,] original = new Quaternion[2, 3];
    private readonly Quaternion[,] applied = new Quaternion[2, 3];
    private readonly bool[] changed = new bool[2];
    private readonly float[] lifts = new float[2];
    private readonly float[] elbowAngles = new float[2];
    private readonly Transform[] bones;
    private readonly Matrix4x4[] bindposes;
    private readonly Matrix4x4[] matrices;
    private readonly Vector3[] vertices;
    private readonly Vector3[] world;
    private readonly BoneWeight[] weights;
    private readonly int[] triangles;
    private readonly SurfaceBounds[] triangleBounds;
    private readonly bool preserveHandHeading;
    private readonly Mesh skirtMesh, coatMesh;
    private readonly int[] vertexSources;
    private readonly List<Vector3> skirtVertices, coatVertices;

    internal SeatedHandClearance(Transform[] body, Transform[] garmentBones, Mesh skirt, Mesh coat,
        bool preserveHandHeading = false)
    {
        this.preserveHandHeading = preserveHandHeading;
        skirtMesh = skirt;
        coatMesh = coat;
        skirtVertices = new List<Vector3>(skirt.vertexCount);
        coatVertices = new List<Vector3>(coat != null ? coat.vertexCount : 0);
        bones = garmentBones;
        bindposes = skirt.bindposes;
        matrices = new Matrix4x4[bones.Length];
        BuildSurface(skirt, coat, out vertices, out weights, out triangles, out vertexSources);
        triangleBounds = new SurfaceBounds[triangles.Length / 3];
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
                if (bone.name == "joint_Thumb03" + suffix) probes[side, 3] = bone;
                if (bone.name == "joint_Index03" + suffix) probes[side, 4] = bone;
                if (bone.name == "joint_Ring03" + suffix) probes[side, 5] = bone;
                if (bone.name == "joint_Pinky03" + suffix) probes[side, 6] = bone;
                if (bone.name == "joint_Thumb01" + suffix) probes[side, 7] = bone;
                if (bone.name == "joint_Pinky01" + suffix) probes[side, 8] = bone;
                if (bone.name == "joint_Knee" + suffix) knees[side] = bone;
            }
        }
    }

    private static void BuildSurface(Mesh skirt, Mesh coat, out Vector3[] points, out BoneWeight[] skin, out int[] faces, out int[] sources)
    {
        var positions = new List<Vector3>();
        var weights = new List<BoneWeight>();
        var triangles = new List<int>();
        var vertexSources = new List<int>();
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
                    vertexSources.Add(source == skirt ? i : -i - 1);
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
        sources = vertexSources.ToArray();
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
        if (seated < .15f)
        {
            lifts[0] = lifts[1] = 0;
            elbowAngles[0] = elbowAngles[1] = 0;
            return;
        }
        float scale = reference.lossyScale.x;
        // Contact the final cloth after leg clearance, not the undeformed
        // authoring surface. Reuse buffers while a sitting animation is active.
        skirtMesh.GetVertices(skirtVertices);
        if (coatMesh != null) coatMesh.GetVertices(coatVertices);
        for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight w = weights[i];
            int source = vertexSources[i];
            Vector3 p = source >= 0 ? skirtVertices[source] : coatVertices[-source - 1];
            world[i] = matrices[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 +
                matrices[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 +
                matrices[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
        }
        for (int i = 0; i < triangleBounds.Length; i++)
        {
            Vector3 a = world[triangles[i * 3]], b = world[triangles[i * 3 + 1]], c = world[triangles[i * 3 + 2]];
            triangleBounds[i] = new SurfaceBounds { minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) };
        }
        for (int side = 0; side < 2; side++)
        {
            Transform wrist = arms[side, 2];
            if (wrist == null || arms[side, 0] == null || arms[side, 1] == null) continue;
            Vector3 local = reference.InverseTransformPoint(wrist.position);
            if (local.z < .1f) { lifts[side] = elbowAngles[side] = 0; continue; }
            // Do not pull an already clear hand down onto the lap (steering,
            // gestures, or a transition away from sitting).
            float overlap = float.NegativeInfinity;
            for (int probe = -1; probe < probes.GetLength(1); probe++)
            {
                Transform p = probe < 0 ? wrist : probes[side, probe];
                if (p == null) continue;
                float height = ContactHeight(p.position, .025f * scale, .12f * scale);
                if (height > p.position.y - .32f * scale && height < p.position.y + .32f * scale)
                    overlap = Mathf.Max(overlap, height + .030f * scale - p.position.y);
            }
            if (overlap <= 0 && RequiredElbowLift(side, scale) <= 0 &&
                lifts[side] < .0001f * scale && elbowAngles[side] < .00001f)
            { lifts[side] = elbowAngles[side] = 0; continue; }
            for (int joint = 0; joint < 3; joint++) original[side, joint] = arms[side, joint].localRotation;
            // Preserve the animated palm orientation instead of retipping it
            // when the sampled cloth tangent changes across a seam.
            if (!preserveHandHeading && probes[side, 2] != null)
            {
                Vector3 direction = probes[side, 2].position - wrist.position;
                Vector3 ahead = knees[0] != null && knees[1] != null ?
                    (knees[0].position + knees[1].position) * .5f - wrist.position : direction;
                ahead = Vector3.ProjectOnPlane(ahead, Vector3.up).normalized;
                Vector3 start = wrist.position + ahead * .06f * scale;
                Vector3 finish = wrist.position + ahead * direction.magnitude;
                float startHeight = SurfaceHeight(start, .015f * scale);
                float endHeight = SurfaceHeight(finish, .015f * scale);
                Vector3 tangent = finish - start;
                if (!float.IsNegativeInfinity(startHeight) && !float.IsNegativeInfinity(endHeight))
                    tangent.y = endHeight - startHeight;
                if (tangent.sqrMagnitude > .000001f)
                    wrist.rotation = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(direction, tangent),
                        75f * Mathf.SmoothStep(0, 1, seated / .7f) *
                        Mathf.SmoothStep(0, 1, Mathf.Max(0, overlap) / (.06f * scale))) * wrist.rotation;
            }
            // With the native palm orientation the activation query already
            // found this exact bound. Only a retipped hand needs another scan.
            float lift = preserveHandHeading ? overlap : float.NegativeInfinity;
            for (int probe = -1; !preserveHandHeading && probe < probes.GetLength(1); probe++)
            {
                Transform contact = probe < 0 ? wrist : probes[side, probe];
                if (contact == null) continue;
                Vector3 point = contact.position;
                float height = ContactHeight(point, .025f * scale, .12f * scale);
                if (height > point.y - .32f * scale && height < point.y + .32f * scale)
                    lift = Mathf.Max(lift, height + .030f * scale - point.y);
            }
            lift = float.IsNegativeInfinity(lift) ? 0f : Mathf.Clamp(lift, 0, .32f * scale);
            // The surface is a lower bound, not a delayed target: filtering an
            // increasing lift lets the descending hand pass through the cloth.
            // Smooth only the release while maintaining clearance this frame.
            lifts[side] = Mathf.Max(lift, Mathf.Lerp(lifts[side], lift, 1 - Mathf.Exp(-deltaTime * 18)));
            MoveWrist(arms[side, 0], arms[side, 1], wrist, wrist.position + Vector3.up * lifts[side]);
            ClearForearm(side, scale, seated, deltaTime);
            for (int joint = 0; joint < 3; joint++) applied[side, joint] = arms[side, joint].localRotation;
            changed[side] = true;
        }
    }

    private void ClearForearm(int side, float scale, float seated, float deltaTime)
    {
        Transform shoulder = arms[side, 0], elbow = arms[side, 1], wrist = arms[side, 2];
        Vector3 origin = shoulder.position, target = wrist.position;
        Vector3 axis = (target - origin).normalized;
        Vector3 center = origin + axis * Vector3.Dot(elbow.position - origin, axis);
        Vector3 radial = elbow.position - center;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        float radius = radial.magnitude;
        if (radius < .0001f || up.sqrMagnitude < .5f) { elbowAngles[side] = 0; return; }
        Vector3 bend = radial / radius;
        float maximum = Mathf.Min(16f * Mathf.Deg2Rad,
            2f * Mathf.Asin(Mathf.Clamp01(.045f * scale / (2f * radius))));
        maximum = Mathf.Min(maximum, Vector3.Angle(bend, up) * Mathf.Deg2Rad);
        float required = RequiredElbowLift(side, scale);
        float wanted = 0;
        if (required > 0)
        {
            float low = 0, high = maximum;
            for (int iteration = 0; iteration < 6; iteration++)
            {
                float angle = (low + high) * .5f;
                Vector3 candidate = center + Vector3.RotateTowards(bend, up, angle, 0) * radius;
                if (candidate.y < elbow.position.y + required) low = angle;
                else high = angle;
            }
            wanted = high * Mathf.SmoothStep(0, 1, seated / .7f);
        }
        elbowAngles[side] = Mathf.Lerp(elbowAngles[side], wanted, 1 - Mathf.Exp(-deltaTime * 18));
        float appliedAngle = Mathf.Min(elbowAngles[side], maximum);
        if (appliedAngle < .00001f) return;
        Vector3 joint = center + Vector3.RotateTowards(bend, up, appliedAngle, 0) * radius;
        Quaternion handRotation = wrist.rotation;
        shoulder.rotation = Quaternion.FromToRotation(elbow.position - origin, joint - origin) * shoulder.rotation;
        elbow.rotation = Quaternion.FromToRotation(wrist.position - elbow.position, target - elbow.position) * elbow.rotation;
        wrist.rotation = handRotation;
    }

    private float RequiredElbowLift(int side, float scale)
    {
        Vector3 elbow = arms[side, 1].position, wrist = arms[side, 2].position;
        float required = 0;
        for (int sample = 0; sample < 3; sample++)
        {
            float along = .30f + sample * .25f;
            Vector3 point = Vector3.Lerp(elbow, wrist, along);
            float radius = Mathf.Lerp(.042f, .028f, along) * scale;
            float height = SurfaceHeight(point, radius);
            if (height < point.y - .12f * scale || height > point.y + .12f * scale) continue;
            required = Mathf.Max(required, (height + radius - point.y) / (1 - along));
        }
        return required;
    }

    private float SurfaceHeight(Vector3 point, float radius) => ContactHeight(point, radius, 0);

    // A sloped approach outside the cloth edge lets the hand rise before
    // crossing its silhouette. A hard edge alone would introduce a step in
    // the required lift even when the animation moves smoothly sideways.
    private float ContactHeight(Vector3 point, float radius, float approach)
    {
        float height = float.NegativeInfinity;
        float reach = radius + approach;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            SurfaceBounds bounds = triangleBounds[i / 3];
            if (point.x < bounds.minX - reach || point.x > bounds.maxX + reach ||
                point.z < bounds.minZ - reach || point.z > bounds.maxZ + reach) continue;
            Vector3 a = world[triangles[i]], b = world[triangles[i + 1]], c = world[triangles[i + 2]];
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float determinant = ab.x * ac.z - ab.z * ac.x;
            if (Mathf.Abs(determinant) < 1e-9f) continue;
            float u = (ap.x * ac.z - ap.z * ac.x) / determinant;
            float v = (ab.x * ap.z - ab.z * ap.x) / determinant;
            if (u >= 0 && v >= 0 && u + v <= 1)
                height = Mathf.Max(height, a.y + u * ab.y + v * ac.y);
            height = Mathf.Max(height, EdgeHeight(point, a, b, radius, reach));
            height = Mathf.Max(height, EdgeHeight(point, b, c, radius, reach));
            height = Mathf.Max(height, EdgeHeight(point, c, a, radius, reach));
        }
        return height;
    }

    private struct SurfaceBounds { internal float minX, maxX, minZ, maxZ; }

    private static float EdgeHeight(Vector3 point, Vector3 a, Vector3 b, float radius, float reach)
    {
        Vector2 edge = new Vector2(b.x - a.x, b.z - a.z);
        Vector2 offset = new Vector2(point.x - a.x, point.z - a.z);
        float t = edge.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(offset, edge) / edge.sqrMagnitude) : 0;
        float distanceSquared = (offset - edge * t).sqrMagnitude;
        if (distanceSquared <= radius * radius) return Mathf.Lerp(a.y, b.y, t);
        return distanceSquared <= reach * reach
            ? Mathf.Lerp(a.y, b.y, t) - 3f * (Mathf.Sqrt(distanceSquared) - radius) : float.NegativeInfinity;
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
