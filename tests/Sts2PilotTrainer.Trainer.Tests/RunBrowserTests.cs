namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The list, and the one rule that decides what is in it.
///
/// Most of what is pinned here is the settled hidden rule, from both directions: a run
/// this game cannot play is not in the list in any state - not greyed, not behind a
/// tickbox, not anywhere - and it is still counted, still described by its code, and
/// still the same run afterwards. The tests are written that way on purpose. A future
/// change that "helpfully" showed an incompatible run disabled would pass a test that
/// only checked the count.
/// </summary>
public sealed class RunBrowserTests
{
    private const string Build = "v0.111.0";

    private static LibraryRun Run(
        string id,
        RunOrigin origin = RunOrigin.Recent,
        RunVerdict verdict = RunVerdict.Passed,
        bool? multiplayer = null,
        string build = Build,
        DateTimeOffset? recorded = null) =>
        new(
            id, origin, "NaveGreed", "CHARACTER.IRONCLAD", 10, build,
            Fights: [1, 2, 3, 4, 5, 6], Outcome: "won", multiplayer, verdict,
            FightsPlayed: [], recorded);

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
    /// Three ways a run can be unplayable and one outcome: it is not in the list, in
    /// any state, and the numeral under the list is what says so.
    /// </summary>
    [Theory]
    [InlineData(RunVerdict.Absent, null)]
    [InlineData(RunVerdict.Failed, null)]
    [InlineData(RunVerdict.Passed, true)]
    public void ARunThisGameCannotPlayHasNoRowAtAll(RunVerdict verdict, bool? multiplayer)
    {
        var browser = RunBrowser.For(
            LibraryTab.Community,
            [Run("shown"), Run("hidden", verdict: verdict, multiplayer: multiplayer)],
            Build);

        Assert.Equal(["shown"], Listed(browser).Select(run => run.RunId));
        Assert.Equal(1, browser.NotShown);
        Assert.Equal("1 not shown", browser.NotShownLabel);
        Assert.Contains(Build, browser.NotShownTooltipBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// A recording that says nothing about being multiplayer is not reported as
    /// single-player: the rule hides what was established and never what nobody asked.
    /// </summary>
    [Fact]
    public void ARecordingThatSaysNothingAboutMultiplayerIsStillListed()
    {
        var browser = RunBrowser.For(LibraryTab.Community, [Run("quiet", multiplayer: null)], Build);

        Assert.Single(Listed(browser));
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
        Assert.Equal("1 runs, 3 MB on this computer", browser.Footer);
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

        Assert.Equal("2 runs, 3 MB on this computer", browser.Footer);
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
    /// A multiplayer run gets no "it may come back" note, because nothing arriving
    /// later turns one into a single-player run. Answered before the build question so
    /// a multiplayer run recorded on another build still reads as multiplayer.
    /// </summary>
    [Fact]
    public void ACodeForAMultiplayerRunSaysWhatWillNeverChange()
    {
        var answer = RunBrowser.Lookup(
            "party",
            [Run("party", verdict: RunVerdict.Absent, multiplayer: true, build: "v0.110.0")],
            Build);

        Assert.Equal(LookupOutcome.Multiplayer, answer.Outcome);
        Assert.Equal(LibraryCopy.LookupRefusedMultiplayer, answer.Body);
        Assert.Null(answer.Note);
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
        Assert.NotEqual(LibraryCopy.LookupRefusedMultiplayer, answer.Body);
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
        Assert.NotEqual(LibraryCopy.LookupRefusedMultiplayer, answer.Body);
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

    /// <summary>A multiplayer run stays a multiplayer run whatever its verdict says,
    /// so that answer is reached first.</summary>
    [Fact]
    public void AMultiplayerRunIsAnsweredAsOneEvenWhenItsVerdictAlsoFailed()
    {
        var answer = RunBrowser.Lookup(
            "party", [Run("party", verdict: RunVerdict.Failed, multiplayer: true)], Build);

        Assert.Equal(LookupOutcome.Multiplayer, answer.Outcome);
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
    /// The pips under a run's fight count are how many of its fights this player has
    /// stood in. Counted against the ordinals the recording proves rather than against
    /// how many there are, so neither a stale ordinal from a longer recording nor one
    /// the recording spent on a fight it stopped inside puts a pip under a fight
    /// nothing offers.
    /// </summary>
    [Fact]
    public void ThePipsCountOnlyFightsThisRecordingHas()
    {
        var run = Run("a") with { Fights = [1, 2, 4], FightsPlayed = [1, 3, 99] };

        Assert.Equal(1, run.PlayedCount);
        Assert.Equal(3, run.FightCount);
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
