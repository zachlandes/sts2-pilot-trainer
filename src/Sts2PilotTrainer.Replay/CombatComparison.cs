using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Two completed fights of the same combat, put side by side.
///
/// It states differences and nothing else. There is no score, no ranking, and no
/// verdict about which line was better: what a comparison should <em>say</em> about
/// two lines is an interface question, and a contract that pre-judged it would have
/// to be unpicked before that question could be answered honestly.
///
/// Both sides must be the same fight from the same boundary. That is checked rather
/// than assumed: two fights that began from different hands produce a table of
/// differences that looks perfectly reasonable and means nothing, which is precisely
/// the failure mode worth refusing.
/// </summary>
public sealed record CombatComparison
{
    [JsonPropertyName("left")]
    public required CombatProjection Left { get; init; }

    [JsonPropertyName("right")]
    public required CombatProjection Right { get; init; }

    /// <summary>The combat summary, field by field. No chronology, by construction:
    /// the summary being compared carries none.</summary>
    [JsonPropertyName("summary")]
    public required IReadOnlyList<ComparedField> Summary { get; init; }

    /// <summary>The turn detail, turn by turn. A turn only one side reached is
    /// present with the other side absent, which is itself the difference.</summary>
    [JsonPropertyName("turns")]
    public required IReadOnlyList<ComparedTurn> Turns { get; init; }

    [JsonPropertyName("caveats")]
    public required IReadOnlyList<string> Caveats { get; init; }

    /// <exception cref="ManifestException">
    /// When the two projections are not the same fight from the same boundary.
    /// </exception>
    public static CombatComparison Between(CombatProjection left, CombatProjection right)
    {
        if (!string.Equals(
                left.CombatStartSnapshotDigest,
                right.CombatStartSnapshotDigest,
                StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"'{left.SourceId}' and '{right.SourceId}' have different complete combat-start snapshot " +
                "digests, so they are not the same fight from the same boundary. Comparing them would " +
                "produce differences that mean nothing:\n" +
                $"  combat_start_snapshot_digest: {left.SourceId} has " +
                $"'{left.CombatStartSnapshotDigest}', {right.SourceId} has " +
                $"'{right.CombatStartSnapshotDigest}'");
        }

        var boundary = BoundaryDifferences(left, right);
        if (boundary.Count > 0)
        {
            throw new ManifestException(
                $"'{left.SourceId}' and '{right.SourceId}' are not the same fight from the same boundary, so " +
                "comparing them would produce differences that mean nothing:\n" +
                string.Join("\n", boundary.Select(field =>
                    $"  {field.Field}: {left.SourceId} has '{field.Left}', {right.SourceId} has '{field.Right}'")));
        }

        var summary = new[]
        {
            Compare("outcome", left.Summary.Outcome, right.Summary.Outcome),
            Compare("total_turns", left.Summary.TotalTurns, right.Summary.TotalTurns),
            Compare("starting_health", left.Summary.StartingHealth, right.Summary.StartingHealth),
            Compare("final_health", left.Summary.FinalHealth, right.Summary.FinalHealth),
            Compare("net_health_change", left.Summary.NetHealthChange, right.Summary.NetHealthChange),
            Compare("consumables_used", left.Summary.ConsumablesUsed, right.Summary.ConsumablesUsed),
            Compare("cards_removed", left.Summary.CardsRemoved, right.Summary.CardsRemoved),
        };

        var numbers = left.Turns.Select(turn => turn.Turn)
            .Union(right.Turns.Select(turn => turn.Turn))
            .Order()
            .ToList();

        return new CombatComparison
        {
            Left = left,
            Right = right,
            Summary = summary,
            Turns = numbers
                .Select(number => new ComparedTurn(
                    number,
                    left.Turns.FirstOrDefault(turn => turn.Turn == number),
                    right.Turns.FirstOrDefault(turn => turn.Turn == number)))
                .ToList(),
            Caveats =
            [
                "This states differences. It does not score either line, rank them, or say which was better.",

                "Enemy health lost and player health lost count only health that actually came off. Damage " +
                "either side's block absorbed is not included in those measurements.",

                "The summary's net health change is final health minus starting health: positive is a net " +
                "gain and negative is a net loss. It includes anything that resolves as combat ends. Turn " +
                "detail reports gross player health lost during each turn, so the measurements do not have " +
                "to add up.",

                "Both lines were sampled by the real engine either side of every action, from the same " +
                "combat-start boundary: a recording replayed headlessly, a fight a person played in the retail " +
                "client with the Combat Trainer capturing it, or one of each. Which is which is stated by each " +
                "side's source id, not judged here.",
            ],
        };
    }

    /// <summary>
    /// Where the two fights disagree about what fight they are.
    ///
    /// A field one side carries and the other does not counts as a difference. The
    /// alternative - ignoring it - would let a boundary check pass because a field
    /// was missing, which is the one way a check like this fails silently.
    /// </summary>
    private static IReadOnlyList<ComparedField> BoundaryDifferences(
        CombatProjection left, CombatProjection right)
    {
        var fields = new SortedSet<string>(left.Boundary.Keys, StringComparer.Ordinal);
        fields.UnionWith(right.Boundary.Keys);
        return fields
            .Select(field => new ComparedField(
                field,
                left.Boundary.GetValueOrDefault(field, "<absent>"),
                right.Boundary.GetValueOrDefault(field, "<absent>"),
                string.Equals(
                    left.Boundary.GetValueOrDefault(field),
                    right.Boundary.GetValueOrDefault(field),
                    StringComparison.Ordinal)))
            .Where(field => !field.Matches)
            .ToList();
    }

    private static ComparedField Compare(string field, int left, int right) =>
        Compare(field,
            left.ToString(System.Globalization.CultureInfo.InvariantCulture),
            right.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static ComparedField Compare(string field, IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        Compare(field, string.Join("|", left), string.Join("|", right));

    private static ComparedField Compare(string field, string left, string right) =>
        new(field, left, right, string.Equals(left, right, StringComparison.Ordinal));
}

public sealed record ComparedField(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("left")] string Left,
    [property: JsonPropertyName("right")] string Right,
    [property: JsonPropertyName("matches")] bool Matches);

/// <summary>One turn on both sides. Either side may be absent: a fight that ran
/// longer reached turns the other never did.</summary>
public sealed record ComparedTurn(
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("left")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CombatTurn? Left,
    [property: JsonPropertyName("right")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CombatTurn? Right);
