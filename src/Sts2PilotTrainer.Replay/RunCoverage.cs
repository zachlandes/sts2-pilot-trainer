using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What a whole-run trace contains: which fights it holds and which floors it
/// reached.
///
/// <see cref="CombatProjection.CoverageOf"/> answers the same question about one
/// fight, and answers it for the thing that has to decide whether a projection is
/// defined. This reads the run. They sit beside each other rather than one wrapping
/// the other because they are asked at different moments and by different callers:
/// the fight coverage gates a comparison, this fills a catalogue entry's fight count
/// and a run view's strip.
///
/// It computes nothing else. There is no score here, no ranking, no judgement about
/// which fight went well - <c>docs/comparison-direction.md</c> owns why, and this is
/// an index, not a reading.
/// </summary>
public sealed record RunCoverage
{
    /// <summary>Every fight the trace holds, in the order the run played them.</summary>
    [JsonPropertyName("fights")]
    public required IReadOnlyList<CoveredFight> Fights { get; init; }

    /// <summary>Every floor the trace reached, in ascending order.</summary>
    [JsonPropertyName("floors")]
    public required IReadOnlyList<CoveredFloor> Floors { get; init; }

    /// <summary>
    /// Every place in this history a player could be stood, in history order.
    ///
    /// The locations only: what the state actually was at each of them is a digest,
    /// and no trace carries one - a trace samples the fields a comparison reads, and a
    /// boundary digest covers the whole canonical state including the draw order and
    /// the random streams. Deriving where the boundaries are is a rule over the
    /// history and belongs here; deriving what they hold needs the engine, and is the
    /// arbiter's.
    ///
    /// A fight the history stops in the middle of contributes nothing. It is a real
    /// fight and it is not a place anybody can be stood: there is no completed
    /// recorded line to compare a player's against, which is the same rule the
    /// validator applies to a declared combat_start.
    /// </summary>
    public IReadOnlyList<BoundaryLocation> Boundaries() =>
    [
        .. Floors
            // The floor the run starts on is not arrived at. Its entry seq is the one
            // before any action, and a floor boundary is the map move that entered the
            // floor - so declaring one here would name a place no plan could reach.
            .Where(floor => floor.EnteredAfterSeq >= 0)
            .Select(floor => new BoundaryLocation(
                ReplayBoundary.FloorEntryKind, floor.EnteredAfterSeq, Floor: floor.Floor))
            .Concat(Fights
                .Where(fight => fight.Finished)
                .SelectMany(fight => new[]
                    {
                        new BoundaryLocation(
                            ReplayBoundary.CombatStartKind, fight.CombatStartSeq, Fight: fight.Fight),
                    }
                    .Concat(fight.Turns.Select(turn => new BoundaryLocation(
                        ReplayBoundary.TurnStartKind, turn.StartedAfterSeq,
                        Fight: fight.Fight, Turn: turn.Turn)))))
            .OrderBy(boundary => boundary.AfterSeq)
            .ThenBy(boundary => boundary.Kind, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Reads a trace into its fights and floors.
    ///
    /// A fight begins at the sample where the engine first reports a combat in
    /// progress and ends at the first sample afterwards where it does not. A fight
    /// the history leaves still in progress is kept, with no end and its outcome as
    /// the engine last reported it - a run whose recording stops mid-fight really did
    /// reach that fight, and dropping it would under-report what the recording holds.
    /// </summary>
    public static RunCoverage Of(ReplayTrace trace)
    {
        var steps = trace.Steps.OrderBy(step => step.Seq).ToList();
        var fights = new List<CoveredFight>();
        var floors = new List<CoveredFloor>();
        var ordinal = 0;
        CoveredFight? open = null;
        var turns = new List<CoveredTurn>();

        foreach (var step in steps)
        {
            if (Floor(step.After) is { } floor && floors.All(entry => entry.Floor != floor))
            {
                floors.Add(new CoveredFloor { Floor = floor, EnteredAfterSeq = step.Seq });
            }

            var wasLive = open is not null;
            var isLive = Outcome(step.After) == InProgress;

            if (!wasLive && isLive)
            {
                ordinal++;
                turns = Turn(step.After) is { } first
                    ? [new CoveredTurn { Turn = first, StartedAfterSeq = step.Seq }]
                    : [];
                open = new CoveredFight
                {
                    Fight = ordinal,
                    CombatStartSeq = step.Seq,
                    EndSeq = null,
                    Outcome = InProgress,
                    Turns = turns,
                };
                continue;
            }

            if (wasLive && isLive && Turn(step.After) is { } turn &&
                turns.All(entry => entry.Turn != turn))
            {
                // The action after which the engine first reports this turn number is
                // the action that started it. Read from the number rather than from the
                // verb, because what ends a turn is not always an EndTurn - a card or a
                // power can, and a rule that counted verbs would number them wrongly.
                turns.Add(new CoveredTurn { Turn = turn, StartedAfterSeq = step.Seq });
                continue;
            }

            if (wasLive && !isLive)
            {
                fights.Add(open! with
                {
                    EndSeq = step.Seq,
                    Outcome = Outcome(step.After),
                    Turns = turns,
                });
                open = null;
                turns = [];
            }
        }

        if (open is not null) fights.Add(open with { Turns = turns });

        return new RunCoverage { Fights = fights, Floors = floors };
    }

    private const string InProgress = "in_progress";

    private static string Outcome(IReadOnlyDictionary<string, string> sample) =>
        sample.GetValueOrDefault("combat.outcome", "none");

    private static int? Turn(IReadOnlyDictionary<string, string> sample) =>
        sample.TryGetValue("combat.turn", out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static int? Floor(IReadOnlyDictionary<string, string> sample) =>
        sample.TryGetValue("run.total_floor", out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>One fight of a run, located in the history that produced it.</summary>
public sealed record CoveredFight
{
    /// <summary>Its ordinal in the run, counting from 1. The same number a
    /// <see cref="ReplayBoundary"/> of kind <c>combat_start</c> carries.</summary>
    [JsonPropertyName("fight")]
    public required int Fight { get; init; }

    /// <summary>The sequence number after which the engine reported the combat live.</summary>
    [JsonPropertyName("combat_start_seq")]
    public required int CombatStartSeq { get; init; }

    /// <summary>The sequence number of the action that ended it, or null when the
    /// history stops while it is still being fought.</summary>
    [JsonPropertyName("end_seq")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EndSeq { get; init; }

    /// <summary>The engine's own last word on the fight.</summary>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    /// <summary>Every turn of this fight, in order, and the action each started
    /// after.</summary>
    [JsonPropertyName("turns")]
    public IReadOnlyList<CoveredTurn> Turns { get; init; } = [];

    [JsonIgnore]
    public bool Finished => EndSeq is not null;
}

/// <summary>One turn of one fight, located in the history that produced it.</summary>
public sealed record CoveredTurn
{
    /// <summary>Its number in the fight, as the engine counts turns.</summary>
    [JsonPropertyName("turn")]
    public required int Turn { get; init; }

    /// <summary>The sequence number after which the engine first reported this
    /// turn.</summary>
    [JsonPropertyName("started_after_seq")]
    public required int StartedAfterSeq { get; init; }
}

/// <summary>
/// Where a boundary is, without what it holds.
///
/// The half of a <see cref="ReplayBoundary"/> that a history determines. The other
/// half is the digest, which only replaying through the engine produces, so this is
/// what a pure reader can honestly hand over.
/// </summary>
public sealed record BoundaryLocation(
    string Kind, int AfterSeq, int? Fight = null, int? Floor = null, int? Turn = null)
{
    /// <summary>The boundary this location is, once the engine has said what the state
    /// there was.</summary>
    public ReplayBoundary With(Fact<string> digest) => new()
    {
        Kind = Kind,
        AfterSeq = AfterSeq,
        Fight = Fight,
        Floor = Floor,
        Turn = Turn,
        Digest = digest,
    };
}

/// <summary>One floor the run reached, and where in the history it was reached.</summary>
public sealed record CoveredFloor
{
    [JsonPropertyName("floor")]
    public required int Floor { get; init; }

    [JsonPropertyName("entered_after_seq")]
    public required int EnteredAfterSeq { get; init; }
}
