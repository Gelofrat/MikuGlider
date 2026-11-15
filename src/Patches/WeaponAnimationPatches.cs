using HarmonyLib;
using Pigeon.Movement;

namespace MikuGlider;

[HarmonyPatch(typeof(PlayerAnimation), nameof(PlayerAnimation.ImportAnimationSet))]
internal static class WeaponAnimationSetPatch
{
    private static void Postfix(PlayerAnimation __instance, bool ___isFirstPerson, Player ___playerMovement)
    {
        if (!___isFirstPerson || ___playerMovement == null || !___playerMovement.IsLocalPlayer ||
            MikuGliderPlugin.Runtime?.Skins.FindByBody(__instance.OverrideThirdPersonMesh) == null)
            return;

        // Gun.Enable imports the new base pose before processing held fire input.
        // The native implementation leaves the previous gun's additive recoil
        // layer active, including its non-neutral final frame. Stop the whole
        // layer so its CurrentState is cleared and the next shot can restart it.
        // PlayerAnimation.Stop only handles the aim layer, not recoil.
        __instance.GetCurrentState(PlayerAnimation.RecoilLayer)?.Layer.Stop();
    }
}
