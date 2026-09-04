using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// One decision in the run, in order. The <see cref="Seq"/>/<see cref="Verb"/>/
/// <see cref="Args"/> triple is the semantic content; everything else is
/// provenance and is excluded from the action-history hash so that improving an
/// annotation never invalidates a verified snapshot.
/// </summary>
public sealed record ActionRecord
{
    /// <summary>Position in the run, from 0. Dense and gap-free by validation rule:
    /// a gap would be a missing action wearing a plausible face.</summary>
    [JsonPropertyName("seq")]
    public required int Seq { get; init; }

    [JsonPropertyName("verb")]
    public required ActionVerb Verb { get; init; }

    /// <summary>Verb-specific parameters. Kept as a sorted string map so that the
    /// canonical form is obvious and the hash is stable across serializers.</summary>
    [JsonPropertyName("args")]
    public IReadOnlyDictionary<string, string> Args { get; init; } =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("source")]
    public required FactSource Source { get; init; }

    [JsonPropertyName("evidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FactEvidence? Evidence { get; init; }

    /// <summary>
    /// Which run-persistent RNG streams this action is expected to advance, if any.
    /// This is the error-tolerance model made explicit: an action that advances no
    /// stream can be misread without desynchronising anything after it, while an
    /// action that advances one is unforgiving. It is a documented expectation, not
    /// a measurement - the arbiter is what actually decides.
    /// </summary>
    private IReadOnlyList<string>? consumesRng;

    [JsonPropertyName("consumes_rng")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ConsumesRng
    {
        get => consumesRng;
        init => consumesRng = value is { Count: > 0 } ? value : null;
    }

    /// <summary>Free-text note for a human reviewer. Never load-bearing.</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

/// <summary>
/// The decision alphabet. Deliberately closed: an action the format cannot name is
/// an action the arbiter cannot replay, and that must be a loud failure rather than
/// a silently dropped decision.
///
/// This milestone implements only the subset the selected proof needs. The rest are
/// named but unimplemented, and the engine refuses them explicitly - a named verb
/// that quietly does nothing would be the worst of both worlds.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionVerb>))]
public enum ActionVerb
{
    // ── Implemented for the selected proof ──
    /// <summary>Pick one of Neow's opening blessings. Args: <c>option_index</c>.</summary>
    ChooseNeowBlessing,

    /// <summary>Move to a map node. Args: <c>act</c>, <c>row</c>, <c>column</c>.</summary>
    MapMove,

    /// <summary>Play a card from hand. Args: <c>card_id</c>, <c>hand_index</c>,
    /// and <c>target_index</c> when choosing among multiple enemies.</summary>
    PlayCard,

    /// <summary>End the player's turn.</summary>
    EndTurn,

    ChooseEventOption,
    ClaimReward,
    TakeCard,
    SkipRewards,
    SelectCardFromScreen,

    // ── Named but not implemented in this milestone ──
    ChooseRestSiteOption,
    UsePotion,
    DiscardPotion,
    SelectHandCards,
    ShopPurchase,
    CloseShop,
    ProceedToMap,
    ProceedToNextAct,
}
