using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MikuGlider;

internal static class SkinFactory
{
    internal static RegisteredSkin Create(SkinVariantDefinition definition, SkinAssets assets, Character character)
    {
        var renderers = GliderRenderers.Read(character);
        Mesh body = assets.GetMesh("body");
        Mesh bodyWithHat = assets.GetMesh("body-hat");
        SkinMeshValidator.ValidateSkinned(body, renderers.Body);
        SkinMeshValidator.ValidateSkinned(bodyWithHat, renderers.Body);
        SkinMeshValidator.ValidateSkinned(assets.GetMesh("arm-left"), renderers.LeftArm);
        SkinMeshValidator.ValidateSkinned(assets.GetMesh("arm-right"), renderers.RightArm);
        if (definition.PoseSkirt)
            SkinMeshValidator.ValidateSkinned(assets.GetMesh("skirt"), renderers.Body, 1);

        SkinUpgrade upgrade = null;
        Sprite icon = null;
        Material utilityMaterial = null;
        Material deathFaceMaterial = null;
        Material deathCoatMaterial = null;
        Material waistJoint = null;
        try
        {
            utilityMaterial = SkinMaterialFactory.Create(assets.GetTexture("body-albedo"), persistent: true);
            deathFaceMaterial = SkinMaterialFactory.Create(assets.GetTexture("face-albedo"), persistent: true);
            waistJoint = SkinMaterialFactory.Create(assets.GetTexture("waist-joint"), persistent: true);
            waistJoint.SetFloat("_AlphaClip", 0f);
            waistJoint.DisableKeyword("_ALPHATEST_ON");
            waistJoint.SetOverrideTag("RenderType", "Opaque");
            waistJoint.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            if (assets.ModelDefinition.coatCullMode != 0)
            {
                deathCoatMaterial = new Material(utilityMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                deathCoatMaterial.SetFloat("_Cull", assets.ModelDefinition.coatCullMode);
            }
            icon = CreateIcon(assets.GetTexture("icon"));
            UpgradeProperty[] properties = CreateProperties(assets, renderers);
            upgrade = CreateUpgrade(definition.Key, definition.NumericId, definition.DisplayName, definition.Rarity, icon, properties);
            return new RegisteredSkin(definition, upgrade, assets, body, bodyWithHat, utilityMaterial, deathFaceMaterial, deathCoatMaterial, waistJoint);
        }
        catch
        {
            Object.Destroy(upgrade);
            Object.Destroy(icon);
            Object.Destroy(utilityMaterial);
            Object.Destroy(deathFaceMaterial);
            Object.Destroy(deathCoatMaterial);
            Object.Destroy(waistJoint);
            throw;
        }
    }

    internal static Sprite CreateIcon(Texture2D texture)
    {
        var icon = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        icon.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return icon;
    }

    internal static SkinUpgrade CreateUpgrade(string key, int id, string displayName, Rarity rarity, Sprite icon, UpgradeProperty[] properties)
    {
        var skin = ScriptableObject.CreateInstance<SkinUpgrade>();
        try
        {
            skin.name = key;
            NativeSkinAccess.SetField(skin, "id", id);
            NativeSkinAccess.SetField(skin, "_name", displayName);
            NativeSkinAccess.SetField(skin, "icon", icon);
            NativeSkinAccess.SetField(skin, "<Rarity>k__BackingField", rarity);
            NativeSkinAccess.SetField(skin, "<Flags>k__BackingField", Upgrade.UpgradeFlags.ExcludeFromWorldPool |
                Upgrade.UpgradeFlags.DisableScrap | Upgrade.UpgradeFlags.NoRandom);
            NativeSkinAccess.SetField(skin, "collectionSource", Upgrade.CollectionSource.None);
            skin.SetProperties(properties);
            return skin;
        }
        catch
        {
            Object.Destroy(skin);
            throw;
        }
    }

    private static UpgradeProperty[] CreateProperties(SkinAssets assets, GliderRenderers renderers)
    {
        SkinModelDefinition definition = assets.ModelDefinition;
        var model = new SkinUpgradeProperty_Glider_CharacterModel();
        NativeSkinAccess.SetField(model, "thirdPersonMesh", assets.GetMesh("body"));
        NativeSkinAccess.SetField(model, "firstPersonArmL", assets.GetMesh("arm-left"));
        NativeSkinAccess.SetField(model, "firstPersonArmR", assets.GetMesh("arm-right"));
        NativeSkinAccess.SetField(model, "torsoData", new DeadPlayerPart.PartSectionData.VariantData
        {
            mesh = SkinMeshValidator.RequireMesh(assets, "death-torso", definition.coatCullMode == 0 ? 3 : 4),
            holdPosition = definition.torsoHoldPosition,
            holdRotation = definition.torsoHoldRotation
        });
        NativeSkinAccess.SetField(model, "legData", new DeadPlayerPart.PartSectionData.VariantData
        {
            mesh = SkinMeshValidator.RequireMesh(assets, "death-legs", 2),
            holdPosition = definition.legsHoldPosition,
            holdRotation = definition.legsHoldRotation
        });
        NativeSkinAccess.SetField(model, "wingBase", SkinMeshValidator.RequireMesh(assets, "wing-base", 1));
        NativeSkinAccess.SetField(model, "wingBlades", SkinMeshValidator.RequireMesh(assets, "wing-blades", 1));
        NativeSkinAccess.SetField(model, "salvoConnection", SkinMeshValidator.RequireMesh(assets, "salvo-connection", 1));
        NativeSkinAccess.SetField(model, "salvoGun", SkinMeshValidator.RequireMesh(assets, "salvo-tip", 1));

        var properties = new List<UpgradeProperty>
        {
            model,
            new SkinTextureProperty(renderers.Body.sharedMaterials[0], assets.GetTexture("face-albedo")),
            new SkinTextureProperty(renderers.Body.sharedMaterials[1], assets.GetTexture("body-albedo"))
        };
        var armMaterials = new HashSet<Material>(renderers.LeftArm.sharedMaterials);
        armMaterials.UnionWith(renderers.RightArm.sharedMaterials);
        foreach (Material material in armMaterials)
            properties.Add(new SkinTextureProperty(material, assets.GetTexture("body-albedo")));

        return properties.ToArray();
    }
}
