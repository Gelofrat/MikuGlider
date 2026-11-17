using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Horse.MikuPhysicsLab
{
    [Serializable] public sealed class LabPhysicsSettings
    {
        public float fixedDeltaTime;
        public float maximumDeltaTime;
        public float maximumParticleDeltaTime;
        public float defaultContactOffset;
        public float defaultSolverIterations;
        public float defaultSolverVelocityIterations;
        public float bounceThreshold;
        public float sleepThreshold;
        public float defaultMaxDepenetrationVelocity;
        public float autoSyncTransforms;
        public float queriesHitBackfaces;
        public float queriesHitTriggers;
        public float clothInterCollisionDistance;
        public float clothInterCollisionStiffness;
        public float clothInterCollisionSettingsToggle;
        public float broadphaseType;
        public float simulationMode;
        public Vector3 gravity;
        public Vector3 clothGravity;
        public int skinWeights;
        public string qualitySource;
    }

    [Serializable] public sealed class LabPhysicsParity
    {
        public string animationDriver;
        public string unity;
        public bool settingsMatch;
        public string[] mismatches;
        public string[] playerLoop;
        public float fixedDeltaTime;
        public float maximumParticleDeltaTime;
        public string skinWeights;
        public bool developmentBuild;
        public string limitation;
    }

    internal static class LabPhysicsProfile
    {
        private static LabPhysicsSettings source;

        internal static void Apply()
        {
            source = JsonUtility.FromJson<LabPhysicsSettings>(Resources.Load<TextAsset>("MikuPhysicsLab/physics-profile").text);
            Time.fixedDeltaTime = source.fixedDeltaTime;
            Time.maximumDeltaTime = source.maximumDeltaTime;
            Time.maximumParticleDeltaTime = source.maximumParticleDeltaTime;
            Physics.gravity = source.gravity;
            Physics.clothGravity = source.clothGravity;
            Physics.defaultContactOffset = source.defaultContactOffset;
            Physics.defaultSolverIterations = (int)source.defaultSolverIterations;
            Physics.defaultSolverVelocityIterations = (int)source.defaultSolverVelocityIterations;
            Physics.bounceThreshold = source.bounceThreshold;
            Physics.sleepThreshold = source.sleepThreshold;
            Physics.defaultMaxDepenetrationVelocity = source.defaultMaxDepenetrationVelocity;
            Physics.autoSyncTransforms = source.autoSyncTransforms != 0;
            Physics.queriesHitBackfaces = source.queriesHitBackfaces != 0;
            Physics.queriesHitTriggers = source.queriesHitTriggers != 0;
            Physics.interCollisionDistance = source.clothInterCollisionDistance;
            Physics.interCollisionStiffness = source.clothInterCollisionStiffness;
            Physics.simulationMode = (SimulationMode)(int)source.simulationMode;
            QualitySettings.skinWeights = (SkinWeights)source.skinWeights;
        }

        internal static LabPhysicsParity Capture()
        {
            var errors = new List<string>();
            Check(errors, "fixedDeltaTime", source.fixedDeltaTime, Time.fixedDeltaTime);
            Check(errors, "maximumDeltaTime", source.maximumDeltaTime, Time.maximumDeltaTime);
            Check(errors, "maximumParticleDeltaTime", source.maximumParticleDeltaTime, Time.maximumParticleDeltaTime);
            Check(errors, "gravity", source.gravity.y, Physics.gravity.y);
            Check(errors, "clothGravity", source.clothGravity.y, Physics.clothGravity.y);
            Check(errors, "contactOffset", source.defaultContactOffset, Physics.defaultContactOffset);
            Check(errors, "solverIterations", source.defaultSolverIterations, Physics.defaultSolverIterations);
            Check(errors, "solverVelocityIterations", source.defaultSolverVelocityIterations, Physics.defaultSolverVelocityIterations);
            Check(errors, "simulationMode", source.simulationMode, (int)Physics.simulationMode);
            Check(errors, "queriesHitTriggers", source.queriesHitTriggers, Physics.queriesHitTriggers ? 1 : 0);
            Check(errors, "interCollisionDistance", source.clothInterCollisionDistance, Physics.interCollisionDistance);
            Check(errors, "interCollisionStiffness", source.clothInterCollisionStiffness, Physics.interCollisionStiffness);
            Check(errors, "skinWeights", source.skinWeights, (int)QualitySettings.skinWeights);
            Check(errors, "autoSyncTransforms", source.autoSyncTransforms, Physics.autoSyncTransforms ? 1 : 0);
            Check(errors, "queriesHitBackfaces", source.queriesHitBackfaces, Physics.queriesHitBackfaces ? 1 : 0);
            Check(errors, "bounceThreshold", source.bounceThreshold, Physics.bounceThreshold);
            Check(errors, "sleepThreshold", source.sleepThreshold, Physics.sleepThreshold);
            Check(errors, "maximumDepenetration", source.defaultMaxDepenetrationVelocity, Physics.defaultMaxDepenetrationVelocity);
            var loop = new List<string>();
            Flatten(PlayerLoop.GetCurrentPlayerLoop(), loop);
            return new LabPhysicsParity { animationDriver = LabController.Argument("-labDriver") ?? "playable",
                unity = Application.unityVersion, settingsMatch = errors.Count == 0, mismatches = errors.ToArray(), playerLoop = loop.ToArray(),
                fixedDeltaTime = Time.fixedDeltaTime, maximumParticleDeltaTime = Time.maximumParticleDeltaTime, skinWeights = QualitySettings.skinWeights.ToString(),
                developmentBuild = Debug.isDebugBuild,
                limitation = "Matches exported project settings and shared mod physics. Game input, locomotion forces, IK, networking and gameplay events are not emulated. Replay actor motion is inferred from its recorded root bone." };
        }

        private static void Check(List<string> errors, string name, float expected, float actual)
        {
            if (Mathf.Abs(expected - actual) > .000001f)
                errors.Add(name + ": expected " + expected + ", actual " + actual);
        }

        private static void Flatten(PlayerLoopSystem system, List<string> output)
        {
            if (system.type != null)
                output.Add(system.type.FullName);
            if (system.subSystemList != null)
                foreach (PlayerLoopSystem child in system.subSystemList)
                    Flatten(child, output);
        }
    }
}
