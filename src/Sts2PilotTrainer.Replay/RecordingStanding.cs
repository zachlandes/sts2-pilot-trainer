namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Whether a recording holds anything either corpus number may be credited with, read
/// off the build it was made on and what the recorder itself said of the run.
///
/// The two numbers are computed over one corpus and have to treat a recording the same
/// way: a recording <c>parity</c> holds nothing on is one <c>coverage</c> credits
/// nothing to, or a point could read covered by a recording the other number says is
/// not an account of its run. This is the one reader of that, so the rule and its
/// sentence cannot drift between the two commands.
///
/// The build is asked first, of every recording, because it is the outermost question:
/// a recording of another build is evidence about that build and none about this one,
/// whatever the recorder said of the run, and the replay's own preflight refuses it
/// before a run is constructed. The rule is <see cref="EnvironmentPreflight.Build"/>,
/// the preflight's own three fields, and the sentence is the preflight's own refusal,
/// so a recording the arbiter refuses as another build's is one the corpus numbers
/// name in the same words - which is what a recording credited by <c>coverage</c> and
/// refused by <c>replay</c> was short of.
///
/// Then the recorder's own account, the same rule <see cref="NativeSource.KeptOnly"/>
/// applies at the library's offer, stated with the cause so a report can print it: an
/// integrity other than complete, or a continuity the recorder marked broken. A rewound
/// recording is whole and holds; a video reconstruction states neither field and holds
/// on the gate's own verdict, once its build is this one.
/// </summary>
/// <param name="Build">The build rule's three fields, compared, whichever way they
/// answered: what a report prints beside a recording of another build.</param>
public sealed record RecordingStanding(
    RecordingStandingKind Kind, string Detail, IReadOnlyList<PreflightField> Build)
{
    public bool Holds => Kind == RecordingStandingKind.Holds;

    /// <param name="native">What the recorder said of the run; null for a reconstruction.</param>
    /// <param name="environment">The build the recording was made on, as it records it.</param>
    /// <param name="build">The build under test - the one this process would replay
    /// with, read by the command and never by this project, which stays game-free.</param>
    public static RecordingStanding Of(NativeSource? native, EnvironmentIdentity environment, LocalBuild build)
    {
        var fields = EnvironmentPreflight.Build(environment, build);
        var mismatched = fields.Where(field => !field.Matches).ToList();
        if (mismatched.Count > 0)
        {
            return new RecordingStanding(
                RecordingStandingKind.AnotherBuild,
                string.Join("\n", mismatched.Select(field => field.Refusal)),
                fields);
        }

        if (native is null) return new RecordingStanding(RecordingStandingKind.Holds, "", fields);

        if (native.StatesSomethingOtherThanComplete)
        {
            return new RecordingStanding(
                RecordingStandingKind.IntegrityNotComplete,
                $"integrity is '{native.Integrity}', so the recorder itself says this recording " +
                "is not a complete account of the run" +
                (native.Unmapped is { Count: > 0 } unmapped
                    ? $": {ManifestValidator.Describe(unmapped[0])}"
                    : ""),
                fields);
        }

        if (!native.HistoryIsWhole)
        {
            return new RecordingStanding(
                RecordingStandingKind.ContinuityBroken,
                $"continuity is '{native.Continuity}', so the recorder stopped watching this run and " +
                "started again; a journal with a hole in it is not held to a replay",
                fields);
        }

        return new RecordingStanding(RecordingStandingKind.Holds, "", fields);
    }
}

public enum RecordingStandingKind
{
    Holds,

    /// <summary>Made on a build other than the one under test, by the preflight's own
    /// build rule; nothing it holds is evidence about this build.</summary>
    AnotherBuild,

    IntegrityNotComplete,
    ContinuityBroken,
}
