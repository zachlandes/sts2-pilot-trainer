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

    /// <summary>A plan and a boundary sequence are two spellings of one question, so
    /// the host that walks and the surface that offers cannot come to disagree.</summary>
    [Fact]
    public void APlanAndItsBoundarySequenceGetTheSameAnswer()
    {
        var actions = new[]
        {
            Action(0, ActionVerb.ChooseNeowBlessing),
            Action(1, ActionVerb.MapMove),
            Action(2, ActionVerb.PlayCard),
            Action(3, ActionVerb.MapMove),
        };
        var recording = Recording(actions);
        var plan = new StubPlan(actions.Take(4).ToList());

        Assert.False(RetailPlayback.CanWalk(plan));
        Assert.Equal(
            RetailPlayback.FirstRefusal(recording, boundarySeq: 3)!.Seq,
            RetailPlayback.FirstRefusal(plan)!.Seq);
    }

    /// <summary>A plan carrying only a prefix, which is all this rule reads of one.</summary>
    private sealed record StubPlan(IReadOnlyList<ActionRecord> PrefixActions) : IBoundaryPlan
    {
        public string Kind => ReplayBoundary.CombatStartKind;

        public int BoundarySeq => PrefixActions[^1].Seq;

        public Checkpoint Boundary => throw new NotSupportedException();

        public SnapshotCacheKey SnapshotKey => throw new NotSupportedException();

        public int? Fight => 1;

        public int? Floor => null;

        public bool Authorises(int stepIndex, ActionRecord action) => true;

        public string Describe() => "the start of fight 1";
    }
}
