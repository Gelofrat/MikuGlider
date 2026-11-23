using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MikuGlider;

internal sealed class SeatedArmProfile
{
    internal const string EnterClip = "Glider Sitting";
    internal const string LoopClip = "Glider Sit Idle";
    internal readonly string[] Bones;
    internal readonly Track Enter, Loop;
    internal readonly string IdleClip, IdleBone;
    internal readonly Quaternion IdleRotation;
    internal readonly string ExitClip;
    internal readonly int[] ExitBones;
    internal readonly Quaternion[] ExitRotations;

    internal static bool IsSeatedClip(string name) =>
        name.StartsWith("Sit ", StringComparison.Ordinal) || name.StartsWith("Glider Sit", StringComparison.Ordinal);

    private SeatedArmProfile(AssetBinaryReader reader, string skin)
    {
        reader.ExpectHeader("MIKUSE01");
        if (reader.ReadName() != skin) throw new InvalidDataException("Seated pose rig differs from the skin.");
        Bones = new string[reader.ReadCount(1, 32, "seated bone count")];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Bones.Length; i++)
        {
            Bones[i] = reader.ReadName();
            if (!unique.Add(Bones[i])) throw new InvalidDataException("Duplicate seated bone.");
        }
        Enter = new Track(reader, EnterClip, Bones.Length);
        Loop = new Track(reader, LoopClip, Bones.Length);
        IdleClip = reader.ReadName();
        IdleBone = reader.ReadName();
        IdleRotation = reader.ReadRotation();
        ExitClip = reader.ReadName();
        ExitBones = new int[reader.ReadCount(0, Bones.Length, "seated exit bone count")];
        ExitRotations = new Quaternion[ExitBones.Length];
        unique.Clear();
        for (int i = 0; i < ExitBones.Length; i++)
        {
            string bone = reader.ReadName();
            ExitBones[i] = Array.IndexOf(Bones, bone);
            if (ExitBones[i] < 0 || !unique.Add(bone)) throw new InvalidDataException("Invalid seated exit bone.");
            ExitRotations[i] = reader.ReadRotation();
        }
        reader.RequireEnd();
    }

    internal static SeatedArmProfile Read(Stream stream, string skin)
    {
        using (var reader = new AssetBinaryReader(stream))
            return new SeatedArmProfile(reader, skin);
    }

    internal sealed class Track
    {
        private readonly float[] times;
        private readonly Quaternion[,] rotations;

        internal Track(AssetBinaryReader reader, string clip, int boneCount)
        {
            if (reader.ReadName() != clip) throw new InvalidDataException("Unexpected seated clip.");
            int count = reader.ReadCount(2, 1024, "seated key count");
            reader.RequireRemaining(count * (4L + 16L * boneCount));
            times = new float[count];
            rotations = new Quaternion[count, boneCount];
            for (int i = 0; i < count; i++)
            {
                times[i] = reader.ReadFinite();
                if ((i == 0 && times[i] != 0) || (i > 0 && times[i] <= times[i - 1]) || (i == count - 1 && times[i] != 1))
                    throw new InvalidDataException("Invalid seated key time.");
                for (int b = 0; b < boneCount; b++) rotations[i, b] = reader.ReadRotation();
            }
        }

        internal void Sample(float phase, float share, Quaternion[] result)
        {
            int hi = Array.BinarySearch(times, phase);
            if (hi < 0) hi = ~hi;
            hi = Mathf.Clamp(hi, 1, times.Length - 1);
            float t = Mathf.InverseLerp(times[hi - 1], times[hi], phase);
            for (int b = 0; b < result.Length; b++)
            {
                var q = Quaternion.Slerp(rotations[hi - 1, b], rotations[hi, b], t);
                result[b] = share >= 1 ? q : Quaternion.Slerp(result[b], q, share);
            }
        }
    }
}
