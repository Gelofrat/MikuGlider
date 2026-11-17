using BepInEx.Configuration;

namespace MikuGlider;

internal sealed class AudioSettings
{
    private readonly ConfigEntry<bool> dalekMissionIntro;

    internal bool DalekMissionIntroEnabled => dalekMissionIntro.Value;

    internal AudioSettings(ConfigFile config)
    {
        dalekMissionIntro = config.Bind("Audio", "EnableDalekMissionIntro", true,
            "Play Dalek Exterminate with a 50% chance when the first main mission objective shows KILL. Local playback through the game's Wwise audio engine. Set false to disable.");
    }
}
