using System;
using System.Collections;
using System.IO;
using System.Linq;
using MikuGlider;
using Pigeon.Movement;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal static class LabFlightHairChecks
    {
        [Serializable] private sealed class Result
        {
            public string variant;
            public float maximumRestError;
            public float maximumMotion;
            public float maximumSeamError;
            public float maximumBakeError;
            public float maximumScaledBakeError;
            public bool passed;
        }

        internal static IEnumerator Run(LabVariant[] variants, string directory)
        {
            Time.captureDeltaTime = 1f / 60f;
            foreach (LabVariant variant in variants)
            {
                var owner = new Player();
                var skin = new RegisteredSkin { Assets = LabAssetLibrary.Get(variant.key), Definition = new SkinVariantDefinition { Key = variant.key } };
                var root = new GameObject("FP flight check");
                root.transform.position = new Vector3(30, 40, 60);
                root.transform.rotation = Quaternion.Euler(10, 30, 50);
                root.transform.localScale = Vector3.one * 2;
                var material = LabActor.MakeMaterial(variant.key, "body-albedo");
                Mesh[] original = { skin.Assets.GetMesh("wing-base"), skin.Assets.GetMesh("wing-blades") };
                var sources = new MeshRenderer[2];
                for (int part = 0; part < 2; part++)
                {
                    Transform mount = part == 0 ? root.transform : new GameObject("Blade").transform;
                    if (part > 0)
                    {
                        mount.SetParent(root.transform, false);
                        mount.localPosition = new Vector3(-.000022643977f, -.0000006580353f, .018689739f);
                    }
                    mount.gameObject.AddComponent<MeshFilter>().sharedMesh = original[part];
                    sources[part] = mount.gameObject.AddComponent<MeshRenderer>();
                    sources[part].sharedMaterial = material;
                }
                FlightHairVisual.Attach(root.transform, skin, owner, .7f);
                yield return new WaitForEndOfFrame();
                var renderers = sources.Select((s, part) => s.transform.Find("Animated " + original[part].name).GetComponent<SkinnedMeshRenderer>()).ToArray();
                var baked = new[] { new Mesh(), new Mesh() };
                var cameraObject = new GameObject("FP hair review");
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.15f, .16f, .19f);
                camera.nearClipPlane = .001f;
                camera.farClipPlane = 2f;
                camera.orthographic = true;
                camera.orthographicSize = .14f;
                Vector3 focus = root.transform.TransformPoint(new Vector3(0, 0, .045f));
                camera.transform.position = focus + root.transform.right * .35f + root.transform.up * .18f;
                camera.transform.LookAt(focus, root.transform.up);
                LabCapture.Save(camera, Path.Combine(directory, variant.key + "-fp-rest.png"));
                var result = new Result { variant = variant.key };
                Vector3[][] rest = new Vector3[2][];
                for (int part = 0; part < 2; part++)
                {
                    rest[part] = original[part].vertices;
                    Vector3[] posed = Skin(renderers[part]);
                    for (int index = 0; index < rest[part].Length; index++)
                        result.maximumRestError = Mathf.Max(result.maximumRestError, Vector3.Distance(rest[part][index], posed[index]));
                }
                var seams = new System.Collections.Generic.List<Vector2Int>();
                var seamPoints = new System.Collections.Generic.Dictionary<Vector3Int, int>();
                for (int index = 0; index < rest[0].Length; index++)
                    seamPoints[Key(rest[0][index])] = index;
                for (int index = 0; index < rest[1].Length; index++)
                {
                    Vector3 point = root.transform.InverseTransformPoint(sources[1].transform.TransformPoint(rest[1][index]));
                    if (seamPoints.TryGetValue(Key(point), out int other))
                        seams.Add(new Vector2Int(other, index));
                }
                Require(seams.Count > 0, "No seam samples");
                owner.Wingsuit.IsFlying = true;
                for (int frame = 0; frame < 120; frame++)
                {
                    yield return new WaitForEndOfFrame();
                    if (frame == 30 || frame == 60 || frame == 90)
                        LabCapture.Save(camera, Path.Combine(directory, variant.key + "-fp-flight-" + frame + ".png"));
                    if (frame % 6 != 0) continue;
                    Vector3[][] positions = renderers.Select(Skin).ToArray();
                    for (int part = 0; part < 2; part++)
                    {
                        Require(sources[part].forceRenderingOff && renderers[part].enabled && !renderers[part].forceRenderingOff,
                            "Original must be hidden and animated replacement visible");
                        for (int index = 0; index < positions[part].Length; index++)
                        {
                            Vector3 point = positions[part][index];
                            Require(float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z), "Invalid skinned position");
                            result.maximumMotion = Mathf.Max(result.maximumMotion, Vector3.Distance(point, rest[part][index]));
                        }
                        renderers[part].BakeMesh(baked[part]);
                        Require(baked[part].vertexCount == original[part].vertexCount, "Baked mesh count");
                        Vector3[] actual = baked[part].vertices;
                        if (frame == 0)
                            File.AppendAllText(Path.Combine(directory, "bake-space.txt"), variant.key + " " + part + " expected=" + positions[part][0].ToString("F8") + " actual=" + actual[0].ToString("F8") + " scale=" + renderers[part].transform.lossyScale + " root=" + renderers[part].rootBone.localPosition + "\n");
                        for (int index = 0; index < actual.Length; index++)
                        {
                            result.maximumBakeError = Mathf.Max(result.maximumBakeError, Vector3.Distance(actual[index], positions[part][index]));
                            result.maximumScaledBakeError = Mathf.Max(result.maximumScaledBakeError, Vector3.Distance(actual[index] * .5f, positions[part][index]));
                        }
                    }
                    foreach (Vector2Int seam in seams)
                    {
                        Vector3 a = sources[0].transform.TransformPoint(positions[0][seam.x]);
                        Vector3 b = sources[1].transform.TransformPoint(positions[1][seam.y]);
                        result.maximumSeamError = Mathf.Max(result.maximumSeamError, Vector3.Distance(a, b));
                    }
                }
                root.SetActive(false);
                Require(sources.All(s => !s.forceRenderingOff), "Disable must restore original visibility");
                root.SetActive(true);
                yield return new WaitForEndOfFrame();
                Require(sources.All(s => s.forceRenderingOff), "Re-enable must resume animation");
                FlightHairVisual.Attach(root.transform, null, owner, 0);
                Require(sources.All(s => !s.forceRenderingOff), "Skin removal must restore native hair");
                yield return new WaitForEndOfFrame();
                Require(root.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0, "Skin removal must remove replacements");
                for (int part = 0; part < 2; part++)
                    Require(original[part].vertices.SequenceEqual(rest[part]), "Source mesh changed");
                result.passed = result.maximumRestError < .00005f && result.maximumMotion > .001f && result.maximumSeamError < .00005f && result.maximumScaledBakeError < .00005f;
                File.AppendAllText(Path.Combine(directory, "first-person-hair.jsonl"), JsonUtility.ToJson(result) + "\n");
                Require(result.passed, "FP hair geometry check failed: " + JsonUtility.ToJson(result));
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(cameraObject);
                foreach (Mesh mesh in baked) UnityEngine.Object.Destroy(mesh);
            }
        }

        private static Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000), Mathf.RoundToInt(p.z * 100000));

        private static Vector3[] Skin(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            Matrix4x4[] binds = mesh.bindposes;
            Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
            Matrix4x4[] matrices = bones.Select((bone, index) => inverse * bone.localToWorldMatrix * binds[index]).ToArray();
            Vector3[] positions = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < positions.Length; i++)
                positions[i] = matrices[weights[i].boneIndex0].MultiplyPoint3x4(positions[i]) * weights[i].weight0 +
                    matrices[weights[i].boneIndex1].MultiplyPoint3x4(positions[i]) * weights[i].weight1;
            return positions;
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
