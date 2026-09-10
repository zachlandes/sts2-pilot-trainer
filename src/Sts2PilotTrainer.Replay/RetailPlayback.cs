namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What a running retail client can replay, and how far into a recording that
/// reaches.
///
/// Two owners need this one answer and they are in different assemblies, which is why
/// it is here rather than on the driver. <c>RunDriver</c> enforces it - a verb outside
/// this set is refused inside a running game - and the run library reads it to decide
/// which of a recording's boundaries it may offer. A copy of the set on either side
/// is how a library came to offer a floor the client aborts on, after the run was
/// built and the player had watched two decisions.
///
/// <para><b>It is a fact about the client, not about the format.</b> The headless
/// arbiter replays every verb the format names; this set is narrower because a fight
/// is the player's, because two prompts have no seam a client can reach, and because
/// the screens between fights have no client-side reveal yet. See
/// <c>docs/headless-fidelity.md</c> and <c>docs/proof-of-concept-path.md</c>.</para>
///
/// <para><b>Nothing here authorises anything.</b> The driver still refuses on its own
/// account at the moment it would issue a verb; this only lets a surface ask the
/// question before it offers somebody a place to stand.</para>
/// </summary>
public static class RetailPlayback
{
    /// <summary>
    /// The verbs a running retail client issues.
    ///
    /// The supported decisions before a fight, and nothing in one. An opening blessing
    /// and an event option are the two the reveal can point at; a map move is how a run
    /// enters a room; a card taken off the screen one of those opened is a decision the
    /// game draws its own screen for. Everything else - a card played, a turn ended,
    /// loot claimed, a rest taken, a shop bought from - is either the player's own fight
    /// or a screen this client has no reveal for.
    /// </summary>
    public static IReadOnlyList<ActionVerb> Verbs { get; } =
    [
        ActionVerb.ChooseNeowBlessing,
        ActionVerb.ChooseEventOption,
        ActionVerb.MapMove,
        ActionVerb.SelectCardFromScreen,
    ];

    /// <summary>
    /// The first decision on the way to this boundary that a running client cannot
    /// issue, or null when it can walk the whole way.
    ///
    /// The action rather than a bare bool, so a caller that wants to say what stopped it
    /// has the sequence number and the verb without asking twice.
    /// </summary>
    /// <param name="recording">The recording being walked.</param>
    /// <param name="boundarySeq">The sequence number the boundary is immediately after,
    /// which is the last action of the prefix a host executes. A boundary the recording
    /// declares before its first action - the run's own start - has none and is reached
    /// trivially.</param>
    public static ActionRecord? FirstRefusal(ReplayManifest recording, int boundarySeq) =>
        recording.Actions
            .Where(action => action.Seq <= boundarySeq)
            .OrderBy(action => action.Seq)
            .FirstOrDefault(action => !Verbs.Contains(action.Verb));

    /// <summary>
    /// The floor arrivals of this recording a run can be restored to, latest first.
    ///
    /// An arrival is restorable where a fight is live at it, which is the whole finding
    /// of the floor-entry measurement (<c>FloorEntrySnapshotEligibility</c>): the game's
    /// own save carries no combat, so an arrival with a finished fight still attached to
    /// the live run restores into the same run and a different canonical state. This is
    /// the manifest's reading of that rule - a combat start the recording declares at
    /// the same action as the arrival, because the map move that arrived is the move
    /// that dealt the fight - and it offers rather than authorises. <c>floor-snapshot</c>
    /// asks the engine the same question of the replayed state before anything is
    /// cached, and a restore is proved at the boundary like a walk is.
    /// </summary>
    public static IReadOnlyList<RestorableArrival> RestorableArrivals(ReplayManifest recording)
    {
        var combatStarts = recording.Boundaries
            .Where(boundary => boundary.IsCombatStart)
            .Select(boundary => boundary.AfterSeq)
            .ToHashSet();

        return recording.Boundaries
            .Where(boundary => boundary.IsFloorEntry && boundary.Floor is not null)
            .Where(boundary => combatStarts.Contains(boundary.AfterSeq))
            .Select(boundary => new RestorableArrival(boundary.Floor!.Value, boundary.AfterSeq))
            .OrderByDescending(arrival => arrival.AfterSeq)
            .ToList();
    }

    /// <summary>
    /// How a running client reaches the boundary after this action, or what stops it.
    ///
    /// Pure, and read from the recording alone: which decisions the client issues and
    /// which arrivals the history can be restored to are both facts about the history,
    /// so a row is offered or refused on the history and never on whether a cache file
    /// happens to be present. The library asks this for every place it offers and the
    /// journey asks it once and executes the answer, so the two cannot disagree about a
    /// boundary.
    ///
    /// The walk comes first where it can: a player watching the recording's decisions
    /// is the point of the journey, and a restore skips exactly that. Only the latest
    /// restorable arrival before the boundary is tried, because an earlier one's tail
    /// contains the later one's and could only meet the same refusal sooner.
    /// </summary>
    /// <param name="boundarySeq">The sequence number the boundary is immediately after,
    /// as <see cref="FirstRefusal"/> takes it.</param>
    public static PlaybackRoute RouteTo(ReplayManifest recording, int boundarySeq)
    {
        if (FirstRefusal(recording, boundarySeq) is not { } refusedOnTheWalk) return new PlaybackRoute.Walk();

        var arrival = RestorableArrivals(recording).FirstOrDefault(candidate => candidate.AfterSeq <= boundarySeq);
        if (arrival is null) return new PlaybackRoute.Unreachable(refusedOnTheWalk);

        var refusedAfterRestore = recording.Actions
            .Where(action => action.Seq > arrival.AfterSeq && action.Seq <= boundarySeq)
            .OrderBy(action => action.Seq)
            .FirstOrDefault(action => !Verbs.Contains(action.Verb));
        if (refusedAfterRestore is not null) return new PlaybackRoute.Unreachable(refusedAfterRestore);

        return arrival.AfterSeq == boundarySeq
            ? new PlaybackRoute.Restore(arrival.Floor, arrival.AfterSeq)
            : new PlaybackRoute.RestoreThenWalk(arrival.Floor, arrival.AfterSeq);
    }

    /// <summary>The same, for a plan: the route to the boundary the plan ends at.</summary>
    public static PlaybackRoute RouteTo(ReplayManifest recording, IBoundaryPlan plan) =>
        RouteTo(recording, plan.BoundarySeq);
}

/// <summary>One floor arrival a run can be restored to: the floor, and the action the
/// arrival is immediately after.</summary>
public sealed record RestorableArrival(int Floor, int AfterSeq);
