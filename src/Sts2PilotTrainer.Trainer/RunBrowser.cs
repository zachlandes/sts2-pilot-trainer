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

/// <summary>
/// What the browser shows for one tab, derived from the runs a host could find and
/// nothing else.
///
/// The whole of the settled compatibility rule lives here rather than in the drawing:
/// a run this game cannot play is not in <see cref="Groups"/>, is counted in
/// <see cref="NotShown"/>, and has no row anywhere in any state. There is no filter to
/// turn off, because there is no filter - the rule is not a preference and a control
/// for it would imply it was one.
///
/// <para>The list is a projection. Nothing here reads a file, replays anything or
/// decides a verdict; every run arrives with its verdict already established, which is
/// what keeps "why is this run not listed" answerable by looking at one field.</para>
/// </summary>
public sealed record RunBrowser(
    LibraryTab Tab,
    IReadOnlyList<BrowserGroup> Groups,
    int NotShown,
    string? NotShownLabel,
    string NotShownTooltipBody,
    string? Footer,
    string? FooterAction)
{
    /// <summary>
    /// The browser for one tab.
    ///
    /// <paramref name="runs"/> is everything a host could find, listed and hidden
    /// alike: the hidden ones have to arrive here to be counted, which is why this
    /// filters rather than being handed a filtered list.
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
        long? myRunsBytes = null)
    {
        var mine = tab == LibraryTab.MyRuns;
        var inTab = runs.Where(run => (run.Origin == RunOrigin.Mine) == mine).ToList();
        var listed = inTab.Where(run => run.Listed).ToList();
        var hidden = inTab.Count - listed.Count;

        var groups = mine
            ? Group(null, Newest(listed))
            :
            [
                .. Group(LibraryCopy.IncludedGroup, Newest(Of(listed, RunOrigin.Included))),
                .. Group(LibraryCopy.FeaturedGroup, Of(listed, RunOrigin.Featured)),
                .. Group(LibraryCopy.RecentGroup, Newest(Of(listed, RunOrigin.Recent))),
            ];

        return new RunBrowser(
            tab,
            groups,
            hidden,
            hidden > 0 ? LibraryCopy.NotShown(hidden) : null,
            LibraryCopy.NotShownTooltip(thisBuild),
            mine && myRunsBytes is { } bytes
                ? LibraryCopy.MyRunsFooter(inTab.Count, LibraryCopy.Size(bytes))
                : null,
            mine && myRunsBytes is not null ? LibraryCopy.MyRunsFooterAction : null);
    }

    /// <summary>
    /// What a run code answers with.
    ///
    /// The one surface on which a run this game cannot play is described at all, and
    /// the reason it exists: a player who typed a code asked about one particular run,
    /// and answering "no such run" about a run that plainly exists would be the library
    /// lying to them. A player who did not type a code is owed a list of runs that
    /// work, which is why nothing here puts a row in the list.
    ///
    /// <para><b>The design specifies two refusals and this build answers four.</b> Its
    /// two are the build sentence with its sub-line and the multiplayer sentence with
    /// none, and both are here word for word. The two added are
    /// <see cref="LookupOutcome.NoLongerMatches"/> and
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
            string.Equals(candidate.RunId, wanted, StringComparison.OrdinalIgnoreCase));
        if (run is null)
        {
            return new RunLookup(
                LookupOutcome.NotFound, null,
                LibraryCopy.LookupNotFoundTitle, LibraryCopy.LookupNotFound, null);
        }

        // Narrowest first, so each refusal is answered by the thing most nearly true of
        // it. Multiplayer heads the list because it is the answer nothing arriving later
        // changes: a build verdict can turn up tomorrow and a multiplayer run stays a
        // multiplayer run. Then the two same-build answers, both of which the build
        // sentence at the end would report by naming one build twice.
        if (run.Multiplayer == true)
        {
            return new RunLookup(
                LookupOutcome.Multiplayer, run,
                LibraryCopy.LookupRefusedTitle, LibraryCopy.LookupRefusedMultiplayer, null);
        }

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

/// <summary>What a run code found. Five refusals and one hit, because a player who
/// typed a code is owed which of them it was.</summary>
public enum LookupOutcome
{
    Found,

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

    Multiplayer,
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
