using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>What the transport is doing, which is what it draws.</summary>
public enum TransportMode
{
    /// <summary>The recording's next decision is revealed on the game's own screen
    /// and the tag is holding on it.</summary>
    Watching,

    /// <summary>The game's screen has arrived and nothing is lit. The viewer sees
    /// every option the recording saw and decides in their head; step reveals. The
    /// same elements as Watching with two drawn differences: the mark has no centre
    /// dot and the current pip is hollow, because the mod has nothing to point at
    /// yet. Between screens it is this mode with everything that moves the run
    /// refused, the screen not having arrived.</summary>
    Considering,

    /// <summary>The player pressed look back. A decision already made is being
    /// re-shown over a ledger of the ones before it; the run has not moved.</summary>
    LookingBack,

    /// <summary>Every recorded decision is made and the game is opening the fight.
    /// The tag stays where it was and refuses everything that would move a run with
    /// nothing left to commit.</summary>
    Opening,

    /// <summary>The fight is the player's. The tag is a chip and says nothing until
    /// it is pressed.</summary>
    Chip,

    /// <summary>The fight has ended and the game has drawn its ending. The chip stays
    /// and the post-fight choice hangs under it; nothing about the recording's line is
    /// drawn until a row is pressed.</summary>
    Ended,

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

    /// <summary>The mark with nothing to point at yet: ring and ticks, no centre dot.
    /// Not a new glyph but the mark while a decision is considered and not yet
    /// revealed.</summary>
    MarkUnlit,

    /// <summary>Hollow eye: a reveal that only looks. Shows the comparison, and marks a
    /// fight shown this sitting.</summary>
    Reveal,

    /// <summary>Filled triangle with a hollow bar after it: the run goes on and the
    /// hold is the player's.</summary>
    Continue,

    /// <summary>A refusal is up.</summary>
    Warn,
}

/// <summary>
/// One of the transport's controls.
///
/// Icon only, by the captain's ruling that progressive disclosure is the game's own
/// principle: the words live in the tooltip, which is why one is required here and a
/// label is not offered. A control that is not on offer is still drawn - buttons that
/// move about between decisions cannot be aimed at - and says why it is refused where
/// a reason has been written. The two between-screens windows deliberately have none,
/// so there the tooltip goes on saying what the control does.
/// </summary>
public sealed record TransportControl(
    TransportGlyph Glyph, bool Enabled, string TooltipTitle, string TooltipBody, string? DisabledReason = null);

/// <summary>
/// Whose recording this is, and where to watch the moment being shown.
///
/// The creator alone was the captain's first correction: he wanted the video named
/// too, and a way through to it. <paramref name="VideoTitle"/> is absent until
/// ingestion fills the manifest's title, and the block falls back to the resolved
/// credit alone rather than inventing one.
///
/// <para>It carries a <see cref="RecordingCredit"/> rather than a name because the
/// captions under it put the credit into sentences, and a run the player recorded
/// themselves is credited in the second person. The tag's own line is the credit's
/// label.</para>
/// </summary>
public sealed record TransportIdentity(
    RecordingCredit Credit, string? VideoTitle, string? VideoUrl, string? OpensAt)
{
    /// <summary>Whether pressing the block opens anything. False on a recording whose
    /// manifest carries no video at all, which a recording made inside the player's
    /// own game does not.</summary>
    public bool IsLink => VideoUrl is not null;

    public string TooltipTitle =>
        VideoTitle is null ? Credit.Label : $"{Credit.Label} · {VideoTitle}";

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
/// <param name="Lit">Whether the current decision is revealed. The current pip is
/// filled once it is and hollow until then, which is one of the two drawn signals
/// that a decision is being considered rather than shown.</param>
/// <param name="RestoredToFloor">The floor the run was restored to, where it was
/// restored rather than walked. There is then nothing to count - no decision was
/// shown - and the numerals name the floor instead, with no pips.</param>
public sealed record TransportCounter(
    int Current, int Count, int? LookingAt, bool Lit = true, int? RestoredToFloor = null)
{
    /// <summary>Above this many decisions the pips stop being a picture and start
    /// being a texture.</summary>
    public const int MostPips = 12;

    public bool ShowPips => RestoredToFloor is null && Count > 0 && Count <= MostPips;

    /// <summary>The step the numerals name: the one being looked at, or the one about
    /// to happen.</summary>
    public int Shown => LookingAt ?? Current;

    /// <summary>What the counter's label reads: the step of the count, the floor a
    /// restored run stands on, or nothing where there is nothing to count.</summary>
    public string Numerals => RestoredToFloor is { } floor
        ? TrainerCopy.RestoredToFloor(floor)
        : Count == 0
            ? string.Empty
            : TrainerCopy.StepCounter(Shown, Count);
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

/// <summary>
/// One row of a menu hung under the tag or the chip.
///
/// A refused row says nothing, which is why there is nowhere here to put a reason.
/// Decided by the project's coordinating owner: the only refused row that exists is
/// jump to the end before anything has been played, refused because there is no result
/// until the player has taken an action, and that clears through the very action the
/// player is already there to take. A permanent explanation for a state that resolves itself in seconds costs
/// more attention than it saves, and drawing one would mean inventing a layout for
/// reason text in a menu row that nobody has approved. A tooltip was weighed as a
/// middle path and rejected: a tooltip answers a player who already suspects
/// something is broken, and nothing here is broken.
/// </summary>
public sealed record MenuRow(
    TransportGlyph? Glyph, string Label, bool Enabled = true, bool IsCurrent = false);

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
/// Where a watched journey has got to.
///
/// It lives here rather than with the run that walks it because the transport's whole
/// state is derived from it, and a derivation the game has to be running to test is a
/// derivation nobody tests. The run inside the retail client owns the transitions; this
/// owns what each one means on screen.
/// </summary>
public enum JourneyPhase
{
    /// <summary>There is no trainer run.</summary>
    None,

    /// <summary>
    /// The save the run will be restored from is being materialised, by the packaged
    /// arbiter in its own process, and no run exists yet.
    ///
    /// A phase of its own rather than a moment inside <see cref="Starting"/> because it
    /// is a different situation: a subprocess is replaying the recording's history and
    /// nothing has been constructed in this game, so a refusal here has nothing to tear
    /// down. The transport draws nothing for it, because it is parented to a run's own
    /// interface and there is no run; what the player sees instead is
    /// <see cref="RestoringNotice"/>, which is derived from this phase alone and drawn
    /// in the game's own loading idiom.
    /// </summary>
    Preparing,

    /// <summary>The run exists and the game is putting it on screen.</summary>
    Starting,

    /// <summary>The recording is making its decisions, on the game's own screens, and
    /// the player is watching.</summary>
    Watching,

    /// <summary>The fight has been proved to be the recorded one and is the player's.
    /// Every action they take is being sampled either side.</summary>
    InFight,

    /// <summary>The fight has ended and the game is drawing its ending. The chip
    /// stays, with both of its rows refused, until the ending is drawn.</summary>
    Result,

    /// <summary>The game has drawn its ending and the post-fight choice is on offer.
    /// The run may still exist underneath, on a win, or have been torn down by the
    /// game's own flow, on a loss; the choice is the same either way.</summary>
    Ended,

    /// <summary>A screen could not be driven and the attempt is being torn down.</summary>
    Refused,
}

/// <summary>
/// Everything the transport is derived from, gathered in one place.
///
/// A record rather than a dozen arguments, because the point of it is that the whole
/// input set is named: anything the tag can say is a function of these values and the
/// phase, so a fact that changes is a re-derivation and never a state assembled by
/// hand at the site that changed it. Four defects on this surface were exactly that -
/// a mode applied once and never re-derived, a speed a hand-built state forgot to pass
/// on. Gathering the input set does not by itself make either unstateable: a factory
/// that ignores a fact it was handed still gets it wrong, which is how the refused tag
/// went on printing 1x. What this buys is that the fact is always there to be read and
/// the mistake is visible in one file.
/// </summary>
/// <param name="Made">The decisions already made, in order, as they were read at the
/// time.</param>
/// <param name="Next">The decision about to be made, absent once there is none.</param>
/// <param name="AtCombatStart">Whether every recorded decision is behind the run, which
/// is the window in which the game is opening the fight.</param>
/// <param name="Arrived">Whether the game's screen for the decision about to be made
/// is up and still. Between committing one decision and the next screen arriving it
/// is not, and a press there acts on a state nobody has been shown.</param>
/// <param name="Lit">Whether the decision about to be made is lit on the game's own
/// screen. Arrived and not lit is the consider hold; a step taken before it is lit
/// would commit a decision nobody was shown, so step reveals there instead.</param>
/// <param name="NextOptionCount">How many options the screen offers for the decision
/// about to be made, read from the screen on arrival, or null where it could not be
/// read. A screen with one option has no consider beat: arrival is the reveal.</param>
/// <param name="AnythingPlayed">Whether the player has taken an action of their own in
/// their own fight. One card is enough; it is not a completed turn.</param>
/// <param name="AfterTheFight">What the post-fight choice is derived from, once the
/// fight has ended. Null until then.</param>
/// <param name="RestoredToFloor">The floor the run was restored to from the game's own
/// save, or null for a run walked from its start. A restored run has watched none of
/// the decisions before that floor, and a counter that said it had would be the
/// transport claiming something nobody was shown.</param>
public sealed record TransportFacts(
    TransportIdentity Identity,
    IReadOnlyList<PrefightChoice> Made,
    PrefightChoice? Next,
    int StepsTaken,
    int Count,
    bool AtCombatStart,
    bool Arrived,
    bool Lit,
    int? NextOptionCount,
    int? LookingBackAt,
    bool Playing,
    bool NoteShown,
    PlaybackSpeed Speed,
    bool AnythingPlayed,
    PostFightFacts? AfterTheFight = null,
    int? RestoredToFloor = null)
{
    /// <summary>
    /// Whether the decision about to be made gets a consider hold before its reveal.
    ///
    /// The rule is on the option count and not on the screen: where the recording
    /// could only have done one thing there is nothing to consider, and arrival is the
    /// reveal. A count that could not be read gets no hold either, because a hold on
    /// a screen nobody counted would be the mod guessing there was a choice.
    /// </summary>
    public bool HasConsiderBeat => NextOptionCount is > 1;
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
/// The vocabulary is consider, reveal, commit. The game's screen arrives with
/// nothing lit and the tag holds there - this model's Considering mode - so the
/// viewer sees every option the recording saw; reveal applies the game's own
/// selected state to the target without clicking and the tag holds again, in
/// Watching; commit calls the game's own click path. Step is therefore pressed
/// twice per decision, and under play the two holds drain one after the other on
/// the same timer. Nothing the viewer thought is captured, compared or scored. Look
/// back re-shows a decision already made and never uncommits one, which is why
/// <see cref="TransportMode.LookingBack"/> is a way of reading rather than a way of
/// moving.
///
/// Nothing here is written down about one recording. The video comes from the
/// manifest's source record, the credit from <see cref="RecordingIdentity"/> with the
/// library's ownership fact, each caption's subject from that credit, and the counter
/// from how many decisions there are.
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
    IReadOnlyList<MenuRow> ChipMenu,
    PostFightChoice? PostFight = null)
{
    /// <summary>The mark; without its centre dot while there is nothing to point at
    /// yet, or the warning that replaces it while a refusal is up.</summary>
    public TransportGlyph Mark => Mode switch
    {
        TransportMode.Refused => TransportGlyph.Warn,
        TransportMode.Considering => TransportGlyph.MarkUnlit,
        _ => TransportGlyph.Mark,
    };

    /// <summary>
    /// What the tag is, element by element - the table the strip draws and the one
    /// place any of it is decided.
    ///
    /// Written out per mode rather than assembled from conditions, because the table
    /// <em>is</em> the design: reading a column tells you the whole of what one mode
    /// looks like, and a cell that is wrong is wrong in one legible place. The strip
    /// projects this and never reads <see cref="Mode"/>.
    /// </summary>
    public TransportSurface Surface => Mode switch
    {
        TransportMode.Watching => new TransportSurface(
            ChipPlate: false,
            Mark: ElementSurface.Shown(Mark),
            Identity: IdentityElement(Identity.IsLink),
            Title: ElementSurface.ShownIf(Identity.VideoTitle is not null),
            Counter: ElementSurface.Shown(),
            Speed: SpeedElement(pressable: true),
            Back: Projected(Back, Press.Back),
            Play: Projected(Play, Press.PlayOrPause),
            Step: Projected(Step, Press.Step),
            HoldLine: true,
            Note: Note.Length > 0,
            Ledger: false,
            Menu: MenuKind.Speed),

        // The same elements as Watching. What differs is drawn from the state rather
        // than decided here: the mark without its dot, the hollow current pip, and
        // step's tooltip saying Show.
        TransportMode.Considering => new TransportSurface(
            ChipPlate: false,
            Mark: ElementSurface.Shown(Mark),
            Identity: IdentityElement(Identity.IsLink),
            Title: ElementSurface.ShownIf(Identity.VideoTitle is not null),
            Counter: ElementSurface.Shown(),
            Speed: SpeedElement(pressable: true),
            Back: Projected(Back, Press.Back),
            Play: Projected(Play, Press.PlayOrPause),
            Step: Projected(Step, Press.Step),
            HoldLine: true,
            Note: false,
            Ledger: false,
            Menu: MenuKind.Speed),

        TransportMode.LookingBack => new TransportSurface(
            ChipPlate: false,
            Mark: ElementSurface.Shown(Mark),
            Identity: IdentityElement(Identity.IsLink),
            Title: ElementSurface.ShownIf(Identity.VideoTitle is not null),
            Counter: ElementSurface.Shown(),
            Speed: SpeedElement(pressable: true),
            Back: Projected(Back, Press.Back),
            Play: Projected(Play, Press.PlayOrPause),
            Step: Projected(Step, Press.Step),
            // No hold: look back stops Play, and a line draining under a decision
            // nobody is about to commit would be saying something untrue.
            HoldLine: false,
            Note: false,
            Ledger: Ledger.Count > 0,
            Menu: MenuKind.Speed),

        // The tag it was, with everything that moves the run refused. Refused rather
        // than absent: controls that vanish for a second and come back are the popup
        // this design replaced.
        TransportMode.Opening => new TransportSurface(
            ChipPlate: false,
            Mark: ElementSurface.Shown(Mark),
            Identity: IdentityElement(Identity.IsLink),
            Title: ElementSurface.ShownIf(Identity.VideoTitle is not null),
            Counter: ElementSurface.Shown(),
            Speed: SpeedElement(pressable: true),
            Back: Projected(Back, Press.Back),
            Play: Projected(Play, Press.PlayOrPause),
            Step: Projected(Step, Press.Step),
            // The one thing on the tag that is still moving. A row of controls that
            // all went dead with nothing else changing reads as broken rather than as
            // busy, and this window says nothing in words by the captain's ruling, so
            // what is happening is shown instead.
            HoldLine: true,
            Note: false,
            Ledger: false,
            Menu: MenuKind.Speed),

        // The mark and the name, and one silent press target over the whole plate.
        // Silent is what lets the chip say nothing until it is pressed and still be
        // pressable at all; a video title on a plate this narrow is not "the mark and
        // the name", so it goes.
        TransportMode.Chip => new TransportSurface(
            ChipPlate: true,
            Mark: ElementSurface.Shown(Mark),
            Identity: ElementSurface.Absent,
            Title: ElementSurface.Absent,
            Counter: ElementSurface.Absent,
            Speed: new ElementSurface(Presence.Silent, Pressable: true, Press.OpenChipMenu),
            Back: ElementSurface.Absent,
            Play: ElementSurface.Absent,
            Step: ElementSurface.Absent,
            HoldLine: false,
            Note: false,
            Ledger: false,
            Menu: MenuKind.Chip),

        // The chip it was during the fight, with the post-fight choice under it in
        // place of the two directions. The one press target closes and re-opens the
        // choice; the rows themselves are the choice's.
        TransportMode.Ended => new TransportSurface(
            ChipPlate: true,
            Mark: ElementSurface.Shown(Mark),
            Identity: ElementSurface.Absent,
            Title: ElementSurface.Absent,
            Counter: ElementSurface.Absent,
            Speed: new ElementSurface(Presence.Silent, Pressable: true, Press.OpenPostFightMenu),
            Back: ElementSurface.Absent,
            Play: ElementSurface.Absent,
            Step: ElementSurface.Absent,
            HoldLine: false,
            Note: false,
            Ledger: false,
            Menu: MenuKind.PostFight),

        // Everything refused, the speed included: a tag that has lost its run has not
        // kept one control that still works. What keeps the derivation total rather
        // than a surface anybody sees - the teardown detaches the tag in the same call
        // stack that applies this, so no frame is drawn with it.
        TransportMode.Refused => new TransportSurface(
            ChipPlate: false,
            Mark: ElementSurface.Shown(Mark),
            Identity: IdentityElement(pressable: false),
            Title: ElementSurface.ShownIf(Identity.VideoTitle is not null),
            Counter: ElementSurface.Absent,
            Speed: SpeedElement(pressable: false),
            Back: Projected(Back, Press.Back),
            Play: Projected(Play, Press.PlayOrPause),
            Step: Projected(Step, Press.Step),
            HoldLine: false,
            Note: false,
            Ledger: false,
            Menu: MenuKind.None),

        _ => throw new ManifestException($"The transport has no surface for {Mode}."),
    };

    // A block with nothing to open carries no tooltip rather than a sentence promising
    // a video the manifest does not have. A disabled Godot button still raises its
    // tooltip on hover, so the words have to be withheld here rather than left to
    // pressability.
    private ElementSurface IdentityElement(bool pressable) => Identity.IsLink
        ? new ElementSurface(
            Presence.Drawn, pressable, Press.OpenVideo, Glyph: null,
            Identity.TooltipTitle, Identity.TooltipBody)
        : new ElementSurface(Presence.Drawn, false, Press.None);

    private ElementSurface SpeedElement(bool pressable) => new(
        Presence.Drawn, pressable, Press.OpenSpeedMenu, Glyph: null,
        TrainerCopy.SpeedTooltipTitle,
        pressable ? TrainerCopy.SpeedTooltipBody : TrainerCopy.RefusedDisabledReason);

    /// <summary>
    /// One control as the strip sees it.
    ///
    /// A refused control's tooltip says why it is refused rather than repeating what
    /// it would have done, and falls back to what it does where no reason has been
    /// written - which is the case in the two windows whose sentence is
    /// <see cref="TrainerCopy.BetweenScreensDisabledReason"/>, still unwritten.
    /// </summary>
    private static ElementSurface Projected(TransportControl control, Press press) => new(
        Presence.Drawn,
        control.Enabled,
        press,
        control.Glyph,
        control.TooltipTitle,
        control.Enabled ? control.TooltipBody : control.DisabledReason ?? control.TooltipBody);

    /// <summary>
    /// The one way to get a transport: total, pure, and the same answer for the same
    /// facts however it is reached.
    ///
    /// Total is the part that matters. Every phase a journey can be in has an answer
    /// here, <c>null</c> included for the two that put nothing on screen, so a phase
    /// change is a re-derivation rather than a site that has to remember to build the
    /// right state - which is what four defects on this surface came down to. The five
    /// shapes below are private because of it: there is no way to construct a state
    /// that the phase and the facts did not ask for.
    /// </summary>
    /// <returns>What the tag says, or null while nothing is docked.</returns>
    public static PlaybackTransport? For(JourneyPhase phase, TransportFacts facts) => phase switch
    {
        JourneyPhase.None or JourneyPhase.Preparing or JourneyPhase.Starting => null,
        JourneyPhase.Watching when facts.AtCombatStart =>
            OpeningTheFight(facts.Identity, facts.Count, facts.Speed, facts.RestoredToFloor),
        JourneyPhase.Watching when facts.LookingBackAt is { } step => LookingBackAt(
            facts.Identity, facts.Made, step, facts.StepsTaken + 1, facts.Count, Next(facts), facts.Speed),
        JourneyPhase.Watching => Revealing(
            facts.Identity, Next(facts), facts.StepsTaken + 1, facts.Count, facts.Playing, facts.NoteShown,
            facts.Arrived, facts.Lit, facts.HasConsiderBeat, facts.Speed),
        JourneyPhase.InFight or JourneyPhase.Result => DuringYourFight(
            facts.Identity, facts.AnythingPlayed, facts.Speed, phase == JourneyPhase.Result),
        JourneyPhase.Ended => AfterTheFight(
            facts.Identity, facts.Speed, facts.AfterTheFight ?? throw new ManifestException(
                "The fight has ended and the transport has not been told how, so the post-fight choice " +
                "cannot be put on it.")),
        JourneyPhase.Refused => Refused(facts.Identity, facts.Speed),
        _ => throw new ManifestException($"A journey cannot be in phase {phase}."),
    };

    private static PrefightChoice Next(TransportFacts facts) =>
        facts.Next ?? throw new ManifestException(
            "The recording has no decision left to show, so the transport cannot be put on one.");

    /// <summary>The speed menu, with the current row marked.</summary>
    public IReadOnlyList<MenuRow> SpeedMenu =>
        [.. PlaybackSpeeds.All.Select(speed =>
            new MenuRow(null, speed.Label(), IsCurrent: speed == Speed))];

    public string SpeedLabel => Speed.Label();

    /// <summary>
    /// The recording's next decision: considered, or revealed and held.
    ///
    /// Three states of one decision and one shape for all of them. Lit is the reveal
    /// hold, exactly as it was. Arrived and not lit is the consider hold, where step
    /// reveals rather than commits and nothing on the tag says what the recording
    /// chose: the caption that names the decision stays out of step's tooltip until
    /// the reveal, and the mark and the current pip are drawn without their fill. Not
    /// arrived is the window between screens, where everything that moves the run is
    /// refused because the screen it would act on is not there yet.
    /// </summary>
    /// <param name="number">Which of the recording's decisions this is, from one.</param>
    /// <param name="count">How many the recording makes before its fight.</param>
    /// <param name="playing">Whether Play is running the sequence, which decides
    /// only which glyph the middle button carries.</param>
    /// <param name="noteShown">Whether the once-per-run sentence has been said.</param>
    /// <param name="arrived">Whether the game's screen for this decision is up and
    /// still.</param>
    /// <param name="lit">Whether the decision is lit on that screen.</param>
    /// <param name="considerBeat">Whether this decision gets a hold before its reveal.
    /// Where it does not, arrival is the reveal, and an arrived-unlit state is the
    /// transition itself rather than an offer to show.</param>
    private static PlaybackTransport Revealing(
        TransportIdentity identity, PrefightChoice choice, int number, int count, bool playing,
        bool noteShown, bool arrived, bool lit, bool considerBeat, PlaybackSpeed speed)
    {
        var considering = arrived && !lit && considerBeat;
        var offered = lit || considering;
        return new PlaybackTransport(
            Mode: lit ? TransportMode.Watching : TransportMode.Considering,
            Identity: identity,
            Counter: Check(number, count) with { Lit = lit },
            Speed: speed,
            Back: BackControl(number > 1, offered),
            Play: PlayControl(playing, enabled: playing || offered),
            Step: lit
                ? StepControl(number, count, Describe(identity.Credit, choice))
                : ShowControl(identity.Credit, number, count, considering),
            Ledger: [],
            // Said once, before the first decision anybody watches, and at the reveal
            // rather than before it: a rule about how to read these screens is worth
            // saying once and tiresome above every one.
            Note: lit && number == 1 && !noteShown
                ? TrainerCopy.ChoicesShownAsRecorded(identity.Credit)
                : string.Empty,
            ChipMenu: []);
    }

    /// <summary>
    /// The chip with the post-fight choice under it.
    ///
    /// The rows are <see cref="PostFightChoice"/>'s and carried here as the chip's
    /// menu, so the strip hangs them in the shape it already has. Every control that
    /// moves the run is absent, as on the chip: the choice is the whole of what is
    /// offered.
    /// </summary>
    private static PlaybackTransport AfterTheFight(
        TransportIdentity identity, PlaybackSpeed speed, PostFightFacts facts)
    {
        var choice = PostFightChoice.For(identity.Credit, facts);
        return new PlaybackTransport(
            Mode: TransportMode.Ended,
            Identity: identity,
            Counter: new TransportCounter(0, 0, null),
            Speed: speed,
            Back: BackControl(false),
            Play: PlayControl(playing: false) with { Enabled = false },
            Step: StepControl(0, 0, string.Empty) with { Enabled = false },
            Ledger: [],
            Note: string.Empty,
            ChipMenu: choice.Menu,
            PostFight: choice);
    }

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
    private static PlaybackTransport LookingBackAt(
        TransportIdentity identity, IReadOnlyList<PrefightChoice> made, int shown, int current, int count,
        PrefightChoice next, PlaybackSpeed speed)
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
            Step: StepControl(current, count, Describe(identity.Credit, next), commits: false),
            Ledger:
            [
                .. made.Select((choice, index) => new LedgerRow(
                    index + 1,
                    ArtOf(choice),
                    Describe(identity.Credit, choice, name: false),
                    IsCurrent: false,
                    IsLookedAt: index + 1 == shown)),
                new LedgerRow(
                    current, ArtOf(next), Describe(identity.Credit, next, name: false),
                    IsCurrent: true, IsLookedAt: false),
            ],
            Note: string.Empty,
            ChipMenu: []);
    }

    /// <summary>
    /// The fight is the player's.
    ///
    /// The tag collapses to a chip carrying the mark and the recording's credit and nothing
    /// else, silent until it is pressed. Not an oversight: the captain's ruling is
    /// that comparing inside a fight is second-order, because a player diverges from
    /// the recorded line almost immediately, so the comparison points are the whole
    /// recorded fight watched and the finished fight's result.
    ///
    /// Pressed, it offers two directions and no third. Both leave the attempt, so
    /// both go through the game's own confirmation first.
    /// </summary>
    /// <param name="anythingPlayed">Whether the player has taken an action of their own
    /// yet. With nothing played there is no end to jump to; one card is enough.</param>
    /// <param name="speed">The speed in force, carried through rather than reset. The
    /// chip does not show it, but the tag it collapsed from did and the tag it becomes
    /// again will, and a state that quietly answered Normal made a chosen speed appear
    /// not to have taken.</param>
    /// <param name="fightOver">Whether the fight has ended and its result is waiting to
    /// be shown. Both directions act on a fight that no longer exists - one would
    /// finish a fight that has finished, the other would discard an attempt whose
    /// result is already in hand and then show it over the run that replaced it - so
    /// both are refused there. The chip itself stays, drawn and pressable, because a
    /// press target that disappears for two seconds is the flicker this design
    /// replaced.</param>
    private static PlaybackTransport DuringYourFight(
        TransportIdentity identity, bool anythingPlayed, PlaybackSpeed speed, bool fightOver) =>
        new(
            Mode: TransportMode.Chip,
            Identity: identity,
            Counter: new TransportCounter(0, 0, null),
            Speed: speed,
            Back: BackControl(false),
            Play: PlayControl(playing: false) with { Enabled = false },
            Step: StepControl(0, 0, string.Empty) with { Enabled = false },
            Ledger: [],
            Note: string.Empty,
            ChipMenu:
            [
                new MenuRow(TransportGlyph.Again, TrainerCopy.JumpToTheBeginning, !fightOver),
                new MenuRow(TransportGlyph.Jump, TrainerCopy.JumpToTheEnd, anythingPlayed && !fightOver),
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
    /// <param name="restoredToFloor">The floor the run was restored to, where it was
    /// restored rather than walked. The counter then names the floor instead of
    /// counting decisions nobody watched. This window says nothing in words by the
    /// captain's ruling, so the floor is the whole of what it says.</param>
    private static PlaybackTransport OpeningTheFight(
        TransportIdentity identity, int count, PlaybackSpeed speed, int? restoredToFloor) =>
        new(
            Mode: TransportMode.Opening,
            Identity: identity,
            Counter: restoredToFloor is { } floor
                ? new TransportCounter(0, 0, null, RestoredToFloor: floor)
                : new TransportCounter(count, count, null),
            Speed: speed,
            Back: BackControl(false) with { DisabledReason = TrainerCopy.BetweenScreensDisabledReason },
            Play: PlayControl(playing: false) with
            {
                Enabled = false,
                DisabledReason = TrainerCopy.BetweenScreensDisabledReason,
            },
            Step: StepControl(0, 0, string.Empty) with
            {
                Enabled = false,
                DisabledReason = TrainerCopy.BetweenScreensDisabledReason,
            },
            Ledger: [],
            Note: string.Empty,
            ChipMenu: []);

    /// <summary>
    /// A screen could not be driven.
    ///
    /// The sentence a player reads is the popup's, and today it is the only thing they
    /// read: the mod's teardown applies this and detaches the tag inside one call
    /// stack, so the state is never on screen for a frame. It exists because every
    /// phase a journey can be in has to have an answer, and it is written the way a
    /// drawn one would be - every control refused rather than removed - so that
    /// holding the tag on screen across the return to the menu stays a change of
    /// timing rather than of model. That it is not drawn is settled rather than
    /// pending, decided by the project's coordinating owner: the popup is the refusal
    /// a player reads, and drawing this one would mean keeping the tag alive across a
    /// return to the main menu that its parent interface does not survive.
    /// </summary>
    private static PlaybackTransport Refused(TransportIdentity identity, PlaybackSpeed speed) =>
        new(
            Mode: TransportMode.Refused,
            Identity: identity,
            Counter: new TransportCounter(0, 0, null),
            Speed: speed,
            Back: BackControl(false) with { DisabledReason = TrainerCopy.RefusedDisabledReason },
            Play: PlayControl(false) with { Enabled = false, DisabledReason = TrainerCopy.RefusedDisabledReason },
            Step: StepControl(0, 0, string.Empty) with
            {
                Enabled = false,
                DisabledReason = TrainerCopy.RefusedDisabledReason,
            },
            Ledger: [],
            Note: string.Empty,
            ChipMenu: []);

    /// <summary>
    /// Look back at a decision the recording already made.
    ///
    /// Refused in the same window Play and Step are, and for the same reason: a press
    /// between committing one decision and revealing the next acts on a state nobody
    /// has been shown, and the reveal that follows clears it with no input from the
    /// player - so the control appeared to work and was undone a frame later.
    ///
    /// The two refusals are different and say so. Nothing behind yet is not the same
    /// as not yet, and when both hold the first is the one worth saying.
    /// </summary>
    private static TransportControl BackControl(bool enabled, bool revealed = true)
    {
        var offered = enabled && revealed;
        var reason = offered ? null
            : enabled ? TrainerCopy.BetweenScreensDisabledReason
            : TrainerCopy.NothingBehindYet;
        return new TransportControl(
            TransportGlyph.Back, offered, TrainerCopy.BackTooltipTitle, TrainerCopy.BackTooltipBody, reason);
    }

    /// <summary>
    /// Play, or Pause once it is running.
    ///
    /// Refused in the same window Step is: a press there would start the sequence on a
    /// decision nobody has been shown. Pause is never refused - it stops the run
    /// rather than moving it, and a sequence that cannot be stopped mid-transition is
    /// the reason somebody reaches for it.
    /// </summary>
    private static TransportControl PlayControl(bool playing, bool enabled = true) => playing
        ? new TransportControl(
            TransportGlyph.Pause, enabled, TrainerCopy.PauseTooltipTitle, TrainerCopy.PauseTooltipBody,
            enabled ? null : TrainerCopy.BetweenScreensDisabledReason)
        : new TransportControl(
            TransportGlyph.Play, enabled, TrainerCopy.PlayTooltipTitle, TrainerCopy.PlayTooltipBody,
            enabled ? null : TrainerCopy.BetweenScreensDisabledReason);

    /// <summary>
    /// Step's tooltip names the decision it is about to make.
    ///
    /// The caption the wide bar drew always is here instead, which is the captain's
    /// tooltips-only ruling: the picture is the lit target on the game's own screen,
    /// and the words are one hover away.
    /// </summary>
    /// <param name="commits">Whether pressing Step here makes the choice it names.
    /// While looking back it does not - it walks the view forward through decisions
    /// already made and commits nothing - so the sentence promising a commit is left
    /// off rather than replaced. Removing a statement that has become false needs no
    /// approval; writing a new true one would, and the counter and the caption already
    /// say which decision is being looked at.</param>
    private static TransportControl StepControl(
        int number, int count, string caption, bool enabled = true, bool commits = true)
    {
        var promise = commits ? TrainerCopy.StepTooltipBody : string.Empty;
        var body = count == 0
            ? promise
            : string.IsNullOrEmpty(promise)
                ? $"{TrainerCopy.StepCounter(number, count)} · {caption}"
                : $"{promise}\n{TrainerCopy.StepCounter(number, count)} · {caption}";
        return new TransportControl(
            TransportGlyph.Step, enabled, TrainerCopy.StepTooltipTitle, body,
            enabled ? null : TrainerCopy.BetweenScreensDisabledReason);
    }

    /// <summary>
    /// Step's tooltip before the reveal: the first of its two presses.
    ///
    /// Titled Show rather than Step because a tooltip that names an action it does
    /// not perform is the same defect as one that cannot be pressed, and the first
    /// press does not commit. The counter is kept and the caption is not: the caption
    /// names what the recording chose, and nothing on the tag may say that before the
    /// reveal. Refused, as every control is between screens, it goes on saying what
    /// it does.
    /// </summary>
    private static TransportControl ShowControl(
        RecordingCredit credit, int number, int count, bool enabled) => new(
        TransportGlyph.Step,
        enabled,
        TrainerCopy.ShowTooltipTitle,
        $"{TrainerCopy.ShowTooltipBody(credit)}\n{TrainerCopy.StepCounter(number, count)}",
        enabled ? null : TrainerCopy.BetweenScreensDisabledReason);

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
    private static string Describe(
        RecordingCredit credit, PrefightChoice choice, bool name = true) => choice switch
        {
            PrefightChoice.Blessing { CardsPicked.Count: > 0 } blessing => name
                ? TrainerCopy.BlessingWithCardsCaption(credit, blessing.RelicModelId, blessing.CardsPicked)
                : TrainerCopy.BlessingWithCardsLedgerRow(blessing.RelicModelId, blessing.CardsPicked),
            PrefightChoice.Blessing blessing => name
                ? TrainerCopy.BlessingCaption(credit, blessing.RelicModelId)
                : TrainerCopy.BlessingLedgerRow(blessing.RelicModelId),
            PrefightChoice.MapMove move => name
                ? TrainerCopy.MapMoveCaption(
                    credit, move.NodeType, MapColumns.Position(move.Column, move.ColumnCount))
                : TrainerCopy.MapMoveLedgerRow(
                    move.NodeType, MapColumns.Position(move.Column, move.ColumnCount)),
            PrefightChoice.CardFromScreen card => name
                ? TrainerCopy.CardFromScreenCaption(credit, card.CardModelId)
                : TrainerCopy.CardFromScreenLedgerRow(card.CardModelId),
            _ => throw new ManifestException(
                $"Action {choice.Seq} is a kind of decision this trainer has no way to describe, so the recording " +
                "cannot be watched making it. Only an opening blessing, a map move and a card taken off a screen " +
                "one of them opened are supported before a fight."),
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
        PrefightChoice.CardFromScreen card => card.CardModelId,
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
