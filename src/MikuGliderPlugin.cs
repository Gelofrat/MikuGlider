using BepInEx;
using System;
using HarmonyLib;

namespace MikuGlider;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[MycoMod(null, ModFlags.IsClientSide)]
public sealed class MikuGliderPlugin : BaseUnityPlugin
{
    public const string Guid = MyPluginInfo.PLUGIN_GUID;
    public const string Version = MyPluginInfo.PLUGIN_VERSION;

    internal static ModRuntime Runtime { get; private set; }
    internal static PorcelainMask Mask { get; private set; }

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
        if (Mask == null)
        {
            try
            {
                Mask = PorcelainMask.Register(Logger);
                Runtime.Inventory.SetSharedHat(Mask.Upgrade, Global.Instance);
            }
            catch (Exception error) { Logger.LogError("Could not register Porcelain Mask: " + error); }
        }
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
        if (Mask == null && (Runtime == null || Runtime.Skins.Count == 0))
        {
            harmony?.UnpatchSelf();
            Runtime = null;
        }
    }
}
