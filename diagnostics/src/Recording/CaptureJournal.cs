using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BepInEx.Logging;

namespace MikuGlider.Diagnostics;

internal sealed class CaptureJournal : ILogListener
{
    private CaptureSession session;
    private readonly Dictionary<string, string> previous = new Dictionary<string, string>();

    internal CaptureJournal()
    {
        Logger.Listeners.Add(this);
    }

    internal void Begin(CaptureSession value)
    {
        Interlocked.Exchange(ref session, value);
        previous.Clear();
    }

    internal string[] Transitions(FrameData frame)
    {
        var changes = new List<string>();
        Compare("scene", frame.scene);
        Compare("skin", frame.cloth?.skin ?? frame.character?.skinMesh);
        Compare("perspective", frame.character?.perspective);
        Compare("motion", frame.character?.motion ?? frame.cloth?.motion);
        Compare("weapon", frame.character?.weapon);
        Compare("cloth_binding", frame.cloth?.binding);
        Compare("animation", frame.character?.animations == null ? frame.cloth?.clip :
            string.Join(" | ", frame.character.animations.Select(state => state.rig + ":" + state.layer + ":" + state.clip)));
        return changes.ToArray();

        void Compare(string key, string value)
        {
            value ??= "unavailable";
            if (previous.TryGetValue(key, out string old) && old == value)
                return;
            previous[key] = value;
            string message = key + ": " + (old ?? "start") + " -> " + value;
            changes.Add(message);
            session?.Event(new CaptureEvent
            {
                utc = DateTime.UtcNow.ToString("O"), realtime = frame.realtime, sample = frame.sample,
                unityFrame = frame.unityFrame, kind = "transition", source = key, message = message
            });
        }
    }

    public void LogEvent(object sender, LogEventArgs args)
    {
        CaptureSession target = Volatile.Read(ref session);
        if (target == null || (args.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
            return;
        string source = args.Source.SourceName ?? string.Empty;
        string message = args.Data?.ToString() ?? string.Empty;
        if (source.IndexOf("Miku", StringComparison.OrdinalIgnoreCase) < 0 &&
            message.IndexOf("Miku", StringComparison.OrdinalIgnoreCase) < 0 &&
            message.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) < 0)
            return;
        target.Event(new CaptureEvent
        {
            utc = DateTime.UtcNow.ToString("O"), kind = args.Level.ToString(),
            source = Limit(source, 120), message = Limit(message, 6000)
        });
    }

    private static string Limit(string value, int count) => value.Length > count ? value.Substring(0, count) + " [truncated]" : value;

    public void Dispose()
    {
        Interlocked.Exchange(ref session, null);
        Logger.Listeners.Remove(this);
    }
}
