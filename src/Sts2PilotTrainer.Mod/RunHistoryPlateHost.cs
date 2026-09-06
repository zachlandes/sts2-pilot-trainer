using System.Reflection;
using Godot;
using HarmonyLib;
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
/// <para>What the accepted design draws and this does not: a flat plate hung under the
/// pane, following the rows down. That is a scene this mod has no path to lay out
/// against furniture it cannot measure, so the same head, the same rows and the same
/// reason are shown in the game's own modal instead. Every sentence and every rule is
/// <see cref="RunHistoryPlate"/>'s.</para>
///
/// <para><b>The marks are derived and not drawn, and that is a gap rather than a
/// decision.</b> <see cref="RunHistoryPlate.Mark"/> answers for every state and nothing
/// here puts a glyph on screen, for two separate reasons. The design's record mark
/// belongs at a <em>run</em> row's end and the game builds one
/// <c>NMapPointHistoryEntry</c> per map point of one run, so this patch has no per-run
/// row to hang it on. And the plate is drawn in the game's own popup, whose head is a
/// plain string: a mark beside it would be a positioned control carrying art the mod's
/// glyph family does not have - it is the transport's set, and no record mark is in it.
/// Both are the same furniture-and-art gap the modal already stands in for, and the
/// states stay distinguishable because every one of them says what it is in words.
/// Drawing them is a change to <see cref="LibraryScreen"/> and to
/// <c>TransportGlyphArt</c>, and to nothing behind either.</para>
/// </summary>
internal static class RunHistoryPlateHost
{
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

    /// <summary>Shows the plate for the run one history entry belongs to.</summary>
    private static void Show(NMapPointHistoryEntry entry)
    {
        try
        {
            var history = HistoryOf(entry);
            var recording = history is null ? null : RecordingFor(history);
            if (PlateFor(history, recording) is not { } plate) return;

            LibraryScreen.Show(
                plate.Head,
                plate.Reason is { Length: > 0 } reason ? LibraryMarkup.Dim(reason) : string.Empty,
                Rows(plate, recording),
                LibraryCopy.Back);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read this run's recording: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
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
    /// The multiplayer answer comes from the game's own history rather than from a
    /// recording, and that is the point: there is no recording of a multiplayer run to
    /// read, because the recorder does not attach to one. Continuity and the recorded
    /// build come from the recording, which is the only thing that witnessed them.
    ///
    /// <para>Whether a console command was used is the recording's own answer, read
    /// through <c>NativeSource.StatesSomethingOtherThanComplete</c> - the owner of that
    /// reading, so the integrity values are compared in one place. A recording stating
    /// no integrity at all answers null rather than a clean run, because reporting one
    /// this never established is the claim <c>AGENTS.md</c> forbids; from format v6 the
    /// field is required and a version-5 file reads as <c>complete</c> through the
    /// migration, so no manifest this build parses reaches that answer.</para>
    ///
    /// <para>Whether there is a submit flow is this build's own answer and it is no:
    /// section 9.8 puts the flow outside this slice, so the row is drawn refused with a
    /// reason rather than drawn as an offer nothing honours.</para>
    /// </summary>
    internal static RunHistoryFacts FactsFor(RunHistory? history, ReplayManifest? recording) =>
        new(
            HasRecording: recording is not null,
            Multiplayer: history is not null && history.Players.Count > 1,
            Continuous: recording?.Source.Native?.IsContinuous ?? false,
            ConsoleUsed: recording?.Source.Native is { Integrity: not null } native
                ? native.StatesSomethingOtherThanComplete
                : null,
            RecordedBuild: recording?.Environment.BuildVersion.Value ?? string.Empty,
            ThisBuild: RunLibrary.ThisBuild(),
            RunInProgress: LocalEnvironment.ReadStartedRun() is not null,
            SubmitAvailable: false,
            LastFight: LastOf(recording, LibraryRun.ProvedFights),
            LastFloor: LastOf(recording, LibraryRun.ProvedFloors));

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
            var fight = row.Fight;
            var floor = row.Floor;
            rows.Add(new ScreenRow(
                row.Label,
                row.Enabled && id is not null,
                () => Enter(id!, fight, floor)));
        }

        return rows;
    }

    private static void Enter(string runId, int? fight, int? floor)
    {
        if (RunLibrary.RecordingFor(runId) is not { } recording)
        {
            throw new InvalidOperationException($"'{runId}' is not a run this library holds.");
        }

        if (fight is { } ordinal)
        {
            RunLibraryStore.RecordFightPlayed(runId, ordinal);
            _ = RecordedFightRun.Start(recording, RecordedFightPlan.For(recording, ordinal));
            return;
        }

        if (floor is { } atFloor)
        {
            _ = RecordedFightRun.Start(recording, FloorEntryPlan.For(recording, atFloor));
            return;
        }

        throw new InvalidOperationException(
            $"That row names no boundary of '{runId}', so there is nowhere to stand.");
    }

    private static int? LastOf(
        ReplayManifest? recording, Func<ReplayManifest, IReadOnlyList<int>> proved) =>
        recording is null ? null : proved(recording).Cast<int?>().LastOrDefault();

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
