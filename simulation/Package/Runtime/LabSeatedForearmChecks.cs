using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MikuGlider;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal static class LabSeatedForearmChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable] private sealed class CaseReport
        {
            public string name;
            public bool passed;
            public string error;
            public float requestedLiftBefore;
            public float requestedLiftAfter;
            public float wristDisplacement;
            public float wristRotation;
            public float forearmWristDisplacement;
            public float forearmWristRotation;
            public float elbowDisplacement;
            public float repeatDrift;
            public float restorationError;
            public float supportHandDisplacement;
            public float standingCorrection;
            public float elbowAngle;
            public float segmentLengthError;
            public float transitionStep;
            public float transitionRestorationError;
            public float frameRateDifference;
        }

        [Serializable] private sealed class Report
        {
            public bool passed;
            public CaseReport[] cases;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("-labSeatedForearmChecks")) return;
            var cases = new List<CaseReport>();
            foreach (float scale in new[] { .5f, 1f, 2f })
                foreach (float yaw in new[] { 0f, 37f, 90f, 180f, 270f })
                {
                    cases.Add(RunCase("reference", scale, new Vector3(0, yaw, 0), new Vector3(.22f, .85f, .25f), true));
                    cases.Add(RunCase("deep bend", scale, new Vector3(0, yaw, 0), new Vector3(.34f, .88f, .12f), false));
                    cases.Add(RunCase("near straight", scale, new Vector3(0, yaw, 0), new Vector3(.06f, .88f, .235f), false));
                }
            foreach (float scale in new[] { .5f, 1f, 2f })
                foreach (Vector3 orientation in new[] { new Vector3(10, 37, 15), new Vector3(-15, 140, -10) })
                    cases.Add(RunCase("tilted", scale, orientation, new Vector3(.22f, .85f, .25f), false));
            var report = new Report { cases = cases.ToArray(), passed = cases.All(value => value.passed) };
            string folder = LabController.Argument("-labReports") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "seated-forearm-checks.json"), JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("SEATED_FOREARM_CHECKS_PASS " + cases.Count + " cases");
            else Debug.LogError("SEATED_FOREARM_CHECKS_FAIL " + string.Join("; ", cases.Where(value => !value.passed).Select(value => value.name + ": " + value.error)));
        }

        private static CaseReport RunCase(string name, float scale, Vector3 orientation, Vector3 elbowPoint, bool requireClearance)
        {
            var report = new CaseReport { name = name + "/" + scale + "/" + orientation };
            GameObject root = new GameObject("Seated forearm regression");
            var surface = new Mesh { name = "Short coat hem contact" };
            try
            {
                Transform shoulder = Bone(root.transform, "joint_Shoulder_L", new Vector3(0, 1, 0));
                Transform elbow = Bone(shoulder, "joint_Elbow_L", elbowPoint);
                Transform wrist = Bone(elbow, "joint_Wrist_L", new Vector3(.10f, .75f, .50f));
                Transform hand = Bone(wrist, "joint_Hand_L", new Vector3(.10f, .75f, .56f));
                Transform middle = Bone(hand, "joint_Middle01_L", new Vector3(.10f, .75f, .62f));
                Transform fingertip = Bone(middle, "joint_Middle03_L", new Vector3(.10f, .75f, .68f));
                float height = Mathf.Lerp(elbowPoint.y, .75f, .8f) - .026f;
                surface.vertices = new[] { new Vector3(-.2f, height, .30f), new Vector3(.4f, height, .30f),
                    new Vector3(.4f, height, .44f), new Vector3(-.2f, height, .44f) };
                surface.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                surface.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 4).ToArray();
                surface.bindposes = new[] { Matrix4x4.identity };
                root.transform.SetPositionAndRotation(new Vector3(19, 25, -116), Quaternion.Euler(orientation));
                root.transform.localScale = Vector3.one * scale;
                var solver = new SeatedHandClearance(new[] { shoulder, elbow, wrist, hand, middle, fingertip },
                    new[] { root.transform }, surface, null, true);
                Vector3[] world = (Vector3[])typeof(SeatedHandClearance).GetField("world", Fields).GetValue(solver);
                for (int index = 0; index < world.Length; index++) world[index] = root.transform.TransformPoint(surface.vertices[index]);
                var measure = typeof(SeatedHandClearance).GetMethod("RequiredElbowLift", Fields);
                report.requestedLiftBefore = (float)measure.Invoke(solver, new object[] { 0, scale });
                Vector3 startWrist = wrist.position, startElbow = elbow.position;
                float upperLength = Vector3.Distance(shoulder.position, startElbow), lowerLength = Vector3.Distance(startElbow, startWrist);
                Quaternion startRotation = wrist.rotation;
                CheckFixedPalm(solver, root.transform, shoulder, elbow, wrist, scale, report);
                Vector3 axis = (startWrist - shoulder.position).normalized;
                Vector3 center = shoulder.position + axis * Vector3.Dot(startElbow - shoulder.position, axis);
                solver.Apply(root.transform, 1, 1);
                report.requestedLiftAfter = (float)measure.Invoke(solver, new object[] { 0, scale });
                report.wristDisplacement = Vector3.Distance(startWrist, wrist.position);
                report.wristRotation = RotationDistance(startRotation, wrist.rotation);
                report.elbowDisplacement = Vector3.Distance(startElbow, elbow.position);
                report.elbowAngle = Vector3.Angle(startElbow - center, elbow.position - center);
                report.segmentLengthError = Mathf.Max(Mathf.Abs(upperLength - Vector3.Distance(shoulder.position, elbow.position)),
                    Mathf.Abs(lowerLength - Vector3.Distance(elbow.position, wrist.position)));
                Vector3 settledWrist = wrist.position, settledElbow = elbow.position;
                for (int frame = 0; frame < 120; frame++)
                {
                    solver.Restore();
                    solver.Apply(root.transform, 1, 1f / 60f);
                    report.repeatDrift = Mathf.Max(report.repeatDrift, Vector3.Distance(settledWrist, wrist.position),
                        Vector3.Distance(settledElbow, elbow.position));
                }
                solver.Restore();
                report.restorationError = Mathf.Max(Vector3.Distance(startWrist, wrist.position),
                    Vector3.Distance(startElbow, elbow.position));
                Vector3 shoulderPosition = shoulder.position;
                shoulder.position -= root.transform.TransformVector(Vector3.forward * .6f);
                Vector3 supportWrist = wrist.position;
                solver.Apply(root.transform, 1, 1);
                report.supportHandDisplacement = Vector3.Distance(supportWrist, wrist.position);
                solver.Restore();
                shoulder.position = shoulderPosition;
                solver.Apply(root.transform, 0, 1);
                float[] angles = (float[])typeof(SeatedHandClearance).GetField("elbowAngles", Fields).GetValue(solver);
                report.standingCorrection = Mathf.Max(Mathf.Abs(angles[0]), Mathf.Abs(angles[1]));
                CheckTransitions(solver, root.transform, elbow, wrist, startElbow, startWrist, report);
                Require(!float.IsNaN(report.requestedLiftAfter) && !float.IsInfinity(report.requestedLiftAfter) &&
                    !float.IsNaN(report.elbowDisplacement) && !float.IsInfinity(report.elbowDisplacement), "Non-finite arm correction");
                Require(report.requestedLiftBefore > .001f * scale, "Fixture did not reproduce lower forearm contact");
                Require(report.requestedLiftAfter <= report.requestedLiftBefore + .0001f * scale, "Forearm clearance worsened");
                if (requireClearance) Require(report.requestedLiftAfter < .001f * scale, "Lower forearm remains inside the coat hem");
                Require(report.forearmWristDisplacement < .00005f * scale && report.forearmWristRotation < .01f, "Forearm correction moved the resting palm");
                Require(report.wristDisplacement < .00005f * scale, "Surface alignment moved the resting palm target");
                Require(report.wristRotation < (orientation.x == 0 && orientation.z == 0 ? .01f : 15.01f), "Surface alignment exceeded its rotation limit");
                Require(report.elbowDisplacement < .0451f * scale && report.elbowAngle < 16.05f, "Elbow correction exceeded its limit");
                Require(report.segmentLengthError < .00005f * scale, "Arm segment length changed");
                Require(report.repeatDrift < .00005f * scale && report.restorationError < .00005f * scale, "Arm correction accumulated or jittered");
                Require(report.supportHandDisplacement < .00005f * scale && report.standingCorrection == 0, "Support or standing pose changed");
                Require(report.transitionRestorationError < .00005f * scale, "Transition retained an arm correction");
                Require(report.transitionStep < .012f * scale && report.frameRateDifference < .001f * scale, "Transition snapped or depends on frame rate");
                report.passed = true;
            }
            catch (Exception error)
            {
                report.error = error.ToString();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(surface);
            }
            return report;
        }

        private static void CheckTransitions(SeatedHandClearance solver, Transform root, Transform elbow, Transform wrist,
            Vector3 startElbow, Vector3 startWrist, CaseReport report)
        {
            Vector3? firstRate = null;
            foreach (int rate in new[] { 30, 60, 120 })
            {
                solver.Restore();
                solver.Apply(root, 0, 1);
                Vector3 previous = elbow.position, peak = previous;
                for (int frame = 1; frame <= rate * 3; frame++)
                {
                    float time = frame / (float)rate;
                    float seated = time < 1 ? time : time < 2 ? 1 : 3 - time;
                    solver.Restore();
                    solver.Apply(root, seated, 1f / rate);
                    report.transitionStep = Mathf.Max(report.transitionStep, Vector3.Distance(previous, elbow.position));
                    previous = elbow.position;
                    if (frame == rate * 2) peak = previous;
                }
                if (firstRate.HasValue) report.frameRateDifference = Mathf.Max(report.frameRateDifference, Vector3.Distance(firstRate.Value, peak));
                else firstRate = peak;
                solver.Restore();
                report.transitionRestorationError = Mathf.Max(report.transitionRestorationError,
                    Vector3.Distance(startWrist, wrist.position), Vector3.Distance(startElbow, elbow.position));
            }
        }

        private static void CheckFixedPalm(SeatedHandClearance solver, Transform root, Transform shoulder,
            Transform elbow, Transform wrist, float scale, CaseReport report)
        {
            Transform[] joints = { shoulder, elbow, wrist };
            Quaternion[] rotations = joints.Select(value => value.localRotation).ToArray();
            Vector3 startWrist = wrist.position;
            Quaternion startRotation = wrist.rotation;
            typeof(SeatedHandClearance).GetMethod("ClearForearm", Fields).Invoke(solver, new object[] { 0, scale, 1f, 1f });
            report.forearmWristDisplacement = Vector3.Distance(startWrist, wrist.position);
            report.forearmWristRotation = RotationDistance(startRotation, wrist.rotation);
            for (int index = 0; index < joints.Length; index++) joints[index].localRotation = rotations[index];
            solver.Apply(root, 0, 1);
        }

        private static float RotationDistance(Quaternion a, Quaternion b)
        {
            Quaternion difference = b * Quaternion.Inverse(a);
            return 2f * Mathf.Atan2(new Vector3(difference.x, difference.y, difference.z).magnitude, Mathf.Abs(difference.w)) * Mathf.Rad2Deg;
        }

        private static Transform Bone(Transform parent, string name, Vector3 position)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.position = position;
            return bone;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
