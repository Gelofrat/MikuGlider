using HarmonyLib;
using UnityEngine;

namespace MikuGlider;

[HarmonyPatch(typeof(DeadPlayerPart), "Initialize_ClientRpc")]
internal static class DeathPartInitializedPatch
{
    private static void Prefix(DeadPlayerPart.PartSectionData[] ___sections) =>
        DeathPartDespawnPatch.Restore(___sections);

    private static void Postfix(DeadPlayerPart.PartSectionData[] ___sections)
    {
        var skins = MikuGliderPlugin.Runtime?.Skins;
        if (skins == null || ___sections == null) return;
        foreach (var section in ___sections)
        {
            if (section.meshes == null || section.meshes.Length == 0) continue;
            var target = section.meshes[0];
            if (target == null || target.mesh == null) continue;
            var renderer = target.mesh.GetComponent<Renderer>();
            if (renderer == null) continue;
            Mesh mesh = target.mesh.sharedMesh;
            foreach (RegisteredSkin skin in skins.Registered)
            {
                bool torso = mesh == skin.Assets.GetMesh("death-torso");
                if (!torso && mesh != skin.Assets.GetMesh("death-legs")) continue;
                DeathPartMaterials.Apply(renderer, torso ? skin.DeathTorsoMaterials : skin.DeathLegMaterials);
                break;
            }
        }
    }
}

[HarmonyPatch(typeof(DeadPlayerPart), nameof(DeadPlayerPart.OnNetworkDespawn))]
internal static class DeathPartDespawnPatch
{
    private static void Prefix(DeadPlayerPart.PartSectionData[] ___sections) => Restore(___sections);

    internal static void Restore(DeadPlayerPart.PartSectionData[] sections)
    {
        if (sections == null) return;
        foreach (var section in sections)
        {
            if (section.meshes == null || section.meshes.Length == 0) continue;
            var target = section.meshes[0];
            if (target == null || target.mesh == null) continue;
            var renderer = target.mesh.GetComponent<Renderer>();
            if (renderer != null) DeathPartMaterials.Release(renderer);
        }
    }
}
