using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>What the transport is doing, which is what it draws.</summary>
public enum TransportMode
{
    /// <summary>The recording's next decision is revealed on the game's own screen
    /// and the tag is holding on it.</summary>
    Watching,

    /// <summary>The player pressed look back. A decision already made is being
    /// re-shown over a ledger of the ones before it; the run has not moved.</summary>
    LookingBack,

    /// <summary>The fight is the player's. The tag is a chip and says nothing until
    /// it is pressed.</summary>
    Chip,

    /// <summary>A screen could not be driven. The mark becomes the warning glyph and
    /// every control is refused; the sentence itself is a popup's, not the tag's.</summary>
    Refused,
}

/// <summary>
/// The transport's drawn shapes.
///
/// The game ships no playback iconography, so this family is the mod's own art, and
/// it carries one rule with meaning rather than decoration: <b>a filled shape moves
/// the run, a hollow shape only looks.</b> That is what separates look back - which
/// re-shows a decision and can never rewind one - from step, which commits one.
/// </summary>
public enum TransportGlyph
{
    /// <summary>Hollow triangle and bar: re-shows a decision, never rewinds.</summary>
    Back,

    /// <summary>Filled triangle: runs the remaining decisions with a hold on each.</summary>
    Play,

    /// <summary>Two bars, sharing Play's button: stops on this decision.</summary>
    Pause,

    /// <summary>Filled triangle and bar: commits this decision, reveals the next.</summary>
    Step,

    /// <summary>Circular arrow with a filled head: back to the proven combat start.</summary>
    Again,

    /// <summary>Two filled triangles and a bar: to the end of the attempt.</summary>
    Jump,

    /// <summary>The trainer's mark, which is the reticle the reveal lights.</summary>
    Mark,

    /// <summary>A refusal is up.</summary>
    Warn,
}

/// <summary>
/// One of the transport's controls.
///
/// Icon only, by the captain's ruling that progressive disclosure is the game's own
/// principle: the words live in the tooltip, which is why one is required here and a
/// label is not offered. A control that is not on offer is still drawn - buttons that
/// move about between decisions cannot be aimed at - and says why it is refused.
/// </summary>
public sealed record TransportControl(
    TransportGlyph Glyph, bool Enabled, string TooltipTitle, string TooltipBody, string? DisabledReason = null);

/// <summary>
/// Whose recording this is, and where to watch the moment being shown.
///
/// The creator alone was the captain's first correction: he wanted the video named
/// too, and a way through to it. <paramref name="VideoTitle"/> is absent until
/// ingestion fills the manifest's title, and the block falls back to the creator
/// alone rather than inventing one.
/// </summary>
public sealed record TransportIdentity(
    string Creator, string? VideoTitle, string? VideoUrl, string? OpensAt)
{
    /// <summary>Whether pressing the block opens anything. False on a recording whose
    /// manifest carries no timestamp for this decision.</summary>
    public bool IsLink => VideoUrl is not null;

    public string TooltipTitle => VideoTitle is null ? Creator : $"{Creator} · {VideoTitle}";

    public string TooltipBody => OpensAt is null
        ? TrainerCopy.IdentityNoTimestamp
        : TrainerCopy.IdentityOpensAt(OpensAt);
}

/// <summary>
/// Where in the recording's decisions the transport is.
///
/// The numerals are always drawn; the pips are drawn only while there are few enough
/// of them to be read at a glance, which is what keeps this honest on a whole run
/// rather than on the two decisions this recording has.
/// </summary>
public sealed record TransportCounter(int Current, int Count, int? LookingAt)
{
    /// <summary>Above this many decisions the pips stop being a picture and start
    /// being a texture.</summary>
    public const int MostPips = 12;

    public bool ShowPips => Count <= MostPips;

    /// <summary>The step the numerals name: the one being looked at, or the one about
    /// to happen.</summary>
    public int Shown => LookingAt ?? Current;

    public string Numerals => TrainerCopy.StepCounter(Shown, Count);
}

/// <summary>
/// One decision already made, as the ledger lists it.
///
/// The ledger exists because looking back usually means looking at a screen that is
/// gone: the run cannot be asked again and must never be rewound to answer, so what
/// was read at the time is kept. <paramref name="ArtModelId"/> is the game's own
/// artwork for the thing chosen; the label is the caption without the creator's name,
/// which the tag above it already carries.
/// </summary>
public sealed record LedgerRow(int Number, string ArtModelId, string Label, bool IsCurrent, bool IsLookedAt);

/// <summary>One row of a menu hung under the tag or the chip.</summary>
public sealed record MenuRow(
    TransportGlyph? Glyph, string Label, bool Enabled = true, string? DisabledReason = null,
    bool IsCurrent = false);

/// <summary>
/// How fast Play runs.
///
/// The captain asked for this the way a video player has it. It divides the hold and
/// nothing else: the per-screen floors still apply, so the map's hold stays behind
/// the game's own one-second select effect however fast this is set.
/// </summary>
public enum PlaybackSpeed
{
    Half,
    Normal,
    OneAndAHalf,
    Double,
}

public static class PlaybackSpeeds
{
    public static readonly IReadOnlyList<PlaybackSpeed> All =
        [PlaybackSpeed.Half, PlaybackSpeed.Normal, PlaybackSpeed.OneAndAHalf, PlaybackSpeed.Double];

    public static double Multiplier(this PlaybackSpeed speed) => speed switch
    {
        PlaybackSpeed.Half => 0.5,
        PlaybackSpeed.Normal => 1.0,
        PlaybackSpeed.OneAndAHalf => 1.5,
        PlaybackSpeed.Double => 2.0,
        _ => 1.0,
    };

    public static string Label(this PlaybackSpeed speed) =>
        speed.Multiplier().ToString("0.#", CultureInfo.InvariantCulture) + "×";

    /// <summary>
    /// The hold, at this speed but never below the screen's own floor.
    ///
    /// The floor is the point: a hold shorter than the game's own animation for the
    /// same decision would commit while the game was still showing the last one, and
    /// a watcher would be reading a screen that had already moved.
    /// </summary>
    public static double Divide(this PlaybackSpeed speed, double hold, double floor) =>
        Math.Max(floor, hold / speed.Multiplier());
}

/// <summary>
/// The playback transport: one long-lived tag that carries the whole watched journey,
/// and the one owner of what it says at each moment.
///
/// It replaces the per-step popup this proof started with, and then the wide text bar
/// that first replaced it. A popup is torn down between screens, so it cannot carry a
/// position through the map-to-combat transition and it covers what the player is
/// there to look at; the bar survived both but read as a debug overlay and sat at the
/// same level of hierarchy as the game's own choices. The accepted design is a tag
/// hanging from the top bar's torn edge, in the game's palette and deliberately not
/// its material - flat where the game is textured - so a player reads "the game, then
/// the mod" with no caption. See docs/mod-ui-direction.md.
///
/// The vocabulary is reveal, hold, commit. Reveal applies the game's own selected
/// state to the target without clicking; the hold is this model's Watching mode,
/// waiting for the player under step and draining a timer under play; commit calls
/// the game's own click path. Look back re-shows a decision already made and never
/// uncommits one, which is why <see cref="TransportMode.LookingBack"/> is a way of
/// reading rather than a way of moving.
///
/// Nothing here is written down about one recording. The creator and the video come
/// from the manifest's source record, each caption's subject from the run the
/// decision is about to act on, and the counter from how many decisions there are.
/// </summary>
public sealed record PlaybackTransport(
    TransportMode Mode,
    TransportIdentity Identity,
    TransportCounter Counter,
    PlaybackSpeed Speed,
    TransportControl Back,
    TransportControl Play,
    TransportControl Step,
    IReadOnlyList<LedgerRow> Ledger,
    string Note,
    IReadOnlyList<MenuRow> ChipMenu)
{
    /// <summary>Whether the tag draws its controls at all. A chip has none: during
    /// the player's own fight nothing is offered unbidden.</summary>
    public bool HasControls => Mode is TransportMode.Watching or TransportMode.LookingBack;

    /// <summary>The mark, or the warning that replaces it while a refusal is up.</summary>
    public TransportGlyph Mark => Mode == TransportMode.Refused ? TransportGlyph.Warn : TransportGlyph.Mark;

    /// <summary>The speed menu, with the current row marked.</summary>
    public IReadOnlyList<MenuRow> SpeedMenu =>
        [.. PlaybackSpeeds.All.Select(speed =>
            new MenuRow(null, speed.Label(), IsCurrent: speed == Speed))];

    public string SpeedLabel => Speed.Label();

    /// <summary>
    /// The recording's next decision, revealed and held.
    /// </summary>
    /// <param name="number">Which of the recording's decisions this is, from one.</param>
    /// <param name="count">How many the recording makes before its fight.</param>
    /// <param name="playing">Whether Play is running the sequence, which decides
    /// only which glyph the middle button carries.</param>
    /// <param name="noteShown">Whether the once-per-run sentence has been said.</param>
    public static PlaybackTransport Revealing(
        TransportIdentity identity, PrefightChoice choice, int number, int count, bool playing,
        bool noteShown, PlaybackSpeed speed = PlaybackSpeed.Normal) =>
        new(
            Mode: TransportMode.Watching,
            Identity: identity,
            Counter: Check(number, count),
            Speed: speed,
            Back: BackControl(number > 1),
            Play: PlayControl(playing),
            Step: StepControl(number, count, Describe(identity.Creator, choice)),
            Ledger: [],
            // Said once, before the first decision anybody watches. A rule about how
            // to read these screens is worth saying once and tiresome above every one.
            Note: number == 1 && !noteShown ? TrainerCopy.ChoicesShownAsRecorded(identity.Creator) : string.Empty,
            ChipMenu: []);

    /// <summary>
    /// A decision the recording already made, re-shown over the ledger of the ones
    /// before it.
    ///
    /// The run does not move. Look back exists because a watcher did not do the
    /// thinking and will miss a step that resolved while they were reading the last
    /// one; it is a way of looking again, and there is no way of undoing from here.
    /// </summary>
    /// <param name="shown">Which already-made decision is being looked at, from one.</param>
    /// <param name="current">The decision the run is actually holding on.</param>
    /// <param name="made">Every decision made so far, in order, as they were read at
    /// the time.</param>
    public static PlaybackTransport LookingBackAt(
        TransportIdentity identity, IReadOnlyList<PrefightChoice> made, int shown, int current, int count,
        PrefightChoice next, PlaybackSpeed speed = PlaybackSpeed.Normal)
    {
        if (shown < 1 || shown > made.Count)
        {
            throw new ManifestException(
                $"{made.Count} decision(s) have been made, so there is no step {shown} to look back at.");
        }

        return new PlaybackTransport(
            Mode: TransportMode.LookingBack,
            Identity: identity,
            Counter: Check(current, count) with { LookingAt = shown },
            Speed: speed,
            Back: BackControl(shown > 1),
            Play: PlayControl(playing: false),
            Step: StepControl(current, count, Describe(identity.Creator, next)),
            Ledger:
            [
                .. made.Select((choice, index) => new LedgerRow(
                    index + 1,
                    ArtOf(choice),
                    Describe(identity.Creator, choice, name: false),
                    IsCurrent: false,
                    IsLookedAt: index + 1 == shown)),
                new LedgerRow(
                    current, ArtOf(next), Describe(identity.Creator, next, name: false),
                    IsCurrent: true, IsLookedAt: false),
            ],
            Note: string.Empty,
            ChipMenu: []);
    }

    /// <summary>
    /// The fight is the player's.
    ///
    /// The tag collapses to a chip carrying the mark and the creator and nothing
    /// else, silent until it is pressed. Not an oversight: the captain's ruling is
    /// that comparing inside a fight is second-order, because a player diverges from
    /// the recorded line almost immediately, so the comparison points are the whole
    /// recorded fight watched and the finished fight's result.
    ///
    /// Pressed, it offers two directions and no third. Both leave the attempt, so
    /// both go through the game's own confirmation first.
    /// </summary>
    /// <param name="anythingPlayed">Whether the player has taken a turn yet. At turn
    /// one with nothing played there is no end to jump to.</param>
    public static PlaybackTransport DuringYourFight(TransportIdentity identity, bool anythingPlayed) =>
        new(
            Mode: TransportMode.Chip,
            Identity: identity,
            Counter: new TransportCounter(0, 0, null),
            Speed: PlaybackSpeed.Normal,
            Back: BackControl(false),
            Play: PlayControl(playing: false) with { Enabled = false },
            Step: StepControl(0, 0, string.Empty) with { Enabled = false },
            Ledger: [],
            Note: string.Empty,
            ChipMenu:
            [
                new MenuRow(TransportGlyph.Again, TrainerCopy.JumpToTheBeginning),
                new MenuRow(
                    TransportGlyph.Jump, TrainerCopy.JumpToTheEnd, anythingPlayed,
                    anythingPlayed ? null : TrainerCopy.NothingPlayedYet),
            ]);

    /// <summary>
    /// Every recorded decision has been made and the game is opening the fight.
    ///
    /// The window is as long as the fight takes to open, and the run is not the
    /// player's yet, so the tag stays where it was and refuses everything that would
    /// move it. Refused rather than absent: a control that moves the run cannot be
    /// left offered over a run with nothing left to commit, and controls that vanish
    /// for a second and come back are the popup this design replaced.
    /// </summary>
    /// <param name="count">How many decisions the recording made, all of them now
    /// behind the run.</param>
    public static PlaybackTransport OpeningTheFight(TransportIdentity identity, int count) =>
        new(
            Mode: TransportMode.Watching,
            Identity: identity,
            Counter: new TransportCounter(count, count, null),
            Speed: PlaybackSpeed.Normal,
            Back: BackControl(false) with { DisabledReason = TrainerCopy.OpeningTheFightDisabledReason },
            Play: PlayControl(playing: false) with
            {
                Enabled = false, DisabledReason = TrainerCopy.OpeningTheFightDisabledReason,
            },
            Step: StepControl(0, 0, string.Empty) with
            {
                Enabled = false, DisabledReason = TrainerCopy.OpeningTheFightDisabledReason,
            },
            Ledger: [],
            Note: string.Empty,
            ChipMenu: []);

    /// <summary>
    /// A screen could not be driven.
    ///
    /// The tag says so and stops offering anything; the sentence a player reads is
    /// the popup's. Every control is drawn and refused rather than removed, so the
    /// tag does not appear to have lost its controls as well as its run.
    /// </summary>
    public static PlaybackTransport Refused(TransportIdentity identity) =>
        new(
            Mode: TransportMode.Refused,
            Identity: identity,
            Counter: new TransportCounter(0, 0, null),
            Speed: PlaybackSpeed.Normal,
            Back: BackControl(false) with { DisabledReason = TrainerCopy.RefusedDisabledReason },
            Play: PlayControl(false) with { Enabled = false, DisabledReason = TrainerCopy.RefusedDisabledReason },
            Step: StepControl(0, 0, string.Empty) with
            {
                Enabled = false, DisabledReason = TrainerCopy.RefusedDisabledReason,
            },
            Ledger: [],
            Note: string.Empty,
            ChipMenu: []);

    private static TransportControl BackControl(bool enabled) => new(
        TransportGlyph.Back, enabled, TrainerCopy.BackTooltipTitle, TrainerCopy.BackTooltipBody,
        enabled ? null : TrainerCopy.NothingBehindYet);

    private static TransportControl PlayControl(bool playing) => playing
        ? new TransportControl(
            TransportGlyph.Pause, true, TrainerCopy.PauseTooltipTitle, TrainerCopy.PauseTooltipBody)
        : new TransportControl(
            TransportGlyph.Play, true, TrainerCopy.PlayTooltipTitle, TrainerCopy.PlayTooltipBody);

    /// <summary>
    /// Step's tooltip names the decision it is about to make.
    ///
    /// The caption the wide bar drew always is here instead, which is the captain's
    /// tooltips-only ruling: the picture is the lit target on the game's own screen,
    /// and the words are one hover away.
    /// </summary>
    private static TransportControl StepControl(int number, int count, string caption) => new(
        TransportGlyph.Step, true, TrainerCopy.StepTooltipTitle,
        count == 0
            ? TrainerCopy.StepTooltipBody
            : $"{TrainerCopy.StepTooltipBody}\n{TrainerCopy.StepCounter(number, count)} · {caption}");

    private static TransportCounter Check(int number, int count)
    {
        if (number < 1 || number > count)
        {
            throw new ManifestException($"This journey has {count} step(s), so there is no step {number}.");
        }

        return new TransportCounter(number, count, null);
    }

    /// <summary>
    /// What one decision says.
    ///
    /// A decision this transport has no approved caption for refuses rather than
    /// getting a generic one. The proof of concept walks past exactly two kinds of
    /// screen, and a third described as "an event option was chosen" would be a
    /// sentence nobody wrote pretending to be one somebody did.
    /// </summary>
    /// <param name="name">Whether to name the creator. The ledger does not: the tag
    /// hanging above it carries the name once, and repeating it down a list of five
    /// rows is the sentence the design replaced.</param>
    private static string Describe(string creator, PrefightChoice choice, bool name = true) => choice switch
    {
        PrefightChoice.Blessing blessing => name
            ? TrainerCopy.BlessingCaption(creator, blessing.RelicModelId)
            : TrainerCopy.BlessingLedgerRow(blessing.RelicModelId),
        PrefightChoice.MapMove move => name
            ? TrainerCopy.MapMoveCaption(
                creator, move.NodeType, MapColumns.Position(move.Column, move.ColumnCount))
            : TrainerCopy.MapMoveLedgerRow(
                move.NodeType, MapColumns.Position(move.Column, move.ColumnCount)),
        _ => throw new ManifestException(
            $"Action {choice.Seq} is a kind of decision this trainer has no way to describe, so the recording " +
            "cannot be watched making it. Only an opening blessing and a map move are supported before a " +
            "fight."),
    };

    /// <summary>
    /// The model id whose artwork stands for a decision in the ledger.
    ///
    /// The game's own art, asked for by id, because the captain's note on the first
    /// mock was that the real icons should be used. A decision whose subject the game
    /// draws nothing for gets no picture rather than a placeholder.
    /// </summary>
    private static string ArtOf(PrefightChoice choice) => choice switch
    {
        PrefightChoice.Blessing blessing => blessing.RelicModelId,
        PrefightChoice.MapMove move => move.NodeType,
        _ => string.Empty,
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
