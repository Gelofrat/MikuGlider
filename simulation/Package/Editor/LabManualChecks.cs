using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MikuGlider;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabManualChecks
    {
        private static int cases;
        private static float maximumError;

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool("MikuManualChecks", true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Connect() => EditorApplication.update += Tick;

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool("MikuManualChecks", false)) return;
            SessionState.SetBool("MikuManualChecks", false);
            int code = RunChecks();
            EditorApplication.Exit(code);
        }

        private static int RunChecks()
        {
            string report = Path.Combine(LabManualSession.ProjectRoot(), "simulation/Reports/manual-editor-checks.txt");
            try
            {
                WeightChecks();
                ActualModels();
                File.WriteAllText(report, "PASS cases=" + cases + " maximumPoseError=" + maximumError.ToString("R"));
                Debug.Log("MANUAL_EDITOR_CHECKS_PASS cases=" + cases + " maximumPoseError=" + maximumError);
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "FAIL " + error);
                Debug.LogException(error);
                return 1;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void WeightChecks()
        {
            var source = new BoneWeight { boneIndex0 = 0, weight0 = .6f, boneIndex1 = 1, weight1 = .3f, boneIndex2 = 2, weight2 = .1f };
            foreach (float amount in new[] { 0, .25f, 1 })
            {
                BoneWeight weight = LabCorrectionMath.SetWeight(source, 3, amount);
                Require(Mathf.Abs(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3 - 1) < .00001f, "Weights were not normalized");
                Require(Mathf.Abs(LabCorrectionMath.Influence(weight, 3) - amount) < .00001f, "Wrong selected influence");
                var profile = new ModelCorrections { meshes = new[] { new MeshCorrection { key = "test", fingerprint = "test",
                    weights = new[] { new VertexWeightEdit { vertex = 2, value = weight } } } } };
                string json = JsonUtility.ToJson(profile);
                BoneWeight restored = ModelCorrections.Parse(json).meshes[0].weights[0].value;
                Require(restored.Equals(weight), "JSON did not preserve BoneWeight: " + json);
                cases++;
            }
        }

        private static void ActualModels()
        {
            var host = new GameObject("Manual checks");
            var lab = host.AddComponent<LabController>();
            lab.enabled = false;
            host.AddComponent<LabInterface>();
            lab.Variants = JsonUtility.FromJson<LabCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/catalog").text).variants;
            lab.Clips = JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips;
            try
            {
                for (int variant = 0; variant < lab.Variants.Length; variant++)
                foreach (bool arms in new[] { false, true })
                {
                    lab.Variant = variant;
                    lab.Arms = arms;
                    lab.Motion = Array.FindIndex(lab.Clips, clip => arms ? clip.rig == "arms" && clip.label.Contains("Shotgun") : clip.rig == "body" && clip.key == "idle");
                    Require(lab.Motion >= 0, "Missing native animation for authoring checks");
                    ClipEntry entry = lab.Clips[lab.Motion];
                    var clip = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + entry.resource);
                    lab.Actor = new LabActor(lab.Variants[variant], arms);
                    lab.Actor.Root.transform.SetPositionAndRotation(new Vector3(3, 2, -4), Quaternion.Euler(0, 35, 0));
                    lab.Actor.Root.transform.localScale = Vector3.one * 1.15f;
                    lab.Actor.Animate(entry, clip, clip, entry.speed, .25f, false, false);
                    foreach (var component in lab.Actor.Root.GetComponentsInChildren<MonoBehaviour>())
                        component.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
                    var surfaces = lab.Actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null &&
                        (r.sharedMesh.name.Contains(".body") || r.sharedMesh.name.Contains(".arm-") || r.sharedMesh.name.Contains(".skirt") ||
                        r.sharedMesh.name.Contains(".coat-flaps") || r.sharedMesh.name.Contains(".sleeves"))).ToArray();
                    foreach (var renderer in surfaces) CheckSurface(lab, renderer);
                    lab.Actor.Dispose();
                    lab.Actor = null;
                    LabAssetLibrary.Clear();
                }
            }
            finally
            {
                lab.Actor?.Dispose();
                Object.DestroyImmediate(host);
            }
        }

        private static void CheckSurface(LabController lab, SkinnedMeshRenderer renderer)
        {
            string label = lab.Variants[lab.Variant].key + "/" + renderer.sharedMesh.name;
            Mesh original = renderer.sharedMesh;
            Transform[] native = renderer.bones;
            Matrix4x4[] frozenMatrices = native.Select(b => b.localToWorldMatrix).ToArray();
            ModelPoseCorrections.Attach(renderer, new ModelCorrections());
            Require(renderer.GetComponent<ModelPoseCorrections>() == null, "Empty profile created runtime behaviour");
            Vector3[] frozen;
            using (var sample = new MeshLease()) { renderer.BakeMesh(sample.Mesh); frozen = sample.Mesh.vertices; }
            MeshCorrection correction;
            Vector3[] expected;
            using (var session = new LabManualSession(lab, renderer))
            {
                Require(Error(frozen, session.Points.Select(renderer.transform.InverseTransformPoint).ToArray()) < .0001f, "Freeze changed pose " + label);
                int chosen = Array.FindIndex(session.Bones, b => b.name.Contains("hand"));
                if (chosen < 0) chosen = session.Bones.Length - 1;
                session.Bones[chosen].localRotation *= Quaternion.Euler(0, 5, 0);
                session.Bones[chosen].localPosition += new Vector3(.003f, .001f, 0);
                session.Drivers.Add(chosen);
                session.Selected.Add(0);
                session.ApplyWeight(chosen, .2f);
                session.Bake();
                using (var sample = new MeshLease()) { renderer.BakeMesh(sample.Mesh); expected = sample.Mesh.vertices; }
                correction = session.Export("Authoring regression", 30, null, false);
                Require(correction.weights.Length > 0 && correction.poses.Length == 1, "No authored change " + label);
                var profile = new ModelCorrections { meshes = new[] { correction } };
                string folder = Path.Combine(LabManualSession.ProjectRoot(), "scratch/manual-editor-checks", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "corrections.json");
                string resource = Path.Combine(folder, "resource.json");
                File.WriteAllText(path, "{\"schemaVersion\":1,\"meshes\":[]}");
                LabManualSession.PersistProfile(path, resource, Path.Combine(folder, "backups"), session.Variant, profile);
                Require(File.ReadAllText(path) == File.ReadAllText(resource), "Resource synchronization differs");
                Require(Directory.GetFiles(Path.Combine(folder, "backups"), "*.json", SearchOption.AllDirectories).Length == 1, "Backup missing");
                correction = ModelCorrections.Parse(File.ReadAllText(path)).meshes[0];
            }
            Require(renderer.sharedMesh == original && renderer.bones.SequenceEqual(native), "Discard did not restore renderer");
            for (int b = 0; b < native.Length; b++)
                for (int k = 0; k < 16; k++)
                    Require(Mathf.Abs(frozenMatrices[b][k] - native[b].localToWorldMatrix[k]) < .0001f, "Session changed native bone " + label + "/" + b);
            var clone = original;
            var positions = native.Select(b => b.position).ToArray();
            var rotations = native.Select(b => b.rotation).ToArray();
            renderer.sharedMesh = clone;
            try
            {
                ModelCorrections.Validate(clone, correction);
                ModelPoseCorrections.Attach(renderer, new ModelCorrections { meshes = new[] { correction } });
                var runtime = renderer.GetComponent<ModelPoseCorrections>();
                Require(runtime != null, "Runtime rejected valid correction " + label);
                runtime.SendMessage("LateUpdate");
                using (var sample = new MeshLease())
                {
                    renderer.BakeMesh(sample.Mesh);
                    float error = Error(expected, sample.Mesh.vertices);
                    maximumError = Mathf.Max(maximumError, error);
                    if (error >= .0002f)
                    {
                        Vector3[] actual = sample.Mesh.vertices;
                        int worst = Enumerable.Range(0, expected.Length).OrderByDescending(i => (expected[i] - actual[i]).sqrMagnitude).First();
                        Debug.Log("MANUAL_MISMATCH vertex=" + worst + " expected=" + expected[worst].ToString("R") + " actual=" + actual[worst].ToString("R") +
                            " shape=" + renderer.GetBlendShapeWeight(clone.blendShapeCount - 1) + " weight=" + JsonUtility.ToJson(new VertexWeightEdit { vertex = worst, value = clone.boneWeights[worst] }));
                    }
                    Require(error < .0002f, "Saved pose differs " + label + ": " + error);
                }
                for (int i = 0; i < native.Length; i++)
                    Require(Vector3.Distance(positions[i], native[i].position) < 1e-6f && Quaternion.Angle(rotations[i], native[i].rotation) < .05f, "Native skeleton moved");
                PoseShapeDriver driver = correction.poses[0].drivers[0];
                Quaternion rotation = native[driver.bone].localRotation;
                native[driver.bone].localRotation = rotation * Quaternion.Euler(0, 60, 0);
                runtime.SendMessage("LateUpdate");
                Require(renderer.GetBlendShapeWeight(clone.blendShapeCount - 1) < .001f, "Correction did not fade away");
                native[driver.bone].localRotation = rotation;
                Object.DestroyImmediate(runtime);
                var mismatch = Object.Instantiate(original);
                try
                {
                    Vector3[] vertices = mismatch.vertices;
                    vertices[0].x += .001f;
                    mismatch.vertices = vertices;
                    bool rejected = false;
                    try { ModelCorrections.Validate(mismatch, correction); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "Stale profile accepted");
                }
                finally { Object.DestroyImmediate(mismatch); }
            }
            finally
            {
                renderer.sharedMesh = original;
            }
            Debug.Log("MANUAL_SURFACE_PASS " + label);
            cases++;
        }

        private static float Error(Vector3[] first, Vector3[] second)
        {
            Require(first.Length == second.Length, "Vertex count differs");
            float maximum = 0;
            for (int i = 0; i < first.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(first[i], second[i]));
            return maximum;
        }

        private sealed class MeshLease : IDisposable
        {
            internal readonly Mesh Mesh = new Mesh();
            public void Dispose() => Object.DestroyImmediate(Mesh);
        }
    }
}
