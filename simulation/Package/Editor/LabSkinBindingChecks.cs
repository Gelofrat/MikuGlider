using System;
using System.Collections.Generic;
using System.Linq;
using MikuGlider;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    // Real body meshes, repeated renderer reuse, and a replacement native rig.
    public static class LabSkinBindingChecks
    {
        public static string Run()
        {
            var root = new GameObject("Skin binding regression");
            var baked = new Mesh();
            var baseline = new Mesh();
            var results = new List<string>();
            try
            {
                var rig = new LabRig("body-rig", root.transform);
                var node = rig.Nodes[rig.Data.renderers[0].node];
                var body = node.gameObject.AddComponent<SkinnedMeshRenderer>();
                body.bones = rig.Bones;
                body.rootBone = rig.Nodes[rig.Data.renderers[0].root];
                var reference = new GameObject("Native baseline").AddComponent<SkinnedMeshRenderer>();
                reference.transform.SetParent(node, false);
                reference.bones = rig.Bones;
                reference.rootBone = body.rootBone;
                var catalog = JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text);
                var entry = catalog.clips.Single(c => c.key == "clip-445");
                var menuClip = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + entry.resource);
                float maxSwap = 0;
                for (int cycle = 0; cycle < 6; cycle++)
                    foreach (string key in new[] { "v1", "v2", "v3", "v1" })
                    {
                        var assets = LabAssetLibrary.Get(key);
                        ModelPoseCorrections.PrepareMeshChange(body);
                        if (!body.bones.SequenceEqual(rig.Bones)) throw new InvalidOperationException("Body swap retained temporary bones");
                        body.sharedMesh = reference.sharedMesh = assets.GetMesh("body");
                        root.transform.SetPositionAndRotation(new Vector3(17 + cycle, 26, -120), Quaternion.Euler(0, cycle * 37, 0));
                        root.transform.localScale = Vector3.one * (.3f + cycle * .13f);
                        rig.Restore();
                        menuClip.SampleAnimation(rig.AnimationRoot, cycle * .1f);
                        ModelPoseCorrections.Attach(body, assets.Corrections);
                        var pose = body.GetComponent<ModelPoseCorrections>();
                        if (pose != null && pose.enabled) pose.SendMessage("LateUpdate");
                        maxSwap = Mathf.Max(maxSwap, Difference(body, reference, baked, baseline));
                        root.SetActive(false);
                        root.SetActive(true);
                        if (pose != null && pose.enabled) pose.SendMessage("LateUpdate");
                        maxSwap = Mathf.Max(maxSwap, Difference(body, reference, baked, baseline));
                    }
                results.Add("swaps=" + maxSwap.ToString("G9"));
                var replacement = new LabRig("body-rig", root.transform);
                replacement.Root.transform.localPosition = new Vector3(10, 0, 0);
                body.bones = reference.bones = replacement.Bones;
                ModelPoseCorrections.Attach(body, LabAssetLibrary.Get("v1").Corrections);
                body.GetComponent<ModelPoseCorrections>().SendMessage("LateUpdate");
                float rebound = Difference(body, reference, baked, baseline);
                results.Add("rebind=" + rebound.ToString("G9"));
                // Disposing the departed lobby must not leave any finger on its rig.
                var sources = body.GetComponent<ModelPoseCorrections>().SourceBones;
                if (!sources.SequenceEqual(replacement.Bones)) throw new InvalidOperationException("Old lobby sources retained");
                Object.DestroyImmediate(rig.Root);
                // Both renderers were children of the old rig; cover new renderers
                // using the already-prepared shared mesh in the next lobby too.
                float warm = 0;
                foreach (string key in new[] { "v1", "v2", "v3", "v1" })
                {
                    var assets = LabAssetLibrary.Get(key);
                    var a = new GameObject("New lobby " + key).AddComponent<SkinnedMeshRenderer>();
                    a.transform.SetParent(replacement.Root.transform, false);
                    var b = new GameObject("Native baseline " + key).AddComponent<SkinnedMeshRenderer>();
                    b.transform.SetParent(replacement.Root.transform, false);
                    a.bones = b.bones = replacement.Bones;
                    a.sharedMesh = b.sharedMesh = assets.GetMesh("body");
                    ModelPoseCorrections.Attach(a, assets.Corrections);
                    var pose = a.GetComponent<ModelPoseCorrections>();
                    if (pose != null && pose.enabled) pose.SendMessage("LateUpdate");
                    warm = Mathf.Max(warm, Difference(a, b, baked, baseline));
                    if (pose != null) pose.ResetShapes();
                    if (!a.bones.SequenceEqual(replacement.Bones)) throw new InvalidOperationException("Swap did not restore native bones");
                }
                results.Add("newLobby=" + warm.ToString("G9"));
                if (Mathf.Max(maxSwap, rebound, warm) > .0001f) throw new InvalidOperationException(string.Join("; ", results));
                return string.Join("; ", results);
            }
            finally
            {
                foreach (var pose in root.GetComponentsInChildren<ModelPoseCorrections>(true)) pose.ResetShapes();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(baseline);
            }
        }

        public static string Arms()
        {
            var root = new GameObject("Arm skin binding regression");
            var baked = new Mesh(); var baseline = new Mesh();
            try
            {
                var oldRig = new LabRig("arms-rig", root.transform);
                var newRig = new LabRig("arms-rig", root.transform);
                newRig.Root.transform.localPosition = new Vector3(10, 0, 0);
                float error = 0;
                int checks = 0;
                for (int side = 0; side < 2; side++)
                {
                    var oldBones = oldRig.Data.renderers[side].bones.Select(i => oldRig.Nodes[i]).ToArray();
                    var newBones = newRig.Data.renderers[side].bones.Select(i => newRig.Nodes[i]).ToArray();
                    var a = new GameObject("Reused arm " + side).AddComponent<SkinnedMeshRenderer>();
                    var b = new GameObject("New arm " + side).AddComponent<SkinnedMeshRenderer>();
                    a.transform.SetParent(root.transform, false); b.transform.SetParent(root.transform, false);
                    a.bones = oldBones; b.bones = newBones;
                    foreach (string key in new[] { "v1", "v2", "v3", "v1" })
                    {
                        var assets = LabAssetLibrary.Get(key);
                        bool wrist = side == 0 || key == "v2";
                        string meshKey = side == 0 ? "arm-left" : "arm-right";
                        ModelPoseCorrections.PrepareMeshChange(a); ModelPoseCorrections.PrepareMeshChange(b);
                        a.sharedMesh = b.sharedMesh = assets.GetMesh(meshKey);
                        a.bones = oldBones;
                        FirstPersonWristVisual.Attach(a, wrist ? a.sharedMesh : null, assets.ModelDefinition);
                        ModelPoseCorrections.Attach(a, assets.Corrections);
                        a.bones = newBones; // Same mesh, different live skeleton.
                        FirstPersonWristVisual.Attach(a, wrist ? assets.GetMesh(meshKey) : null, assets.ModelDefinition);
                        ModelPoseCorrections.Attach(a, assets.Corrections);
                        FirstPersonWristVisual.Attach(b, wrist ? b.sharedMesh : null, assets.ModelDefinition);
                        ModelPoseCorrections.Attach(b, assets.Corrections);
                        foreach (var surface in new[] { a, b })
                        {
                            var pose = surface.GetComponent<ModelPoseCorrections>();
                            if (pose != null && pose.enabled) pose.SendMessage("LateUpdate");
                        }
                        error = Mathf.Max(error, Difference(a, b, baked, baseline));
                        ModelPoseCorrections.PrepareMeshChange(a);
                        if (!a.bones.SequenceEqual(newBones) || a.sharedMesh != assets.GetMesh(meshKey))
                            throw new InvalidOperationException("Arm swap retained wrist/grip binding: " + key + " " + side);
                        checks++;
                    }
                    ModelPoseCorrections.PrepareMeshChange(b);
                }
                if (error > .0001f) throw new InvalidOperationException("Arm rebind error " + error);
                return "arms=" + checks + "; rebind=" + error.ToString("G9");
            }
            finally
            {
                foreach (var surface in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) ModelPoseCorrections.PrepareMeshChange(surface);
                Object.DestroyImmediate(root); Object.DestroyImmediate(baked); Object.DestroyImmediate(baseline);
            }
        }

        private static float Difference(SkinnedMeshRenderer body, SkinnedMeshRenderer reference, Mesh a, Mesh b)
        {
            body.BakeMesh(a); reference.BakeMesh(b);
            var av = a.vertices; var bv = b.vertices;
            return av.Select((v, i) => Vector3.Distance(v, bv[i])).Max();
        }
    }
}
