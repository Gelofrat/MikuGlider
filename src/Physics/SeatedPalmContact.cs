using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

// Fit the visible hand to the lap after the native animation and grip layers.
internal sealed class SeatedPalmContact
{
    // Finger probes represent small patches of the skinned mesh. Leave room
    // between these probes for the surface of a fingertip crossing a cloth fold.
    private const float PalmPadding = .0008f, FingerPadding = .004f;
    private readonly HandBones[] hands;
    private readonly float[,] thickness = new float[2, 2];
    private readonly Quaternion[] tilt = { Quaternion.identity, Quaternion.identity };
    private readonly float[] lifts = new float[2];
    private readonly float[] activation = new float[2];
    private readonly float[] supportPitch = new float[2];
    private readonly float[] supportRoll = new float[2];
    private readonly int[] palmSupportCount = new int[2];
    private readonly Vector3[][] supportPoints = new Vector3[2][];
    private readonly bool[] active = new bool[2];
    private readonly Func<Vector3, float, float> height;
    private readonly List<Sample>[] samples = { new List<Sample>(), new List<Sample>() };
    private struct Sample { internal Vector3 vertex; internal BoneWeight weight; internal float depth; internal bool central, palmSupport; }
    private readonly Transform[][] skinBones = new Transform[2][];
    private readonly Matrix4x4[][] skinBind = new Matrix4x4[2][];
    private readonly Matrix4x4[][] skinMatrices = new Matrix4x4[2][];
    private readonly int[][] sampledBones = new int[2][];
    private readonly List<Sample>[,] fingerSamples = new List<Sample>[2, 5];
    private Action updateSleeves;

    private sealed class HandBones
    {
        internal readonly Transform Shoulder, Elbow, Hand;
        internal readonly Transform IndexBase, MiddleBase, PinkyBase;
        internal readonly Transform[][] Fingers;

        internal HandBones(Transform[] bones, string suffix)
        {
            Transform Find(string name) => CharacterBodyBones.Find(bones, "joint_" + name + suffix);
            Transform[] Finger(string name) =>
                new[] { Find(name + "01"), Find(name + "02"), Find(name + "03"), Find(name + "04") };

            Shoulder = Find("Shoulder");
            Elbow = Find("Elbow");
            Hand = Find("Hand");
            Transform[] index = Finger("Index");
            Transform[] middle = Finger("Middle");
            Transform[] pinky = Finger("Pinky");
            IndexBase = index[0];
            MiddleBase = middle[0];
            PinkyBase = pinky[0];
            Fingers = new[] { Finger("Thumb"), index, middle, Finger("Ring"), pinky };
        }
    }

    internal SeatedPalmContact(Transform[] bones, Mesh mesh, Mesh fallback, Func<Vector3, float, float> height)
    {
        this.height = height;
        hands = new[] { new HandBones(bones, "_L"), new HandBones(bones, "_R") };
        for (int side = 0; side < hands.Length; side++)
        {
            for (int finger = 0; finger < hands[side].Fingers.Length; finger++)
                fingerSamples[side, finger] = new List<Sample>();
            if (mesh != null) MeasurePalm(side, bones, mesh);
            if (thickness[side, 0] == 0 && fallback != null) MeasurePalm(side, bones, fallback);
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
        HandBones joints = hands[side];
        if (joints.Hand == null || joints.MiddleBase == null || joints.IndexBase == null || joints.PinkyBase == null)
            return;
        var bind = mesh.bindposes;
        int handIndex = Array.IndexOf(bones, joints.Hand);
        Vector3 hand = bind[handIndex].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 middle = bind[Array.IndexOf(bones, joints.MiddleBase)].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 index = bind[Array.IndexOf(bones, joints.IndexBase)].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 pinky = bind[Array.IndexOf(bones, joints.PinkyBase)].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 forward = (middle - hand).normalized, across = (index - pinky).normalized;
        Vector3 normal = Vector3.Cross(across, forward).normalized;
        float length = Vector3.Distance(hand, middle), width = Vector3.Distance(index, pinky);
        var points = mesh.vertices; var weights = mesh.boneWeights; var depths = new List<float>();
        var selected = new List<int>(); var unique = new HashSet<Vector3>();
        var palmVertices = new bool[points.Length];
        Vector3 palmAcross = Vector3.Cross(forward, normal).normalized;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 offset = points[i] - hand;
            float along = Vector3.Dot(offset, forward);
            palmVertices[i] = weights[i].boneIndex0 == handIndex && along >= -length * .1f && along <= length * 1.3f &&
                Mathf.Abs(Vector3.Dot(offset, palmAcross)) <= width * .82f && Mathf.Abs(Vector3.Dot(offset, normal)) <= width * .5f;
        }
        // Cuffs and isolated hand-weighted vertices can touch the lap while the
        // palm still hovers. Use complete palm faces as the contact target.
        var palmSurface = new HashSet<Vector3>();
        var faces = mesh.triangles;
        for (int face = 0; face < faces.Length; face += 3)
        {
            int a = faces[face], b = faces[face + 1], c = faces[face + 2];
            if (!palmVertices[a] || !palmVertices[b] || !palmVertices[c]) continue;
            palmSurface.Add(points[a]);
            palmSurface.Add(points[b]);
            palmSurface.Add(points[c]);
        }
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
            palmSupport = palmSurface.Contains(points[i]),
            central = Vector3.Dot(points[i] - hand, forward) > -length * .25f && Vector3.Dot(points[i] - hand, forward) < length * .95f && Mathf.Abs(Vector3.Dot(points[i] - hand, across)) < width * .45f
        });
        samples[side].Sort((a, b) => b.palmSupport.CompareTo(a.palmSupport));
        int support = samples[side].FindIndex(s => !s.palmSupport);
        palmSupportCount[side] = support > 0 ? support : samples[side].Count;
        skinBones[side] = preparedBones; skinBind[side] = prepared.bindposes;
        skinMatrices[side] = new Matrix4x4[preparedBones.Length];
        for (int finger = 0; finger < 5; finger++)
        {
            var ids = new HashSet<int>();
            foreach (Transform joint in joints.Fingers[finger])
            {
                if (joint == null) continue;
                int id = Array.IndexOf(bones, joint);
                if (id >= 0) ids.Add(id);
            }
            var cells = new HashSet<Vector3Int>();
            fingerSamples[side, finger].Clear();
            for (int i = 0; i < points.Length; i++)
            {
                BoneWeight w = weights[i]; float amount = (ids.Contains(w.boneIndex0) ? w.weight0 : 0) + (ids.Contains(w.boneIndex1) ? w.weight1 : 0) + (ids.Contains(w.boneIndex2) ? w.weight2 : 0) + (ids.Contains(w.boneIndex3) ? w.weight3 : 0);
                if (amount < .5f || !cells.Add(Vector3Int.RoundToInt(points[i] / .003f))) continue;
                w = finalWeights[i]; fingerSamples[side, finger].Add(new Sample { vertex = finalPoints[i], weight = w });
            }
        }
        int count = samples[side].Count;
        for (int finger = 0; finger < 5; finger++) count += fingerSamples[side, finger].Count;
        supportPoints[side] = new Vector3[count];
        var usedBones = new HashSet<int>();
        void Include(Sample sample)
        {
            BoneWeight w = sample.weight;
            usedBones.Add(w.boneIndex0);
            if (w.weight1 > 0) usedBones.Add(w.boneIndex1);
            if (w.weight2 > 0) usedBones.Add(w.boneIndex2);
            if (w.weight3 > 0) usedBones.Add(w.boneIndex3);
        }
        foreach (var sample in samples[side]) Include(sample);
        for (int finger = 0; finger < 5; finger++)
            foreach (var sample in fingerSamples[side, finger]) Include(sample);
        sampledBones[side] = new int[usedBones.Count];
        usedBones.CopyTo(sampledBones[side]);
    }

    internal void Release(int side)
    {
        tilt[side] = Quaternion.identity;
        lifts[side] = activation[side] = 0;
        active[side] = false;
        supportPitch[side] = supportRoll[side] = 0;
    }

    private void UpdateMatrices(int side)
    {
        updateSleeves?.Invoke();
        var matrices = skinMatrices[side];
        foreach (int i in sampledBones[side]) matrices[i] = skinBones[side][i].localToWorldMatrix * skinBind[side][i];
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
        HandBones joints = hands[side];
        Transform hand = joints.Hand;
        float scale = reference.lossyScale.x;
        Vector3 nativeCenter = Vector3.Lerp(hand.position, joints.MiddleBase.position, .5f), anchor = hand.position;
        Vector3 forward = (joints.MiddleBase.position - hand.position).normalized;
        Vector3 normal = Vector3.Cross(joints.IndexBase.position - joints.PinkyBase.position, forward).normalized;
        bool flip = normal.y < 0; if (flip) normal = -normal;
        UpdateMatrices(side);
        normal = PalmNormal(side, hand.position, forward, normal, flip);
        Vector3 heading = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (heading.sqrMagnitude < .5f) { Release(side); return false; }
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
        if (count < 4) { Release(side); return false; }
        float average = sum / count, pad = (thickness[side, flip ? 1 : 0] + .002f) * scale;
        float contact = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f * scale, .015f * scale, nativeCenter.y - average - pad));
        if (contact <= 0) { Release(side); return false; }
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
        Quaternion easedTilt = Quaternion.Slerp(tilt[side], Quaternion.Inverse(frame) * wanted * frame, gain);
        tilt[side] = Quaternion.RotateTowards(tilt[side], easedTilt, Mathf.Lerp(240f, 16f, activation[side]) * dt);
        hand.rotation = frame * tilt[side] * Quaternion.Inverse(frame) * hand.rotation;
        // The native hand joint is the support anchor. Retarget that anchor to
        // the center of this skin's longer palm instead of leaving it at the edge.
        Vector3 target = Vector3.Lerp(nativeCenter, anchor, activation[side]);
        Vector3 shift = target - Vector3.Lerp(hand.position, joints.MiddleBase.position, .5f); shift.y = 0;
        SeatedHandClearance.MoveWrist(joints.Shoulder, joints.Elbow, hand, hand.position + shift);
        UpdateMatrices(side);
        FitHandSupport(side, hand, heading, scale, contact, dt);
        UpdateMatrices(side);
        float lift = float.NegativeInfinity;
        foreach (var sample in samples[side])
        {
            Vector3 p = Position(side, sample);
            float h = height(p, 0);
            if (!float.IsNegativeInfinity(h)) lift = Mathf.Max(lift, h + PalmPadding * scale - p.y);
        }
        if (float.IsNegativeInfinity(lift)) lift = 0;
        lift = Mathf.Clamp(lift, -.08f * scale * contact, .32f * scale);
        lifts[side] = Mathf.Max(lift, Mathf.Lerp(lifts[side], lift, gain));
        SeatedHandClearance.MoveWrist(joints.Shoulder, joints.Elbow, hand, hand.position + Vector3.up * lifts[side]);
        return true;
    }

    private void FitHandSupport(int side, Transform hand, Vector3 heading, float scale, float contact, float dt)
    {
        // Test a rigid rotation around the wrist. The palm and all five digits
        // move together, preserving the finger spacing authored in the clip.
        Vector3 pivot = hand.position;
        var points = supportPoints[side];
        int count = 0;
        foreach (var sample in samples[side]) points[count++] = Position(side, sample) - pivot;
        for (int finger = 0; finger < 5; finger++)
            foreach (var sample in fingerSamples[side, finger]) points[count++] = Position(side, sample) - pivot;

        Vector3 axis = Vector3.Cross(heading, Vector3.up);
        float tolerance = .0015f * scale;
        float currentGap = float.PositiveInfinity;
        if (activation[side] > .8f)
        {
            Quaternion current = Quaternion.AngleAxis(supportRoll[side], heading) * Quaternion.AngleAxis(supportPitch[side], axis);
            currentGap = SupportGap(side, pivot, current, scale);
            if (currentGap <= tolerance)
            {
                hand.rotation = current * hand.rotation;
                return;
            }
        }
        float wantedPitch = 0, wantedRoll = 0;
        float best = SupportGap(side, pivot, Quaternion.identity, scale);
        if (best > tolerance && !float.IsPositiveInfinity(best))
        {
            float step = 15f * contact;
            for (int i = 1; i <= 4; i++)
            {
                float angle = step * i;
                float gap = SupportGap(side, pivot, Quaternion.AngleAxis(angle, axis), scale);
                if (gap >= best) continue;
                best = gap;
                wantedPitch = angle;
                if (gap <= tolerance)
                {
                    float low = angle - step;
                    for (int j = 0; j < 4; j++)
                    {
                        float mid = (low + wantedPitch) * .5f;
                        if (SupportGap(side, pivot, Quaternion.AngleAxis(mid, axis), scale) > tolerance) low = mid;
                        else wantedPitch = mid;
                    }
                    break;
                }
            }
            // A sideways fold can support the fingertips before the palm. Allow
            // a little roll toward either edge while keeping the digits together.
            Quaternion pitch = Quaternion.AngleAxis(wantedPitch, axis);
            for (int stepIndex = 1; best > tolerance && stepIndex <= 2; stepIndex++)
            {
                float angle = stepIndex * 9f * contact;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float roll = sign * angle;
                    float gap = SupportGap(side, pivot, Quaternion.AngleAxis(roll, heading) * pitch, scale);
                    if (gap + .0005f * scale >= best) continue;
                    best = gap;
                    wantedRoll = roll;
                }
            }
        }
        // Neighbouring folds may favour opposite palm edges. Keep the previous
        // fit when the height improvement is smaller than the cloth tolerance.
        if (!float.IsPositiveInfinity(currentGap) && currentGap <= best + tolerance)
        {
            wantedPitch = supportPitch[side];
            wantedRoll = supportRoll[side];
        }
        float gain = 1f - Mathf.Exp(-dt * 16f);
        float maxStep = Mathf.Lerp(200f, 22f, activation[side]) * dt;
        Vector2 currentAngles = new Vector2(supportPitch[side], supportRoll[side]);
        Vector2 eased = Vector2.Lerp(currentAngles, new Vector2(wantedPitch, wantedRoll), gain);
        Vector2 angles = Vector2.MoveTowards(currentAngles, eased, maxStep);
        supportPitch[side] = angles.x;
        supportRoll[side] = angles.y;
        hand.rotation = Quaternion.AngleAxis(supportRoll[side], heading) * Quaternion.AngleAxis(supportPitch[side], axis) * hand.rotation;
    }

    private float SupportGap(int side, Vector3 pivot, Quaternion rotation, float scale)
    {
        Vector3[] points = supportPoints[side];
        float palm = float.NegativeInfinity, obstacle = float.NegativeInfinity;
        int support = palmSupportCount[side], palmCount = samples[side].Count;
        var pose = Matrix4x4.TRS(pivot, rotation, Vector3.one);
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 point = pose.MultiplyPoint3x4(points[i]);
            float lift = height(point, 0) - point.y;
            if (i < support) palm = Mathf.Max(palm, lift);
            obstacle = Mathf.Max(obstacle, i < palmCount ? lift : lift + (FingerPadding - PalmPadding) * scale);
        }
        return float.IsNegativeInfinity(palm) ? float.PositiveInfinity : obstacle - palm;
    }

    internal void Settle(int side, Transform reference)
    {
        if (!active[side]) return;
        HandBones joints = hands[side];
        float scale = reference.lossyScale.x;
        for (int iteration = 0; iteration < 2; iteration++)
        {
            UpdateMatrices(side);
            float overlap = 0;
            foreach (var sample in samples[side]) overlap = Mathf.Max(overlap, Overlap(side, sample, PalmPadding * scale));
            for (int finger = 0; finger < 5; finger++) foreach (var sample in fingerSamples[side, finger])
                overlap = Mathf.Max(overlap, Overlap(side, sample, FingerPadding * scale));
            if (overlap < .0001f * scale) break;
            overlap = Mathf.Min(overlap, .08f * scale);
            SeatedHandClearance.MoveWrist(joints.Shoulder, joints.Elbow, joints.Hand, joints.Hand.position + Vector3.up * overlap);
            lifts[side] += overlap;
        }
    }

    private float Overlap(int side, Sample sample, float padding)
    {
        Vector3 p = Position(side, sample);
        return height(p, 0) + padding - p.y;
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
}
