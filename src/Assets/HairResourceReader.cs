using System.IO;
using UnityEngine;

namespace MikuGlider;

internal static class HairResourceReader
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly string[] ClipNames = { "Hair_IdleSway", "Hair_Unfold", "Hair_GlideLoop", "Hair_Fold" };

    internal static HairAnimationData Read(Stream stream)
    {
        using (var reader = new AssetBinaryReader(stream))
        {
            reader.ExpectHeader("MIKUHR01");
            int boneCount = reader.ReadCount(1, 256, "hair bone count");
            var parents = new int[boneCount];
            var names = new string[boneCount];
            var positions = new Vector3[boneCount];
            var scales = new Vector3[boneCount];
            var rotations = new Quaternion[boneCount];

            for (int index = 0; index < boneCount; index++)
            {
                int parent = reader.ReadInt32();
                if (parent < -1 || parent >= index)
                    throw new InvalidDataException("Invalid hair bone hierarchy.");

                parents[index] = parent;
                names[index] = reader.ReadName(160);
                positions[index] = reader.ReadVector3();
                rotations[index] = reader.ReadRotation();
                scales[index] = reader.ReadVector3();
                if (scales[index].x <= 0f || scales[index].y <= 0f || scales[index].z <= 0f)
                    throw new InvalidDataException("Invalid hair bone scale.");
            }

            reader.ReadCount(ClipNames.Length, ClipNames.Length, "hair clip count");
            var clips = new HairAnimationClip[ClipNames.Length];
            for (int index = 0; index < clips.Length; index++)
                clips[index] = ReadClip(reader, ClipNames[index], boneCount);

            reader.RequireEnd();
            return new HairAnimationData(parents, names, positions, scales, rotations, clips);
        }
    }

    private static HairAnimationClip ReadClip(AssetBinaryReader reader, string expectedName, int boneCount)
    {
        if (reader.ReadName(160) != expectedName)
            throw new InvalidDataException("Unexpected hair clip; expected " + expectedName + ".");

        int frameCount = reader.ReadCount(2, 300, "hair frame count");
        float framesPerSecond = reader.ReadFinite();
        if (framesPerSecond < 1f || framesPerSecond > 120f)
            throw new InvalidDataException("Invalid hair animation frame rate.");

        reader.RequireRemaining(frameCount * boneCount * 16L);
        var frames = new Quaternion[frameCount][];
        for (int frame = 0; frame < frames.Length; frame++)
        {
            frames[frame] = new Quaternion[boneCount];
            for (int bone = 0; bone < boneCount; bone++)
                frames[frame][bone] = reader.ReadRotation();
        }

        return new HairAnimationClip(frames, framesPerSecond);
    }
}
