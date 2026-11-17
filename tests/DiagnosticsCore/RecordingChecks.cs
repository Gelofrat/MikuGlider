using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using MikuGlider.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class RecordingChecks
{
    internal static void Run(string workspace)
    {
        string output = Path.Combine(workspace, "scratch/diagnostics-v3-tests", DateTime.UtcNow.Ticks.ToString());
        string template = File.ReadAllText(Path.Combine(workspace, "diagnostics/viewer.html"));
        var capture = new CaptureSession(output, new { schema = 2 }, 32 * 1024 * 1024, (b, w, h) => b, template);
        var vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        Require(capture.Archive("renderers/00001.json.gz", new RendererAsset
        {
            binding = "00001", vertices = vertices, normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.up }, submeshes = new[] { new[] { 0, 1, 2 } }
        }, 8192), "metadata accepted");
        var matrix = new float[] { 2, 0, 0, 10, 0, 3, 0, 20, 0, 0, 4, 30, 0, 0, 0, 1 };
        Require(capture.Archive("poses/pose-000001.json.gz", new GeometryCapture
        {
            sample = 1, unityFrame = 99, surfaces = new[] { new SurfacePose
            {
                binding = "00001", role = "arm_left", localToWorld = matrix, vertices = vertices,
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward }
            } }
        }, 8192), "pose accepted");
        Require(capture.Frame(new FrameData
        {
            sample = 1, unityFrame = 99, realtime = 1, character = new CharacterFrame
            {
                phase = "onPostRender", perspective = "FirstPerson", skinMesh = "MikuV2", weapon = "test_weapon",
                geometryPath = "poses/pose-000001.json.gz", animations = new[] { new AnimationData { clip = "reload" } },
                renderers = new[] { new RendererFrame { role = "arm_left", binding = "00001", boneMatrices = matrix, geometryStatus = "captured" } }
            }
        }, null), "first person requires no skirt");
        for (int i = 0; i < 502; i++)
            capture.Event(new CaptureEvent { kind = "Warning", message = "test warning", utc = DateTime.UtcNow.ToString("O") });
        capture.SkippedImage();
        bool rejected = false;
        try { capture.Archive("../outside.json.gz", new { }, 1); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "unsafe archive path rejected");
        capture.Stop("recording_checks");
        Require(capture.Completion.Wait(10000) && capture.Fault == null, "writer completes");
        var report = JObject.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "summary.json")));
        Require((int)report["schema"] == 2 && (int)report["skippedImages"] == 1 && (int)report["suppressedEvents"] == 2, "schema and bounded event/image counters");
        Require((string)report["frames"][0]["perspective"] == "FirstPerson" && (string)report["frames"][0]["skin"] == "MikuV2", "first-person summary without cloth");
        using (var gzip = new GZipStream(File.OpenRead(Path.Combine(capture.DirectoryPath, "renderers/00001.json.gz")), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
        {
            var asset = JObject.Parse(reader.ReadToEnd());
            Require(asset["uv"][1].Count() == 2 && (int)asset["submeshes"][0][2] == 2, "UV and triangle archive round trip");
        }
        var cloth = new ClothFrame { surfaceWorld = vertices };
        Require(cloth.RecordingView(false).surfaceWorld == null && cloth.surfaceWorld == vertices, "surface arrays decimated without changing overlay");
        Console.WriteLine("RECORDING_CHECKS_PASS " + Path.GetFullPath(capture.DirectoryPath));
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
