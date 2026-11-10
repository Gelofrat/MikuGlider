using System;
using System.Reflection;
using UnityEngine;

namespace MikuGlider;

internal static class NativeWeaponAccess
{
    private static readonly FieldInfo ThirdPersonData = Find("thirdPersonData");
    private static readonly FieldInfo ThirdPersonScale = Find("thirdPersonScaleMultiplier");
    private static readonly FieldInfo Held = Find("isGunHeld");

    internal static bool TryGet(Gun gun, Character character, out Gun.ThirdPersonData data, out Vector3 scale)
    {
        data = default;
        scale = Vector3.one;
        if (gun == null || character == null || gun.GunModel == null || !(bool)Held.GetValue(gun))
            return false;
        var entries = ThirdPersonData.GetValue(gun) as Gun.ThirdPersonData[];
        if (entries == null || character.Index < 0 || character.Index >= entries.Length)
            return false;
        data = entries[character.Index];
        Gun prefab = gun.Prefab as Gun;
        scale = (prefab != null && prefab.GunModel != null ? prefab.GunModel.localScale : gun.GunModel.localScale) *
            (float)ThirdPersonScale.GetValue(gun);
        return true;
    }

    private static FieldInfo Find(string name)
    {
        return typeof(Gun).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(Gun).FullName, name);
    }
}
