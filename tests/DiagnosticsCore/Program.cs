using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MikuGlider.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class Program
{
    private static int checks;

    private static int Main(string[] args)
    {
        string managed = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string name = new AssemblyName(request.Name).Name + ".dll";
            foreach (string folder in new[] { managed, Path.GetFullPath(Path.Combine(managed, "../../BepInEx/core")) })
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try
        {
            Run(args[0]);
            Console.WriteLine("DIAGNOSTICS_CHECKS_PASS assertions=" + checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Run(string workspace)
    {
        Geometry();
        Coordinates();
        InitializationMapping();
        Motion();
        Json();
        string output = Path.Combine(workspace, "scratch", "diagnostics-tests", DateTime.UtcNow.Ticks.ToString());
        Directory.CreateDirectory(output);
        Control(output);
        Sessions(output, File.ReadAllText(Path.Combine(workspace, "diagnostics/viewer.html")));
        Metadata(workspace);
        RegressionChecks.Run(workspace);
        RecordingChecks.Run(workspace);
    }

    private static void Geometry()
    {
        Near(GeometryMath.CapsuleDistance(new Vector3(.15f, .5f, 0), Vector3.zero, Vector3.up, .2f, .2f), -.05f, "capsule penetration");
        Near(GeometryMath.CapsuleDistance(new Vector3(.3f, .5f, 0), Vector3.zero, Vector3.up, .2f, .2f), .1f, "capsule outside");
        Near(GeometryMath.CapsuleDistance(new Vector3(0, 1.3f, 0), Vector3.zero, Vector3.up, .2f, .2f), .1f, "capsule endcap");
        Near(GeometryMath.CapsuleDistance(Vector3.zero, Vector3.zero, Vector3.zero, .2f, .3f), -.3f, "degenerate pair is sphere");
        Near(GeometryMath.CapsuleDistance(Vector3.zero, Vector3.zero, Vector3.up * .1f, .4f, .1f), -.4f, "containing sphere");
        Vector3 point = new Vector3(.3f, .5f, 0);
        float expected = float.MaxValue;
        for (int index = 0; index <= 10000; index++)
        {
            float t = index / 10000f;
            expected = Math.Min(expected, (point - Vector3.up * t).magnitude - (.1f + .2f * t));
        }
        Near(GeometryMath.CapsuleDistance(point, Vector3.zero, Vector3.up, .1f, .3f), expected, "tapered hull exact minimum", .00001f);
        Vector3 a = Vector3.zero;
        Vector3 b = Vector3.right;
        Vector3 c = Vector3.up;
        Check(GeometryMath.SegmentTriangle(new Vector3(.2f, .2f, -1), new Vector3(.2f, .2f, 1), a, b, c, .001f, out Vector3 hit), "triangle crossing");
        Near(hit.z, 0, "crossing location");
        Check(!GeometryMath.SegmentTriangle(new Vector3(2, 2, -1), new Vector3(2, 2, 1), a, b, c, .001f, out _), "miss");
        Check(!GeometryMath.SegmentTriangle(new Vector3(.2f, .2f, 0), new Vector3(.2f, .2f, 1), a, b, c, .001f, out _), "endpoint contact excluded");
        Check(!GeometryMath.SegmentTriangle(new Vector3(.1f, .1f, 0), new Vector3(.5f, .2f, 0), a, b, c, .001f, out _), "coplanar excluded");
        var tree = new TriangleIndex(new[] { a, b, c }, new[] { 0, 1, 2 }, new[] { 10, 11, 12 });
        Check(tree.Intersect(new Vector3(.2f, .2f, -1), new Vector3(.2f, .2f, 1), -1, -1, out _), "BVH crossing");
        Check(!tree.Intersect(new Vector3(.2f, .2f, -1), new Vector3(.2f, .2f, 1), 10, -1, out _), "logical adjacency excluded");
        tree.Update(new[] { a + Vector3.forward * 4, b + Vector3.forward * 4, c + Vector3.forward * 4 });
        Check(!tree.Intersect(new Vector3(.2f, .2f, -1), new Vector3(.2f, .2f, 1), -1, -1, out _), "BVH refit follows body");
        var random = new System.Random(711);
        Vector3[] points = Enumerable.Range(0, 400).Select(_ => new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble())).ToArray();
        var indexTree = new PointIndex(points);
        for (int index = 0; index < 200; index++)
        {
            Vector3 query = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            Near(indexTree.DistanceSquared(query), points.Min(v => (query - v).sqrMagnitude), "KD exact nearest", 1e-7f);
        }
    }

    private static void Coordinates()
    {
        Vector3[] raw = { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
        Matrix4x4 translated = Matrix4x4.identity;
        translated.m03 = 30;
        translated.m13 = 2;
        Matrix4x4[] candidates = { Matrix4x4.identity, translated };
        Vector3[] reference = raw.Select(translated.MultiplyPoint3x4).Reverse().ToArray();
        var resolver = new ParticleSpace();
        resolver.Resolve(raw, reference, new[] { 0f, 0f, 0f, .1f }, candidates);
        Check(resolver.Selected == 1 && resolver.Reliable, "world/local frame resolved despite reordered vertices");
        translated.m03 = 34;
        reference = raw.Select(translated.MultiplyPoint3x4).Reverse().ToArray();
        candidates[1] = translated;
        resolver.Resolve(raw, reference, new[] { 0f, 0f, 0f, .1f }, candidates);
        Check(resolver.Reliable, "pinned mapping follows moving root");
        raw[0] += Vector3.forward * .015f;
        resolver.Resolve(raw, reference, new[] { 0f, 0f, 0f, .1f }, candidates);
        Near(resolver.Error, .015f, "fixed pinned correspondence reveals drift");
        raw[0] += Vector3.forward * .1f;
        resolver.Resolve(raw, reference, new[] { 0f, 0f, 0f, .1f }, candidates);
        Check(!resolver.Reliable, "uncertain coordinates rejected");
        resolver = new ParticleSpace();
        resolver.Resolve(raw, reference, new[] { .1f, .1f, .1f, .1f }, candidates);
        Check(!resolver.Reliable, "no anchors cannot establish coordinates");
    }

    private static void Motion()
    {
        var motion = new ParticleMotion();
        float[] distances = { .1f, 0f };
        for (int frame = 0; frame < 8; frame++)
        {
            var data = new ClothFrame();
            motion.Measure(new[] { Vector3.right * frame * .02f, Vector3.up * frame }, distances, .02f, 4, 90, data);
            Check(data.speedOutliers == 0 && data.accelerationOutliers == 0, "steady movement is not jitter and pins excluded");
            if (frame > 0)
                Near(data.maximumRelativeSpeed, 1, "relative velocity");
        }
        var jump = new ClothFrame();
        motion.Measure(new[] { Vector3.right * 1.2f, Vector3.zero }, distances, .02f, 4, 90, jump);
        Check(jump.speedOutliers == 1 && jump.accelerationOutliers == 1 && jump.jitterIndices.SequenceEqual(new[] { 0 }), "spike localized");
        var gap = new ClothFrame();
        motion.Measure(new[] { Vector3.right * 10, Vector3.zero }, distances, .3f, 4, 90, gap);
        Check(gap.issues == ClothIssue.None, "sample gap resets derivatives");
        motion.Reset();
        var reset = new ClothFrame();
        motion.Measure(new[] { Vector3.right * 100, Vector3.zero }, distances, .02f, 4, 90, reset);
        Check(reset.issues == ClothIssue.None, "teleport reset");
    }

    private static void InitializationMapping()
    {
        Vector3[] rest = { Vector3.zero, Vector3.up, Vector3.right, Vector3.right };
        Vector3 offset = new Vector3(10, 2, -3);
        Vector3[] posed = rest.Select(p => p + offset).ToArray();
        Vector3[] particles = { posed[2], posed[1], posed[0] };
        Vector3[] original = particles.ToArray();
        ClothVertexMapping map = ClothVertexMapping.Create(particles, rest, new[] { posed });
        Check(map.Reliable && map.MeshToParticle.SequenceEqual(new[] { 2, 1, 0, 0 }), "welded and reordered initialization mapping");
        Vector3[] deformed = { particles[0] + Vector3.forward * .2f, particles[1], particles[2] };
        Vector3[] surface = map.Surface(deformed);
        Check(surface[2] == deformed[0] && surface[3] == deformed[0], "actual cloth deformation reconstructs UV seam duplicates");
        Check(particles.SequenceEqual(original) && posed.SequenceEqual(rest.Select(p => p + offset)), "observer never mutates initialization arrays");
        map = ClothVertexMapping.Create(particles, rest.Concat(new[] { Vector3.right * 100 }).ToArray(),
            new[] { posed.Concat(new[] { Vector3.right * 100 }).ToArray() });
        Check(!map.Reliable, "unmatched source vertex rejects uncertain topology");
        bool rejected = false;
        try { map.Surface(new[] { Vector3.zero }); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "particle-layout change invalidates reconstruction");
    }

    private static void Json()
    {
        string json = CaptureJson.Write(new { point = new Vector3(float.NaN, float.PositiveInfinity, 3), text = "</script><script>bad</script>", issue = ClothIssue.BodyCrossing | ClothIssue.SelfCrossing });
        var parsed = JObject.Parse(json);
        Check(parsed["point"].Count() == 3 && parsed["point"][0].Type == JTokenType.Null, "invalid numbers use explicit null");
        Check(!json.Contains("</script>"), "embedded viewer JSON cannot close script element");
        Check((string)parsed["issue"] == "BodyCrossing, SelfCrossing", "readable flags");
        Check(JObject.Parse(CaptureJson.Write(new { number = float.PositiveInfinity }))["number"].Type == JTokenType.Null, "invalid scalar sanitized");
    }

    private static void Control(string root)
    {
        var control = new LocalControl(root);
        string path = Path.Combine(root, "control/command.json");
        File.WriteAllText(path, "{\"id\":\"1\",\"action\":\"start\",\"label\":\"jump\"}");
        Check(control.Read().action == "start", "local command");
        Check(control.Read() == null, "command IDs are idempotent");
        File.WriteAllText(path, "{");
        Check(control.Read() == null, "partial write tolerated");
        File.WriteAllText(path, CaptureJson.Write(new ControlCommand { id = "2", action = "start", label = new string('x', 121) }));
        Check(control.Read() == null && control.LastResult == "label_too_long", "bounded labels");
        control.Status(new { state = "idle" });
        control.Status(new { state = "recording" });
        Check((string)JObject.Parse(File.ReadAllText(Path.Combine(root, "status.json")))["state"] == "recording", "atomic status replacement");
        File.WriteAllText(path, "{\"id\":\"old\",\"action\":\"start\"}");
        var restarted = new LocalControl(root);
        Check(restarted.Read() == null && restarted.LastResult == "ignored_existing_command", "startup never replays an old recording command");
    }

    private static void Sessions(string root, string template)
    {
        var session = new CaptureSession(root, new { schema = 1 }, 1024 * 1024, (data, w, h) => new byte[] { 137, 80, 78, 71 }, template);
        Check(session.Frame(new FrameData { sample = 1, unityFrame = 22, realtime = 3, cloth = new ClothFrame { skin = "Miku", issues = ClothIssue.ColliderPenetration, maximumPenetration = .01f } }, null), "frame queued");
        Check(session.LeaseImage(), "GPU lease acquired");
        session.Stop("test");
        Check(!session.Completion.Wait(30), "stop waits for GPU callback without blocking caller");
        session.Image(new ImageData { sample = 1, unityFrame = 22, realtime = 3, width = 1, height = 1 }, new byte[4]);
        session.ReleaseImage();
        Check(session.Completion.Wait(5000) && session.Fault == null, "writer drains and completes");
        var summary = JObject.Parse(File.ReadAllText(Path.Combine(session.DirectoryPath, "summary.json")));
        Check(summary["frames"].Count() == 1 && summary["images"].Count() == 1, "viewer includes telemetry and images");
        Check((int)summary["images"][0]["unityFrame"] == 22, "GPU completion retains original frame ID");
        Check(File.ReadAllText(Path.Combine(session.DirectoryPath, "index.html")).Contains("\"unityFrame\":22"), "standalone viewer data embedded");
        Check(!session.LeaseImage(), "closed session refuses new readbacks");
        var encoderFailure = new CaptureSession(root, new { schema = 1 }, 1024 * 1024,
            (data, width, height) => throw new InvalidOperationException("synthetic encoder fault"), template);
        encoderFailure.Frame(new FrameData { sample = 1 }, null);
        Check(encoderFailure.LeaseImage(), "encoder failure lease");
        encoderFailure.Image(new ImageData { sample = 1, width = 1, height = 1 }, new byte[4]);
        encoderFailure.ReleaseImage();
        encoderFailure.Stop("test");
        Check(encoderFailure.Completion.Wait(5000) && encoderFailure.Fault == null, "image encoder failure preserves telemetry");
        JObject failedImage = JObject.Parse(File.ReadAllText(Path.Combine(encoderFailure.DirectoryPath, "summary.json")));
        Check(failedImage["frames"].Count() == 1 && ((string)failedImage["images"][0]["error"]).Contains("synthetic encoder fault"), "encoding error recorded explicitly");
        var budget = new CaptureSession(root, new { schema = 1 }, 128 * 1024, (data, w, h) => data, template);
        for (int index = 0; index < 500; index++)
            budget.Frame(new FrameData { sample = index, realtime = index }, null);
        budget.Stop("test");
        Check(budget.Completion.Wait(5000) && budget.Fault == null, "budget writer completes");
        Check(budget.BudgetReached, "byte limit enforced");
        Check(Directory.GetFiles(budget.DirectoryPath, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) <= 128 * 1024, "reports fit reserved disk budget");
        using (var release = new ManualResetEventSlim())
        {
            var pressure = new CaptureSession(root, new { schema = 1 }, 1024 * 1024, (data, w, h) => { release.Wait(2000); return data; }, template);
            Check(pressure.LeaseImage(), "queue test lease");
            pressure.Image(new ImageData { width = 1, height = 1 }, new byte[4]);
            pressure.ReleaseImage();
            for (int index = 0; index < 200; index++)
                pressure.Frame(new FrameData { sample = index }, null);
            Check(pressure.Dropped > 0, "bounded queue drops instead of blocking game");
            pressure.Stop("test");
            release.Set();
            Check(pressure.Completion.Wait(5000), "pressure queue drains");
        }
        Console.WriteLine("DIAGNOSTIC_SAMPLE_SESSION " + session.DirectoryPath);
    }

    private static void Near(float actual, float expected, string label, float tolerance = .0001f)
    {
        Check(Math.Abs(actual - expected) <= tolerance, label + " actual=" + actual + " expected=" + expected);
    }

    private static void Metadata(string workspace)
    {
        Assembly plugin = Assembly.LoadFrom(Path.Combine(workspace, "diagnostics/bin/Release/MikuGlider.Diagnostics.dll"));
        Type type = plugin.GetType("MikuGlider.Diagnostics.DiagnosticsPlugin", true);
        var attributes = type.GetCustomAttributesData();
        var identity = attributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        Check((string)identity.ConstructorArguments[0].Value == "horse.mycopunk.mikuglider.diagnostics", "separate plugin identity");
        Check(!plugin.GetReferencedAssemblies().Any(a => a.Name == "MikuGlider"), "no assembly dependency on main plugin");
        Check(plugin.GetManifestResourceNames().SequenceEqual(new[] { "MikuGlider.Diagnostics.Viewer" }), "no models or game assets embedded");
        var mod = attributes.Single(a => a.AttributeType.Name == "MycoModAttribute");
        Type flags = mod.ConstructorArguments[1].ArgumentType;
        Check(Convert.ToInt64(mod.ConstructorArguments[1].Value) == Convert.ToInt64(Enum.Parse(flags, "IsClientSide")), "diagnostics is client-side");
        Assembly main = Assembly.LoadFrom(Path.Combine(workspace, "bin/Release/MikuGlider.dll"));
        Type visual = main.GetType("MikuGlider.SkirtGarmentVisual", true);
        foreach (string name in new[] { "skin", "body", "skirt", "owner", "ready", "failed" })
            Check(visual.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) != null, "compatible read-only field " + name);
        Check(visual.GetProperty("UsesPoseRig", BindingFlags.NonPublic | BindingFlags.Instance) != null, "pose rig diagnostics contract");
        Check(!main.GetReferencedAssemblies().Any(a => a.Name == "UnityEngine.ClothModule"), "no native Cloth runtime dependency");
        Check(!plugin.GetReferencedAssemblies().Any(a => a.Name == "UnityEngine.ClothModule"), "no native Cloth diagnostics dependency");
        Check(plugin.GetTypes().All(t => t.GetCustomAttributesData().All(a => a.AttributeType.FullName != "HarmonyLib.HarmonyPatch")), "no broad gameplay patch attributes");
    }

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition)
            throw new InvalidOperationException(label);
    }
}
