using System;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class ClothVertexMapping
{
    internal int[] MeshToParticle { get; }
    internal float Error { get; }
    internal int ParticleCount { get; }
    internal bool Reliable => Error <= 0.002f;

    internal ClothVertexMapping(int[] mapping, float error, int particles)
    {
        MeshToParticle = mapping;
        Error = error;
        ParticleCount = particles;
    }

    internal static ClothVertexMapping Create(Vector3[] particles, Vector3[] rest, Vector3[][] candidates)
    {
        if (particles.Length == 0 || rest.Length == 0)
            throw new InvalidOperationException("Empty cloth initialization geometry.");
        Vector3[] selected = rest;
        float best = ErrorTo(particles, rest);
        foreach (Vector3[] candidate in candidates)
        {
            if (best <= 0.002f)
                break;
            if (candidate.Length != rest.Length)
                throw new InvalidOperationException("Invalid cloth initialization reference.");
            float error = ErrorTo(particles, candidate);
            if (error < best)
            {
                best = error;
                selected = candidate;
            }
        }
        var particleIndex = new PointIndex(particles);
        var mapping = new int[rest.Length];
        for (int vertex = 0; vertex < mapping.Length; vertex++)
        {
            mapping[vertex] = particleIndex.Nearest(selected[vertex]);
            best = Math.Max(best, Vector3.Distance(selected[vertex], particles[mapping[vertex]]));
        }
        return new ClothVertexMapping(mapping, best, particles.Length);
    }

    internal Vector3[] Surface(Vector3[] particles)
    {
        if (particles.Length != ParticleCount)
            throw new InvalidOperationException("Cloth particle layout changed after initialization.");
        var surface = new Vector3[MeshToParticle.Length];
        for (int index = 0; index < surface.Length; index++)
            surface[index] = particles[MeshToParticle[index]];
        return surface;
    }

    private static float ErrorTo(Vector3[] particles, Vector3[] reference)
    {
        foreach (Vector3 point in reference)
            if (!GeometryMath.Finite(point))
                return float.MaxValue;
        var tree = new PointIndex(reference);
        float error = 0f;
        foreach (Vector3 point in particles)
        {
            if (!GeometryMath.Finite(point))
                throw new InvalidOperationException("Invalid cloth initialization particle.");
            error = Math.Max(error, tree.DistanceSquared(point));
        }
        return (float)Math.Sqrt(error);
    }
}
