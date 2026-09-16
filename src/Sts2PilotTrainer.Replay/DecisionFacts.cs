using System.Globalization;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A place a decision can be made, as the coverage number counts it: a kind, and the
/// identity within that kind.
///
/// The recorder's second number asks, for every point the game can offer, how many
/// recordings exercise it. What the game can offer is walked off the assembly by
/// <c>DecisionSurface</c> in the engine; what a recording exercised is projected off
/// its history by <see cref="DecisionFacts"/> here, with no game. Both answer in this
/// vocabulary, which is why it lives in the format rather than in either.
/// </summary>
/// <param name="Kind">One of <see cref="DecisionKinds.All"/>.</param>
/// <param name="Identity">The point within the kind: a verb's name, a reward kind, an
/// option id, an event id, an entry point's signature.</param>
public sealed record DecisionPoint(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("identity")] string Identity)
{
    public override string ToString() => $"{Kind}  {Identity}";
}

/// <summary>
/// The kinds of decision point, and which of them a recording in this format can be
/// projected to.
///
/// A kind is projectable when the manifest's own arguments name the point: the verb
/// itself, and the argument the validator already requires of the five verbs whose
/// discriminator is a fact about the run. A card prompt is not: the format records
/// which position was picked in the list the prompt offered and not which entry point
/// asked, so every card-prompt point is reported as one this format cannot count, by
/// name, rather than as covered or uncovered. A net action is not either, until the
/// claims table that maps a verb onto the game action it goes through exists.
/// </summary>
public static class DecisionKinds
{
    public const string Verb = "verb";
    public const string RewardKind = "reward-kind";
    public const string CardRewardAlternative = "card-reward-alternative";
    public const string ShopKind = "shop-kind";
    public const string RestOption = "rest-option";
    public const string Event = "event";
    public const string CardPrompt = "card-prompt";
    public const string NetAction = "net-action";

    /// <summary>Every kind, in the order the coverage report lists them.</summary>
    public static readonly string[] All =
        [Verb, RewardKind, CardRewardAlternative, ShopKind, RestOption, Event, CardPrompt, NetAction];

    /// <summary>The kinds a recording in this format projects to.</summary>
    public static readonly string[] Projectable =
        [Verb, RewardKind, CardRewardAlternative, ShopKind, RestOption, Event];

    public static bool IsProjectable(string kind) => Projectable.Contains(kind, StringComparer.Ordinal);

    /// <summary>Why a kind cannot be projected from this format, for the two that
    /// cannot; null for the rest.</summary>
    public static string? NotProjectableBecause(string kind) => kind switch
    {
        CardPrompt =>
            "not projectable from this format: a card selection records the position picked in the list " +
            "the prompt offered, not which entry point asked",
        NetAction =>
            "not projectable from this format: which game action a decision went through is the claims " +
            "table's knowledge, which this build does not carry",
        _ => null,
    };
}

/// <summary>
/// The decision points a recording exercised, read off its history and nothing else.
///
/// Every action projects to its verb. Five verbs also project to the point their own
/// argument names - the argument the validator requires of them, so a manifest that
/// validates always projects cleanly: a claimed reward to its kind, a card taken to the
/// card reward kind (it is the one reward not claimed through <c>ClaimReward</c>), an
/// alternative to its id, a purchase to its shelf, a rest to its option, an event
/// option to its event. An action missing that argument projects to its verb alone,
/// because this reader validates nothing and a coverage count is not a place to refuse.
/// </summary>
public static class DecisionFacts
{
    public static IReadOnlySet<DecisionPoint> Of(ReplayManifest manifest)
    {
        var points = new HashSet<DecisionPoint>();
        foreach (var action in manifest.Actions)
        {
            points.Add(new DecisionPoint(DecisionKinds.Verb, action.Verb.ToString()));
            switch (action.Verb)
            {
                case ActionVerb.ClaimReward:
                    Add(points, DecisionKinds.RewardKind, action, "reward_type");
                    break;
                case ActionVerb.TakeCard:
                    points.Add(new DecisionPoint(DecisionKinds.RewardKind, CardRewardKind));
                    break;
                case ActionVerb.TakeCardRewardAlternative:
                    Add(points, DecisionKinds.CardRewardAlternative, action, "option_id");
                    break;
                case ActionVerb.ShopPurchase:
                    Add(points, DecisionKinds.ShopKind, action, "kind");
                    break;
                case ActionVerb.ChooseRestSiteOption:
                    Add(points, DecisionKinds.RestOption, action, "option_id");
                    break;
                case ActionVerb.ChooseEventOption:
                    Add(points, DecisionKinds.Event, action, "event_id");
                    break;
            }
        }

        return points;
    }

    /// <summary>The reward kind a card reward is counted under: the format names it
    /// in a description only, and the engine's <c>LootRewards.KindOf</c> spells it
    /// the same way.</summary>
    public const string CardRewardKind = "card";

    private static void Add(HashSet<DecisionPoint> points, string kind, ActionRecord action, string argument)
    {
        if (action.Args.TryGetValue(argument, out var identity) && identity.Length > 0)
        {
            points.Add(new DecisionPoint(kind, identity));
        }
    }
}

/// <summary>
/// The coverage number: every point the game offers, with how many recordings reach
/// it, computed over a denominator somebody else walked and a corpus somebody else
/// projected. Pure, so it is held on inputs written by hand.
/// </summary>
public static class DecisionCoverage
{
    public static CoverageReport Over(
        IReadOnlyList<DecisionPoint> denominator,
        IReadOnlyDictionary<DecisionPoint, string> excusals,
        IReadOnlyList<CoveredRecording> recordings)
    {
        var counts = new Dictionary<DecisionPoint, int>();
        foreach (var recording in recordings)
        {
            foreach (var point in recording.Points)
            {
                counts[point] = counts.GetValueOrDefault(point) + 1;
            }
        }

        var rows = denominator
            .Select(point =>
            {
                var count = counts.GetValueOrDefault(point);
                var notProjectable = DecisionKinds.NotProjectableBecause(point.Kind);
                var excused = excusals.TryGetValue(point, out var reason) ? reason : null;
                var state = notProjectable is not null ? CoverageState.NotProjectable
                    : count > 0 ? CoverageState.Covered
                    : excused is not null ? CoverageState.Excused
                    : CoverageState.Uncovered;
                return new CoverageRow(point, count, state, state == CoverageState.Excused ? excused : null);
            })
            .ToList();

        // A point a recording reached that no walk produced is a finding about the
        // walk or the format, never silently dropped: it is listed after the
        // denominator under its own heading.
        var known = denominator.ToHashSet();
        var outside = counts.Keys
            .Where(point => !known.Contains(point))
            .OrderBy(point => Array.IndexOf(DecisionKinds.All, point.Kind))
            .ThenBy(point => point.Identity, StringComparer.Ordinal)
            .Select(point => new CoverageRow(point, counts[point], CoverageState.OutsideTheDenominator, null))
            .ToList();

        // An excusal is stale two ways: a recording has reached its point, or no walk
        // produces the point any more. Either is named so it comes out
        var staleExcusals = excusals.Keys
            .Where(point => counts.GetValueOrDefault(point) > 0 || !known.Contains(point))
            .OrderBy(point => Array.IndexOf(DecisionKinds.All, point.Kind))
            .ThenBy(point => point.Identity, StringComparer.Ordinal)
            .ToList();

        return new CoverageReport(rows, outside, staleExcusals, recordings.Count);
    }
}

/// <summary>One recording's projection, named so a row can say who reached it.</summary>
public sealed record CoveredRecording(string RunId, IReadOnlySet<DecisionPoint> Points);

public enum CoverageState
{
    Covered,
    Uncovered,
    Excused,
    NotProjectable,
    OutsideTheDenominator,
}

public sealed record CoverageRow(DecisionPoint Point, int Recordings, CoverageState State, string? Excuse)
{
    /// <summary>The row as the report prints it.</summary>
    public string Describe() => State switch
    {
        CoverageState.Covered =>
            $"{Point}  {Recordings.ToString(CultureInfo.InvariantCulture)} recording(s)",
        CoverageState.Uncovered => $"{Point}  uncovered",
        CoverageState.Excused => $"{Point}  excused: {Excuse}",
        CoverageState.NotProjectable => $"{Point}  {DecisionKinds.NotProjectableBecause(Point.Kind)}",
        CoverageState.OutsideTheDenominator =>
            $"{Point}  {Recordings.ToString(CultureInfo.InvariantCulture)} recording(s), and no walk of " +
            "this build produced the point",
        _ => throw new ArgumentOutOfRangeException(nameof(State), State, "unknown coverage state"),
    };
}

/// <summary>What <see cref="DecisionCoverage.Over"/> found.</summary>
/// <param name="Rows">One per point of the denominator, in the denominator's order.</param>
/// <param name="OutsideTheDenominator">Points a recording reached that no walk produced.</param>
/// <param name="StaleExcusals">Excusals to take out: a recording has reached the point, or no walk produces it.</param>
/// <param name="Recordings">How many recordings were projected.</param>
public sealed record CoverageReport(
    IReadOnlyList<CoverageRow> Rows,
    IReadOnlyList<CoverageRow> OutsideTheDenominator,
    IReadOnlyList<DecisionPoint> StaleExcusals,
    int Recordings)
{
    public int Points => Rows.Count;
    public int Covered => Count(CoverageState.Covered);
    public int Uncovered => Count(CoverageState.Uncovered);
    public int Excused => Count(CoverageState.Excused);
    public int NotProjectable => Count(CoverageState.NotProjectable);

    /// <summary>Whether the bar holds: no point is uncovered, no recording reached a
    /// point outside the denominator, and no excusal is stale.</summary>
    public bool Holds => Uncovered == 0 && OutsideTheDenominator.Count == 0 && StaleExcusals.Count == 0;

    private int Count(CoverageState state) => Rows.Count(row => row.State == state);

    /// <summary>The totals, as the report's last lines before the verdict.</summary>
    public IEnumerable<string> Totals()
    {
        var n = (int value) => value.ToString(CultureInfo.InvariantCulture);
        yield return $"points: {n(Points)}  covered: {n(Covered)}  excused: {n(Excused)}  uncovered: {n(Uncovered)}  " +
                     $"not projectable: {n(NotProjectable)}  recordings: {n(Recordings)}";
        if (OutsideTheDenominator.Count > 0)
        {
            yield return $"outside the denominator: {n(OutsideTheDenominator.Count)}";
        }

        if (StaleExcusals.Count > 0)
        {
            yield return $"stale excusals: {n(StaleExcusals.Count)}";
        }
    }
}
