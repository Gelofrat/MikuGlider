using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using MikuGlider;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    // Render runtime surfaces directly and retain baked snapshots for inspection.
    public static class LabArtworkExport
    {
        public static string ExportAll(string folder)
        {
            Directory.CreateDirectory(folder);
            var reports = new List<object>();
            var catalog = JsonUtility.FromJson<ClipCatalog>(Resources.Load<TextAsset>("MikuPhysicsLab/animations").text);
            foreach (string variant in new[] { "v1", "v2", "v3" })
                foreach (string action in new[] { "idle", "run", "flight" })
                {
                    var actor = new LabActor(new LabVariant { key = variant, label = variant + " artwork" }, false);
                    var baked = new Mesh();
                    try
                    {
                        string clipKey = action == "flight" ? "clip-443" : action;
                        var entry = catalog.clips.Single(c => c.key == clipKey);
                        var clip = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + entry.resource);
                        float time = action == "run" ? .18f : .5f;
                        for (int frame = 0; frame <= 30; frame++)
                        {
                            actor.Garment.SendMessage("Update");
                            actor.Sample(clip, time * frame / 30, action == "flight");
                            actor.Garment.SendMessage("LateUpdate");
                        }
                        var hair = actor.Root.GetComponent<AnimatedHairVisual>();
                        hair.SendMessage("LateUpdate");
                        var hairClip = actor.Skin.Assets.Hair.GetClip(action == "flight" ? HairAnimationState.Gliding : HairAnimationState.Idle);
                        var hairBones = actor.HairBones;
                        for (int i = 0; i < hairBones.Length; i++) hairBones[i].localRotation = hairClip.Sample(.5f, i, true);
                        var pose = actor.Body.GetComponent<ModelPoseCorrections>();
                        if (pose != null && pose.enabled) pose.SendMessage("LateUpdate");
                        var parts = actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>()
                            .Where(r => r.enabled && !r.forceRenderingOff && r.sharedMesh != null).Select(renderer =>
                            {
                                renderer.BakeMesh(baked, true);
                                return new
                                {
                                    name = renderer.sharedMesh.name,
                                    positions = baked.vertices.Select(v => V(renderer.transform.TransformPoint(v))).ToArray(),
                                    normals = baked.normals.Select(v => V(renderer.transform.TransformDirection(v).normalized)).ToArray(),
                                    uv = baked.uv.Select(v => new[] { v.x, v.y }).ToArray(),
                                    submeshes = Enumerable.Range(0, baked.subMeshCount).Select(baked.GetTriangles).ToArray(),
                                    materials = renderer.sharedMaterials.Select(m => new {
                                        texture = m.mainTexture.name,
                                        cull = m.HasProperty("_Cull") ? (int)m.GetFloat("_Cull") : 0
                                    }).ToArray()
                                };
                            }).ToArray();
                        string posePath = Path.Combine(folder, variant + "-" + action + ".json");
                        File.WriteAllText(posePath,
                            JsonConvert.SerializeObject(new { variant, action, clip = clipKey, time, parts }));
                        string imageName = "miku-" + variant + "-" + action + ".png";
                        string imagePath = Path.Combine(folder, imageName);
                        float[][] frameBounds = Capture(actor, imagePath, action);
                        reports.Add(new { image = imageName, variant, action, clip = clipKey, time,
                            poseSha256 = Hash(posePath), sha256 = Hash(imagePath), frameBounds,
                            dimensions = action == "flight" ? new[] { 1600, 900 } : new[] { 1000, 1200 },
                            parts = parts.Select(p => p.name).ToArray() });
                    }
                    finally
                    {
                        actor.Dispose(); Object.DestroyImmediate(actor.Root); Object.DestroyImmediate(baked);
                    }
                }
            File.WriteAllText(Path.Combine(folder, "render-report.json"), JsonConvert.SerializeObject(reports, Formatting.Indented));
            return "Rendered nine runtime model views";
        }
        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static float[][] Capture(LabActor actor, string path, string action)
        {
            int width = action == "flight" ? 1600 : 1000, height = action == "flight" ? 900 : 1200;
            var cameraObject = new GameObject("Artwork verification camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(width * 2, height * 2, 24) { filterMode = FilterMode.Bilinear };
            var resolved = new RenderTexture(width, height, 0);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var mesh = new Mesh();
            var previous = RenderTexture.active;
            try
            {
                var points = new List<Vector3>();
                foreach (var renderer in actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!renderer.enabled || renderer.forceRenderingOff || renderer.sharedMesh == null) continue;
                    renderer.gameObject.layer = 31;
                    renderer.BakeMesh(mesh);
                    points.AddRange(mesh.vertices.Select(renderer.transform.TransformPoint));
                }
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.19f, .215f, .235f);
                camera.cullingMask = 1 << 31;
                camera.allowHDR = false;
                camera.orthographic = true;
                camera.aspect = (float)width / height;
                var offset = action == "run" ? new Vector3(-5, .25f, 0) :
                    action == "flight" ? new Vector3(-2, .8f, 6) : new Vector3(-1.3f, .6f, 6);
                camera.transform.position = bounds.center + offset;
                camera.transform.LookAt(bounds.center);
                float extent = 0;
                foreach (var point in points)
                {
                    Vector3 p = camera.transform.InverseTransformPoint(point);
                    extent = Mathf.Max(extent, Mathf.Abs(p.x) / camera.aspect, Mathf.Abs(p.y));
                }
                camera.orthographicSize = extent / .86f;
                camera.targetTexture = target;
                camera.Render();
                Graphics.Blit(target, resolved);
                RenderTexture.active = resolved;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                var projected = points.Select(camera.WorldToViewportPoint).ToArray();
                float minX = projected.Min(p => p.x), minY = projected.Min(p => p.y);
                float maxX = projected.Max(p => p.x), maxY = projected.Max(p => p.y);
                if (minX < .04f || minY < .04f || maxX > .96f || maxY > .96f)
                    throw new InvalidOperationException("Model does not fit artwork frame");
                return new[] { new[] { minX, minY }, new[] { maxX, maxY } };
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(resolved);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
