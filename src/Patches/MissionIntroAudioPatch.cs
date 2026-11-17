using HarmonyLib;

namespace MikuGlider;

[HarmonyPatch(typeof(MissionHUD), nameof(MissionHUD.ShowNewObjective))]
internal static class MissionIntroAudioPatch
{
    private static void Prefix(MissionHUD __instance, string popup, bool overrideCurrent, out bool __state)
    {
        // Mirror the native early exits: ignored popups must not consume a roll.
        __state = !string.IsNullOrEmpty(popup) && (!__instance.IsShowingNewObjective || overrideCurrent);
    }

    private static void Postfix(string popup, bool showNewObjectiveText, bool isSideObjective, bool __state)
    {
        if (__state)
            MikuGliderPlugin.Runtime?.MissionAudio.OnObjectiveShown(popup, showNewObjectiveText, isSideObjective);
    }
}
