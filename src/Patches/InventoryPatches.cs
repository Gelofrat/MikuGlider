using HarmonyLib;

namespace MikuGlider;

[HarmonyPatch(typeof(PlayerData), "OnAwake")]
internal static class ProfileLoadedPatch
{
    private static void Postfix()
    {
        MikuGliderPlugin.Runtime?.Inventory.GrantForLoadedProfile();
    }
}

[HarmonyPatch(typeof(SkinInstance), nameof(SkinInstance.Upgrade), MethodType.Getter)]
internal static class SkinInstanceLookupPatch
{
    private static void Postfix(SkinInstance __instance, ref Upgrade __result)
    {
        if (__result == null)
            __result = MikuGliderPlugin.Runtime?.Skins.FindById(__instance.upgradeID)?.Upgrade;
    }
}
