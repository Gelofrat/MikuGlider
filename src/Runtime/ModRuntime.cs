using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace MikuGlider;

internal sealed class ModRuntime
{
    internal ManualLogSource Logger { get; }
    internal SkinRegistry Skins { get; }
    internal SkinInventory Inventory { get; }
    internal SkinAppearanceController Appearance { get; }
    internal OrbitCameraController Camera { get; }

    internal ModRuntime(ManualLogSource logger, IReadOnlyList<SkinVariantDefinition> variants, ConfigFile config)
    {
        Logger = logger;
        Skins = new SkinRegistry(variants, logger);
        Inventory = new SkinInventory(Skins, logger);
        Appearance = new SkinAppearanceController(Skins);
        Camera = new OrbitCameraController(Appearance, new HotkeySettings(config));
    }

    internal void RegisterSkins(Character character)
    {
        Inventory.SetCharacter(character);
        Skins.RegisterAll(character);
    }

    internal void Update()
    {
        Inventory.GrantForLoadedProfile();
        Camera.UpdateInput();
    }
}
