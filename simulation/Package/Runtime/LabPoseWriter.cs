using System;
using System.IO;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal sealed class LabPoseWriter : IDisposable
    {
        private readonly BinaryWriter writer;
        private readonly SkinnedMeshRenderer[] renderers;
        private readonly Matrix4x4[][] bindposes;
        private int previousStep = -1;

        internal LabPoseWriter(LabActor actor, string path)
        {
            renderers = actor.Arms ? actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>() : new[] { actor.Body };
            bindposes = new Matrix4x4[renderers.Length][];
            writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("MIKUPOS1"));
            writer.Write(renderers.Length);
            for (int index = 0; index < renderers.Length; index++)
            {
                SkinnedMeshRenderer renderer = renderers[index];
                writer.Write(actor.Arms ? renderer.name.ToLowerInvariant().Contains("left") ? "arm-left" : "arm-right" : "body");
                bindposes[index] = renderer.sharedMesh.bindposes;
                writer.Write(bindposes[index].Length);
            }
        }

        internal void Sample(float time, float coverage)
        {
            int step = Mathf.FloorToInt(Mathf.Clamp01(coverage) * 8);
            if (step == previousStep)
                return;
            previousStep = step;
            writer.Write(time);
            for (int index = 0; index < renderers.Length; index++)
                for (int bone = 0; bone < bindposes[index].Length; bone++)
                {
                    Matrix4x4 matrix = renderers[index].bones[bone].localToWorldMatrix * bindposes[index][bone];
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++)
                            writer.Write(matrix[row, column]);
                }
        }

        public void Dispose() => writer.Dispose();
    }
}
