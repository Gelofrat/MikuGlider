using BepInEx;
using System;
using HarmonyLib;

namespace MikuGlider;

[BepInPlugin(Guid, "Miku Glider", Version)]
[MycoMod(null, (ModFlags)0)]
public sealed class MikuGliderPlugin : BaseUnityPlugin
{
    public const string Guid = "horse.mycopunk.mikuglider";
    public const string Version = BuildInfo.Version;

    internal static ModRuntime Runtime { get; private set; }

    private Harmony harmony;
    private bool active;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try
        {
            Runtime ??= new ModRuntime(Logger, SkinCatalog.Variants, Config);
            harmony.PatchAll(typeof(MikuGliderPlugin).Assembly);
            active = true;
            Runtime.StartSession();
            PlayerData.AddRegisterUpgradesCallback(RegisterSkins);
        }
        catch (Exception error)
        {
            Deactivate();
            Logger.LogError("Miku Glider initialization failed: " + error);
        }
    }

    private void RegisterSkins()
    {
        if (!active)
            return;

        Runtime.RegisterSkins(Global.Instance.GliderCharacter);
    }

    private void Update()
    {
        if (active)
            Runtime.Update();
    }

    private void OnDestroy()
    {
        Deactivate();
    }

    private void OnEnable()
    {
        if (active) Runtime?.StartSession();
    }

    private void OnDisable() => Runtime?.StopSession();

    private void Deactivate()
    {
        active = false;
        enabled = false;
        Runtime?.StopSession();
        if (Runtime == null || Runtime.Skins.Count == 0)
        {
            harmony?.UnpatchSelf();
            Runtime = null;
        }
    }
}
