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
    internal DalekMissionAudio MissionAudio { get; }
    internal bool SessionEnabled { get; private set; }

    internal ModRuntime(ManualLogSource logger, IReadOnlyList<SkinVariantDefinition> variants, ConfigFile config)
    {
        Logger = logger;
        Skins = new SkinRegistry(variants, logger);
        Inventory = new SkinInventory(Skins, logger);
        Appearance = new SkinAppearanceController(Skins);
        Camera = new OrbitCameraController(Appearance, new HotkeySettings(config));
        MissionAudio = new DalekMissionAudio(new AudioSettings(config), logger);
        MissionAudio.SetActive(false);
    }

    internal void RegisterSkins(Character character)
    {
        Inventory.SetCharacter(character);
        Skins.RegisterAll(character);
    }

    internal void Update()
    {
        if (!SessionEnabled) return;
        Inventory.GrantForLoadedProfile();
        Camera.UpdateInput();
    }

    internal void StartSession()
    {
        SessionEnabled = true;
        MissionAudio.SetActive(true);
    }

    internal void StopSession()
    {
        SessionEnabled = false;
        // Stop audio even if native camera cleanup fails.
        try { Camera.Stop(); }
        finally { MissionAudio.SetActive(false); }
    }
}
