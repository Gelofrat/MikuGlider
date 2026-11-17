using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MikuGlider;
using MikuGlider.Diagnostics;
using UnityEngine;

internal static class SkirtPanelChecks
{
    internal static void Run(string workspace, MeshData[] skirts)
    {
        var solvers = skirts.Select(m => new SkirtPanelSolver(m.Vertices)).ToArray();
        float worstPenetration = 0f;
        float worstShift = 0f;
        int crossings = 0;
        var geometry = skirts.Select(m => new SurfaceGeometry(m.Vertices, m.Submeshes[0], new[] { new Vector3(100f,0f,0f),new Vector3(100f,1f,0f),new Vector3(100f,0f,1f) },new[] {0,1,2})).ToArray();
        double milliseconds = 0;
        int cases;
        var saved = new Dictionary<string, float>();
        using (var input = new BinaryReader(File.OpenRead(Path.Combine(workspace, "scratch/review-0.8.2/panel-poses.bin"))))
        {
            cases = input.ReadInt32();
            for (int sample = 0; sample < cases; sample++)
            {
                int variant = input.ReadInt32();
                string label = Encoding.UTF8.GetString(input.ReadBytes(input.ReadInt32()));
                var collisions = new SimulationCapsule[3];
                for (int index = 0; index < 3; index++)
                    collisions[index] = new SimulationCapsule(Read(input), Read(input), input.ReadSingle(), input.ReadSingle());
                var watch = Stopwatch.StartNew();
                SkirtPanelSolver solver = solvers[variant];
                solver.Step(collisions, 0f);
                milliseconds += watch.Elapsed.TotalMilliseconds;
                Vector3[] positions = skirts[variant].Vertices.Select(solver.Pose).ToArray();
                var result = new GeometryData();
                geometry[variant].Analyze(positions, skirts[variant].Vertices, new[] { new Vector3(100f,0f,0f),new Vector3(100f,1f,0f),new Vector3(100f,0f,1f) }, 10000, result);
                crossings += result.selfCrossings;
                float maxPenetration = 0f;
                float shift = 0f;
                for (int index = 0; index < positions.Length; index++)
                {
                    Vector3 rest = skirts[variant].Vertices[index];
                    Vector3 point = positions[index];
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)) throw new Exception("Nonfinite skirt");
                    if (solver.Influence(rest) == 0f && Vector3.Distance(point, rest) > 0.000001f) throw new Exception("Moved waist");
                    foreach (SimulationCapsule capsule in collisions)
                        maxPenetration = Math.Max(maxPenetration, capsule.Penetration(point));
                    shift = Math.Max(shift, Vector3.Distance(point, rest));
                }
                if (maxPenetration > worstPenetration)
                    Console.WriteLine("PANEL_WORST " + label + " penetration=" + maxPenetration);
                worstPenetration = Math.Max(worstPenetration, maxPenetration);
                worstShift = Math.Max(worstShift, shift);
                string group = variant + "-" + label.Substring(label.LastIndexOf('_') + 1);
                if (!saved.TryGetValue(group, out float previous) || maxPenetration > previous)
                {
                    saved[group] = maxPenetration;
                    using (var output = new BinaryWriter(File.Create(Path.Combine(workspace, "scratch/review-0.8.2/panel-" + group + ".bin"))))
                    {
                        byte[] name = Encoding.UTF8.GetBytes(label);
                        output.Write(name.Length); output.Write(name); output.Write(positions.Length);
                        foreach (Vector3 point in positions) { output.Write(point.x); output.Write(point.y); output.Write(point.z); }
                        foreach (float angle in solver.Angles) output.Write(angle);
                    }
                }
            }
        }
        Console.WriteLine("PANEL_CHECKS cases=" + cases + " maxPenetration=" + worstPenetration + " maxShift=" + worstShift +
            " selfCrossings=" + crossings + " averageMilliseconds=" + milliseconds / cases);
        if (worstPenetration > 0.0125f || worstShift > .5f || crossings != 0) throw new Exception("Skirt guide needs adjustment");
        for (int variant = 0; variant < skirts.Length; variant++)
        {
            var collision = new[] { new SimulationCapsule(new Vector3(.128f, 1.15f, .04f), new Vector3(.128f, 1.26f, .30f), .12f) };
            var at30 = new SkirtPanelSolver(skirts[variant].Vertices);
            var at120 = new SkirtPanelSolver(skirts[variant].Vertices);
            at30.Step(collision, 0f); at120.Step(collision, 0f);
            for (int frame = 0; frame < 30; frame++) at30.Step(Array.Empty<SimulationCapsule>(), 1f / 30f);
            for (int frame = 0; frame < 120; frame++) at120.Step(Array.Empty<SimulationCapsule>(), 1f / 120f);
            if (at30.Angles.Zip(at120.Angles, (a,b) => Math.Abs(a-b)).Max() > .00001f)
                throw new Exception("Frame-dependent skirt relaxation");
        }
        Console.WriteLine("PANEL_RELAXATION_PASS 30/120fps");
    }

    private static Vector3 Read(BinaryReader input) => new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
}
