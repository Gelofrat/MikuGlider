using System;
using HarmonyLib;
using Pigeon.Movement;

namespace MikuGlider;

[HarmonyPatch(typeof(PlayerLook), nameof(PlayerLook.DisableThirdPerson))]
internal static class ThirdPersonDisabledPatch
{
    private static void Prefix(PlayerLook __instance)
    {
        MikuGliderPlugin.Runtime?.Camera.BeforeDisable(__instance);
    }
}

[HarmonyPatch(typeof(PlayerLook), nameof(PlayerLook.EnableThirdPerson))]
internal static class ThirdPersonEnabledPatch
{
    private static void Prefix(PlayerLook __instance)
    {
        MikuGliderPlugin.Runtime?.Camera.BeforeEnable(__instance);
    }
}

[HarmonyPatch(typeof(PlayerLook), "HandleCameraControl")]
internal static class CameraControlPatch
{
    private static void Prefix(PlayerLook __instance)
    {
        MikuGliderPlugin.Runtime?.Camera.BeforeCameraUpdate(__instance);
    }

    private static void Postfix(PlayerLook __instance)
    {
        MikuGliderPlugin.Runtime?.Camera.AfterCameraUpdate(__instance);
    }

    private static Exception Finalizer(PlayerLook __instance, Exception __exception)
    {
        MikuGliderPlugin.Runtime?.Camera.ReleaseFrameOverrides(__instance);
        return __exception;
    }
}

[HarmonyPatch(typeof(ThirdPersonAnimator), "HandleInterrupts")]
internal static class ThirdPersonInterruptsPatch
{
    private static bool Prefix(ThirdPersonAnimator __instance)
    {
        return MikuGliderPlugin.Runtime?.Camera.ControlsAnimator(__instance) != true;
    }
}
