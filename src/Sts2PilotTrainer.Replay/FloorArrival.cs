namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Where a run stands when it arrives on a floor, read off the decision that moved
/// it there.
///
/// <see cref="FloorEntryPlan"/> refuses to stand anybody on a floor whose boundary
/// has no checkpoint naming <see cref="FloorEntryPlan.RequiredBoundaryFields"/>, so a
/// manifest without one carries a coordinate a host will abort on in front of a
/// player. The two fields are not a reading anybody has to take again: a map move
/// records the act, row and column it moved to, and
/// <c>CanonicalStateProjection</c> writes the coordinate as exactly
/// <c>r{row}c{col}</c>, so the arrival follows from the recorded decision and the
/// floor the boundary names.
///
/// One owner for that derivation. The validator re-derives through this to check
/// what a manifest declares, and <c>migrate-manifest --derive-boundaries</c> writes
/// through it, so the guard and the deriver cannot disagree about one history. It
/// derives and never invents: where the named action is not a map move, or does not
/// record where it moved to, there is no arrival here and the caller refuses rather
/// than filling one in.
/// </summary>
public static class FloorArrival
{
    /// <summary>What a checkpoint standing at a floor arrival is about.</summary>
    public const string CheckpointKind = ReplayBoundary.FloorEntryKind;

    /// <summary>The canonical fields a floor arrival is proved by, and their values,
    /// for the floor_entry boundary declared after this action - or null where the
    /// manifest declares none there, or the decision there is not a map move that
    /// says where it moved to.</summary>
    public static IReadOnlyDictionary<string, string>? At(ReplayManifest manifest, int afterSeq)
    {
        var boundary = manifest.Boundaries
            .FirstOrDefault(candidate => candidate.IsFloorEntry && candidate.AfterSeq == afterSeq);
        if (boundary?.Floor is not > 0) return null;

        var action = manifest.Actions.FirstOrDefault(candidate => candidate.Seq == afterSeq);
        if (action is null || action.Verb != ActionVerb.MapMove) return null;
        if (!action.Args.TryGetValue("row", out var row) ||
            !action.Args.TryGetValue("column", out var column))
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["run.total_floor"] = boundary.Floor.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["run.map_coord"] = $"r{row}c{column}",
        };
    }

    /// <summary>
    /// The same manifest, with an arrival checkpoint at every floor_entry boundary
    /// that has none.
    ///
    /// A boundary that already has one keeps it: a recorder's checkpoint is what a
    /// live game read, and replacing it with a derivation would trade an observation
    /// for a restatement of the history. The derived one is inferred and says so -
    /// nobody watched it, and its evidence is the decision it follows from.
    /// </summary>
    public static ReplayManifest WithArrivalCheckpoints(ReplayManifest manifest)
    {
        var checkpoints = new List<Checkpoint>(manifest.Checkpoints);
        foreach (var boundary in manifest.Boundaries
                     .Where(boundary => boundary.IsFloorEntry)
                     .OrderBy(boundary => boundary.AfterSeq))
        {
            if (ArrivalCheckpointAt(checkpoints, boundary.AfterSeq) is not null) continue;
            if (Checkpoint(manifest, boundary) is not { } derived) continue;

            checkpoints.Insert(
                checkpoints.FindLastIndex(existing => existing.AfterSeq <= derived.AfterSeq) + 1, derived);
        }

        return checkpoints.Count == manifest.Checkpoints.Count
            ? manifest
            : manifest with { Checkpoints = checkpoints };
    }

    /// <summary>
    /// The same manifest with every derived arrival re-read off the history as it now
    /// stands.
    ///
    /// A negative control that walks to a different node changes what the run's own
    /// history says it arrived at. An arrival still restating the decision that was
    /// there before is a value nobody derived, and would be refused as a malformed
    /// manifest rather than replayed and refused by the engine - which is the whole
    /// point of a control. What a video or a recorder observed is untouched: only a
    /// value this owner derived is re-derived.
    /// </summary>
    public static ReplayManifest WithRederivedArrivals(ReplayManifest manifest)
    {
        var checkpoints = manifest.Checkpoints
            .Select(checkpoint =>
            {
                if (At(manifest, checkpoint.AfterSeq) is not { } arrival) return checkpoint;

                var expect = checkpoint.Expect.ToDictionary(
                    field => field.Key,
                    field => field.Value.Source == FactSource.Inferred &&
                             arrival.TryGetValue(field.Key, out var derived)
                        ? field.Value with { Value = derived }
                        : field.Value,
                    StringComparer.Ordinal);
                return checkpoint with { Expect = expect };
            })
            .ToList();

        return manifest with { Checkpoints = checkpoints };
    }

    /// <summary>The checkpoint a floor plan would take at this action: the first one
    /// there naming every field the plan requires, which is how
    /// <see cref="FloorEntryPlan.For"/> chooses between several at one action.</summary>
    public static Checkpoint? ArrivalCheckpointAt(IEnumerable<Checkpoint> checkpoints, int afterSeq) =>
        checkpoints
            .Where(checkpoint => checkpoint.AfterSeq == afterSeq)
            .FirstOrDefault(checkpoint =>
                FloorEntryPlan.RequiredBoundaryFields.All(field => checkpoint.Expect.ContainsKey(field)));

    private static Checkpoint? Checkpoint(ReplayManifest manifest, ReplayBoundary boundary)
    {
        if (At(manifest, boundary.AfterSeq) is not { } arrival) return null;

        var seq = boundary.AfterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Checkpoint
        {
            Id = $"floor-{boundary.Floor.GetValueOrDefault().ToString(System.Globalization.CultureInfo.InvariantCulture)}-arrival",
            AfterSeq = boundary.AfterSeq,
            Kind = CheckpointKind,
            Expect = arrival.ToDictionary(
                field => field.Key,
                field => Fact<string>.Inferred(
                    field.Value,
                    FactEvidence.Reasoning(
                        $"derived from the map move at action {seq} and the floor its boundary names")),
                StringComparer.Ordinal),
        };
    }
}
