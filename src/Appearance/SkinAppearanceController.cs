using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class SkinAppearanceController
{
    private readonly SkinRegistry skins;
    private readonly ConditionalWeakTable<MenuRig, PreviewBody> previewBodies = new ConditionalWeakTable<MenuRig, PreviewBody>();

    internal SkinAppearanceController(SkinRegistry skins)
    {
        this.skins = skins;
    }

    internal void RefreshPlayer(Player player)
    {
        if (player == null || player.Animator == null)
            return;

        RegisteredSkin skin = skins.FindByBody(player.Animator.OverrideThirdPersonMesh);
        RefreshFirstPersonWrists(player, skin);
        if (skin == null)
            return;

        var activeSkins = NativeSkinAccess.GetActiveSkins(player);
        if (activeSkins == null)
            return;

        bool hasSkin = false;
        bool hasHat = false;
        foreach (SkinInstance instance in activeSkins)
        {
            Upgrade upgrade = instance.Upgrade;
            if (upgrade == skin.Upgrade)
                hasSkin = true;
            if (upgrade is SkinUpgrade && SkinUpgrade.GetHat(player.Character, player, upgrade, out var hat) &&
                hat.GetInstance(player, out _, out _) != null)
                hasHat = true;
        }

        if (!hasSkin)
            return;

        Mesh body = skin.SelectBody(hasHat);
        NativeSkinAccess.SetBodyOverride(player.Animator, body);
        UpdateRigBody(player.Animator.ThirdPersonRig, body);
        EnsureRigVisuals(player.Animator.ThirdPersonRig, player);

        ThirdPersonAnimator self = player.Animator.SelfThirdPersonAnimator;
        if (self != null && self.CurrentCharacter == player.Character)
        {
            UpdateRigBody(self.Rig, body);
            EnsureRigVisuals(self.Rig, player);
        }
    }

    private static void RefreshFirstPersonWrists(Player player, RegisteredSkin skin)
    {
        if (!player.IsLocalPlayer) return;
        Transform root = player.Animator.FirstPersonRoot;
        if (root == null || root.childCount < 3) return;
        bool correct = skin != null && (skin.Definition.Key == "MikuGliderV2" || skin.Definition.Key == "MikuGliderV3");
        FirstPersonWristVisual.Attach(root.GetChild(1).GetComponent<SkinnedMeshRenderer>(),
            correct ? skin.Assets.GetMesh("arm-left") : null);
        FirstPersonWristVisual.Attach(root.GetChild(2).GetComponent<SkinnedMeshRenderer>(),
            correct ? skin.Assets.GetMesh("arm-right") : null);
    }

    internal void RefreshUtilities(Player player)
    {
        if (player == null || !player.IsLocalPlayer)
            return;

        Wingsuit wingsuit = GliderEquipment.FindWingsuit(player);
        if (wingsuit != null && wingsuit.Prefab != null && NativeSkinAccess.GetWingsuitOwner(wingsuit) == player)
            wingsuit.OnSkinsApplied();
    }

    internal void ApplyUtilityMaterials(Player player, Transform left, Transform right, Transform salvo)
    {
        if (player == null || player.Animator == null)
            return;

        RegisteredSkin skin = skins.FindByBody(player.Animator.OverrideThirdPersonMesh);
        if (skin == null)
            return;

        SetUtilityMaterial(left, skin.UtilityMaterial);
        SetUtilityMaterial(right, skin.UtilityMaterial);
        SetUtilityMaterial(salvo, skin.UtilityMaterial);
    }

    internal void RefreshPreview(MenuRig menu, Character character, Transform head)
    {
        if (menu == null || character == null)
            return;

        SkinnedMeshRenderer body = FindPreviewBody(menu);
        if (body == null)
            return;

        RegisteredSkin skin = skins.FindByBody(body.sharedMesh);
        bool hasHat = head != null && HasPreviewHat(menu, character);
        Mesh selected = skin.SelectBody(hasHat);
        if (body.sharedMesh != selected)
            body.sharedMesh = selected;
    }

    internal void PreparePreviewMaterials(Mesh mesh, List<Material> materials)
    {
        RegisteredSkin skin = skins.FindByBody(mesh);
        if (skin != null)
            SkinMaterialOrder.Normalize(materials, skin.Assets.GetTexture("face-albedo"),
                skin.Assets.GetTexture("body-albedo"));
    }

    internal void ResetPreview(MenuRig menu)
    {
        if (menu == null)
            return;

        SkinnedMeshRenderer body = FindPreviewBody(menu);
        if (body != null)
        {
            Mesh selected = skins.FindByBody(body.sharedMesh).Body;
            if (body.sharedMesh != selected)
                body.sharedMesh = selected;
        }
    }

    internal void EnsureRigVisuals(ThirdPersonRig rig, Player player)
    {
        if (rig == null || rig.SkinRenderers == null || rig.SkinRenderers.Length == 0 ||
            !(rig.SkinRenderers[0] is SkinnedMeshRenderer body))
            return;

        RegisteredSkin skin = skins.FindByBody(body.sharedMesh);
        if (rig.HeadBone != null && skin?.Assets.Hair != null)
            AnimatedHairVisual.Attach(rig.gameObject, body, rig.HeadBone, rig.MainRendererIndex, skin, player);
        if (skin?.Definition.PoseSkirt == true)
            SkirtGarmentVisual.Attach(rig.gameObject, body, rig.MainRendererIndex, skin, player);
    }

    internal void EnsurePreviewVisuals(MenuRig menu, Transform head)
    {
        if (menu == null)
            return;

        SkinnedMeshRenderer body = FindPreviewBody(menu);
        if (body == null)
            return;

        RegisteredSkin skin = skins.FindByBody(body.sharedMesh);
        if (head != null && skin?.Assets.Hair != null)
            AnimatedHairVisual.Attach(menu.gameObject, body, head, 1, skin, null);
        if (skin?.Definition.PoseSkirt == true)
            SkirtGarmentVisual.Attach(menu.gameObject, body, 1, skin, null);
    }

    private void UpdateRigBody(ThirdPersonRig rig, Mesh body)
    {
        if (rig == null || rig.SkinRenderers == null || rig.SkinRenderers.Length == 0)
            return;

        if (rig.SkinRenderers[0] is SkinnedMeshRenderer renderer && renderer.sharedMesh != body && skins.FindByBody(renderer.sharedMesh) != null)
            renderer.sharedMesh = body;
    }

    private SkinnedMeshRenderer FindPreviewBody(MenuRig menu)
    {
        PreviewBody cached = previewBodies.GetValue(menu, CreatePreviewBody);
        if (cached.Renderer != null && cached.Renderer.transform.IsChildOf(menu.transform))
            return skins.FindByBody(cached.Renderer.sharedMesh) != null ? cached.Renderer : null;
        foreach (SkinnedMeshRenderer renderer in menu.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skins.FindByBody(renderer.sharedMesh) != null)
            {
                cached.Renderer = renderer;
                return renderer;
            }
        }

        return null;
    }

    private static PreviewBody CreatePreviewBody(MenuRig menu) => new PreviewBody();

    private sealed class PreviewBody
    {
        internal SkinnedMeshRenderer Renderer;
    }

    private static bool HasPreviewHat(MenuRig menu, Character character)
    {
        if (menu.OverrideHatThisFrame != null)
            return HasHatProperty(menu.OverrideHatThisFrame);

        var equipped = new PlayerData.EquippedSkinEnumerator(character);
        while (equipped.MoveNext())
        {
            if (HasHatProperty(equipped.Upgrade))
                return true;
        }

        return false;
    }

    private static bool HasHatProperty(UpgradeInstance instance)
    {
        return instance != null && instance.Upgrade is SkinUpgrade skin &&
            skin.FindProperty<SkinUpgradeProperty_Hat>() != null;
    }

    private static void SetUtilityMaterial(Transform root, Material material)
    {
        if (root == null)
            return;

        SetRendererMaterial(root, material);
        if (root.childCount > 0)
            SetRendererMaterial(root.GetChild(0), material);
    }

    private static void SetRendererMaterial(Transform transform, Material material)
    {
        var renderer = transform.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
    }
}
