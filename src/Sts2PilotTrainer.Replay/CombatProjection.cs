using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// One completed fight, read out of a replay trace as the two projections the
/// product asks for.
///
/// This is the contract <c>docs/comparison-direction.md</c> left to a separate work
/// item. The direction it records is honoured here rather than reinterpreted: the
/// summary and the turn detail are two readings of the same events and they stay
/// apart, the summary carries no chronology, and nothing in either ranks a line or
/// scores an outcome.
///
/// It derives, and it does not replay. Everything below is a difference between two
/// moments the trace already sampled, which is the whole reason the trace keeps both
/// samples either side of every action. Nothing here touches the game assembly, so
/// the contract and its tests outlive a build.
///
/// It computes over a <em>finished</em> fight and refuses anything else. Total turns,
/// health lost and the final health are all defined at the end of a combat, and a
/// projection that quietly reported them for a fight still in progress would be the
/// confident wrong answer this project exists to prevent.
/// </summary>
public sealed record CombatProjection
{
    /// <summary>Which run this fight came out of. Carried so a comparison can name its
    /// two sides without the caller having to keep them straight.</summary>
    [JsonPropertyName("source_id")]
    public required string SourceId { get; init; }

    /// <summary>
    /// The canonical state at combat start, restricted to what identifies the fight.
    ///
    /// Kept because two fights are only comparable if they are the same fight from the
    /// same boundary, and that is a check somebody has to actually make.
    /// </summary>
    [JsonPropertyName("boundary")]
    public required IReadOnlyDictionary<string, string> Boundary { get; init; }

    [JsonPropertyName("summary")]
    public required CombatSummary Summary { get; init; }

    [JsonPropertyName("turns")]
    public required IReadOnlyList<CombatTurn> Turns { get; init; }

    /// <summary>
    /// The canonical fields that identify which fight this is, sampled at combat
    /// start. Enemy fields are numbered and so are selected by suffix.
    /// </summary>
    private static readonly string[] BoundaryFields =
    [
        "combat.encounter", "combat.turn", "combat.hand", "combat.enemy_count",
        "player.hp", "player.max_hp", "player.deck", "player.relics", "player.potions",
    ];

    private static readonly string[] BoundaryEnemySuffixes = [".model", ".max_hp"];

    /// <summary>
    /// Reads one completed fight out of a trace.
    /// </summary>
    /// <exception cref="ManifestException">
    /// When the trace reaches no combat, when its combat never finishes, or when a
    /// step changes the enemy roster in a way the sampled state cannot attribute.
    /// Each of those is refused rather than approximated.
    /// </exception>
    public static CombatProjection FromTrace(string sourceId, ReplayTrace trace)
    {
        var steps = trace.Steps.OrderBy(step => step.Seq).ToList();
        if (steps.Count == 0 || !steps[0].After.ContainsKey("combat.outcome"))
        {
            throw new ManifestException(
                "This trace has no 'combat.outcome' samples, so whether its fight finished cannot be read. " +
                "It was recorded before the canonical state could see the end of a combat; replay the " +
                "manifest again to produce a trace that can be projected.");
        }

        var start = steps.FindIndex(step => Outcome(step.After) == InProgress);
        if (start < 0)
        {
            throw new ManifestException(
                "This history never enters combat, so there is no fight to project. The supported unit is " +
                "a whole fight from combat start.");
        }

        var fight = steps.Skip(start + 1).TakeWhile(step => Outcome(step.Before) == InProgress).ToList();
        if (fight.Count == 0 || Outcome(fight[^1].After) == InProgress)
        {
            throw new ManifestException(
                "This history's combat is still in progress when the history ends, so it has no completed " +
                "fight to project. Total turns, health lost and the final health are all defined at the end " +
                "of a fight; reporting them for one still being fought would be a confident wrong answer.");
        }

        var boundary = steps[start].After;
        var turns = TurnsOf(fight);
        var startingHealth = Int(boundary, "player.hp");
        var finalHealth = Int(fight[^1].After, "player.hp");

        return new CombatProjection
        {
            SourceId = sourceId,
            Boundary = BoundaryOf(boundary),
            Summary = new CombatSummary
            {
                Outcome = Outcome(fight[^1].After),
                // The last turn the fight reached, read from the samples taken inside
                // it. Not the turn counter after it ended, which has no fixed meaning
                // once the engine has stopped advancing it.
                TotalTurns = turns.Count == 0 ? 0 : turns[^1].Turn,
                StartingHealth = startingHealth,
                FinalHealth = finalHealth,
                HealthLost = startingHealth - finalHealth,
                // Deliberately no turn numbers here. The summary answers "what
                // happened in this fight"; the chronology is the other projection's
                // question, and a summary carrying both would make every consumer
                // decide which half to trust.
                ConsumablesUsed = turns.SelectMany(turn => turn.ConsumablesUsed).ToList(),
                // Represented, and not prioritised: permanent removal is rare, it
                // matters when it happens, and no screen is designed around it. Its
                // inputs are sampled either side of every action, so leaving it out
                // here would lose a quantity nothing downstream could recover.
                CardsRemoved = Missing(Sequence(boundary, "player.deck"), Sequence(fight[^1].After, "player.deck")),
            },
            Turns = turns,
        };
    }

    private const string InProgress = "in_progress";

    private static string Outcome(IReadOnlyDictionary<string, string> sample) =>
        sample.GetValueOrDefault("combat.outcome", "none");

    private static IReadOnlyDictionary<string, string> BoundaryOf(IReadOnlyDictionary<string, string> sample) =>
        new SortedDictionary<string, string>(
            sample
                .Where(field => BoundaryFields.Contains(field.Key, StringComparer.Ordinal) ||
                                (field.Key.StartsWith(ReplayTrace.EnemyFieldPrefix, StringComparison.Ordinal) &&
                                 BoundaryEnemySuffixes.Any(suffix =>
                                     field.Key.EndsWith(suffix, StringComparison.Ordinal))))
                .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>
    /// The fight's turns, in order, each with the actions that fell in it and what it
    /// cost either side.
    ///
    /// A step is attributed to the turn its <em>before</em> sample was taken in, so a
    /// step that crosses a turn boundary - ending a turn, which resolves the whole
    /// enemy turn - belongs to the turn the player was in when they took it. That is
    /// what puts an enemy's attack on the turn it answered.
    /// </summary>
    private static List<CombatTurn> TurnsOf(List<ReplayStep> fight)
    {
        var turns = new List<CombatTurn>();
        foreach (var step in fight)
        {
            var number = Int(step.Before, "combat.turn");
            var index = turns.FindIndex(turn => turn.Turn == number);
            if (index < 0)
            {
                turns.Add(new CombatTurn
                {
                    Turn = number,
                    Actions = [],
                    DamageDealt = 0,
                    HealthLost = 0,
                    ConsumablesUsed = [],
                });
                index = turns.Count - 1;
            }

            var potions = Missing(Potions(step.Before), Potions(step.After));
            turns[index] = turns[index] with
            {
                Actions = [.. turns[index].Actions, new TurnAction(step.Seq, step.Verb, step.Args)],
                DamageDealt = turns[index].DamageDealt + DamageDealt(step),
                HealthLost = turns[index].HealthLost +
                             Math.Max(0, Int(step.Before, "player.hp") - Int(step.After, "player.hp")),
                ConsumablesUsed = [.. turns[index].ConsumablesUsed, .. potions],
            };
        }

        return turns;
    }

    /// <summary>
    /// Hit points taken off the enemies over one step.
    ///
    /// Enemies are matched by index, which is only sound while the roster keeps its
    /// shape. The engine removes a dead enemy from the combat state rather than
    /// leaving it at zero health, so the killing step's <em>after</em> sample has
    /// fewer enemies than its <em>before</em>: when they all go, each one's remaining
    /// health is what the step dealt.
    ///
    /// Anything else - a roster that shrinks with survivors left, or one that grows -
    /// re-indexes the enemies, and a delta taken across that re-indexing is a number
    /// with no meaning. Those are refused. Attributing damage across a changing
    /// multi-enemy roster needs something the sampled state does not carry, and
    /// inventing it here is exactly the plausible-looking wrong answer this project
    /// is built to refuse.
    /// </summary>
    private static int DamageDealt(ReplayStep step)
    {
        var before = Int(step.Before, "combat.enemy_count");
        var after = step.After.TryGetValue("combat.enemy_count", out var raw)
            ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        if (after == 0)
        {
            return Enumerable.Range(0, before).Sum(i => Int(step.Before, $"combat.enemy.{i}.hp"));
        }

        if (after != before)
        {
            throw new ManifestException(
                $"Step {step.Seq} ({step.Verb}) leaves {after} of {before} enemies alive. The engine " +
                "re-indexes the survivors, so hit points compared by index across this step would be a " +
                "number about two different enemies. Refusing: attributing damage across a changing " +
                "multi-enemy roster needs state the trace does not sample.");
        }

        var dealt = 0;
        for (var i = 0; i < before; i++)
        {
            var model = step.Before.GetValueOrDefault($"combat.enemy.{i}.model");
            if (model != step.After.GetValueOrDefault($"combat.enemy.{i}.model"))
            {
                throw new ManifestException(
                    $"Step {step.Seq} ({step.Verb}) has a different enemy at index {i} afterwards, so the " +
                    "roster was re-indexed. Refusing to subtract one enemy's hit points from another's.");
            }

            dealt += Math.Max(0, Int(step.Before, $"combat.enemy.{i}.hp") - Int(step.After, $"combat.enemy.{i}.hp"));
        }

        return dealt;
    }

    /// <summary>Entries present in the first sequence and no longer present in the
    /// second, counting duplicates - two Strikes removed is two removals.</summary>
    private static List<string> Missing(IEnumerable<string> before, IEnumerable<string> after)
    {
        var remaining = after.GroupBy(entry => entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var entry in before)
        {
            if (remaining.TryGetValue(entry, out var count) && count > 0) remaining[entry] = count - 1;
            else missing.Add(entry);
        }
        return missing;
    }

    private static IEnumerable<string> Potions(IReadOnlyDictionary<string, string> sample) =>
        Sequence(sample, "player.potions").Where(entry => entry != "empty");

    private static IEnumerable<string> Sequence(IReadOnlyDictionary<string, string> sample, string field) =>
        sample.TryGetValue(field, out var value) && value.Length > 0 ? value.Split('|') : [];

    private static int Int(IReadOnlyDictionary<string, string> sample, string field) =>
        sample.TryGetValue(field, out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ManifestException(
                $"A trace sample carries no readable '{field}'. The projection reads only fields " +
                "ReplayTrace.SampledFields names, so this is a trace that was not sampled as the format says.");
}

/// <summary>
/// What happened in this fight. No chronology: the exact turn something happened is
/// the other projection's answer.
///
/// The health outcome is read at the end of the fight, so it includes whatever
/// resolves as the combat ends. Ironclad's starting relic heals six the moment the
/// last enemy dies, which is why this summary's <see cref="HealthLost"/> can be
/// smaller than the turn detail's health lost added up. They are two different
/// measurements - what the fight cost in the end, and what came off during each turn -
/// and they are not required to agree. Reconciling them by quietly picking one would
/// throw away the difference, which is a real thing about the fight.
/// </summary>
public sealed record CombatSummary
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("total_turns")]
    public required int TotalTurns { get; init; }

    [JsonPropertyName("starting_health")]
    public required int StartingHealth { get; init; }

    [JsonPropertyName("final_health")]
    public required int FinalHealth { get; init; }

    [JsonPropertyName("health_lost")]
    public required int HealthLost { get; init; }

    [JsonPropertyName("consumables_used")]
    public required IReadOnlyList<string> ConsumablesUsed { get; init; }

    [JsonPropertyName("cards_removed")]
    public required IReadOnlyList<string> CardsRemoved { get; init; }
}

/// <summary>
/// One turn of the fight: what was done, what it cost, and what it achieved.
///
/// <see cref="HealthLost"/> is health that actually came off, which is the damage
/// that got through. Damage a block absorbed is not separately recoverable from the
/// sampled state - block is reset at the start of a turn, and the trace samples
/// either side of an action rather than inside one - so this field is named for what
/// it measures rather than for what a reader might hope it measures.
/// </summary>
public sealed record CombatTurn
{
    [JsonPropertyName("turn")]
    public required int Turn { get; init; }

    [JsonPropertyName("actions")]
    public required IReadOnlyList<TurnAction> Actions { get; init; }

    [JsonPropertyName("damage_dealt")]
    public required int DamageDealt { get; init; }

    [JsonPropertyName("health_lost")]
    public required int HealthLost { get; init; }

    [JsonPropertyName("consumables_used")]
    public required IReadOnlyList<string> ConsumablesUsed { get; init; }
}

/// <summary>
/// One action in a turn, in order. Enough for a later read-only walkthrough to step
/// through a solution that has already been computed - it re-solves nothing and
/// resets nothing.
/// </summary>
public sealed record TurnAction(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("verb")] string Verb,
    [property: JsonPropertyName("args")] IReadOnlyDictionary<string, string> Args);
