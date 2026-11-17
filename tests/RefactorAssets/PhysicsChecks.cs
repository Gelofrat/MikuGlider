using System;
using System.Linq;
using MikuGlider;
using UnityEngine;

internal static class PhysicsChecks
{
    private static int checks;

    internal static void Run(HairAnimationData data, MeshData skirt, bool requireSeams = true)
    {
        foreach (HairAnimationState state in Enum.GetValues(typeof(HairAnimationState)))
        {
            Require(!HairSpringSolver.ShouldSimulate(state, true), "no flight physics");
            Require(HairSpringSolver.ShouldSimulate(state, false) == (state == HairAnimationState.Idle), "transition gate");
        }
        Vector3[] local = RestPose(data);
        Vector3[] at30 = Simulate(data, local, 30);
        Vector3[] at60 = Simulate(data, local, 60);
        Vector3[] at120 = Simulate(data, local, 120);
        float frameError = at30.Zip(at120, Vector3.Distance).Max();
        Require(frameError < 0.035f, "frame independence: " + frameError);
        Require(at60.Zip(at120, Vector3.Distance).Max() < 0.025f, "60/120 frame independence");
        CheckCollisions(data, local);

        var reset = new HairSpringSolver(data);
        reset.Reset(local);
        Vector3[] teleport = local.Select(p => p + new Vector3(20f, 0f, 0f)).ToArray();
        reset.Step(teleport, Array.Empty<SimulationCapsule>(), 1f / 60f, 1f);
        Require(reset.Positions.Zip(teleport, Vector3.Distance).Max() == 0f, "teleport resets inertia");
        reset.Invalidate();
        reset.Step(local, Array.Empty<SimulationCapsule>(), 1f / 60f, 1f);
        Require(reset.Positions.Zip(local, Vector3.Distance).Max() == 0f, "visibility reset");

        Console.WriteLine("PHYSICS_CHECKS_PASS assertions=" + checks + " 30/120Error=" + frameError);
    }

    private static void CheckCollisions(HairAnimationData data, Vector3[] local)
    {
        var capsule = new SimulationCapsule(new Vector3(0f, -1f, 0f), Vector3.zero, 0.2f);
        Vector3 projected = capsule.ProjectOutside(new Vector3(0.02f, -0.5f, 0f), Vector3.right);
        Require(Math.Abs(projected.x - 0.2f) < 0.000001f, "capsule projection");
        var solver = new HairSpringSolver(data);
        float penetration = 0f;
        for (int frame = 0; frame < 240; frame++)
        {
            Vector3 origin = new Vector3(0.03f * (float)Math.Sin(frame * 0.05f), 2f, frame * 0.03f);
            Vector3[] pose = local.Select(p => p + origin).ToArray();
            var collision = new SimulationCapsule(origin + new Vector3(0f, -0.65f, 0f),
                origin + new Vector3(0f, -0.25f, 0f), 0.145f);
            solver.Step(pose, new[] { collision }, 1f / 60f, 1f);
            for (int index = 0; index < pose.Length; index++)
            {
                if (solver.Freedom[index] < 0.15f) continue;
                Vector3 corrected = collision.ProjectOutside(solver.Positions[index], pose[index]);
                penetration = Math.Max(penetration, Vector3.Distance(corrected, solver.Positions[index]));
                Require(Vector3.Distance(solver.Positions[index], pose[index]) < 0.4f, "collision motion bounded");
            }
        }
        Require(penetration < 0.025f, "capsule penetration: " + penetration);
        Console.WriteLine("HAIR_COLLISION_CHECK maximumPenetration=" + penetration);
    }

    private static Vector3[] Simulate(HairAnimationData data, Vector3[] local, int fps)
    {
        var solver = new HairSpringSolver(data);
        float maximumMotion = 0f;
        for (int frame = 0; frame <= 4 * fps; frame++)
        {
            float time = frame / (float)fps;
            float yaw = 0.6f * (float)Math.Sin(time * 2f);
            var rotation = new Quaternion(0f, (float)Math.Sin(yaw * 0.5f), 0f, (float)Math.Cos(yaw * 0.5f));
            float jump = time > 1f && time < 2f ? 0.65f * (float)Math.Sin((time - 1f) * Math.PI) : 0f;
            float slide = time > 2.5f && time < 3.5f ? -0.35f * (float)Math.Sin((time - 2.5f) * Math.PI) : 0f;
            Vector3 origin = new Vector3(0f, 2f + jump + slide + 0.02f * (float)Math.Sin(time * 15f), time * 3f);
            Vector3[] pose = local.Select(p => rotation * p + origin).ToArray();
            solver.Step(pose, Array.Empty<SimulationCapsule>(), 1f / fps, 1f);
            for (int index = 0; index < pose.Length; index++)
            {
                Vector3 actual = solver.Positions[index];
                Require(!float.IsNaN(actual.x) && !float.IsInfinity(actual.y), "finite spring positions");
                if (solver.Freedom[index] == 0f)
                    Require(Vector3.Distance(actual, pose[index]) < 0.000001f, "anchors stay fixed");
                else
                {
                    float motion = Vector3.Distance(actual, pose[index]);
                    maximumMotion = Math.Max(maximumMotion, motion);
                    Require(motion < 0.36f, "hair excursion bounded: " + motion);
                    int parent = data.Parents[index];
                    float error = Math.Abs(Vector3.Distance(actual, solver.Positions[parent]) - Vector3.Distance(pose[index], pose[parent]));
                    Require(error < 0.001f, "segment length: " + error);
                }
            }
        }
        Require(maximumMotion > 0.025f, "movement produces real trailing motion: " + maximumMotion);
        return solver.Positions;
    }

    private static Vector3[] RestPose(HairAnimationData data)
    {
        var positions = new Vector3[data.Parents.Length];
        var rotations = new Quaternion[positions.Length];
        var scales = new Vector3[positions.Length];
        for (int index = 0; index < positions.Length; index++)
        {
            int parent = data.Parents[index];
            if (parent < 0)
            {
                positions[index] = data.Positions[index];
                rotations[index] = data.RestRotations[index];
                scales[index] = data.Scales[index];
            }
            else
            {
                positions[index] = positions[parent] + rotations[parent] * Vector3.Scale(scales[parent], data.Positions[index]);
                rotations[index] = rotations[parent] * data.RestRotations[index];
                scales[index] = Vector3.Scale(scales[parent], data.Scales[index]);
            }
        }
        return positions;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
