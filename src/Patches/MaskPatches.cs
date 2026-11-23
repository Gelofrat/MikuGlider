using HarmonyLib;
using UnityEngine;

namespace MikuGlider;

[HarmonyPatch(typeof(MenuRig), "DrawHatPreview")]
internal static class MaskPreviewPatch
{
    private static void Prefix(MenuRig __instance, UpgradeInstance upgrade, ref SkinUpgradeProperty_Hat hat,
        Character ___character, Transform ___headBone)
    {
        var mask = MikuGliderPlugin.Mask;
        if (mask != null && upgrade?.Upgrade == mask.Upgrade && ___character != null && ___headBone != null)
            hat = mask.Preview(__instance, ___character, ___headBone);
    }
}

[HarmonyPatch(typeof(SkinUpgradeProperty_Hat), nameof(SkinUpgradeProperty_Hat.SetupInstance))]
internal static class MaskFaceSocketPatch
{
    private static void Prefix(SkinUpgradeProperty_Hat __instance, ref Transform parent)
    {
        if (__instance == MikuGliderPlugin.Mask?.Hat && PorcelainMask.FaceSocket(parent) is Transform face)
            parent = face;
    }
}
