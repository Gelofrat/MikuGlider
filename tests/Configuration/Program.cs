using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine.InputSystem;
using MikuGlider;

internal static class Program
{
    private static void Check(bool value) { if (!value) throw new Exception("Configuration check failed."); }
    private static ConfigFile Open(string path)
    {
        var file = new ConfigFile(path, false);
        new HotkeySettings(file);
        return file;
    }
    private static int Main(string[] args)
    {
        string path = Path.GetFullPath(args[0]);
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException();
        var config = Open(path);
        if (args.Length > 1 && args[1] == "--create") { config.Save(); Console.WriteLine("CONFIG_CREATED"); return 0; }
        Check((Key)config["Hotkeys", "CyclePerspective"].BoxedValue == Key.J);
        Check((Key)config["Hotkeys", "ToggleWeapon"].BoxedValue == Key.K);
        config["Hotkeys", "CyclePerspective"].BoxedValue = Key.F5;
        config["Hotkeys", "ToggleWeapon"].BoxedValue = Key.None;
        config.Save();
        var loaded = Open(path);
        Check((Key)loaded["Hotkeys", "CyclePerspective"].BoxedValue == Key.F5);
        Check((Key)loaded["Hotkeys", "ToggleWeapon"].BoxedValue == Key.None);
        loaded["Hotkeys", "CyclePerspective"].BoxedValue = Key.J;
        loaded["Hotkeys", "ToggleWeapon"].BoxedValue = Key.K;
        loaded.Save();
        Console.WriteLine("CONFIGURATION_PASS actual BepInEx save/load and custom keys");
        return 0;
    }
}
