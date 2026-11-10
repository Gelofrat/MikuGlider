using System;
using System.Collections.Generic;
using System.Reflection;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal static class NativeSkinAccess
{
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly FieldInfo UpgradeIds = FindField(typeof(PlayerData), "upgradeIDs");
    private static readonly PropertyInfo ActiveSkins = FindProperty(typeof(Player), "ActiveSkins");
    private static readonly FieldInfo BodyOverride = FindField(typeof(PlayerAnimation), "overrideThirdPersonMesh");
    private static readonly FieldInfo SelfRig = FindField(typeof(ThirdPersonAnimator), "thirdPersonRig");
    private static readonly FieldInfo WingsuitOwner = FindField(typeof(Wingsuit), "player");
    private static readonly FieldInfo FlightAnimation = FindField(typeof(Wingsuit), "thirdPersonFlyAnimation");

    internal static void ValidateLayout()
    {
        if (UpgradeIds == null || ActiveSkins == null || BodyOverride == null || SelfRig == null ||
            WingsuitOwner == null || FlightAnimation == null)
            throw new MissingMemberException("The native Glider skin or animation layout changed.");
    }

    internal static void ValidateAvailableId(int numericId)
    {
        ValidateLayout();
        if (!(UpgradeIds.GetValue(null) is IDictionary<UpgradeID, Upgrade> upgrades))
            throw new MissingFieldException("Cannot inspect native upgrade IDs.");

        foreach (UpgradeID id in upgrades.Keys)
        {
            if (id.ID == numericId)
                throw new InvalidOperationException("Numeric skin ID is already registered: " + numericId);
        }
    }

    internal static List<SkinInstance> GetActiveSkins(Player player)
    {
        return ActiveSkins.GetValue(player, null) as List<SkinInstance>;
    }

    internal static void SetBodyOverride(PlayerAnimation animator, Mesh mesh)
    {
        BodyOverride.SetValue(animator, mesh);
    }

    internal static ThirdPersonRig GetSelfRig(ThirdPersonAnimator animator)
    {
        var rigObject = SelfRig.GetValue(animator) as GameObject;
        return rigObject != null ? rigObject.GetComponent<ThirdPersonRig>() : null;
    }

    internal static Player GetWingsuitOwner(Wingsuit wingsuit)
    {
        return WingsuitOwner.GetValue(wingsuit) as Player;
    }

    internal static PlayerAnimation.Key GetFlightAnimation(Wingsuit wingsuit)
    {
        return FlightAnimation.GetValue(wingsuit) as PlayerAnimation.Key;
    }

    internal static void SetField(object target, string name, object value)
    {
        FieldInfo field = FindField(target.GetType(), name);
        if (field == null)
            throw new MissingFieldException(target.GetType().FullName, name);

        field.SetValue(target, value);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(name, MemberFlags);
            if (field != null)
                return field;

            type = type.BaseType;
        }

        return null;
    }

    private static PropertyInfo FindProperty(Type type, string name)
    {
        while (type != null)
        {
            PropertyInfo property = type.GetProperty(name, MemberFlags);
            if (property != null)
                return property;

            type = type.BaseType;
        }

        return null;
    }
}
