using System;

namespace MikuGlider;

// Value-only eligibility policy. No objective class allowlist or engine calls.
internal struct MissionIntroContext
{
    internal bool ShowNewObjectiveText, IsSideObjective, LateJoined, MissionEnded;
    internal int MainObjectiveIndex, ObjectiveTextIndex;
    internal string Popup, KillTitle, MainTitle;

    internal bool IsEligible => ShowNewObjectiveText && !IsSideObjective && !LateJoined && !MissionEnded &&
        MainObjectiveIndex == 0 && ObjectiveTextIndex == 0 && !string.IsNullOrEmpty(Popup) &&
        string.Equals(Popup, KillTitle, StringComparison.Ordinal) &&
        string.Equals(Popup, MainTitle, StringComparison.Ordinal);
}
