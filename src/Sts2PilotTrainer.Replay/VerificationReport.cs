using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>What the arbiter concluded, and on what basis.</summary>
public sealed record VerificationReport
{
    [JsonPropertyName("status")]
    public required VerificationStatus Status { get; init; }

    [JsonPropertyName("arbiter_version")]
    public required string ArbiterVersion { get; init; }

    /// <summary>Result of comparing the manifest's environment identity against the
    /// local install. A failure here stops everything: replaying a run in the wrong
    /// environment produces a confident, wrong answer.</summary>
    [JsonPropertyName("preflight")]
    public required PreflightResult Preflight { get; init; }

    [JsonPropertyName("checkpoints")]
    public IReadOnlyList<CheckpointResult> Checkpoints { get; init; } = [];

    /// <summary>Digest of the engine's canonical end state. Two runs of the same
    /// manifest must produce the same digest, in separate processes.</summary>
    [JsonPropertyName("final_state_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinalStateDigest { get; init; }

    /// <summary>
    /// What happened along the way, as data rather than as a summary. Present on any
    /// replay that started, including a rejected one - a history that diverged is
    /// exactly the one whose intermediate states are worth reading.
    /// </summary>
    [JsonPropertyName("trace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReplayTrace? Trace { get; init; }

    /// <summary>
    /// Every place in this history a player could be stood, with the digest the engine
    /// produced there.
    ///
    /// Derived rather than copied: where the boundaries are is a rule over the trace,
    /// and what the state was at each is the whole canonical state, which no trace
    /// carries. This is what <c>migrate-manifest --derive-boundaries</c> writes into a
    /// manifest's <see cref="ReplayManifest.Boundaries"/>.
    ///
    /// Present only on a replay that ran to the end of the history, because a partial
    /// one has not established where the boundaries after its stop are.
    /// </summary>
    [JsonPropertyName("boundaries")]
    public IReadOnlyList<ReplayBoundary> Boundaries { get; init; } = [];

    [JsonPropertyName("action_history_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionHistoryHash { get; init; }

    /// <summary>
    /// Everything a reader would need to know before treating this as proof, in
    /// plain words. Never empty in practice: the headless host is not the retail
    /// client, and saying so is part of the result rather than a footnote to it.
    /// </summary>
    [JsonPropertyName("caveats")]
    public IReadOnlyList<string> Caveats { get; init; } = [];

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<VerificationStatus>))]
public enum VerificationStatus
{
    /// <summary>Replayed, and every checkpoint agreed.</summary>
    Verified,

    /// <summary>Only a requested prefix was replayed. Unevaluated actions and
    /// checkpoints prevent this result from verifying the manifest.</summary>
    Partial,

    /// <summary>Not attempted, because the environment did not match. This is a
    /// clean refusal, not a failure - the manifest may be perfectly good elsewhere.</summary>
    Refused,

    /// <summary>Attempted and contradicted: the engine disagreed with an observation,
    /// or the replay could not be carried out. The manifest is wrong, or the video
    /// reading is, and either way it is not proof of anything.</summary>
    Rejected,
}

/// <summary>Field-by-field comparison of the manifest's environment against this machine.</summary>
public sealed record PreflightResult(
    [property: JsonPropertyName("matches")] bool Matches,
    [property: JsonPropertyName("fields")] IReadOnlyList<PreflightField> Fields);

/// <summary>
/// What one preflight rule decided, in the three answers a rule can honestly have.
///
/// The third exists because a bool has to lie about one of them. A shortfall a
/// person can go and fix and a shortfall nobody can fix are both "false", and a
/// reader that cannot tell them apart ends up telling somebody to go and unlock
/// content their build does not contain - an instruction that can never come true,
/// given about a game that is working correctly.
///
/// Numbered from one, so that zero means "the file did not say". A report written
/// before this existed carries only <c>matches</c>, and the reader derives the
/// outcome from that rather than taking a default that would read a recorded refusal
/// back as a pass.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreflightOutcome>))]
public enum PreflightOutcome
{
    /// <summary>This environment satisfies the rule.</summary>
    Met = 1,

    /// <summary>It does not, and meeting it is something somebody can do: unlock the
    /// content by playing, install the build the recording names, disable a mod. The
    /// diagnostic on such a field says what to do.</summary>
    NotMet = 2,

    /// <summary>
    /// It does not, and nobody can make it: this build does not ship what the
    /// recording names, or the question could not be asked because of that.
    ///
    /// A diagnostic here states the fact and stops. It must never instruct, because
    /// every instruction it could give would be false.
    /// </summary>
    Unavailable = 3,
}

/// <summary>
/// One rule's verdict, with the values it compared and - where it failed - the
/// sentence that says why.
///
/// <see cref="Matches"/> is derived rather than stored, so the failing outcomes
/// cannot drift apart from the pass/fail every caller already reads.
/// </summary>
public sealed record PreflightField
{
    public PreflightField(
        string field, string expected, string actual, PreflightOutcome outcome, string? diagnostic = null)
    {
        Field = field;
        Expected = expected;
        Actual = actual;
        Outcome = outcome;
        Diagnostic = diagnostic;
    }

    /// <summary>
    /// The pass-or-fail rules, which is most of them: a build version, a content
    /// hash, a mod list, an ascension ceiling. Failing one of those is something
    /// somebody can act on, so false is <see cref="PreflightOutcome.NotMet"/>.
    ///
    /// A rule whose failure nobody can act on names <see cref="PreflightOutcome"/>
    /// explicitly instead. Those all live in <see cref="EnvironmentPreflight"/>'s
    /// unlock rules: an exact requirement naming ids this build does not ship or that
    /// nothing enumerated, and the act question that could not be asked because of
    /// it.
    /// </summary>
    public PreflightField(string field, string expected, string actual, bool matches, string? diagnostic = null)
        : this(field, expected, actual, matches ? PreflightOutcome.Met : PreflightOutcome.NotMet, diagnostic)
    {
    }

    /// <summary>
    /// Reading one back, including one written before the outcome existed.
    ///
    /// A report is somebody's evidence and older ones carry only <c>matches</c>, so an
    /// absent outcome is derived from it rather than defaulted - defaulting would read
    /// a recorded failure back as a pass, which is the one direction that must never
    /// happen silently. Where both are present the outcome wins, because it is the
    /// finer answer and <c>matches</c> is derived from it on the way out.
    /// </summary>
    [JsonConstructor]
    public PreflightField(
        string field, string expected, string actual, PreflightOutcome outcome, bool matches, string? diagnostic)
        : this(
            field, expected, actual,
            Enum.IsDefined(outcome)
                ? outcome
                : matches ? PreflightOutcome.Met : PreflightOutcome.NotMet,
            diagnostic)
    {
    }

    [JsonPropertyName("field")]
    public string Field { get; init; }

    [JsonPropertyName("expected")]
    public string Expected { get; init; }

    [JsonPropertyName("actual")]
    public string Actual { get; init; }

    [JsonPropertyName("outcome")]
    public PreflightOutcome Outcome { get; init; }

    [JsonPropertyName("matches")]
    public bool Matches => Outcome == PreflightOutcome.Met;

    [JsonPropertyName("diagnostic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Diagnostic { get; init; }
}

public sealed record CheckpointResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("after_seq")] int AfterSeq,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("comparisons")] IReadOnlyList<FieldComparison> Comparisons);

public sealed record FieldComparison(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("actual")] string Actual,
    [property: JsonPropertyName("matches")] bool Matches);
