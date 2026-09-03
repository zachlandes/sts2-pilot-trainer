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
/// refactor.
/// </summary>
public static class TrainerCopy
{
    /// <summary>The mod's name, in the game's mod list and on its mode card.</summary>
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

    /// <summary>The mod list's description, and the mode card's.</summary>
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

    /// <summary>Makes the recording's next decision.</summary>
    public const string NextButton = "Next";

    /// <summary>Makes every remaining recorded decision at once.</summary>
    public const string SkipButton = "Skip to the fight";

    /// <summary>Where in the recording's decisions this is.</summary>
    public static string StepCounter(int step, int count) =>
        $"{step.ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)}";

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
