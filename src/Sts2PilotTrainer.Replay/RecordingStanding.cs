namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Whether a recording holds anything either corpus number may be credited with, read
/// off what the recorder itself said of the run.
///
/// The two numbers are computed over one corpus and have to treat a recording the same
/// way: a recording <c>parity</c> holds nothing on is one <c>coverage</c> credits
/// nothing to, or a point could read covered by a recording the other number says is
/// not an account of its run. This is the one reader of that, so the rule and its
/// sentence cannot drift between the two commands. It is the same rule
/// <see cref="NativeSource.KeptOnly"/> applies at the library's offer, stated with the
/// cause so a report can print it: an integrity other than complete, or a continuity
/// the recorder marked broken. A rewound recording is whole and holds; a video
/// reconstruction states neither field and holds too, on the gate's own verdict.
/// </summary>
public sealed record RecordingStanding(RecordingStandingKind Kind, string Detail)
{
    public bool Holds => Kind == RecordingStandingKind.Holds;

    public static RecordingStanding Of(NativeSource? native)
    {
        if (native is null) return new RecordingStanding(RecordingStandingKind.Holds, "");

        if (native.StatesSomethingOtherThanComplete)
        {
            return new RecordingStanding(
                RecordingStandingKind.IntegrityNotComplete,
                $"integrity is '{native.Integrity}', so the recorder itself says this recording " +
                "is not a complete account of the run" +
                (native.Unmapped is { Count: > 0 } unmapped
                    ? $": {ManifestValidator.Describe(unmapped[0])}"
                    : ""));
        }

        if (!native.HistoryIsWhole)
        {
            return new RecordingStanding(
                RecordingStandingKind.ContinuityBroken,
                $"continuity is '{native.Continuity}', so the recorder stopped watching this run and " +
                "started again; a journal with a hole in it is not held to a replay");
        }

        return new RecordingStanding(RecordingStandingKind.Holds, "");
    }
}

public enum RecordingStandingKind
{
    Holds,
    IntegrityNotComplete,
    ContinuityBroken,
}
