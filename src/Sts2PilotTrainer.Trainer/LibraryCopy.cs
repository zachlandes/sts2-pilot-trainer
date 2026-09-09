using System.Globalization;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Every fixed word the run library shows a player, in one place.
///
/// The library's counterpart of <see cref="TrainerCopy"/>, and separate from it for
/// the reason that file gives for existing at all: it holds what the recorded-fight journey
/// says, and the recorded-fight journey is one module of three. Splitting them keeps "what
/// does this feature say" answerable by reading one file, and keeps a change to the
/// trainer's wording from touching the library's.
///
/// Two rules hold over everything here, and both are settled rather than stylistic.
///
/// <para><b>One entering verb.</b> A player plays <em>from</em> a run: "Play from this
/// floor", "Play from floor 6", "Continue". No other verb enters a recording
/// anywhere on this surface, because two of them would read as two different things
/// happening.</para>
///
/// <para><b>No fight is named by number.</b> No player has that concept: the strip
/// enumerates floors and the game's own screens count floors, so every row here names
/// a floor. A fight ordinal travels on a row for the entry to use and is never
/// written.</para>
///
/// <para><b>The run noun.</b> What a player has is runs - Others, Mine, "{n} runs".
/// "Recording", "manifest" and "journal" are this project's internal words for
/// the file, and a player never sees one.</para>
///
/// Every value a recording supplies is interpolated. A sentence here that named a
/// creator, an enemy or a node would be a sentence only one recording could be shown
/// under.
/// </summary>
public static class LibraryCopy
{
    /// <summary>
    /// The Compendium card that opens the library.
    ///
    /// The mod's own name rather than a description of the screen, because the card
    /// sits beside the game's own Run History and anything built from the run noun
    /// would read as a second one of those. The main-menu row carries the same word for
    /// the same reason and is the only other place on this surface that names the mod;
    /// between them, whichever one a player found is where they learn which mod put it
    /// there.
    /// </summary>
    public const string CompendiumCard = "Runmobile";

    // ── The browser ────────────────────────────────────────────────────────

    /// <summary>Runs anybody made. The tab a player opens on.</summary>
    public const string CommunityTab = "Others";

    /// <summary>Runs of the player's own, which the recorder wrote.</summary>
    public const string MyRunsTab = "Mine";

    /// <summary>The recordings that travel inside the mod, present with no network
    /// and no index.</summary>
    public const string IncludedGroup = "Included with Runmobile";

    /// <summary>Curated runs from the index, in the curator's order.</summary>
    public const string FeaturedGroup = "Featured";

    /// <summary>Everything else from the index, newest first.</summary>
    public const string RecentGroup = "Recent";

    /// <summary>
    /// The count of runs hidden by compatibility and multiplayer rules.
    ///
    /// The compatibility filter defaults on. Turning it off reveals disabled
    /// incompatible rows; the numeral keeps every remaining omission visible.
    /// </summary>
    public static string NotShown(int count) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} not shown";

    /// <summary>
    /// The whole of why a run is not shown, and the one thing it may name is a build.
    ///
    /// There is nothing else to name: the recorder attaches to single-player runs only,
    /// so no other kind of run ever reaches a list to be hidden from it.
    /// </summary>
    public static string NotShownTooltip(string thisBuild) =>
        "Recorded on a build your game can't play. They come back when a verdict " +
        $"for {thisBuild} arrives; a run code still finds one.";

    /// <summary>What the pane says about the selected run, in the eligibility screen's
    /// green. The pane only ever shows a listed run, so this is all it says about
    /// compatibility.</summary>
    public static string WorksWithYourVersion(string thisBuild) =>
        $"Works with your version · {thisBuild}";

    /// <summary>The pane's ribbon, and the one way to the run view in either tab.</summary>
    public const string OpenTheRun = "Open the run";

    /// <summary>The column that says where in a run this player was working. Named for
    /// a floor rather than a count of fights: not every use of a run is a fight, and a
    /// floor is the unit the game and the strip already count.</summary>
    public const string LastFloorReplayed = "Last floor replayed";

    /// <summary>How many cards the run's deck holds, beside the relics at the pane's
    /// top right. A count nobody recorded is not written at all rather than written as
    /// zero.</summary>
    public static string DeckCount(int cards) =>
        $"{cards.ToString(CultureInfo.InvariantCulture)} cards";

    /// <summary>The list's own header, inside the left pane: the columns it holds. It
    /// sits over the list rather than over the screen, because a header belongs to the
    /// thing it heads.</summary>
    public static string ListHeader() => $"Run · Act reached · {LastFloorReplayed}";

    /// <summary>The last act the recording reached.</summary>
    public static string ActReached(int act) =>
        $"Act {act.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>What a fight on the selected floor is against, under the run view's
    /// strip. The enemy is interpolated; no sentence here names one.</summary>
    public static string FightAgainst(string enemy) => $"Against {enemy}";

    /// <summary>The health the selected position starts at.</summary>
    public static string HealthAt(int hp, int maxHp) =>
        $"{hp.ToString(CultureInfo.InvariantCulture)}/{maxHp.ToString(CultureInfo.InvariantCulture)} HP";

    /// <summary>How the run ended, under the pane's identity. Interpolated from the
    /// recording's own outcome.</summary>
    public static string RunReached(int floor) =>
        $"Reached floor {floor.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The relics a row's strip had no room for.</summary>
    public static string MoreRelics(int count) =>
        $"+{count.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Removes one of the recorder's own runs, through the game's own confirm.
    /// The per-run counterpart of the settings row's Remove all my runs.</summary>
    public const string RemoveThisRun = "Remove this run";

    /// <summary>What the confirm asks before it. It says what is not touched, because
    /// what a player is afraid of when a mod offers to remove something is the run
    /// history and the save beside it.</summary>
    public const string RemoveThisRunBody =
        "This removes the run Runmobile recorded. Your saves, profile and run history are not touched.";

    /// <summary>The confirm's two ribbons. Removing is the affirmative and keeping is
    /// the way out, which is what a player lands on.</summary>
    public const string Remove = "Remove";

    /// <inheritdoc cref="Remove"/>
    public const string KeepIt = "Keep it";

    /// <summary>
    /// What the run-code field says before anything is typed in it.
    ///
    /// A code finds a run hidden by the default compatibility filter and selects it in
    /// the list, so somebody who was sent one sees the run and why it is disabled.
    /// </summary>
    public const string RunCodeField = "Run code";

    public const string CompatibleFilter = "Compatible with your game version";

    public const string FetchRunIndex = "Fetch the run index";

    /// <summary>
    /// The main-menu row itself: the mod's own name, the same word the Compendium card
    /// carries, because they are two ways to the one library rather than two things.
    /// </summary>
    public const string MainMenuRow = "Runmobile";

    /// <summary>
    /// The settings control that governs whether that row is drawn, value included.
    ///
    /// The value is in the line rather than beside it, the way the run-index control
    /// already states its own: a control whose label and value were two elements is one
    /// that can show a reading taken before a press beside a label written after it.
    /// </summary>
    public static string MainMenuRowSetting(bool shown) =>
        $"{MainMenuRow} on the main menu: {(shown ? "on" : "off")}";

    public const string FetchingRunIndex = "Fetching the run index…";

    public const string FetchRunIndexFailed =
        "The run index could not be fetched. Direct run-code lookup is still available.";

    public const string SharingServiceUnavailable =
        "Online sharing is unavailable because no authorized service is configured.";

    public const string LookingUpRunCode = "Looking up that run code…";

    public const string SubmitThisRun = "Share this run";

    public const string SharePrivacy = "No other personal information travels with this run.";

    public const string ShareConsent = "I release this run under CC0.";

    public const string ShareLocalValidation = "Validation runs locally before anything is sent.";

    public const string ShareValidating = "Validating this run locally…";

    public const string ShareNameField = "Run name (40 characters)";

    public const string ShareDescriptionField = "Description (200 characters)";

    public const string ShareDisplayNameField = "Display name (required for sharing)";

    public const string ShareSubmit = "Share";

    /// <summary>What the player's own runs occupy, under the Mine list.</summary>
    public static string MyRunsFooter(int runs, string size) =>
        $"{runs.ToString(CultureInfo.InvariantCulture)} runs · {size} on this computer";

    /// <summary>Where the count and the size are acted on. The footer carries no
    /// control of its own: removing runs is a setting, and two places to do it would
    /// be two places to get it wrong.</summary>
    public const string MyRunsFooterAction = "Keep or remove them in Settings";

    // ── Looking a run up by its code ───────────────────────────────────────

    /// <summary>
    /// What a run code answers with when it names a run this game cannot play.
    ///
    /// The build sentence shown on an incompatible row selected by exact code.
    /// A player who typed a code asked about a specific run and is owed both the build
    /// it requires and the build currently running.
    /// </summary>
    public const string LookupRefusedTitle = "Not playable on your version";

    /// <inheritdoc cref="LookupRefusedTitle"/>
    public static string LookupRefusedBuild(string recordedBuild, string thisBuild) =>
        $"This run exists. It was recorded on {recordedBuild} and your game is {thisBuild}.";

    /// <summary>Said under <see cref="LookupRefusedBuild"/>, because the run is not
    /// gone - it is waiting on a verdict.</summary>
    public static string LookupRefusedBuildNote(string thisBuild) =>
        $"It returns to the list if a verdict for {thisBuild} arrives.";

    /// <summary>
    /// What a run code answers with for a run recorded on this very build that this
    /// game can no longer reproduce.
    ///
    /// Its own sentence rather than the build one, because the build one would print
    /// the same build twice and promise a verdict that already exists and already
    /// failed. It names what is true and diagnoses nothing: which prerequisite moved is
    /// the eligibility screen's to say, not a popup's.
    /// </summary>
    /// <summary>A multiplayer run remains refused regardless of later verdicts.</summary>
    public const string LookupRefusedMultiplayer =
        "This run exists. It is a multiplayer run, and Runmobile plays single-player runs.";

    public const string LookupRefusedNoLongerMatches =
        "This run exists. It was recorded on your build, and your game no longer matches what it was " +
        "recorded under.";

    /// <summary>Said under <see cref="LookupRefusedNoLongerMatches"/>: the run is not
    /// gone, and what it is waiting for is the game rather than a verdict.</summary>
    public const string LookupRefusedNoLongerMatchesNote =
        "It returns to the list when your game matches it again.";

    /// <summary>
    /// What a run code answers with for a run recorded on this very build that this
    /// game could not be read to judge.
    ///
    /// A verdict nobody could reach is not the same fact as a verdict that does not
    /// exist, so it gets its own sentence rather than borrowing the build one - which
    /// would name one build twice and promise a verdict that is not what is missing.
    /// What went wrong is a line in the log, not something a player reads.
    /// </summary>
    public const string LookupRefusedUnjudged =
        "This run exists. It was recorded on your build, and your game could not be read to say whether " +
        "it plays.";

    /// <summary>Said under <see cref="LookupRefusedUnjudged"/>: what the run is waiting
    /// for is a reading, and it is one this game takes again every time.</summary>
    public const string LookupRefusedUnjudgedNote = "It returns to the list once it can.";

    /// <summary>What a code that names nothing answers with.</summary>
    public const string LookupNotFoundTitle = "No run with that code";

    /// <inheritdoc cref="LookupNotFoundTitle"/>
    public const string LookupNotFound =
        "Nothing here is that run. Check the code, or open Others and pick a run from the list.";

    /// <summary>Leaves any of the lookup's answers.</summary>
    public const string Back = "Back";

    /// <summary>The two rows a column too long for the panel spends its last places on.
    /// Rows rather than a control of their own, because everything a player presses on
    /// this surface is a row and a second kind would be a second thing to learn.</summary>
    public const string PreviousPage = "Previous";

    /// <inheritdoc cref="PreviousPage"/>
    public const string NextPage = "Next";

    // ── The run view ───────────────────────────────────────────────────────

    /// <summary>
    /// The one play-from row. Its label never changes; its second line does.
    ///
    /// One row rather than two, because a fight is one thing a floor can hold rather
    /// than a thing beside it. Where it stands a player follows the floor's own kind:
    /// a combat floor's fight start, and any other floor's entry.
    /// </summary>
    public const string PlayFromThisFloor = "Play from this floor";

    /// <summary>
    /// The play-from row's second line: the selected floor and what it held.
    ///
    /// A kind nothing established is left unnamed rather than guessed, so the line then
    /// reads "Floor 7" and stops. That is the honest answer for a floor whose recording
    /// made no decision saying what was there.
    /// </summary>
    public static string FloorLine(int floor, FloorKind kind = FloorKind.Unknown)
    {
        var number = $"Floor {floor.ToString(CultureInfo.InvariantCulture)}";
        return KindWord(kind) is { } word ? $"{number} · {word}" : number;
    }

    /// <summary>
    /// What a floor's kind is called on this surface, or null where nothing established
    /// one.
    ///
    /// Lower case, because it follows a floor number in one line rather than heading
    /// anything: "Floor 3 · combat", "Floor 4 · shop", "Floor 11 · event".
    /// </summary>
    public static string? KindWord(FloorKind kind) => kind switch
    {
        FloorKind.Combat => "combat",
        FloorKind.Shop => "shop",
        FloorKind.Rest => "rest",
        FloorKind.Event => "event",
        FloorKind.Treasure => "treasure",
        FloorKind.Unknown => null,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "This surface has no word for it."),
    };

    /// <summary>
    /// The one row that does not move with the selection: the next fight this player
    /// has not played from.
    ///
    /// It names no fight number, which is the settled rule for everything on this
    /// surface. Its second line is that fight's floor, and the drawing puts the enemy's
    /// map icon beside it where the recording names one.
    /// </summary>
    public const string ContinueFromNextUnplayed = "Continue";

    /// <summary>
    /// Walks the recording from its beginning, showing every decision on the game's own
    /// screens, and comes to rest at the first fight.
    ///
    /// Every way into a recorded run walks it from run start - <c>RecordedFightEntry</c>
    /// has one journey and every plan replays the recording's own decisions up to its
    /// boundary, with the transport revealing each. The rows differ only in where they
    /// come to rest. This one always rests at the run's first fight; the play-from row
    /// rests wherever the strip is pointing. So on the first fight's own floor the two
    /// are one offer reached from two rows, and on every other floor they are different
    /// destinations - which is what this row is for: it is the offer that does not move
    /// with the selection.
    ///
    /// <para>It carries no second line. One said nothing its label did not, and a second
    /// line exists here only where it adds a fact the label lacks.</para>
    /// </summary>
    public const string StartTheRunOver = "Start the run over";

    /// <summary>Why a fight the recording stops inside offers nothing: there is no
    /// finished recorded line for a player's own to be set beside.</summary>
    public const string FightNotFinished = "the recording does not reach the end of this fight";

    /// <summary>Why the run's first floor is not a floor to play from: it is where
    /// starting the run over already puts you.</summary>
    public const string RunStartsHere = "the run starts here";

    /// <summary>
    /// Why a place further into the run offers nothing yet: getting there means
    /// replaying a fight the recording already fought, and this build cannot drive the
    /// game through one.
    ///
    /// It names what is missing rather than what the player did wrong, because nothing
    /// about their run is at fault - the recording holds the floor, the arbiter can
    /// stand in it, and it is this client that cannot walk there. Said in the same
    /// lower-case fragment the other two refusals use, because it takes the same place
    /// on the row.
    /// </summary>
    public const string EarlierFightNotReplayable =
        "this build cannot yet replay the fights before it";

    /// <summary>Said on the floor pane, and only there. From a floor entry nothing is
    /// compared, and a player who expected a comparison would be waiting for one that
    /// never comes.</summary>
    public const string FloorIsYours = "The run is yours from here.";

    /// <summary>
    /// Said once, beside the row that plays from a run, and never as a head line.
    ///
    /// The run noun rather than <see cref="TrainerCopy.NotSavedNote"/>'s fight, because
    /// this surface plays from a floor as readily as from a fight and a sentence about
    /// "this fight" would be wrong on half of them. It is the same load-bearing claim
    /// that one makes: the run a row enters is constructed at the recording's identity
    /// and is never written anywhere, and a player who thought it was theirs would go
    /// looking for it afterwards.
    ///
    /// <para>It is drawn only where a play-from row is actually offered. A surface whose
    /// rows are all refused has nothing to warn anybody about, and the sentence there
    /// would read as a reason the rows are refused.</para>
    /// </summary>
    public const string NotSaved =
        "Playing from a run is not saved and does not count toward your run history.";

    // ── The run-history plate ──────────────────────────────────────────────

    /// <summary>
    /// The head line of a recording the recorder lost sight of part way through.
    ///
    /// A head exists only where there is a status to state. The ordinary state has
    /// none: the history row's own record mark already says the run is recorded, and a
    /// head line repeating it would be the plate introducing itself.
    /// </summary>
    public const string PlateCantBeReplayed = "Can't be replayed";

    /// <summary>The head line of a recording this game cannot play, in the eligibility
    /// screen's red. Both versions, because a player reading it needs to see which of
    /// the two is theirs.</summary>
    public static string PlateRecordedOn(string recordedBuild, string thisBuild) =>
        $"Recorded on {recordedBuild} · your game is {thisBuild}";

    /// <summary>The plate's play-from row, for the floor the run ended at, named with
    /// its kind exactly as the run view's own row names a floor.</summary>
    public static string PlayFromFloor(int floor, FloorKind kind = FloorKind.Unknown)
    {
        var number = $"Play from floor {floor.ToString(CultureInfo.InvariantCulture)}";
        return KindWord(kind) is { } word ? $"{number} · {word}" : number;
    }

    /// <summary>The play-from row when the recording proves no floor to name. Kept in
    /// its place and refused, because the affordance's position is how a player learns
    /// it exists.</summary>
    public const string PlayFromAFloor = "Play from a floor";

    /// <summary>Opens this run in the run view, a deeper screen about the same run, so
    /// the game's own disclosure chevron is its glyph.</summary>
    public const string ChooseAnotherFloor = "Choose another floor";

    /// <summary>Why a run cannot be submitted. Not why it cannot be played from: a
    /// console command changes what the run was, and submitting it would be publishing
    /// a run nobody can reproduce.</summary>
    public const string PlateConsoleUsed = "A console command was used, so it can't be submitted.";

    /// <summary>Why the submit row is refused when its host has no sharing service.</summary>
    public const string PlateSubmitComing = "Submitting runs is unavailable";

    /// <summary>Why nothing on a broken recording is offered.</summary>
    public const string PlateContinuityBroken =
        "Part of this run was played while Runmobile wasn't recording.";

    /// <summary>Why nothing is offered while a run is in progress. Play-from is after
    /// the fact, and this is the sentence that says so where a player would ask.</summary>
    public const string PlateDuringARun = "Not while a run is in progress.";

    // ── Shared ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A size on disk as this surface writes one.
    ///
    /// Whole megabytes above a megabyte and whole kilobytes below it: a player reading
    /// "how much of my disk is this" is deciding whether to care, and a second decimal
    /// place is precision nobody acts on.
    /// </summary>
    public static string Size(long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "A size is not negative.");
        const long kilobyte = 1024;
        const long megabyte = kilobyte * 1024;
        return bytes >= megabyte
            ? $"{(bytes / megabyte).ToString(CultureInfo.InvariantCulture)} MB"
            : $"{(bytes / kilobyte).ToString(CultureInfo.InvariantCulture)} KB";
    }
}
