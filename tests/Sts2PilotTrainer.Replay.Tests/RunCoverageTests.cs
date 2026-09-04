namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What a whole-run trace contains, read as an index and nothing more. No score, no
/// ranking, no judgement about how any fight went.
/// </summary>
public sealed class RunCoverageTests
{
    [Fact]
    public void ReadsEveryFightInRunOrderWithWhereItStartedAndEnded()
    {
        var coverage = RunCoverage.Of(WholeRun());

        Assert.Equal([1, 2], coverage.Fights.Select(fight => fight.Fight));
        Assert.Equal([1, 4], coverage.Fights.Select(fight => fight.CombatStartSeq));
        Assert.Equal([2, 5], coverage.Fights.Select(fight => fight.EndSeq));
        Assert.Equal(["victory", "defeat"], coverage.Fights.Select(fight => fight.Outcome));
        Assert.All(coverage.Fights, fight => Assert.True(fight.Finished));
    }

    [Fact]
    public void ReadsEveryFloorTheRunReachedInOrder()
    {
        var coverage = RunCoverage.Of(WholeRun());

        Assert.Equal([1, 2, 3], coverage.Floors.Select(floor => floor.Floor));
        Assert.Equal([-1, 1, 3], coverage.Floors.Select(floor => floor.EnteredAfterSeq));
    }

    /// <summary>
    /// A recording that stops mid-fight really did reach that fight. Dropping it would
    /// under-report what the recording holds, so it is kept with no end and the
    /// outcome the engine last reported.
    /// </summary>
    [Fact]
    public void KeepsAFightTheHistoryLeavesStillBeingFought()
    {
        var trace = new ReplayTrace { Steps = WholeRun().Steps.Take(6).ToList() };

        var coverage = RunCoverage.Of(trace);

        Assert.Equal([1, 2], coverage.Fights.Select(fight => fight.Fight));
        Assert.False(coverage.Fights[1].Finished);
        Assert.Null(coverage.Fights[1].EndSeq);
        Assert.Equal("in_progress", coverage.Fights[1].Outcome);
    }

    [Fact]
    public void ReadsNoFightOutOfATraceThatNeverEnteredOne()
    {
        var trace = new ReplayTrace { Steps = [Step(-1, "run_start", Outside(1), Outside(1))] };

        var coverage = RunCoverage.Of(trace);

        Assert.Empty(coverage.Fights);
        Assert.Equal([1], coverage.Floors.Select(floor => floor.Floor));
    }

    /// <summary>Two fights, one won and one lost, over three floors.</summary>
    private static ReplayTrace WholeRun() => new()
    {
        Steps =
        [
            Step(-1, "run_start", Outside(1), Outside(1)),
            Step(0, "ChooseNeowBlessing", Outside(1), Outside(1)),
            Step(1, "MapMove", Outside(1), InCombat(2)),
            Step(2, "PlayCard", InCombat(2), Ended(2, "victory")),
            Step(3, "MapMove", Ended(2, "victory"), Outside(3)),
            Step(4, "MapMove", Outside(3), InCombat(3)),
            Step(5, "PlayCard", InCombat(3), Ended(3, "defeat")),
        ],
    };

    private static ReplayStep Step(
        int seq, string verb, IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after) =>
        new() { Seq = seq, Verb = verb, Before = before, After = after };

    private static Dictionary<string, string> Outside(int floor) => Sample(floor, "none");

    private static Dictionary<string, string> InCombat(int floor) => Sample(floor, "in_progress");

    private static Dictionary<string, string> Ended(int floor, string outcome) => Sample(floor, outcome);

    private static Dictionary<string, string> Sample(int floor, string outcome) => new(StringComparer.Ordinal)
    {
        ["combat.outcome"] = outcome,
        ["run.total_floor"] = floor.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The marker a future rewind will use to keep a line a player tried and unwound.
/// Nothing reads it in these phases; what it has to do now is be absent by default
/// and survive a round trip, so that when a rewind exists the format does not move.
/// </summary>
public sealed class DiscardedStepTests
{
    [Fact]
    public void IsAbsentFromAStepNobodyMarked()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Step(), ManifestJson.Options);

        Assert.DoesNotContain("discarded", json, StringComparison.Ordinal);
        Assert.Null(Step().Discarded);
    }

    [Fact]
    public void SurvivesAJsonRoundTrip()
    {
        var marked = Step() with { Discarded = true };
        var json = System.Text.Json.JsonSerializer.Serialize(marked, ManifestJson.Options);

        var read = ManifestJson.DeserializeRequired<ReplayStep>(json, "Replay step");

        Assert.True(read.Discarded);
    }

    private static ReplayStep Step() => new()
    {
        Seq = 0,
        Verb = "PlayCard",
        Before = new Dictionary<string, string>(StringComparer.Ordinal) { ["player.hp"] = "64" },
        After = new Dictionary<string, string>(StringComparer.Ordinal) { ["player.hp"] = "64" },
    };
}
