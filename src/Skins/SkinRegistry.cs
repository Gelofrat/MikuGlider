using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MikuGlider;

internal sealed class SkinRegistry
{
    private readonly IReadOnlyList<SkinVariantDefinition> definitions;
    private readonly ManualLogSource logger;
    private readonly Dictionary<int, RegisteredSkin> skinsById = new Dictionary<int, RegisteredSkin>();
    private readonly Dictionary<Mesh, RegisteredSkin> skinsByBody = new Dictionary<Mesh, RegisteredSkin>();

    internal int Count => skinsById.Count;
    internal IEnumerable<RegisteredSkin> Registered => skinsById.Values;

    internal SkinRegistry(IReadOnlyList<SkinVariantDefinition> definitions, ManualLogSource logger)
    {
        this.definitions = definitions;
        this.logger = logger;
        ValidateDefinitions();
    }

    internal void RegisterAll(Character character)
    {
        foreach (SkinVariantDefinition definition in definitions)
        {
            if (!skinsById.ContainsKey(definition.NumericId))
                Register(character, definition);
        }
    }

    internal RegisteredSkin FindById(int numericId)
    {
        skinsById.TryGetValue(numericId, out RegisteredSkin skin);
        return skin;
    }

    internal RegisteredSkin FindByBody(Mesh mesh)
    {
        if (mesh == null)
            return null;

        skinsByBody.TryGetValue(mesh, out RegisteredSkin skin);
        return skin;
    }

    private void Register(Character character, SkinVariantDefinition definition)
    {
        SkinAssets assets = null;
        RegisteredSkin candidate = null;
        try
        {
            NativeSkinAccess.ValidateAvailableId(definition.NumericId);
            assets = SkinAssets.Load(definition);
            candidate = SkinFactory.Create(definition, assets, character);
            Object.DontDestroyOnLoad(candidate.Upgrade);
            PlayerData.RegisterUpgrade(candidate.Upgrade, character, MikuGliderPlugin.Guid);
            if (PlayerData.GetUpgradeFromID(definition.UpgradeId) != candidate.Upgrade)
                throw new InvalidOperationException("The game did not register skin " + definition.Key + ".");

            Retain(candidate);
            logger.LogInfo("Registered " + definition.DisplayName + " (" + definition.Rarity + ").");
        }
        catch (Exception error)
        {
            if (candidate != null && PlayerData.GetUpgradeFromID(definition.UpgradeId) == candidate.Upgrade)
                Retain(candidate);
            else if (candidate != null)
                candidate.DisposeUnregistered();
            else
                assets?.Dispose();

            logger.LogError("Could not register " + definition.DisplayName + ": " + error);
        }
    }

    private void Retain(RegisteredSkin skin)
    {
        skinsById[skin.Definition.NumericId] = skin;
        skinsByBody[skin.Body] = skin;
        skinsByBody[skin.BodyWithHat] = skin;
    }

    private void ValidateDefinitions()
    {
        var ids = new HashSet<int>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SkinVariantDefinition definition in definitions)
        {
            if (definition == null || !ids.Add(definition.NumericId) || !keys.Add(definition.Key))
                throw new InvalidOperationException("Skin definitions must have unique numeric IDs and keys.");
        }
    }
}
