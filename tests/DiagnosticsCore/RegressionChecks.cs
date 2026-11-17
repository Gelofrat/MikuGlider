using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MikuGlider.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class RegressionChecks
{
    internal static void Run(string workspace)
    {
        var raw = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
        var reference = raw.ToArray();
        var distances = new[] { 0f, 0f, 0f, .1f };
        var other = Matrix4x4.identity;
        other.m03 = .01f;
        var candidates = new[] { Matrix4x4.identity, other };
        var resolver = new ParticleSpace();
        resolver.Resolve(raw, reference, distances, candidates, new[] { 0, 1, 2, 3 });
        Require(resolver.Selected == 0 && resolver.Reliable, "initial calibrated frame");
        raw[0].x += .001f;
        raw[1].x += .001f;
        raw[2].x += .001f;
        other.m03 = -.001f;
        candidates[1] = other;
        resolver.Resolve(raw, reference, distances, candidates, new[] { 0, 1, 2, 3 });
        Require(resolver.Selected == 0, "small pin motion cannot switch the calibrated coordinate basis");
        Require(Math.Abs(resolver.Error - .001f) < 1e-5, "drift remains visible");
        raw[0].x += .015f;
        resolver.Resolve(raw, reference, distances, candidates, new[] { 0, 1, 2, 3 });
        Require(!resolver.Reliable, "misaligned coordinates suppress collision claims");
        var original = new ClothFrame { particlesWorld = raw, skinnedWorld = reference, bones = new[] { new BoneData() } };
        ClothFrame scalar = original.RecordingView(false);
        Require(scalar.particlesWorld == null && scalar.skinnedWorld == null && scalar.bones == null, "scalar samples omit large arrays");
        Require(original.particlesWorld == raw && original.bones.Length == 1, "recording projection cannot mutate the overlay snapshot");
        JArray fixtures = JArray.Parse(File.ReadAllText(Path.Combine(workspace, "tests/fixtures/skirt-regression.json")));
        foreach (JObject fixture in fixtures)
        {
            Vector3[] points = ((JArray)fixture["positions"]).Select(p => new Vector3((float)p[0], (float)p[1], (float)p[2])).ToArray();
            int[] triangles = ((JArray)fixture["triangles"]).Select(i => (int)i).ToArray();
            bool clean = (bool)fixture["clean"];
            GeometryData result = Task.Run(() =>
            {
                var analysis = new SurfaceGeometry(points, triangles, Array.Empty<Vector3>(), Array.Empty<int>());
                var data = new GeometryData();
                analysis.Analyze(points, points, Array.Empty<Vector3>(), 20000, data);
                return data;
            }).GetAwaiter().GetResult();
            Require(result.checkedEdges == result.totalEdges, "complete topology coverage");
            Require(clean ? result.selfCrossings == 0 : result.selfCrossings > 0, "recorded bad topology and repaired skirt differ");
            Require(result.stretchedEdges == 0 && result.collapsedTriangles == 0, "rest geometry is not stretched or collapsed");
            Console.WriteLine("SKIRT_REGRESSION " + fixture["name"] + " crossings=" + result.selfCrossings + " edges=" + result.checkedEdges);
        }
        Console.WriteLine("DIAGNOSTICS_REGRESSIONS_PASS");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
    }
}
