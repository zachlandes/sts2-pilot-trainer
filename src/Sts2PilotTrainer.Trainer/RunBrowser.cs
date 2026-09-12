namespace Sts2PilotTrainer.Trainer;

/// <summary>Which list the browser is showing. Two, and there is no third: a run is
/// either somebody's or the player's own.</summary>
public enum LibraryTab
{
    Community,
    MyRuns,
}

/// <summary>One headed run set in the list. A heading of null is the My runs list,
/// which is one set and does not head itself.</summary>
public sealed record BrowserGroup(string? Heading, IReadOnlyList<LibraryRun> Runs);

/// <summary>Which offer a row of the pane's plate is.</summary>
public enum PaneRowKind
{
    /// <summary>Leads to the submit flow, which is somewhere else.</summary>
    Submit,

    /// <summary>Removes one of the recorder's own runs, through the game's confirm. The
    /// per-run counterpart of the settings row's Remove all my runs.</summary>
    Remove,

}

/// <summary>One row of the flat plate under the browser's pane.</summary>
/// <param name="Confirms">Whether pressing it opens the game's own confirm first.
/// Removing is the one thing here that cannot be undone.</param>
public sealed record PaneRow(PaneRowKind Kind, string Label, bool Enabled, bool Confirms = false);

/// <summary>
/// The selected run as the browser's right-hand pane shows it: who it is, what it
/// carried, how far it went, and the one way into it.
///
/// It is the run-history screen's own language - identity, relics, the deck as card
/// tiles, the run strip - read out of the recording rather than out of a save. Nothing
/// here is computed: every value is a field a checkpoint carries or a boundary the
/// recording proves.
/// </summary>
/// <param name="Verdict">Whether this build has a passing verdict for the run.</param>
/// <param name="PlateReason">Why a plate row is refused, or null where none is refused
/// for a reason about this run: the same sentence the run-history plate says under its
/// own rows, so the two surfaces refuse in one voice.</param>
/// <param name="Open">The ribbon that opens the run view. The one way there, in either
/// tab.</param>
/// <param name="OpenEnabled">Whether that ribbon can be pressed.</param>
public sealed record RunPane(
    LibraryRun Run,
    IReadOnlyList<RunRelic> Relics,
    IReadOnlyList<DeckTile>? Deck,
    int? DeckCount,
    IReadOnlyList<RunStripCell> Strip,
    string Verdict,
    string Open,
    bool OpenEnabled,
    IReadOnlyList<PaneRow> Plate,
    string? PlateReason = null);

/// <summary>
/// What the browser shows for one tab, derived from the runs a host could find and
/// nothing else.
///
/// The whole of the settled compatibility rule lives here rather than in the drawing.
/// A run this game cannot play is hidden by default and counted in <see cref="NotShown"/>.
/// Turning the visible filter off reveals it as a disabled row, and an exact code does
/// the same before selecting that run.
///
/// <para>The list is a projection. Nothing here reads a file, replays anything or
/// decides a verdict; every run arrives with its verdict already established, which is
/// what keeps "why is this run not listed" answerable by looking at one field.</para>
/// </summary>
public sealed record RunBrowser(
    LibraryTab Tab,
    IReadOnlyList<BrowserGroup> Groups,
    RunPane? Pane,
    int NotShown,
    string? NotShownLabel,
    string NotShownTooltipBody,
    string? Footer,
    string? FooterAction,
    bool CompatibleOnly = true,
    string? SelectedEntryId = null)
{
    /// <summary>
    /// The browser for one tab, with one run selected.
    ///
    /// <paramref name="runs"/> is everything a host could find, listed and hidden
    /// alike: the hidden ones have to arrive here to be counted, which is why this
    /// filters rather than being handed a filtered list.
    ///
    /// <paramref name="selected"/> null selects the first run the list holds, so the
    /// pane is never empty on a list that is not.
    /// </summary>
    /// <param name="myRunsBytes">What the player's own runs occupy on this computer,
    /// or null when nobody read it. The footer is drawn only with a reading behind it,
    /// because a size nobody measured is not a size to put on screen.
    ///
    /// The count beside it is every finished run of the player's own this build can
    /// read, listed or not - deliberately not the count of rows, because a player whose
    /// game has just updated has nothing listed and twelve megabytes on disk, and "0
    /// runs, 12 MB" would be describing two different things in one sentence. The runs
    /// that are listed are the rows themselves, and what is not listed is the numeral
    /// above.
    ///
    /// The two halves are close together rather than identical, and the difference is a
    /// run still being played: its journal is sized and its manifest does not exist
    /// yet, so it is in the megabytes and not in the count. The size covers everything
    /// the settings purge would remove.</param>
    public static RunBrowser For(
        LibraryTab tab,
        IReadOnlyList<LibraryRun> runs,
        string thisBuild,
        long? myRunsBytes = null,
        bool compatibleOnly = true,
        string? selectedEntryId = null,
        bool submitAvailable = false)
    {
        var mine = tab == LibraryTab.MyRuns;
        var inTab = runs.Where(run => (run.Origin == RunOrigin.Mine) == mine).ToList();
        var selected = selectedEntryId is null
            ? null
            : inTab.FirstOrDefault(run =>
                string.Equals(run.EntryId, selectedEntryId, StringComparison.Ordinal));
        if (selected is { Verdict: RunVerdict.Absent, Multiplayer: not true }) compatibleOnly = false;

        var eligible = inTab.Where(run =>
            run.Multiplayer != true && run.Verdict is RunVerdict.Passed or RunVerdict.Absent).ToList();
        var visible = compatibleOnly ? eligible.Where(run => run.Listed).ToList() : eligible;
        var hidden = inTab.Count - visible.Count;

        var groups = mine
            ? Group(null, Newest(visible))
            :
            [
                .. Group(LibraryCopy.IncludedGroup, Newest(Of(visible, RunOrigin.Included))),
                .. Group(LibraryCopy.FeaturedGroup, Of(visible, RunOrigin.Featured)),
                .. Group(LibraryCopy.RecentGroup, Newest(Of(visible, RunOrigin.Recent))),
            ];

        var shown = groups.SelectMany(group => group.Runs).ToList();
        var run = selected is null
            ? shown.FirstOrDefault()
            : shown.FirstOrDefault(candidate =>
                string.Equals(candidate.EntryId, selected.EntryId, StringComparison.Ordinal))
              ?? shown.FirstOrDefault();

        return new RunBrowser(
            tab,
            groups,
            run is null
                ? null
                : PaneFor(run, thisBuild, submitAvailable),
            hidden,
            hidden > 0 ? LibraryCopy.NotShown(hidden) : null,
            LibraryCopy.NotShownTooltip(thisBuild),
            mine && myRunsBytes is { } bytes
                ? LibraryCopy.MyRunsFooter(inTab.Count, LibraryCopy.Size(bytes))
                : null,
            mine && myRunsBytes is not null ? LibraryCopy.MyRunsFooterAction : null,
            compatibleOnly,
            selected?.EntryId);
    }

    /// <summary>
    /// The pane for one run, and the plate under it.
    ///
    /// The plate's rows are the tab's and the run's origin, not a preference: your own
    /// run offers Submit and Remove this run, and somebody else's offers the save row
    /// where that shape is built. Removing goes through the game's confirm, because it
    /// is the one thing on this surface that cannot be undone.
    /// </summary>
    private static RunPane PaneFor(
        LibraryRun run, string thisBuild, bool submitAvailable)
    {
        var plate = new List<PaneRow>();
        var mine = run.Origin == RunOrigin.Mine;
        if (mine)
        {
            plate.Add(new PaneRow(
                PaneRowKind.Submit, LibraryCopy.SubmitThisRun, submitAvailable && !run.Rewound));
            plate.Add(new PaneRow(
                PaneRowKind.Remove, LibraryCopy.RemoveThisRun, Enabled: true, Confirms: true));
        }

        return new RunPane(
            run,
            RelicStrip.ForPane(run.Relics),
            run.Deck,
            run.DeckCount,
            [
                // The pane's strip shows the run rather than a place in it: nothing is
                // selected here, because selecting a position is the run view's job and
                // this pane's one way forward is the Open the run ribbon. The last
                // floor this player loaded is ticked, which is the same fact the list's
                // own column names.
                .. run.Positions.Select(position => new RunStripCell(
                    position.Floor,
                    position.Kind,
                    Played: run.LastFloorReplayed == position.Floor,
                    Selected: false,
                    position.Playable,
                    position.Bookmarked)),
            ],
            run.Listed
                ? LibraryCopy.WorksWithYourVersion(thisBuild)
                : LibraryCopy.LookupRefusedBuild(run.RecordedBuild, thisBuild),
            LibraryCopy.OpenTheRun,
            run.Listed,
            plate,
            mine && run.Rewound ? LibraryCopy.PlateRewound : null);
    }

    /// <summary>
    /// What a run code answers with.
    ///
    /// A player who typed a code asked about one particular run, and answering "no such
    /// run" about a run that plainly exists would be the library lying to them.
    /// An incompatible result can be selected as a disabled row with its refusal intact.
    ///
    /// <para><b>The design specifies one refusal and this build answers three.</b> Its
    /// one is the build sentence with its sub-line, and it is here word for word. The
    /// two added are <see cref="LookupOutcome.NoLongerMatches"/> and
    /// <see cref="LookupOutcome.CouldNotJudge"/>, and neither elaborates on the design -
    /// each exists to stop a sentence that would have been false. A run recorded on this
    /// very build that failed its preflight, or that this game could not be read to
    /// judge, is not a build mismatch: the specified body would print one build twice
    /// ("recorded on v0.111.0 and your game is v0.111.0") under a sub-line promising a
    /// verdict that already exists and already failed. Adding a refusal here means a
    /// player could be told something untrue without one; it is not a place to elaborate.
    /// </para>
    ///
    /// <para><b>The code is the one string here a person types.</b> Every other run-id
    /// comparison in the library is <c>Ordinal</c>, because every other one is a string a
    /// program passed. This one is trimmed and matched without case, so a code somebody
    /// was sent is not defeated by a trailing space or by how they typed it. What leaves
    /// this method is the run's canonical id, so the exact-match readers downstream still
    /// get the exact string.</para>
    /// </summary>
    public static RunLookup Lookup(string? code, IReadOnlyList<LibraryRun> runs, string thisBuild)
    {
        var wanted = code?.Trim();
        if (string.IsNullOrEmpty(wanted))
        {
            return new RunLookup(
                LookupOutcome.NotFound, null,
                LibraryCopy.LookupNotFoundTitle, LibraryCopy.LookupNotFound, null);
        }

        var run = runs.FirstOrDefault(candidate =>
            string.Equals(candidate.EntryId, wanted, StringComparison.OrdinalIgnoreCase));
        if (run is null)
        {
            return new RunLookup(
                LookupOutcome.NotFound, null,
                LibraryCopy.LookupNotFoundTitle, LibraryCopy.LookupNotFound, null);
        }

        // Multiplayer stays refused regardless of a later compatibility verdict.
        if (run.Multiplayer == true)
        {
            return new RunLookup(
                LookupOutcome.Multiplayer, run,
                LibraryCopy.LookupRefusedTitle, LibraryCopy.LookupRefusedMultiplayer, null);
        }

        // The two same-build answers come before the build sentence, which would
        // otherwise report either of them by naming one build twice.
        if (run.Verdict == RunVerdict.Unjudged)
        {
            return new RunLookup(
                LookupOutcome.CouldNotJudge, run,
                LibraryCopy.LookupRefusedTitle,
                LibraryCopy.LookupRefusedUnjudged,
                LibraryCopy.LookupRefusedUnjudgedNote);
        }

        if (run.Verdict == RunVerdict.Failed)
        {
            return new RunLookup(
                LookupOutcome.NoLongerMatches, run,
                LibraryCopy.LookupRefusedTitle,
                LibraryCopy.LookupRefusedNoLongerMatches,
                LibraryCopy.LookupRefusedNoLongerMatchesNote);
        }

        if (!run.Listed)
        {
            return new RunLookup(
                LookupOutcome.IncompatibleBuild, run,
                LibraryCopy.LookupRefusedTitle,
                LibraryCopy.LookupRefusedBuild(run.RecordedBuild, thisBuild),
                LibraryCopy.LookupRefusedBuildNote(thisBuild));
        }

        return new RunLookup(LookupOutcome.Found, run, string.Empty, string.Empty, null);
    }

    private static IReadOnlyList<BrowserGroup> Group(string? heading, IReadOnlyList<LibraryRun> runs) =>
        runs.Count == 0 ? [] : [new BrowserGroup(heading, runs)];

    /// <summary>The runs of one origin, in the order they arrived. Featured is the one
    /// group drawn straight from this: the curator chose that order and re-sorting it
    /// would be this surface overruling them. Every other group is newest first.</summary>
    private static IReadOnlyList<LibraryRun> Of(IEnumerable<LibraryRun> runs, RunOrigin origin) =>
        [.. runs.Where(run => run.Origin == origin)];

    /// <summary>Newest first, with a run whose time nobody read sorting last rather
    /// than being given one.</summary>
    private static IReadOnlyList<LibraryRun> Newest(IEnumerable<LibraryRun> runs) =>
    [
        .. runs
            .OrderByDescending(run => run.Recorded is not null)
            .ThenByDescending(run => run.Recorded ?? DateTimeOffset.MinValue)
            .ThenBy(run => run.RunId, StringComparer.Ordinal),
    ];
}

/// <summary>What a run code found. Four refusals and one hit, because a player who
/// typed a code is owed which of them it was.</summary>
public enum LookupOutcome
{
    Found,

    /// <summary>An established multiplayer recording cannot be played from.</summary>
    Multiplayer,

    /// <summary>Recorded on a build this game is not, and waiting on a verdict for
    /// this one.</summary>
    IncompatibleBuild,

    /// <summary>Recorded on this very build, and this game no longer matches what it
    /// was recorded under. A verdict exists for it and it failed, which is a different
    /// fact from there being none.</summary>
    NoLongerMatches,

    /// <summary>Recorded on this very build, and this game could not be read to say
    /// whether it plays. A verdict nobody could reach, which is a third fact again.
    /// </summary>
    CouldNotJudge,

    NotFound,
}

/// <summary>
/// The answer to a run code, as the popup reads it.
///
/// <see cref="Run"/> is filled in even for a refusal, because the refusal is precisely
/// the claim that the run exists - a popup saying so with nothing behind it would be a
/// sentence nobody could check.
/// </summary>
public sealed record RunLookup(
    LookupOutcome Outcome, LibraryRun? Run, string Title, string Body, string? Note)
{
    public string Back => LibraryCopy.Back;

    /// <summary>Whether this answer is a popup rather than a selection in the
    /// list.</summary>
    public bool Refused => Outcome != LookupOutcome.Found;
}
