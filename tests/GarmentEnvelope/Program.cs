using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MikuGlider;
using UnityEngine;

internal static class Program
{
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => Assembly.LoadFrom(Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll"));
        Run();
    }

    private static void Run()
    {
        var points = new Vector3[128];
        var triangles = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI / 32;
            points[i] = new Vector3(Mathf.Cos(a) * .4f, 0, Mathf.Sin(a) * .4f);
            points[i + 64] = new Vector3(Mathf.Cos(a) * .15f, .3f, Mathf.Sin(a) * .15f);
            int n = (i + 1) % 64;
            triangles.AddRange(new[] { i, n, i + 64, n, n + 64, i + 64 });
        }
        var envelope = new SkirtLayerEnvelope(Vector3.zero, triangles.ToArray());
        envelope.Update(points);
        float maximum = 0;
        for (int h = 0; h <= 30; h++)
            for (int angle = 0; angle < 360; angle += 3)
            {
                float y = h * .01f;
                float radius = .4f - .25f * y / .3f;
                var direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector3 point = direction * (radius + .010f) + Vector3.up * y;
                float error = Vector3.Distance(point, envelope.Constrain(point, .004f));
                maximum = Math.Max(maximum, error);
                if (error > .0001f) throw new Exception("Clear garment was inflated: " + error);
                point = direction * (radius - .03f) + Vector3.up * y;
                Vector3 corrected = envelope.Constrain(point, .004f);
                float correctedRadius = new Vector2(corrected.x, corrected.z).magnitude;
                if (correctedRadius < radius-.001f || correctedRadius > radius+.010f)
                    throw new Exception("Invalid contact radius: " + correctedRadius + " expected " + radius);
            }
        if (envelope.Constrain(new Vector3(.1f, .4f, 0), .004f) != new Vector3(.1f, .4f, 0))
            throw new Exception("Constrained above garment");
        Console.WriteLine("GARMENT_ENVELOPE_PASS samples=3720 maxInflation=" + maximum);
    }
}
