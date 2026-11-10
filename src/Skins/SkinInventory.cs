using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace MikuGlider;

internal sealed class SkinInventory
{
    private readonly SkinRegistry skins;
    private readonly ManualLogSource logger;
    private readonly HashSet<int> processedSkins = new HashSet<int>();
    private PlayerData currentProfile;
    private Character character;

    internal SkinInventory(SkinRegistry skins, ManualLogSource logger)
    {
        this.skins = skins;
        this.logger = logger;
    }

    internal void SetCharacter(Character character)
    {
        this.character = character;
    }

    internal void GrantForLoadedProfile()
    {
        if (character == null || PlayerData.Instance == null || skins.Count == 0)
            return;

        if (currentProfile != PlayerData.Instance)
        {
            currentProfile = PlayerData.Instance;
            processedSkins.Clear();
        }

        if (processedSkins.Count == skins.Count)
            return;

        foreach (RegisteredSkin skin in skins.Registered)
        {
            if (processedSkins.Add(skin.Definition.NumericId))
                GrantIfMissing(skin);
        }
    }

    private void GrantIfMissing(RegisteredSkin skin)
    {
        try
        {
            var unlocked = PlayerData.GetUnlockedInstances(skin.Upgrade);
            if (unlocked?.Instances != null && unlocked.Instances.Count > 0)
                return;

            PlayerData.CollectInstance(character, skin.Upgrade, PlayerData.UnlockFlags.Hidden);
            PlayerData.SaveData();
            logger.LogInfo("Added " + skin.Definition.DisplayName + " to Glider's skin inventory.");
        }
        catch (Exception error)
        {
            logger.LogError("Could not grant " + skin.Definition.DisplayName + ": " + error);
        }
    }
}
