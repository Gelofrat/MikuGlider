using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MikuGlider.Diagnostics;

internal sealed class GameFrameCapture : IDisposable
{
    private sealed class Slot
    {
        internal RenderTexture full;
        internal RenderTexture scaled;
        internal bool busy;
        internal bool lease;
        internal CaptureSession session;
        internal ImageData metadata;
        internal double started;
    }

    private readonly Slot[] slots = { new Slot(), new Slot() };
    private bool disposed;
    private double retryAfter;
    internal int Skipped { get; private set; }
    internal string Error { get; private set; }

    internal bool Request(CaptureSession session, FrameData frame, int maximumWidth)
    {
        if (Time.realtimeSinceStartupAsDouble < retryAfter)
        {
            Skipped++;
            return false;
        }
        if (disposed || !SystemInfo.supportsAsyncGPUReadback || Screen.width < 1 || Screen.height < 1)
        {
            Error = "Asynchronous GPU readback is unavailable; telemetry remains active.";
            Skipped++;
            return false;
        }
        Slot slot = Array.Find(slots, item => !item.busy);
        if (slot == null)
        {
            Skipped++;
            return false;
        }
        if (!session.LeaseImage())
        {
            Skipped++;
            return false;
        }
        var metadata = new ImageData { sample = frame.sample, unityFrame = frame.unityFrame, realtime = frame.realtime };
        slot.lease = true;
        slot.session = session;
        slot.metadata = metadata;
        slot.started = Time.realtimeSinceStartupAsDouble;
        RenderTexture previous = RenderTexture.active;
        try
        {
            int width = Math.Min(maximumWidth, Screen.width);
            int height = Math.Max(1, (int)Math.Round((double)Screen.height * width / Screen.width));
            Ensure(slot, Screen.width, Screen.height, width, height);
            slot.busy = true;
            metadata.width = width;
            metadata.height = height;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(slot.full);
            Graphics.Blit(slot.full, slot.scaled);
            AsyncGPUReadback.Request(slot.scaled, 0, TextureFormat.RGBA32, result =>
            {
                try
                {
                    if (!slot.lease)
                        return;
                    if (result.hasError)
                    {
                        metadata.error = "GPU readback failed.";
                        session.Image(metadata, null);
                    }
                    else
                    {
                        Error = null;
                        byte[] data = result.GetData<byte>().ToArray();
                        session.Image(metadata, data);
                    }
                }
                catch (Exception error)
                {
                    Error = error.Message;
                    metadata.error = error.Message;
                    session.Image(metadata, null);
                }
                finally
                {
                    slot.busy = false;
                    ReleaseLease(slot);
                    if (disposed)
                        Release(slot);
                }
            });
            return true;
        }
        catch (Exception error)
        {
            slot.busy = false;
            Error = error.Message;
            retryAfter = Time.realtimeSinceStartupAsDouble + 5;
            metadata.error = error.Message;
            session.Image(metadata, null);
            ReleaseLease(slot);
            Skipped++;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    internal static byte[] Encode(byte[] pixels, int width, int height, bool flip = false)
    {
        int stride = checked(width * 4);
        if (pixels.Length != checked(stride * height))
            throw new InvalidOperationException("Unexpected screenshot buffer length.");
        if (flip)
        {
            var flipped = new byte[pixels.Length];
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(pixels, row * stride, flipped, (height - row - 1) * stride, stride);
            pixels = flipped;
        }
        return ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
    }

    internal void Tick()
    {
        foreach (Slot slot in slots)
            if (slot.busy && slot.lease && Time.realtimeSinceStartupAsDouble - slot.started > 5)
            {
                Error = "GPU readback timed out; telemetry continues.";
                slot.metadata.error = Error;
                slot.session.Image(slot.metadata, null);
                ReleaseLease(slot);
            }
    }

    public void Dispose()
    {
        disposed = true;
        foreach (Slot slot in slots)
        {
            if (slot.lease)
            {
                slot.metadata.error = "Capture component was disabled before readback completed.";
                slot.session.Image(slot.metadata, null);
                ReleaseLease(slot);
            }
            if (!slot.busy)
                Release(slot);
        }
    }

    private static void ReleaseLease(Slot slot)
    {
        if (!slot.lease)
            return;
        slot.lease = false;
        slot.session.ReleaseImage();
        slot.session = null;
        slot.metadata = null;
    }

    private static void Ensure(Slot slot, int fullWidth, int fullHeight, int width, int height)
    {
        if (slot.full != null && slot.full.width == fullWidth && slot.full.height == fullHeight &&
            slot.scaled != null && slot.scaled.width == width && slot.scaled.height == height)
            return;
        Release(slot);
        slot.full = Texture(fullWidth, fullHeight, "Miku diagnostics screen");
        slot.scaled = Texture(width, height, "Miku diagnostics capture");
    }

    private static RenderTexture Texture(int width, int height, string name)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = name,
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear
        };
        if (!texture.Create())
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidOperationException("Could not create a screenshot texture.");
        }
        return texture;
    }

    private static void Release(Slot slot)
    {
        if (slot.full != null)
        {
            slot.full.Release();
            UnityEngine.Object.Destroy(slot.full);
        }
        if (slot.scaled != null)
        {
            slot.scaled.Release();
            UnityEngine.Object.Destroy(slot.scaled);
        }
        slot.full = null;
        slot.scaled = null;
    }
}
