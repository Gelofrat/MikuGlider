using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal static class ShoulderCamera
{
    internal static Vector3 ConstrainPosition(PlayerLook look, Vector3 desired)
    {
        Vector3 origin = NativeCameraAccess.GetCenterPivot(look);
        Vector3 offset = desired - origin;
        float distance = offset.magnitude;
        if (distance < 0.01f)
            return desired;

        if (Physics.SphereCast(origin, 0.14f, offset / distance, out RaycastHit hit, distance,
            NativeCameraAccess.GetObstructionMask(), QueryTriggerInteraction.Ignore))
            return origin + offset / distance * Mathf.Max(0f, hit.distance - 0.04f);

        return desired;
    }
}
