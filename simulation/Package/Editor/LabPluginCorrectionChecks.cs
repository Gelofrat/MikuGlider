using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabPluginCorrectionChecks
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public static string Run(string pluginPath, string captureRoot, string output)
        {
            Directory.CreateDirectory(output);
            Assembly plugin = Assembly.Load(File.ReadAllBytes(pluginPath));
            Type profiles = plugin.GetType("MikuGlider.ModelCorrections", true);
            Type behaviour = plugin.GetType("MikuGlider.ModelPoseCorrections", true);
            MethodInfo parse = profiles.GetMethod("Parse", Members);
            var results = new List<object>();
            foreach (string variant in new[] { "v1", "v2", "v3" })
            {
                string prefix = variant == "v1" ? "MikuGlider" : "MikuGliderV" + variant[1];
                string text;
                using (var reader = new StreamReader(plugin.GetManifestResourceStream(prefix + ".Corrections"))) text = reader.ReadToEnd();
                object profile = parse.Invoke(null, new object[] { text });
                Array edits = (Array)profiles.GetField("meshes").GetValue(profile);
                int expected = JArray.Parse(JObject.Parse(text)["meshes"].ToString()).Count;
                Require(edits.Length == expected, "Compiled plugin lost correction entries: " + prefix);
                if (variant == "v2") continue;
                Require(expected == 1, "Missing sleeve correction: " + prefix);
                string capture = variant == "v1" ? "20261114-205404-512-cc5e2b" : "20261114-205338-592-540a17";
                results.Add(CheckCapture(variant, Path.Combine(captureRoot, capture), output, behaviour, profile, JObject.Parse(text)));
            }
            var weight = new BoneWeight { boneIndex0 = 7, weight0 = .7f, boneIndex1 = 3, weight1 = .3f };
            string weightJson = "{\"schemaVersion\":1,\"meshes\":[{\"key\":\"test\",\"fingerprint\":\"test\",\"weights\":[{\"vertex\":2,\"value\":" + JsonUtility.ToJson(weight) + "}],\"poses\":[]}]}";
            object weightProfile = parse.Invoke(null, new object[] { weightJson });
            object meshEdit = ((Array)profiles.GetField("meshes").GetValue(weightProfile)).GetValue(0);
            object vertexEdit = ((Array)meshEdit.GetType().GetField("weights").GetValue(meshEdit)).GetValue(0);
            Require(weight.Equals(vertexEdit.GetType().GetField("value").GetValue(vertexEdit)), "Saved bone weights changed in plugin");
            bool rejected = false;
            try { parse.Invoke(null, new object[] { "{\"schemaVersion\":1}" }); }
            catch (TargetInvocationException error) when (error.InnerException is JsonException) { rejected = true; }
            Require(rejected, "Incomplete correction file silently accepted");
            string result = JsonConvert.SerializeObject(new { version = plugin.GetName().Version.ToString(), passed = true, variants = results }, Formatting.Indented);
            File.WriteAllText(Path.Combine(output, "plugin-checks.json"), result);
            return result;
        }

        private static object CheckCapture(string variant, string capture, string output, Type behaviour, object profile, JObject document)
        {
            JObject dump;
            using (var file = File.OpenRead(Path.Combine(capture, "renderers/00001.json.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip)) dump = JObject.Parse(reader.ReadToEnd());
            var host = new GameObject("Compiled plugin sleeve check");
            var renderer = host.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = Object.Instantiate(LabAssetLibrary.Get(variant).GetMesh("arm-left"));
            mesh.name = variant + ".arm-left";
            mesh.ClearBlendShapes();
            var baked = new Mesh();
            Transform[] bones = dump["bonePaths"].Select(path => new GameObject(path.ToString().Split('/').Last()).transform).ToArray();
            int[] parents = dump["boneParents"].ToObject<int[]>();
            for (int i = 0; i < bones.Length; i++) bones[i].SetParent(parents[i] >= 0 ? bones[parents[i]] : host.transform, false);
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.quality = SkinQuality.Bone4;
            renderer.updateWhenOffscreen = true;
            MethodInfo attach = behaviour.GetMethod("Attach", Members), tick = behaviour.GetMethod("LateUpdate", Members);
            int frames = 0, active = 0;
            float maximumOutsideEdit = 0, maximumDelta = 0;
            float minimumWeight = 100, maximumWeight = 0;
            int[] edited = document["meshes"][0]["poses"][0]["vertices"].ToObject<int[]>();
            var mask = new HashSet<int>(edited);
            bool saved = false;
            try
            {
                attach.Invoke(null, new object[] { renderer, profile, null, "" });
                var component = renderer.GetComponent(behaviour);
                Require(component != null && mesh.blendShapeCount == 1, "Plugin did not attach sleeve shape: " + variant);
                foreach (string line in File.ReadLines(Path.Combine(capture, "frames.jsonl")))
                {
                    JObject row = JObject.Parse(line);
                    JToken character = row["character"];
                    if (character?["perspective"]?.ToString() != "FirstPerson") continue;
                    JToken arm = character["renderers"].First(r => r["role"].ToString() == "arm_left");
                    float[] matrices = arm["boneMatrices"].ToObject<float[]>();
                    SetTransform(host.transform, ReadMatrix(arm["localToWorld"].ToObject<float[]>(), 0));
                    for (int i = 0; i < bones.Length; i++) SetTransform(bones[i], ReadMatrix(matrices, i * 16));
                    Matrix4x4[] frozen = bones.Select(b => b.localToWorldMatrix).ToArray();
                    renderer.SetBlendShapeWeight(0, 0);
                    renderer.BakeMesh(baked);
                    Vector3[] before = baked.vertices;
                    tick.Invoke(component, null);
                    float weight = renderer.GetBlendShapeWeight(0);
                    minimumWeight = Mathf.Min(minimumWeight, weight);
                    maximumWeight = Mathf.Max(maximumWeight, weight);
                    renderer.BakeMesh(baked);
                    Vector3[] after = baked.vertices;
                    float delta = 0;
                    for (int v = 0; v < before.Length; v++)
                    {
                        float distance = Vector3.Distance(before[v], after[v]);
                        delta = Mathf.Max(delta, distance);
                        if (!mask.Contains(v)) maximumOutsideEdit = Mathf.Max(maximumOutsideEdit, distance);
                    }
                    Require(maximumOutsideEdit < .00001f, "Sleeve correction moved the hand or fingers: " + variant);
                    for (int i = 0; i < bones.Length; i++) Require(frozen[i] == bones[i].localToWorldMatrix, "Plugin moved native grip bones");
                    if (weight > 99) { active++; Require(delta > .01f, "Active shape did not affect baked geometry"); }
                    maximumDelta = Mathf.Max(maximumDelta, delta);
                    if (!saved && weight > 99)
                    {
                        File.WriteAllText(Path.Combine(output, variant + "-baked.json"), JsonConvert.SerializeObject(new
                        {
                            frame = row, before = before.Select(p => new[] { p.x, p.y, p.z }).ToArray(),
                            after = after.Select(p => new[] { p.x, p.y, p.z }).ToArray(), weight
                        }));
                        saved = true;
                    }
                    frames++;
                }
                Require(active > 100 && minimumWeight == 0, "Recording did not cover active and inactive poses");
                attach.Invoke(null, new object[] { renderer, profile, null, "" });
                Require(mesh.blendShapeCount == 1, "Reapplying skin duplicated correction");
                var neutral = new Mesh { name = "native.arm-left" };
                renderer.sharedMesh = neutral;
                tick.Invoke(component, null);
                Require(!((Behaviour)component).enabled, "Correction remained active on native skin");
                renderer.sharedMesh = mesh;
                attach.Invoke(null, new object[] { renderer, profile, null, "" });
                Require(((Behaviour)component).enabled, "Returning to Miku did not reactivate correction");
                Object.DestroyImmediate(neutral);
                host.SetActive(false);
                if (!Application.isPlaying) behaviour.GetMethod("OnDisable", Members).Invoke(component, null);
                Require(renderer.GetBlendShapeWeight(0) == 0, "Disabling renderer left correction weight");
                return new { variant, frames, active, minimumWeight, maximumWeight, maximumDelta, maximumOutsideEdit };
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(baked);
            }
        }

        private static Matrix4x4 ReadMatrix(float[] data, int offset)
        {
            var matrix = new Matrix4x4();
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) matrix[row, column] = data[offset + row * 4 + column];
            return matrix;
        }

        private static void SetTransform(Transform target, Matrix4x4 matrix)
        {
            target.SetPositionAndRotation(matrix.GetColumn(3), matrix.rotation);
            Vector3 scale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            Vector3 world = matrix.lossyScale;
            target.localScale = new Vector3(world.x / scale.x, world.y / scale.y, world.z / scale.z);
        }

        private static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
    }
}
