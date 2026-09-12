namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The list, and the one rule that decides what is in it.
///
/// Most of what is pinned here is the settled hidden rule, from both directions.
/// A run this game cannot play is hidden by default, remains counted, and appears only
/// as a disabled row when the visible filter or an exact code reveals it.
/// </summary>
public sealed class RunBrowserTests
{
    private const string Build = "v0.111.0";

    private static LibraryRun Run(
        string id,
        RunOrigin origin = RunOrigin.Recent,
        RunVerdict verdict = RunVerdict.Passed,
        string build = Build,
        DateTimeOffset? recorded = null,
        int? lastFloorReplayed = null,
        int? actReached = null,
        bool rewound = false) =>
        new LibraryRun(
            id, origin, "NaveGreed", "CHARACTER.IRONCLAD", 10, build,
            Fights: [1, 2, 3, 4, 5, 6], Floors: [2, 3, 4, 5, 6, 7], Outcome: "won", verdict,
            Relics: [], DeckCount: 11, lastFloorReplayed,
            Positions: [], Deck: null, Recorded: recorded)
        {
            ActReached = actReached,
            Rewound = rewound,
        };

    /// <summary>Every run actually drawn, in the order the groups draw them.</summary>
    private static IReadOnlyList<LibraryRun> Listed(RunBrowser browser) =>
        [.. browser.Groups.SelectMany(group => group.Runs)];

    [Fact]
    public void EveryRunWithAPassingVerdictIsInTheList()
    {
        var browser = RunBrowser.For(LibraryTab.Community, [Run("a"), Run("b")], Build);

        Assert.Equal(["a", "b"], Listed(browser).Select(run => run.RunId).Order());
        Assert.Equal(0, browser.NotShown);
        Assert.Null(browser.NotShownLabel);
    }

    /// <summary>
    /// Two ways a run can be unplayable and one default outcome: it is not in the list,
    /// and the numeral under the list is what says so.
    /// </summary>
    [Theory]
    [InlineData(RunVerdict.Absent)]
    [InlineData(RunVerdict.Failed)]
    public void ARunThisGameCannotPlayHasNoRowAtAll(RunVerdict verdict)
    {
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [Run("shown"), Run("hidden", verdict: verdict)],
            Build);

        Assert.Equal(["shown"], Listed(browser).Select(run => run.RunId));
        Assert.Equal(1, browser.NotShown);
        Assert.Equal("1 not shown", browser.NotShownLabel);
        Assert.Contains(Build, browser.NotShownTooltipBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole of why a run is hidden, and the one thing the tooltip may name is a
    /// build. The recorder attaches to single-player runs only, so no other kind of run
    /// ever reaches a list to be hidden from it.
    /// </summary>
    [Fact]
    public void TheHiddenCountsTooltipNamesBuildsAndNothingElse()
    {
        var browser = RunBrowser.For(
            LibraryTab.Community, [Run("hidden", verdict: RunVerdict.Absent)], Build);

        Assert.Contains(Build, browser.NotShownTooltipBody, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "multiplayer", browser.NotShownTooltipBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTwoTabsHoldDifferentRuns()
    {
        IReadOnlyList<LibraryRun> runs = [Run("theirs"), Run("mine", RunOrigin.Mine)];

        Assert.Equal(
            ["theirs"],
            Listed(RunBrowser.For(LibraryTab.Community, runs, Build)).Select(run => run.RunId));
        Assert.Equal(
            ["mine"],
            Listed(RunBrowser.For(LibraryTab.MyRuns, runs, Build)).Select(run => run.RunId));
    }

    [Fact]
    public void CommunityIsGroupedByWhereARunCameFromAndAnEmptyGroupIsNotDrawn()
    {
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [Run("shipped", RunOrigin.Included), Run("recent", RunOrigin.Recent)],
            Build);

        Assert.Equal(
            [LibraryCopy.IncludedGroup, LibraryCopy.RecentGroup],
            browser.Groups.Select(group => group.Heading));
    }

    /// <summary>My runs is one set and does not head itself.</summary>
    [Fact]
    public void MyRunsIsOneUnheadedList()
    {
        var browser = RunBrowser.For(
            LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine)], Build, myRunsBytes: 3 * 1024 * 1024);

        var group = Assert.Single(browser.Groups);
        Assert.Null(group.Heading);
        Assert.Equal("1 runs · 3 MB on this computer", browser.Footer);
        Assert.Equal(LibraryCopy.MyRunsFooterAction, browser.FooterAction);
    }

    /// <summary>
    /// The footer's two halves are one set: everything stored on this computer, which
    /// is what the size covers and what the settings purge would remove. Counting the
    /// rows instead would have a player whose game has just updated reading "0 runs,
    /// 3 MB on this computer" - two different things in one sentence.
    /// </summary>
    [Fact]
    public void TheFooterCountsEveryStoredRunAndNotJustTheRowsDrawn()
    {
        var browser = RunBrowser.For(
            LibraryTab.MyRuns,
            [
                Run("mine", RunOrigin.Mine),
                Run("hidden", RunOrigin.Mine, verdict: RunVerdict.Absent),
            ],
            Build,
            myRunsBytes: 3 * 1024 * 1024);

        Assert.Equal("2 runs · 3 MB on this computer", browser.Footer);
        Assert.Equal(["mine"], Listed(browser).Select(run => run.RunId));
        Assert.Equal("1 not shown", browser.NotShownLabel);
    }

    /// <summary>A size nobody measured is not a size to put on screen.</summary>
    [Fact]
    public void TheFooterIsAbsentWithoutAReadingBehindIt()
    {
        var browser = RunBrowser.For(LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine)], Build);

        Assert.Null(browser.Footer);
        Assert.Null(browser.FooterAction);
    }

    [Fact]
    public void RecentAndMyRunsAreNewestFirstAndAnUndatedRunSortsLast()
    {
        var older = Run("older", recorded: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = Run("newer", recorded: new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));
        var undated = Run("undated");

        var browser = RunBrowser.For(LibraryTab.Community, [older, undated, newer], Build);

        Assert.Equal(["newer", "older", "undated"], Listed(browser).Select(run => run.RunId));
    }

    /// <summary>The shipped set and the curated set arrive in an order somebody chose,
    /// and this surface does not overrule it.</summary>
    [Fact]
    public void TheShippedAndCuratedGroupsKeepTheOrderTheyArrivedIn()
    {
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [
                Run("second", RunOrigin.Featured,
                    recorded: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                Run("first", RunOrigin.Featured,
                    recorded: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
            ],
            Build);

        Assert.Equal(["second", "first"], Listed(browser).Select(run => run.RunId));
    }

    [Fact]
    public void ACodeFindsARunTheListHolds()
    {
        var answer = RunBrowser.Lookup("wanted", [Run("wanted")], Build);

        Assert.Equal(LookupOutcome.Found, answer.Outcome);
        Assert.False(answer.Refused);
        Assert.Equal("wanted", answer.Run!.RunId);
    }

    /// <summary>
    /// The one place an incompatible run is described. It says the run exists, which is
    /// the whole point: the list could not, and a player who typed its code is owed the
    /// truth rather than "no such run".
    /// </summary>
    [Fact]
    public void ACodeForARunThisBuildCannotPlaySaysSoAndSaysItMayComeBack()
    {
        var answer = RunBrowser.Lookup(
            "old", [Run("old", verdict: RunVerdict.Absent, build: "v0.110.0")], Build);

        Assert.Equal(LookupOutcome.IncompatibleBuild, answer.Outcome);
        Assert.Equal(LibraryCopy.LookupRefusedTitle, answer.Title);
        Assert.Contains("v0.110.0", answer.Body, StringComparison.Ordinal);
        Assert.Contains(Build, answer.Body, StringComparison.Ordinal);
        Assert.Contains(Build, answer.Note!, StringComparison.Ordinal);
        Assert.NotNull(answer.Run);
    }

    [Fact]
    public void AnIncompatibleCodeRevealsItsDisabledRowOnTheLaterSortedPage()
    {
        var runs = Enumerable.Range(1, 49)
            .Select(index => Run(
                $"recent-{index:00}",
                recorded: new DateTimeOffset(2026, 9, index % 28 + 1, 0, 0, 0, TimeSpan.Zero)))
            .Append(Run(
                "old",
                verdict: RunVerdict.Absent,
                build: "v0.110.0",
                recorded: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .ToList();

        var answer = RunBrowser.Lookup("old", runs, Build);
        var browser = RunBrowser.For(
            LibraryTab.Community, runs, Build, compatibleOnly: true,
            selectedEntryId: answer.Run!.EntryId);
        var sorted = Listed(browser).ToList();
        var selected = sorted.FindIndex(run => run.EntryId == browser.SelectedEntryId);
        var page = ScreenPage.Containing(sorted.Count, perPage: 8, selected, pinned: 2);

        Assert.False(browser.CompatibleOnly);
        Assert.False(sorted[selected].Listed);
        Assert.True(page.Index > 0);
        Assert.InRange(selected, page.First, page.First + page.Count - 1);
        Assert.Contains("v0.110.0", answer.Body, StringComparison.Ordinal);
        Assert.Contains(Build, answer.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run recorded on this very build that this game can no longer reproduce gets
    /// its own sentence. The build refusal would name one build twice and promise a
    /// verdict that already exists and already failed.
    /// </summary>
    [Fact]
    public void ACodeForARunThisGameNoLongerMatchesSaysThatAndNotTheBuildSentence()
    {
        var answer = RunBrowser.Lookup("moved", [Run("moved", verdict: RunVerdict.Failed)], Build);

        Assert.Equal(LookupOutcome.NoLongerMatches, answer.Outcome);
        Assert.Equal(LibraryCopy.LookupRefusedTitle, answer.Title);
        Assert.Equal(LibraryCopy.LookupRefusedNoLongerMatches, answer.Body);
        Assert.Equal(LibraryCopy.LookupRefusedNoLongerMatchesNote, answer.Note);
        Assert.DoesNotContain(Build, answer.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A verdict nobody could reach is a third fact again: not a verdict that failed,
    /// and not a build this game is not.
    /// </summary>
    [Fact]
    public void ACodeForARunThisGameCouldNotJudgeSaysThatAndNoneOfTheOthers()
    {
        var answer = RunBrowser.Lookup("unread", [Run("unread", verdict: RunVerdict.Unjudged)], Build);

        Assert.Equal(LookupOutcome.CouldNotJudge, answer.Outcome);
        Assert.Equal(LibraryCopy.LookupRefusedTitle, answer.Title);
        Assert.Equal(LibraryCopy.LookupRefusedUnjudged, answer.Body);
        Assert.Equal(LibraryCopy.LookupRefusedUnjudgedNote, answer.Note);
        Assert.DoesNotContain(Build, answer.Body, StringComparison.Ordinal);
        Assert.NotEqual(LibraryCopy.LookupRefusedNoLongerMatches, answer.Body);
    }

    /// <summary>A run this game could not judge is no more in the list than one whose
    /// verdict failed.</summary>
    [Fact]
    public void ARunThisGameCouldNotJudgeIsNotInTheList()
    {
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [Run("shown"), Run("unread", verdict: RunVerdict.Unjudged)],
            Build);

        Assert.Equal(["shown"], Listed(browser).Select(run => run.RunId));
        Assert.Equal(1, browser.NotShown);
    }

    /// <summary>
    /// The intent names one exception to newest-first and it is Featured. The shipped
    /// group is sorted like the rest; the curated one keeps the order it arrived in.
    /// </summary>
    [Fact]
    public void EveryGroupButFeaturedIsNewestFirst()
    {
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [
                Run("shipped-old", RunOrigin.Included, recorded: older),
                Run("shipped-new", RunOrigin.Included, recorded: newer),
                Run("curated-old", RunOrigin.Featured, recorded: older),
                Run("curated-new", RunOrigin.Featured, recorded: newer),
            ],
            Build);

        Assert.Equal(
            ["shipped-new", "shipped-old", "curated-old", "curated-new"],
            Listed(browser).Select(run => run.RunId));
    }

    [Fact]
    public void ACodeForNothingSaysThatRatherThanRefusingARunThatDoesNotExist()
    {
        foreach (var code in new[] { "nobody", "", "   ", null })
        {
            var answer = RunBrowser.Lookup(code, [Run("a")], Build);
            Assert.Equal(LookupOutcome.NotFound, answer.Outcome);
            Assert.Null(answer.Run);
        }
    }

    [Fact]
    public void ACodeIsMatchedWithoutCareForItsCaseOrSurroundingSpace()
    {
        Assert.Equal(LookupOutcome.Found, RunBrowser.Lookup("  WaNtEd ", [Run("wanted")], Build).Outcome);
    }

    /// <summary>
    /// The pane is the selected run, and the first run the list holds is selected when
    /// nothing else is asked for - so the pane is never empty on a list that is not.
    /// </summary>
    [Fact]
    public void ThePaneShowsTheSelectedRunAndTheFirstOneWhenNoneIsAskedFor()
    {
        IReadOnlyList<LibraryRun> runs = [Run("a"), Run("b")];

        Assert.Equal("a", RunBrowser.For(LibraryTab.Community, runs, Build).Pane!.Run.RunId);
        Assert.Equal(
            "b", RunBrowser.For(LibraryTab.Community, runs, Build, selectedEntryId: "b").Pane!.Run.RunId);
    }

    /// <summary>The pane's strip carries the recording's bookmarks in either tab: the
    /// mark is about the run, and the pane reads the same positions the run view
    /// does.</summary>
    [Fact]
    public void ThePaneStripCarriesTheRecordingsBookmarksInEitherTab()
    {
        var positions = new[]
        {
            new RunViewPosition(1, null, FloorKind.Unknown, false, true, -1),
            new RunViewPosition(2, 1, FloorKind.Combat, false, false, 10, Bookmarked: true),
            new RunViewPosition(3, 2, FloorKind.Combat, false, false, 20),
        };
        IReadOnlyList<LibraryRun> runs =
        [
            Run("theirs") with { Positions = positions },
            Run("mine", origin: RunOrigin.Mine) with { Positions = positions },
        ];

        foreach (var tab in new[] { LibraryTab.Community, LibraryTab.MyRuns })
        {
            var strip = RunBrowser.For(tab, runs, Build).Pane!.Strip;
            Assert.Equal([false, true, false], strip.Select(cell => cell.Bookmarked));
        }
    }

    /// <summary>An exact selection reveals an incompatible run without allowing entry.</summary>
    [Fact]
    public void SelectingAnIncompatibleRunDisablesItsOpenRibbon()
    {
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [Run("a"), Run("hidden", verdict: RunVerdict.Absent, build: "v0.110.0")],
            Build,
            selectedEntryId: "hidden");

        Assert.Equal("hidden", browser.Pane!.Run.RunId);
        Assert.False(browser.CompatibleOnly);
        Assert.False(browser.Pane.OpenEnabled);
        Assert.Contains("v0.110.0", browser.Pane.Verdict, StringComparison.Ordinal);
        Assert.Contains(Build, browser.Pane.Verdict, StringComparison.Ordinal);
    }

    /// <summary>An empty list has no pane rather than an empty one.</summary>
    [Fact]
    public void AnEmptyListHasNoPane()
    {
        Assert.Null(RunBrowser.For(LibraryTab.Community, [], Build).Pane);
    }

    /// <summary>The pane's verdict line is the captain's wording, in the eligibility
    /// screen's green, and it is the only thing the pane says about compatibility -
    /// because the pane only ever shows a run this game can play.</summary>
    [Fact]
    public void ThePaneSaysTheRunWorksWithThisVersion()
    {
        var browser = RunBrowser.For(LibraryTab.Community, [Run("a")], Build);

        Assert.Equal($"Works with your version · {Build}", browser.Pane!.Verdict);
        Assert.Equal(LibraryCopy.OpenTheRun, browser.Pane.Open);
    }

    /// <summary>
    /// Your own run's plate offers Submit and Remove this run; somebody else's offers
    /// nothing until the save-run shape is built. Removing is the one thing here that
    /// goes through the game's confirm, because it is the one that cannot be undone.
    /// </summary>
    [Fact]
    public void TheMyRunsPlateOffersSubmitAndRemoveAndTheCommunityOneOffersNothingYet()
    {
        var mine = RunBrowser.For(LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine)], Build);
        var theirs = RunBrowser.For(LibraryTab.Community, [Run("theirs")], Build);

        Assert.Equal(
            [PaneRowKind.Submit, PaneRowKind.Remove],
            mine.Pane!.Plate.Select(row => row.Kind));
        Assert.Equal(LibraryCopy.RemoveThisRun, mine.Pane.Plate[1].Label);
        Assert.True(mine.Pane.Plate[1].Confirms);
        Assert.False(mine.Pane.Plate[0].Confirms);
        Assert.Empty(theirs.Pane!.Plate);
    }

    /// <summary>
    /// The submit row is refused unless the host establishes that sharing is available.
    /// One supplied fact turns it on and nothing else about the plate moves.
    /// </summary>
    [Fact]
    public void TheSubmitRowIsRefusedUntilTheFlowItLeadsToExists()
    {
        var without = RunBrowser.For(LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine)], Build);
        var with = RunBrowser.For(
            LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine)], Build, submitAvailable: true);

        Assert.False(without.Pane!.Plate[0].Enabled);
        Assert.True(with.Pane!.Plate[0].Enabled);
        Assert.Equal(without.Pane.Plate.Count, with.Pane.Plate.Count);
    }

    /// <summary>
    /// A run a reload rewound is played from and never submitted, and the pane says so
    /// under its rows the way the run-history plate does, rather than leaving the
    /// publication gate to refuse it after the form is filled in.
    /// </summary>
    [Fact]
    public void ARewoundRunRefusesTheSubmitRowWithAReasonAndNothingElse()
    {
        var browser = RunBrowser.For(
            LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine, rewound: true)], Build, submitAvailable: true);
        var pane = browser.Pane!;

        Assert.False(pane.Plate[0].Enabled);
        Assert.True(pane.Plate[1].Enabled);
        Assert.True(pane.OpenEnabled);
        Assert.Equal(LibraryCopy.PlateRewound, pane.PlateReason);
        Assert.Null(RunBrowser.For(
            LibraryTab.MyRuns, [Run("mine", RunOrigin.Mine)], Build, submitAvailable: true).Pane!.PlateReason);
    }

    /// <summary>
    /// The Last floor replayed column reads the last floor of this run the player
    /// loaded, and is blank until there is one. Saving a run does not set it, so a run
    /// nobody has stood in says nothing rather than zero.
    /// </summary>
    [Fact]
    public void TheLastFloorReplayedColumnIsBlankUntilTheresOne()
    {
        Assert.Null(Run("cold").LastFloorReplayed);
        Assert.Equal(6, Run("warm", lastFloorReplayed: 6).LastFloorReplayed);
    }

    [Fact]
    public void TheActReachedColumnCarriesTheRecordingDerivedValue()
    {
        var run = Run("act-two", actReached: 2);

        var act = Assert.IsType<int>(run.ActReached);
        Assert.Equal(2, act);
        Assert.Equal("Act 2", LibraryCopy.ActReached(act));
    }

    /// <summary>
    /// The run's own reach and how far this player has replayed are two facts, and the
    /// row draws them in two places. Collapsing them would make a run somebody has
    /// never opened look like one they finished.
    /// </summary>
    [Fact]
    public void HowFarTheRunWentAndHowFarThisPlayerHasAreSeparate()
    {
        var run = Run("a", lastFloorReplayed: 3);

        Assert.Equal(7, run.LastFloor);
        Assert.Equal(3, run.LastFloorReplayed);
    }

    [Fact]
    public void ASizeIsWholeUnitsBecauseNobodyActsOnADecimalPlace()
    {
        Assert.Equal("0 KB", LibraryCopy.Size(0));
        Assert.Equal("512 KB", LibraryCopy.Size(512 * 1024));
        Assert.Equal("2 MB", LibraryCopy.Size((2 * 1024 * 1024) + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryCopy.Size(-1));
    }
}
