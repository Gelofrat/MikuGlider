// Contract fixtures: this suite does not claim to run Unity or native Wwise.
using System;
using System.Runtime.CompilerServices;

public class MissionManager
{
    public static MissionManager Instance;
    public static bool LateJoined;
    public bool HasMissionEnded;
    public int CurrentObjectiveIndex;
    public IObjective CurrentObjective = new RegulatedRampageObjective();
}
public interface IObjective
{
    bool IsSideObjective { get; set; }
    int ObjectiveTextIndex { get; set; }
    ObjectiveInfo[] ObjectiveInfoList { get; }
}
public struct ObjectiveInfo { public string title; }
public class TestObjective : IObjective
{
    public bool IsSideObjective { get; set; }
    public int ObjectiveTextIndex { get; set; }
    public ObjectiveInfo[] ObjectiveInfoList { get; set; } = new[] { new ObjectiveInfo { title = TextBlocks.Kill } };
}
public class RegulatedRampageObjective : TestObjective { }
public class AmalgamationObjective : TestObjective { }
public class FutureKillObjective : TestObjective { }
public static class TextBlocks
{
    public static string Kill = "KILL";
    public static string GetString(string key) => key == "o_kill" ? Kill : key;
}
public class MissionHUD
{
    public bool IsShowingNewObjective;
    public int Shown;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ShowNewObjective(string popup, bool showNewObjectiveText = true, float delay = 0,
        bool overrideCurrent = true, bool isSideObjective = false)
    {
        if (string.IsNullOrEmpty(popup) || (IsShowingNewObjective && !overrideCurrent)) return;
        Shown++;
    }
}
namespace Pigeon.Movement
{
    public class PlayerLook
    {
        public static PlayerLook Instance = new PlayerLook();
        public readonly object gameObject = new object();
    }
}
public class AkAudioFormat
{
    public class Channels { public uint uNumChannels, eConfigType, uChannelMask; }
    public readonly Channels channelConfig = new Channels();
    public uint uSampleRate;
}
public static class AkAudioInputManager
{
    public static int Calls;
    public static string LastEvent;
    public static AkAudioFormat Format;
    public static Func<uint, uint, float[], bool> Fill;
    public static uint PostAudioInputEvent(string name, object emitter,
        Func<uint, uint, float[], bool> samples, Action<uint, AkAudioFormat> format)
    {
        Calls++;
        LastEvent = name;
        Fill = samples;
        Format = new AkAudioFormat();
        format(123, Format);
        return 123;
    }
}
public static class AkUnitySoundEngine
{
    public static uint Stopped;
    public static void StopPlayingID(uint id) { Stopped = id; }
}
namespace MikuGlider
{
    public class MikuGliderPlugin { internal static TestRuntime Runtime; }
    internal class TestRuntime { internal DalekMissionAudio MissionAudio; }
}
