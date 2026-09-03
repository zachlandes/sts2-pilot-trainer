using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// One of the recording's decisions, as the screen showing it says it.
/// </summary>
public sealed record PrefightStep(int Number, int Count, string Caption)
{
    /// <summary>Where in the recording's decisions this one is.</summary>
    public string Counter => TrainerCopy.StepCounter(Number, Count);
}

/// <summary>
/// What a player sees while the recording makes the decisions that lead to its
/// fight.
///
/// The recording owns every one of them, which is the whole reason this exists: a
/// different blessing or a different node is a different run, and the comparison
/// this proof is built for would have nothing to compare. So the screens are the
/// game's own, the choices are already made, and the only controls are the two that
/// move through them.
///
/// Nothing here is written down about one recording. The creator's name comes from
/// the manifest's source record, each caption's subject from the run the recording's
/// action is about to act on, and the counter from how many decisions there are. A
/// second recording changes what this says without changing a line of it.
/// </summary>
public sealed record PrefightJourney(
    string Chip,
    string ChoicesShownAsRecorded,
    string NextButton,
    string SkipButton,
    IReadOnlyList<PrefightStep> Steps)
{
    /// <param name="stepCount">
    /// How many decisions the recording makes before its fight, which is not always
    /// how many are described yet: a caption names what the run is standing in front
    /// of, and a host reaches the later screens one at a time. The counter says where
    /// in the whole journey a step is, so it has to come from the plan rather than
    /// from the list.
    /// </param>
    /// <summary>
    /// One step of the journey, numbered where it actually falls.
    ///
    /// A host reaching the recording's screens one at a time knows which step it is
    /// on and holds only that one, so numbering from a list's position would call
    /// every step the first one - which is what the second panel said out loud in the
    /// retail client before this existed.
    /// </summary>
    public static PrefightJourney ForStep(string creator, PrefightChoice choice, int number, int count)
    {
        if (number < 1 || number > count)
        {
            throw new ManifestException(
                $"This journey has {count} step(s), so there is no step {number}.");
        }

        return new PrefightJourney(
            Chip: TrainerCopy.WatchingChip(creator),
            ChoicesShownAsRecorded: TrainerCopy.ChoicesShownAsRecorded(creator),
            NextButton: TrainerCopy.NextButton,
            SkipButton: TrainerCopy.SkipButton,
            Steps: [new PrefightStep(number, count, Caption(creator, choice))]);
    }

    public static PrefightJourney For(
        string creator, IReadOnlyList<PrefightChoice> choices, int? stepCount = null)
    {
        var count = stepCount ?? choices.Count;
        if (count < choices.Count)
        {
            throw new ManifestException(
                $"This journey describes {choices.Count} decision(s) out of {count}, which is more than the " +
                "recording makes.");
        }

        return new PrefightJourney(
            Chip: TrainerCopy.WatchingChip(creator),
            ChoicesShownAsRecorded: TrainerCopy.ChoicesShownAsRecorded(creator),
            NextButton: TrainerCopy.NextButton,
            SkipButton: TrainerCopy.SkipButton,
            Steps: [.. choices.Select((choice, index) =>
                new PrefightStep(index + 1, count, Caption(creator, choice)))]);
    }

    /// <summary>
    /// What one decision says.
    ///
    /// A decision this journey has no approved caption for refuses rather than
    /// getting a generic one. The proof of concept walks past exactly two kinds of
    /// screen, and a third described as "an event option was chosen" would be a
    /// sentence nobody wrote pretending to be one somebody did.
    /// </summary>
    private static string Caption(string creator, PrefightChoice choice) => choice switch
    {
        PrefightChoice.Blessing blessing => TrainerCopy.BlessingCaption(creator, blessing.RelicModelId),
        PrefightChoice.MapMove move => TrainerCopy.MapMoveCaption(
            creator, move.NodeType, MapColumns.Position(move.Column, move.ColumnCount)),
        _ => throw new ManifestException(
            $"Action {choice.Seq} is a kind of decision this trainer has no way to describe, so the recording " +
            "cannot be watched making it. Only an opening blessing and a map move are supported before a " +
            "fight."),
    };
}

/// <summary>
/// Where a column sits on the map, in the three words the journey's caption uses.
///
/// Thirds of the act's own width rather than a written-down index, because how wide
/// an act is belongs to the map and the same column number is not the same place on
/// two different ones.
/// </summary>
public static class MapColumns
{
    public static string Position(int column, int columnCount)
    {
        if (columnCount <= 0)
        {
            throw new ManifestException(
                "This act reports no columns, so where a node sits on it cannot be said.");
        }

        if (column < 0 || column >= columnCount)
        {
            throw new ManifestException(
                $"Column {column} is outside this act's {columnCount} column(s).");
        }

        if (column * 3 < columnCount) return "left";
        return column * 3 >= columnCount * 2 ? "right" : "centre";
    }
}
