using System;
using System.Runtime.CompilerServices;

namespace MikuGlider;

internal enum MissionIntroDecision
{
    Ignored,
    Disabled,
    Skipped,
    Play
}

internal sealed class MissionIntroChance
{
    private readonly ConditionalWeakTable<object, object> attempts = new ConditionalWeakTable<object, object>();
    private readonly Func<int> nextBit;

    internal MissionIntroChance(Func<int> nextBit)
    {
        this.nextBit = nextBit;
    }

    // A failed roll (or disabled setting) also consumes this mission's single attempt.
    internal MissionIntroDecision Decide(object mission, bool enabled)
    {
        if (mission == null || attempts.TryGetValue(mission, out _))
            return MissionIntroDecision.Ignored;
        attempts.Add(mission, new object());
        if (!enabled)
            return MissionIntroDecision.Disabled;
        return nextBit() == 0 ? MissionIntroDecision.Play : MissionIntroDecision.Skipped;
    }
}
