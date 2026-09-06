using System.Globalization;
using System.Text;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The browse-and-play-from surface: the list, one run opened, and the way into any
/// place that run's recording proves.
///
/// It is one screen in the sense that matters - one vocabulary, one entering verb, one
/// set of rules about what is shown - drawn as a short sequence of the game's own
/// modals because that is the furniture this mod has. Nothing here decides anything:
/// <c>RunLibrary</c> gathers the runs, <c>RunBrowser</c> and <c>RunView</c> decide
/// what is offered, and this puts the rows on screen and calls the one entry there is.
///
/// <para><b>The list never holds a run this game cannot play.</b> That rule lives in
/// <c>LibraryRun.Listed</c> and reaches here as a shorter list plus a number; there is
/// no control on this screen that turns it off, because it is not a preference. The
/// run-code field is the one way to ask about a run that is not listed, and it answers
/// with the game's own popup rather than by putting a row back.</para>
///
/// <para>What the accepted design draws and this does not: the run strip, the deck
/// tiles, the relic row and the portrait; and the Community list's group headings,
/// which are a summary line in the popup's body rather than headings between the rows,
/// so the rows follow group order without each one saying which group it is in. Those
/// are a scene this mod has no path to build, and what stands in for them is a row per
/// floor and a line of text. The offers, the wording and the rules are the design's
/// exactly, and the furniture follow-up draws them properly.</para>
/// </summary>
internal static class RunBrowserScreen
{
    /// <summary>Opens the library on the tab a player lands on: everybody's runs.</summary>
    internal static void Open() => OpenTab(LibraryTab.Community);

    internal static void OpenTab(LibraryTab tab)
    {
        try
        {
            var build = RunLibrary.ThisBuild();
            var runs = RunLibrary.Runs();
            var browser = RunBrowser.For(
                tab, runs, build, tab == LibraryTab.MyRuns ? RunLibraryStore.MyRunsBytes() : null);

            // Nothing captured here is a sibling assembly's type. A lambda in this
            // assembly becomes a class whose fields are what it captured, and the game
            // enumerates this assembly's types one phase before it can resolve a
            // sibling - so a captured LibraryTab or LibraryRun stops the whole mod
            // loading. ModAssemblyLoadOrderTests is what says so; see
            // docs/in-game-host.md.
            var community = tab == LibraryTab.Community;
            var rows = new List<ScreenRow>
            {
                new(
                    community ? LibraryCopy.MyRunsTab : LibraryCopy.CommunityTab,
                    Enabled: true,
                    () => OpenTab(community ? LibraryTab.MyRuns : LibraryTab.Community)),
            };

            foreach (var group in browser.Groups)
            {
                foreach (var run in group.Runs)
                {
                    var runId = run.RunId;
                    var mine = !community;
                    rows.Add(new ScreenRow(
                        RowLabel(run), Enabled: true, () => OpenRun(runId, fromMyRuns: mine)));
                }
            }

            // This is the screen a player enters on, so its ribbon closes the library
            // rather than going anywhere.
            LibraryScreen.Show(
                LibraryCopy.CompendiumCard,
                Body(browser),
                rows,
                LibraryCopy.Back,
                codeSubmitted: code => Look(code, !community),
                codePlaceholder: LibraryCopy.RunCodeField);
        }
        catch (Exception ex)
        {
            Refuse("could not open the run library", ex);
        }
    }

    /// <summary>
    /// One run, opened at a floor.
    ///
    /// Every row it offers is a boundary the recording proves, and every one of them
    /// goes through the same entry - so a row here can never offer somewhere
    /// <see cref="RecordedFightEntry"/> would refuse to stand a player.
    ///
    /// <paramref name="fromMyRuns"/> is which tab the ribbon goes back to, carried as a
    /// bool rather than a <c>LibraryTab</c>: it ends up in a lambda's captured fields,
    /// and a captured sibling-assembly type stops the whole mod loading. See
    /// docs/in-game-host.md.
    /// </summary>
    internal static void OpenRun(string runId, int? floor = null, bool fromMyRuns = false)
    {
        try
        {
            if (RunLibrary.RecordingFor(runId) is not { } recording)
            {
                Refuse($"'{runId}' is not a run this library holds", null);
                return;
            }

            var view = RunView.For(recording, RunLibraryStore.ReadProgress(), floor);
            var rows = new List<ScreenRow>(EnteringRows(view, runId));

            if (view.Positions.Count > 1)
            {
                var id = runId;
                var mine = fromMyRuns;
                rows.Add(new ScreenRow(
                    LibraryCopy.ChooseAFloor, Enabled: true, () => ChooseFloor(id, floor, mine)));
            }

            var backToTab = fromMyRuns;
            LibraryScreen.Show(
                Title(recording),
                RunBody(recording, view),
                rows,
                LibraryCopy.Back,
                back: () => OpenTab(backToTab ? LibraryTab.MyRuns : LibraryTab.Community));
        }
        catch (Exception ex)
        {
            Refuse($"could not open '{runId}'", ex);
        }
    }

    /// <summary>
    /// The run view's own rows, as the screen presses them.
    ///
    /// Each carries the design's second line as well as its label, because the note is
    /// what tells a player where pressing goes - "the next fight not yet played", "from
    /// run start, every choice shown, ending at fight 1" - and a row that only had its
    /// label would be four offers a player has to guess between.
    ///
    /// Primitives only in what the lambdas capture, for the load-order reason OpenTab
    /// records.
    /// </summary>
    internal static IReadOnlyList<ScreenRow> EnteringRows(RunView view, string runId)
    {
        var rows = new List<ScreenRow>();
        foreach (var row in view.Rows)
        {
            var id = runId;
            var kind = (int)row.Kind;
            var fight = row.Fight;
            var atFloor = row.Floor;
            rows.Add(new ScreenRow(
                row.Label, row.Enabled, () => Enter(id, kind, fight, atFloor), row.Note, row.Reason));
        }

        return rows;
    }

    /// <summary>The strip, as rows: every floor the recording proves, and the fight on
    /// it where there is one. Its ribbon goes back to the run as it was left, at the
    /// floor it was standing on.</summary>
    internal static void ChooseFloor(string runId, int? floor = null, bool fromMyRuns = false)
    {
        try
        {
            if (RunLibrary.RecordingFor(runId) is not { } recording)
            {
                Refuse($"'{runId}' is not a run this library holds", null);
                return;
            }

            var rows = new List<ScreenRow>();
            foreach (var position in RunView.PositionsIn(recording))
            {
                var id = runId;
                var atFloor = position.Floor;
                var mine = fromMyRuns;
                rows.Add(new ScreenRow(
                    LibraryCopy.FloorRow(position.Floor, position.Fight),
                    Enabled: true,
                    () => OpenRun(id, atFloor, mine)));
            }

            var backId = runId;
            var backFloor = floor;
            var backToMyRuns = fromMyRuns;
            LibraryScreen.Show(
                Title(recording),
                LibraryCopy.ChooseAFloorNote,
                rows,
                LibraryCopy.Back,
                back: () => OpenRun(backId, backFloor, backToMyRuns));
        }
        catch (Exception ex)
        {
            Refuse($"could not read the floors of '{runId}'", ex);
        }
    }

    /// <summary>
    /// Answers a run code.
    ///
    /// A code that names a listed run opens it. A code that names a run this game
    /// cannot play says so - which is the only place on this surface such a run is
    /// described at all - and a code that names nothing says that instead of
    /// pretending the run does not exist.
    ///
    /// The browser's own modal comes down first, once, because both exits open another
    /// one and the container holds a single screen - the same rule
    /// <c>LibraryScreen.Press</c> follows for every row, and this is the one way in that
    /// does not go through a row. Both exits go back to the tab the code was typed on,
    /// for the same reason every other nested screen does.
    ///
    /// Guarded like every other way in here, and for a sharper reason: this is reached
    /// from a signal rather than from a row, so a throw would leave Godot's own dispatch
    /// holding it - with the browser already taken down and nothing on screen to say
    /// what happened.
    /// </summary>
    private static void Look(string code, bool fromMyRuns)
    {
        try
        {
            LibraryScreen.Dismiss();
            var answer = RunBrowser.Lookup(code, RunLibrary.Runs(), RunLibrary.ThisBuild());
            if (!answer.Refused && answer.Run is { } found)
            {
                OpenRun(found.RunId, fromMyRuns: fromMyRuns);
                return;
            }

            var body = answer.Note is { Length: > 0 } note
                ? $"{answer.Body}\n\n{LibraryMarkup.Dim(note)}"
                : answer.Body;
            var mine = fromMyRuns;
            LibraryScreen.Show(
                answer.Title, body, [], answer.Back,
                back: () => OpenTab(mine ? LibraryTab.MyRuns : LibraryTab.Community));
        }
        catch (Exception ex)
        {
            Refuse("could not look up that run code", ex);
        }
    }

    /// <summary>
    /// Stands the player where the row says, through the one entry there is.
    ///
    /// The progress record is written first and deliberately: it records that the
    /// player asked to play from this fight, which is what the pips and Continue's
    /// number are about. It is not a claim that the fight was won, or finished, or
    /// even entered - the run they are about to be put in is the recording's, and what
    /// happens in it is the comparison's business rather than this file's.
    /// </summary>
    private static void Enter(string runId, int kind, int? fight, int? floor)
    {
        if (RunLibrary.RecordingFor(runId) is not { } recording)
        {
            throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
        }

        if (fight is { } ordinal) RunLibraryStore.RecordFightPlayed(runId, ordinal);

        var plan = (RunViewRowKind)kind switch
        {
            RunViewRowKind.PlayFromFloor when floor is { } atFloor =>
                (IBoundaryPlan)FloorEntryPlan.For(recording, atFloor),
            _ when fight is { } atFight => RecordedFightPlan.For(recording, atFight),
            _ => throw new InvalidOperationException(
                $"That row names no boundary of '{runId}', so there is nowhere to stand."),
        };

        _ = RecordedFightRun.Start(recording, plan);
    }

    private static string RowLabel(LibraryRun run)
    {
        var parts = new List<string> { ModelIdNames.Display(run.Character) };
        if (run.Creator is { Length: > 0 } creator) parts.Insert(0, creator);
        parts.Add($"Ascension {run.Ascension.ToString(CultureInfo.InvariantCulture)}");
        parts.Add(
            $"{run.PlayedCount.ToString(CultureInfo.InvariantCulture)}/" +
            $"{run.FightCount.ToString(CultureInfo.InvariantCulture)} fights");
        if (run.Won) parts.Add("won");
        return string.Join(" · ", parts);
    }

    private static string Title(ReplayManifest recording) =>
        RecordingIdentity.CreatorOrNull(recording) is { Length: > 0 } creator
            ? creator
            : LibraryCopy.CompendiumCard;

    private static string Body(RunBrowser browser)
    {
        var body = new StringBuilder();
        body.Append(LibraryMarkup.Dim(
            browser.Tab == LibraryTab.Community ? LibraryCopy.CommunityTab : LibraryCopy.MyRunsTab));
        foreach (var group in browser.Groups.Where(group => group.Heading is { Length: > 0 }))
        {
            body.Append('\n').Append(LibraryMarkup.Dim(
                $"{group.Heading} · " +
                $"{group.Runs.Count.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (browser.NotShownLabel is { Length: > 0 } hidden)
        {
            body.Append("\n\n").Append(LibraryMarkup.Dim(hidden))
                .Append('\n').Append(LibraryMarkup.Dim(browser.NotShownTooltipBody));
        }

        if (browser.Footer is { Length: > 0 } footer)
        {
            body.Append("\n\n").Append(LibraryMarkup.Dim(footer));
            if (browser.FooterAction is { Length: > 0 } action)
            {
                body.Append('\n').Append(LibraryMarkup.Dim(action));
            }
        }

        return body.ToString();
    }

    private static string RunBody(ReplayManifest recording, RunView view)
    {
        var body = new StringBuilder();
        body.Append(LibraryMarkup.Dim(RecordingIdentity.Subtitle(recording)));
        if (view.Selected is { } selected)
        {
            body.Append('\n').Append(LibraryMarkup.Dim(
                LibraryCopy.FloorRow(selected.Floor, selected.Fight)));
        }

        body.Append('\n').Append(LibraryMarkup.Dim(
            $"{view.FightsPlayed.Count.ToString(CultureInfo.InvariantCulture)}/" +
            $"{view.FightCount.ToString(CultureInfo.InvariantCulture)} fights played"));
        body.Append("\n\n").Append(LibraryMarkup.Dim(view.FloorNote));
        return body.ToString();
    }

    /// <summary>
    /// Says what could not be done, on screen and in the log, and never guesses.
    ///
    /// A library that quietly showed an empty list where it had failed to read one
    /// would be the failure mode this project exists to prevent, at the one moment a
    /// player is deciding whether the mod works.
    /// </summary>
    private static void Refuse(string what, Exception? ex)
    {
        var detail = ex is null ? what : $"{what}: {ex.GetType().Name}: {ex.Message}";
        Log.Error($"[{RunmobileMod.ModId}] {detail}", 2);
        LibraryScreen.Show(
            LibraryCopy.CompendiumCard, LibraryMarkup.Dim(detail), [], LibraryCopy.Back);
    }
}

/// <summary>
/// The library's supporting text, in the game's own markup.
///
/// One colour and one rule: everything this surface says under a row or under the
/// list is supporting text, dimmer than the rows so the rows read first. It is the
/// same colour <see cref="ScreenMarkup"/> uses for the same job, held separately
/// because that file is the eligibility screen's layout rather than a palette.
/// </summary>
internal static class LibraryMarkup
{
    private const string SupportingColor = "#b6a892";

    internal static string Dim(string text) => $"[color={SupportingColor}]{Escape(text)}[/color]";

    /// <summary>Keeps a recording's own values out of the markup parser: a stray
    /// bracket in a run id would otherwise swallow the rest of the sentence.</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);
}
