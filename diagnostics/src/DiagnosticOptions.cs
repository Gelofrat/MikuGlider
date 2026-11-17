using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace MikuGlider.Diagnostics;

internal sealed class DiagnosticOptions
{
    internal ConfigEntry<Key> RecordKey { get; }
    internal ConfigEntry<Key> OverlayKey { get; }
    internal ConfigEntry<Key> SnapshotKey { get; }
    internal ConfigEntry<Key> NextTargetKey { get; }
    internal ConfigEntry<int> SamplesPerSecond { get; }
    internal ConfigEntry<int> ImagesPerSecond { get; }
    internal ConfigEntry<int> ParticleFramesPerSecond { get; }
    internal ConfigEntry<bool> AutomaticImageOrientation { get; }
    internal ConfigEntry<int> ImageWidth { get; }
    internal ConfigEntry<int> DurationSeconds { get; }
    internal ConfigEntry<int> SessionMegabytes { get; }
    internal ConfigEntry<int> GeometryPerSecond { get; }
    internal ConfigEntry<int> GeometrySegments { get; }
    internal ConfigEntry<float> PenetrationTolerance { get; }
    internal ConfigEntry<float> AnchorTolerance { get; }
    internal ConfigEntry<float> ParticleSpeedLimit { get; }
    internal ConfigEntry<float> ParticleAccelerationLimit { get; }
    internal ConfigEntry<bool> CaptureImages { get; }
    internal ConfigEntry<bool> IncludePreviews { get; }
    internal ConfigEntry<bool> RecordParticles { get; }
    internal ConfigEntry<bool> FlipImagesVertically { get; }
    internal ConfigEntry<bool> CaptureMeshes { get; }
    internal ConfigEntry<int> MeshFramesPerSecond { get; }
    internal ConfigEntry<int> MaximumMeshVertices { get; }

    internal DiagnosticOptions(ConfigFile config)
    {
        RecordKey = config.Bind("Controls", "Record", Key.F6, "Start or stop a bounded recording.");
        OverlayKey = config.Bind("Controls", "Overlay", Key.F7, "Toggle cloth particles and collider outlines.");
        SnapshotKey = config.Bind("Controls", "Snapshot", Key.F8, "Capture one frame, or mark the active recording.");
        NextTargetKey = config.Bind("Controls", "NextTarget", Key.F9, "Cycle local-player and preview skirt renderers.");
        SamplesPerSecond = config.Bind("Recording", "SamplesPerSecond", 30, Range(5, 120, "Cloth telemetry rate."));
        ImagesPerSecond = config.Bind("Recording", "ImagesPerSecond", 10, Range(1, 30, "Normal screenshot rate; new anomalies request a short 30 Hz burst."));
        ParticleFramesPerSecond = config.Bind("Recording", "ParticleFramesPerSecond", 5, Range(1, 30, "Full particle arrays and bones per second; scalar telemetry retains its normal rate."));
        AutomaticImageOrientation = config.Bind("Recording", "AutomaticImageOrientation", true, "Correct GPU row orientation automatically; disable to use only FlipImagesVertically.");
        ImageWidth = config.Bind("Recording", "ImageWidth", 1280, Range(320, 1920, "Maximum screenshot width; aspect ratio is preserved."));
        DurationSeconds = config.Bind("Recording", "DurationSeconds", 15, Range(1, 120, "Automatic recording duration limit."));
        SessionMegabytes = config.Bind("Recording", "SessionMegabytes", 256, Range(8, 1024, "Maximum bytes written by one session, in MiB."));
        CaptureImages = config.Bind("Recording", "CaptureImages", true, "Capture the final game image after rendering.");
        RecordParticles = config.Bind("Recording", "RecordParticles", true, "Store raw particles and skinned reference positions in addition to resolved world particles.");
        FlipImagesVertically = config.Bind("Recording", "FlipImagesVertically", false, "Enable only if screenshots from the current graphics backend appear upside down.");
        CaptureMeshes = config.Bind("Recording", "CaptureMeshes", true, "Save bounded geometry snapshots of arms, weapon, skirt, coat and body for offline inspection.");
        MeshFramesPerSecond = config.Bind("Recording", "MeshFramesPerSecond", 2, Range(1, 10, "Geometry snapshot frequency; bone matrices retain the telemetry frequency."));
        MaximumMeshVertices = config.Bind("Recording", "MaximumMeshVertices", 120000, Range(10000, 160000, "Maximum vertices per geometry snapshot. Skipped surfaces are explicitly marked."));
        IncludePreviews = config.Bind("Targets", "IncludePreviews", true, "Allow active character-selection and inventory previews.");
        GeometryPerSecond = config.Bind("Analysis", "GeometryPerSecond", 3, Range(1, 30, "Rate of validated rendered-surface and triangle-intersection checks."));
        GeometrySegments = config.Bind("Analysis", "GeometrySegments", 1200, Range(100, 20000, "Maximum cloth edges checked per geometric analysis; coverage is recorded."));
        PenetrationTolerance = config.Bind("Analysis", "PenetrationTolerance", 0.008f, Range(0.001f, 0.05f, "Collider penetration warning threshold in world metres."));
        AnchorTolerance = config.Bind("Analysis", "AnchorTolerance", 0.01f, Range(0.002f, 0.05f, "Pinned-particle alignment warning threshold in world metres."));
        ParticleSpeedLimit = config.Bind("Analysis", "ParticleSpeedLimit", 4f, Range(0.5f, 20f, "Particle speed warning threshold after removing pelvis motion, metres/second."));
        ParticleAccelerationLimit = config.Bind("Analysis", "ParticleAccelerationLimit", 90f, Range(10f, 1000f, "Relative particle acceleration warning threshold, metres/second squared."));
    }

    private static ConfigDescription Range<T>(T minimum, T maximum, string description) where T : System.IComparable
    {
        return new ConfigDescription(description, new AcceptableValueRange<T>(minimum, maximum));
    }
}
