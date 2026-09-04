using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What a merchant sells, as the shelf a purchase came off.
///
/// Named rather than indexed across the whole shop, because the merchant's inventory
/// is four separate lists plus one card removal, and a single flat position would be
/// a layout detail of a screen rather than anything the engine has. Shared by the
/// validator and the driver so the two cannot disagree about what a kind is.
/// </summary>
public static class ShopPurchaseKinds
{
    public const string CharacterCard = "character_card";
    public const string ColorlessCard = "colorless_card";
    public const string Relic = "relic";
    public const string Potion = "potion";

    /// <summary>The one purchase that buys a service rather than a thing: it opens a
    /// screen over the deck and the card that came off it is recorded separately, as
    /// a card selection.</summary>
    public const string CardRemoval = "card_removal";

    public static readonly string[] All = [CharacterCard, ColorlessCard, Relic, Potion, CardRemoval];

    /// <summary>The argument naming what was bought, for the kinds that buy a thing.</summary>
    public static string? IdArgument(string kind) => kind switch
    {
        CharacterCard or ColorlessCard => "card_id",
        Relic => "relic_id",
        Potion => "potion_id",
        _ => null,
    };
}

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
/// The card-selection actions that answer the screen one action opened.
///
/// The contiguous run of <see cref="ActionVerb.SelectCardFromScreen"/> records
/// immediately after it, which is all the driver ever reads and is where a screen
/// answered inside the call that opened it gets its answer from. One owner because
/// every caller that hands an action a window into the rest of the history - a whole
/// replay, a prefix that stops at a boundary, a walk to one - has to cut that window
/// in the same place: one that stopped short would refuse the screen for an omission
/// the truncation caused rather than one the recording made.
/// </summary>
public static class CardScreenAnswers
{
    public static IReadOnlyList<ActionRecord> After(IEnumerable<ActionRecord> actions, int seq) =>
        actions
            .OrderBy(action => action.Seq)
            .SkipWhile(action => action.Seq <= seq)
            .TakeWhile(action => action.Verb == ActionVerb.SelectCardFromScreen)
            .ToList();
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

    /// <summary>Take the relic a treasure chest offered. Args: <c>relic_id</c>,
    /// <c>option_index</c>.</summary>
    TakeChestRelic,

    /// <summary>Leave a treasure chest's relic behind. Written down because the
    /// engine discards it either way, so an omitted action would replay exactly like
    /// a declined one.</summary>
    SkipChestRelic,

    ChooseRestSiteOption,

    /// <summary>Drink one potion off the belt. Args: <c>potion_id</c>,
    /// <c>slot_index</c>, and <c>target_index</c> when choosing among multiple
    /// enemies.</summary>
    UsePotion,

    /// <summary>Throw one potion away to make room. Args: <c>potion_id</c>,
    /// <c>slot_index</c>.</summary>
    DiscardPotion,

    /// <summary>Buy one thing from the merchant. Args: <c>kind</c>, and for
    /// everything but a card removal an <c>option_index</c> and the id of what was
    /// bought. See <see cref="ShopPurchaseKinds"/>.</summary>
    ShopPurchase,

    ProceedToNextAct,

    // ── Named, and this build maps nothing onto them; see EngineCommands ──
    SelectHandCards,
    CloseShop,
    ProceedToMap,
}
