using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class ParticleMotion
{
    private Vector3[] previous;
    private Vector3[] velocities;

    internal void Reset()
    {
        previous = null;
        velocities = null;
    }

    internal void Measure(Vector3[] relative, float[] distances, float delta, float speedLimit, float accelerationLimit, ClothFrame result)
    {
        if (delta <= 0.00001f || delta > 0.1f || previous != null && previous.Length != relative.Length)
            Reset();
        var currentVelocity = new Vector3[relative.Length];
        var outliers = new List<int>();
        if (previous != null && previous.Length == relative.Length && delta > 0.00001f && delta <= 0.1f)
        {
            for (int index = 0; index < relative.Length; index++)
            {
                if (distances[index] == 0f)
                    continue;
                currentVelocity[index] = (relative[index] - previous[index]) / delta;
                float speed = currentVelocity[index].magnitude;
                float acceleration = velocities != null ? ((currentVelocity[index] - velocities[index]) / delta).magnitude : 0f;
                result.maximumRelativeSpeed = Math.Max(result.maximumRelativeSpeed, speed);
                result.maximumRelativeAcceleration = Math.Max(result.maximumRelativeAcceleration, acceleration);
                bool warning = false;
                if (speed > speedLimit)
                {
                    result.speedOutliers++;
                    warning = true;
                }
                if (acceleration > accelerationLimit)
                {
                    result.accelerationOutliers++;
                    warning = true;
                }
                if (warning)
                    outliers.Add(index);
            }
        }
        velocities = previous != null ? currentVelocity : null;
        previous = relative;
        result.jitterIndices = outliers.ToArray();
        if (result.speedOutliers > 0)
            result.issues |= ClothIssue.SpeedOutlier;
        if (result.accelerationOutliers > 0)
            result.issues |= ClothIssue.AccelerationOutlier;
    }
}
