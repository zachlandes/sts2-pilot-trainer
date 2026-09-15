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
        // Where on the map the run stands. A floor arrival is proved by the coordinate
        // as well as the floor, so a recorder that sampled only the floor wrote
        // recordings nobody could be stood on a floor of.
        "run.map_coord",
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

    /// <summary>
    /// The part of a sample the game's own save can carry.
    ///
    /// A finished fight stays on the player until the next fight replaces it, and
    /// <c>CanonicalStateProjection</c> keeps projecting it, so every reading taken at
    /// a shop, a rest site, an event or a loot screen after a fight carries that
    /// fight's residue. <c>SerializableRun</c> has no combat member, so a run
    /// continued from the game's save comes back without it, and a live fight is the
    /// one combat state the save has an answer for: it is rolled back to the room's
    /// entry, where the fight opens again. So every <c>combat.</c> field is dropped
    /// unless the fight is in progress, and nothing else is: what a save carries of
    /// the run - floor, coordinate, health, gold, deck, relics, potions - is kept
    /// whole. Two samples are compared through <see cref="SameSample"/>; the same
    /// filter over the complete canonical fields is digested by
    /// <see cref="SaveRepresentableDigest"/>, under its own prefix so it cannot be
    /// mistaken for the complete digest a boundary is identified by.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SaveRepresentable(IReadOnlyDictionary<string, string> sample)
    {
        var inProgress = string.Equals(
            sample.GetValueOrDefault("combat.outcome"), "in_progress", StringComparison.Ordinal);
        return new SortedDictionary<string, string>(
            sample
                .Where(field => inProgress || !field.Key.StartsWith("combat.", StringComparison.Ordinal))
                .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>What a save-representable digest begins with, so no reader can take
    /// one for a complete digest.</summary>
    public const string SaveRepresentableDigestPrefix = "sha256-sr:";

    /// <summary>
    /// The digest of everything in a complete canonical state the game's own save
    /// can carry: the same filter as <see cref="SaveRepresentable"/>, over every
    /// projected field rather than the sampled ones, rendered and hashed the way
    /// <see cref="CanonicalState"/> renders and hashes.
    ///
    /// A sample cannot see a random stream's position or the draw order, so two
    /// readings whose samples agree can still be two moments - an event page turned,
    /// a stream consumed - and a resume that compared samples alone would read the
    /// game's rollback of the page as nothing having happened. This is what a
    /// recorder writes beside each decision so the resume after it can ask the exact
    /// question: the same state, but for a finished fight's residue.
    /// </summary>
    public static string SaveRepresentableDigest(IReadOnlyDictionary<string, string> fields)
    {
        var rendering = new System.Text.StringBuilder();
        foreach (var (key, value) in SaveRepresentable(fields))
        {
            rendering.Append(key).Append('=').Append(value).Append('\n');
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(rendering.ToString());
        return SaveRepresentableDigestPrefix +
               Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    /// <summary>
    /// Whether a sample carries a finished fight: a combat outcome that is neither a
    /// fight in progress nor the projection's "none" for a run with no combat state
    /// at all. This is the residue <see cref="SaveRepresentable"/> takes away.
    /// </summary>
    public static bool CarriesFinishedCombat(IReadOnlyDictionary<string, string> sample) =>
        sample.TryGetValue("combat.outcome", out var outcome) &&
        !string.Equals(outcome, "in_progress", StringComparison.Ordinal) &&
        !string.Equals(outcome, "none", StringComparison.Ordinal);

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
