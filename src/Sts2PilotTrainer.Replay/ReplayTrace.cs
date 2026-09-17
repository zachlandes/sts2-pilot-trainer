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
        EndedOnSideField,
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
        // Where on the map the run stands. A floor arrival is proved by the coordinate
        // as well as the floor, so a recorder that sampled only the floor wrote
        // recordings nobody could be stood on a floor of.
        "run.map_coord",
        "run.is_game_over",
    ];

    /// <summary>Per-enemy fields are numbered, so they are selected by prefix.</summary>
    public const string EnemyFieldPrefix = "combat.enemy.";

    /// <summary>
    /// Which side's turn a finished fight ended in, <c>player</c> or <c>enemy</c>: the
    /// one reading of a finished fight a trace keeps, and the one sampled field the
    /// digest does not hash. The game's save carries no combat state, so a run
    /// continued onto the loot screen has no such reading where the run that was
    /// never quit does; a comparison that holds two hosts to one state therefore
    /// leaves it out, as <see cref="HeldToTheDigest"/> says, and the step that ends a
    /// fight reads it to tell a kill from a flight.
    /// </summary>
    public const string EndedOnSideField = "combat.ended_on_side";

    /// <summary>Whether a sampled field is one two hosts have to agree on: every
    /// field but the ones the projection keeps outside the digest.</summary>
    public static bool HeldToTheDigest(string field) =>
        !string.Equals(field, EndedOnSideField, StringComparison.Ordinal);

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

    /// <summary>
    /// The fields in which two samples differ, one line each as
    /// <c>field: left -> right</c>, ordered by field; an absent field reads as
    /// <c>absent</c>. For a log line or a failure message, never for a comparison.
    /// </summary>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Keys.Union(right.Keys, StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .Where(field =>
                !left.TryGetValue(field, out var l) || !right.TryGetValue(field, out var r) ||
                !string.Equals(l, r, StringComparison.Ordinal))
            .Select(field =>
                $"{field}: {(left.TryGetValue(field, out var l) ? l : "absent")} -> " +
                $"{(right.TryGetValue(field, out var r) ? r : "absent")}")
            .ToList();

    /// <summary>Whether two samples read the same state: the same fields with the
    /// same values, over the fields two hosts have to agree on.</summary>
    public static bool SameSample(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var held = left.Keys.Union(right.Keys, StringComparer.Ordinal).Where(HeldToTheDigest);
        return held.All(field =>
            left.TryGetValue(field, out var l) && right.TryGetValue(field, out var r) &&
            string.Equals(l, r, StringComparison.Ordinal));
    }

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

    /// <summary>
    /// The complete canonical state digest either side of the step, where the host
    /// that produced the trace took one.
    ///
    /// A sample keeps the fields a comparison reads; the digest covers the draw order
    /// and every random stream's position besides. Two steps whose samples agree and
    /// whose digests do not have diverged in hidden state, which is what
    /// <see cref="TraceParity"/> reports by decision. Absent on a trace written before
    /// the digests were kept, and absent from a captured fight, whose boundary digest
    /// binds it instead; a comparison holds the digests only where both sides carry
    /// one.
    /// </summary>
    [JsonPropertyName("before_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeforeDigest { get; init; }

    [JsonPropertyName("after_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AfterDigest { get; init; }

    /// <summary>Whether this step began inside a live fight and settled with it
    /// over: the killing play, whose settled reading the retail client rolls the
    /// rewards after, on its own clock. One reading of that rule, asked by the
    /// recorder's resume and by <see cref="TraceParity"/> alike.</summary>
    [JsonIgnore]
    public bool EndsAFight => InALiveFight(Before) && !InALiveFight(After);

    /// <summary>Whether a sample was read inside a fight still being fought.</summary>
    public static bool InALiveFight(IReadOnlyDictionary<string, string> sample) =>
        string.Equals(sample.GetValueOrDefault("combat.outcome", "none"), "in_progress", StringComparison.Ordinal);

    /// <summary>
    /// Reserved marker for a step that happened and was then unwound.
    ///
    /// Absent by default and read by nothing. Native mid-fight rollbacks instead keep
    /// each branch in <see cref="NativeSource.Discarded"/>, with its own trace, because
    /// that evidence must replay separately from the continued history.
    /// </summary>
    [JsonPropertyName("discarded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Discarded { get; init; }
}
