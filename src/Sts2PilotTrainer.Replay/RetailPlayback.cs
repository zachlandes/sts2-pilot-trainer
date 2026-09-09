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

    /// <summary>The same question of a plan a host is about to walk, so the surface that
    /// offers a boundary and the host that walks to it read one rule.</summary>
    public static ActionRecord? FirstRefusal(IBoundaryPlan plan) =>
        plan.PrefixActions
            .OrderBy(action => action.Seq)
            .FirstOrDefault(action => !Verbs.Contains(action.Verb));

    /// <summary>Whether a running client can walk this recording to that boundary.</summary>
    public static bool CanReach(ReplayManifest recording, int boundarySeq) =>
        FirstRefusal(recording, boundarySeq) is null;

    /// <summary>Whether a running client can walk this plan.</summary>
    public static bool CanWalk(IBoundaryPlan plan) => FirstRefusal(plan) is null;
}
