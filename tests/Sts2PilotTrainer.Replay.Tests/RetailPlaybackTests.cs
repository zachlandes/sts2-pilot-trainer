using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// How far into a recording a running retail client can walk.
///
/// The rule exists because two owners in different assemblies need one answer: the
/// driver refuses a verb it does not issue, and the library has to know that before it
/// offers a player a place to stand. A second copy of the verb set is exactly how the
/// library came to offer a floor the journey aborts on.
///
/// These are the rule's own tests. That the driver enforces the same set is
/// <c>RecordedFightVerbAgreementTests</c>' to hold, because that needs the engine.
/// </summary>
public sealed class RetailPlaybackTests
{
    private static ActionRecord Action(int seq, ActionVerb verb) => new()
    {
        Seq = seq,
        Verb = verb,
        Source = FactSource.Captured,
        Args = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    private static ReplayManifest Recording(params ActionRecord[] actions) =>
        Fixtures.ValidManifest() with { Actions = actions, Checkpoints = [] };

    [Fact]
    public void TheClientIssuesTheFourDecisionsBeforeAFightAndNothingInOne()
    {
        Assert.Equal(
            [
                ActionVerb.ChooseNeowBlessing,
                ActionVerb.ChooseEventOption,
                ActionVerb.MapMove,
                ActionVerb.SelectCardFromScreen,
            ],
            RetailPlayback.Verbs);
    }

    [Fact]
    public void AWalkOfOnlyThoseVerbsIsReachable()
    {
        var recording = Recording(
            Action(0, ActionVerb.ChooseNeowBlessing),
            Action(1, ActionVerb.SelectCardFromScreen),
            Action(2, ActionVerb.MapMove));

        Assert.True(RetailPlayback.CanReach(recording, boundarySeq: 2));
        Assert.Null(RetailPlayback.FirstRefusal(recording, boundarySeq: 2));
    }

    /// <summary>
    /// The refusal names the first decision the client cannot issue, not the last and
    /// not a count. A surface that wants to say what stopped it has the sequence number
    /// and the verb without asking twice.
    /// </summary>
    [Fact]
    public void AWalkThroughAnEarlierFightNamesTheFirstDecisionTheClientCannotIssue()
    {
        var recording = Recording(
            Action(0, ActionVerb.ChooseNeowBlessing),
            Action(1, ActionVerb.MapMove),
            Action(2, ActionVerb.PlayCard),
            Action(3, ActionVerb.EndTurn),
            Action(4, ActionVerb.ClaimReward),
            Action(5, ActionVerb.MapMove));

        Assert.False(RetailPlayback.CanReach(recording, boundarySeq: 5));

        var refusal = RetailPlayback.FirstRefusal(recording, boundarySeq: 5);
        Assert.NotNull(refusal);
        Assert.Equal(2, refusal.Seq);
        Assert.Equal(ActionVerb.PlayCard, refusal.Verb);
    }

    /// <summary>The boundary bounds the walk: a fight further on does not make an
    /// earlier one unreachable.</summary>
    [Fact]
    public void OnlyTheDecisionsBeforeTheBoundaryCount()
    {
        var recording = Recording(
            Action(0, ActionVerb.ChooseNeowBlessing),
            Action(1, ActionVerb.MapMove),
            Action(2, ActionVerb.PlayCard));

        Assert.True(RetailPlayback.CanReach(recording, boundarySeq: 1));
        Assert.False(RetailPlayback.CanReach(recording, boundarySeq: 2));
    }

    /// <summary>A boundary before the first action - the run's own start - is reached
    /// trivially rather than by a special case.</summary>
    [Fact]
    public void ABoundaryBeforeTheFirstActionIsReached()
    {
        var recording = Recording(Action(0, ActionVerb.PlayCard));

        Assert.True(RetailPlayback.CanReach(recording, boundarySeq: -1));
    }
}

/// <summary>
/// How a running client reaches a boundary: walked, restored, or not at all, and what
/// stops it.
///
/// The route is read from the recording alone. Whether a save is cached is not an
/// input, because a row that was greyed over a missing file would be greyed over the
/// wrong fact; the cache is materialised at the press where the history says a
/// restore is possible.
/// </summary>
public sealed class RetailPlaybackRouteTests
{
    private static Fact<string> Digest(string value) => Fact<string>.Engine(value);

    private static ActionRecord Action(int seq, ActionVerb verb) => new()
    {
        Seq = seq,
        Verb = verb,
        Source = FactSource.Captured,
        Args = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    /// <summary>
    /// The shape of every real recording: a blessing and a move into the first fight,
    /// the fight, its loot, a move into the second fight, that fight, its loot, and a
    /// move into an event.
    /// </summary>
    private static ReplayManifest TwoFightsThenAnEvent() => Fixtures.ValidManifest() with
    {
        Checkpoints = [],
        Boundaries =
        [
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 1, Digest("floor-2")),
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 1, Digest("fight-1")),
            ReplayBoundary.FloorEntry(floor: 3, afterSeq: 6, Digest("floor-3")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 6, Digest("fight-2")),
            ReplayBoundary.FloorEntry(floor: 4, afterSeq: 11, Digest("floor-4")),
        ],
        Actions =
        [
            Action(0, ActionVerb.ChooseNeowBlessing),
            Action(1, ActionVerb.MapMove),
            Action(2, ActionVerb.PlayCard),
            Action(3, ActionVerb.EndTurn),
            Action(4, ActionVerb.ClaimReward),
            Action(5, ActionVerb.TakeCard),
            Action(6, ActionVerb.MapMove),
            Action(7, ActionVerb.PlayCard),
            Action(8, ActionVerb.EndTurn),
            Action(9, ActionVerb.ClaimReward),
            Action(10, ActionVerb.SkipRewards),
            Action(11, ActionVerb.MapMove),
            Action(12, ActionVerb.ChooseEventOption),
        ],
    };

    [Fact]
    public void ARestorableArrivalIsOneWhereTheRecordingDeclaresACombatStartAtTheSameAction()
    {
        Assert.Equal(
            [new RestorableArrival(3, 6), new RestorableArrival(2, 1)],
            RetailPlayback.RestorableArrivals(TwoFightsThenAnEvent()));
    }

    /// <summary>The walk comes first where the client can walk: watching the decisions
    /// is the point, and a restore skips exactly that.</summary>
    [Fact]
    public void TheFirstFightIsWalkedEvenThoughItsArrivalCouldBeRestored()
    {
        var route = RetailPlayback.RouteTo(TwoFightsThenAnEvent(), boundarySeq: 1);

        Assert.IsType<PlaybackRoute.Walk>(route);
        Assert.True(route.Reachable);
    }

    [Fact]
    public void AFightPastTheFirstIsRestoredFromTheArrivalThatDealtIt()
    {
        var route = Assert.IsType<PlaybackRoute.Restore>(RetailPlayback.RouteTo(TwoFightsThenAnEvent(), 6));

        Assert.Equal(3, route.Floor);
        Assert.Equal(6, route.AfterSeq);
        Assert.True(route.Reachable);
    }

    /// <summary>
    /// A floor between fights is refused, and the refusal names the first decision
    /// after the best restore point rather than the first in the run: the second
    /// fight's first card, not the first fight's.
    /// </summary>
    [Fact]
    public void AFloorBetweenFightsIsUnreachableAndNamesTheDecisionAfterTheBestRestorePoint()
    {
        var route = Assert.IsType<PlaybackRoute.Unreachable>(RetailPlayback.RouteTo(TwoFightsThenAnEvent(), 11));

        Assert.False(route.Reachable);
        Assert.Equal(7, route.Refused.Seq);
        Assert.Equal(ActionVerb.PlayCard, route.Refused.Verb);
    }

    /// <summary>Where nothing on the way is restorable, the refusal is the first
    /// decision the client cannot issue, exactly as <c>FirstRefusal</c> names it.</summary>
    [Fact]
    public void WithNoRestorePointTheRefusalIsTheFirstOnTheWalk()
    {
        var recording = TwoFightsThenAnEvent() with { Boundaries = [] };

        var route = Assert.IsType<PlaybackRoute.Unreachable>(RetailPlayback.RouteTo(recording, 11));

        Assert.Equal(2, route.Refused.Seq);
        Assert.Equal(RetailPlayback.FirstRefusal(recording, 11), route.Refused);
    }

    /// <summary>
    /// A boundary past a restorable arrival whose tail the client can walk is restored
    /// and then walked. On this build no recording produces it - a fight is live at every
    /// restorable arrival, so the next decision is the fight's - and the shape is here
    /// for the day the fight can be walked, when it becomes the route to every floor
    /// between fights. The fixture is synthetic for that reason.
    /// </summary>
    [Fact]
    public void ARestorePointWithAWalkableTailIsRestoredThenWalked()
    {
        var recording = Fixtures.ValidManifest() with
        {
            Checkpoints = [],
            Boundaries =
            [
                ReplayBoundary.FloorEntry(floor: 2, afterSeq: 3, Digest("floor-2")),
                ReplayBoundary.CombatStart(fight: 1, afterSeq: 3, Digest("fight-1")),
                ReplayBoundary.CombatStart(fight: 2, afterSeq: 5, Digest("fight-2")),
            ],
            Actions =
            [
                Action(0, ActionVerb.ChooseNeowBlessing),
                Action(1, ActionVerb.PlayCard),
                Action(2, ActionVerb.EndTurn),
                Action(3, ActionVerb.MapMove),
                Action(4, ActionVerb.ChooseEventOption),
                Action(5, ActionVerb.ChooseEventOption),
            ],
        };

        var route = Assert.IsType<PlaybackRoute.RestoreThenWalk>(RetailPlayback.RouteTo(recording, 5));

        Assert.Equal(2, route.Floor);
        Assert.Equal(3, route.AfterSeq);
        Assert.True(route.Reachable);
    }

    [Fact]
    public void APlanRoutesToItsOwnBoundary()
    {
        var recording = EntryFixtures.WholeRun();
        var fight = RecordedFightPlan.For(recording, 2);

        Assert.Equal(
            RetailPlayback.RouteTo(recording, fight.BoundarySeq),
            RetailPlayback.RouteTo(recording, fight));
    }
}
