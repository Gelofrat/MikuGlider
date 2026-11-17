using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MikuGlider;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal static class LabWristReplayChecks
    {
        [Serializable] private sealed class Frame
        {
            public string capture;
            public int sample;
            public float[] bones;
        }

        [Serializable] private sealed class Input { public string mesh; public Frame[] frames; }
        [Serializable] private sealed class Geometry
        {
            public string capture;
            public int sample;
            public Vector3[] vertices;
        }

        [Serializable] private sealed class Report
        {
            public bool passed;
            public string error;
            public int samples;
            public int modifiedVertices;
            public float maximumGripDisplacement;
            public float maximumSeamOpening;
            public float maximumPalmMatrixError;
            public float maximumEndTangentError;
            public float maximumChange;
            public float maximumFingerWeightChange;
            public float maximumForearmDisplacement;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string path = LabController.Argument("-labWristReplay");
            if (path == null) return;
            var report = new Report();
            string folder = LabController.Argument("-labReports");
            var root = new GameObject("Recorded wrist validation");
            var before = new Mesh();
            var after = new Mesh();
            try
            {
                Input input = JsonUtility.FromJson<Input>(File.ReadAllText(path));
                Mesh source = LabAssetLibrary.Get("v2").GetMesh(input.mesh ?? "arm-left");
                Matrix4x4[] binds = source.bindposes;
                Transform[] bones = Enumerable.Range(0, binds.Length).Select(index =>
                {
                    var bone = new GameObject("recorded " + index).transform;
                    bone.SetParent(root.transform, false);
                    SetMatrix(bone, binds[index].inverse);
                    return bone;
                }).ToArray();
                var visual = root.AddComponent<SkinnedMeshRenderer>();
                visual.sharedMesh = source;
                visual.bones = bones;
                visual.rootBone = root.transform;
                visual.quality = SkinQuality.Bone4;
                visual.forceRenderingOff = true;
                var reference = new GameObject("source");
                reference.transform.SetParent(root.transform, false);
                var native = reference.AddComponent<SkinnedMeshRenderer>();
                native.sharedMesh = source;
                native.bones = bones;
                native.rootBone = root.transform;
                native.quality = SkinQuality.Bone4;
                native.forceRenderingOff = true;
                FirstPersonWristVisual.Attach(visual, source);
                var correction = visual.GetComponent<FirstPersonWristVisual>();
                BoneWeight[] weights = source.boneWeights;
                Vector3[] vertices = source.vertices;
                Vector3 wrist = binds[2].inverse.MultiplyPoint3x4(Vector3.zero);
                Vector3 elbow = binds[1].inverse.MultiplyPoint3x4(Vector3.zero);
                Vector3 axis = (wrist - elbow).normalized;
                BoneWeight[] adjusted = visual.sharedMesh.boneWeights;
                bool[] protectedGrip = weights.Select((weight,index) => CoreWeight(weight) < .00001f).ToArray();
                report.modifiedVertices = weights.Where((weight,index) => !weight.Equals(adjusted[index])).Count();
                for (int index = 0; index < weights.Length; index++)
                    for (int bone = 4; bone < binds.Length; bone++)
                        report.maximumFingerWeightChange = Mathf.Max(report.maximumFingerWeightChange,
                            Mathf.Abs(Influence(weights[index], bone) - Influence(adjusted[index], bone)));
                int[][] seams = Enumerable.Range(0, vertices.Length).GroupBy(index =>
                    Vector3Int.RoundToInt(vertices[index]*100000f)).Where(group => group.Count()>1)
                    .Select(group => group.ToArray()).ToArray();
                foreach (Frame frame in input.frames)
                {
                    for (int index = 0; index < bones.Length; index++)
                    {
                        var matrix = new Matrix4x4();
                        for (int row = 0; row < 4; row++)
                            for (int column = 0; column < 4; column++)
                                matrix[row,column] = frame.bones[index*16+row*4+column];
                        SetMatrix(bones[index],matrix);
                    }
                    correction.UpdatePose();
                    native.BakeMesh(before);
                    visual.BakeMesh(after);
                    Vector3[] expected = before.vertices, actual = after.vertices;
                    for (int index = 0; index < actual.Length; index++)
                    {
                        float difference = Vector3.Distance(actual[index], expected[index]);
                        if (!float.IsFinite(difference)) throw new InvalidOperationException("Nonfinite wrist vertex");
                        report.maximumChange = Mathf.Max(report.maximumChange,difference);
                        if (protectedGrip[index]) report.maximumGripDisplacement = Mathf.Max(report.maximumGripDisplacement,difference);
                        if (Vector3.Dot(vertices[index]-wrist,axis) < -.09f && Influence(weights[index],1) > .99999f)
                            report.maximumForearmDisplacement = Mathf.Max(report.maximumForearmDisplacement,difference);
                    }
                    foreach (int[] seam in seams)
                        foreach (int index in seam)
                            report.maximumSeamOpening = Mathf.Max(report.maximumSeamOpening,Vector3.Distance(actual[index],actual[seam[0]]));
                    Matrix4x4 palm = bones[3].localToWorldMatrix * binds[3];
                    Matrix4x4 end = visual.bones.Last().localToWorldMatrix * binds[1];
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++)
                            report.maximumPalmMatrixError = Mathf.Max(report.maximumPalmMatrixError,Mathf.Abs(end[row,column]-palm[row,column]));
                    report.maximumEndTangentError = Mathf.Max(report.maximumEndTangentError,
                        Vector3.Angle(end.MultiplyVector(axis),palm.MultiplyVector(axis)));
                    if (frame.sample%30 == 0)
                        File.WriteAllText(Path.Combine(folder,frame.capture+"-"+frame.sample+"-wrist.json"),
                            JsonUtility.ToJson(new Geometry { capture=frame.capture,sample=frame.sample,vertices=actual }));
                    report.samples++;
                }
                report.passed = report.samples > 300 && report.maximumChange > .001f &&
                    report.maximumGripDisplacement < .00005f && report.maximumSeamOpening < .0001f &&
                    report.maximumFingerWeightChange < .000001f && report.maximumForearmDisplacement < .00005f &&
                    report.maximumPalmMatrixError < .0001f && report.maximumEndTangentError < .1f;
                if (!report.passed) throw new InvalidOperationException("Recorded wrist acceptance checks failed");
                Debug.Log("WRIST_REPLAY_PASS samples="+report.samples);
            }
            catch (Exception error)
            {
                report.passed = false;
                report.error = error.ToString();
                Debug.LogError("WRIST_REPLAY_FAIL "+error);
            }
            finally
            {
                File.WriteAllText(Path.Combine(folder,"wrist-replay-checks.json"),JsonUtility.ToJson(report,true));
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(before);
                UnityEngine.Object.DestroyImmediate(after);
            }
        }

        private static float CoreWeight(BoneWeight weight) => Influence(weight,1) + Influence(weight,2) + Influence(weight,3);

        private static float Influence(BoneWeight weight, int bone) =>
            (weight.boneIndex0 == bone ? weight.weight0 : 0) + (weight.boneIndex1 == bone ? weight.weight1 : 0) +
            (weight.boneIndex2 == bone ? weight.weight2 : 0) + (weight.boneIndex3 == bone ? weight.weight3 : 0);

        private static void SetMatrix(Transform bone, Matrix4x4 matrix)
        {
            bone.SetPositionAndRotation(matrix.MultiplyPoint3x4(Vector3.zero),matrix.rotation);
            bone.localScale = matrix.lossyScale;
        }
    }
}
