using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The browse-and-play-from surface: the list with the selected run beside it, one run
/// opened, and the way into any place that run's recording proves.
///
/// It is one screen - one vocabulary, one entering verb, one set of rules about what is
/// shown - drawn on the game's own parchment with the design's furniture laid out in
/// it. Nothing here decides anything: <c>RunLibrary</c> gathers the runs,
/// <c>RunBrowser</c> and <c>RunView</c> decide what is offered, and this puts it on
/// screen and calls the one entry there is.
///
/// <para><b>Ordinary browsing hides a run this game cannot play.</b> That rule lives in
/// <c>LibraryRun.Listed</c> and reaches here as a shorter list plus a number.
/// The visible compatibility control can reveal those runs only as disabled rows, and
/// the run-code field does the same for an exact incompatible result.</para>
///
/// <para><b>The run view is reached one way.</b> The pane's "Open the run" ribbon, in
/// either tab. Back returns to the browser with the same run still selected, so the
/// list a player was reading does not move under them. Run history never opens it
/// except through its own "Choose another floor" row, which is about the run it was
/// already showing.</para>
/// </summary>
internal static class RunBrowserScreen
{
    private static readonly object PendingLock = new();
    private static readonly Dictionary<int, IndexRequest> PendingIndexRequests = [];
    private static readonly Dictionary<int, Task<IReadOnlyList<SharedRunSummary>>> PendingIndexes = [];
    private static readonly Dictionary<int, LookupRequest> PendingLookupRequests = [];
    private static readonly Dictionary<int, Task<SharedRun?>> PendingLookups = [];
    private static string? indexFailure;
    private static string? indexFailureScope;
    private static int nextRequest;

    /// <summary>Opens the library on the tab a player lands on: everybody's runs.</summary>
    internal static void Open() => OpenTab(LibraryTab.Community);

    /// <summary>
    /// One tab of the browser, with one run selected.
    ///
    /// <paramref name="selected"/> null selects the first run the list holds, so the
    /// pane is never empty on a list that is not; a run id keeps the selection across a
    /// return from the run view.
    ///
    /// Nothing captured here is a sibling assembly's type. A lambda in this assembly
    /// becomes a class whose fields are what it captured, and the game enumerates this
    /// assembly's types one phase before it can resolve a sibling - so a captured
    /// LibraryTab or LibraryRun stops the whole mod loading. ModAssemblyLoadOrderTests
    /// is what says so; see docs/in-game-host.md.
    /// </summary>
    internal static void OpenTab(
        LibraryTab tab, bool compatibleOnly = true, string? selected = null,
        bool skipIndexFetch = false, int? stripPage = null)
    {
        try
        {
            if (tab == LibraryTab.Community && !skipIndexFetch && RunLibrary.ShouldFetchIndex)
            {
                BeginIndexFetch((int)tab, compatibleOnly, selected);
                return;
            }

            var build = RunLibrary.ThisBuild();
            var runs = RunLibrary.Runs();
            var community = tab == LibraryTab.Community;
            var browser = RunBrowser.For(
                tab,
                runs,
                build,
                community ? null : RunLibraryStore.MyRunsBytes(),
                compatibleOnly,
                selectedEntryId: selected,
                submitAvailable: RunLibrary.SharingAvailable);

            var showCommunityRuns = RunmobileSettings.Read().FetchRunIndex;
            if (!showCommunityRuns) ForgetIndexFailure();
            var locked = CommunityLock.For(RunLibrary.SharingAvailable, showCommunityRuns);
            var rows = ListRows(browser, community);
            var selectedRow = rows
                .Select((row, index) => (row, index))
                .Where(item => item.row.Selected)
                .Select(item => (int?)item.index)
                .FirstOrDefault();

            LibraryScreen.Show(new LibraryPage(
                LibraryCopy.CompendiumCard,
                Tabs(community, locked),
                LibraryCopy.ListHeader(),
                rows,
                Pane(browser, community, stripPage),
                LibraryCopy.Back,
                ListFilter: new ScreenFilter(
                    LibraryCopy.CompatibleFilter,
                    browser.CompatibleOnly,
                    () => OpenTab(
                        community ? LibraryTab.Community : LibraryTab.MyRuns,
                        !compatibleOnly)),
                CodeSubmitted: code => Look(code, !community),
                CodePlaceholder: LibraryCopy.RunCodeField,
                Body: BrowserStatus(community ? locked : null),
                ListFooter: BrowserFooter(browser),
                ListFooterTooltip: browser.NotShownTooltipBody,
                SelectedRow: selectedRow));
        }
        catch (Exception ex)
        {
            Refuse("could not open the run library", ex);
        }
    }

    /// <summary>The two tabs across the band. The one you are on is selected and takes
    /// no press; the other crosses to itself with nothing selected, because a run of
    /// one tab is not a run of the other. Community wears the lock while it is short of
    /// a service or the setting, with the reason behind it.</summary>
    private static IReadOnlyList<ScreenTab> Tabs(bool community, CommunityLock? locked) =>
    [
        new(LibraryCopy.CommunityTab, community, () => OpenTab(LibraryTab.Community), locked?.Tooltip),
        new(LibraryCopy.MyRunsTab, !community, () => OpenTab(LibraryTab.MyRuns)),
    ];

    private static void BeginIndexFetch(int tab, bool compatibleOnly, string? selectedEntryId)
    {
        var surface = LibraryScreen.Show(new LibraryPage(
            LibraryCopy.CompendiumCard,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Body: LibraryMarkup.Dim(LibraryCopy.FetchingRunIndex)));
        var request = Interlocked.Increment(ref nextRequest);
        var task = RunLibrary.FetchIndexAsync(out var scope);
        lock (PendingLock)
        {
            PendingIndexRequests[request] = new IndexRequest(
                tab, compatibleOnly, selectedEntryId, surface, scope);
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

        if (!RunLibrary.IsCurrentSharingScope(state.Scope))
        {
            if (LibraryScreen.IsCurrent(state.Surface))
            {
                LibraryScreen.Dismiss();
                OpenTab((LibraryTab)state.Tab, state.CompatibleOnly, state.SelectedEntryId);
            }
            return;
        }

        var failed = !task.IsCompletedSuccessfully;
        try
        {
            if (!failed)
            {
                if (!RunLibrary.AcceptIndex(task.Result, state.Scope))
                {
                    if (LibraryScreen.IsCurrent(state.Surface))
                    {
                        LibraryScreen.Dismiss();
                        OpenTab((LibraryTab)state.Tab, state.CompatibleOnly, state.SelectedEntryId);
                    }
                    return;
                }

                indexFailure = null;
                indexFailureScope = null;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            indexFailure = LibraryCopy.FetchRunIndexFailed;
            indexFailureScope = state.Scope;
            Log.Error($"[{RunmobileMod.ModId}] could not accept the run index: {ex.Message}", 2);
        }

        if (failed)
        {
            RunLibrary.RefuseIndex(state.Scope);
            indexFailure = LibraryCopy.FetchRunIndexFailed;
            indexFailureScope = state.Scope;
            if (!task.IsCompletedSuccessfully)
            {
                Log.Error($"[{RunmobileMod.ModId}] could not fetch the run index: " +
                    task.Exception?.GetBaseException().Message, 2);
            }
        }

        if (!LibraryScreen.IsCurrent(state.Surface)) return;
        LibraryScreen.Dismiss();
        OpenTab(
            (LibraryTab)state.Tab, state.CompatibleOnly, state.SelectedEntryId,
            skipIndexFetch: failed);
    }

    private sealed record IndexRequest(
        int Tab, bool CompatibleOnly, string? SelectedEntryId, long Surface, string Scope);

    /// <summary>
    /// The one line over the tabs accounting for what the list is not showing.
    ///
    /// A locked Community tab says why it is locked and nothing else: a fetch this
    /// sitting failed is not what a player is looking at once the setting is off, and
    /// two accounts of one empty list is worse than the one that is still true.
    /// <paramref name="locked"/> is the same <see cref="CommunityLock"/> the tab's
    /// tooltip came from, so the hover and the line cannot give different reasons.
    /// </summary>
    private static string? BrowserStatus(CommunityLock? locked)
    {
        if (locked is { } reason) return LibraryMarkup.Dim(reason.Body);
        if (!RunLibrary.SharingAvailable)
            return LibraryMarkup.Dim(LibraryCopy.SharingServiceUnavailable);

        return indexFailure is { Length: > 0 } failure &&
               indexFailureScope is { } scope &&
               RunLibrary.IsCurrentSharingScope(scope)
            ? LibraryMarkup.Dim(failure)
            : null;
    }

    /// <summary>Drops a fetch failure this sitting recorded. A player who has turned
    /// the setting off attempts no fetch, so a failure from before it is a line about
    /// something that is no longer being tried.</summary>
    private static void ForgetIndexFailure()
    {
        indexFailure = null;
        indexFailureScope = null;
    }



    /// <summary>
    /// The list, one row per run, in the groups the browser put them in.
    ///
    /// A group's heading is a row of its own, refused, so it sits in the column where
    /// the design's headed sections sit and nothing about it is pressable. The My runs
    /// list is one set and heads nothing.
    /// </summary>
    private static IReadOnlyList<ScreenRow> ListRows(RunBrowser browser, bool community)
    {
        var rows = new List<ScreenRow>();
        var compatibleOnly = browser.CompatibleOnly;
        foreach (var group in browser.Groups)
        {
            if (group.Heading is { Length: > 0 } heading)
            {
                rows.Add(new ScreenRow(
                    heading, Enabled: false, () => { }, Heading: true));
            }

            foreach (var run in group.Runs)
            {
                var runId = run.EntryId;
                var mine = !community;
                rows.Add(new ScreenRow(
                    RowLabel(run),
                    Enabled: run.Listed,
                    () => OpenTab(
                        mine ? LibraryTab.MyRuns : LibraryTab.Community,
                        compatibleOnly,
                        selected: runId),
                    Note: RowNote(run),
                    Glyph: run.Won ? LibraryGlyph.Crown : LibraryGlyph.CrownStruck,
                    Selected: browser.Pane is not null &&
                              string.Equals(browser.Pane.Run.EntryId, runId, StringComparison.Ordinal),
                    ActReached: run.ActReached is { } act ? LibraryCopy.ActReached(act) : null,
                    Trailing: run.LastFloorReplayed is { } floor
                        ? floor.ToString(CultureInfo.InvariantCulture)
                        : null,
                    Character: run.Character));
            }
        }

        return rows;
    }

    /// <summary>
    /// The pane: the selected run, and the plate under it.
    ///
    /// The plate's rows are the browser's answer and this only presses them, which is
    /// what keeps "which rows does a My-runs pane carry" answerable by reading
    /// <c>RunBrowser</c>.
    /// </summary>
    private static ScreenPane? Pane(RunBrowser browser, bool community, int? stripPage)
    {
        if (browser.Pane is not { } pane) return null;

        var runId = pane.Run.EntryId;
        var mine = !community;
        var compatibleOnly = browser.CompatibleOnly;
        var plate = new List<ScreenRow>();
        foreach (var row in pane.Plate)
        {
            var kind = (int)row.Kind;
            var id = runId;
            plate.Add(new ScreenRow(
                row.Label,
                row.Enabled,
                () => PressPlate(id, kind),
                Glyph: row.Kind == PaneRowKind.Remove ? LibraryGlyph.Bin : null));
        }

        return new ScreenPane(
            ModelIdNames.Display(pane.Run.Character),
            Subtitle(pane.Run),
            [.. pane.Relics.Select(relic => relic.Id)],
            pane.Deck,
            pane.DeckCount,
            pane.Strip,
            // The pane's strip does not move a selection: a pane's one way forward is
            // its ribbon, and a strip that selected here would be a second run view.
            SelectFloor: null,
            StripPage: stripPage,
            SelectStripPage: page => OpenTab(
                mine ? LibraryTab.MyRuns : LibraryTab.Community,
                compatibleOnly,
                selected: runId,
                skipIndexFetch: true,
                stripPage: page),
            pane.Verdict,
            // The pane says how far the run itself went; how far this player has
            // replayed is the list's own column, so the two are not both here.
            pane.Run.LastFloor is { } reached ? [LibraryCopy.RunReached(reached)] : [],
            plate,
            new ScreenRow(
                pane.Open, pane.OpenEnabled, () => OpenRun(
                    runId, fromMyRuns: mine, compatibleOnly: compatibleOnly),
                Reason: pane.OpenEnabled ? null : pane.Verdict),
            VerdictPassed: pane.Run.Listed);
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
    internal static void OpenRun(
        string runId, int? floor = null, bool fromMyRuns = false, bool compatibleOnly = true,
        int? stripPage = null)
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
                RecordedFightModule.Instance.FightsShownThisSitting(runId),
                progressId: runId);
            var isPlayersOwn = RunLibrary.Runs().FirstOrDefault(run =>
                string.Equals(run.EntryId, runId, StringComparison.Ordinal))?.Origin == RunOrigin.Mine;

            var id = runId;
            var mine = fromMyRuns;
            LibraryScreen.Show(new LibraryPage(
                Title(recording),
                Tabs: [],
                ListHeader: null,
                EnteringRows(
                    view, runId, RecordingIdentity.CreditOrNull(recording, isPlayersOwn), isPlayersOwn),
                ViewPane(recording, view, runId, fromMyRuns, compatibleOnly, stripPage),
                LibraryCopy.Back,
                Back: () => OpenTab(
                    mine ? LibraryTab.MyRuns : LibraryTab.Community,
                    compatibleOnly,
                    selected: id)));
        }
        catch (Exception ex)
        {
            Refuse($"could not open '{runId}'", ex);
        }
    }

    /// <summary>
    /// The run view's pane: the identity, the relics, the deck at the selected
    /// position's start, the strip, and the fight pane's facts.
    ///
    /// Its strip is the one on this surface that moves the selection - pressing a cell
    /// re-opens the run at that floor - because selecting where to stand is what this
    /// screen is for. It carries no ribbon and no plate: every offer here is a row.
    /// </summary>
    private static ScreenPane ViewPane(
        ReplayManifest recording, RunView view, string runId, bool fromMyRuns,
        bool compatibleOnly, int? stripPage)
    {
        var facts = new List<string>();
        if (view.Reading.Enemies.FirstOrDefault() is { } enemy)
        {
            facts.Add(LibraryCopy.FightAgainst(ModelIdNames.Display(enemy.Model)));
        }

        if (view.Reading is { Hp: { } hp, MaxHp: { } maxHp }) facts.Add(LibraryCopy.HealthAt(hp, maxHp));

        // The floor pane's one sentence, said where a floor entry is what the row
        // offers: from there the run is the player's and nothing is compared.
        if (view.Selected is { Fight: null, IsRunStart: false, Unfinished: false }) facts.Add(view.FloorNote);

        // Said once, beside the rows, where one of them would actually stand a player
        // somewhere - the way the shipped trainer says it beside its Enter button.
        if (view.NotSaved is { Length: > 0 } notSaved) facts.Add(notSaved);

        var id = runId;
        var mine = fromMyRuns;
        var selectedFloor = view.Selected?.Floor;
        return new ScreenPane(
            ModelIdNames.Display(recording.Environment.Character.Value),
            RecordingIdentity.CreatorOrNull(recording),
            [.. view.Relics.Select(relic => relic.Id)],
            view.Deck,
            view.DeckCount,
            view.Strip,
            SelectFloor: atFloor => OpenRun(id, atFloor, mine, compatibleOnly),
            StripPage: stripPage,
            SelectStripPage: page => OpenRun(
                id, selectedFloor, mine, compatibleOnly, page),
            Verdict: null,
            facts,
            Plate: [],
            Ribbon: null);
    }

    /// <summary>
    /// The run view's own rows, as the screen presses them.
    ///
    /// Each carries its second line as well as its label, because that is what tells a
    /// player where pressing goes - the play-from row's line names the selected floor
    /// and what it held, and Continue's names the floor it goes to.
    ///
    /// Primitives only in what the lambdas capture, for the load-order reason OpenTab
    /// records.
    /// </summary>
    /// <param name="credit">Whose recording it is, for the sentence behind the
    /// shown-this-sitting mark; a recording nothing can credit carries the mark with
    /// the feature's own name in its place.</param>
    internal static IReadOnlyList<ScreenRow> EnteringRows(
        RunView view, string runId, RecordingCredit? credit = null, bool isPlayersOwn = false)
    {
        var rows = new List<ScreenRow>();
        foreach (var row in view.Rows)
        {
            var id = runId;
            var kind = (int)row.Kind;
            var fight = row.Fight;
            var atFloor = row.Floor;
            var held = row.Held;
            var own = isPlayersOwn;
            rows.Add(new ScreenRow(
                row.Label, row.Enabled, () => Enter(id, kind, fight, atFloor, own), row.Note, row.Reason,
                MarkTooltip: row.ShownThisSitting
                    ? TrainerCopy.ShownThisSittingTooltip(
                        credit ?? RecordingCredit.Named(TrainerCopy.Name))
                    : null,
                // The mark for what the row's floor held. The game's own run-history
                // room icons are keyed by whether the room was a monster, an elite or a
                // boss, and a recording establishes the enemy but not that - so drawing
                // one would be a claim nobody made, and the mod's own kind glyph says
                // exactly what was established.
                Glyph: held == FloorKind.Unknown ? null : LibraryGlyphArt.For(held)));
        }

        return rows;
    }

    /// <summary>
    /// Answers a run code.
    ///
    /// A code that names a listed run selects it in the list. A code that names a run
    /// this game cannot play says so - which is the only place on this surface such a
    /// run is described at all - and a code that names nothing says that instead of
    /// pretending the run does not exist.
    ///
    /// The browser's own panel comes down first, once, because both exits open another
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
        long? surface = null;
        try
        {
            LibraryScreen.Dismiss();
            var loadingSurface = LibraryScreen.Show(new LibraryPage(
                LibraryCopy.CompendiumCard,
                Tabs: [],
                ListHeader: null,
                Rows: [],
                Pane: null,
                LibraryCopy.Back,
                Back: static () => { },
                Body: LibraryMarkup.Dim(LibraryCopy.LookingUpRunCode)));
            surface = loadingSurface;
            var request = Interlocked.Increment(ref nextRequest);
            var task = RunLibrary.FindSharedAsync(code, out var scope);
            lock (PendingLock)
            {
                PendingLookupRequests[request] = new LookupRequest(
                    code, fromMyRuns, loadingSurface, scope);
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
            if (surface is { } ownedSurface && LibraryScreen.IsCurrent(ownedSurface))
                LibraryScreen.Dismiss();
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

        if (!RunLibrary.IsCurrentSharingScope(state.Scope))
        {
            if (LibraryScreen.IsCurrent(state.Surface))
            {
                LibraryScreen.Dismiss();
                OpenTab(state.FromMyRuns ? LibraryTab.MyRuns : LibraryTab.Community);
            }
            return;
        }
        if (!LibraryScreen.IsCurrent(state.Surface)) return;
        LibraryScreen.Dismiss();

        if (task.IsCompletedSuccessfully && task.Result is { } found)
        {
            try
            {
                var shared = RunLibrary.AcceptShared(found, state.Code, expectedScope: state.Scope);
                var lookup = RunBrowser.Lookup(
                    shared.Run.EntryId, [shared.Run], RunLibrary.ThisBuild());
                if (lookup.Outcome == LookupOutcome.Found)
                {
                    OpenTab(LibraryTab.Community, selected: shared.Run.EntryId);
                    return;
                }

                if (lookup.Outcome == LookupOutcome.IncompatibleBuild)
                {
                    OpenTab(LibraryTab.Community, compatibleOnly: false,
                        selected: shared.Run.EntryId);
                    return;
                }

                ShowLookup(lookup, state.FromMyRuns);
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

        ShowLookup(
            RunBrowser.Lookup(state.Code, [], RunLibrary.ThisBuild()), state.FromMyRuns);
    }

    private static void ShowLookup(RunLookup answer, bool fromMyRuns)
    {
        var body = answer.Note is { Length: > 0 } note
            ? $"{answer.Body}\n\n{LibraryMarkup.Dim(note)}"
            : answer.Body;
        var mine = fromMyRuns;
        LibraryScreen.Show(new LibraryPage(
            answer.Title,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            answer.Back,
            Back: () => OpenTab(mine ? LibraryTab.MyRuns : LibraryTab.Community),
            Body: body));
    }

    private static void RefuseLookup(string what, Exception? ex, bool fromMyRuns)
    {
        var detail = ex is null ? what : $"{what}: {ex.GetType().Name}: {ex.Message}";
        Log.Error($"[{RunmobileMod.ModId}] {detail}", 2);
        var mine = fromMyRuns;
        LibraryScreen.Show(new LibraryPage(
            LibraryCopy.CompendiumCard,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Back: () => OpenTab(mine ? LibraryTab.MyRuns : LibraryTab.Community),
            Body: LibraryMarkup.Dim(detail)));
    }

    private sealed record LookupRequest(string Code, bool FromMyRuns, long Surface, string Scope);

    /// <summary>
    /// Stands the player where the row says, through the one entry there is.
    ///
    /// The progress record is not written here. What the strip's ticks, Continue's
    /// floor and the Last floor replayed column are about is where a player has been
    /// stood, and that is not known at the press: a restore can refuse, fail or outlive
    /// its bound after a wait. So the floor this row names travels with the journey and
    /// <c>RecordedFightRun</c> writes both down once the boundary is proved. It is
    /// still not a claim that the fight was won or finished - what happens in it is the
    /// comparison's business rather than this file's.
    ///
    /// <para>Where a row stands a player follows the floor's own kind, which is the
    /// single play-from row's whole rule: a combat floor's fight start, and any other
    /// floor's entry. A row that names a fight carries its ordinal for exactly this,
    /// and never draws it.</para>
    /// </summary>
    private static void Enter(
        string runId, int kind, int? fight, int? floor, bool isPlayersOwn)
    {
        if (RunLibrary.RecordingFor(runId) is not { } recording)
        {
            throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
        }

        var plan = (RunViewRowKind)kind switch
        {
            _ when fight is { } atFight => (IBoundaryPlan)RecordedFightPlan.For(recording, atFight),
            RunViewRowKind.PlayFrom when floor is { } atFloor => FloorEntryPlan.For(recording, atFloor),
            _ => throw new InvalidOperationException(
                $"That row names no boundary of '{runId}', so there is nowhere to stand."),
        };

        var credit = RecordingIdentity.Credit(recording, isPlayersOwn);
        _ = RecordedFightRun.Start(recording, plan, credit, runId, floor);
    }

    /// <summary>
    /// The plate's rows, pressed.
    ///
    /// Submit opens the existing sharing flow. Removing is the one thing on this
    /// surface that cannot be undone, so it goes through the game's own confirm and then
    /// through <c>RecordingRetention</c>, which is the one owner of which files a run's
    /// are and the one thing that refuses to remove the run the game can continue.
    /// </summary>
    private static void PressPlate(string runId, int kind)
    {
        if ((PaneRowKind)kind == PaneRowKind.Submit)
        {
            if (RunLibrary.RecordingFor(runId) is not { } recording)
                throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
            var selectedId = runId;
            RunHistoryPlateHost.ShowShare(
                recording, () => OpenTab(LibraryTab.MyRuns, selected: selectedId));
            return;
        }

        if ((PaneRowKind)kind != PaneRowKind.Remove)
        {
            throw new InvalidOperationException("That plate row has no action in this build.");
        }

        var id = runId;
        LibraryScreen.Confirm(
            LibraryCopy.RemoveThisRun,
            LibraryMarkup.Dim(LibraryCopy.RemoveThisRunBody),
            LibraryCopy.Remove,
            LibraryCopy.KeepIt,
            () => Removed(id),
            () => OpenTab(LibraryTab.MyRuns, selected: id));
    }

    /// <summary>
    /// Removes one run and re-opens the list without it.
    ///
    /// The selection is not carried back: the run it named is gone, so the browser
    /// selects the first run the list still holds. A pane still showing a removed run
    /// would be describing something that is not there.
    /// </summary>
    private static void Removed(string runId)
    {
        try
        {
            if (!RecordingRetention.RemoveRun(runId))
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] removed nothing for '{runId}': it is either already gone or " +
                    "the run this game can continue.", 2);
            }
        }
        catch (Exception ex)
        {
            Refuse($"could not remove '{runId}'", ex);
            return;
        }

        OpenTab(LibraryTab.MyRuns);
    }

    private static string RowLabel(LibraryRun run)
    {
        var identity = run.Creator is { Length: > 0 } creator
            ? creator
            : ModelIdNames.Display(run.Character);
        return $"{identity} · A{run.Ascension.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>A row's second line: what the run carried, in the relic strip's own
    /// order. Written rather than drawn, because a list row is one duplicated ribbon
    /// and the relic icons belong to the pane.</summary>
    private static string? RowNote(LibraryRun run)
    {
        var strip = RelicStrip.ForRow(run.Relics, RelicRarities.Of);
        var parts = new List<string>();
        if (strip.Shown.Count > 0)
        {
            var relics = string.Join(", ", strip.Shown.Select(relic => ModelIdNames.Display(relic.Id)));
            parts.Add(strip.MoreLabel is { } more ? $"{relics} {more}" : relics);
        }

        if (run.DeckCount is { } cards) parts.Add(LibraryCopy.DeckCount(cards));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Title(ReplayManifest recording) =>
        RecordingIdentity.CreatorOrNull(recording) is { Length: > 0 } creator
            ? creator
            : LibraryCopy.CompendiumCard;

    private static string? Subtitle(LibraryRun run) =>
        run.Creator is { Length: > 0 } creator
            ? $"{creator} · Ascension {run.Ascension.ToString(CultureInfo.InvariantCulture)}"
            : null;

    /// <summary>The My runs footer, which is the one thing this screen says under the
    /// list rather than beside it. It carries no control: removing runs is a setting,
    /// and two places to do it would be two places to get it wrong.</summary>
    private static string? BrowserFooter(RunBrowser browser)
    {
        var parts = new List<string>();
        if (browser.NotShownLabel is { Length: > 0 } hidden) parts.Add(hidden);
        if (browser.Footer is { Length: > 0 } footer) parts.Add(footer);

        var summary = string.Join(" · ", parts);
        return browser.FooterAction is { Length: > 0 } action
            ? string.IsNullOrEmpty(summary) ? action : $"{summary}\n{action}"
            : string.IsNullOrEmpty(summary) ? null : summary;
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
        LibraryScreen.Show(new LibraryPage(
            LibraryCopy.CompendiumCard,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Body: LibraryMarkup.Dim(detail)));
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
