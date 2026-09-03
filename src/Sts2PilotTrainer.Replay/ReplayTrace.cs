using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What happened during a replay, step by step, kept as data.
///
/// A verified replay's end state answers "did it reproduce the run". It cannot
/// answer the question this project exists to serve next - how a played combat
/// compares with an alternative line - because that question is about the shape of
/// the fight, not its last frame. A final state can retain final health and the last
/// combat turn reached. It cannot recover the starting state and chronology needed
/// for net health change, ordered actions, per-turn health loss, consumable use
/// timing, or permanent card removals.
///
/// So the trace samples the canonical state either side of every action and keeps
/// the samples verbatim. It computes nothing and ranks nothing: <see
/// cref="CombatProjection"/> and <see cref="CombatComparison"/> own those derived
/// readings, and a trace that pre-judged them would have to be unpicked first.
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

    /// <summary>
    /// The part of a canonical state the trace keeps.
    ///
    /// One owner for the filter, whoever is sampling: the headless replay and the
    /// capture of a fight a person plays both read the same projection through this,
    /// so a field cannot be kept by one and dropped by the other.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Sample(IReadOnlyDictionary<string, string> fields) =>
        new SortedDictionary<string, string>(
            fields
                .Where(field => IsSampled(field.Key))
                .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>Whether two samples carry the same fields with the same values.</summary>
    public static bool SameSample(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(field =>
            right.TryGetValue(field.Key, out var value) &&
            string.Equals(field.Value, value, StringComparison.Ordinal));

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
