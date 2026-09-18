using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
/// option id, an event id, an event's option key under its event, a seam at its
/// timing class, an entry point's signature.</param>
public sealed record DecisionPoint(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("identity")] string Identity)
{
    public override string ToString() => $"{Kind}  {Identity}";

    /// <summary>An event's option as a point: the event's id and the option's key,
    /// which is the relic id where the option deals a relic and the option's own
    /// text key otherwise - the one spelling the driver checks and the recorder
    /// writes as <c>option_key</c>.</summary>
    public static DecisionPoint EventOption(string eventId, string optionKey) =>
        new(DecisionKinds.EventOption, $"{eventId} {optionKey}");

    /// <summary>A seam at a timing class as a point: the way a decision reaches the
    /// game, at the game hook the producing content runs in.</summary>
    public static DecisionPoint Seam(string seam, string timing) =>
        new(DecisionKinds.Seam, $"{seam} @ {timing}");
}

/// <summary>
/// The kinds of decision point, and which of them a recording in this format can be
/// projected to.
///
/// A kind is projectable when the manifest's own arguments name the point: the verb
/// itself, the argument the validator already requires of the five verbs whose
/// discriminator is a fact about the run, and the option key a native recording
/// carries on every event option. A seam is reached by co-occurrence rather than
/// projected: a recording that holds one of the seam's producers in its sampled state
/// and answers the seam's own decision has exercised the seam's recorder and driver
/// path, and the printed word for that is co-occurrence, never covered, because the
/// format does not record which producer opened the decision. A card prompt is not
/// projectable: the format records which position was picked in the list the prompt
/// offered and not which entry point asked, so every card-prompt point is reported as
/// one this format cannot count, by name, rather than as covered or uncovered. A net
/// action is not either, until the claims table that maps a verb onto the game action
/// it goes through exists.
/// </summary>
public static class DecisionKinds
{
    public const string Verb = "verb";
    public const string RewardKind = "reward-kind";
    public const string CardRewardAlternative = "card-reward-alternative";
    public const string ShopKind = "shop-kind";
    public const string RestOption = "rest-option";
    public const string Event = "event";
    public const string EventOption = "event-option";
    public const string Seam = "seam";
    public const string CardPrompt = "card-prompt";
    public const string NetAction = "net-action";

    /// <summary>Every kind, in the order the coverage report lists them.</summary>
    public static readonly string[] All =
        [Verb, RewardKind, CardRewardAlternative, ShopKind, RestOption, Event, EventOption, Seam, CardPrompt, NetAction];

    /// <summary>The kinds a recording in this format projects to.</summary>
    public static readonly string[] Projectable =
        [Verb, RewardKind, CardRewardAlternative, ShopKind, RestOption, Event, EventOption];

    public static bool IsProjectable(string kind) => Projectable.Contains(kind, StringComparer.Ordinal);

    /// <summary>The one kind a recording reaches by co-occurrence rather than by an
    /// argument of its own.</summary>
    public static bool IsReachedByCoOccurrence(string kind) => kind == Seam;

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
/// option to its event. An event option and a Neow blessing that carry the option key
/// a native recording names project to the event's option as well, under the event's
/// id and under Neow's for the blessing, whose verb carries no event id. An action
/// missing that argument projects to its verb alone, because this reader validates
/// nothing and a coverage count is not a place to refuse.
///
/// The decisions on a discarded branch - a reward claimed before the game's own
/// rollback undid it - are projected beside the continued history: the player reached
/// that point and chose, and the replay holds the branch as it holds the history.
/// </summary>
public static class DecisionFacts
{
    /// <summary>The event id a Neow blessing's option is counted under: the verb
    /// carries none, and the model database names the ancient this way.</summary>
    public const string NeowEventId = "EVENT.NEOW";

    public static IReadOnlySet<DecisionPoint> Of(ReplayManifest manifest)
    {
        var points = new HashSet<DecisionPoint>();
        foreach (var action in Actions(manifest))
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
                    if (Argument(action, "event_id") is { } eventId && Argument(action, "option_key") is { } key)
                    {
                        points.Add(DecisionPoint.EventOption(eventId, key));
                    }

                    break;
                case ActionVerb.ChooseNeowBlessing:
                    if (Argument(action, "option_key") is { } blessing)
                    {
                        points.Add(DecisionPoint.EventOption(NeowEventId, blessing));
                    }

                    break;
            }
        }

        return points;
    }

    /// <summary>
    /// Every model a recording met, by id, read off the same history and its sampled
    /// state: the relics, deck and potions its checkpoints expect, the powers and
    /// enemies its fights sampled, and the card, relic, potion or event each action
    /// names. This is the co-occurrence side of a seam: a recording that met a seam's
    /// producer and answered the seam's decision exercised the seam. It reads ids by
    /// their spelling and not by which field held them, because a coverage count
    /// asks whether the producer was there at all, not where.
    /// </summary>
    public static IReadOnlySet<string> ModelsMet(ReplayManifest manifest)
    {
        var models = new HashSet<string>(StringComparer.Ordinal);
        foreach (var checkpoint in manifest.Checkpoints)
        {
            foreach (var expectation in checkpoint.Expect.Values)
            {
                AddIds(models, expectation.Value);
            }
        }

        foreach (var action in Actions(manifest))
        {
            foreach (var argument in action.Args.Values)
            {
                AddIds(models, argument);
            }
        }

        return models;
    }

    /// <summary>The reward kind a card reward is counted under: the format names it
    /// in a description only, and the engine's <c>LootRewards.KindOf</c> spells it
    /// the same way.</summary>
    public const string CardRewardKind = "card";

    private static IEnumerable<ActionRecord> Actions(ReplayManifest manifest)
    {
        var branches = manifest.Source.Native?.Discarded ?? [];
        return manifest.Actions.Concat(branches.SelectMany(branch => branch.Actions));
    }

    private static string? Argument(ActionRecord action, string argument) =>
        action.Args.TryGetValue(argument, out var value) && value.Length > 0 ? value : null;

    private static void Add(HashSet<DecisionPoint> points, string kind, ActionRecord action, string argument)
    {
        if (Argument(action, argument) is { } identity)
        {
            points.Add(new DecisionPoint(kind, identity));
        }
    }

    // A model id as the game spells one: its family and its entry, which is how a
    // sampled deck lists a card before its upgrade mark and how an argument names a
    // relic
    private static readonly Regex ModelId = new(
        @"\b(CARD|RELIC|POTION|EVENT|MONSTER|POWER|ENCOUNTER|MODIFIER)\.[A-Z0-9_]+", RegexOptions.CultureInvariant);

    private static void AddIds(HashSet<string> models, string? value)
    {
        if (value is null) return;
        foreach (Match match in ModelId.Matches(value))
        {
            models.Add(match.Value);
        }
    }
}

/// <summary>
/// Why a point the build offers is allowed to stay unreached by the committed
/// corpus, as a class the map can be held to.
///
/// The first seven are derived: the coverage map says, from the game assembly and the
/// host's own tables, whether each is admissible for a point, and an excusal claiming
/// one the map does not admit fails the bar. The last two are held by something
/// else - a merge-gate row, or nothing yet - and are admissible only where no derived
/// class is, because a point with no producer on this build is not one a row will
/// ever reach and a placeholder there would be a sentence about the wrong thing.
/// </summary>
public enum ExcusalClass
{
    /// <summary>Nothing on this build produces the point: no content model reaches its
    /// seam and no engine path constructs it. A game update that adds a producer turns
    /// the excusal stale by itself.</summary>
    NoProducerOnThisBuild,

    /// <summary>Offered only to a run with more than one player, which the recorder
    /// never records: constructed under the game's own player-count branch, or a verb
    /// the client offers only while another player has not acted.</summary>
    MultiplayerOnly,

    /// <summary>Answered on a screen the headless host has none for and stands in for
    /// at the prompt; no generated walk can draw it.</summary>
    ScreenWithoutHeadlessHost,

    /// <summary>Reachable only in a window the retail client's own timing leaves open,
    /// which the headless host runs inside another decision. Derived for no point on
    /// v0.111.0: the one verb it was thought to class is offered to another player and
    /// never to a singleplayer run, so it is multiplayer only.</summary>
    RetailOnlyTiming,

    /// <summary>An answer no recording of which can replay on this build: either the
    /// driver refuses it by name, because what the recording would have to carry after
    /// it is a decision the format has no record of (the reroll), or the key the
    /// recorder writes is one the driver never matches (an event option keyed by a
    /// LocString's raw text, the Doll Room's dolls).</summary>
    NotReplayable,

    /// <summary>The victory room's own event and its options: reached by the win and
    /// by no act's roll, so it is neither on any act's list nor without a producer.
    /// Every won run's last decisions are its lines and its PROCEED.</summary>
    ReachedByTheWin,

    /// <summary>An event option constructed with no work - the locked form an event
    /// offers where the player cannot afford the real one - whose button refuses the
    /// press (<c>NEventOptionButton.OnRelease</c>): it is offered and never chosen, so
    /// no recording of any play can carry it.</summary>
    NotChoosable,

    /// <summary>Reached by a generated recording on every merge, through the real
    /// recorder and replayed to parity; the recording is generated rather than
    /// committed, which is why the point is excused rather than counted.</summary>
    Generated,

    /// <summary>No committed recording reaches it and no generated walk yet does: a
    /// placeholder the retail soak or a later walk retires, and not an acceptable
    /// excuse at release for a point a player can reach.</summary>
    NotOnTheRoute,
}

public static class ExcusalClasses
{
    /// <summary>The classes the map derives; an excusal claiming one is held to it.</summary>
    public static readonly ExcusalClass[] Derived =
    [
        ExcusalClass.NoProducerOnThisBuild, ExcusalClass.MultiplayerOnly, ExcusalClass.ScreenWithoutHeadlessHost,
        ExcusalClass.RetailOnlyTiming, ExcusalClass.NotReplayable, ExcusalClass.ReachedByTheWin, ExcusalClass.NotChoosable,
    ];

    /// <summary>The classes admissible where the map derives none: held by a row, or
    /// by nothing yet.</summary>
    public static readonly ExcusalClass[] Undeclared = [ExcusalClass.Generated, ExcusalClass.NotOnTheRoute];

    public static bool IsDerived(ExcusalClass excusalClass) => Derived.Contains(excusalClass);

    /// <summary>The class as the record and the report print it.</summary>
    public static string Name(ExcusalClass excusalClass) => excusalClass switch
    {
        ExcusalClass.NoProducerOnThisBuild => "no-producer-on-this-build",
        ExcusalClass.MultiplayerOnly => "multiplayer-only",
        ExcusalClass.ScreenWithoutHeadlessHost => "screen-without-headless-host",
        ExcusalClass.RetailOnlyTiming => "retail-only-timing",
        ExcusalClass.NotReplayable => "not-replayable",
        ExcusalClass.ReachedByTheWin => "reached-by-the-win",
        ExcusalClass.NotChoosable => "not-choosable",
        ExcusalClass.Generated => "generated",
        ExcusalClass.NotOnTheRoute => "not-on-the-route",
        _ => throw new ArgumentOutOfRangeException(nameof(excusalClass), excusalClass, "unknown excusal class"),
    };
}

/// <summary>An excusal: its class, which the map is held to, and the reason a person
/// wrote, which is the whole of what the class cannot say.</summary>
public sealed record Excusal(ExcusalClass Class, string Reason)
{
    /// <summary>The excusal as the record and the report print it.</summary>
    public string Describe() => $"excused [{ExcusalClasses.Name(Class)}]: {Reason}";
}

/// <summary>
/// One seam at one timing class, with the content that produces it: a row of the
/// producer map, which the engine walks off the game assembly and the coverage number
/// reads for co-occurrence.
/// </summary>
/// <param name="Seam">The way a decision reaches the game: a reward kind constructed,
/// a rest option, a shop shelf, an event option, a card-reward alternative, a prompt
/// entry point, a screen shown, a reward set offered.</param>
/// <param name="Timing">The game hook the producer's own code runs in when it reaches
/// the seam - the base member it overrides - which is when the recorder and the driver
/// meet the decision; two producers of one seam at one timing exercise the same path.</param>
/// <param name="Producers">Every model that reaches the seam at that timing, by id, in
/// the order the walk lists them.</param>
/// <param name="AnsweredAt">The decision points the seam is answered at in this
/// format: the points a recording that exercised the seam projects to.</param>
public sealed record ProducerSeam(
    string Seam, string Timing, IReadOnlyList<string> Producers, IReadOnlyList<DecisionPoint> AnsweredAt)
{
    public DecisionPoint Point => DecisionPoint.Seam(Seam, Timing);
}

/// <summary>
/// The coverage number: every point the game offers, with how many recordings reach
/// it, computed over a denominator somebody else walked and a corpus somebody else
/// projected. Pure, so it is held on inputs written by hand.
///
/// A recording credits a point only where <see cref="RecordingStanding"/> says it
/// holds. One made on another build, or one the recorder marked broken, unmapped or
/// non-standard, is projected all the same and tallied apart, as reached and
/// unverified: a point only such a recording reaches stays uncovered, or excused, and
/// the tally is printed beside it so the corpus is not read as shorter than it is. A
/// manifest this build cannot read projects nothing and is listed by name for the
/// same reason.
///
/// A seam point is credited by co-occurrence: a crediting recording that met one of
/// the seam's producers and answered one of the points the seam is answered at. Every
/// excusal is held to the classes the map admits for its point; one claiming a class
/// the map contradicts is named and fails the bar the way a stale excusal does.
/// </summary>
public static class DecisionCoverage
{
    public static CoverageReport Over(
        IReadOnlyList<DecisionPoint> denominator,
        IReadOnlyDictionary<DecisionPoint, Excusal> excusals,
        IReadOnlyList<CoveredRecording> recordings,
        IReadOnlyList<ProducerSeam>? producerMap = null,
        IReadOnlyDictionary<DecisionPoint, IReadOnlySet<ExcusalClass>>? admissible = null,
        IReadOnlyList<UnreadableRecording>? unreadable = null)
    {
        var credited = new Dictionary<DecisionPoint, int>();
        var unverified = new Dictionary<DecisionPoint, int>();
        foreach (var recording in recordings)
        {
            var counts = recording.Credits ? credited : unverified;
            foreach (var point in recording.Points.Concat(SeamsReachedBy(recording, producerMap ?? [])))
            {
                counts[point] = counts.GetValueOrDefault(point) + 1;
            }
        }

        var rows = denominator
            .Select(point =>
            {
                var count = credited.GetValueOrDefault(point);
                var notProjectable = DecisionKinds.NotProjectableBecause(point.Kind);
                var excused = excusals.TryGetValue(point, out var excusal) ? excusal : null;
                var state = notProjectable is not null ? CoverageState.NotProjectable
                    : count > 0 && DecisionKinds.IsReachedByCoOccurrence(point.Kind) ? CoverageState.CoOccurrence
                    : count > 0 ? CoverageState.Covered
                    : excused is not null ? CoverageState.Excused
                    : CoverageState.Uncovered;
                return new CoverageRow(
                    point, count, unverified.GetValueOrDefault(point), state,
                    state == CoverageState.Excused ? excused : null);
            })
            .ToList();

        // A point a recording reached that no walk produced is a finding about the
        // walk or the format, never silently dropped: it is listed after the
        // denominator under its own heading, whichever standing reached it.
        var known = denominator.ToHashSet();
        var outside = credited.Keys.Concat(unverified.Keys).Distinct()
            .Where(point => !known.Contains(point))
            .OrderBy(point => Array.IndexOf(DecisionKinds.All, point.Kind))
            .ThenBy(point => point.Identity, StringComparer.Ordinal)
            .Select(point => new CoverageRow(
                point, credited.GetValueOrDefault(point), unverified.GetValueOrDefault(point),
                CoverageState.OutsideTheDenominator, null))
            .ToList();

        // An excusal naming a point no walk produces is stale on any corpus and comes
        // out. One a crediting recording reached is a fact about this corpus and not
        // about the build: over the committed corpus it is the excusal's own sentence
        // gone false, which the merge gate holds to zero; over a wider corpus it is
        // progress, and named so rather than failed
        var staleExcusals = excusals.Keys
            .Where(point => !known.Contains(point))
            .OrderBy(point => Array.IndexOf(DecisionKinds.All, point.Kind))
            .ThenBy(point => point.Identity, StringComparer.Ordinal)
            .ToList();
        var excusedAndReached = excusals.Keys
            .Where(point => known.Contains(point) && credited.GetValueOrDefault(point) > 0)
            .OrderBy(point => Array.IndexOf(DecisionKinds.All, point.Kind))
            .ThenBy(point => point.Identity, StringComparer.Ordinal)
            .ToList();

        // An excusal whose class the map does not admit for its point is a sentence
        // about the wrong thing - a placeholder on a point nothing produces, a derived
        // class the walk contradicts - and is named with what the map admits instead
        var inadmissible = excusals
            .Where(entry => known.Contains(entry.Key) && admissible is not null)
            .Select(entry => (Point: entry.Key, entry.Value.Class,
                Admitted: admissible!.TryGetValue(entry.Key, out var classes) ? classes : new HashSet<ExcusalClass>()))
            .Where(entry => !entry.Admitted.Contains(entry.Class))
            .OrderBy(entry => Array.IndexOf(DecisionKinds.All, entry.Point.Kind))
            .ThenBy(entry => entry.Point.Identity, StringComparer.Ordinal)
            .Select(entry => new InadmissibleExcusal(entry.Point, entry.Class, entry.Admitted.Order().ToList()))
            .ToList();

        return new CoverageReport(
            rows, outside, staleExcusals, excusedAndReached, inadmissible,
            recordings.Where(recording => !recording.Credits).ToList(),
            unreadable ?? [],
            recordings.Count + (unreadable?.Count ?? 0));
    }

    /// <summary>The seam points one recording reached by co-occurrence: a producer met
    /// and the seam's decision answered, in the same recording.</summary>
    public static IEnumerable<DecisionPoint> SeamsReachedBy(CoveredRecording recording, IReadOnlyList<ProducerSeam> producerMap) =>
        producerMap
            .Where(seam => seam.Producers.Any(recording.ModelsMet.Contains) && seam.AnsweredAt.Any(recording.Points.Contains))
            .Select(seam => seam.Point);
}

/// <summary>One recording's projection, named so a row can say who reached it, with
/// the standing that says whether it credits what it reached and the models it met,
/// for the seams it reached by co-occurrence.</summary>
public sealed record CoveredRecording(
    string RunId, IReadOnlySet<DecisionPoint> Points, RecordingStanding Standing, IReadOnlySet<string>? Models = null)
{
    public bool Credits => Standing.Holds;

    public IReadOnlySet<string> ModelsMet => Models ?? new HashSet<string>();
}

/// <summary>A manifest in the corpus this build could not read, with the parser's words.</summary>
public sealed record UnreadableRecording(string Manifest, string Detail);

/// <summary>An excusal whose class the map does not admit for its point, with the
/// classes it does.</summary>
public sealed record InadmissibleExcusal(DecisionPoint Point, ExcusalClass Claimed, IReadOnlyList<ExcusalClass> Admitted)
{
    public string Describe() =>
        $"{Point}  excused as {ExcusalClasses.Name(Claimed)}, and the map admits " +
        (Admitted.Count == 0 ? "no class here" : string.Join(", ", Admitted.Select(ExcusalClasses.Name)));
}

public enum CoverageState
{
    Covered,
    CoOccurrence,
    Uncovered,
    Excused,
    NotProjectable,
    OutsideTheDenominator,
}

/// <param name="Recordings">How many crediting recordings reached the point.</param>
/// <param name="UnverifiedRecordings">How many recordings that credit nothing reached it;
/// printed beside the row and never folded into its state.</param>
public sealed record CoverageRow(
    DecisionPoint Point, int Recordings, int UnverifiedRecordings, CoverageState State, Excusal? Excuse)
{
    /// <summary>The row as the report prints it.</summary>
    public string Describe() => State switch
    {
        CoverageState.Covered =>
            $"{Point}  {Recordings.ToString(CultureInfo.InvariantCulture)} recording(s){Unverified}",
        CoverageState.CoOccurrence =>
            $"{Point}  co-occurrence in {Recordings.ToString(CultureInfo.InvariantCulture)} recording(s){Unverified}",
        CoverageState.Uncovered => $"{Point}  uncovered{Unverified}",
        CoverageState.Excused => $"{Point}  {Excuse!.Describe()}{Unverified}",
        CoverageState.NotProjectable => $"{Point}  {DecisionKinds.NotProjectableBecause(Point.Kind)}",
        CoverageState.OutsideTheDenominator =>
            $"{Point}  {Recordings.ToString(CultureInfo.InvariantCulture)} recording(s){Unverified}, and no walk of " +
            "this build produced the point",
        _ => throw new ArgumentOutOfRangeException(nameof(State), State, "unknown coverage state"),
    };

    private string Unverified => UnverifiedRecordings > 0
        ? $"; reached by {UnverifiedRecordings.ToString(CultureInfo.InvariantCulture)} unverified recording(s), " +
          "not credited"
        : "";
}

/// <summary>What <see cref="DecisionCoverage.Over"/> found.</summary>
/// <param name="Rows">One per point of the denominator, in the denominator's order.</param>
/// <param name="OutsideTheDenominator">Points a recording reached that no walk produced.</param>
/// <param name="StaleExcusals">Excusals to take out: no walk on this build produces the point.</param>
/// <param name="ExcusedAndReached">Excused points a crediting recording of this corpus reached: over the
/// committed corpus, an excusal whose sentence has gone false; over a wider one, progress.</param>
/// <param name="InadmissibleExcusals">Excusals claiming a class the map does not admit for their point.</param>
/// <param name="Unverified">The recordings projected and credited nothing, each with the recorder's own reason.</param>
/// <param name="Unreadable">The manifests this build could not read, each with the parser's words.</param>
/// <param name="Recordings">How many recordings the corpus held, unverified and unreadable included.</param>
public sealed record CoverageReport(
    IReadOnlyList<CoverageRow> Rows,
    IReadOnlyList<CoverageRow> OutsideTheDenominator,
    IReadOnlyList<DecisionPoint> StaleExcusals,
    IReadOnlyList<DecisionPoint> ExcusedAndReached,
    IReadOnlyList<InadmissibleExcusal> InadmissibleExcusals,
    IReadOnlyList<CoveredRecording> Unverified,
    IReadOnlyList<UnreadableRecording> Unreadable,
    int Recordings)
{
    public int Points => Rows.Count;
    public int Covered => Count(CoverageState.Covered);
    public int CoOccurrence => Count(CoverageState.CoOccurrence);
    public int Uncovered => Count(CoverageState.Uncovered);
    public int Excused => Count(CoverageState.Excused);
    public int NotProjectable => Count(CoverageState.NotProjectable);
    public int CreditedRecordings => Recordings - Unverified.Count - Unreadable.Count;

    /// <summary>Whether the bar holds: no point is uncovered, no recording reached a
    /// point outside the denominator, no excusal is stale, and none claims a class
    /// the map does not admit.</summary>
    public bool Holds =>
        Uncovered == 0 && OutsideTheDenominator.Count == 0 && StaleExcusals.Count == 0 && InadmissibleExcusals.Count == 0;

    private int Count(CoverageState state) => Rows.Count(row => row.State == state);

    /// <summary>The totals, as the report's last lines before the verdict.</summary>
    public IEnumerable<string> Totals()
    {
        var n = (int value) => value.ToString(CultureInfo.InvariantCulture);
        yield return $"points: {n(Points)}  covered: {n(Covered)}  co-occurrence: {n(CoOccurrence)}  " +
                     $"excused: {n(Excused)}  uncovered: {n(Uncovered)}  " +
                     $"not projectable: {n(NotProjectable)}  recordings: {n(Recordings)}";
        if (Unverified.Count > 0 || Unreadable.Count > 0)
        {
            yield return $"recordings credited: {n(CreditedRecordings)}  unverified: {n(Unverified.Count)}  " +
                         $"unreadable: {n(Unreadable.Count)}";
        }

        if (OutsideTheDenominator.Count > 0)
        {
            yield return $"outside the denominator: {n(OutsideTheDenominator.Count)}";
        }

        if (StaleExcusals.Count > 0)
        {
            yield return $"stale excusals: {n(StaleExcusals.Count)}";
        }

        if (InadmissibleExcusals.Count > 0)
        {
            yield return $"inadmissible excusals: {n(InadmissibleExcusals.Count)}";
        }

        if (ExcusedAndReached.Count > 0)
        {
            yield return $"excused and reached by this corpus: {n(ExcusedAndReached.Count)}";
        }
    }
}
