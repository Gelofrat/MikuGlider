using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class SurfaceAnalysis : IDisposable
{
    private sealed class Result
    {
        internal GeometryData geometry;
        internal Vector3[] surface;
    }

    private readonly SkinningModel skirt;
    private readonly SkinningModel body;
    private readonly SkinningModel rendered;
    private readonly SkinningModel coat;
    private SurfaceGeometry geometry;
    private SurfaceGeometry layers;
    private Task<Result> pending;
    private bool disposed;

    internal SurfaceAnalysis(SkirtTarget target, SkinningModel skirt)
    {
        this.skirt = skirt;
        body = new SkinningModel(target.Body, true);
        if (target.Surface != null)
            rendered = new SkinningModel(target.Surface);
        if (target.Coat != null)
            coat = new SkinningModel(target.Coat);
    }

    internal bool TryCollect(out GeometryData data, out Vector3[] surface)
    {
        data = null;
        surface = null;
        if (disposed || pending == null || !pending.IsCompleted)
            return false;
        Result result = pending.GetAwaiter().GetResult();
        pending = null;
        data = result.geometry;
        surface = result.surface;
        return true;
    }

    internal bool Schedule(Vector3[] particles, Vector3[] reference, int budget, double realtime)
    {
        if (disposed || pending != null)
            return false;
        rendered?.RefreshVertices();
        coat?.RefreshVertices();
        Vector3[] surface = rendered != null ? rendered.Pose() : particles;
        Vector3[] overlay = coat?.Pose();
        Matrix4x4[] matrices = body.CaptureMatrices();
        pending = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            var data = new GeometryData
            {
                sampledAt = realtime,
                source = rendered != null ? "published_render_surface" : "skinned_pose",
                surfaceReliable = true,
                surfaceError = 0
            };
            try
            {
                body.PrepareLowerBody();
                geometry ??= new SurfaceGeometry(skirt.Rest, skirt.Triangles, body.Rest, body.Triangles);
                geometry.Analyze(surface, reference, body.Pose(matrices), budget, data);
                if (overlay != null)
                {
                    layers ??= new SurfaceGeometry(skirt.Rest, skirt.Triangles, coat.Rest, coat.Triangles);
                    var layerResult = new GeometryData();
                    layers.Analyze(surface, reference, overlay, budget, layerResult);
                    data.coatCrossings = layerResult.bodyCrossings;
                }
            }
            catch (Exception error)
            {
                data.surfaceReliable = false;
                data.limitation = "Background surface analysis failed: " + error.Message;
            }
            data.analysisMilliseconds = watch.Elapsed.TotalMilliseconds;
            return new Result { geometry = data, surface = data.surfaceReliable ? surface : null };
        });
        return true;
    }

    public void Dispose()
    {
        disposed = true;
        pending = null;
    }
}
