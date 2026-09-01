using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A point in the replay where independently observed state must agree with the
/// engine. Checkpoints are what make a replay falsifiable: without them, a replay
/// that ran to completion proves only that it ran.
/// </summary>
public sealed record Checkpoint
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Evaluated immediately after the action with this sequence number, or before
    /// any action when -1. Bound to sequence rather than to a timestamp because
    /// action timing is not part of the run's identity - the game documents its
    /// non-gameplay randomness as explicitly non-deterministic, and its gameplay
    /// randomness as driven by ordered events rather than by elapsed time.
    /// </summary>
    [JsonPropertyName("after_seq")]
    public required int AfterSeq { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>
    /// Field name to expected value. Field names are the canonical state field
    /// names produced by the engine projection, so a checkpoint that names a field
    /// the projection does not produce is a manifest defect and is reported as one
    /// rather than passing vacuously.
    /// </summary>
    [JsonPropertyName("expect")]
    public required IReadOnlyDictionary<string, Fact<string>> Expect { get; init; }

    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}
