using BepInEx.Configuration;
using System;
using UnityEngine.InputSystem;

namespace MikuGlider;

internal sealed class HotkeySettings
{
    private readonly ConfigEntry<Key> perspective;
    private readonly ConfigEntry<Key> weapon;

    internal HotkeySettings(ConfigFile config)
    {
        perspective = config.Bind("Hotkeys", "CyclePerspective", Key.J,
            "Cycle first person, free orbit and shoulder camera. Set None to disable.");
        weapon = config.Bind("Hotkeys", "ToggleWeapon", Key.K,
            "Show or hide the local weapon. Set None to disable. Use a different key from CyclePerspective.");
    }

    internal bool PerspectivePressed => Pressed(perspective.Value);
    internal bool WeaponPressed => weapon.Value != perspective.Value && Pressed(weapon.Value);

    private static bool Pressed(Key key)
    {
        return key != Key.None && Enum.IsDefined(typeof(Key), key) && Keyboard.current != null &&
            Keyboard.current[key].wasPressedThisFrame;
    }
}
