using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace MikuGlider.Diagnostics;

[BepInPlugin(Guid, "Miku Glider Diagnostics", Version)]
[BepInDependency("horse.mycopunk.mikuglider", "0.12.0")]
[MycoMod(null, ModFlags.IsClientSide)]
public sealed class DiagnosticsPlugin : BaseUnityPlugin
{
    public const string Guid = "horse.mycopunk.mikuglider.diagnostics";
    public const string Version = "0.4.5";
    private DiagnosticOptions options;
    private SkirtTargets discovery;
    private LocalControl control;
    private ClothSampler sampler;
    private CaptureSession recording;
    private readonly List<CaptureSession> finishing = new List<CaptureSession>();
    private readonly GameFrameCapture images = new GameFrameCapture();
    private readonly ClothOverlay overlay = new ClothOverlay();
    private List<SkirtTarget> targets = new List<SkirtTarget>();
    private Mesh boundBodyMesh;
    private string root;
    private string template;
    private string lastPath;
    private string lastError;
    private string marker;
    private int selectedId;
    private int sample;
    private bool showOverlay;
    private bool snapshot;
    private bool forceImage;
    private double endsAt;
    private double nextSample;
    private double nextImage;
    private double nextParticles;
    private double nextDiscovery;
    private double nextControl;
    private double nextStatus;
    private double burstUntil;
    private ClothIssue observedIssues;
    private CharacterCapture characterCapture;
    private CaptureJournal journal;
    private CharacterFrame renderedCharacter;
    private FrameData lastFrame;
    private double previousSample;
    private int initialSkippedImages;
    private bool characterDue;

    private void Awake()
    {
        try
        {
            options = new DiagnosticOptions(Config);
            discovery = new SkirtTargets();
            characterCapture = new CharacterCapture(options, discovery.SourceAssembly);
            journal = new CaptureJournal();
            FirstPersonProjectionCapture.Install();
            if (FirstPersonProjectionCapture.Error != null) Logger.LogWarning("First-person projection capture unavailable: " + FirstPersonProjectionCapture.Error);
            Camera.onPostRender += AfterCamera;
            RenderPipelineManager.endCameraRendering += AfterPipelineCamera;
            root = Path.Combine(Paths.BepInExRootPath, "MikuGliderDiagnostics");
            control = new LocalControl(root);
            template = CaptureSession.LoadViewer();
            StartCoroutine(EndOfFrame());
            Logger.LogInfo("Diagnostics ready: F6 record, F7 overlay, F8 snapshot, F9 target. " + root);
        }
        catch (Exception error)
        {
            Logger.LogError("Diagnostics initialization failed: " + error);
            enabled = false;
        }
    }

    private void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        try
        {
            images.Tick();
            ReadKeys(now);
            if (now >= nextControl)
            {
                nextControl = now + 0.25;
                Execute(control.Read(), now);
            }
            if (recording != null && (now >= endsAt || recording.BudgetReached || recording.Fault != null))
                StopRecording(recording.BudgetReached ? "session_byte_limit" : recording.Fault != null ? "writer_error" : "duration_limit");
            for (int index = finishing.Count - 1; index >= 0; index--)
            {
                if (!finishing[index].Finished)
                    continue;
                if (finishing[index].Fault != null)
                {
                    lastError = finishing[index].Fault;
                    Logger.LogError("Diagnostic writer failed: " + lastError);
                }
                else
                    Logger.LogInfo("Diagnostic capture saved: " + finishing[index].DirectoryPath);
                finishing.RemoveAt(index);
            }
            if (now >= nextStatus)
            {
                nextStatus = now + 1;
                var status = new
                {
                    schema = 2,
                    pluginVersion = Version,
                    utc = DateTime.UtcNow.ToString("O"),
                    recording = recording != null,
                    finalizing = finishing.Count,
                    overlay = showOverlay,
                    target = sampler?.Target.Skin,
                    context = sampler?.Target.Context,
                    state = sampler?.Last?.state,
                    issues = sampler?.Last?.issues,
                    sample,
                    path = recording?.DirectoryPath ?? lastPath,
                    droppedJobs = recording?.Dropped ?? 0,
                    skippedImages = images.Skipped,
                    perspective = lastFrame?.character?.perspective,
                    renderers = lastFrame?.character?.renderers?.Length ?? 0,
                    geometryPath = lastFrame?.character?.geometryPath,
                    capturePhase = lastFrame?.character?.phase,
                    error = lastError ?? images.Error,
                    commandId = control.LastId,
                    commandResult = control.LastResult
                };
                try
                {
                    control.Status(status);
                }
                catch (IOException)
                {
                    nextStatus = now + 0.25;
                }
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    private void LateUpdate()
    {
        if (recording == null)
            return;
        double now = Time.realtimeSinceStartupAsDouble;
        characterDue = now >= nextSample || marker != null;
        try
        {
            if (now >= nextDiscovery || sampler != null && !sampler.Target.Alive)
                Discover(now);
        }
        catch (Exception error)
        {
            lastError = "Target discovery: " + error.Message;
        }
    }

    private void AfterPipelineCamera(ScriptableRenderContext context, Camera camera) => CaptureRenderedCharacter(camera, "endCameraRendering");

    private void AfterCamera(Camera camera) => CaptureRenderedCharacter(camera, "onPostRender");

    private void CaptureRenderedCharacter(Camera camera, string phase)
    {
        if (!enabled || recording == null || !characterDue || renderedCharacter?.unityFrame == Time.frameCount)
            return;
        try
        {
            Camera expected = sampler?.Target.FindCamera() ?? PlayerLook.LocalCamera ?? Camera.main;
            if (camera != expected)
                return;
            renderedCharacter = characterCapture.Read(camera, sampler?.Target, sample, forceImage, phase);
        }
        catch (Exception error)
        {
            renderedCharacter = new CharacterFrame { unityFrame = Time.frameCount, phase = phase, error = error.Message };
        }
    }

    private IEnumerator EndOfFrame()
    {
        var end = new WaitForEndOfFrame();
        while (true)
        {
            yield return end;
            if (!enabled || recording == null && !showOverlay)
                continue;
            double now = Time.realtimeSinceStartupAsDouble;
            if (recording != null ? !characterDue : now < nextSample && marker == null)
                continue;
            double interval = 1.0 / options.SamplesPerSecond.Value;
            nextSample = nextSample == 0 ? now + interval : nextSample + Math.Max(1, Math.Floor((now - nextSample) / interval) + 1) * interval;
            try
            {
                Sample(now);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }
    }

    private void Sample(double now)
    {
        var watch = Stopwatch.StartNew();
        int overlayFrame = sampler?.Last?.sampledFrame ?? -1;
        if (now >= nextDiscovery || sampler != null && !sampler.Target.Alive)
            Discover(now);
        ClothFrame cloth = sampler?.Sample(now, Time.timeAsDouble);
        var frame = new FrameData
        {
            sample = sample++,
            unityFrame = Time.frameCount,
            realtime = now,
            deltaTime = Time.deltaTime,
            unscaledDeltaTime = Time.unscaledDeltaTime,
            timeScale = Time.timeScale,
            marker = marker,
            scene = SceneManager.GetActiveScene().name,
            overlay = showOverlay,
            overlayTelemetryFrame = overlayFrame,
            cloth = cloth
        };
        Camera camera = sampler?.Target.FindCamera() ?? PlayerLook.LocalCamera ?? Camera.main;
        frame.camera = CharacterCapture.ReadCamera(camera);
        frame.diagnosticMilliseconds = watch.Elapsed.TotalMilliseconds;
        if (recording == null)
            return;
        frame.character = renderedCharacter?.unityFrame == frame.unityFrame ? renderedCharacter :
            characterCapture.Read(camera, sampler?.Target, frame.sample, forceImage, "end_of_frame_fallback");
        frame.sampleInterval = previousSample == 0 ? 0 : now - previousSample;
        frame.estimatedMissedSamples = previousSample == 0 ? 0 : Math.Max(0, (int)Math.Floor(frame.sampleInterval * options.SamplesPerSecond.Value) - 1);
        previousSample = now;
        frame.droppedJobs = recording.Dropped;
        frame.skippedImages = images.Skipped - initialSkippedImages;
        frame.transitions = journal.Transitions(frame);
        frame.diagnosticMilliseconds = watch.Elapsed.TotalMilliseconds + (renderedCharacter == frame.character ? frame.character.captureMilliseconds : 0);
        lastFrame = frame;
        ClothIssue issues = cloth?.issues ?? ClothIssue.None;
        if ((issues & ~observedIssues) != 0)
            burstUntil = now + 0.8;
        observedIssues |= issues;
        TargetData metadata = cloth != null && !recording.HasBinding(cloth.binding) ? sampler.Metadata() : null;
        bool includeParticles = now >= nextParticles || forceImage;
        frame.cloth = cloth?.RecordingView(includeParticles);
        bool accepted = recording.Frame(frame, metadata);
        if (accepted)
        {
            if (includeParticles)
                nextParticles = now + 1.0 / options.ParticleFramesPerSecond.Value;
            if (options.CaptureImages.Value && (forceImage || now >= nextImage))
            {
                if (!images.Request(recording, frame, forceImage ? Math.Max(1920, options.ImageWidth.Value) : options.ImageWidth.Value))
                    recording.SkippedImage();
                nextImage = now + 1.0 / (now < burstUntil ? 30 : options.ImagesPerSecond.Value);
            }
            marker = null;
            forceImage = false;
        }
        if (snapshot && accepted)
            StopRecording("snapshot");
    }

    private void Discover(double now)
    {
        targets = discovery.Find(options.IncludePreviews.Value);
        nextDiscovery = now + 0.5;
        SkirtTarget target = targets.Find(item => item.Id == selectedId);
        if (target == null && targets.Count > 0)
            target = targets[0];
        if (target == null)
        {
            sampler?.Dispose();
            sampler = null;
            return;
        }
        selectedId = target.Id;
        if (sampler != null && sampler.Target.Id == target.Id && sampler.Target.Skirt == target.Skirt &&
            sampler.Mesh == target.Skirt?.sharedMesh && boundBodyMesh == target.Body.sharedMesh)
            return;
        sampler?.Dispose();
        sampler = new ClothSampler(target, options);
        boundBodyMesh = target.Body.sharedMesh;
    }

    private void StartRecording(double now, string label, int duration, bool single)
    {
        if (recording != null)
            return;
        if (finishing.Count >= 2)
            throw new InvalidOperationException("The previous captures are still being saved.");
        sampler?.Dispose();
        sampler = null;
        nextDiscovery = 0;
        lastError = null;
        snapshot = single;
        sample = 0;
        marker = label;
        forceImage = true;
        nextSample = nextImage = nextParticles = 0;
        observedIssues = ClothIssue.None;
        renderedCharacter = null;
        lastFrame = null;
        previousSample = 0;
        initialSkippedImages = images.Skipped;
        bool flipImages = options.FlipImagesVertically.Value || options.AutomaticImageOrientation.Value && SystemInfo.graphicsUVStartsAtTop;
        endsAt = now + (single ? 10 : Math.Max(1, Math.Min(120, duration)));
        recording = new CaptureSession(Path.Combine(root, "captures"), new
        {
            schema = 2,
            plugin = Guid,
            version = Version,
            mikuVersion = Chainloader.PluginInfos["horse.mycopunk.mikuglider"].Metadata.Version.ToString(),
            utc = DateTime.UtcNow.ToString("O"),
            label,
            unityVersion = Application.unityVersion,
            gameVersion = Application.version,
            mainAssemblyMvid = discovery.SourceAssembly.ManifestModule.ModuleVersionId.ToString(),
            gameAssemblyMvid = typeof(Player).Assembly.ManifestModule.ModuleVersionId.ToString(),
            startedRealtime = now,
            fixedDeltaTime = Time.fixedDeltaTime,
            maximumDeltaTime = Time.maximumDeltaTime,
            targetFrameRate = Application.targetFrameRate,
            vSyncCount = QualitySettings.vSyncCount,
            gravity = Physics.gravity,
            skinWeights = QualitySettings.skinWeights.ToString(),
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            meshFramesPerSecond = options.MeshFramesPerSecond.Value,
            maximumMeshVertices = options.MaximumMeshVertices.Value,
            captureMeshes = options.CaptureMeshes.Value,
            matrixLayout = "row_major_4x4",
            characterSampling = "camera_end_callback; explicitly labeled end_of_frame_fallback if unavailable",
            samplesPerSecond = options.SamplesPerSecond.Value,
            imagesPerSecond = options.ImagesPerSecond.Value,
            particleFramesPerSecond = options.ParticleFramesPerSecond.Value,
            backgroundGeometry = true,
            graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
            imageWidth = options.ImageWidth.Value,
            geometryPerSecond = options.GeometryPerSecond.Value,
            geometrySegments = options.GeometrySegments.Value,
            penetrationTolerance = options.PenetrationTolerance.Value,
            anchorTolerance = options.AnchorTolerance.Value,
            particleSpeedLimit = options.ParticleSpeedLimit.Value,
            particleAccelerationLimit = options.ParticleAccelerationLimit.Value,
            asyncReadback = SystemInfo.supportsAsyncGPUReadback,
            garmentMode = "pose_driven",
            flipImages,
            note = "Read-only observation. Threshold flags are candidates for review, not proof of a simulation defect."
        }, options.SessionMegabytes.Value * 1024L * 1024L,
            (pixels, width, height) => GameFrameCapture.Encode(pixels, width, height, flipImages), template);
        lastPath = recording.DirectoryPath;
        FirstPersonProjectionCapture.Reset();
        FirstPersonProjectionCapture.Enabled = true;
        characterCapture.Begin(recording);
        journal.Begin(recording);
        Logger.LogInfo("Diagnostic recording started: " + lastPath);
    }

    private void StopRecording(string reason)
    {
        FirstPersonProjectionCapture.Enabled = false;
        FirstPersonProjectionCapture.Reset();
        if (recording == null)
            return;
        journal.Begin(null);
        characterCapture.Begin(null);
        recording.Stop(reason);
        finishing.Add(recording);
        recording = null;
        snapshot = false;
        marker = null;
        forceImage = false;
    }

    private void ReadKeys(double now)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || PlayerLook.Instance != null && PlayerLook.Instance.IsTextChatOpen)
            return;
        if (Pressed(keyboard, options.RecordKey.Value))
        {
            if (recording != null)
                StopRecording("manual_stop");
            else
                StartRecording(now, "manual", options.DurationSeconds.Value, false);
        }
        if (Pressed(keyboard, options.OverlayKey.Value))
            showOverlay = !showOverlay;
        if (Pressed(keyboard, options.SnapshotKey.Value))
            Snapshot(now, "manual_snapshot");
        if (Pressed(keyboard, options.NextTargetKey.Value))
            NextTarget(now);
    }

    private void Execute(ControlCommand command, double now)
    {
        if (command == null)
            return;
        switch (command.action)
        {
            case "start":
                StartRecording(now, command.label ?? "local_command", command.durationSeconds == 0 ? options.DurationSeconds.Value : command.durationSeconds, false);
                break;
            case "stop": StopRecording("local_command"); break;
            case "snapshot": Snapshot(now, command.label ?? "snapshot"); break;
            case "mark":
                if (recording == null)
                {
                    control.Result("not_recording");
                    return;
                }
                marker = command.label ?? "marker";
                forceImage = true;
                break;
            case "overlay_on": showOverlay = true; break;
            case "overlay_off": showOverlay = false; break;
            case "next_target": NextTarget(now); break;
            case "status": break;
            default: control.Result("unknown_action"); return;
        }
        control.Result("ok");
        nextStatus = 0;
    }

    private void Snapshot(double now, string label)
    {
        if (recording == null)
            StartRecording(now, label, 1, true);
        else
        {
            marker = label;
            forceImage = true;
        }
    }

    private void NextTarget(double now)
    {
        Discover(now);
        if (targets.Count > 0)
        {
            int current = targets.FindIndex(item => item.Id == selectedId);
            selectedId = targets[(current + 1) % targets.Count].Id;
            nextDiscovery = 0;
        }
    }

    private static bool Pressed(Keyboard keyboard, Key key)
    {
        return key != Key.None && keyboard[key].wasPressedThisFrame;
    }

    private void OnGUI()
    {
        string context = lastFrame?.character == null ? null : lastFrame.character.perspective + " · " +
            lastFrame.character.renderers.Length + " renderers · " + lastFrame.character.phase + "\n";
        overlay.Draw(sampler, showOverlay, recording != null, context + (lastError ?? images.Error ?? Path.GetFileName(lastPath ?? string.Empty)));
    }

    private void Fail(Exception error)
    {
        string message = error.ToString();
        if (lastError != message)
            Logger.LogError("Diagnostic observation failed: " + message);
        lastError = message;
        StopRecording("observation_error");
        showOverlay = false;
        sampler?.Dispose();
        sampler = null;
    }

    private void OnDestroy()
    {
        StopRecording("plugin_unloaded");
        images.Dispose();
        sampler?.Dispose();
        Camera.onPostRender -= AfterCamera;
        RenderPipelineManager.endCameraRendering -= AfterPipelineCamera;
        characterCapture?.Dispose();
        FirstPersonProjectionCapture.Dispose();
        journal?.Dispose();
    }
}
