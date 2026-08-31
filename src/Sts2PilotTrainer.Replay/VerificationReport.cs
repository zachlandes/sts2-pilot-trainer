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

public sealed record PreflightField(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("actual")] string Actual,
    [property: JsonPropertyName("matches")] bool Matches,
    [property: JsonPropertyName("diagnostic")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Diagnostic = null);

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
