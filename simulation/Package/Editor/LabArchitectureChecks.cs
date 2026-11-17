using System;
using System.Linq;
using MikuGlider;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabArchitectureChecks
    {
        public static string Run()
        {
            var root = new GameObject("Architecture regression checks");
            var mesh = new Mesh { name = "Fixture.arm-left" };
            Mesh secondMesh = null;
            int checks = 0, warnings = 0;
            Action<bool, string> check = (ok, message) =>
            {
                if (!ok) throw new InvalidOperationException(message);
                checks++;
            };
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type == LogType.Warning && message.Contains("skipped incompatible manual corrections")) warnings++;
            };
            Application.logMessageReceived += log;
            try
            {
                var bones = Enumerable.Range(0, 6).Select(i => new GameObject("joint" + i).transform).ToArray();
                for (int i = 0; i < bones.Length; i++) bones[i].SetParent(i == 0 ? root.transform : bones[i - 1], false);
                mesh.vertices = new[] { Vector3.zero, Vector3.right * .01f, Vector3.up * .01f };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateNormals();
                mesh.bindposes = bones.Select(b => Matrix4x4.identity).ToArray();
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 4, weight0 = 1 }, 3).ToArray();
                var correction = new MeshCorrection
                {
                    key = "arm-left", fingerprint = ModelCorrections.Fingerprint(mesh),
                    poses = new[] { new PoseShapeEdit
                    {
                        name = "grip", drivers = new[] { new PoseShapeDriver { bone = 0, name = "joint0", rotation = Quaternion.identity } },
                        boneRotations = new[] { new PoseBoneRotation { bone = 4, name = "joint4", rotation = Quaternion.Euler(10, 0, 0) } }
                    } }
                };
                var profile = new ModelCorrections { meshes = new[] { correction } };
                var wrong = new GameObject("wrong joint").transform;
                wrong.SetParent(root.transform, false);
                var badBones = (Transform[])bones.Clone();
                badBones[4] = wrong;
                var bad = Surface(root, mesh, badBones);
                for (int i = 0; i < 3; i++) ModelPoseCorrections.Attach(bad, profile);
                check(bad.GetComponent<ModelPoseCorrections>() == null && warnings == 1, "Repeated bad rig must be rejected once");
                var truncated = Surface(root, mesh, bones.Take(4).ToArray());
                ModelPoseCorrections.Attach(truncated, profile);
                check(truncated.GetComponent<ModelPoseCorrections>() == null, "Truncated rig must be rejected before indexing missing bones");
                var good = Surface(root, mesh, bones);
                ModelPoseCorrections.Attach(good, profile);
                var pose = good.GetComponent<ModelPoseCorrections>();
                check(pose != null && pose.enabled, "Bad renderer poisoned a valid renderer sharing its mesh");
                pose.SendMessage("LateUpdate");
                var copies = good.bones;
                check(copies[4] != bones[4] && Quaternion.Angle(bones[4].localRotation, Quaternion.identity) < .001f,
                    "Grip must change renderer copies, not animation bones");
                wrong.name = "joint4";
                ModelPoseCorrections.Attach(bad, profile);
                check(bad.GetComponent<ModelPoseCorrections>()?.enabled == true, "Correcting the same rig must allow retry");
                pose.enabled = false;
                pose.SendMessage("OnDisable");
                check(good.bones.SequenceEqual(bones), "Disable must restore source rig");
                pose.enabled = true;
                pose.SendMessage("LateUpdate");
                check(good.bones[4] == copies[4], "Enable must restore grip copies");
                pose.ResetShapes();

                var binding = RendererBoneBinding.For(good);
                var extended = bones.Concat(new[] { wrong }).ToArray();
                binding.SetWrist(extended);
                var grip = (Transform[])extended.Clone(); grip[4] = wrong;
                binding.SetGrip(grip);
                binding.ReleaseGrip();
                check(good.bones.SequenceEqual(extended), "Grip cleanup must retain wrist layer");
                binding.SetGrip(grip);
                binding.ReleaseWrist();
                check(good.bones.SequenceEqual(grip), "Wrist cleanup must not overwrite grip layer");
                binding.ReleaseGrip();
                check(good.bones.SequenceEqual(bones), "Last layer must restore native rig");
                binding = RendererBoneBinding.For(good);
                binding.SetWrist(extended);
                good.bones = badBones; // Simulate a game-side renderer rebind.
                binding.ReleaseWrist();
                check(good.bones.SequenceEqual(badBones), "Cleanup overwrote a newer game-side rig");

                secondMesh = Object.Instantiate(mesh); secondMesh.name = mesh.name; secondMesh.ClearBlendShapes();
                var asset = Surface(root, secondMesh, bones);
                var invalidProfile = new ModelCorrections { meshes = new[] { new MeshCorrection { key = "arm-left", fingerprint = "wrong" } } };
                ModelPoseCorrections.Attach(asset, invalidProfile);
                ModelPoseCorrections.Attach(asset, profile);
                check(asset.GetComponent<ModelPoseCorrections>()?.enabled == true, "Bad profile poisoned a later valid profile");
                return "ARCHITECTURE_UNITY_PASS checks=" + checks;
            }
            finally
            {
                Application.logMessageReceived -= log;
                foreach (var pose in root.GetComponentsInChildren<ModelPoseCorrections>()) pose.ResetShapes();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                if (secondMesh != null) Object.DestroyImmediate(secondMesh);
            }
        }

        private static SkinnedMeshRenderer Surface(GameObject root, Mesh mesh, Transform[] bones)
        {
            var node = new GameObject("Surface"); node.transform.SetParent(root.transform, false);
            var renderer = node.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; renderer.bones = bones;
            return renderer;
        }
    }
}
