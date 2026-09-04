using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A point in a recording's history that a player can be stood at, and the digest
/// that proves the state reached there is the recorded one.
///
/// A recording used to carry exactly one of these - the start of its first fight -
/// as a single field on the source. That was the whole product: one fight, one
/// entry. A list replaces the scalar because a recording is a whole run and every
/// fight in it, and every floor of it, is somewhere a player can be put; a second
/// scalar per kind would have been a second mechanism for the same idea.
///
/// It records where and what, and decides nothing. Which boundary a host enters is
/// <see cref="RecordedFightPlan"/>'s and <see cref="FloorEntryPlan"/>'s question,
/// and whether the live state is this one is <see cref="BoundaryEquality"/>'s.
/// </summary>
public sealed record ReplayBoundary
{
    /// <summary>The start of a fight, named by its ordinal in the run.</summary>
    public const string CombatStartKind = "combat_start";

    /// <summary>Arrival on a floor, before whatever that floor turns out to be.</summary>
    public const string FloorEntryKind = "floor_entry";

    /// <summary>The start of one turn inside a fight. Carried by the format so a
    /// later rewind has somewhere to land; no reader in these phases enters one.</summary>
    public const string TurnStartKind = "turn_start";

    /// <summary>
    /// Every kind a boundary may have. Closed, and enforced by the validator: an
    /// unrecognised kind is a boundary nothing knows how to reach, and accepting one
    /// would mean a host silently ignoring a place the recording says it can go.
    ///
    /// Deliberately not <see cref="Checkpoint.Kind"/>, which is free text describing
    /// what a checkpoint is about and is not a dispatch key.
    /// </summary>
    public static readonly string[] Kinds = [CombatStartKind, FloorEntryKind, TurnStartKind];

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>The state is this boundary's immediately after the action with this
    /// sequence number, or before any action when -1. Same coordinate a
    /// <see cref="Checkpoint"/> uses, for the same reason: sequence is identity and
    /// elapsed time is not.</summary>
    [JsonPropertyName("after_seq")]
    public required int AfterSeq { get; init; }

    /// <summary>Which fight of the run this is, counting from 1. Present for the
    /// combat kinds and absent otherwise.</summary>
    [JsonPropertyName("fight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Fight { get; init; }

    /// <summary>Which floor this is arrival on. Present for
    /// <see cref="FloorEntryKind"/> and absent otherwise.</summary>
    [JsonPropertyName("floor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Floor { get; init; }

    /// <summary>Which turn of the fight this starts. Present for
    /// <see cref="TurnStartKind"/> and absent otherwise.</summary>
    [JsonPropertyName("turn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Turn { get; init; }

    /// <summary>
    /// The complete canonical state digest at this boundary.
    ///
    /// <see cref="FactSource.Engine"/> when the verifier re-derived it by replaying
    /// the history, <see cref="FactSource.Captured"/> when a recorder read it out of
    /// the live game the run was played in. Never observed and never inferred: no
    /// video shows draw order or a random stream's position, which is the whole
    /// reason the digest exists.
    /// </summary>
    [JsonPropertyName("digest")]
    public required Fact<string> Digest { get; init; }

    [JsonIgnore]
    public bool IsCombatStart => string.Equals(Kind, CombatStartKind, StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsFloorEntry => string.Equals(Kind, FloorEntryKind, StringComparison.Ordinal);

    /// <summary>How a diagnostic names this boundary to a person.</summary>
    public string Describe() => Kind switch
    {
        CombatStartKind => $"the start of fight {Fight?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}",
        FloorEntryKind => $"arrival on floor {Floor?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}",
        TurnStartKind =>
            $"turn {Turn?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} of fight " +
            $"{Fight?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}",
        _ => $"a boundary of unsupported kind '{Kind}'",
    };

    public static ReplayBoundary CombatStart(int fight, int afterSeq, Fact<string> digest) =>
        new() { Kind = CombatStartKind, Fight = fight, AfterSeq = afterSeq, Digest = digest };

    public static ReplayBoundary FloorEntry(int floor, int afterSeq, Fact<string> digest) =>
        new() { Kind = FloorEntryKind, Floor = floor, AfterSeq = afterSeq, Digest = digest };

    public static ReplayBoundary TurnStart(int fight, int turn, int afterSeq, Fact<string> digest) =>
        new() { Kind = TurnStartKind, Fight = fight, Turn = turn, AfterSeq = afterSeq, Digest = digest };
}
