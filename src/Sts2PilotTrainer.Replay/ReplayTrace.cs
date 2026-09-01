using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What happened during a replay, step by step, kept as data.
///
/// A verified replay's end state answers "did it reproduce the run". It cannot
/// answer the question this project exists to serve next - how a played combat
/// compares with an alternative line - because that question is about the shape of
/// the fight, not its last frame. Total turns, health lost, which consumable was
/// drunk on which turn, enemy and player health lost each turn, and cards removed for
/// good are all differences between two moments, and a report that keeps only the
/// final moment has thrown every one of them away.
///
/// So the trace samples the canonical state either side of every action and keeps
/// the samples verbatim. It computes nothing and ranks nothing: what a comparison
/// should say about two lines is a contract nobody has written yet, and a trace
/// that pre-judged it would have to be unpicked before that contract could be.
///
/// The samples are drawn from <see cref="CanonicalState"/>, so the trace and the
/// verification are reading the same engine state through the same projection, and
/// a field cannot mean one thing in a checkpoint and another here.
/// </summary>
public sealed record ReplayTrace
{
    /// <summary>
    /// The canonical fields sampled either side of each action.
    ///
    /// Named explicitly rather than "everything", because a trace that grew with the
    /// projection would silently become an artifact nobody could read. Each entry is
    /// here because a listed derivation needs it; adding a derivation means adding
    /// its inputs here, deliberately.
    ///
    /// Enemy fields are indexed and so cannot be listed by name; they are matched by
    /// the <c>combat.enemy.</c> prefix instead.
    /// </summary>
    public static readonly IReadOnlyList<string> SampledFields =
    [
        "combat.in_progress",
        "combat.outcome",
        "combat.turn",
        "combat.round",
        "combat.encounter",
        "combat.energy",
        "combat.block",
        "combat.player_hp",
        "combat.player_powers",
        "combat.enemy_count",
        "combat.hand",
        "player.hp",
        "player.max_hp",
        "player.gold",
        "player.deck",
        "player.relics",
        "player.potions",
        "run.act_floor",
        "run.total_floor",
        "run.is_game_over",
    ];

    /// <summary>Per-enemy fields are numbered, so they are selected by prefix.</summary>
    public const string EnemyFieldPrefix = "combat.enemy.";

    /// <summary>Whether a canonical field belongs in a trace sample.</summary>
    public static bool IsSampled(string field) =>
        SampledFields.Contains(field, StringComparer.Ordinal) ||
        field.StartsWith(EnemyFieldPrefix, StringComparison.Ordinal);

    [JsonPropertyName("steps")]
    public required IReadOnlyList<ReplayStep> Steps { get; init; }
}

/// <summary>
/// One action and the state either side of it.
///
/// Both samples are kept rather than only the later one: the difference is the
/// event, and reconstructing it from a chain of afters would break the moment a
/// step is skipped, refused or replayed from a snapshot.
/// </summary>
public sealed record ReplayStep
{
    /// <summary>The action's position in the history, or -1 for the sample taken
    /// before any action ran.</summary>
    [JsonPropertyName("seq")]
    public required int Seq { get; init; }

    [JsonPropertyName("verb")]
    public required string Verb { get; init; }

    [JsonPropertyName("args")]
    public IReadOnlyDictionary<string, string> Args { get; init; } =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("before")]
    public required IReadOnlyDictionary<string, string> Before { get; init; }

    [JsonPropertyName("after")]
    public required IReadOnlyDictionary<string, string> After { get; init; }
}
