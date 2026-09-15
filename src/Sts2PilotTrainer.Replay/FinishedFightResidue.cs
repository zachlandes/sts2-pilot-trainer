namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What a format-6 recording carries of a fight that had ended, and format 7 does
/// not.
///
/// Through format 6 the canonical projection kept emitting a finished fight's
/// combat fields - its turn, energy, empty piles, the encounter, an enemy count of
/// zero - into every reading taken after it until the next fight replaced it, and
/// <c>combat.outcome</c> read <c>victory</c> all the way to the next combat start.
/// The game's own save carries none of that, so a run continued from a save stood at
/// the same place with a fresh combat state or none, and every reading it made after
/// the Continue disagreed with the engine's replay of the same history in exactly
/// those fields. Format 7 projects nothing of a fight outside a live one: after a
/// fight, <c>combat.in_progress</c> is false and <c>combat.outcome</c> is
/// <c>victory</c> only while the run still stands in the fight's own room, then
/// <c>none</c>.
///
/// This is the one owner of the difference. The in-memory reader takes a format-6
/// checkpoint's residue expectations away, because they describe a state format 7
/// never produces; it cannot take a digest anywhere, because a digest is a hash of
/// the whole state, so a format-6 boundary digest at a floor arrival with no live
/// fight after the first fight is a claim in the older unit. <see cref="PredatesThisProjection"/>
/// says which those are, so the gate can name the cause of a mismatch there and
/// <c>migrate-manifest --derive-boundaries</c> can re-derive exactly them, and
/// nothing else, from a verified replay.
/// </summary>
public static class FinishedFightResidue
{
    /// <summary>The format that first projected nothing of a finished fight.</summary>
    public const int FirstFormatWithout = 7;

    /// <summary>The one combat field format 7 keeps outside a live fight beside the
    /// outcome, and the field that says whether an expectation was taken outside one.</summary>
    public const string LiveField = "combat.in_progress";

    /// <summary>Whether a canonical field is one an older projection emitted for a
    /// finished fight: every <c>combat.</c> field but the live flag. The outcome is
    /// among them, because the older projection read <c>victory</c> on every floor
    /// after a fight and this one reads it only in the fight's own room.</summary>
    public static bool IsResidueField(string field) =>
        field.StartsWith("combat.", StringComparison.Ordinal) &&
        !string.Equals(field, LiveField, StringComparison.Ordinal);

    /// <summary>
    /// The manifest with every format-6 residue expectation removed: at each
    /// checkpoint whose expectations say no fight is live, every combat field but
    /// that one. A checkpoint taken inside a live fight is untouched, and so is one
    /// that says nothing about whether a fight is live - a video reading of a health
    /// bar mid-fight names no <c>combat.in_progress</c>, and dropping its combat
    /// fields would drop an observation.
    /// </summary>
    public static ReplayManifest StripExpectations(ReplayManifest manifest) =>
        manifest with
        {
            Checkpoints = manifest.Checkpoints.Select(checkpoint =>
                    TakenOutsideALiveFight(checkpoint)
                        ? checkpoint with
                        {
                            Expect = new SortedDictionary<string, Fact<string>>(
                                checkpoint.Expect
                                    .Where(field => !IsResidueField(field.Key))
                                    .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
                                StringComparer.Ordinal),
                        }
                        : checkpoint)
                .ToList(),
        };

    private static bool TakenOutsideALiveFight(Checkpoint checkpoint) =>
        checkpoint.Expect.TryGetValue(LiveField, out var live) &&
        string.Equals(live.Value, "false", StringComparison.Ordinal);

    /// <summary>
    /// Whether a declared boundary's digest was produced under a projection that
    /// carried a finished fight, so that this build cannot reproduce it and a
    /// mismatch there says nothing about the run.
    ///
    /// Which format a manifest was written in is <see cref="ReplayManifest.WrittenIn"/>:
    /// a native recording's own note, or the version the file declared when it was
    /// read. Of one written before format 7, exactly the floor arrivals after the
    /// first fight that opened no fight of their own carried the residue: a combat
    /// start and a turn start are read inside a live fight, whose projection did not
    /// change, and so is an arrival the same map move dealt a fight on - declared as
    /// a combat start at the same action, or read as live by a checkpoint there,
    /// which is how a history that stops inside its last fight names that fight;
    /// before the first fight there was nothing to carry.
    /// </summary>
    public static bool PredatesThisProjection(ReplayManifest manifest, ReplayBoundary boundary)
    {
        if (manifest.WrittenIn >= FirstFormatWithout) return false;
        if (!string.Equals(boundary.Kind, ReplayBoundary.FloorEntryKind, StringComparison.Ordinal)) return false;

        var combatStarts = manifest.Boundaries
            .Where(candidate => candidate.IsCombatStart)
            .Select(candidate => candidate.AfterSeq)
            .ToList();
        var liveThere = combatStarts.Contains(boundary.AfterSeq) ||
                        manifest.Checkpoints.Any(checkpoint =>
                            checkpoint.AfterSeq == boundary.AfterSeq &&
                            checkpoint.Expect.TryGetValue(LiveField, out var live) &&
                            string.Equals(live.Value, "true", StringComparison.Ordinal));
        return combatStarts.Any(seq => seq < boundary.AfterSeq) && !liveThere;
    }
}
