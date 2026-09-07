using System.Globalization;
using System.Text;
using Godot;
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
/// <para>What the accepted design draws and this does not: the run strip, the deck
/// tiles, the relic row and the portrait; and the list's group headings,
/// which are a summary line in the popup's body rather than headings between the rows,
/// so the rows follow group order without each one saying which group it is in. Those
/// are a scene this mod has no path to build, and what stands in for them is a row per
/// floor and a line of text. The offers, the wording and the rules are the design's
/// exactly, and the furniture follow-up draws them properly.</para>
/// </summary>
internal static class RunBrowserScreen
{
    private static readonly object PendingLock = new();
    private static readonly Dictionary<int, IndexRequest> PendingIndexRequests = [];
    private static readonly Dictionary<int, Task<IReadOnlyList<SharedRunSummary>>> PendingIndexes = [];
    private static readonly Dictionary<int, LookupRequest> PendingLookupRequests = [];
    private static readonly Dictionary<int, Task<SharedRun?>> PendingLookups = [];
    private static string? indexFailure;
    private static int nextRequest;

    /// <summary>Opens the library on the tab a player lands on: everybody's runs.</summary>
    internal static void Open() => OpenTab(LibraryTab.Community);

    internal static void OpenTab(
        LibraryTab tab, bool compatibleOnly = true, string? selectedRunId = null,
        bool skipIndexFetch = false)
    {
        try
        {
            if (!skipIndexFetch && RunLibrary.ShouldFetchIndex)
            {
                BeginIndexFetch((int)tab, compatibleOnly, selectedRunId);
                return;
            }

            var build = RunLibrary.ThisBuild();
            var runs = RunLibrary.Runs();
            var browser = RunBrowser.For(
                tab, runs, build, tab == LibraryTab.MyRuns ? RunLibraryStore.MyRunsBytes() : null,
                compatibleOnly, selectedRunId);

            // Nothing captured here is a sibling assembly's type. A lambda in this
            // assembly becomes a class whose fields are what it captured, and the game
            // enumerates this assembly's types one phase before it can resolve a
            // sibling - so a captured LibraryTab or LibraryRun stops the whole mod
            // loading. ModAssemblyLoadOrderTests is what says so; see
            // docs/in-game-host.md.
            var community = tab == LibraryTab.Community;
            var showCompatibleOnly = browser.CompatibleOnly;
            var rows = new List<ScreenRow>
            {
                // Pinned: the way to the other tab is this screen's own navigation, and
                // a paged column that carried it as an ordinary row would drop it on
                // every page after the first. The ribbon here closes the library, so
                // that would leave a player no way across.
                new(
                    community ? LibraryCopy.MyRunsTab : LibraryCopy.CommunityTab,
                    Enabled: true,
                    () => OpenTab(community ? LibraryTab.MyRuns : LibraryTab.Community),
                    Pinned: true),
                new(
                    $"{(showCompatibleOnly ? "✓" : "□")} {LibraryCopy.CompatibleFilter}",
                    Enabled: true,
                    () => OpenTab(
                        community ? LibraryTab.Community : LibraryTab.MyRuns,
                        !showCompatibleOnly),
                    Pinned: true),
            };
            int? revealRow = null;

            foreach (var group in browser.Groups)
            {
                foreach (var run in group.Runs)
                {
                    var runId = run.RunId;
                    var shareCode = community ? RunLibrary.ShareCodeFor(runId) : null;
                    var mine = !community;
                    var selected = string.Equals(
                        browser.SelectedRunId, run.RunId, StringComparison.Ordinal);
                    var reason = run.Listed
                        ? null
                        : LibraryCopy.LookupRefusedBuild(run.RecordedBuild, build);
                    if (selected) revealRow = rows.Count - 2;
                    rows.Add(new ScreenRow(
                        $"{(selected ? "▶ " : string.Empty)}{RowLabel(run)}",
                        Enabled: run.Listed,
                        () =>
                        {
                            if (shareCode is { Length: > 0 }) Look(shareCode, mine);
                            else OpenRun(runId, fromMyRuns: mine);
                        },
                        Reason: reason));
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
                codePlaceholder: LibraryCopy.RunCodeField,
                revealRow: revealRow);
        }
        catch (Exception ex)
        {
            Refuse("could not open the run library", ex);
        }
    }

    private static void BeginIndexFetch(int tab, bool compatibleOnly, string? selectedRunId)
    {
        var surface = LibraryScreen.Show(
            LibraryCopy.CompendiumCard,
            LibraryMarkup.Dim(LibraryCopy.FetchingRunIndex),
            [],
            LibraryCopy.Back,
            back: static () => { });
        var request = Interlocked.Increment(ref nextRequest);
        var task = RunLibrary.FetchIndexAsync();
        lock (PendingLock)
        {
            PendingIndexRequests[request] = new IndexRequest(
                tab, compatibleOnly, selectedRunId, surface);
            PendingIndexes[request] = task;
        }
        _ = task.ContinueWith(
            static (_, value) => Callable.From(() => CompleteIndex((int)value!)).CallDeferred(),
            request,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void CompleteIndex(int request)
    {
        Task<IReadOnlyList<SharedRunSummary>> task;
        IndexRequest state;
        lock (PendingLock)
        {
            task = PendingIndexes[request];
            state = PendingIndexRequests[request];
            PendingIndexes.Remove(request);
            PendingIndexRequests.Remove(request);
        }

        var failed = !task.IsCompletedSuccessfully;
        try
        {
            if (!failed)
            {
                RunLibrary.AcceptIndex(task.Result);
                indexFailure = null;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            indexFailure = LibraryCopy.FetchRunIndexFailed;
            Log.Error(
                $"[{RunmobileMod.ModId}] could not accept the run index: {ex.Message}", 2);
        }

        if (failed)
        {
            RunLibrary.RefuseIndex();
            indexFailure = LibraryCopy.FetchRunIndexFailed;
            if (!task.IsCompletedSuccessfully)
            {
                Log.Error($"[{RunmobileMod.ModId}] could not fetch the run index: " +
                    task.Exception?.GetBaseException().Message, 2);
            }
        }

        if (!LibraryScreen.IsCurrent(state.Surface)) return;
        LibraryScreen.Dismiss();
        OpenTab(
            (LibraryTab)state.Tab, state.CompatibleOnly, state.SelectedRunId,
            skipIndexFetch: failed);
    }

    private sealed record IndexRequest(
        int Tab, bool CompatibleOnly, string? SelectedRunId, long Surface);

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

            var view = RunView.For(
                recording, RunLibraryStore.ReadProgress(), floor,
                CombatTrainerModule.Instance.FightsShownThisSitting(runId));
            var rows = new List<ScreenRow>(EnteringRows(view, runId, RecordingIdentity.CreatorOrNull(recording)));

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
    /// <param name="creator">Whose recording it is, for the sentence behind the
    /// shown-this-sitting mark; a recording that names nobody carries the mark with
    /// the feature's own name in its place.</param>
    internal static IReadOnlyList<ScreenRow> EnteringRows(RunView view, string runId, string? creator = null)
    {
        var rows = new List<ScreenRow>();
        foreach (var row in view.Rows)
        {
            var id = runId;
            var kind = (int)row.Kind;
            var fight = row.Fight;
            var atFloor = row.Floor;
            rows.Add(new ScreenRow(
                row.Label, row.Enabled, () => Enter(id, kind, fight, atFloor), row.Note, row.Reason,
                MarkTooltip: row.ShownThisSitting
                    ? TrainerCopy.ShownThisSittingTooltip(creator ?? TrainerCopy.Name)
                    : null));
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
    /// The browser's own modal becomes a loading surface while the request runs because
    /// both exits open another one and the container holds a single screen. A completion
    /// belongs to that surface and does nothing after the player leaves it. Both exits go
    /// back to the tab the code was typed on, for the same reason every other nested
    /// screen does.
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
            var surface = LibraryScreen.Show(
                LibraryCopy.CompendiumCard,
                LibraryMarkup.Dim(LibraryCopy.LookingUpRunCode),
                [],
                LibraryCopy.Back,
                back: static () => { });
            var request = Interlocked.Increment(ref nextRequest);
            var task = RunLibrary.FindSharedAsync(code);
            lock (PendingLock)
            {
                PendingLookupRequests[request] = new LookupRequest(code, fromMyRuns, surface);
                PendingLookups[request] = task;
            }
            _ = task.ContinueWith(
                static (_, value) => Callable.From(() => CompleteLookup((int)value!)).CallDeferred(),
                request,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            RefuseLookup("could not look up that run code", ex, fromMyRuns);
        }
    }

    private static void CompleteLookup(int request)
    {
        Task<SharedRun?> task;
        LookupRequest state;
        lock (PendingLock)
        {
            task = PendingLookups[request];
            state = PendingLookupRequests[request];
            PendingLookups.Remove(request);
            PendingLookupRequests.Remove(request);
        }

        if (!LibraryScreen.IsCurrent(state.Surface)) return;
        LibraryScreen.Dismiss();

        if (task.IsCompletedSuccessfully && task.Result is { } found)
        {
            try
            {
                var shared = RunLibrary.AcceptShared(found, state.Code, exact: true);
                var lookup = RunBrowser.Lookup(
                    shared.Run.RunId, [shared.Run], RunLibrary.ThisBuild());
                if (lookup.Outcome == LookupOutcome.Found)
                {
                    OpenRun(shared.Run.RunId, fromMyRuns: state.FromMyRuns);
                    return;
                }

                if (lookup.Outcome == LookupOutcome.IncompatibleBuild)
                {
                    OpenTab(LibraryTab.Community, compatibleOnly: false,
                        selectedRunId: shared.Run.RunId);
                    return;
                }

                var remoteBody = lookup.Note is { Length: > 0 } remoteNote
                    ? $"{lookup.Body}\n\n{LibraryMarkup.Dim(remoteNote)}"
                    : lookup.Body;
                var backToMyRuns = state.FromMyRuns;
                LibraryScreen.Show(
                    lookup.Title, remoteBody, [], lookup.Back,
                    back: () => OpenTab(backToMyRuns ? LibraryTab.MyRuns : LibraryTab.Community));
                return;
            }
            catch (Exception ex)
            {
                RefuseLookup("could not accept that run code", ex, state.FromMyRuns);
                return;
            }
        }

        if (!task.IsCompletedSuccessfully)
        {
            RefuseLookup(
                "could not look up that run code",
                task.Exception?.GetBaseException(),
                state.FromMyRuns);
            return;
        }

        var fromMyRuns = state.FromMyRuns;
        var answer = RunBrowser.Lookup(state.Code, RunLibrary.Runs(), RunLibrary.ThisBuild());
        if (!answer.Refused && answer.Run is { } local)
        {
            OpenRun(local.RunId, fromMyRuns: state.FromMyRuns);
            return;
        }

        var body = answer.Note is { Length: > 0 } note
            ? $"{answer.Body}\n\n{LibraryMarkup.Dim(note)}"
            : answer.Body;
        LibraryScreen.Show(
            answer.Title, body, [], answer.Back,
            back: () => OpenTab(fromMyRuns ? LibraryTab.MyRuns : LibraryTab.Community));
    }

    private sealed record LookupRequest(string Code, bool FromMyRuns, long Surface);

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
        body.Append('\n').Append(LibraryMarkup.Dim(
            $"{LibraryCopy.CompatibleFilter}: {(browser.CompatibleOnly ? "on" : "off")}"));
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

        if (indexFailure is { Length: > 0 } failure)
        {
            body.Append("\n\n").Append(LibraryMarkup.Dim(failure));
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
    private static void RefuseLookup(string what, Exception? ex, bool fromMyRuns)
    {
        var detail = ex is null ? what : $"{what}: {ex.GetType().Name}: {ex.Message}";
        Log.Error($"[{RunmobileMod.ModId}] {detail}", 2);
        LibraryScreen.Show(
            LibraryCopy.CompendiumCard,
            LibraryMarkup.Dim(detail),
            [],
            LibraryCopy.Back,
            back: () => OpenTab(fromMyRuns ? LibraryTab.MyRuns : LibraryTab.Community));
    }

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
