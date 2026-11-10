using System;
using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;
using Random = Pigeon.Math.Random;

namespace MikuGlider;

internal sealed class SkinTextureProperty : SkinUpgradeProperty, ISkinMaterialProperty
{
    private readonly Material original;
    private readonly Texture2D texture;
    private readonly Dictionary<object, Material> instances = new Dictionary<object, Material>();

    public override float Chance => 1f;
    public int MaterialCount => 1;

    internal SkinTextureProperty(Material original, Texture2D texture)
    {
        this.original = original != null ? original : throw new ArgumentNullException(nameof(original));
        this.texture = texture;
    }

    public Material GetMaterialAt(int index)
    {
        return original;
    }

    public override float GetPropertyChance(Random random, IUpgradable gear)
    {
        return 1f;
    }

    public override void Apply(IUpgradable gear, Upgrade upgrade, int seed, ref Random random,
        Player owner, ref bool modifiedSkin, List<SkinOverrideMaterial> overrideMaterials)
    {
        if (overrideMaterials != null)
        {
            Material preview = SkinUpgrade.GetMaterial(SkinMaterialFactory.Shader);
            SkinMaterialFactory.Apply(preview, texture);
            overrideMaterials.Add(new SkinOverrideMaterial(preview, original));
            return;
        }

        object key = GetOwnerKey(gear, owner);
        RemoveInstance(gear, gear.GetPrefab(), owner, key);
        Material material = SkinMaterialFactory.Create(texture);
        instances.Add(key, material);
        gear.SetMaterialSkin(gear.GetPrefab(), original, material, owner);
    }

    public override void Remove(IUpgradable gear, IUpgradable prefab, Upgrade upgrade, int seed,
        ref Random random, Player owner)
    {
        RemoveInstance(gear, prefab, owner, GetOwnerKey(gear, owner));
    }

    public override void AddModifiableMaterials(IUpgradable gear, Player owner, List<Material> materials)
    {
        if (instances.TryGetValue(GetOwnerKey(gear, owner), out Material material))
            materials.Add(material);
    }

    public void AddInstancesToList(IUpgradable gear, Player player, List<Material> materialBuffer,
        Range<int>? materialRange, ref int materialIndex)
    {
        if (!materialRange.HasValue ||
            (materialIndex >= materialRange.Value.min && materialIndex <= materialRange.Value.max))
            AddModifiableMaterials(gear, player, materialBuffer);

        materialIndex++;
    }

    private void RemoveInstance(IUpgradable gear, IUpgradable prefab, Player owner, object key)
    {
        if (!instances.TryGetValue(key, out Material material))
            return;

        gear.RemoveMaterialSkin(prefab, material, owner);
        UnityEngine.Object.Destroy(material);
        instances.Remove(key);
    }

    private static object GetOwnerKey(IUpgradable gear, Player owner)
    {
        return gear is Character ? (object)owner : gear;
    }
}
