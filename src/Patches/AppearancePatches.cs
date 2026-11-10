using System.Collections.Generic;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

[HarmonyPatch(typeof(Player), nameof(Player.ApplySkins))]
internal static class PlayerSkinsAppliedPatch
{
    private static void Postfix(Player __instance)
    {
        SkinAppearanceController appearance = MikuGliderPlugin.Runtime?.Appearance;
        appearance?.RefreshPlayer(__instance);
        appearance?.RefreshUtilities(__instance);
    }
}

[HarmonyPatch(typeof(ThirdPersonAnimator), nameof(ThirdPersonAnimator.UpdateMesh))]
internal static class SelfMeshUpdatedPatch
{
    private static void Postfix(ThirdPersonAnimator __instance)
    {
        ThirdPersonRig rig = NativeSkinAccess.GetSelfRig(__instance);
        if (rig != null)
            MikuGliderPlugin.Runtime?.Appearance.EnsureRigVisuals(rig, Player.LocalPlayer);
    }
}

[HarmonyPatch(typeof(MenuRig), "LateUpdate")]
internal static class MenuRigUpdatePatch
{
    private static void Prefix(MenuRig __instance, Character ___character, Transform ___headBone)
    {
        MikuGliderPlugin.Runtime?.Appearance.RefreshPreview(__instance, ___character, ___headBone);
    }

    private static void Postfix(MenuRig __instance, Transform ___headBone)
    {
        MikuGliderPlugin.Runtime?.Appearance.EnsurePreviewVisuals(__instance, ___headBone);
    }
}

[HarmonyPatch(typeof(CharacterSelectWindow), "ApplySkin")]
internal static class CharacterPreviewMaterialsPatch
{
    private static void Prefix(List<Material> mats, Mesh mesh)
    {
        MikuGliderPlugin.Runtime?.Appearance.PreparePreviewMaterials(mesh, mats);
    }
}

[HarmonyPatch(typeof(MenuRig), "OnDisable")]
internal static class MenuRigDisabledPatch
{
    private static void Postfix(MenuRig __instance)
    {
        MikuGliderPlugin.Runtime?.Appearance.ResetPreview(__instance);
    }
}

[HarmonyPatch(typeof(Wingsuit), nameof(Wingsuit.OnSkinsApplied))]
internal static class WingsuitSkinsAppliedPatch
{
    private static void Postfix(Player ___player, Transform ___wingLeft, Transform ___wingRight, Transform ___salvoModel)
    {
        MikuGliderPlugin.Runtime?.Appearance.ApplyUtilityMaterials(___player, ___wingLeft, ___wingRight, ___salvoModel);
    }
}
