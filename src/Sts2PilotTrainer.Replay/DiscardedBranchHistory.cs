namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The history as it stood when a discarded branch was made.
///
/// A branch is the decisions a rollback removed, and it was played from the history
/// as it stood then. A later rollback that rewound behind the branch's own return
/// point remade that history: the decisions the branch was played from are now the
/// later branch's own actions, and the continued history holds what was played in
/// their place. So the decision at any ordinal, as a branch saw it, is held by the
/// nearest later branch whose rollback rewound behind that ordinal, and by the
/// continued history where none did. That later branch always reaches the ordinal:
/// every rollback between the two returned to it or past it, so the history the
/// later branch was cut from still stood there.
///
/// This is the one reader of that rule. The validator holds a branch's return point
/// and its verified boundary to it, and the arbiter replays a branch from it; a
/// branch replayed from the remade history instead runs its decisions in rooms the
/// player never stood in.
/// </summary>
public static class DiscardedBranchHistory
{
    /// <summary>The decision at <paramref name="seq"/> as branch
    /// <paramref name="branchIndex"/> saw it, or null where nothing holds it.</summary>
    public static ActionRecord? ActionAsTheBranchSawIt(
        IReadOnlyList<DiscardedBranch> branches, int branchIndex, IReadOnlyList<ActionRecord> actions, int seq)
    {
        for (var later = branchIndex + 1; later < branches.Count; later++)
        {
            var candidate = branches[later];
            if (candidate.RollbackToSeq < seq)
            {
                return candidate.Actions.FirstOrDefault(action => action.Seq == seq);
            }
        }

        return actions.FirstOrDefault(action => action.Seq == seq);
    }

    /// <summary>Whether the continued history still holds decision
    /// <paramref name="seq"/> as branch <paramref name="branchIndex"/> saw it: no
    /// later branch rewound behind it. Where it does, what was verified on the
    /// continued history at that ordinal - a checkpoint, a boundary digest - is the
    /// branch's too.</summary>
    public static bool ContinuedHistoryHolds(IReadOnlyList<DiscardedBranch> branches, int branchIndex, int seq)
    {
        for (var later = branchIndex + 1; later < branches.Count; later++)
        {
            if (branches[later].RollbackToSeq < seq) return false;
        }

        return true;
    }

    /// <summary>The decisions branch <paramref name="branchIndex"/> was played from,
    /// in order, up to and including the one it returned to.</summary>
    /// <exception cref="ManifestException">An ordinal on that prefix is held by
    /// nothing.</exception>
    public static IReadOnlyList<ActionRecord> PrefixOf(
        IReadOnlyList<DiscardedBranch> branches, int branchIndex, IReadOnlyList<ActionRecord> actions)
    {
        var branch = branches[branchIndex];
        var prefix = new List<ActionRecord>(Math.Max(branch.RollbackToSeq + 1, 0));
        for (var seq = 0; seq <= branch.RollbackToSeq; seq++)
        {
            prefix.Add(
                ActionAsTheBranchSawIt(branches, branchIndex, actions, seq)
                ?? throw new ManifestException(
                    $"Discarded branch {branchIndex} returned to decision {branch.RollbackToSeq}, and neither " +
                    $"the continued history nor a later branch holds decision {seq} as it stood then."));
        }

        return prefix;
    }
}
