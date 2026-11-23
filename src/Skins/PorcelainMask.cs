using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using Pigeon.Movement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MikuGlider;

internal sealed class PorcelainMask
{
    internal const int NumericId = 0x4D494D41;
    internal SkinUpgrade Upgrade { get; private set; }
    internal SkinUpgradeProperty_Hat Hat { get; private set; }
    private readonly Dictionary<string, Mesh> fits = new Dictionary<string, Mesh>();
    private readonly ConditionalWeakTable<MenuRig, PreviewHat> previews = new ConditionalWeakTable<MenuRig, PreviewHat>();

    internal static PorcelainMask Register(ManualLogSource logger)
    {
        NativeSkinAccess.ValidateAvailableId(NumericId);
        var mask = new PorcelainMask();
        var owned = new List<Object>();
        var id = new UpgradeID(NumericId, MikuGliderPlugin.Guid);
        try
        {
            var resources = new EmbeddedResources("MikuGlider.PorcelainMask");
            using (var stream = resources.Open("Model", 4 * 1024 * 1024))
                mask.LoadFits(stream, owned);

            // Keep an active template under an inactive parent. The game's pool clones the
            // template alone, so newly equipped masks are visible to observers immediately.
            var holder = new GameObject("Porcelain mask templates");
            owned.Add(holder);
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var template = new GameObject("Porcelain Mask");
            template.transform.SetParent(holder.transform, false);
            var surface = new GameObject("Shell");
            surface.transform.SetParent(template.transform, false);
            var rig = Global.Instance.GliderCharacter.ThirdPersonModel.GetComponent<ThirdPersonRig>();
            surface.layer = template.layer = rig.SkinRenderers[0].gameObject.layer;
            long pixels = 0;
            var albedo = TextureResourceReader.Read(resources, "porcelain-albedo", "Albedo", false, ref pixels);
            owned.Add(albedo);
            var porcelain = SkinMaterialFactory.Create(albedo, persistent: true);
            owned.Add(porcelain);
            var materials = new List<Material> { porcelain };
            var fallback = mask.fits["Glider"];
            surface.AddComponent<MeshFilter>().sharedMesh = fallback;
            surface.AddComponent<MeshRenderer>().SetSharedMaterials(materials);
            mask.Hat = new EquippedHat(mask) { hatPrefab = template, mesh = fallback, defaultMaterials = materials };
            var texture = TextureResourceReader.Read(resources, "icon", "Icon", false, ref pixels);
            owned.Add(texture);
            var icon = SkinFactory.CreateIcon(texture);
            owned.Add(icon);
            mask.Upgrade = SkinFactory.CreateUpgrade("MikuPorcelainMask", NumericId, "Porcelain Mask", Rarity.Exotic,
                icon, new UpgradeProperty[] { mask.Hat });
            owned.Add(mask.Upgrade);
            NativeSkinAccess.SetField(mask.Upgrade, "skinType", SkinType.Crab);
            Object.DontDestroyOnLoad(mask.Upgrade);
            PlayerData.RegisterUpgrade(mask.Upgrade, Global.Instance, MikuGliderPlugin.Guid);
            if (PlayerData.GetUpgradeFromID(id) != mask.Upgrade)
                throw new InvalidOperationException("The shared hat was not registered.");
            return mask;
        }
        catch (Exception error)
        {
            // A callback can fail after the native item table has already retained the item.
            if (mask.Upgrade != null && PlayerData.GetUpgradeFromID(id) == mask.Upgrade)
            {
                if (!Global.Instance.Info.Upgrades.Contains(mask.Upgrade))
                    Global.Instance.Info.Upgrades.Add(mask.Upgrade);
                logger.LogWarning("Porcelain Mask was registered before the callback failed: " + error.Message);
                return mask;
            }
            foreach (var item in owned) Object.Destroy(item);
            throw;
        }
    }

    private void LoadFits(Stream stream, List<Object> owned)
    {
        foreach (var data in MeshResourceReader.Read(stream))
        {
            if (data.BindPoses.Length != 0 || data.Submeshes.Length != 1)
                throw new InvalidDataException("Invalid mask geometry: " + data.Name);
            var mesh = new Mesh { name = "Porcelain Mask / " + data.Name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            owned.Add(mesh);
            mesh.vertices = data.Vertices;
            mesh.normals = data.Normals;
            mesh.uv = data.UV;
            MeshIndices.Assign(mesh, data.Submeshes);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            fits.Add(data.Name, mesh);
        }
        foreach (string key in new[] { "Glider", "Wrangler", "Bruiser", "Scrapper", "Mycologist" })
            if (!fits.ContainsKey(key)) throw new InvalidDataException("Missing mask fit: " + key);
        foreach (var skin in SkinCatalog.Variants)
            if (!fits.ContainsKey(skin.Key)) throw new InvalidDataException("Missing mask fit: " + skin.Key);
    }

    private Mesh SelectFit(Character character, RegisteredSkin skin)
    {
        if (skin != null && fits.TryGetValue(skin.Definition.Key, out Mesh fit)) return fit;
        var game = Global.Instance;
        if (character == game.BruiserCharacter) return fits["Bruiser"];
        if (character == game.ScrapperCharacter) return fits["Scrapper"];
        if (character == game.WranglerCharacter) return fits["Wrangler"];
        if (character == game.MycologistCharacter) return fits["Mycologist"];
        return fits["Glider"];
    }

    internal void RefreshPlayer(Player player)
    {
        if (player == null) return;
        var instance = Hat.GetInstance(player, out _, out _);
        if (instance != null) FitInstance(instance, player);
    }

    private void FitInstance(GameObject instance, Player player)
    {
        var skin = MikuGliderPlugin.Runtime.Skins.FindByBody(player.Animator?.OverrideThirdPersonMesh);
        var filter = instance.transform.GetChild(0).GetComponent<MeshFilter>();
        filter.sharedMesh = SelectFit(player.Character, skin);
    }

    internal SkinUpgradeProperty_Hat Preview(MenuRig menu, Character character, Transform socket)
    {
        if (!previews.TryGetValue(menu, out var preview))
        {
            preview = new PreviewHat { hatPrefab = Hat.hatPrefab, defaultMaterials = Hat.defaultMaterials };
            previews.Add(menu, preview);
        }
        var skin = character == Global.Instance.GliderCharacter ? MikuGliderPlugin.Runtime.Appearance.GetPreviewSkin(menu) : null;
        preview.mesh = SelectFit(character, skin);
        preview.SetSocket(FaceSocket(socket) ?? socket, character);
        return preview;
    }

    internal void FinishPreview(MenuRig menu)
    {
        if (previews.TryGetValue(menu, out var preview)) preview.Surface.EndFrame();
    }

    internal void HidePreview(MenuRig menu)
    {
        if (previews.TryGetValue(menu, out var preview)) preview.Surface.Hide();
    }

    // The native Mycologist hat socket is the neck. A face covering must follow the head above it.
    internal static Transform FaceSocket(Transform socket) =>
        socket != null && socket.name == "joint_Neck" ? socket.Find("joint_Head") : null;

    private sealed class EquippedHat : SkinUpgradeProperty_Hat
    {
        private readonly PorcelainMask mask;
        internal EquippedHat(PorcelainMask mask) { this.mask = mask; }

        protected override void AddInstance(Player player, GameObject instance)
        {
            base.AddInstance(player, instance);
            mask.FitInstance(instance, player);
        }
    }

    private sealed class PreviewHat : SkinUpgradeProperty_Hat
    {
        internal readonly MaskPreviewSurface Surface = new MaskPreviewSurface();
        private Character character;
        private ThirdPersonRig rig;
        private Transform socket;

        internal void SetSocket(Transform current, Character selected)
        {
            socket = current;
            if (character == selected && rig != null) return;
            character = selected;
            rig = selected.ThirdPersonModel.GetComponent<ThirdPersonRig>();
        }

        protected override void RenderPreview(Matrix4x4 localToWorld, RenderParams renderParams, List<Material> materials, float scaleMultiplier)
        {
            if (socket == null || rig == null) return;
            var scale = rig.HatScaleOverride > 0f ? Vector3.one * rig.HatScaleOverride : hatPrefab.transform.localScale;
            Surface.Show(socket, mesh, defaultMaterials[0], rig.HatOffset, Quaternion.Euler(rig.HatRotation),
                scale * scaleMultiplier, renderParams.layer, renderParams.renderingLayerMask);
        }
    }
}
