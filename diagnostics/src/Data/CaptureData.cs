using System;
using UnityEngine;

namespace MikuGlider.Diagnostics;

[Flags]
internal enum ClothIssue
{
    None = 0,
    InvalidNumber = 1,
    CoordinatesUnresolved = 2,
    AnchorDrift = 4,
    ColliderPenetration = 8,
    SpeedOutlier = 16,
    AccelerationOutlier = 32,
    BodyCrossing = 64,
    SelfCrossing = 128,
    StretchedSurface = 256,
    SkinnedFallback = 512,
    ParticleLayoutMismatch = 1024,
    GarmentCrossing = 2048
}

internal sealed class FrameData
{
    public int sample;
    public int unityFrame;
    public double realtime;
    public float deltaTime;
    public float unscaledDeltaTime;
    public float timeScale;
    public string marker;
    public string scene;
    public bool overlay;
    public int overlayTelemetryFrame = -1;
    public double diagnosticMilliseconds;
    public CameraData camera;
    public ClothFrame cloth;
    public CharacterFrame character;
    public double sampleInterval;
    public int estimatedMissedSamples;
    public int droppedJobs;
    public int skippedImages;
    public string[] transitions;
}

internal sealed class ClothFrame
{
    internal ClothFrame RecordingView(bool includeParticles)
    {
        var copy = (ClothFrame)MemberwiseClone();
        if (!includeParticles)
        {
            copy.particlesRaw = null;
            copy.particlesWorld = null;
            copy.skinnedWorld = null;
            copy.surfaceWorld = null;
            copy.bones = null;
        }
        return copy;
    }

    public int targetId;
    public int sampledFrame;
    public double sampledAt;
    public string binding;
    public string skin;
    public string context;
    public string state;
    public double garmentPoseMilliseconds;
    public string motion;
    public string clip;
    public string clipError;
    public float clipTime;
    public Vector3 playerVelocity;
    public bool grounded;
    public bool sliding;
    public bool sprinting;
    public bool flying;
    public string historyReset = default;
    public string particleSpace;
    public bool coordinatesReliable;
    public float coordinateError = default;
    public int particleCount = default;
    public int pinnedCount = default;
    public int invalidParticles;
    public int invalidColliders = default;
    public int penetratingParticles = default;
    public int speedOutliers;
    public int accelerationOutliers;
    public float maximumPenetration = default;
    public float maximumAnchorError = default;
    public float maximumRelativeSpeed;
    public float maximumRelativeAcceleration;
    public ClothIssue issues;
    public GeometryData geometry;
    public ColliderData[] colliders;
    public Vector3[] particlesRaw;
    public Vector3[] particlesWorld;
    public Vector3[] skinnedWorld;
    public Vector3[] surfaceWorld;
    public int[] penetratingIndices = default;
    public int[] jitterIndices;
    public BoneData[] bones;
}

internal sealed class TargetData
{
    public int id;
    public string binding;
    public string skin;
    public string context;
    public string path;
    public string mesh;
    public Vector3[] restVertices;
    public int[] visibleTriangles;
    public float[] maxDistances;
    public float[] collisionSphereDistances;
    public string[] boneNames;
    public int[] meshToParticle = default;
    public float? mappingError = default;
    public int? mappingFrame = default;
    public double? mappingBuildMilliseconds = default;
    public object settings;
}

internal sealed class GeometryData
{
    public double analysisMilliseconds;
    public double sampledAt;
    public string source;
    public bool surfaceReliable;
    public float surfaceError;
    public int checkedEdges;
    public int totalEdges;
    public int bodyCrossings;
    public int coatCrossings;
    public int selfCrossings;
    public int stretchedEdges;
    public int collapsedTriangles;
    public float maximumStretch;
    public Vector3[] crossingPoints;
    public string limitation;
}

internal sealed class ColliderData
{
    public string name = default;
    public string kind = default;
    public Vector3 start = default;
    public Vector3 end = default;
    public float startRadius = default;
    public float endRadius = default;
    public bool enabled = default;
    public bool valid = default;
}

internal sealed class BoneData
{
    public string name;
    public Vector3 position;
    public Quaternion rotation;
}

internal sealed class CameraData
{
    public string name;
    public Vector3 position;
    public Quaternion rotation;
    public float fieldOfView;
    public int width;
    public int height;
    public int pixelWidth;
    public int pixelHeight;
    public int cullingMask;
    public float near;
    public float far;
    public bool orthographic;
    public string targetTexture;
    public float[] worldToCamera;
    public float[] projection;
    public RenderProjectionData firstPersonPass;
    public string firstPersonProjectionError;
}

internal sealed class RenderProjectionData
{
    public int unityFrame;
    public string source;
    public uint renderingLayerMask;
    public float fieldOfView;
    public float aspect;
    public float near;
    public float far;
    public float[] worldToCamera;
    public float[] projection;
    public float[] gpuProjection;
}

internal sealed class FrameSummary
{
    public int sample;
    public int unityFrame;
    public double realtime;
    public string skin;
    public string motion;
    public string state;
    public bool coordinatesReliable;
    public bool geometryReliable;
    public int checkedEdges;
    public int totalEdges;
    public double geometryRealtime;
    public double geometryMilliseconds;
    public string marker;
    public ClothIssue issues;
    public float penetration;
    public float speed;
    public float acceleration;
    public int bodyCrossings;
    public int selfCrossings;
    public double diagnosticMilliseconds;
    public int coatCrossings;
    public string perspective;
    public string weapon;
    public string clip;
    public string capturePhase;
    public string geometryPath;
    public string captureError;
    public int rendererCount;
    public int missingGeometry;
    public int estimatedMissedSamples;
    public int droppedJobs;
    public int skippedImages;
    public double sampleInterval;
    public double characterMilliseconds;
    public string[] transitions;
}

internal sealed class ImageData
{
    public int sample;
    public int unityFrame;
    public double realtime;
    public string path;
    public string error;
    public int width;
    public int height;
}
