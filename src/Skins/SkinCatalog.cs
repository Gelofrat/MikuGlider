using System;
using System.Collections.Generic;

namespace MikuGlider;

internal static class SkinCatalog
{
    internal static IReadOnlyList<SkinVariantDefinition> Variants { get; } = Array.AsReadOnly(new[]
    {
        new SkinVariantDefinition(
            numericId: 0x4D494B55,
            key: "MikuGlider",
            displayName: "Hatsune Miku v1",
            rarity: Rarity.Exotic,
            resourcePrefix: "MikuGlider",
            poseSkirt: true),
        new SkinVariantDefinition(
            numericId: 0x4D494B56,
            key: "MikuGliderV2",
            displayName: "Hatsune Miku v2",
            rarity: Rarity.Exotic,
            resourcePrefix: "MikuGliderV2",
            poseSkirt: true),
        new SkinVariantDefinition(
            numericId: 0x4D494B57,
            key: "MikuGliderV3",
            displayName: "Hatsune Miku v3",
            rarity: Rarity.Exotic,
            resourcePrefix: "MikuGliderV3",
            poseSkirt: true)
    });
}

internal sealed class SkinVariantDefinition
{
    internal int NumericId { get; }
    internal string Key { get; }
    internal string DisplayName { get; }
    internal Rarity Rarity { get; }
    internal string ResourcePrefix { get; }
    internal bool AnimatedHair { get; }
    internal bool PoseSkirt { get; }
    internal UpgradeID UpgradeId => new UpgradeID(NumericId, MikuGliderPlugin.Guid);

    internal SkinVariantDefinition(int numericId, string key, string displayName, Rarity rarity,
        string resourcePrefix, bool animatedHair = true, bool poseSkirt = false)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A skin key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A skin display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(resourcePrefix))
            throw new ArgumentException("An embedded resource prefix is required.", nameof(resourcePrefix));

        NumericId = numericId;
        Key = key;
        DisplayName = displayName;
        Rarity = rarity;
        ResourcePrefix = resourcePrefix;
        AnimatedHair = animatedHair;
        PoseSkirt = poseSkirt;
    }
}
