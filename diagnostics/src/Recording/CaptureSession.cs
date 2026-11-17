using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MikuGlider.Diagnostics;

internal sealed class CaptureSession
{
    private sealed class Work
    {
        internal string file;
        internal object data;
        internal byte[] rgba;
        internal ImageData image;
        internal int estimate;
    }

    private const long MaximumQueuedBytes = 32L * 1024 * 1024;
    private readonly BlockingCollection<Work> queue = new BlockingCollection<Work>(32);
    private readonly Func<byte[], int, int, byte[]> encoder;
    private readonly string viewerTemplate;
    private readonly long maximumBytes;
    private readonly List<FrameSummary> summaries = new List<FrameSummary>();
    private readonly List<ImageData> images = new List<ImageData>();
    private readonly List<CaptureEvent> events = new List<CaptureEvent>();
    private readonly HashSet<string> bindings = new HashSet<string>();
    private readonly object gate = new object();
    private readonly Task worker;
    private long queuedBytes;
    private long writtenBytes;
    private int dropped;
    private int pendingImages;
    private int attemptedFrames;
    private int eventAttempts;
    private int suppressedEvents;
    private int skippedImages;
    private bool stopping;
    private string stopReason;
    private volatile string fault;
    private volatile bool budgetReached;
    internal string DirectoryPath { get; }
    internal bool Finished => worker.IsCompleted;
    internal bool BudgetReached => budgetReached;
    internal int Dropped => Volatile.Read(ref dropped);
    internal long WrittenBytes => Interlocked.Read(ref writtenBytes);
    internal string Fault => fault;
    internal Task Completion => worker;

    internal bool HasBinding(string binding)
    {
        return bindings.Contains(binding);
    }

    internal CaptureSession(string root, object manifest, long maximumBytes, Func<byte[], int, int, byte[]> encoder, string viewerTemplate)
    {
        DirectoryPath = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "images"));
        this.maximumBytes = maximumBytes;
        this.encoder = encoder;
        this.viewerTemplate = viewerTemplate;
        worker = Task.Run(Run);
        Enqueue(new Work { file = "manifest.json", data = manifest, estimate = 4096 });
    }

    internal bool Frame(FrameData frame, TargetData metadata)
    {
        Interlocked.Increment(ref attemptedFrames);
        if (metadata != null && !bindings.Contains(metadata.binding))
        {
            if (!Enqueue(new Work
            {
                file = "target-" + metadata.binding + ".json",
                data = metadata,
                estimate =
                4096 + (metadata.restVertices?.Length ?? 0) * 48 + (metadata.visibleTriangles?.Length ?? 0) * 8
            }))
                return false;
            bindings.Add(metadata.binding);
        }
        int matrices = frame.character?.renderers?.Sum(renderer => renderer.boneMatrices?.Length ?? 0) ?? 0;
        return Enqueue(new Work { file = "frames.jsonl", data = frame, estimate = 16384 + (frame.cloth?.particleCount ?? 0) * 192 + matrices * 24 });
    }

    internal bool Archive(string relative, object data, int estimate)
    {
        if (!relative.EndsWith(".json.gz", StringComparison.Ordinal) || relative.Contains("..") ||
            Path.IsPathRooted(relative) || relative.Contains(":"))
            throw new ArgumentException("An archive must be a relative .json.gz path.", nameof(relative));
        return Enqueue(new Work { file = relative, data = data, estimate = estimate });
    }

    internal void Event(CaptureEvent value)
    {
        if (Interlocked.Increment(ref eventAttempts) > 500)
        {
            Interlocked.Increment(ref suppressedEvents);
            return;
        }
        Enqueue(new Work { file = "events.jsonl", data = value, estimate = 24576 });
    }

    internal void SkippedImage() => Interlocked.Increment(ref skippedImages);

    internal bool LeaseImage()
    {
        lock (gate)
        {
            if (stopping || fault != null || BudgetReached)
                return false;
            pendingImages++;
            return true;
        }
    }

    internal void Image(ImageData metadata, byte[] rgba)
    {
        Enqueue(new Work { file = "images.jsonl", data = metadata, image = metadata, rgba = rgba, estimate = (rgba?.Length ?? 0) + 1024 });
    }

    internal void ReleaseImage()
    {
        lock (gate)
        {
            pendingImages--;
            CloseIfReady();
        }
    }

    internal void Stop(string reason)
    {
        lock (gate)
        {
            stopping = true;
            stopReason ??= reason;
            CloseIfReady();
        }
    }

    internal static string LoadViewer()
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MikuGlider.Diagnostics.Viewer"))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            return reader.ReadToEnd();
    }

    private bool Enqueue(Work work)
    {
        lock (gate)
        {
            if (fault != null || BudgetReached || queue.IsAddingCompleted || queuedBytes + work.estimate > MaximumQueuedBytes || !queue.TryAdd(work))
            {
                Interlocked.Increment(ref dropped);
                return false;
            }
            queuedBytes += work.estimate;
            return true;
        }
    }

    private void CloseIfReady()
    {
        if (stopping && pendingImages == 0 && !queue.IsAddingCompleted)
            queue.CompleteAdding();
    }

    private void Run()
    {
        try
        {
            foreach (Work work in queue.GetConsumingEnumerable())
            {
                try
                {
                    if (!BudgetReached)
                        Write(work);
                    else
                        Interlocked.Increment(ref dropped);
                }
                finally
                {
                    lock (gate)
                        queuedBytes -= work.estimate;
                }
            }
            var report = new
            {
                schema = 2,
                stopReason = BudgetReached ? "session_byte_limit" : stopReason,
                droppedJobs = Dropped,
                writtenBytes = WrittenBytes,
                attemptedFrames,
                skippedImages,
                suppressedEvents,
                frames = summaries,
                images,
                events
            };
            string json = CaptureJson.Write(report);
            File.WriteAllText(Path.Combine(DirectoryPath, "summary.json"), json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(DirectoryPath, "index.html"), viewerTemplate.Replace("__CAPTURE_DATA__", json), new UTF8Encoding(false));
        }
        catch (Exception error)
        {
            fault = error.ToString();
        }
    }

    private void Write(Work work)
    {
        if (work.rgba != null)
        {
            byte[] encoded = null;
            try
            {
                encoded = encoder(work.rgba, work.image.width, work.image.height);
                if (encoded == null || encoded.Length == 0)
                    throw new InvalidOperationException("The image encoder returned an empty image.");
            }
            catch (Exception error)
            {
                work.image.error = "Image encoding failed: " + error.Message;
            }
            if (encoded != null)
            {
                string relative = "images/frame-" + work.image.sample.ToString("D6") + ".png";
                if (!Reserve(encoded.Length))
                    return;
                File.WriteAllBytes(Path.Combine(DirectoryPath, relative.Replace('/', Path.DirectorySeparatorChar)), encoded);
                work.image.path = relative;
            }
        }
        string json = CaptureJson.Write(work.data);
        byte[] bytes = Encoding.UTF8.GetBytes(json + (work.file.EndsWith(".jsonl", StringComparison.Ordinal) ? "\n" : string.Empty));
        if (work.file.EndsWith(".gz", StringComparison.Ordinal))
        {
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, true))
                    gzip.Write(bytes, 0, bytes.Length);
                bytes = memory.ToArray();
            }
        }
        if (!Reserve(bytes.Length))
            return;
        string path = Path.Combine(DirectoryPath, work.file);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (work.file.EndsWith(".jsonl", StringComparison.Ordinal))
        {
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                stream.Write(bytes, 0, bytes.Length);
        }
        else
            File.WriteAllBytes(path, bytes);
        if (work.data is FrameData frame)
        {
            ClothFrame cloth = frame.cloth;
            summaries.Add(new FrameSummary
            {
                sample = frame.sample,
                unityFrame = frame.unityFrame,
                realtime = frame.realtime,
                skin = cloth?.skin,
                motion = cloth?.motion,
                state = cloth?.state,
                coordinatesReliable = cloth?.coordinatesReliable ?? false,
                geometryReliable = cloth?.geometry?.surfaceReliable ?? false,
                checkedEdges = cloth?.geometry?.checkedEdges ?? 0,
                totalEdges = cloth?.geometry?.totalEdges ?? 0,
                geometryRealtime = cloth?.geometry?.sampledAt ?? -1,
                geometryMilliseconds = cloth?.geometry?.analysisMilliseconds ?? 0,
                marker = frame.marker,
                issues = cloth?.issues ?? ClothIssue.None,
                penetration = cloth?.maximumPenetration ?? 0f,
                speed = cloth?.maximumRelativeSpeed ?? 0f,
                acceleration = cloth?.maximumRelativeAcceleration ?? 0f,
                bodyCrossings = cloth?.geometry?.bodyCrossings ?? 0,
                coatCrossings = cloth?.geometry?.coatCrossings ?? 0,
                selfCrossings = cloth?.geometry?.selfCrossings ?? 0,
                diagnosticMilliseconds = frame.diagnosticMilliseconds,
                perspective = frame.character?.perspective,
                weapon = frame.character?.weapon,
                clip = frame.character?.animations?.FirstOrDefault()?.clip ?? cloth?.clip,
                capturePhase = frame.character?.phase,
                geometryPath = frame.character?.geometryPath,
                captureError = frame.character?.error,
                rendererCount = frame.character?.renderers?.Length ?? 0,
                missingGeometry = frame.character?.renderers?.Count(renderer => renderer.error != null || renderer.geometryStatus == "vertex_budget" || renderer.geometryStatus == "writer_rejected" || renderer.geometryStatus == "metadata_rejected" || renderer.geometryStatus == "unreadable_mesh") ?? 0,
                estimatedMissedSamples = frame.estimatedMissedSamples,
                droppedJobs = frame.droppedJobs,
                skippedImages = frame.skippedImages,
                sampleInterval = frame.sampleInterval,
                characterMilliseconds = frame.character?.captureMilliseconds ?? 0,
                transitions = frame.transitions
            });
            FrameSummary summary = summaries[summaries.Count - 1];
            summary.skin ??= frame.character?.skinMesh;
            summary.motion ??= frame.character?.motion;
        }
        if (work.image != null)
            images.Add(work.image);
        if (work.data is CaptureEvent captureEvent)
            events.Add(captureEvent);
    }

    private bool Reserve(int bytes)
    {
        long reportReserve = 65536L + (summaries.Count + 1L) * 8192L + (images.Count + 1L) * 2048L + (events.Count + 1L) * 49152L;
        if (WrittenBytes + bytes + reportReserve > maximumBytes)
        {
            budgetReached = true;
            return false;
        }
        Interlocked.Add(ref writtenBytes, bytes);
        return true;
    }
}
