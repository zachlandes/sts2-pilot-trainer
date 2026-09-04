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
        Assert.Equal([1, 6], coverage.Fights.Select(fight => fight.CombatStartSeq));
        Assert.Equal([4, 7], coverage.Fights.Select(fight => fight.EndSeq));
        Assert.Equal(["victory", "defeat"], coverage.Fights.Select(fight => fight.Outcome));
        Assert.All(coverage.Fights, fight => Assert.True(fight.Finished));
    }

    [Fact]
    public void ReadsEveryFloorTheRunReachedInOrder()
    {
        var coverage = RunCoverage.Of(WholeRun());

        Assert.Equal([1, 2, 3], coverage.Floors.Select(floor => floor.Floor));
        Assert.Equal([-1, 1, 5], coverage.Floors.Select(floor => floor.EnteredAfterSeq));
    }

    /// <summary>
    /// A recording that stops mid-fight really did reach that fight. Dropping it would
    /// under-report what the recording holds, so it is kept with no end and the
    /// outcome the engine last reported.
    /// </summary>
    [Fact]
    public void KeepsAFightTheHistoryLeavesStillBeingFought()
    {
        var trace = new ReplayTrace { Steps = WholeRun().Steps.Take(8).ToList() };

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

    [Fact]
    public void ReadsEveryTurnOfEveryFightAndTheActionItStartedAfter()
    {
        var coverage = RunCoverage.Of(WholeRun());

        Assert.Equal([1, 2], coverage.Fights[0].Turns.Select(turn => turn.Turn));
        Assert.Equal([1, 3], coverage.Fights[0].Turns.Select(turn => turn.StartedAfterSeq));
        Assert.Equal([1], coverage.Fights[1].Turns.Select(turn => turn.Turn));
        Assert.Equal([6], coverage.Fights[1].Turns.Select(turn => turn.StartedAfterSeq));
    }

    /// <summary>
    /// Where a player can be stood, read off the history and nothing else. The digest
    /// at each is the engine's to produce; this says only where they are.
    /// </summary>
    [Fact]
    public void LocatesEveryBoundaryTheHistoryPassed()
    {
        var boundaries = RunCoverage.Of(WholeRun()).Boundaries();

        Assert.Equal(
            [
                (ReplayBoundary.CombatStartKind, 1),
                (ReplayBoundary.FloorEntryKind, 1),
                (ReplayBoundary.TurnStartKind, 1),
                (ReplayBoundary.TurnStartKind, 3),
                (ReplayBoundary.FloorEntryKind, 5),
                (ReplayBoundary.CombatStartKind, 6),
                (ReplayBoundary.TurnStartKind, 6),
            ],
            boundaries.Select(boundary => (boundary.Kind, boundary.AfterSeq)));
    }

    /// <summary>The floor the run starts on is not arrived at: a floor boundary is the
    /// map move that entered it, and there was none.</summary>
    [Fact]
    public void LocatesNoBoundaryOnTheFloorTheRunStartsOn()
    {
        var boundaries = RunCoverage.Of(WholeRun()).Boundaries();

        Assert.DoesNotContain(boundaries, boundary => boundary.AfterSeq < 0);
        Assert.DoesNotContain(boundaries, boundary => boundary.Floor == 1);
    }

    /// <summary>
    /// A fight the history leaves unfinished is not a place anybody can be stood.
    /// There is no completed recorded line there to compare a player's against, which
    /// is the same rule the validator applies to a declared combat_start.
    /// </summary>
    [Fact]
    public void LocatesNoBoundaryInsideAFightTheHistoryNeverFinishes()
    {
        var trace = new ReplayTrace { Steps = WholeRun().Steps.Take(8).ToList() };

        var boundaries = RunCoverage.Of(trace).Boundaries();

        Assert.DoesNotContain(boundaries, boundary => boundary.Fight == 2);
        Assert.Contains(boundaries, boundary => boundary.Floor == 3);
    }

    /// <summary>A location becomes a boundary only once something says what the state
    /// there was; nothing here can, and nothing here pretends to.</summary>
    [Fact]
    public void BecomesABoundaryOnlyWhenGivenADigest()
    {
        var location = RunCoverage.Of(WholeRun()).Boundaries()
            .First(boundary => boundary.Kind == ReplayBoundary.CombatStartKind);

        var boundary = location.With(Fact<string>.Engine("sha256:" + new string('a', 64)));

        Assert.Equal(ReplayBoundary.CombatStartKind, boundary.Kind);
        Assert.Equal(1, boundary.Fight);
        Assert.Equal(1, boundary.AfterSeq);
        Assert.Equal(FactSource.Engine, boundary.Digest.Source);
    }

    /// <summary>
    /// Two fights over three floors: the first won across two turns, the second lost
    /// on its first.
    /// </summary>
    private static ReplayTrace WholeRun() => new()
    {
        Steps =
        [
            Step(-1, "run_start", Outside(1), Outside(1)),
            Step(0, "ChooseNeowBlessing", Outside(1), Outside(1)),
            Step(1, "MapMove", Outside(1), InCombat(2)),
            Step(2, "PlayCard", InCombat(2), InCombat(2)),
            Step(3, "EndTurn", InCombat(2), InCombat(2, turn: 2)),
            Step(4, "PlayCard", InCombat(2, turn: 2), Ended(2, "victory")),
            Step(5, "MapMove", Ended(2, "victory"), Outside(3)),
            Step(6, "MapMove", Outside(3), InCombat(3)),
            Step(7, "PlayCard", InCombat(3), Ended(3, "defeat")),
        ],
    };

    private static ReplayStep Step(
        int seq, string verb, IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after) =>
        new() { Seq = seq, Verb = verb, Before = before, After = after };

    private static Dictionary<string, string> Outside(int floor) => Sample(floor, "none");

    private static Dictionary<string, string> InCombat(int floor, int turn = 1) =>
        Sample(floor, "in_progress", turn);

    private static Dictionary<string, string> Ended(int floor, string outcome) => Sample(floor, outcome, 0);

    private static Dictionary<string, string> Sample(int floor, string outcome, int turn = 0)
    {
        var sample = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.outcome"] = outcome,
            ["run.total_floor"] = floor.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (turn > 0)
        {
            sample["combat.turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return sample;
    }
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
