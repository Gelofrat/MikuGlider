using UnityEngine;

namespace MikuGlider;

internal enum HairAnimationState
{
    Idle,
    Unfolding,
    Gliding,
    Folding
}

internal sealed class HairAnimationData
{
    internal int[] Parents { get; }
    internal string[] Names { get; }
    internal Vector3[] Positions { get; }
    internal Vector3[] Scales { get; }
    internal Quaternion[] RestRotations { get; }
    private readonly HairAnimationClip[] clips;

    internal HairAnimationData(int[] parents, string[] names, Vector3[] positions, Vector3[] scales,
        Quaternion[] restRotations, HairAnimationClip[] clips)
    {
        Parents = parents;
        Names = names;
        Positions = positions;
        Scales = scales;
        RestRotations = restRotations;
        this.clips = clips;
    }

    internal HairAnimationClip GetClip(HairAnimationState state)
    {
        return clips[(int)state];
    }
}

internal sealed class HairAnimationClip
{
    private readonly Quaternion[][] frames;
    private readonly float framesPerSecond;

    internal float Duration => (frames.Length - 1) / framesPerSecond;

    internal HairAnimationClip(Quaternion[][] frames, float framesPerSecond)
    {
        this.frames = frames;
        this.framesPerSecond = framesPerSecond;
    }

    internal Quaternion Sample(float time, int bone, bool loop)
    {
        float frame = (loop ? Mathf.Repeat(time, Duration) : Mathf.Clamp(time, 0f, Duration)) * framesPerSecond;
        int first = Mathf.Min((int)frame, frames.Length - 1);
        int second = Mathf.Min(first + 1, frames.Length - 1);
        return Quaternion.Slerp(frames[first][bone], frames[second][bone], frame - first);
    }
}
