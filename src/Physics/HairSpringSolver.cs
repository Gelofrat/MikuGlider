using System;
using UnityEngine;

namespace MikuGlider;

internal sealed class HairSpringSolver
{
    private const float MaximumStep = 1f / 120f;
    private const int ConstraintIterations = 5;
    private readonly int[] parents;
    private readonly float[] freedom;
    private readonly float[] lengths;
    private readonly Vector3[] previous;
    private readonly Vector3[] previousTargets;
    private readonly Vector3[] targets;
    private readonly Vector3[] positions;
    private bool initialized;
    private float previousStep = MaximumStep;

    internal Vector3[] Positions => positions;
    internal float[] Freedom => freedom;

    internal HairSpringSolver(HairAnimationData data)
    {
        parents = data.Parents;
        int count = parents.Length;
        freedom = new float[count];
        lengths = new float[count];
        previous = new Vector3[count];
        previousTargets = new Vector3[count];
        targets = new Vector3[count];
        positions = new Vector3[count];

        var distances = new float[count];
        var depths = new int[count];
        float maximumDistance = 0f;
        for (int index = 0; index < count; index++)
        {
            int parent = parents[index];
            if (!data.Names[index].StartsWith("j_tail_", StringComparison.Ordinal) || parent < 0)
                continue;

            bool tailParent = data.Names[parent].StartsWith("j_tail_", StringComparison.Ordinal);
            distances[index] = tailParent ? distances[parent] + data.Positions[index].magnitude : 0f;
            depths[index] = tailParent ? depths[parent] + 1 : 0;
            maximumDistance = Mathf.Max(maximumDistance, distances[index]);
        }

        for (int index = 0; index < count; index++)
        {
            if (depths[index] > 1 && maximumDistance > 0f)
                freedom[index] = Mathf.Pow(Mathf.Clamp01(distances[index] / maximumDistance), 0.7f);
        }
    }

    internal static bool ShouldSimulate(HairAnimationState state, bool flying)
    {
        return !flying && state == HairAnimationState.Idle;
    }

    internal void Reset(Vector3[] pose)
    {
        Array.Copy(pose, positions, pose.Length);
        Array.Copy(pose, previous, pose.Length);
        Array.Copy(pose, previousTargets, pose.Length);
        initialized = true;
        previousStep = MaximumStep;
    }

    internal void Invalidate()
    {
        initialized = false;
    }

    internal void Step(Vector3[] pose, SimulationCapsule[] collisions, float deltaTime, float scale)
    {
        if (!initialized || deltaTime > 0.1f || (pose[0] - previousTargets[0]).sqrMagnitude > 2.25f * scale * scale)
        {
            Reset(pose);
            return;
        }

        if (deltaTime <= 0f)
            return;

        int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / MaximumStep), 1, 12);
        float step = deltaTime / steps;
        for (int substep = 0; substep < steps; substep++)
        {
            float fraction = (substep + 1f) / steps;
            for (int index = 0; index < pose.Length; index++)
                targets[index] = Vector3.Lerp(previousTargets[index], pose[index], fraction);

            Integrate(step, scale);
            for (int iteration = 0; iteration < ConstraintIterations; iteration++)
                Constrain(collisions, scale);

            previousStep = step;
        }

        Array.Copy(pose, previousTargets, pose.Length);
    }

    private void Integrate(float step, float scale)
    {
        float velocityScale = step / previousStep;
        float damping = Mathf.Exp(-4.8f * step);
        for (int index = 0; index < positions.Length; index++)
        {
            int parent = parents[index];
            lengths[index] = parent >= 0 ? Vector3.Distance(targets[index], targets[parent]) : 0f;
            if (freedom[index] == 0f)
            {
                positions[index] = targets[index];
                previous[index] = targets[index];
                continue;
            }

            Vector3 position = positions[index];
            Vector3 velocity = (position - previous[index]) * velocityScale * damping;
            float stiffness = Mathf.Lerp(160f, 44f, freedom[index]);
            Vector3 acceleration = (targets[index] - position) * stiffness + Vector3.down * (1.4f * scale);
            positions[index] = position + velocity + acceleration * (step * step);
            previous[index] = position;
        }
    }

    private void Constrain(SimulationCapsule[] collisions, float scale)
    {
        for (int index = 0; index < positions.Length; index++)
        {
            if (freedom[index] == 0f)
                continue;

            int parent = parents[index];
            Vector3 position = positions[index];
            float maximumOffset = scale * (0.008f + 0.30f * freedom[index] * freedom[index]);
            position = targets[index] + Vector3.ClampMagnitude(position - targets[index], maximumOffset);
            foreach (SimulationCapsule capsule in collisions)
                position = capsule.ProjectOutside(position, targets[index]);

            Vector3 direction = position - positions[parent];
            if (direction.sqrMagnitude < 0.000001f)
                direction = targets[index] - targets[parent];

            positions[index] = positions[parent] + direction.normalized * lengths[index];
        }
    }
}
