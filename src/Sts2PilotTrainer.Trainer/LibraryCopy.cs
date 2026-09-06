using System.Globalization;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Every fixed word the run library shows a player, in one place.
///
/// The library's counterpart of <see cref="TrainerCopy"/>, and separate from it for
/// the reason that file gives for existing at all: it holds what the Combat Trainer
/// says, and the Combat Trainer is one module of three. Splitting them keeps "what
/// does this feature say" answerable by reading one file, and keeps a change to the
/// trainer's wording from touching the library's.
///
/// Two rules hold over everything here, and both are settled rather than stylistic.
///
/// <para><b>One entering verb.</b> A player plays <em>from</em> a run: "Play from this
/// fight", "Play from floor 6", "Continue: play from fight 4". No other verb enters a
/// recording anywhere on this surface, because two of them would read as two different
/// things happening.</para>
///
/// <para><b>The run noun.</b> What a player has is runs - Community, My runs, "{n}
/// runs". "Recording", "manifest" and "journal" are this project's internal words for
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
    /// would read as a second one of those. Nothing else on this surface names the
    /// mod, so the card is where a player learns which mod put it there.
    /// </summary>
    public const string CompendiumCard = "Runmobile";

    // ── The browser ────────────────────────────────────────────────────────

    /// <summary>Runs anybody made. The tab a player opens on.</summary>
    public const string CommunityTab = "Community";

    /// <summary>Runs of the player's own, which the recorder wrote.</summary>
    public const string MyRunsTab = "My runs";

    /// <summary>The recordings that travel inside the mod, present with no network
    /// and no index.</summary>
    public const string IncludedGroup = "Included with Runmobile";

    /// <summary>Curated runs from the index, in the curator's order.</summary>
    public const string FeaturedGroup = "Featured";

    /// <summary>Everything else from the index, newest first.</summary>
    public const string RecentGroup = "Recent";

    /// <summary>
    /// The one trace an incompatible run leaves: a count, under the list.
    ///
    /// Settled and not a filter. There is no tickbox for it and no greyed row, so a
    /// run this game cannot play is simply not in the list - and this numeral is what
    /// stops that being a silent disappearance.
    /// </summary>
    public static string NotShown(int count) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} not shown";

    /// <inheritdoc cref="NotShown"/>
    public static string NotShownTooltip(string thisBuild) =>
        "Recorded on a build your game can't play, or as a multiplayer run. They come back when a verdict " +
        $"for {thisBuild} arrives; a run code still finds one.";

    /// <summary>
    /// What the run-code field says before anything is typed in it.
    ///
    /// The code is the one way to ask about a run that is not in the list, which is
    /// what makes the hidden rule bearable: a run this game cannot play is not shown
    /// and is still findable by somebody who was sent it.
    /// </summary>
    public const string RunCodeField = "Run code";

    /// <summary>Leads to the submit flow, which is somewhere else.</summary>
    public const string SubmitThisRun = "Submit this run";

    /// <summary>What the player's own runs occupy, under the My runs list.</summary>
    public static string MyRunsFooter(int runs, string size) =>
        $"{runs.ToString(CultureInfo.InvariantCulture)} runs, {size} on this computer";

    /// <summary>Where the count and the size are acted on. The footer carries no
    /// control of its own: removing runs is a setting, and two places to do it would
    /// be two places to get it wrong.</summary>
    public const string MyRunsFooterAction = "Keep or remove them in Settings";

    // ── Looking a run up by its code ───────────────────────────────────────

    /// <summary>
    /// What a run code answers with when it names a run this game cannot play.
    ///
    /// The one place on the surface where an incompatible run is described at all. It
    /// is a popup rather than a row because the list never holds one: a player who
    /// typed a code asked about a specific run and is owed an answer, and a player who
    /// did not is owed a list of runs that work.
    /// </summary>
    public const string LookupRefusedTitle = "Not playable on your game";

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

    /// <summary>The one refusal with no note: nothing arriving later makes a
    /// multiplayer run into a single-player one.</summary>
    public const string LookupRefusedMultiplayer =
        "This run exists. It is a multiplayer run, and Runmobile plays single-player runs.";

    /// <summary>What a code that names nothing answers with.</summary>
    public const string LookupNotFoundTitle = "No run with that code";

    /// <inheritdoc cref="LookupNotFoundTitle"/>
    public const string LookupNotFound =
        "Nothing here is that run. Check the code, or open Community and pick a run from the list.";

    /// <summary>Leaves any of the lookup's answers.</summary>
    public const string Back = "Back";

    // ── The run view ───────────────────────────────────────────────────────

    /// <summary>Stands the player at the recorded start of the selected fight.</summary>
    public const string PlayFromThisFight = "Play from this fight";

    /// <inheritdoc cref="PlayFromThisFight"/>
    public const string PlayFromThisFightNote = "from the fight's recorded start";

    /// <summary>Stands the player at the selected floor's first screen, from where the
    /// run is theirs.</summary>
    public const string PlayFromThisFloor = "Play from this floor";

    /// <inheritdoc cref="PlayFromThisFloor"/>
    public const string PlayFromThisFloorNote = "from the floor's first screen";

    /// <summary>The one row that does not depend on what is selected: the next fight
    /// this player has not played from.</summary>
    public static string ContinueAtFight(int fight) =>
        $"Continue: play from fight {fight.ToString(CultureInfo.InvariantCulture)}";

    /// <inheritdoc cref="ContinueAtFight"/>
    public const string ContinueNote = "the next fight not yet played";

    /// <summary>
    /// Moves the run view to another floor.
    ///
    /// Not in the accepted design, which selects a floor on the run strip. This screen
    /// is drawn in the game's own popup and has no strip in it, so the same choice is
    /// offered as a row; when the strip is drawn this row goes and nothing else about
    /// the view changes.
    /// </summary>
    public const string ChooseAFloor = "Choose a floor";

    /// <inheritdoc cref="ChooseAFloor"/>
    public const string ChooseAFloorNote = "pick where in the run to stand";

    /// <summary>
    /// Walks the recording from its beginning, showing every decision on the game's own
    /// screens, and comes to rest at the first fight.
    ///
    /// The same destination as "Play from this fight" on fight 1's floor, reached two
    /// ways: one walks the run and one goes straight in. The note says where it ends
    /// because a row promising run start and landing mid-fight-1 without saying so was
    /// the row describing somewhere it does not go.
    /// </summary>
    public const string StartTheRunOver = "Start the run over";

    /// <inheritdoc cref="StartTheRunOver"/>
    public const string StartTheRunOverNote = "from run start, every choice shown, ending at fight 1";

    /// <summary>One floor on the chooser, and what the recording proves about
    /// it.</summary>
    public static string FloorRow(int floor, int? fight) =>
        fight is { } ordinal
            ? $"Floor {floor.ToString(CultureInfo.InvariantCulture)} · fight " +
              ordinal.ToString(CultureInfo.InvariantCulture)
            : $"Floor {floor.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Why a floor offers no fight to play from.</summary>
    public const string NoFightOnThisFloor = "no fight starts on this floor";

    /// <summary>Why a fight the recording stops inside offers nothing: there is no
    /// finished recorded line for a player's own to be set beside.</summary>
    public const string FightNotFinished = "the recording does not reach the end of this fight";

    /// <summary>Why the run's first floor is not a floor to play from: it is where
    /// starting the run over already puts you.</summary>
    public const string RunStartsHere = "the run starts here";

    /// <summary>Said on the floor rows, and only there. From a floor entry nothing is
    /// compared, and a player who expected a comparison would be waiting for one that
    /// never comes.</summary>
    public const string FloorIsYours = "The run is yours from here.";

    // ── The run-history plate ──────────────────────────────────────────────

    /// <summary>A recorded run, whole, with nothing standing in its way.</summary>
    public const string PlateRecorded = "Recorded, not saved, not counted";

    /// <summary>A recorded run the recorder lost sight of part way through.</summary>
    public const string PlateRecordedWithAGap = "Recorded, with a gap";

    /// <summary>A recorded run this game cannot play, described rather than
    /// entered.</summary>
    public static string PlateRecordedOn(string build) => $"Recorded on {build}";

    /// <summary>A run the game has history for and the recorder never watched, because
    /// Runmobile does not attach to a multiplayer game.</summary>
    public const string PlateMultiplayer = "Multiplayer run";

    /// <summary>A recorded run reached at a moment when nothing may be played
    /// from.</summary>
    public const string PlateRecordedPlain = "Recorded";

    /// <summary>The plate's fight row, for the run's own last fight.</summary>
    public static string PlayFromFight(int fight) =>
        $"Play from fight {fight.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The plate's floor row, for the last floor the run reached.</summary>
    public static string PlayFromFloor(int floor) =>
        $"Play from floor {floor.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The fight row when the recording proves no fight to name. Kept in its
    /// place and refused, because the affordance's position is how a player learns it
    /// exists.</summary>
    public const string PlayFromAFight = "Play from a fight";

    /// <inheritdoc cref="PlayFromAFight"/>
    public const string PlayFromAFloor = "Play from a floor";

    /// <summary>Why a run cannot be submitted. Not why it cannot be played from: a
    /// console command changes what the run was, and submitting it would be publishing
    /// a run nobody can reproduce.</summary>
    public const string PlateConsoleUsed = "A console command was used, so it can't be submitted.";

    /// <summary>Why nothing on a broken recording is offered.</summary>
    public const string PlateContinuityBroken = "The game reloaded past a point already recorded.";

    /// <summary>Why a recording made on another build is described and not
    /// entered.</summary>
    public static string PlateOtherBuild(string thisBuild) => $"Your game is {thisBuild}.";

    /// <summary>Why a multiplayer run has no rows.</summary>
    public const string PlateMultiplayerReason = "Runmobile plays single-player runs.";

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
