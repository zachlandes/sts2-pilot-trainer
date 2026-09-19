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
///
/// Last, the recorder that wrote it, and this is the one question the two numbers
/// answer differently. A journal is what a recorder wrote down as the state each
/// decision began from and settled into, and a recorder defect is a defect in that
/// file: a recording whose <c>source.native.recorder_version</c> is below the recorder
/// this build carries is <see cref="RecordingStandingKind.OlderRecorder"/>, and
/// <c>parity</c> holds nothing on it - its journal is what the older recorder got
/// wrong, and holding the release candidate to it would count the older recorder's
/// findings against the candidate. Its manifest still replays on this build, and a
/// replayed history is a witness that every point on it is reachable, so it still
/// <see cref="CreditsCoverage">credits coverage</see>. A version the recording does not
/// carry, one that does not parse, and the <c>1.0.0.0</c> a recorder built before the
/// version was stamped named itself with are all an older recorder, never
/// <see cref="RecordingStandingKind.Holds"/>: the release measurement is over the
/// candidate's own recordings, and a recording that cannot say it is one is not.
/// </summary>
/// <param name="Build">The build rule's three fields, compared, whichever way they
/// answered: what a report prints beside a recording of another build.</param>
public sealed record RecordingStanding(
    RecordingStandingKind Kind, string Detail, IReadOnlyList<PreflightField> Build)
{
    /// <summary>Nothing stands against the recording: <c>parity</c> holds its journal
    /// to a replay and <c>coverage</c> credits what it reached.</summary>
    public bool Holds => Kind == RecordingStandingKind.Holds;

    /// <summary>Whether <c>coverage</c> credits what the recording reached: a
    /// recording that holds, and one an older recorder wrote, whose manifest replays
    /// on this build whatever its journal got wrong.</summary>
    public bool CreditsCoverage => Kind is RecordingStandingKind.Holds or RecordingStandingKind.OlderRecorder;

    /// <summary>How a recorder names itself, ahead of the version the mod manifest spells.</summary>
    public const string RecorderPrefix = "runmobile-recorder/";

    /// <summary>
    /// What a recorder built before Runmobile's version was stamped into it named
    /// itself with: .NET's default assembly version, four parts, which the mod
    /// manifest never spells (<c>Directory.Build.props</c> owns why). It is above every
    /// stamped version as a number and older than all of them as a recorder.
    /// </summary>
    public const string UnstampedRecorderVersion = "1.0.0.0";

    /// <param name="native">What the recorder said of the run; null for a reconstruction.</param>
    /// <param name="environment">The build the recording was made on, as it records it.</param>
    /// <param name="build">The build under test - the one this process would replay
    /// with, read by the command and never by this project, which stays game-free.</param>
    /// <param name="recorderVersion">The version of the recorder this build carries, as
    /// the mod manifest spells it (<c>0.2.0</c>): what <c>RunmobileVersion.Current</c>
    /// reports, passed in by the command the way the build is.</param>
    public static RecordingStanding Of(
        NativeSource? native, EnvironmentIdentity environment, LocalBuild build, string recorderVersion)
    {
        if (!Version.TryParse(recorderVersion, out var current))
        {
            throw new ArgumentException(
                $"'{recorderVersion}' is not a version the recorder could have been stamped with; the build " +
                "under test names its own recorder the way Runmobile.json spells it.",
                nameof(recorderVersion));
        }

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

        if (RecorderVersionOf(native.RecorderVersion) is not { } wrote || wrote < current)
        {
            return new RecordingStanding(
                RecordingStandingKind.OlderRecorder,
                $"journal written by recorder '{native.RecorderVersion}'; this build's recorder is " +
                $"'{RecorderPrefix}{recorderVersion}', and what changed between them is why the journal is not " +
                "held to a replay. The manifest still replays on this build, so what it reached is credited to " +
                "coverage",
                fields);
        }

        return new RecordingStanding(RecordingStandingKind.Holds, "", fields);
    }

    /// <summary>The version a recorder named itself with, or null where it named none
    /// this build reads as one: no prefix, a string that is not a version, or the
    /// unstamped default.</summary>
    private static Version? RecorderVersionOf(string? recorderVersion)
    {
        if (recorderVersion is null || !recorderVersion.StartsWith(RecorderPrefix, StringComparison.Ordinal)) return null;
        var spelled = recorderVersion[RecorderPrefix.Length..];
        if (string.Equals(spelled, UnstampedRecorderVersion, StringComparison.Ordinal)) return null;
        return Version.TryParse(spelled, out var version) ? version : null;
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

    /// <summary>Written by a recorder below the one this build carries, or by one that
    /// named no version this build reads; its journal is not held to a replay, and its
    /// manifest still credits coverage.</summary>
    OlderRecorder,
}
