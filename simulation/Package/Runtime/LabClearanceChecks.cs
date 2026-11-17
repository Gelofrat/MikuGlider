using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MikuGlider;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Horse.MikuPhysicsLab
{
    internal static class LabClearanceChecks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("-labClearanceChecks")) return;
            try
            {
                CheckWidth();
                CheckArmRestoration();
                CheckBroadphase();
                CheckWeights();
                Debug.Log("CLEARANCE_CHECKS_PASS");
            }
            catch (Exception error)
            {
                Debug.LogError("CLEARANCE_CHECKS_FAIL " + error);
            }
        }

        private static void CheckWidth()
        {
            var cone = new GarmentWidthProfile(new[] { new Vector3(-.2f, 1, 0), new Vector3(.2f, 1, 0),
                new Vector3(-.4f, 0, 0), new Vector3(.4f, 0, 0) }, new[] { 0, 2, 1, 1, 2, 3 });
            for (int i = 0; i <= 10000; i++)
            {
                float height = i / 10000f;
                if (Mathf.Abs(cone.AtHeight(height) - (.4f - .2f * height)) > .000001f)
                    throw new InvalidOperationException("Inaccurate triangle cross-section at " + height);
            }
            Mesh skirt = LabAssetLibrary.Get("v3").GetMesh("skirt");
            var profile = new GarmentWidthProfile(skirt.vertices, skirt.triangles);
            float previous = profile.AtHeight(1.214f), maximum = 0;
            for (int i = 1; i <= 2000; i++)
            {
                float width = profile.AtHeight(1.214f + i * .00001f);
                maximum = Mathf.Max(maximum, Mathf.Abs(width - previous));
                previous = width;
            }
            if (maximum > .001f) throw new InvalidOperationException("Discontinuous v3 idle clearance " + maximum);
            Debug.Log("CLEARANCE_WIDTH_PASS analytic=10001 idleSamples=2001 maximumDelta=" + maximum);
        }

        private static void CheckArmRestoration()
        {
            var root = new GameObject("Clearance stability test");
            Mesh skirt = LabAssetLibrary.Get("v3").GetMesh("skirt");
            var bones = new Transform[4];
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1 : 1;
                Transform shoulder = new GameObject("joint_Shoulder" + (side == 0 ? "_L" : "_R")).transform;
                shoulder.SetParent(root.transform, false);
                shoulder.localPosition = new Vector3(sign * .15f, 1.8f, 0);
                Transform wrist = new GameObject("joint_Wrist" + (side == 0 ? "_L" : "_R")).transform;
                wrist.SetParent(shoulder, false);
                wrist.localPosition = new Vector3(sign * .11f, -.58f, .14f);
                bones[side * 2] = shoulder;
                bones[side * 2 + 1] = wrist;
            }
            var correction = new GarmentArmClearance(bones, skirt);
            var previous = new Vector3[2];
            float drift = 0;
            for (int frame = 0; frame < 1000; frame++)
            {
                correction.Restore();
                for (int side = 0; side < 2; side++)
                    if (Quaternion.Angle(bones[side * 2].localRotation, Quaternion.identity) > .001f)
                        throw new InvalidOperationException("Correction accumulation at frame " + frame);
                correction.Apply(Matrix4x4.identity, 1f / 90f);
                for (int side = 0; side < 2; side++)
                {
                    Vector3 wrist = bones[side * 2 + 1].position;
                    if (frame > 100) drift = Mathf.Max(drift, Vector3.Distance(previous[side], wrist));
                    previous[side] = wrist;
                }
            }
            UnityEngine.Object.Destroy(root);
            if (drift > .00001f) throw new InvalidOperationException("Static arm oscillation " + drift);
            Debug.Log("CLEARANCE_STATIC_ARM_PASS frames=1000 maximumDrift=" + drift);
        }

        private static void CheckBroadphase()
        {
            var random = new System.Random(11011);
            var points = new Vector3[512];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(Next(random, -.13f, .23f), Next(random, -.4f, 0), Next(random, -.13f, .13f));
            var set = new GarmentProbeSet(points);
            var radii = new[] { .14f, .13f, .12f, .1f, .085f, .09f };
            var left = new GarmentCollisionCapsule[10000];
            var right = new GarmentCollisionCapsule[left.Length];
            var expected = new bool[left.Length];
            for (int i = 0; i < left.Length; i++)
            {
                left[i] = RandomCapsule(random, radii);
                right[i] = RandomCapsule(random, radii);
            }
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < left.Length; i++)
                foreach (Vector3 point in points)
                    if (left[i].Contains(point) || right[i].Contains(point)) { expected[i] = true; break; }
            double full = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            for (int i = 0; i < left.Length; i++)
                if (set.Intersects(left[i], right[i]) != expected[i])
                    throw new InvalidOperationException("Broadphase lost a contact at " + i);
            Debug.Log("CLEARANCE_BROADPHASE_PASS cases=" + left.Length + " fullMs=" + full +
                " groupedMs=" + clock.Elapsed.TotalMilliseconds);
        }

        private static GarmentCollisionCapsule RandomCapsule(System.Random random, float[] radii)
        {
            Vector3 start = new Vector3(Next(random, -.5f, .5f), Next(random, -.5f, .4f), Next(random, -.5f, .5f));
            Vector3 direction = new Vector3(Next(random, -1, 1), Next(random, -1, 1), Next(random, -1, 1)).normalized;
            return new GarmentCollisionCapsule(start, direction, Next(random, .15f, .5f), radii, .14f);
        }

        private static void CheckWeights()
        {
            var random = new System.Random(1112);
            var source = new BoneWeight[10000];
            var fractions = new float[source.Length];
            var expected = new BoneWeight[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                float a = random.Next(0, 9), b = random.Next(0, 9), c = random.Next(0, 9), d = random.Next(0, 9);
                if (a + b + c + d <= 0) a = 1;
                float total = a + b + c + d;
                source[i] = new BoneWeight
                {
                    boneIndex0 = random.Next(0, 8), weight0 = a / total,
                    boneIndex1 = random.Next(0, 8), weight1 = b / total,
                    boneIndex2 = random.Next(0, 8), weight2 = c / total,
                    boneIndex3 = random.Next(0, 8), weight3 = d / total
                };
                fractions[i] = i % 4 == 0 ? 0 : i % 4 == 1 ? 1 : i % 4 == 2 ? .5f : (float)random.NextDouble();
            }
            long oldAllocation = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < source.Length; i++) expected[i] = BaselineWeights(source[i], 1, 2, 3, 9, fractions[i]);
            oldAllocation = GC.GetAllocatedBytesForCurrentThread() - oldAllocation;
            long newAllocation = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < source.Length; i++)
            {
                BoneWeight actual = SkinWeightBlend.Replace(source[i], 1, 2, 3, 9, fractions[i]);
                if (!SameWeights(expected[i], actual)) throw new InvalidOperationException("Weight mismatch " + i);
            }
            newAllocation = GC.GetAllocatedBytesForCurrentThread() - newAllocation;
            for (int i = 0; i < source.Length; i++)
                if (!SameWeights(BaselineWeights(source[i], 3, 5, 7, 17, fractions[i]),
                    SkinWeightBlend.Replace(source[i], 3, 5, 7, 17, fractions[i])))
                    throw new InvalidOperationException("Sleeve weight mismatch " + i);
            if (newAllocation != 0) throw new InvalidOperationException("Weight helper allocated " + newAllocation);
            Debug.Log("CLEARANCE_WEIGHT_PASS cases=20000 baselineBytes=" + oldAllocation + " optimizedBytes=" + newAllocation);
        }

        private static bool SameWeights(BoneWeight a, BoneWeight b) =>
            a.boneIndex0 == b.boneIndex0 && a.boneIndex1 == b.boneIndex1 &&
            a.boneIndex2 == b.boneIndex2 && a.boneIndex3 == b.boneIndex3 &&
            a.weight0 == b.weight0 && a.weight1 == b.weight1 && a.weight2 == b.weight2 && a.weight3 == b.weight3;

        private static BoneWeight BaselineWeights(BoneWeight source, int first, int second, int third, int joint, float fraction)
        {
            int[] indices = { source.boneIndex0, source.boneIndex1, source.boneIndex2, source.boneIndex3 };
            float[] values = { source.weight0, source.weight1, source.weight2, source.weight3 };
            var remaining = new List<KeyValuePair<int, float>>(6);
            float amount = 0;
            for (int i = 0; i < 4; i++)
                if (indices[i] == first || indices[i] == second || indices[i] == third) amount += values[i];
                else if (values[i] > 0) remaining.Add(new KeyValuePair<int, float>(indices[i], values[i]));
            if (amount <= 0) return source;
            remaining.Add(new KeyValuePair<int, float>(joint, amount * (1 - fraction)));
            remaining.Add(new KeyValuePair<int, float>(joint + 1, amount * fraction));
            remaining.Sort((a, b) => b.Value.CompareTo(a.Value));
            while (remaining.Count < 4) remaining.Add(new KeyValuePair<int, float>(0, 0));
            float total = remaining[0].Value + remaining[1].Value + remaining[2].Value + remaining[3].Value;
            return new BoneWeight
            {
                boneIndex0 = remaining[0].Key, weight0 = remaining[0].Value / total,
                boneIndex1 = remaining[1].Key, weight1 = remaining[1].Value / total,
                boneIndex2 = remaining[2].Key, weight2 = remaining[2].Value / total,
                boneIndex3 = remaining[3].Key, weight3 = remaining[3].Value / total
            };
        }

        private static float Next(System.Random random, float minimum, float maximum) =>
            minimum + (maximum - minimum) * (float)random.NextDouble();
    }
}
