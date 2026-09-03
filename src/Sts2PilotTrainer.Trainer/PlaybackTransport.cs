using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>What the transport is doing, which is what it draws.</summary>
public enum TransportMode
{
    /// <summary>The recording's next decision is revealed on the game's own screen
    /// and the transport is holding on it.</summary>
    Watching,

    /// <summary>The player pressed Back. A decision already made is being re-shown;
    /// the run has not moved.</summary>
    LookingBack,

    /// <summary>The fight is the player's. The strip is a chip and says nothing.</summary>
    Chip,
}

/// <summary>One of the transport's three controls, and whether it can be used.</summary>
public sealed record TransportControl(string Label, bool Enabled);

/// <summary>
/// One of the recording's decisions, as the transport says it.
/// </summary>
public sealed record PrefightStep(int Number, int Count, string Caption)
{
    /// <summary>Where in the recording's decisions this one is.</summary>
    public string Counter => TrainerCopy.StepCounter(Number, Count);
}

/// <summary>
/// The playback transport: one long-lived strip that carries the whole watched
/// journey, and the one owner of what it says at each moment.
///
/// It replaces the per-step popup this proof started with. The reason is not tidiness
/// - a popup is torn down and rebuilt between screens, so it cannot carry a position
/// through the map-to-combat transition, and it dims or covers the screen the player
/// is here to look at. One node that outlives the screens can do both, which is why
/// the strip and this model exist as one thing rather than as a second playback path
/// beside the popup.
///
/// The vocabulary is reveal, hold, commit. Reveal applies the game's own selected
/// state to the target without clicking; the hold is this model's Watching mode,
/// either waiting for Forward or timing out under Play; commit calls the game's own
/// click path. Back re-shows a decision already made and never uncommits one, which
/// is why <see cref="TransportMode.LookingBack"/> is a way of reading rather than a
/// way of moving.
///
/// Nothing here is written down about one recording. The creator comes from the
/// manifest's source record, each caption's subject from the run the decision is
/// about to act on, and the counter from how many decisions there are.
/// </summary>
public sealed record PlaybackTransport(
    TransportMode Mode,
    string Chip,
    string Counter,
    string Caption,
    string Note,
    TransportControl Back,
    TransportControl Forward,
    TransportControl Play)
{
    /// <summary>Whether the strip draws its controls at all. A chip has none: during
    /// the player's own fight nothing is offered unbidden.</summary>
    public bool HasControls => Mode != TransportMode.Chip;

    /// <summary>
    /// The recording's next decision, revealed and held.
    /// </summary>
    /// <param name="number">Which of the recording's decisions this is, counted from one.</param>
    /// <param name="count">How many the recording makes before its fight.</param>
    /// <param name="playing">Whether Play is running the sequence, which is the only
    /// thing that changes about the strip while it does.</param>
    /// <param name="noteShown">Whether the once-per-run sentence has already been said.</param>
    public static PlaybackTransport Revealing(
        string creator, PrefightChoice choice, int number, int count, bool playing, bool noteShown) =>
        new(
            Mode: TransportMode.Watching,
            Chip: TrainerCopy.WatchingChip(creator),
            Counter: Step(creator, choice, number, count).Counter,
            Caption: Step(creator, choice, number, count).Caption,
            // Said once, before the first decision anybody watches. A rule about how to
            // read these screens is worth saying once and tiresome above every one.
            Note: number == 1 && !noteShown ? TrainerCopy.ChoicesShownAsRecorded(creator) : string.Empty,
            Back: new TransportControl(TrainerCopy.BackButton, number > 1),
            Forward: new TransportControl(TrainerCopy.ForwardButton, true),
            Play: new TransportControl(playing ? TrainerCopy.PauseButton : TrainerCopy.PlayButton, true));

    /// <summary>
    /// A decision the recording already made, re-shown.
    ///
    /// The run does not move. Back exists because a watcher did not do the thinking
    /// and will miss a step that resolved while they were reading the last one; it is
    /// a way of looking again, and there is no way of undoing from here.
    /// </summary>
    public static PlaybackTransport LookingBackAt(
        string creator, PrefightChoice choice, int number, int count) =>
        new(
            Mode: TransportMode.LookingBack,
            Chip: TrainerCopy.WatchingChip(creator),
            Counter: TrainerCopy.PreviousStepCounter(Step(creator, choice, number, count).Number, count),
            Caption: Step(creator, choice, number, count).Caption,
            Note: string.Empty,
            Back: new TransportControl(TrainerCopy.BackButton, number > 1),
            Forward: new TransportControl(TrainerCopy.ForwardButton, true),
            Play: new TransportControl(TrainerCopy.PlayButton, true));

    /// <summary>
    /// The fight is the player's.
    ///
    /// The strip collapses to a chip carrying the trainer's own name and nothing
    /// else. Not an oversight: a player fighting wants nothing in the way, and a
    /// counter or a caption here would be the recording's line shown beside a fight
    /// it is not part of.
    /// </summary>
    public static PlaybackTransport DuringYourFight() =>
        new(
            Mode: TransportMode.Chip,
            Chip: TrainerCopy.Name,
            Counter: string.Empty,
            Caption: string.Empty,
            Note: string.Empty,
            Back: new TransportControl(TrainerCopy.BackButton, false),
            Forward: new TransportControl(TrainerCopy.ForwardButton, false),
            Play: new TransportControl(TrainerCopy.PlayButton, false));

    private static PrefightStep Step(string creator, PrefightChoice choice, int number, int count)
    {
        if (number < 1 || number > count)
        {
            throw new ManifestException(
                $"This journey has {count} step(s), so there is no step {number}.");
        }

        return new PrefightStep(number, count, Describe(creator, choice));
    }

    /// <summary>
    /// What one decision says.
    ///
    /// A decision this transport has no approved caption for refuses rather than
    /// getting a generic one. The proof of concept walks past exactly two kinds of
    /// screen, and a third described as "an event option was chosen" would be a
    /// sentence nobody wrote pretending to be one somebody did.
    /// </summary>
    private static string Describe(string creator, PrefightChoice choice) => choice switch
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
/// Where a column sits on the map, in the three words the caption uses.
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
