using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MikuGlider;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }

    private static void Reject(byte[] bytes, string name)
    {
        try { MonoPcmWave.Read(new MemoryStream(bytes)); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception("Accepted " + name);
    }

    private static int Main(string[] args)
    {
        string path = Path.GetFullPath(args[0]);
        var config = new ConfigFile(path, false);
        var settings = new AudioSettings(config);
        if (args.Length > 1 && args[1] == "--create")
        {
            config.Save();
            Console.WriteLine("AUDIO_CONFIG_SAVED (existing settings preserved)");
            return 0;
        }
        Check(settings.DalekMissionIntroEnabled, "default enabled");
        config["Audio", "EnableDalekMissionIntro"].BoxedValue = false;
        config.Save();
        var loaded = new AudioSettings(new ConfigFile(path, false));
        Check(!loaded.DalekMissionIntroEnabled, "disabled setting survives reload");
        config["Audio", "EnableDalekMissionIntro"].BoxedValue = true;
        config.Save();

        byte[] bytes;
        using (var source = new EmbeddedResources("MikuGlider.Audio").Open("DalekExterminate", 4 * 1024 * 1024))
        using (var copy = new MemoryStream()) { source.CopyTo(copy); bytes = copy.ToArray(); }
        var wave = MonoPcmWave.Read(new MemoryStream(bytes));
        Check(wave.Samples.Length > 96000 && wave.Samples.Length < 144000, "2-3 second audio");
        Check(wave.Samples.Any(x => Math.Abs(x) > .1f), "audio not silent");
        Check(wave.Samples.All(x => x >= -1 && x < 1), "PCM range");
        var playback = new MonoPcmPlayback(wave);
        var buffer = new float[1024];
        int offset = 0;
        while (playback.Fill(123, 0, buffer))
        {
            for (int i = 0; i < buffer.Length; i++)
                Check(buffer[i] == (offset + i < wave.Samples.Length ? wave.Samples[offset + i] : 0), "sample exact, tail padded");
            offset += buffer.Length;
        }
        Check(buffer.All(x => x == 0) && !playback.Fill(123, 0, buffer), "EOS stays silent and stops");
        Reject(bytes.Take(bytes.Length - 1).ToArray(), "truncated WAV");
        var corrupt = (byte[])bytes.Clone(); corrupt[0] = 0;
        Reject(corrupt, "not RIFF");
        corrupt = (byte[])bytes.Clone(); corrupt[22] = 2;
        Reject(corrupt, "stereo sent to mono callback");

        int rolls = 0;
        var decision = new MissionIntroChance(() => rolls++ % 2);
        var token = new object();
        Check(decision.Decide(token, false) == MissionIntroDecision.Disabled && rolls == 0, "disabled consumes no RNG");
        Check(decision.Decide(token, true) == MissionIntroDecision.Ignored, "enabling does not replay same intro");
        int wins = 0;
        for (int i = 0; i < 100; i++)
        {
            token = new object();
            if (decision.Decide(token, true) == MissionIntroDecision.Play) wins++;
            Check(decision.Decide(token, true) == MissionIntroDecision.Ignored, "duplicate cannot reroll wins or losses");
        }
        Check(wins == 50 && rolls == 100, "exactly one of two equiprobable outcomes plays");

        var logger = new ManualLogSource("MissionAudioTest");
        var messages = new List<string>();
        logger.LogEvent += (sender, e) => messages.Add(e.Data.ToString());
        int introRolls = 0;
        var audio = new DalekMissionAudio(settings, logger, () => { introRolls++; return 0; });
        MikuGliderPlugin.Runtime = new TestRuntime { MissionAudio = audio };
        var harmony = new Harmony("horse.mission-audio.test");
        harmony.PatchAll(typeof(Program).Assembly);
        var hud = new MissionHUD();
        Action<string> show = s => hud.ShowNewObjective(s);
        Action reset = () => { MissionManager.Instance = new MissionManager(); MissionManager.LateJoined = false; };
        reset();
        show(null); show("OTHER");
        Check(introRolls == 0, "unrelated/empty popup does not roll");
        hud.IsShowingNewObjective = true;
        hud.ShowNewObjective("KILL", overrideCurrent: false);
        Check(introRolls == 0, "native ignored popup does not roll");
        hud.IsShowingNewObjective = false;
        show("KILL"); show("KILL");
        Check(introRolls == 1 && AkAudioInputManager.Calls == 1, "one native event for repeated intro");
        Check(AkAudioInputManager.LastEvent == "Play_VideoAudio" &&
            AkAudioInputManager.Format.uSampleRate == 48000 && AkAudioInputManager.Format.channelConfig.uNumChannels == 1,
            "correct game event and PCM format");
        Check(AkAudioInputManager.Fill(123, 0, buffer), "callback retained and playable");
        reset(); MissionManager.Instance.CurrentObjectiveIndex = 1; show("KILL");
        reset(); MissionManager.Instance.CurrentObjective = null; show("KILL");
        reset(); MissionManager.Instance.HasMissionEnded = true; show("KILL");
        reset(); MissionManager.LateJoined = true; show("KILL");
        reset(); ((RegulatedRampageObjective)MissionManager.Instance.CurrentObjective).IsSideObjective = true; show("KILL");
        reset(); ((RegulatedRampageObjective)MissionManager.Instance.CurrentObjective).ObjectiveTextIndex = 1; show("KILL");
        reset(); hud.ShowNewObjective("KILL", isSideObjective: true);
        reset(); hud.ShowNewObjective("KILL", showNewObjectiveText: false);
        Check(introRolls == 1, "later, ended, side, missing objective, late join and non-objective excluded");
        reset(); config["Audio", "EnableDalekMissionIntro"].BoxedValue = false; show("KILL");
        Check(introRolls == 1 && AkAudioInputManager.Calls == 1, "config off prevents playback and RNG");
        TextBlocks.Kill = "УБИВАЙ";
        reset(); config["Audio", "EnableDalekMissionIntro"].BoxedValue = true; show("УБИВАЙ");
        Check(introRolls == 2 && AkAudioInputManager.Calls == 2, "localized native key works on next mission");
        reset(); MissionManager.Instance.CurrentObjective = new AmalgamationObjective();
        show("УБИВАЙ"); show("УБИВАЙ");
        Check(introRolls == 3 && AkAudioInputManager.Calls == 3, "REGRESSION: Amalgamation Hunt KILL gets one attempt");
        reset(); MissionManager.Instance.CurrentObjective = new FutureKillObjective(); show("УБИВАЙ");
        Check(introRolls == 4 && AkAudioInputManager.Calls == 4, "no objective class allowlist");
        Check(messages.Count(x => x.Contains("AmalgamationObjective") && x.Contains("roll=play")) == 1,
            "Amalgamation decision logged once");
        Check(messages.Any(x => x.Contains("disabled by config")), "disabled reason logged");
        reset();
        ((TestObjective)MissionManager.Instance.CurrentObjective).ObjectiveInfoList[0].title = "ACTIVATE RING";
        show("УБИВАЙ");
        reset(); ((TestObjective)MissionManager.Instance.CurrentObjective).ObjectiveInfoList = null; show("УБИВАЙ");
        reset(); ((TestObjective)MissionManager.Instance.CurrentObjective).ObjectiveInfoList = new ObjectiveInfo[0]; show("УБИВАЙ");
        Check(introRolls == 4, "nested kill subtask and missing title metadata do not count as main intro");
        audio.Stop(); Check(AkUnitySoundEngine.Stopped == 123, "stops only own playing ID");
        var losingAudio = new DalekMissionAudio(settings, logger, () => 1);
        MikuGliderPlugin.Runtime.MissionAudio = losingAudio;
        reset(); MissionManager.Instance.CurrentObjective = new AmalgamationObjective();
        show("УБИВАЙ"); show("УБИВАЙ");
        Check(AkAudioInputManager.Calls == 4, "failed coin toss does not play");
        Check(messages.Count(x => x.Contains("AmalgamationObjective") && x.Contains("roll=skip")) == 1,
            "failed Amalgamation coin toss logged once instead of silent type rejection");

        // Both outcomes must be reachable for every class; no statistical/flaky test.
        foreach (Func<IObjective> makeObjective in new Func<IObjective>[] {
            () => new RegulatedRampageObjective(), () => new AmalgamationObjective(), () => new FutureKillObjective() })
        {
            int classRolls = 0;
            var classAudio = new DalekMissionAudio(settings, logger, () => classRolls++ % 2);
            MikuGliderPlugin.Runtime.MissionAudio = classAudio;
            int previousCalls = AkAudioInputManager.Calls;
            for (int i = 0; i < 10; i++)
            {
                reset(); MissionManager.Instance.CurrentObjective = makeObjective();
                show("УБИВАЙ"); show("УБИВАЙ");
            }
            Check(classRolls == 10 && AkAudioInputManager.Calls - previousCalls == 5,
                "all initial KILL objective classes reach the unchanged 50% decision exactly once per mission");
            classAudio.Stop();
        }
        int lifecycleRolls = 0;
        var lifecycle = new DalekMissionAudio(settings, logger, () => { lifecycleRolls++; return 0; });
        MikuGliderPlugin.Runtime.MissionAudio = lifecycle;
        lifecycle.SetActive(false);
        int callsBeforeStop = AkAudioInputManager.Calls;
        reset(); show(TextBlocks.Kill);
        Check(lifecycleRolls == 0 && AkAudioInputManager.Calls == callsBeforeStop,
            "retained Harmony patches cannot play or roll while session is disabled");
        lifecycle.SetActive(true); show(TextBlocks.Kill);
        Check(lifecycleRolls == 1 && AkAudioInputManager.Calls == callsBeforeStop + 1, "resume accepts next eligible intro");
        lifecycle.SetActive(false);
        Check(AkUnitySoundEngine.Stopped == 123, "session disable stops own playback");
        lifecycle.SetActive(true); show(TextBlocks.Kill);
        Check(lifecycleRolls == 1, "session resume cannot replay already consumed mission");
        harmony.UnpatchSelf();
        Console.WriteLine("MISSION_AUDIO_PASS " + checks + " checks; Harmony fixture, config, 50% decision, exclusions, exact PCM and final buffer. Native game audio not executed.");
        return 0;
    }
}
