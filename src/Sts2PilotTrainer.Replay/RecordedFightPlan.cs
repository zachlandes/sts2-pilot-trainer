namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What a host has to do, in order, to stand somebody at one boundary of a recording.
///
/// Two plans implement it and they stay two types: a fight's boundary and a floor's
/// are different moments, proved by different fields, found by different rules. What
/// they share is the shape a host consumes - a prefix of the recording's own
/// decisions, the boundary they end at, and the authority to say which decision comes
/// next - and that is what this names, so <c>RecordedFightEntry</c> can walk either
/// without knowing which it has.
///
/// It adds no rule of its own. Every refusal is still the plan's.
/// </summary>
public interface IBoundaryPlan
{
    /// <summary>Which kind of boundary this plan ends at, from
    /// <see cref="ReplayBoundary.Kinds"/>.</summary>
    string Kind { get; }

    /// <summary>The recording's decisions before the boundary, in order. Every one of
    /// them is executed; none is optional, and none is the host's.</summary>
    IReadOnlyList<ActionRecord> PrefixActions { get; }

    /// <summary>The sequence number the boundary is immediately after.</summary>
    int BoundarySeq { get; }

    /// <summary>What the recording observed there.</summary>
    Checkpoint Boundary { get; }

    /// <summary>Identity of the snapshot this plan reproduces.</summary>
    SnapshotCacheKey SnapshotKey { get; }

    /// <summary>Which fight of the run this is, for the combat kinds; null
    /// otherwise.</summary>
    int? Fight { get; }

    /// <summary>Which floor this arrives on, for a floor entry; null otherwise.</summary>
    int? Floor { get; }

    /// <summary>
    /// Whether an action is one this plan authorises at this point in the journey.
    ///
    /// The plan is the whole authority on what may happen before the boundary, which
    /// is what lets a host refuse a decision that came from anywhere else without
    /// having to know what a screen looks like.
    /// </summary>
    bool Authorises(int stepIndex, ActionRecord action);

    /// <summary>How a diagnostic names this plan's destination to a person.</summary>
    string Describe();
}

/// <summary>
/// What a host has to do, in order, to stand a player in one of the recording's
/// fights: the decisions the recording made before that fight, and the boundary the
/// fight starts at.
///
/// Derived from the manifest and from nothing else, so the whole plan can be read
/// and tested on a machine that does not own the game. It decides nothing: every
/// step is an action the recording already contains, in the sequence the recording
/// already fixes, and the boundary is the same combat-start boundary
/// <see cref="CombatProjection.CoverageOf"/> reads back out of a replay. A host
/// checks the two against each other rather than trusting either alone - see
/// <see cref="BoundaryEquality"/>.
///
/// Which fight is asked for by ordinal, and the manifest's
/// <see cref="ReplayManifest.Boundaries"/> is what says where that fight begins.
/// The first fight has a second, older way of being found - the first action that
/// could only have been taken inside a fight - which is how a recording's first
/// boundary gets derived before any boundary exists. Nothing past the first fight
/// can be found that way, and guessing is not on offer.
///
/// It refuses rather than guessing. A manifest that reaches no such fight, or that
/// reaches one without recording what the fight opened with, has nothing an entry
/// could be proved correct against, and entering it anyway is exactly the confident
/// wrong answer this project exists to prevent.
/// </summary>
public sealed record RecordedFightPlan : IBoundaryPlan
{
    /// <inheritdoc/>
    public string Kind => ReplayBoundary.CombatStartKind;

    /// <inheritdoc/>
    public int? Floor => null;

    /// <inheritdoc/>
    public string Describe() =>
        $"the start of fight {FightOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>The verbs that can only be issued inside a fight. The first of them
    /// in the history is what makes the action before it the one that entered the
    /// fight.</summary>
    private static readonly ActionVerb[] CombatVerbs = [ActionVerb.PlayCard, ActionVerb.EndTurn];

    /// <summary>Canonical fields that only exist while a fight is live. A boundary
    /// checkpoint has to name at least one, or it is not an observation of a fight
    /// starting.</summary>
    private static readonly string[] LiveCombatFields =
        ["combat.turn", "combat.hand", "combat.encounter", "combat.enemy_count"];

    /// <summary>Which fight of the run this plan reaches, counting from 1.</summary>
    public required int FightOrdinal { get; init; }

    /// <inheritdoc/>
    public int? Fight => FightOrdinal;

    /// <inheritdoc cref="IBoundaryPlan.PrefixActions"/>
    public required IReadOnlyList<ActionRecord> PrefixActions { get; init; }

    /// <summary>The sequence number the fight is live after. The last pre-fight
    /// action's own sequence number: the action that enters the room is the action
    /// that starts the fight.</summary>
    public required int BoundarySeq { get; init; }

    /// <summary>What the recording observed at that boundary. This is the thing a
    /// live entry is proved equal to before a player is given the controls.</summary>
    public required Checkpoint Boundary { get; init; }

    /// <summary>Identity of the combat-start snapshot this plan reproduces, so an
    /// entry and a cached snapshot can be compared without either one guessing which
    /// history the other came from.</summary>
    public required SnapshotCacheKey SnapshotKey { get; init; }

    /// <summary>The plan for the recording's first fight.</summary>
    public static RecordedFightPlan For(ReplayManifest manifest) => For(manifest, fight: 1);

    /// <summary>The plan for the fight with this ordinal, counting from 1.</summary>
    public static RecordedFightPlan For(ReplayManifest manifest, int fight)
    {
        if (fight < 1)
        {
            throw new ManifestException(
                $"Fight {fight.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not a fight. " +
                "Fights are numbered from 1, in the order the run played them.");
        }

        var ordered = manifest.Actions.OrderBy(action => action.Seq).ToList();
        var combatStartSeq = CombatStartSeqOf(manifest, ordered, fight);
        var prefix = ordered.TakeWhile(action => action.Seq <= combatStartSeq).ToList();

        var boundary = manifest.Checkpoints
            .Where(checkpoint => checkpoint.AfterSeq == combatStartSeq)
            .FirstOrDefault(checkpoint => checkpoint.Expect.Keys.Any(LiveCombatFields.Contains));

        if (boundary is null)
        {
            throw new ManifestException(
                $"This recording enters fight {fight.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"after action {combatStartSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)} and " +
                "records nothing it observed there. Entering it would put a player in a fight nobody could show " +
                "was the recorded one, which is the failure this arbiter exists to prevent. Add a checkpoint at " +
                "that boundary.");
        }

        return new RecordedFightPlan
        {
            FightOrdinal = fight,
            PrefixActions = prefix,
            BoundarySeq = combatStartSeq,
            Boundary = boundary,
            SnapshotKey = SnapshotCacheKey.For(manifest, combatStartSeq),
        };
    }

    /// <summary>
    /// Where the recording's first fight begins, read the way the format read it
    /// before boundaries were a list: the action before the first that could only have
    /// been taken inside a fight.
    ///
    /// Separate from <see cref="For(ReplayManifest)"/> because migrating an older
    /// manifest needs the sequence number and nothing else. A plan additionally
    /// requires the recording to have observed something at that boundary, which is a
    /// condition on entering a fight rather than on reading where one starts.
    /// </summary>
    internal static int FirstCombatStartSeq(ReplayManifest manifest) =>
        CombatStartSeqOf(manifest, manifest.Actions.OrderBy(action => action.Seq).ToList(), fight: 1);

    private static int CombatStartSeqOf(ReplayManifest manifest, IReadOnlyList<ActionRecord> ordered, int fight)
    {
        if (manifest.BoundaryAt(ReplayBoundary.CombatStartKind, fight: fight) is { } declared)
        {
            return declared.AfterSeq;
        }

        if (fight > 1)
        {
            throw new ManifestException(
                $"This recording declares no combat-start boundary for fight " +
                $"{fight.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Where a later fight " +
                "begins is a fact about what the engine did, so it is derived by replaying the run and written " +
                "into the manifest's boundaries - never guessed from the shape of the history.");
        }

        // The recording's first boundary has to be findable before any boundary
        // exists, because deriving it is what produces the first entry in the list.
        // The first action that could only have been taken inside a fight is what
        // makes the action before it the one that entered the fight.
        var firstCombatAction = ordered.ToList().FindIndex(action => CombatVerbs.Contains(action.Verb));

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

        return ordered[firstCombatAction - 1].Seq;
    }

    /// <inheritdoc/>
    public bool Authorises(int stepIndex, ActionRecord action) =>
        stepIndex >= 0 &&
        stepIndex < PrefixActions.Count &&
        PrefixActions[stepIndex].Seq == action.Seq;
}

/// <summary>
/// What a host has to do to stand a player at the moment a recording arrived on one
/// of its floors, before whatever that floor turned out to be.
///
/// The same shape as <see cref="RecordedFightPlan"/> and deliberately not the same
/// type: the boundary it ends at is a different kind of moment and is proved by
/// different fields. A fight's boundary is observed through what the fight opened
/// with; a floor's is observed through where the run now stands - the floor number
/// and the position on the map - because none of the combat fields exist yet.
///
/// Where a floor begins is never inferred here. The recording's
/// <see cref="ReplayManifest.Boundaries"/> says which action arrived on it, derived
/// by replaying the run; reading it off the shape of the history would be inventing
/// a moment nobody measured.
/// </summary>
public sealed record FloorEntryPlan : IBoundaryPlan
{
    /// <inheritdoc/>
    public string Kind => ReplayBoundary.FloorEntryKind;

    /// <inheritdoc/>
    public int? Fight => null;

    /// <inheritdoc/>
    public string Describe() =>
        $"arrival on floor {FloorNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Canonical fields that place a run on the map. A floor-entry boundary
    /// checkpoint has to name both, or it is not an observation of arriving
    /// anywhere.</summary>
    public static readonly string[] RequiredBoundaryFields = ["run.total_floor", "run.map_coord"];

    /// <summary>Which floor of the run this plan reaches.</summary>
    public required int FloorNumber { get; init; }

    /// <inheritdoc/>
    public int? Floor => FloorNumber;

    /// <inheritdoc cref="IBoundaryPlan.PrefixActions"/>
    public required IReadOnlyList<ActionRecord> PrefixActions { get; init; }

    /// <summary>The sequence number the run stands on this floor after: the map move
    /// that entered it.</summary>
    public required int BoundarySeq { get; init; }

    /// <summary>What the recording observed on arrival.</summary>
    public required Checkpoint Boundary { get; init; }

    /// <summary>Identity of the snapshot this plan reproduces.</summary>
    public required SnapshotCacheKey SnapshotKey { get; init; }

    public static FloorEntryPlan For(ReplayManifest manifest, int floor)
    {
        var declared = manifest.BoundaryAt(ReplayBoundary.FloorEntryKind, floor: floor)
            ?? throw new ManifestException(
                $"This recording declares no floor-entry boundary for floor " +
                $"{floor.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Which action arrives on " +
                "a floor is a fact about what the engine did; it is derived by replaying the run and written " +
                "into the manifest's boundaries, never read off the shape of the history.");

        var ordered = manifest.Actions.OrderBy(action => action.Seq).ToList();
        var entry = ordered.FirstOrDefault(action => action.Seq == declared.AfterSeq)
            ?? throw new ManifestException(
                $"This recording's floor-{floor.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"boundary names action {declared.AfterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                "which is not in its history.");

        if (entry.Verb != ActionVerb.MapMove)
        {
            throw new ManifestException(
                $"This recording's floor-{floor.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"boundary names action {entry.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"({entry.Verb}), and a floor is arrived on by moving on the map. A boundary pointing at any " +
                "other action is not the moment it claims to be.");
        }

        var boundary = manifest.Checkpoints
            .Where(checkpoint => checkpoint.AfterSeq == declared.AfterSeq)
            .FirstOrDefault(checkpoint =>
                RequiredBoundaryFields.All(field => checkpoint.Expect.ContainsKey(field)))
            ?? throw new ManifestException(
                $"This recording arrives on floor {floor.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                $"after action {declared.AfterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)} and " +
                $"records no checkpoint there naming {string.Join(" and ", RequiredBoundaryFields)}. A floor " +
                "arrival is proved by where the run stands, not by what a fight opened with, and standing a " +
                "player somewhere nobody observed is the failure this arbiter exists to prevent.");

        var expectedFloor = floor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(boundary.Expect["run.total_floor"].Value, expectedFloor, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"This recording declares a boundary for floor {expectedFloor}, but the checkpoint there says " +
                $"run.total_floor is {boundary.Expect["run.total_floor"].Value}. A floor plan cannot hand over " +
                "a checkpoint for another floor.");
        }

        return new FloorEntryPlan
        {
            FloorNumber = floor,
            PrefixActions = ordered.TakeWhile(action => action.Seq <= declared.AfterSeq).ToList(),
            BoundarySeq = declared.AfterSeq,
            Boundary = boundary,
            SnapshotKey = SnapshotCacheKey.For(manifest, declared.AfterSeq),
        };
    }

    /// <inheritdoc/>
    public bool Authorises(int stepIndex, ActionRecord action) =>
        stepIndex >= 0 &&
        stepIndex < PrefixActions.Count &&
        PrefixActions[stepIndex].Seq == action.Seq;
}
