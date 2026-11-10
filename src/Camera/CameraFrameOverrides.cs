using System;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class CameraFrameOverrides : IDisposable
{
    internal PlayerLook Look { get; }
    private Quaternion previousRotation;
    private Vector2 previousBounds;
    private Vector3 positionOffset;
    private bool changedRotation;
    private bool changedBounds;
    private bool lockedYaw;
    private bool disposed;

    internal CameraFrameOverrides(PlayerLook look)
    {
        Look = look;
    }

    internal void SetFreeOrbit(float worldYaw)
    {
        previousRotation = NativeCameraAccess.GetGlobalRotation(Look);
        changedRotation = true;
        float parentYaw = Look.transform.parent.eulerAngles.y;
        Quaternion relativeYaw = Quaternion.Euler(0f, Mathf.DeltaAngle(parentYaw, worldYaw), 0f);
        NativeCameraAccess.SetGlobalRotation(Look, relativeYaw * previousRotation);
        Look.RotationLocksY++;
        lockedYaw = true;
    }

    internal void SetShoulder(Vector3 offset)
    {
        previousBounds = NativeCameraAccess.GetDistanceBounds(Look);
        changedBounds = true;
        Look.OverrideOrbitDistanceBounds(1.1f, 3.2f);
        positionOffset = offset;
        Look.AddPosition(positionOffset);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (Look == null)
            return;

        if (lockedYaw)
            Look.RotationLocksY--;
        if (changedRotation)
            NativeCameraAccess.SetGlobalRotation(Look, previousRotation);
        if (changedBounds)
            Look.OverrideOrbitDistanceBounds(previousBounds.x, previousBounds.y);

        Look.AddPosition(-positionOffset);
    }
}
