using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The plate under the game's own run history: what this run's recording is, and the
/// two places it will stand you.
///
/// The game builds one <c>NMapPointHistoryEntry</c> per floor of the run it is
/// showing, and each of them is an <c>NClickableControl</c> that already emits
/// <c>Released</c> with nothing in the game connected to it. That signal is the hook,
/// and the entry itself carries both halves of what is needed: the
/// <c>RunHistory</c> it belongs to and its own floor number. So there is one patch
/// here rather than two - nothing has to follow the screen's own selection to know
/// which run a press is about.
///
/// <para><b>Play-from is after the fact.</b> Run history is a Compendium screen and
/// the game opens it only when no run is live, so in practice this is never reached
/// mid-run. The plate still carries the state for it and says so in a sentence,
/// because a surface that would be wrong if the game changed should say what it
/// believes rather than assume it.</para>
///
/// <para><b>A flat plate in the game's own tree, not a modal.</b> It is inserted as the
/// next sibling after the run-history pane, so a parent that lays its children out
/// moves the rows below it down rather than having the plate drawn over them; a parent
/// that does not is measured and the plate is positioned under the pane. Either way the
/// history screen is still the screen a player is on - a modal here would have covered
/// the run they pressed on with a panel about it.</para>
///
/// <para><b>One plate at a time, and it belongs to the entry that is showing.</b> A
/// second press replaces the first, because the plate is about the run under the
/// cursor and two of them would be two answers to one question.</para>
///
/// <para><b>No head line in the ordinary state.</b> The history row's own record mark
/// already says the run is recorded. What a head there is, and its mark, is
/// <see cref="RunHistoryPlate"/>'s answer, and it is drawn with the library's own
/// glyph family - the warning triangle in the eligibility screen's colours.</para>
/// </summary>
internal static class RunHistoryPlateHost
{
    private static readonly object PendingLock = new();
    private static readonly Dictionary<int, Task<SharedRun>> PendingShares = [];
    private static readonly Dictionary<int, long> PendingShareSurfaces = [];
    private static readonly Dictionary<int, string> PendingShareScopes = [];
    private static readonly Dictionary<int, Action> PendingShareBacks = [];
    private static readonly HashSet<long> SubmittingSurfaces = [];
    private static int nextShare;

    /// <summary>
    /// Connects each run-history entry the game builds, once.
    ///
    /// A postfix on <c>_Ready</c>, which is where the entry connects its own signals -
    /// so this runs after the node is in the tree and its own wiring is done, and
    /// before a player can have pressed anything.
    /// </summary>
    [HarmonyPatch(typeof(NMapPointHistoryEntry))]
    internal static class HistoryEntry
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(NMapPointHistoryEntry._Ready))]
        internal static void Connect(NMapPointHistoryEntry __instance)
        {
            try
            {
                var entry = __instance;
                entry.Connect(
                    NClickableControl.SignalName.Released,
                    Callable.From<NButton>(_ => Show(entry)));
            }
            catch (Exception ex)
            {
                // The player's run history is not ours to break. A plate that never
                // appeared is a bug report; a history screen that failed to build is a
                // broken game.
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not connect a run-history entry: " +
                    $"{ex.GetType().Name}: {ex.Message}", 2);
            }
        }
    }

    /// <summary>What this mod has hung under the pane, so a second press replaces the
    /// first rather than stacking. Held as the node itself, and every read checks it is
    /// still in the tree - the history screen frees its children when it changes run,
    /// and a reference to a freed node is not a plate.</summary>
    private static Control? _plate;

    /// <summary>Shows the plate for the run one history entry belongs to.</summary>
    private static void Show(NMapPointHistoryEntry entry)
    {
        try
        {
            Clear();
            var history = HistoryOf(entry);
            var recording = history is null ? null : RecordingFor(history);
            if (PlateFor(history, recording) is not { } plate) return;

            var pane = PaneOf(entry);
            if (pane?.GetParent() is not Node parent) return;

            // The screen's own text size, read where the plate will hang rather than
            // written down: the plate is part of this screen and is drawn at its size.
            _plate = RunHistoryPlateArt.Build(
                parent, plate, Rows(plate, recording), pane.Size.X, HistoryText(pane));

            // Immediately after the pane, so a parent that lays its children out puts
            // the plate between the pane and whatever follows it - which is what makes
            // the rows below move down rather than be covered. A parent that lays
            // nothing out ignores this and the plate keeps the position it was built
            // with, under the pane.
            parent.MoveChild(_plate, pane.GetIndex() + 1);
            _plate.Position = new Vector2(pane.Position.X, pane.Position.Y + pane.Size.Y);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read this run's recording: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>Takes down whatever this mod has hung under the pane.</summary>
    private static void Clear()
    {
        if (_plate is { } plate && GodotObject.IsInstanceValid(plate)) plate.QueueFreeSafely();
        _plate = null;
    }

    /// <summary>
    /// The pane the plate hangs under: the run-history screen's own map-point history,
    /// which is the node the pressed entry is inside.
    ///
    /// Found by walking up from the entry rather than by looking the screen up, for the
    /// reason there is one patch here rather than two: the entry a player pressed knows
    /// which pane it is in, and a second reading of which screen is up could disagree
    /// with it.
    /// </summary>
    private static RunHistoryPlateText HistoryText(NMapPointHistory pane)
    {
        for (Node? node = pane; node is not null; node = node.GetParent())
        {
            if (node is NRunHistory history)
            {
                return new RunHistoryPlateText(
                    GameText.Require(
                        history.GetNodeOrNull<Control>("%BuildLabel"), "run-history fact line"),
                    GameText.Scene(NativeTextRole.ButtonCaption));
            }
        }

        throw new InvalidOperationException("This map-point history has no run-history screen.");
    }

    private static NMapPointHistory? PaneOf(NMapPointHistoryEntry entry)
    {
        for (Node? node = entry; node is not null; node = node.GetParent())
        {
            if (node is NMapPointHistory pane) return pane;
        }

        return null;
    }

    /// <summary>
    /// The plate a press gets, or none at all.
    ///
    /// The shell is asked before anything is drawn, and a no is the whole answer: no
    /// plate, so no modal, and no sentence saying why one is missing. What this mod may
    /// put in front of somebody else's game is the shell's, and a refusal popup would be
    /// the mod speaking anyway.
    /// </summary>
    internal static RunHistoryPlate? PlateFor(RunHistory? history, ReplayManifest? recording) =>
        RunmobileMod.MayDraw ? RunHistoryPlate.For(FactsFor(history, recording)) : null;

    /// <summary>
    /// What is true of the run the history is showing, each part read from whatever
    /// established it.
    ///
    /// Continuity and the recorded build come from the recording, which is the only
    /// thing that witnessed them; what the run's last floor held is derived from the
    /// recording's own decisions on it, through the same <c>FloorKinds</c> the run view
    /// reads, so a floor named one thing here and another there is impossible.
    ///
    /// <para>Whether a console command was used is the recording's own answer, read
    /// through <c>NativeSource.StatesSomethingOtherThanComplete</c> - the owner of that
    /// reading, so the integrity values are compared in one place. A recording stating
    /// no integrity at all answers null rather than a clean run, because reporting one
    /// this never established is the claim <c>AGENTS.md</c> forbids; from format v6 the
    /// field is required and a version-5 file reads as <c>complete</c> through the
    /// migration, so no manifest this build parses reaches that answer.</para>
    ///
    /// </summary>
    internal static RunHistoryFacts FactsFor(RunHistory? history, ReplayManifest? recording)
    {
        var positions = recording is null
            ? []
            : RunView.PositionsIn(recording);

        // The last floor this build can stand a player at, rather than the last floor
        // the run reached. The row is an offer to play from somewhere, so naming a
        // place the journey would abort on would be the plate offering what the run
        // view refuses; RunViewPosition.Playable is the one rule both read.
        var last = positions.LastOrDefault(position => position.Playable);

        return new RunHistoryFacts(
            HasRecording: recording is not null,
            Continuous: recording?.Source.Native?.IsContinuous ?? false,
            ConsoleUsed: recording?.Source.Native is { Integrity: not null } native
                ? native.StatesSomethingOtherThanComplete
                : null,
            RecordedBuild: recording?.Environment.BuildVersion.Value ?? string.Empty,
            ThisBuild: RunLibrary.ThisBuild(),
            RunInProgress: LocalEnvironment.ReadStartedRun() is not null,
            SubmitAvailable: RunLibrary.SharingAvailable,
            LastFloor: last?.Floor,
            LastFloorKind: last?.Kind ?? FloorKind.Unknown,
            HasOtherFloors: positions.Count > 1);
    }

    /// <summary>
    /// The plate's rows as this screen presses them.
    ///
    /// Nothing captured is a sibling assembly's type. A lambda here becomes a class
    /// whose fields are what it captured, and the game enumerates this assembly's types
    /// one phase before it can resolve a sibling - so a captured <c>PlateRow</c> or
    /// <c>ReplayManifest</c> stops the whole mod loading. See docs/in-game-host.md;
    /// <c>ModAssemblyLoadOrderTests</c> is what actually says so.
    ///
    /// Whether a row can be pressed is the plate's answer and nothing here overrules
    /// it: this asks only whether there is a run behind the row to press it against.
    /// </summary>
    private static IReadOnlyList<ScreenRow> Rows(RunHistoryPlate plate, ReplayManifest? recording)
    {
        var runId = recording?.RunId;
        var rows = new List<ScreenRow>();
        foreach (var row in plate.Rows)
        {
            var id = runId;
            var floor = row.Floor;
            var kind = (int)row.Kind;
            rows.Add(new ScreenRow(
                row.Label,
                row.Enabled && id is not null,
                () => Press(id!, kind, floor),
                // The disclosure chevron on the row that drills in, because it opens a
                // deeper screen about the same run rather than going somewhere else.
                Glyph: row.Kind == PlateRowKind.ChooseAnotherFloor ? LibraryGlyph.Chevron : null));
        }

        return rows;
    }

    /// <summary>
    /// What one plate row does.
    ///
    /// Nothing captured is a sibling assembly's type, which is why the row's kind
    /// travels as an int: a lambda here becomes a class whose fields are what it
    /// captured, and the game enumerates this assembly's types one phase before it can
    /// resolve a sibling. See docs/in-game-host.md;
    /// <c>ModAssemblyLoadOrderTests</c> is what actually says so.
    /// </summary>
    private static void Press(string runId, int kind, int? floor)
    {
        Clear();
        if ((PlateRowKind)kind == PlateRowKind.ChooseAnotherFloor)
        {
            RunBrowserScreen.OpenRun(runId, floor, fromMyRuns: true);
            return;
        }

        if (RunLibrary.RecordingFor(runId) is not { } recording)
        {
            throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
        }

        if ((PlateRowKind)kind == PlateRowKind.Submit)
        {
            ShowShare(recording);
            return;
        }

        if (floor is not { } atFloor)
        {
            throw new InvalidOperationException(
                $"That row names no boundary of '{runId}', so there is nowhere to stand.");
        }

        // Where a floor holds a fight the recording finished, the entry is that fight's
        // start - the same rule the run view's single play-from row follows, so a floor
        // named the same way on both surfaces stands a player in the same place.
        RunLibraryStore.RecordFloorLoaded(runId, atFloor);
        var fight = RunView.PositionsIn(recording)
            .FirstOrDefault(position => position.Floor == atFloor)?.Fight;
        if (fight is { } ordinal)
        {
            RunLibraryStore.RecordFightPlayed(runId, ordinal);
            _ = RecordedFightRun.Start(
                recording, RecordedFightPlan.For(recording, ordinal),
                RecordingIdentity.Credit(recording, isPlayersOwn: true));
            return;
        }

        _ = RecordedFightRun.Start(
            recording, FloorEntryPlan.For(recording, atFloor),
            RecordingIdentity.Credit(recording, isPlayersOwn: true));
    }

    internal static void ShowShare(ReplayManifest recording, Action? back = null)
    {
        var form = ShareRunForm.For(recording);
        var id = recording.RunId;
        var body = string.Join("\n", [
            form.IdentitySeal,
            form.IntegritySeal,
            string.Empty,
            form.Privacy,
            form.LocalValidation,
        ]);
        LibraryScreen.Show(new LibraryPage(
            LibraryCopy.SubmitThisRun,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Back: back,
            Body: LibraryMarkup.Dim(body),
            ShareSubmitted: (surface, name, description, displayName, consent) =>
                Submit(surface, id, name, description, displayName, consent, back)));
    }

    private static void Submit(
        long surface, string runId, string name, string description, string displayName, bool consent,
        Action? back)
    {
        lock (PendingLock)
        {
            if (!SubmittingSurfaces.Add(surface)) return;
        }

        try
        {
            new ShareSubmission(name, description, displayName, consent).Validate();
            if (RunLibrary.RecordingFor(runId) is null)
                throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
            LibraryScreen.Invalidate(surface);
            Callable.From(() => BeginSubmit(
                surface, runId, name, description, displayName, consent, back)).CallDeferred();
        }
        catch (Exception ex)
        {
            lock (PendingLock) SubmittingSurfaces.Remove(surface);
            LibraryScreen.Invalidate(surface);
            var message = ex.Message;
            Callable.From(() => ShowSubmitFailure(runId, message, back)).CallDeferred();
        }
    }

    private static void ShowSubmitFailure(string runId, string message, Action? back)
    {
        LibraryScreen.Show(new LibraryPage(
            LibraryCopy.SubmitThisRun,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Back: () =>
            {
                if (RunLibrary.RecordingFor(runId) is { } retry) ShowShare(retry, back);
            },
            Body: LibraryMarkup.Dim(message)));
    }

    private static void BeginSubmit(
        long formSurface,
        string runId,
        string name,
        string description,
        string displayName,
        bool consent,
        Action? back)
    {
        var loadingSurface = LibraryScreen.Show(new LibraryPage(
            LibraryCopy.SubmitThisRun,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Back: static () => { },
            Body: LibraryMarkup.Dim(LibraryCopy.ShareValidating)));
        try
        {
            if (RunLibrary.RecordingFor(runId) is not { } recording)
                throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
            var request = Interlocked.Increment(ref nextShare);
            var task = RunLibrary.ShareAsync(
                recording, new ShareSubmission(name, description, displayName, consent),
                out var scope);
            lock (PendingLock)
            {
                SubmittingSurfaces.Remove(formSurface);
                SubmittingSurfaces.Add(loadingSurface);
                PendingShares[request] = task;
                PendingShareSurfaces[request] = loadingSurface;
                PendingShareScopes[request] = scope;
                if (back is not null) PendingShareBacks[request] = back;
            }
            _ = task.ContinueWith(
                static (_, value) => Callable.From(() => CompleteShare((int)value!)).CallDeferred(),
                request,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            lock (PendingLock)
            {
                SubmittingSurfaces.Remove(formSurface);
                SubmittingSurfaces.Remove(loadingSurface);
            }
            if (!LibraryScreen.IsCurrent(loadingSurface)) return;
            LibraryScreen.Dismiss();
            ShowSubmitFailure(runId, ex.Message, back);
        }
    }

    private static void CompleteShare(int request)
    {
        Task<SharedRun> task;
        long surface;
        string scope;
        Action? back;
        lock (PendingLock)
        {
            task = PendingShares[request];
            surface = PendingShareSurfaces[request];
            scope = PendingShareScopes[request];
            PendingShareBacks.TryGetValue(request, out back);
            PendingShares.Remove(request);
            PendingShareSurfaces.Remove(request);
            PendingShareScopes.Remove(request);
            PendingShareBacks.Remove(request);
            SubmittingSurfaces.Remove(surface);
        }

        if (!RunLibrary.IsCurrentSharingScope(scope))
        {
            if (LibraryScreen.IsCurrent(surface)) LibraryScreen.Dismiss();
            return;
        }

        SharedRun? shared = null;
        Exception? failure = task.Exception?.GetBaseException();
        if (task.IsCompletedSuccessfully)
        {
            try
            {
                shared = RunLibrary.AcceptShared(task.Result, expectedScope: scope);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        if (!LibraryScreen.IsCurrent(surface)) return;
        LibraryScreen.Dismiss();
        LibraryScreen.Show(new LibraryPage(
            LibraryCopy.SubmitThisRun,
            Tabs: [],
            ListHeader: null,
            Rows: [],
            Pane: null,
            LibraryCopy.Back,
            Back: back,
            Body: LibraryMarkup.Dim(shared is null
                ? failure?.Message ?? "Sharing was refused."
                : $"Shared as {shared.Code}")));
    }

    /// <summary>The four things the game's own history and a recording both carry and
    /// both mean the same way. Read off the history once, so what a recording is matched
    /// against is a value rather than a screen. A character of null is a run more than
    /// one person played, which no recording is a recording of.</summary>
    internal readonly record struct HistoryIdentity(
        string Seed, int Ascension, string Build, string? Character);

    /// <summary>Which of the player's recordings is a recording of this run.</summary>
    internal static ReplayManifest? RecordingFor(RunHistory history) => RecordingOf(IdentityOf(history));

    /// <inheritdoc cref="RecordingFor"/>
    internal static HistoryIdentity IdentityOf(RunHistory history) =>
        new(
            history.Seed,
            history.Ascension,
            history.BuildId,
            history.Players.Count == 1 ? history.Players[0].Character.ToString() : null);

    /// <summary>
    /// Which of the player's recordings is a recording of this run.
    ///
    /// Matched on the four things the game's own history and a recording both carry and
    /// both mean the same way: the seed, the character, the ascension and the build.
    /// Two runs a player started on the same seed with the same character at the same
    /// ascension on the same build are indistinguishable here, and the honest answer to
    /// that is none rather than the first of them - a plate that offered the wrong run's
    /// fights would stand somebody in a fight they never had.
    ///
    /// <para>The seed narrows before anything is opened. A recording's name carries the
    /// seed of the run it recorded, so a press reads the one or two recordings that could
    /// be this run rather than the fifty a player's disk may hold - and it stops at the
    /// second match, which is all the ambiguity rule needs to know.</para>
    /// </summary>
    internal static ReplayManifest? RecordingOf(HistoryIdentity identity)
    {
        ReplayManifest? found = null;
        foreach (var runId in RunLibraryStore.StoredRunIdsOn(identity.Seed))
        {
            if (RunLibraryStore.RecordingFor(runId) is not { } recording) continue;
            if (!Matches(recording, identity)) continue;
            if (found is not null) return null;

            found = recording;
        }

        return found;
    }

    private static bool Matches(ReplayManifest recording, HistoryIdentity identity) =>
        identity.Character is not null &&
        string.Equals(recording.Environment.Seed.Value, identity.Seed, StringComparison.Ordinal) &&
        recording.Environment.Ascension.Value == identity.Ascension &&
        string.Equals(
            recording.Environment.BuildVersion.Value, identity.Build, StringComparison.Ordinal) &&
        string.Equals(
            recording.Environment.Character.Value, identity.Character, StringComparison.Ordinal);

    /// <summary>
    /// The run one history entry belongs to.
    ///
    /// Read by name off the entry's own private field, the way every other private
    /// reading in this project is taken, and refusing loudly when a build no longer has
    /// it. The alternative - following the run-history screen's own selection - would
    /// be a second reading of which run is on screen that could disagree with the entry
    /// a player actually pressed.
    /// </summary>
    internal static RunHistory? HistoryOf(NMapPointHistoryEntry entry)
    {
        var field = typeof(NMapPointHistoryEntry).GetField(
            "_runHistory", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "NMapPointHistoryEntry has no '_runHistory' on this build, so nothing here can say which " +
                "run a history entry belongs to.");
        return field.GetValue(entry) as RunHistory;
    }
}
