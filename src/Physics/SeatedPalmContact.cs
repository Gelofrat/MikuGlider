using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

// Rest the palm itself on the cloth. Finger clearance is solved locally so a
// bent fingertip cannot lift the whole arm away from its support surface.
internal sealed class SeatedPalmContact
{
    private readonly Transform[,] joints = new Transform[2, 7];
    private readonly Transform[,] fingers = new Transform[2, 20];
    private readonly float[,] thickness = new float[2, 2];
    private readonly Quaternion[] tilt = { Quaternion.identity, Quaternion.identity };
    private readonly float[] lifts = new float[2];
    private readonly float[] activation = new float[2];
    private readonly bool[] active = new bool[2];
    private readonly Func<Vector3, float, float> height;
    private readonly List<Sample>[] samples = { new List<Sample>(), new List<Sample>() };
    private struct Sample { internal Vector3 vertex; internal BoneWeight weight; internal float depth; internal bool central; }
    private readonly Transform[][] skinBones = new Transform[2][];
    private readonly Matrix4x4[][] skinBind = new Matrix4x4[2][];
    private readonly Matrix4x4[][] skinMatrices = new Matrix4x4[2][];
    private readonly List<Sample>[,] fingerSamples = new List<Sample>[2, 5];
    private readonly List<int>[,] fingerMatrices = new List<int>[2, 5];
    private Action updateSleeves;

    internal SeatedPalmContact(Transform[] bones, Mesh mesh, Mesh fallback, Func<Vector3, float, float> height)
    {
        this.height = height;
        string[] names = { "Shoulder", "Elbow", "Wrist", "Hand", "Middle01", "Index01", "Pinky01" };
        string[] digits = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        for (int side = 0; side < 2; side++)
        {
            string suffix = side == 0 ? "_L" : "_R";
            for (int i = 0; i < names.Length; i++) joints[side, i] = Array.Find(bones, b => b.name == "joint_" + names[i] + suffix);
            for (int i = 0; i < digits.Length; i++)
            {
                fingerSamples[side, i] = new List<Sample>(); fingerMatrices[side, i] = new List<int>();
                for (int joint = 0; joint < 4; joint++)
                    fingers[side, i * 4 + joint] = Array.Find(bones, b => b.name == "joint_" + digits[i] + "0" + (joint + 1) + suffix);
            }
            if (mesh != null && joints[side, 6] != null) MeasurePalm(side, bones, mesh);
            if (thickness[side, 0] == 0 && fallback != null && joints[side, 6] != null) MeasurePalm(side, bones, fallback);
        }
    }

    private void MeasurePalm(int side, Transform[] bones, Mesh mesh)
        => SetMesh(side, bones, mesh, mesh, bones);

    internal void BindSleeves(Transform[] bones, Mesh source, Mesh prepared, Transform[] preparedBones, Action update)
    {
        updateSleeves = update;
        for (int side = 0; side < 2; side++) SetMesh(side, bones, source, prepared, preparedBones);
    }

    private void SetMesh(int side, Transform[] bones, Mesh mesh, Mesh prepared, Transform[] preparedBones)
    {
        var bind = mesh.bindposes;
        int handIndex = Array.IndexOf(bones, joints[side, 3]);
        Vector3 hand = bind[handIndex].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 middle = bind[Array.IndexOf(bones, joints[side, 4])].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 index = bind[Array.IndexOf(bones, joints[side, 5])].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 pinky = bind[Array.IndexOf(bones, joints[side, 6])].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 forward = (middle - hand).normalized, across = (index - pinky).normalized;
        Vector3 normal = Vector3.Cross(across, forward).normalized;
        float length = Vector3.Distance(hand, middle), width = Vector3.Distance(index, pinky);
        var points = mesh.vertices; var weights = mesh.boneWeights; var depths = new List<float>();
        var selected = new List<int>(); var unique = new HashSet<Vector3>();
        for (int i = 0; i < points.Length; i++)
        {
            BoneWeight w = weights[i];
            float amount = (w.boneIndex0 == handIndex ? w.weight0 : 0) + (w.boneIndex1 == handIndex ? w.weight1 : 0) +
                (w.boneIndex2 == handIndex ? w.weight2 : 0) + (w.boneIndex3 == handIndex ? w.weight3 : 0);
            Vector3 p = points[i] - hand;
            float along = Vector3.Dot(p, forward);
            float depth = Vector3.Dot(p, normal);
            if (amount > .25f && along > -.1f && along < length * 1.2f && Mathf.Abs(Vector3.Dot(p, across)) < width * .8f && Mathf.Abs(depth) < .045f && unique.Add(points[i]))
            { depths.Add(depth); selected.Add(i); }
        }
        if (depths.Count == 0) return;
        depths.Sort();
        thickness[side, 0] = Mathf.Clamp(-depths[0], .005f, .05f);
        thickness[side, 1] = Mathf.Clamp(depths[depths.Count - 1], .005f, .05f);
        samples[side].Clear();
        var finalPoints = prepared.vertices; var finalWeights = prepared.boneWeights;
        foreach (int i in selected) samples[side].Add(new Sample
        {
            vertex = finalPoints[i],
            weight = finalWeights[i],
            depth = Vector3.Dot(points[i] - hand, normal),
            central = Vector3.Dot(points[i] - hand, forward) > -length * .25f && Vector3.Dot(points[i] - hand, forward) < length * .95f && Mathf.Abs(Vector3.Dot(points[i] - hand, across)) < width * .45f
        });
        skinBones[side] = preparedBones; skinBind[side] = prepared.bindposes;
        skinMatrices[side] = new Matrix4x4[preparedBones.Length];
        for (int finger = 0; finger < 5; finger++)
        {
            var ids = new HashSet<int>(); for (int joint = 0; joint < 4; joint++) { int id = Array.IndexOf(bones, fingers[side, finger * 4 + joint]); if (id >= 0) ids.Add(id); }
            var cells = new HashSet<Vector3Int>(); var affected = new HashSet<int>();
            fingerSamples[side, finger].Clear(); fingerMatrices[side, finger].Clear();
            for (int i = 0; i < points.Length; i++)
            {
                BoneWeight w = weights[i]; float amount = (ids.Contains(w.boneIndex0) ? w.weight0 : 0) + (ids.Contains(w.boneIndex1) ? w.weight1 : 0) + (ids.Contains(w.boneIndex2) ? w.weight2 : 0) + (ids.Contains(w.boneIndex3) ? w.weight3 : 0);
                if (amount < .65f || !cells.Add(Vector3Int.RoundToInt(points[i] / .006f))) continue;
                w = finalWeights[i]; fingerSamples[side, finger].Add(new Sample { vertex = finalPoints[i], weight = w });
                affected.Add(w.boneIndex0); affected.Add(w.boneIndex1); affected.Add(w.boneIndex2); affected.Add(w.boneIndex3);
            }
            fingerMatrices[side, finger].AddRange(affected);
        }
    }

    internal void Reset(int side) { tilt[side] = Quaternion.identity; lifts[side] = 0; activation[side] = 0; active[side] = false; }

    private void UpdateMatrices(int side)
    {
        updateSleeves?.Invoke();
        var matrices = skinMatrices[side];
        for (int i = 0; i < matrices.Length; i++) matrices[i] = skinBones[side][i].localToWorldMatrix * skinBind[side][i];
    }

    private Vector3 Position(int side, Sample sample)
    {
        var matrices = skinMatrices[side]; var w = sample.weight; var v = sample.vertex;
        Vector3 position = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0;
        if (w.weight1 > 0) position += matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1;
        if (w.weight2 > 0) position += matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2;
        if (w.weight3 > 0) position += matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
        return position;
    }

    internal bool Apply(int side, Transform reference, float seated, float dt, float previousLift)
    {
        if (thickness[side, 0] == 0) return false;
        Transform shoulder = joints[side, 0], elbow = joints[side, 1], hand = joints[side, 3], middle = joints[side, 4];
        float scale = reference.lossyScale.x;
        Vector3 nativeCenter = Vector3.Lerp(hand.position, middle.position, .5f), anchor = hand.position;
        Vector3 forward = (middle.position - hand.position).normalized;
        Vector3 normal = Vector3.Cross(joints[side, 5].position - joints[side, 6].position, forward).normalized;
        bool flip = normal.y < 0; if (flip) normal = -normal;
        UpdateMatrices(side);
        normal = PalmNormal(side, hand.position, forward, normal, flip);
        Vector3 heading = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (heading.sqrMagnitude < .5f) { Reset(side); return false; }
        Vector3 lateral = Vector3.Cross(Vector3.up, heading);
        float sum = 0, sx = 0, sz = 0, xx = 0, zz = 0, xz = 0, hx = 0, hz = 0;
        int count = 0;
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            float px = x * .04f * scale, pz = z * .035f * scale;
            float h = height(anchor + lateral * px + heading * pz, .004f * scale);
            if (float.IsNegativeInfinity(h) || Mathf.Abs(h - nativeCenter.y) > .35f * scale) continue;
            count++; sum += h; sx += px; sz += pz; xx += px * px; zz += pz * pz; xz += px * pz; hx += h * px; hz += h * pz;
        }
        if (count < 4) { Reset(side); return false; }
        float average = sum / count, pad = (thickness[side, flip ? 1 : 0] + .002f) * scale;
        float contact = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f * scale, .015f * scale, nativeCenter.y - average - pad));
        if (contact <= 0) { Reset(side); return false; }
        if (!active[side]) { lifts[side] = previousLift; active[side] = true; }
        float gain = 1 - Mathf.Exp(-dt * 18);
        activation[side] = Mathf.Lerp(activation[side], contact, gain);
        xx -= sx * sx / count; zz -= sz * sz / count; xz -= sx * sz / count;
        hx -= average * sx; hz -= average * sz;
        float determinant = xx * zz - xz * xz;
        Vector3 targetNormal = Vector3.up;
        if (Mathf.Abs(determinant) > 1e-12f)
            targetNormal = (Vector3.up - lateral * ((hx * zz - hz * xz) / determinant) - heading * ((hz * xx - hx * xz) / determinant)).normalized;
        targetNormal = Vector3.RotateTowards(Vector3.up, targetNormal, 60f * Mathf.Deg2Rad, 0);
        Quaternion wanted = Quaternion.LookRotation(Vector3.ProjectOnPlane(heading, targetNormal), targetNormal) *
            Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, normal), normal));
        wanted = Quaternion.RotateTowards(Quaternion.identity, wanted, 75f * contact * Mathf.SmoothStep(0, 1, seated / .7f));
        Quaternion frame = reference.rotation;
        tilt[side] = Quaternion.Slerp(tilt[side], Quaternion.Inverse(frame) * wanted * frame, gain);
        hand.rotation = frame * tilt[side] * Quaternion.Inverse(frame) * hand.rotation;
        // The native hand joint is the support anchor. Retarget that anchor to
        // the center of this skin's longer palm instead of leaving it at the edge.
        Vector3 target = Vector3.Lerp(nativeCenter, anchor, activation[side]);
        Vector3 shift = target - Vector3.Lerp(hand.position, middle.position, .5f); shift.y = 0;
        SeatedHandClearance.MoveWrist(shoulder, elbow, hand, hand.position + shift);
        UpdateMatrices(side);
        float lift = float.NegativeInfinity;
        foreach (var sample in samples[side])
        {
            Vector3 p = Position(side, sample);
            float h = height(p, 0);
            if (!float.IsNegativeInfinity(h)) lift = Mathf.Max(lift, h + .0008f * scale - p.y);
        }
        if (float.IsNegativeInfinity(lift)) lift = 0;
        lift = Mathf.Clamp(lift, -.08f * scale * contact, .32f * scale);
        lifts[side] = Mathf.Max(lift, Mathf.Lerp(lifts[side], lift, gain));
        SeatedHandClearance.MoveWrist(shoulder, elbow, hand, hand.position + Vector3.up * lifts[side]);
        for (int finger = 0; finger < 5; finger++) ClearFinger(side, finger, scale, contact);
        return true;
    }

    internal void Settle(int side, Transform reference)
    {
        if (!active[side]) return;
        float scale = reference.lossyScale.x;
        for (int iteration = 0; iteration < 2; iteration++)
        {
            UpdateMatrices(side);
            float overlap = 0;
            foreach (var sample in samples[side]) overlap = Mathf.Max(overlap, Overlap(side, sample, scale));
            for (int finger = 0; finger < 5; finger++) foreach (var sample in fingerSamples[side, finger])
                overlap = Mathf.Max(overlap, Overlap(side, sample, scale));
            if (overlap < .0001f * scale) break;
            overlap = Mathf.Min(overlap, .08f * scale);
            SeatedHandClearance.MoveWrist(joints[side, 0], joints[side, 1], joints[side, 3], joints[side, 3].position + Vector3.up * overlap);
            lifts[side] += overlap;
        }
    }

    private float Overlap(int side, Sample sample, float scale)
    {
        Vector3 p = Position(side, sample);
        return height(p, 0) + .0008f * scale - p.y;
    }

    private Vector3 PalmNormal(int side, Vector3 origin, Vector3 forward, Vector3 normal, bool flip)
    {
        Vector3 across = Vector3.Cross(normal, forward).normalized;
        forward = Vector3.Cross(across, normal).normalized;
        float sx = 0, sz = 0, sy = 0, xx = 0, zz = 0, xz = 0, xy = 0, zy = 0; int count = 0;
        foreach (var sample in samples[side])
        {
            if (!sample.central || (flip ? sample.depth < 0 : sample.depth > 0)) continue;
            Vector3 p = Position(side, sample) - origin;
            float x = Vector3.Dot(p, across), z = Vector3.Dot(p, forward), y = Vector3.Dot(p, normal);
            count++; sx += x; sz += z; sy += y; xx += x * x; zz += z * z; xz += x * z; xy += x * y; zy += z * y;
        }
        if (count < 4) return normal;
        xx -= sx * sx / count; zz -= sz * sz / count; xz -= sx * sz / count; xy -= sx * sy / count; zy -= sz * sy / count;
        float determinant = xx * zz - xz * xz;
        if (Mathf.Abs(determinant) < 1e-12f) return normal;
        Vector3 fitted = (normal - across * ((xy * zz - zy * xz) / determinant) - forward * ((zy * xx - xy * xz) / determinant)).normalized;
        return Vector3.RotateTowards(normal, fitted, 25f * Mathf.Deg2Rad, 0);
    }

    private void ClearFinger(int side, int finger, float scale, float contact)
    {
        Transform first = fingers[side, finger * 4], tip = fingers[side, finger * 4 + 3] ?? fingers[side, finger * 4 + 2];
        if (first == null || tip == null) return;
        Vector3 axis = Vector3.Cross(tip.position - first.position, Vector3.up).normalized;
        if (axis.sqrMagnitude < .5f) return;
        Quaternion start = first.rotation;
        float low = 0, high = 75 * contact;
        if (FingerOverlap(side, finger, scale) <= 0) return;
        for (int i = 0; i < 6; i++)
        {
            float angle = (low + high) * .5f;
            first.rotation = Quaternion.AngleAxis(angle, axis) * start;
            if (FingerOverlap(side, finger, scale) > 0) low = angle; else high = angle;
        }
        first.rotation = Quaternion.AngleAxis(high, axis) * start;
    }

    private float FingerOverlap(int side, int finger, float scale)
    {
        foreach (int id in fingerMatrices[side, finger]) skinMatrices[side][id] = skinBones[side][id].localToWorldMatrix * skinBind[side][id];
        float overlap = float.NegativeInfinity;
        foreach (var sample in fingerSamples[side, finger])
        {
            Vector3 point = Position(side, sample);
            overlap = Mathf.Max(overlap, height(point, 0) + .0025f * scale - point.y);
        }
        return overlap;
    }
}
