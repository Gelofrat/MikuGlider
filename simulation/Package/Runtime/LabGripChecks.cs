using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MikuGlider;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Horse.MikuPhysicsLab
{
    internal static class LabGripChecks
    {
        [Serializable] private sealed class GripCase
        {
            public string variant;
            public string arm;
            public string clip;
            public string resource;
            public bool additiveSource;
            public int samples;
            public int protectedVertices;
            public int fingerVertices;
            public int sharedMaterialNailVertices;
            public int changedSkinWeights;
            public float maximumNativeDifference;
            public float maximumGripDifference;
            public float maximumGripNormalDifference;
            public float maximumBoneDifference;
            public float maximumAuthoredPoseDifference;
            public bool passed;
        }

        [Serializable] private sealed class GripReport
        {
            public bool passed;
            public string error;
            public string animationDriver = "Native Unity AnimationClipPlayable: unchanged grip, cuff, finger geometry and original bones; base plus additive composition is covered by the weapon suite";
            public GripCase[] checks;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("-labGripChecks")) return;
            var records = new List<GripCase>();
            var report = new GripReport();
            try
            {
                Require(typeof(FirstPersonWristVisual).Assembly.GetType("MikuGlider.CuffPoseController") == null,
                    "Removed cuff controller remains in the runtime assembly");
                string[] weaponKeys = { "mini-cannon", "fast-shotgun", "heavy-shotgun", "bounce-shotgun" };
                ClipEntry[] selected = JsonUtility.FromJson<ClipCatalog>(
                    Resources.Load<TextAsset>("MikuPhysicsLab/animations").text).clips
                    .Where(clip => clip.rig == "arms" &&
                        (clip.label.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         clip.label.IndexOf("MiniCannon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         clip.category == "weapon-action" && weaponKeys.Contains(clip.weaponKey))).ToArray();
                foreach (string weaponKey in weaponKeys)
                    Require(selected.Any(clip => clip.category == "weapon-action" && clip.weaponKey == weaponKey &&
                        clip.key.Contains("reload")) && selected.Any(clip => clip.category == "weapon-action" &&
                        clip.weaponKey == weaponKey && clip.key.Contains("fire")), "Fire or reload coverage missing for " + weaponKey);
                ClipEntry[] clips = selected.GroupBy(clip => clip.resource).Select(group => group.First()).ToArray();
                Require(clips.Any(clip => clip.label.Contains("MiniCannon")) &&
                    clips.Any(clip => clip.label.Contains("Shotgun")), "Native weapon animation coverage is missing");
                foreach (string variant in new[] { "v1", "v2", "v3" })
                    CheckVariant(variant, clips, records);
                Require(records.Where(record => record.maximumAuthoredPoseDifference > .001f)
                    .Select(record => record.resource).Distinct().Count() >= 10,
                    "Native animation clips did not exercise the recorded arm rig");
                report.passed = records.Count > 0 && records.All(record => record.passed);
                Debug.Log("GRIP_CHECKS_PASS cases=" + records.Count + " samples=" + records.Sum(record => record.samples));
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                Debug.LogError("GRIP_CHECKS_FAIL " + error);
            }
            finally
            {
                report.checks = records.ToArray();
                string folder = LabController.Argument("-labReports") ??
                    Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "grip-checks.json"), JsonUtility.ToJson(report, true));
            }
        }

        private static void CheckVariant(string variant, ClipEntry[] clips, List<GripCase> records)
        {
            var root = new GameObject("Grip regression " + variant);
            var rig = new LabRig("arms-rig", root.transform);
            var surfaces = new List<GripSurface>();
            try
            {
                SkinAssets assets = LabAssetLibrary.Get(variant);
                foreach (RigRenderer renderer in rig.Data.renderers)
                {
                    string part = renderer.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "arm-left" : "arm-right";
                    surfaces.Add(new GripSurface(rig, renderer, assets.GetMesh(part), variant, part));
                }
                Require(surfaces.Count == 2, "Expected both first-person hands");
                foreach (ClipEntry entry in clips)
                {
                    AnimationClip clip = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + entry.resource);
                    Require(clip != null && clip.length > 0f, "Missing native clip " + entry.label);
                    PlayableGraph graph = PlayableGraph.Create("Grip regression " + entry.key);
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        AnimationClipPlayable player = AnimationClipPlayable.Create(graph, clip);
                        player.SetApplyFootIK(false);
                        player.SetApplyPlayableIK(false);
                        player.SetSpeed(0);
                        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Native arms",
                            rig.AnimationRoot.GetComponent<Animator>());
                        output.SetSourcePlayable(player);
                        graph.Play();
                        GripCase[] cases = surfaces.Select(surface => surface.NewCase(entry)).ToArray();
                        records.AddRange(cases);
                        for (int sample = 0; sample <= 8; sample++)
                        {
                            rig.Restore();
                            var rest = rig.Nodes.Select(node => new PoseState(node)).ToArray();
                            player.SetTime(clip.length * sample / 8d);
                            graph.Evaluate(0f);
                            float authoredDifference = 0f;
                            for (int index = 0; index < rig.Nodes.Length; index++)
                                authoredDifference = Mathf.Max(authoredDifference, rest[index].Difference(rig.Nodes[index]));
                            var before = rig.Nodes.Select(node => new PoseState(node)).ToArray();
                            foreach (GripSurface surface in surfaces) surface.UpdatePose();
                            float boneDifference = 0f;
                            for (int index = 0; index < rig.Nodes.Length; index++)
                                boneDifference = Mathf.Max(boneDifference, before[index].Difference(rig.Nodes[index]));
                            Require(boneDifference < 1e-7f, "Wrist correction changed an original animation bone");
                            for (int side = 0; side < surfaces.Count; side++)
                            {
                                cases[side].maximumBoneDifference = Mathf.Max(cases[side].maximumBoneDifference, boneDifference);
                                cases[side].maximumAuthoredPoseDifference = Mathf.Max(cases[side].maximumAuthoredPoseDifference, authoredDifference);
                                surfaces[side].CheckPose(cases[side]);
                            }
                        }
                        foreach (GripCase record in cases) record.passed = true;
                    }
                    finally
                    {
                        if (graph.IsValid()) graph.Destroy();
                    }
                }
            }
            finally
            {
                foreach (GripSurface surface in surfaces) surface.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class GripSurface : IDisposable
        {
            private readonly SkinnedMeshRenderer visual;
            private readonly SkinnedMeshRenderer native;
            private readonly FirstPersonWristVisual correction;
            private readonly Mesh nativeBake = new Mesh();
            private readonly Mesh visualBake = new Mesh();
            private readonly bool[] protectedVertices;
            private readonly string variant;
            private readonly string arm;
            private readonly int fingerVertices;
            private readonly int sharedMaterialNailVertices;
            private readonly int changedSkinWeights;
            private readonly Mesh source;
            private readonly Vector3[] sourcePositions;
            private readonly Vector3[] sourceNormals;
            private readonly Vector2[] sourceUV;
            private readonly BoneWeight[] sourceWeights;

            internal GripSurface(LabRig rig, RigRenderer renderer, Mesh source, string variant, string arm)
            {
                this.variant = variant;
                this.arm = arm;
                this.source = source;
                sourcePositions = source.vertices;
                sourceNormals = source.normals;
                sourceUV = source.uv;
                sourceWeights = source.boneWeights;
                Transform parent = rig.Nodes[renderer.node];
                Transform[] bones = renderer.bones.Select(index => rig.Nodes[index]).ToArray();
                visual = parent.gameObject.AddComponent<SkinnedMeshRenderer>();
                Configure(visual, source, bones, rig.Nodes[renderer.root], renderer.quality);
                var reference = new GameObject("Unmodified mesh reference");
                reference.transform.SetParent(parent, false);
                native = reference.AddComponent<SkinnedMeshRenderer>();
                Configure(native, source, bones, rig.Nodes[renderer.root], renderer.quality);
                FirstPersonWristVisual.Attach(visual, variant == "v2" ? source : null);
                correction = visual.GetComponent<FirstPersonWristVisual>();
                Mesh result = visual.sharedMesh;
                Vector3[] points = source.vertices;
                Vector2[] uv = source.uv;
                BoneWeight[] originalWeights = source.boneWeights;
                BoneWeight[] correctedWeights = result.boneWeights;
                protectedVertices = new bool[points.Length];
                Require(points.SequenceEqual(result.vertices) && source.normals.SequenceEqual(result.normals) &&
                    uv.SequenceEqual(result.uv), "Wrist correction modified source geometry or texture coordinates");
                Require(result.blendShapeCount == source.blendShapeCount, "Unexpected cuff blendshape remains");
                for (int index = 0; index < result.blendShapeCount; index++)
                    Require(result.GetBlendShapeName(index).IndexOf("Cuff", StringComparison.OrdinalIgnoreCase) < 0,
                        "Removed cuff blendshape remains");
                Require(source.subMeshCount == result.subMeshCount, "Wrist correction changed material slots");
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                    Require(source.GetIndices(submesh).SequenceEqual(result.GetIndices(submesh)), "Wrist correction changed topology");
                Matrix4x4[] binds = source.bindposes;
                Vector3 wrist = binds[2].inverse.MultiplyPoint3x4(Vector3.zero);
                Vector3 axis = (wrist - binds[1].inverse.MultiplyPoint3x4(Vector3.zero)).normalized;
                for (int index = 0; index < points.Length; index++)
                {
                    BoneWeight weight = originalWeights[index];
                    if (!EquivalentWeights(weight, correctedWeights[index])) changedSkinWeights++;
                    bool finger = FingerWeight(bones, weight.boneIndex0, weight.weight0) +
                        FingerWeight(bones, weight.boneIndex1, weight.weight1) +
                        FingerWeight(bones, weight.boneIndex2, weight.weight2) +
                        FingerWeight(bones, weight.boneIndex3, weight.weight3) > 0f;
                    if (finger) fingerVertices++;
                    if (variant == "v3" && finger && IsNailMaterial(uv[index])) sharedMaterialNailVertices++;
                    bool unchanged = EquivalentWeights(weight, correctedWeights[index]);
                    bool forearm = weight.boneIndex0 == 1 && weight.weight0 > 0f ||
                        weight.boneIndex1 == 1 && weight.weight1 > 0f ||
                        weight.boneIndex2 == 1 && weight.weight2 > 0f ||
                        weight.boneIndex3 == 1 && weight.weight3 > 0f;
                    protectedVertices[index] = correction == null || finger || unchanged ||
                        !forearm && Vector3.Dot(points[index] - wrist, axis) >= 0f;
                    if (finger)
                        Require(EquivalentWeights(weight, correctedWeights[index]), "Wrist correction changed protected grip weights: " + variant + "/" + arm + "/" + index);
                }
                Require(fingerVertices > 100, "Grip fixture does not cover fingers");
                Require(variant != "v3" || sharedMaterialNailVertices >= 100, "Grip fixture does not cover v3 shared-material nails");
                if (variant == "v2" && arm == "arm-left")
                    Require(result != source && correction != null && visual.bones.Length == bones.Length + 9,
                        "Support arm correction was not exercised");
                else
                    Require(result == source && correction == null && visual.bones.SequenceEqual(bones),
                        variant + " no longer uses its native mesh and bones");
                for (int index = 0; index < bones.Length; index++)
                    Require(visual.bones[index] == bones[index], "Original bone was replaced");
            }

            private static void Configure(SkinnedMeshRenderer renderer, Mesh mesh, Transform[] bones, Transform root, int quality)
            {
                renderer.sharedMesh = mesh;
                renderer.bones = bones;
                renderer.rootBone = root;
                renderer.quality = (SkinQuality)quality;
                renderer.updateWhenOffscreen = true;
                renderer.forceRenderingOff = true;
            }

            internal void UpdatePose() => correction?.UpdatePose();

            private static bool EquivalentWeights(BoneWeight a, BoneWeight b) =>
                a.weight0 == b.weight0 && a.weight1 == b.weight1 && a.weight2 == b.weight2 && a.weight3 == b.weight3 &&
                (a.weight0 == 0 || a.boneIndex0 == b.boneIndex0) && (a.weight1 == 0 || a.boneIndex1 == b.boneIndex1) &&
                (a.weight2 == 0 || a.boneIndex2 == b.boneIndex2) && (a.weight3 == 0 || a.boneIndex3 == b.boneIndex3);

            internal GripCase NewCase(ClipEntry clip) => new GripCase
            {
                variant = variant, arm = arm, clip = clip.label, resource = clip.resource, additiveSource = clip.additive,
                protectedVertices = protectedVertices.Count(value => value), fingerVertices = fingerVertices,
                sharedMaterialNailVertices = sharedMaterialNailVertices, changedSkinWeights = changedSkinWeights
            };

            internal void CheckPose(GripCase record)
            {
                native.BakeMesh(nativeBake);
                visual.BakeMesh(visualBake);
                Vector3[] expected = nativeBake.vertices, actual = visualBake.vertices;
                Vector3[] expectedNormals = nativeBake.normals, actualNormals = visualBake.normals;
                for (int index = 0; index < expected.Length; index++)
                {
                    float difference = Vector3.Distance(expected[index], actual[index]);
                    Require(float.IsFinite(difference), "Nonfinite grip geometry");
                    record.maximumNativeDifference = Mathf.Max(record.maximumNativeDifference, difference);
                    if (!protectedVertices[index]) continue;
                    record.maximumGripDifference = Mathf.Max(record.maximumGripDifference, difference);
                    record.maximumGripNormalDifference = Mathf.Max(record.maximumGripNormalDifference,
                        Vector3.Distance(expectedNormals[index], actualNormals[index]));
                }
                record.samples++;
                Require(sourcePositions.SequenceEqual(source.vertices) && sourceNormals.SequenceEqual(source.normals) &&
                    sourceUV.SequenceEqual(source.uv) && sourceWeights.SequenceEqual(source.boneWeights),
                    "Cached source mesh changed during native animation playback");
                Require(record.maximumGripDifference < .00003f && record.maximumGripNormalDifference < .00003f,
                    "Native grip changed: " + JsonUtility.ToJson(record));
                if (correction == null)
                    Require(record.maximumNativeDifference < .00003f, variant + " mesh differs from native animation");
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(nativeBake);
                UnityEngine.Object.DestroyImmediate(visualBake);
            }
        }

        private readonly struct PoseState
        {
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;

            internal PoseState(Transform value)
            {
                position = value.localPosition;
                rotation = value.localRotation;
                scale = value.localScale;
            }

            internal float Difference(Transform value) => Mathf.Max(Vector3.Distance(position, value.localPosition),
                Mathf.Max(Vector3.Distance(scale, value.localScale), Mathf.Max(Mathf.Abs(rotation.x - value.localRotation.x),
                    Mathf.Max(Mathf.Abs(rotation.y - value.localRotation.y), Mathf.Max(Mathf.Abs(rotation.z - value.localRotation.z),
                        Mathf.Abs(rotation.w - value.localRotation.w))))));
        }

        private static float FingerWeight(Transform[] bones, int index, float weight)
        {
            if (weight <= 0f) return 0f;
            string name = bones[index].name;
            return name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") ||
                name.Contains("Ring") || name.Contains("Pinky") ? weight : 0f;
        }

        private static bool IsNailMaterial(Vector2 uv)
        {
            Vector2 pixel = uv * 4096f;
            return pixel.x >= 1183.95f && pixel.x <= 1696.05f && pixel.y >= 1183.95f && pixel.y <= 1696.05f;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
