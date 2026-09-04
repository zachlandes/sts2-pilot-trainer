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
                open = new CoveredFight
                {
                    Fight = ordinal,
                    CombatStartSeq = step.Seq,
                    EndSeq = null,
                    Outcome = InProgress,
                };
                continue;
            }

            if (wasLive && !isLive)
            {
                fights.Add(open! with { EndSeq = step.Seq, Outcome = Outcome(step.After) });
                open = null;
            }
        }

        if (open is not null) fights.Add(open);

        return new RunCoverage { Fights = fights, Floors = floors };
    }

    private const string InProgress = "in_progress";

    private static string Outcome(IReadOnlyDictionary<string, string> sample) =>
        sample.GetValueOrDefault("combat.outcome", "none");

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

    [JsonIgnore]
    public bool Finished => EndSeq is not null;
}

/// <summary>One floor the run reached, and where in the history it was reached.</summary>
public sealed record CoveredFloor
{
    [JsonPropertyName("floor")]
    public required int Floor { get; init; }

    [JsonPropertyName("entered_after_seq")]
    public required int EnteredAfterSeq { get; init; }
}
