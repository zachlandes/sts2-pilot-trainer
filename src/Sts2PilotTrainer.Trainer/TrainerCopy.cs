using System.Globalization;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Every fixed word the Combat Trainer shows a player, in one place.
///
/// One file so that "what does the mod say" is answerable by reading a file rather
/// than by grepping a scene graph, and so that nothing can drift into inventing a
/// sentence: anything a screen renders is either here, derived from the selected
/// manifest, or a diagnostic <see cref="Sts2PilotTrainer.Replay.EnvironmentPreflight"/>
/// already produces and is shown verbatim.
///
/// These strings are approved wording. Changing one is a product decision, not a
/// refactor. The transport's provisional labels are gone: the accepted design is
/// icon only, so what were button labels are tooltips now, and docs/mod-ui-direction.md
/// is the design they answer to.
/// </summary>
public static class TrainerCopy
{
    /// <summary>The training feature's name, on its mode card and above its result
    /// panel. Not the mod's name: a player installs Runmobile, and the Combat Trainer
    /// is one module inside it. The mod list shows the shell, this shows the
    /// feature.</summary>
    public const string Name = "Combat Trainer";

    /// <summary>
    /// Which fight of the recording this build carries, as a player reads it: the
    /// floor, and the enemy on it.
    ///
    /// The one recording-specific value still written down here, and the reason is a
    /// gap rather than a preference: the floor and the enemy are not manifest fields.
    /// The floor is legible in the recording's top bar and the enemy's name under its
    /// health bar, so both are observable, and the owner-aligned place for them is the
    /// combat-start checkpoint - <c>run.total_floor</c> and <c>combat.enemy.0.model</c>
    /// beside the twelve values already read at that frame. Adding them means reading
    /// the recording again at source resolution, which is manifest work rather than
    /// host work. Until it is done, a second recording needs this line changed with it.
    /// </summary>
    public const string FightFloor = "Floor 2";

    /// <inheritdoc cref="FightFloor"/>
    public const string FightEnemy = "Sludge Spinner";

    /// <summary>The mode card's description. The mod list's own line belongs to the
    /// shell and lives in <c>Runmobile.json</c>.</summary>
    public static string Description(string creator) =>
        $"Fight {creator}'s {FightFloor} {FightEnemy} exactly as recorded, then compare your fight with " +
        "the recording. Reads your game; never writes to it.";

    /// <summary>What this one recording is, under the screen's title.</summary>
    public static string Subtitle(string creator, string character, int ascension) =>
        string.Join(" · ",
            creator,
            ModelIdNames.Display(character),
            $"Ascension {ascension.ToString(CultureInfo.InvariantCulture)}",
            FightFloor,
            FightEnemy);

    // ── Standing in the recording's fight ───────────────────────────────────
    //
    // The wording below is the approved Direction A journey, with every
    // recording-specific value interpolated rather than written down: the creator
    // comes from the manifest's source record, the blessing and the node from the
    // run the recording's own actions are about to act on, and the counter from how
    // many decisions the recording made. Nothing here names this recording.

    /// <summary>Offers the fight, on the eligibility screen.</summary>
    public const string EnterButton = "Enter the fight";

    /// <summary>Shown with <see cref="EnterButton"/>. Load-bearing rather than
    /// reassuring: the run this enters is constructed at the recording's identity and
    /// is never written anywhere, and a player who thought it was theirs would be
    /// looking for it afterwards.</summary>
    public const string NotSavedNote =
        "This fight is not saved and does not count toward your run history.";

    /// <summary>The state signal, on throughout the recording's own decisions and
    /// gone the moment the fight is the player's.</summary>
    public static string WatchingChip(string creator) => $"Watching {creator}";

    // ── The playback transport ─────────────────────────────────────────────
    //
    // Icon only. The captain's ruling is that progressive disclosure is the game's
    // own principle, so the controls carry drawn glyphs and the words live in their
    // tooltips; there is no always-visible caption line. What each tooltip says is
    // what the control does, in a player's terms rather than the format's.

    /// <summary>Look back: the hollow glyph, and the promise that goes with it.</summary>
    public const string BackTooltipTitle = "Look back";

    public const string BackTooltipBody = "Shows an earlier choice again. Nothing is undone.";

    /// <summary>Why look back is refused on the first decision.</summary>
    public const string NothingBehindYet = "This is the first choice.";

    public const string PlayTooltipTitle = "Play";

    public const string PlayTooltipBody = "Makes the rest of the choices, pausing on each one.";

    public const string PauseTooltipTitle = "Pause";

    public const string PauseTooltipBody = "Stops here, on this choice.";

    public const string StepTooltipTitle = "Step";

    /// <summary>Step's tooltip names the decision it is about to make; the counter
    /// and the caption are appended to this line.</summary>
    public const string StepTooltipBody = "Makes this choice, then shows the next.";

    /// <summary>The playback speed, which the captain asked for the way a video
    /// player has it.</summary>
    public const string SpeedTooltipTitle = "Speed";

    public const string SpeedTooltipBody = "How long each choice is held before the next one.";

    /// <summary>Why every control is refused while a refusal popup is up.</summary>
    public const string RefusedDisabledReason = "Combat Trainer stopped; dismiss the message first.";

    /// <summary>Where in the recording's decisions this is.</summary>
    public static string StepCounter(int step, int count) =>
        $"{step.ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)}";

    // The ledger's rows. The tag hanging above them names the creator once, so the
    // rows do not: five rows each opening with the same name is the repetition the
    // caption line was replaced to avoid.

    public static string BlessingLedgerRow(string relicModelId) => ModelIdNames.Display(relicModelId);

    public static string MapMoveLedgerRow(string nodeType, string columnPosition) =>
        $"{ModelIdNames.Display(nodeType)} node, {columnPosition} column";

    // The identity block: whose recording, which video, and a way through to the
    // moment being shown.

    public static string IdentityOpensAt(string timestamp) =>
        $"Opens the video at {timestamp}, where this move is made.";

    /// <summary>Shown when the recording has no timestamp for this decision, so
    /// there is nowhere in the video to open at.</summary>
    public const string IdentityNoTimestamp = "Opens the video.";

    // The chip's two directions during the player's own fight. Both leave the
    // attempt, so both are confirmed first; there is no third row, because comparing
    // inside a fight is the second-order thing the captain ruled out.

    public const string JumpToTheBeginning = "Jump to the beginning";

    public const string JumpToTheEnd = "Jump to the end";

    /// <summary>Why jumping to the end is refused before a turn has been played.</summary>
    public const string NothingPlayedYet = "You have not played a turn yet.";

    /// <summary>The confirmation before the one destructive thing the transport
    /// offers. Named plainly: what is lost, and that the same fight comes back.</summary>
    public static string ConfirmJumpToTheBeginningTitle(string creator) => $"Start {creator}'s fight again?";

    public const string ConfirmJumpToTheBeginningBody =
        "This attempt is discarded and the fight starts again from exactly where it started before.";

    public const string ConfirmJumpToTheEndTitle = "Finish here?";

    public const string ConfirmJumpToTheEndBody =
        "This attempt ends where it is and the result is shown.";

    public const string ConfirmKeepFighting = "Keep fighting";

    public const string ConfirmGoBack = "Go back";

    public const string ConfirmFinish = "Finish";


    /// <summary>What the recording did at its opening event.</summary>
    public static string BlessingCaption(string creator, string relicModelId) =>
        $"{creator} took {ModelIdNames.Display(relicModelId)}";

    /// <summary>What the recording did on the map.</summary>
    public static string MapMoveCaption(string creator, string nodeType, string columnPosition) =>
        $"{creator} moved to the {ModelIdNames.Display(nodeType)} node, {columnPosition} column";

    /// <summary>Shown once, the first time a player watches the recording decide
    /// anything. It says what the screens are and, as importantly, what they are
    /// not.</summary>
    public static string ChoicesShownAsRecorded(string creator) =>
        $"{creator}'s choices are shown as recorded. This shows what was chosen, not why.";

    // ── A refusal, in a player's words ─────────────────────────────────────
    //
    // The captain read the first refusal and said it looked like debugging
    // information. It was: the engine's diagnostic, shown verbatim, talking about
    // rows and columns to somebody who has never seen either. So the popup now says
    // what happened in a player's terms and the engine's own sentence is kept behind
    // a fold and in the log. The refusal is not softened - it still stops, still says
    // the game was untouched, and still carries the exact reason for anyone who wants
    // it. Only the sentence a player reads changes.

    /// <summary>Which screen did not match, named the way a player sees it.</summary>
    public static string RefusalHeadline(string creator, string screen) =>
        $"This {screen} doesn't match {creator}'s recording, so Combat Trainer stopped rather than guess.";

    /// <summary>The reassurance that is load-bearing rather than soothing: a refused
    /// entry leaves nothing behind, and a player who thought otherwise would go
    /// looking for a run that was never saved.</summary>
    public const string RefusalNoHarm = "Your game wasn't changed.";

    /// <summary>Opens the engine's own sentence. It is never the first thing shown
    /// and it is never absent.</summary>
    public const string RefusalShowDetails = "Show details";

    public const string RefusalHideDetails = "Hide details";

    /// <summary>The screens this journey walks, as a player names them. A screen with
    /// no name here has no refusal sentence, and the engine's own is shown alone
    /// rather than a wrong noun being invented for it.</summary>
    public const string MapScreenName = "map";

    public const string EventScreenName = "choice";

    public const string PassHeadline = "Your game can play this fight as recorded.";

    public const string FailHeadline = "Your game cannot play this fight as recorded yet.";

    /// <summary>
    /// Says which profile the unlock rows were measured against.
    ///
    /// Load-bearing rather than decorative: the game forks a separate profile for
    /// modded play, so a player with a complete unmodded profile can fail these rows
    /// and have no idea why. The remedy is the game's own import, which is why the
    /// sentence names it.
    /// </summary>
    public const string ProfileNote =
        "Checked against the profile the game uses when running modded. If your unmodded progress is " +
        "missing here, import it from the profile select screen.";

    public const string BackButton = "Back";

    /// <summary>The build the recording was made on, as the screen states it.</summary>
    public static string RecordingLine(string buildVersion, string buildDateUtc) =>
        $"Recorded on {buildVersion} ({buildDateUtc})";

    // ── The player's fight, compared with the recording's ───────────────────
    //
    // The approved result wording. Shown once, on the trainer's own result panel,
    // after the player's fight has ended and been captured whole; nothing is shown
    // during the fight. The panel is mostly pictures - card art by turn, two lines on
    // a chart, figures in two columns - so what is left here is the furniture those
    // pictures need and the sentences that are rules rather than captions. Every
    // number comes from CombatComparison and every name from the manifest; nothing
    // below names a recording.

    /// <summary>The panel's title over a comparison.</summary>
    public static string ComparisonTitle(string creator) => $"Your fight and {creator}'s";

    /// <summary>The column the player's numbers sit under.</summary>
    public const string YouColumn = "You";

    /// <summary>The summary row labels, in the contract's order.</summary>
    public const string OutcomeRow = "Outcome";

    public const string TurnsRow = "Turns";

    public const string StartingHealthRow = "Health at the start";

    public const string FinalHealthRow = "Health at the end";

    public const string NetHealthChangeRow = "Net health change";

    public const string PotionsUsedRow = "Potions used";

    public const string CardsRemovedRow = "Cards removed";

    /// <summary>The engine's outcome, as the player reads it.</summary>
    public const string WonOutcome = "Won";

    public const string LostOutcome = "Lost";

    public const string EndedOutcome = "Ended";

    /// <summary>An empty list of potions or cards.</summary>
    public const string None = "none";

    /// <summary>Why the two lines can be set beside each other at all: the boundary
    /// was proved identical before the fight was handed over. Load-bearing rather
    /// than reassuring - it is what makes every difference below a difference in the
    /// fighting rather than in the fight.</summary>
    public const string SameBoundaryNote = "Both fights started from the same position.";

    public const string TurnDetailHeading = "Turn by turn";

    /// <summary>Over the chart of what each turn cost either side.</summary>
    public const string ChartHeading = "Health lost each turn";

    /// <summary>The chart's two measures, in the contract's own terms.</summary>
    public const string EnemyMeasureLabel = "Enemy health lost";

    public const string PlayerMeasureLabel = "Health lost";

    /// <summary>The turn axis, and the turn column of the chronology.</summary>
    public const string TurnLabel = "Turn";

    /// <summary>Where one side reached a turn the other never did. The turn is the
    /// difference, so it is said rather than drawn as a row of zeroes.</summary>
    public const string FightOverLabel = "fight over";

    /// <summary>The contract's caveats, shortened for a panel. The first is the one
    /// that keeps this a statement of differences.</summary>
    public const string NoVerdictNote = "This states differences. It does not say which fight was better.";

    public const string BlockNote =
        "Health lost counts only health that came off. Damage absorbed by block is not counted.";

    /// <summary>Closes the result and returns to the main menu. The run is discarded,
    /// as a refused entry's is.</summary>
    public const string DoneButton = "Done";

    /// <summary>Shown in place of a comparison when the player's fight was lost. The
    /// recording's fight was won, and a lost fight has no completed line to put
    /// beside it.</summary>
    public static string LostNote(string creator) =>
        $"You did not win this fight, so there is no completed line to compare with {creator}'s.";

    /// <summary>Shown in place of a comparison when the fight was left before it
    /// ended: quit, returned to the main menu, or abandoned.</summary>
    public const string LeftNote = "This fight was left before it ended, so there is nothing to compare.";
}
