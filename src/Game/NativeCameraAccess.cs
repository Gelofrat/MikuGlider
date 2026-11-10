using System;
using System.Reflection;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal static class NativeCameraAccess
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly FieldInfo GlobalRotation = FindField("globalRotation");
    private static readonly FieldInfo AddedPosition = FindField("addPosition");
    private static readonly FieldInfo OrbitDistance = FindField("orbitDistance");
    private static readonly FieldInfo MinimumDistance = FindField("minOrbitDistance");
    private static readonly FieldInfo MaximumDistance = FindField("maxOrbitDistance");
    private static readonly FieldInfo MinimumHeight = FindField("minYPosition");
    private static readonly FieldInfo ObstructionMask = FindField("thirdPersonObstructionMask");
    private static readonly FieldInfo Aiming = FindField("isAiming");

    internal static void ValidateLayout()
    {
        if (GlobalRotation == null || AddedPosition == null || OrbitDistance == null || MinimumDistance == null ||
            MaximumDistance == null || MinimumHeight == null || ObstructionMask == null || Aiming == null)
            throw new MissingMemberException("The native third-person camera layout changed.");
    }

    internal static Quaternion GetGlobalRotation(PlayerLook look)
    {
        return (Quaternion)GlobalRotation.GetValue(look);
    }

    internal static void SetGlobalRotation(PlayerLook look, Quaternion rotation)
    {
        GlobalRotation.SetValue(look, rotation);
    }

    internal static Vector2 GetDistanceBounds(PlayerLook look)
    {
        return new Vector2((float)MinimumDistance.GetValue(look), (float)MaximumDistance.GetValue(look));
    }

    internal static float GetOrbitDistance(PlayerLook look)
    {
        return (float)OrbitDistance.GetValue(look);
    }

    internal static void SetOrbitDistance(PlayerLook look, float distance)
    {
        OrbitDistance.SetValue(look, distance);
    }

    internal static float ReadHorizontalInput(PlayerLook look)
    {
        if (look.RotationLocksY > 0)
            return 0f;

        Vector2 input = look.GetMouseDelta();
        float sensitivity = (bool)Aiming.GetValue(look) ? look.AimSensitivityMultiplier : 1f;
        return input.x * sensitivity;
    }

    internal static Vector3 GetCenterPivot(PlayerLook look)
    {
        Vector3 pivot = look.BasePosition + (Vector3)AddedPosition.GetValue(look);
        pivot.y = Mathf.Max(pivot.y, (float)MinimumHeight.GetValue(look));
        pivot.y -= 0.5f;
        return look.transform.parent.TransformPoint(pivot);
    }

    internal static int GetObstructionMask()
    {
        return (int)ObstructionMask.GetValue(null);
    }

    private static FieldInfo FindField(string name)
    {
        return typeof(PlayerLook).GetField(name, Flags);
    }
}
