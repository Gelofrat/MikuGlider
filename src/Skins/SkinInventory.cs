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
    private SkinUpgrade sharedHat;
    private IUpgradable hatInventory;

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

    internal void SetSharedHat(SkinUpgrade hat, IUpgradable owner)
    {
        sharedHat = hat;
        hatInventory = owner;
    }

    internal void GrantForLoadedProfile()
    {
        int itemCount = skins.Count + (sharedHat != null ? 1 : 0);
        if (PlayerData.Instance == null || itemCount == 0)
            return;

        var progress = profiles.GetValue(PlayerData.Instance, _ => new ProfileProgress());
        if ((progress.Processed.Count == itemCount && !progress.SavePending) || utcNow() < progress.RetryAt)
            return;

        foreach (RegisteredSkin skin in skins.Registered)
        {
            if (character != null)
                GrantIfMissing(skin.Definition.NumericId, skin.Definition.DisplayName, skin.Upgrade, character, progress);
        }
        if (sharedHat != null)
            GrantIfMissing(sharedHat.NumberID, sharedHat.Name, sharedHat, hatInventory, progress);
        if (progress.SavePending)
        {
            try { PlayerData.SaveData(); progress.SavePending = false; }
            catch (Exception error) { logger.LogError("Could not save skin inventory; will retry: " + error); }
        }
        // A failed grant or save must remain retryable without logging every frame.
        progress.RetryAt = utcNow().AddSeconds(5);
    }

    private void GrantIfMissing(int id, string name, SkinUpgrade upgrade, IUpgradable owner, ProfileProgress progress)
    {
        if (progress.Processed.Contains(id)) return;
        try
        {
            var unlocked = PlayerData.GetUnlockedInstances(upgrade);
            if (unlocked?.Instances == null || unlocked.Instances.Count == 0)
            {
                // CollectInstance can mutate the profile before throwing.
                progress.SavePending = true;
                PlayerData.CollectInstance(owner, upgrade, PlayerData.UnlockFlags.Hidden);
                unlocked = PlayerData.GetUnlockedInstances(upgrade);
                if (unlocked?.Instances == null || unlocked.Instances.Count == 0)
                    throw new InvalidOperationException("The game did not add the cosmetic to the inventory.");
                logger.LogInfo("Added " + name + " to the cosmetic inventory.");
            }
            progress.Processed.Add(id);
        }
        catch (Exception error)
        {
            logger.LogError("Could not grant " + name + ": " + error);
        }
    }
}
