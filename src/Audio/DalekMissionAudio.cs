using System;
using BepInEx.Logging;

namespace MikuGlider;

internal sealed class DalekMissionAudio
{
    private readonly AudioSettings settings;
    private readonly ManualLogSource logger;
    private readonly MissionIntroChance chance;
    private readonly DalekPlayback playback = new DalekPlayback();
    private bool active = true;

    internal DalekMissionAudio(AudioSettings settings, ManualLogSource logger, Func<int> nextBit = null)
    {
        this.settings = settings;
        this.logger = logger;
        // Keep cosmetic randomness separate from Unity/Pigeon's gameplay RNG.
        var random = new Random();
        chance = new MissionIntroChance(nextBit ?? (() => random.Next(2)));
    }

    internal void OnObjectiveShown(string popup, bool showNewObjectiveText, bool isSideObjective)
    {
        if (!active) return;
        try
        {
            var mission = MissionManager.Instance;
            var objective = mission?.CurrentObjective;
            if (mission == null || objective == null) return;
            var info = objective.ObjectiveInfoList;
            var context = new MissionIntroContext
            {
                ShowNewObjectiveText = showNewObjectiveText,
                IsSideObjective = isSideObjective || objective.IsSideObjective,
                LateJoined = MissionManager.LateJoined,
                MissionEnded = mission.HasMissionEnded,
                MainObjectiveIndex = mission.CurrentObjectiveIndex,
                ObjectiveTextIndex = objective.ObjectiveTextIndex,
                Popup = popup,
                KillTitle = TextBlocks.GetString("o_kill"),
                MainTitle = info != null && info.Length > 0 ? info[0].title : null
            };
            if (!context.IsEligible) return;

            var decision = chance.Decide(mission, settings.DalekMissionIntroEnabled);
            if (decision == MissionIntroDecision.Ignored)
                return;
            string description = "Dalek mission intro: KILL (" + objective.GetType().Name + ")";
            if (decision != MissionIntroDecision.Play)
            {
                logger.LogInfo(description + (decision == MissionIntroDecision.Disabled
                    ? "; disabled by config." : "; roll=skip (50%)."));
                return;
            }
            uint playingId = playback.Play();
            if (playingId == 0)
                logger.LogWarning(description + "; roll=play (50%), but Wwise rejected Play_VideoAudio.");
            else
                logger.LogInfo(description + "; roll=play (50%); Wwise playing ID " + playingId + ".");
        }
        catch (Exception error)
        {
            // A cosmetic audio failure must never interrupt objective creation.
            logger.LogWarning("Dalek mission intro skipped: " + error.Message);
        }
    }

    internal void SetActive(bool value)
    {
        active = value;
        if (!active) Stop();
    }

    internal void Stop() => playback.Stop();
}
