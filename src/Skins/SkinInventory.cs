using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace MikuGlider;

internal sealed class SkinInventory
{
    private readonly SkinRegistry skins;
    private readonly ManualLogSource logger;
    private readonly ConditionalWeakTable<PlayerData, ProfileProgress> profiles = new ConditionalWeakTable<PlayerData, ProfileProgress>();
    private readonly Func<DateTime> utcNow;
    private Character character;

    private sealed class ProfileProgress
    {
        internal readonly HashSet<int> Processed = new HashSet<int>();
        internal DateTime RetryAt;
        internal bool SavePending;
    }

    internal SkinInventory(SkinRegistry skins, ManualLogSource logger, Func<DateTime> utcNow = null)
    {
        this.skins = skins;
        this.logger = logger;
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    internal void SetCharacter(Character character)
    {
        this.character = character;
    }

    internal void GrantForLoadedProfile()
    {
        if (character == null || PlayerData.Instance == null || skins.Count == 0)
            return;

        var progress = profiles.GetValue(PlayerData.Instance, _ => new ProfileProgress());
        if ((progress.Processed.Count == skins.Count && !progress.SavePending) || utcNow() < progress.RetryAt)
            return;

        foreach (RegisteredSkin skin in skins.Registered)
        {
            if (!progress.Processed.Contains(skin.Definition.NumericId) && GrantIfMissing(skin, progress))
                progress.Processed.Add(skin.Definition.NumericId);
        }
        if (progress.SavePending)
        {
            try { PlayerData.SaveData(); progress.SavePending = false; }
            catch (Exception error) { logger.LogError("Could not save skin inventory; will retry: " + error); }
        }
        // A failed grant or save must remain retryable without logging every frame.
        progress.RetryAt = utcNow().AddSeconds(5);
    }

    private bool GrantIfMissing(RegisteredSkin skin, ProfileProgress progress)
    {
        try
        {
            var unlocked = PlayerData.GetUnlockedInstances(skin.Upgrade);
            if (unlocked?.Instances != null && unlocked.Instances.Count > 0)
                return true;

            // CollectInstance can mutate the profile before throwing.
            progress.SavePending = true;
            PlayerData.CollectInstance(character, skin.Upgrade, PlayerData.UnlockFlags.Hidden);
            unlocked = PlayerData.GetUnlockedInstances(skin.Upgrade);
            if (unlocked?.Instances == null || unlocked.Instances.Count == 0)
                throw new InvalidOperationException("The game did not add the skin to the inventory.");
            logger.LogInfo("Added " + skin.Definition.DisplayName + " to Glider's skin inventory.");
            return true;
        }
        catch (Exception error)
        {
            logger.LogError("Could not grant " + skin.Definition.DisplayName + ": " + error);
            return false;
        }
    }
}
