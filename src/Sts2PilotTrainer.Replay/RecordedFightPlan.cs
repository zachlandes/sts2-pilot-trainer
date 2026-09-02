namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What a host has to do, in order, to stand a player in the recording's first
/// fight: the decisions the recording made before that fight, and the boundary the
/// fight starts at.
///
/// Derived from the manifest and from nothing else, so the whole plan can be read
/// and tested on a machine that does not own the game. It decides nothing: every
/// step is an action the recording already contains, in the sequence the recording
/// already fixes, and the boundary is the same combat-start boundary
/// <see cref="CombatProjection.CoverageOf"/> reads back out of a replay. A host
/// checks the two against each other rather than trusting either alone - see
/// <see cref="CombatStartEquality"/>.
///
/// It refuses rather than guessing. A manifest that reaches no fight, or that
/// reaches one without recording what the fight opened with, has nothing an entry
/// could be proved correct against, and entering it anyway is exactly the confident
/// wrong answer this project exists to prevent.
/// </summary>
public sealed record RecordedFightPlan
{
    /// <summary>The verbs that can only be issued inside a fight. The first of them
    /// in the history is what makes the action before it the one that entered the
    /// fight.</summary>
    private static readonly ActionVerb[] CombatVerbs = [ActionVerb.PlayCard, ActionVerb.EndTurn];

    /// <summary>Canonical fields that only exist while a fight is live. A boundary
    /// checkpoint has to name at least one, or it is not an observation of a fight
    /// starting.</summary>
    private static readonly string[] LiveCombatFields =
        ["combat.turn", "combat.hand", "combat.encounter", "combat.enemy_count"];

    /// <summary>The recording's decisions before the fight, in order. Every one of
    /// them is executed; none is optional, and none is ours.</summary>
    public required IReadOnlyList<ActionRecord> PrefightActions { get; init; }

    /// <summary>The sequence number the fight is live after. The last pre-fight
    /// action's own sequence number: the action that enters the room is the action
    /// that starts the fight.</summary>
    public required int CombatStartSeq { get; init; }

    /// <summary>What the recording observed at that boundary. This is the thing a
    /// live entry is proved equal to before a player is given the controls.</summary>
    public required Checkpoint Boundary { get; init; }

    /// <summary>Identity of the combat-start snapshot this plan reproduces, so an
    /// entry and a cached snapshot can be compared without either one guessing which
    /// history the other came from.</summary>
    public required SnapshotCacheKey SnapshotKey { get; init; }

    public static RecordedFightPlan For(ReplayManifest manifest)
    {
        var ordered = manifest.Actions.OrderBy(action => action.Seq).ToList();
        var firstCombatAction = ordered.FindIndex(action => CombatVerbs.Contains(action.Verb));

        if (firstCombatAction < 0)
        {
            throw new ManifestException(
                "This recording never plays a card or ends a turn, so it never reaches a fight and there is " +
                "no combat to enter. The supported boundary is the start of a fight.");
        }

        if (firstCombatAction == 0)
        {
            throw new ManifestException(
                "This recording's first action is already inside a fight, so the history does not contain the " +
                "decisions that led to it. A fight can only be entered by replaying the run that reaches it.");
        }

        var prefight = ordered.Take(firstCombatAction).ToList();
        var combatStartSeq = prefight[^1].Seq;

        var boundary = manifest.Checkpoints
            .Where(checkpoint => checkpoint.AfterSeq == combatStartSeq)
            .FirstOrDefault(checkpoint => checkpoint.Expect.Keys.Any(LiveCombatFields.Contains));

        if (boundary is null)
        {
            throw new ManifestException(
                $"This recording enters a fight after action {combatStartSeq} and records nothing it observed " +
                "there. Entering it would put a player in a fight nobody could show was the recorded one, " +
                "which is the failure this arbiter exists to prevent. Add a checkpoint at that boundary.");
        }

        return new RecordedFightPlan
        {
            PrefightActions = prefight,
            CombatStartSeq = combatStartSeq,
            Boundary = boundary,
            SnapshotKey = SnapshotCacheKey.For(manifest, combatStartSeq),
        };
    }

    /// <summary>
    /// Whether an action is one this plan authorises at this point in the journey.
    ///
    /// The plan is the whole authority on what may happen before the fight, which is
    /// what lets a host refuse a decision that came from anywhere else without
    /// having to know what a screen looks like.
    /// </summary>
    public bool Authorises(int stepIndex, ActionRecord action) =>
        stepIndex >= 0 &&
        stepIndex < PrefightActions.Count &&
        PrefightActions[stepIndex].Seq == action.Seq;
}
