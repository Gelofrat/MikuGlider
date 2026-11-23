using UnityEngine;

namespace MikuGlider;

internal sealed class RegisteredSkin
{
    internal SkinVariantDefinition Definition { get; }
    internal SkinUpgrade Upgrade { get; }
    internal SkinAssets Assets { get; }
    internal Mesh Body { get; }
    internal Mesh BodyWithHat { get; }
    internal Material UtilityMaterial { get; }
    internal Material[] DeathTorsoMaterials { get; }
    internal Material[] DeathLegMaterials { get; }

    internal RegisteredSkin(SkinVariantDefinition definition, SkinUpgrade upgrade, SkinAssets assets,
        Mesh body, Mesh bodyWithHat, Material utilityMaterial, Material deathFaceMaterial, Material deathCoatMaterial, Material waistJoint)
    {
        Definition = definition;
        Upgrade = upgrade;
        Assets = assets;
        Body = body;
        BodyWithHat = bodyWithHat;
        UtilityMaterial = utilityMaterial;
        DeathTorsoMaterials = deathCoatMaterial == null ? new[] { utilityMaterial, deathFaceMaterial, waistJoint }
            : new[] { utilityMaterial, deathFaceMaterial, deathCoatMaterial, waistJoint };
        DeathLegMaterials = new[] { utilityMaterial, waistJoint };
    }

    internal bool OwnsBody(Mesh mesh)
    {
        return mesh != null && (mesh == Body || mesh == BodyWithHat);
    }

    internal Mesh SelectBody(bool hasHat)
    {
        return hasHat ? BodyWithHat : Body;
    }

    internal void DisposeUnregistered()
    {
        if (Upgrade != null)
        {
            Object.Destroy(Upgrade.Icon);
            Object.Destroy(Upgrade);
        }

        Object.Destroy(UtilityMaterial);
        for (int slot = 1; slot < DeathTorsoMaterials.Length; slot++)
            Object.Destroy(DeathTorsoMaterials[slot]);
        Assets.Dispose();
    }
}
