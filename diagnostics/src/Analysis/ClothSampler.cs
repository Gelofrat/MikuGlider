using System;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class ClothSampler : IDisposable
{
    private static int nextBinding;
    private readonly int binding = System.Threading.Interlocked.Increment(ref nextBinding);
    private readonly SkirtTarget target;
    private readonly DiagnosticOptions options;
    private readonly SkinningModel skinning;
    private SurfaceAnalysis surface;
    private double nextGeometry;
    private GeometryData lastGeometry;
    internal SkirtTarget Target => target;
    internal Mesh Mesh => skinning?.Mesh;
    internal float[] Distances { get; private set; } = Array.Empty<float>();
    internal ClothFrame Last { get; private set; }

    internal ClothSampler(SkirtTarget target, DiagnosticOptions options)
    {
        this.target = target;
        this.options = options;
        if (target.Skirt != null && target.Skirt.sharedMesh != null)
            skinning = new SkinningModel(target.Skirt);
    }

    internal ClothFrame Sample(double realtime, double simulationTime)
    {
        var frame = new ClothFrame
        {
            targetId = target.Id,
            sampledFrame = Time.frameCount,
            sampledAt = realtime,
            skin = target.Skin,
            context = target.Context,
            state = target.Failed ? "skinned_fallback" : !target.Ready ? "initializing" :
                Time.timeScale == 0f ? "paused" : "pose_driven",
            binding = Binding()
        };
        Last = frame;
        target.ReadMotion(frame);
        if (target.Failed)
            frame.issues |= ClothIssue.SkinnedFallback;
        if (skinning == null || target.Skirt == null)
            return frame;
        return SamplePose(frame, realtime);
    }

    private ClothFrame SamplePose(ClothFrame frame, double realtime)
    {
        frame.garmentPoseMilliseconds = target.PoseMilliseconds;
        frame.bones = ReadBones(target.Skirt.bones);
        frame.particleSpace = "skinned_world";
        frame.coordinatesReliable = target.Ready;
        frame.colliders = Array.Empty<ColliderData>();
        Vector3[] posed = skinning.Pose();
        frame.surfaceWorld = posed;
        frame.skinnedWorld = posed;
        foreach (Vector3 point in posed)
            if (!GeometryMath.Finite(point)) frame.invalidParticles++;
        if (frame.invalidParticles != 0)
        {
            frame.issues |= ClothIssue.InvalidNumber;
            return frame;
        }
        if (target.Ready)
        {
            surface ??= new SurfaceAnalysis(target, skinning);
            if (surface.TryCollect(out GeometryData completed, out Vector3[] rendered)) lastGeometry = completed;
            if (realtime >= nextGeometry)
            {
                surface.Schedule(posed, posed, options.GeometrySegments.Value, realtime);
                nextGeometry = realtime + 1.0 / options.GeometryPerSecond.Value;
            }
            frame.geometry = lastGeometry;
            if (lastGeometry?.bodyCrossings > 0) frame.issues |= ClothIssue.BodyCrossing;
            if (lastGeometry?.coatCrossings > 0) frame.issues |= ClothIssue.GarmentCrossing;
            if (lastGeometry?.selfCrossings > 0) frame.issues |= ClothIssue.SelfCrossing;
            if (lastGeometry?.stretchedEdges > 0 || lastGeometry?.collapsedTriangles > 0)
                frame.issues |= ClothIssue.StretchedSurface;
        }
        return frame;
    }

    internal TargetData Metadata()
    {
        Transform[] bones = target.Skirt != null ? target.Skirt.bones : Array.Empty<Transform>();
        var names = new string[bones.Length];
        for (int index = 0; index < names.Length; index++)
            names[index] = bones[index] != null ? bones[index].name : "missing";
        return new TargetData
        {
            id = target.Id,
            binding = Binding(),
            skin = target.Skin,
            context = target.Context,
            path = SkirtTarget.PathOf(target.Host.transform),
            mesh = Mesh != null ? Mesh.name : null,
            restVertices = skinning?.Rest,
            visibleTriangles = skinning?.Triangles,
            maxDistances = Distances,
            collisionSphereDistances = Array.Empty<float>(),
            boneNames = names,
            settings = new { mode = "pose_driven" }
        };
    }

    public void Dispose()
    {
        surface?.Dispose();
    }

    private string Binding()
    {
        return target.Id + "_" + binding;
    }

    private static BoneData[] ReadBones(Transform[] bones)
    {
        var result = new BoneData[bones.Length];
        for (int index = 0; index < bones.Length; index++)
        {
            Transform bone = bones[index];
            result[index] = new BoneData
            {
                name = bone != null ? bone.name : "missing",
                position = bone != null ? bone.position : Vector3.zero,
                rotation = bone != null ? bone.rotation : Quaternion.identity
            };
        }
        return result;
    }
}
