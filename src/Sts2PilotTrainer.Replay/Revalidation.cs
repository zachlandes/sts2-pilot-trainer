using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>What a reconstruction does when it is replayed on a build it was not recorded on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReproductionStatus>))]
public enum ReproductionStatus
{
    /// <summary>Every observed value still agrees and the fight still starts where it did.
    /// The patch did not touch anything on this run's path.</summary>
    Reproduces,

    /// <summary>The engine and the recording disagree somewhere. The patch changed something
    /// this run depends on, and this recording is retired for this build.</summary>
    Diverges,

    /// <summary>The question could not be asked - the environment differs in a way that is
    /// not a build difference, so nothing was measured.</summary>
    Blocked,
}

/// <summary>
/// Whether one reconstruction still reproduces on one build.
///
/// This is deliberately a separate artifact rather than an edit to the manifest. The
/// manifest says what the recording was made on and that never changes; this says what a
/// later build did with the same history. A catalogue is therefore indexed by
/// (recording, build), and a patch does not invalidate a recording - it invalidates the
/// claim that the recording still reproduces, which is a claim this record either renews
/// or withdraws.
/// </summary>
public sealed record ReproductionVerdict
{
    public const string CurrentSchema = "sts2-pilot-trainer/reproduction-verdict/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = CurrentSchema;
    [JsonPropertyName("run_id")] public required string RunId { get; init; }

    /// <summary>Binds this verdict to the exact history it was measured over. A manifest whose
    /// actions changed is a different reconstruction and needs its own verdict.</summary>
    [JsonPropertyName("action_history_hash")] public required string ActionHistoryHash { get; init; }

    [JsonPropertyName("recorded_build")] public required string RecordedBuild { get; init; }
    [JsonPropertyName("verified_build")] public required string VerifiedBuild { get; init; }
    [JsonPropertyName("recorded_content_hash")] public required string RecordedContentHash { get; init; }
    [JsonPropertyName("verified_content_hash")] public required string VerifiedContentHash { get; init; }

    [JsonPropertyName("status")] public required ReproductionStatus Status { get; init; }

    /// <summary>Where the engine first disagreed, when it did. The single most useful field
    /// on a divergence, because it usually names the card or monster the patch changed.</summary>
    [JsonPropertyName("first_divergence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstDivergence { get; init; }

    /// <summary>The combat-start digest this build derived. A recording whose checkpoints all
    /// pass but whose boundary moved is still retired: the fight a player would enter is not
    /// the recorded one.</summary>
    [JsonPropertyName("combat_start_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CombatStartDigest { get; init; }

    [JsonPropertyName("note")] public required string Note { get; init; }
}

/// <summary>
/// Asking an old reconstruction whether it still works on the build that is installed now.
///
/// The game ships a minor version roughly every fortnight and most of them change something.
/// The existing gate answers "does this reproduce?" only for the build the recording was made
/// on, and refuses everything else before it tries - so today a patch retires the whole
/// catalogue by declaration rather than by measurement. Most patches do not touch most runs,
/// and the difference between those two statements is the difference between a catalogue with
/// a two-week half-life and one that mostly survives.
/// </summary>
public static class Revalidation
{
    /// <summary>The environment fields a build change is allowed to move. Anything else
    /// differing means this is not a patch-drift question and nothing should be measured.</summary>
    public static readonly string[] BuildScopedFields = ["build_version", "build_date_utc", "content_hash"];

    // There is deliberately no "rebase this manifest onto the new build" helper here, and it
    // is worth saying why, because it is the obvious thing to reach for and it is wrong.
    //
    // It was written, and the validator rejected it on a rule that had nothing to do with the
    // build fields themselves: `source.run_summary` is a second reading of the environment
    // taken from the far end of the recording, and it must agree with `environment`. Rewriting
    // `environment` to say v0.112.0 leaves the summary still reading v0.111.0, which is exactly
    // the shape of a recording spliced from two runs - so the manifest becomes internally
    // inconsistent by construction. The only way to silence that is to rewrite the summary too,
    // and those are observations of the video: changing them would be falsifying evidence to
    // make a tool run.
    //
    // So the manifest is never edited. It says what the recording was made on, permanently. The
    // build being tested is an argument to the replay and a field on the verdict, and the
    // difference between the two is the subject of the measurement rather than something to be
    // smoothed away.

    /// <summary>
    /// Whether the only thing standing between this manifest and a replay is the build.
    ///
    /// A mismatched ascension, character, mode or unlock state is not patch drift and must not
    /// be silently rebased away; those are refusals with their own remediation, and answering
    /// a question nobody asked would bury them.
    /// </summary>
    public static bool IsBuildDriftOnly(IEnumerable<string> mismatchedFields) =>
        mismatchedFields.All(field => BuildScopedFields.Contains(field, StringComparer.Ordinal));

    /// <summary>
    /// Reads a verdict out of what the replay did. Pure, so the rule that decides whether a
    /// recording survives a patch has tests that need no game.
    /// </summary>
    public static ReproductionVerdict Decide(
        ReplayManifest manifest,
        string verifiedBuild,
        string verifiedContentHash,
        string? actionHistoryHash,
        bool replayedCleanly,
        string? firstDivergence,
        string? derivedCombatStartDigest)
    {
        var recordedDigest = manifest.Source.CombatStartSnapshotDigest?.Value;
        var boundaryMoved = recordedDigest is not null && derivedCombatStartDigest is not null &&
                            !string.Equals(recordedDigest, derivedCombatStartDigest, StringComparison.Ordinal);

        var status = replayedCleanly && !boundaryMoved
            ? ReproductionStatus.Reproduces
            : ReproductionStatus.Diverges;

        var note = status == ReproductionStatus.Reproduces
            ? $"Every observed value still agrees on {verifiedBuild} and the fight still starts where it did. " +
              "This recording carries forward; nothing about the manifest changes."
            : boundaryMoved && replayedCleanly
                ? $"Every checkpoint still passes on {verifiedBuild}, but the combat-start boundary moved. " +
                  "The fight a player would enter is not the recorded one, so this recording is retired for " +
                  "this build even though nothing observable disagreed."
                : $"The history no longer reproduces on {verifiedBuild}. " +
                  (firstDivergence is null
                      ? "It is retired for this build."
                      : $"First divergence: {firstDivergence}");

        return new ReproductionVerdict
        {
            RunId = manifest.RunId,
            ActionHistoryHash = actionHistoryHash ?? "(not computed)",
            RecordedBuild = manifest.Environment.BuildVersion.Value,
            VerifiedBuild = verifiedBuild,
            RecordedContentHash = manifest.Environment.ContentHash.Value,
            VerifiedContentHash = verifiedContentHash,
            Status = status,
            FirstDivergence = firstDivergence,
            CombatStartDigest = derivedCombatStartDigest,
            Note = note,
        };
    }

    /// <summary>The verdict for a manifest whose environment differs in ways a build change
    /// cannot explain, so nothing was measured and nothing may be concluded.</summary>
    public static ReproductionVerdict Blocked(
        ReplayManifest manifest, string verifiedBuild, string verifiedContentHash,
        IReadOnlyList<string> mismatchedFields) =>
        new()
        {
            RunId = manifest.RunId,
            ActionHistoryHash = "(not computed)",
            RecordedBuild = manifest.Environment.BuildVersion.Value,
            VerifiedBuild = verifiedBuild,
            RecordedContentHash = manifest.Environment.ContentHash.Value,
            VerifiedContentHash = verifiedContentHash,
            Status = ReproductionStatus.Blocked,
            Note =
                "This machine differs from the recording in ways a patch does not explain: " +
                string.Join(", ", mismatchedFields) +
                ". Those are their own refusals with their own remediation, so no reproduction " +
                "question was asked and none is answered here.",
        };
}
