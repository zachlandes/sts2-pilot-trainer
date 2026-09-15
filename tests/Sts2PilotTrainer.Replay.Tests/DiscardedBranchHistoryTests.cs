namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The history a discarded branch was played from, read from the recording alone.
///
/// The recording keeps the continued history as it stands now, and every rollback
/// that cut it, in order. A branch cut early was played from decisions a later
/// rollback may since have remade, and those decisions are then the later branch's
/// own; what a branch replays from has to be read back through that, or the branch
/// runs against a history the player never played it on.
/// </summary>
public sealed class DiscardedBranchHistoryTests
{
    /// <summary>
    /// Three rollbacks, each behind the last: the first at 20, then one at 10, then
    /// one at 5. The first branch was played from decisions 0-20 as they stood then;
    /// 6-10 of those are now the third branch's, 11-20 the second's, and only 0-5
    /// are still the continued history's.
    /// </summary>
    [Fact]
    public void ADecisionIsHeldByTheNearestLaterBranchThatRewoundBehindIt()
    {
        var continued = History("continued", 0, 30);
        var branches = new[]
        {
            Branch(20, History("first", 21, 25)),
            Branch(10, History("second", 11, 27)),
            Branch(5, History("third", 6, 14)),
        };

        Assert.Equal("continued", Verb(branches, 0, continued, 5));
        Assert.Equal("third", Verb(branches, 0, continued, 6));
        Assert.Equal("third", Verb(branches, 0, continued, 10));
        Assert.Equal("second", Verb(branches, 0, continued, 11));
        Assert.Equal("second", Verb(branches, 0, continued, 20));

        Assert.Equal("continued", Verb(branches, 1, continued, 5));
        Assert.Equal("third", Verb(branches, 1, continued, 10));
        Assert.Equal("continued", Verb(branches, 2, continued, 5));

        var prefix = DiscardedBranchHistory.PrefixOf(branches, 0, continued);
        Assert.Equal(Enumerable.Range(0, 21), prefix.Select(action => action.Seq));
        Assert.Equal(
            Enumerable.Repeat("continued", 6).Concat(Enumerable.Repeat("third", 5)).Concat(Enumerable.Repeat("second", 10)),
            prefix.Select(action => action.Args["from"]));
        Assert.Equal(
            Enumerable.Repeat("continued", 6).Concat(Enumerable.Repeat("third", 5)),
            DiscardedBranchHistory.PrefixOf(branches, 1, continued).Select(action => action.Args["from"]));
        Assert.Equal(
            Enumerable.Repeat("continued", 6),
            DiscardedBranchHistory.PrefixOf(branches, 2, continued).Select(action => action.Args["from"]));
    }

    [Fact]
    public void TheContinuedHistoryHoldsADecisionOnlyWhereNoLaterBranchRewoundBehindIt()
    {
        var branches = new[]
        {
            Branch(20, History("first", 21, 25)),
            Branch(10, History("second", 11, 27)),
        };

        Assert.True(DiscardedBranchHistory.ContinuedHistoryHolds(branches, 0, -1));
        Assert.True(DiscardedBranchHistory.ContinuedHistoryHolds(branches, 0, 10));
        Assert.False(DiscardedBranchHistory.ContinuedHistoryHolds(branches, 0, 11));
        Assert.False(DiscardedBranchHistory.ContinuedHistoryHolds(branches, 0, 20));
        Assert.True(DiscardedBranchHistory.ContinuedHistoryHolds(branches, 1, 10));
        Assert.True(DiscardedBranchHistory.ContinuedHistoryHolds(branches, 1, 27));
    }

    [Fact]
    public void ABranchFromTheOpeningReadingHasNoPrefix()
    {
        var branches = new[] { Branch(-1, History("first", 0, 3)) };
        Assert.Empty(DiscardedBranchHistory.PrefixOf(branches, 0, History("continued", 0, 5)));
    }

    /// <summary>A later branch that rewound behind a decision holds it, and the
    /// continued history's decision at that ordinal is never read in its place: a
    /// branch whose actions stop short is refused, not patched from the remade
    /// history.</summary>
    [Fact]
    public void ADecisionTheRewindingBranchDoesNotHoldIsRefusedRatherThanReadFromTheContinuedHistory()
    {
        var continued = History("continued", 0, 30);
        var branches = new[]
        {
            Branch(20, History("first", 21, 25)),
            Branch(10, History("second", 11, 15)),
        };

        Assert.Null(DiscardedBranchHistory.ActionAsTheBranchSawIt(branches, 0, continued, 16));
        var refusal = Assert.Throws<ManifestException>(() => DiscardedBranchHistory.PrefixOf(branches, 0, continued));
        Assert.Contains("decision 16", refusal.Message, StringComparison.Ordinal);
    }

    private static string Verb(
        IReadOnlyList<DiscardedBranch> branches, int branchIndex, IReadOnlyList<ActionRecord> continued, int seq) =>
        DiscardedBranchHistory.ActionAsTheBranchSawIt(branches, branchIndex, continued, seq)!.Args["from"];

    private static IReadOnlyList<ActionRecord> History(string from, int firstSeq, int lastSeq) =>
        Enumerable.Range(firstSeq, lastSeq - firstSeq + 1)
            .Select(seq => Fixtures.Action(seq, ActionVerb.EndTurn, ("from", from)))
            .ToList();

    private static DiscardedBranch Branch(int rollbackToSeq, IReadOnlyList<ActionRecord> actions) => new()
    {
        RollbackToSeq = rollbackToSeq,
        RollbackToDigest = "sha256:" + new string('0', 64),
        Actions = actions,
        Trace = new ReplayTrace { Steps = [] },
    };
}
