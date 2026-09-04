using System.Globalization;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>Where a trainer run has got to.</summary>
internal enum RecordedFightPhase
{
    /// <summary>There is no trainer run.</summary>
    None,

    /// <summary>The run exists and the game is putting it on screen.</summary>
    Starting,

    /// <summary>The recording is making its decisions, on the game's own screens,
    /// and the player is watching.</summary>
    Watching,

    /// <summary>The fight has been proved to be the recorded one and is the
    /// player's. Every action they take is being sampled either side.</summary>
    InFight,

    /// <summary>The fight has ended and its result is on screen. The run still
    /// exists underneath until the player leaves.</summary>
    Result,
}

/// <summary>
/// The trainer's run inside the retail client: constructing it, walking it through
/// the recording's decisions, and handing the fight over once it has been shown to
/// be the recorded one.
///
/// Everything it decides is somebody else's. <see cref="RecordedFightEntry"/> owns
/// the construction, the ordered decisions and the proof; this owns when they happen
/// relative to the game's frames and what the player sees while they do. That split
/// is what lets the command line run the same journey without a scene tree.
///
/// Two things are enforced here and nowhere else, because both are about the retail
/// client specifically. The run is set up before the profile write barrier could
/// possibly be needed, not after. And while the recording is deciding, the game's
/// own commands for those decisions refuse anybody but this class - a player who
/// clicked a different blessing would be in a different run, and the comparison this
/// whole proof exists for would have nothing to compare.
/// </summary>
internal static class RecordedFightRun
{
    private static RecordedFightEntry? _entry;
    private static PlayerFightObserver? _observer;
    private static FightResultScreen? _resultAfterMainMenu;

    /// <summary>
    /// Set only while this class is issuing one of the recording's own decisions, so
    /// the lock below can tell it from a player's click.
    ///
    /// Held across the whole step rather than around the call that starts it, and not
    /// thread-static. A screen's command is asynchronous - the map screen fades, and
    /// only then enters the coordinate - so an authorisation that ended when the
    /// starting call returned had already lapsed by the time the command it was
    /// authorising ran, and the lock refused the recording's own move. Everything here
    /// runs on the game's one main thread, which is what makes a plain flag the honest
    /// way to say "this step is ours" rather than a per-thread one that quietly does
    /// not cover its own continuation.
    /// </summary>
    private static bool _authorising;

    /// <summary>How long to let the game run before reading the fight it is opening.
    /// A fight that opens slowly is fine; a boundary read half-open is not, and the
    /// refusal says what it saw either way.</summary>
    private const double OpeningTheFightSeconds = 2.0;

    /// <summary>How long to let the game finish the fight's ending - the last enemy's
    /// death, the loot appearing, the death screen - before the result goes over it.
    /// The result is computed before the wait; only the drawing is deferred.</summary>
    private const double EndingTheFightSeconds = 2.0;

    internal static RecordedFightPhase Phase { get; private set; } = RecordedFightPhase.None;

    /// <summary>
    /// Whether the recording, rather than the player, owns the decisions the game is
    /// currently asking for.
    ///
    /// <see cref="RecordedFightPhase.Starting"/> is deliberately not one of them, and
    /// this is load-bearing rather than a tidy boundary: the game's own
    /// <c>RunManager.EnterAct</c> enters the act's starting node with the same
    /// <c>EnterMapCoord</c> the lock below guards, while the run is still being put on
    /// screen. Locking then would stop the run entering its first room at all. There
    /// is nothing to lock during that phase either - no screen is up and no player has
    /// anything to click.
    /// </summary>
    internal static bool IsWatching => Phase == RecordedFightPhase.Watching;

    /// <summary>
    /// Starts the recording's run and walks it to the fight.
    ///
    /// Async and never awaited by its caller: the button that starts it returns to
    /// the game immediately, and everything after that happens on the game's own
    /// frames. Every failure ends the attempt and says why on screen rather than
    /// leaving a half-built run behind.
    /// </summary>
    internal static async Task Start(ReplayManifest recording)
    {
        if (Phase != RecordedFightPhase.None)
        {
            Log.Warn($"[{RunmobileMod.ModId}] a recorded fight is already under way; ignoring.", 2);
            return;
        }

        // Raised before the run exists rather than after, so there is no moment in
        // which a trainer run could reach a write.
        ProfileWriteBarrier.Raise();
        Phase = RecordedFightPhase.Starting;

        try
        {
            var creator = RecordingIdentity.Creator(recording);
            _entry = RecordedFightEntry.PrepareInRunningGame(recording, TravelOnTheGamesMapScreen);

            // Awaiting the game's own start-run task is what puts the run on screen and
            // in its first room; the task completes when it has.
            await LaunchThroughTheGame(_entry.PreparedRun);

            Phase = RecordedFightPhase.Watching;
            Log.Info(
                $"[{RunmobileMod.ModId}] constructed the recording's run; watching " +
                $"{_entry.Plan.PrefixActions.Count.ToString(CultureInfo.InvariantCulture)} recorded " +
                "decision(s) before the fight", 2);
            ShowWhenTheGameHasFinishedMoving(creator, _entry);
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
        }
    }

    /// <summary>Makes the recording's next decision, then shows the next one.</summary>
    private static async void Next()
    {
        try
        {
            await AdvanceOne();
            if (_entry is { AtBoundary: false } entry)
            {
                ShowWhenTheGameHasFinishedMoving(RecordingIdentity.Creator(entry.Manifest), entry);
                return;
            }

            HandOverWhenTheGameHasFinishedMoving();
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
        }
    }

    /// <summary>Makes every remaining recorded decision, without stopping between
    /// them. The same decisions in the same order; only the pauses go.</summary>
    private static async void SkipToTheFight()
    {
        try
        {
            while (_entry is { AtBoundary: false }) await AdvanceOne();
            HandOverWhenTheGameHasFinishedMoving();
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
        }
    }

    private static async Task AdvanceOne()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");

        PrefightScreen.Close();
        var step = entry.StepsTaken + 1;
        _authorising = true;
        try
        {
            entry.AdvanceOneStep();

            Log.Info(
                $"[{RunmobileMod.ModId}] made recorded decision " +
                $"{step.ToString(CultureInfo.InvariantCulture)} of " +
                $"{entry.Plan.PrefixActions.Count.ToString(CultureInfo.InvariantCulture)}", 2);

            // The engine's own task for the decision, where it has one. Awaiting the
            // game's task is the one kind of waiting this host can do: the game
            // completes it when the work is done. The authorisation is still held,
            // because a screen's command does most of its work inside this task.
            if (entry.Pending is { } pending) await pending;
        }
        finally
        {
            _authorising = false;
        }

        CarryOnPastAnyScreenWaitingToProceed();
    }

    /// <summary>
    /// Clicks past the screens between one recorded decision and the next.
    ///
    /// The recording contains the decisions and not the screen transitions, so after
    /// an event option is chosen the game sits on its own "Proceed" until somebody
    /// carries on. Bounded rather than looped freely: two of these in a row is
    /// already more than this journey meets, and a host that would press onward
    /// indefinitely is a host that could walk a run somewhere nobody asked for.
    /// </summary>
    private static void CarryOnPastAnyScreenWaitingToProceed()
    {
        for (var dismissed = 0; dismissed < 2; dismissed++)
        {
            if (_entry is not { } entry) return;

            bool carriedOn;
            string observed;
            _authorising = true;
            try
            {
                carriedOn = CarryOnPastTheGamesOwnProceed(out observed);
            }
            finally
            {
                _authorising = false;
            }

            if (!carriedOn)
            {
                Log.Info($"[{RunmobileMod.ModId}] nothing to carry on past: {observed}", 2);
                return;
            }

            Log.Info($"[{RunmobileMod.ModId}] carried on past a screen that was waiting to proceed", 2);
        }
    }

    /// <summary>
    /// Moves on the map the way a clicked node does.
    ///
    /// `NMapScreen.TravelToMapCoord` is the game's own command for a map move, and the
    /// engine's `EnterMapCoord` is the middle of it: the screen fades out around the
    /// call, and the room the run entered is only put on screen by that transition.
    /// Measured - calling the middle alone left the client on the map with a combat
    /// built behind it that never dealt its opening hand.
    /// </summary>
    private static Task TravelOnTheGamesMapScreen(MapCoord coord)
    {
        var map = NMapScreen.Instance
            ?? throw new InvalidOperationException(
                "The recording moves on the map, and this game has no map screen to move on.");

        return map.TravelToMapCoord(coord);
    }

    /// <summary>
    /// Presses the game's own continue, where that is all the screen is offering.
    ///
    /// Not a decision and not a verb: S2.5 decided a screen transition is not
    /// something the run contains, which leaves it for a host to drive. Where the
    /// engine keeps it took measuring - the event model's own option list is empty by
    /// this point, and the continue is a button the screen is holding - so this asks
    /// the screen, and calls the method the game calls when a player clicks one of
    /// those buttons.
    ///
    /// It refuses to decide anything: only where the continue is the single button
    /// left. A screen with a real choice on it belongs to the recording.
    /// </summary>
    private static bool CarryOnPastTheGamesOwnProceed(out string observed)
    {
        if (NEventRoom.Instance is not { } room)
        {
            observed = "no event screen is up";
            return false;
        }

        if (room.Layout is not { } layout)
        {
            observed = "the event screen has no layout to read";
            return false;
        }

        var buttons = layout.OptionButtons.ToList();
        observed = buttons.Count == 0
            ? "the event screen offers no buttons"
            : "the event screen offers " + string.Join(", ", buttons.Select(button =>
                $"{button.Option?.TextKey ?? "?"}{(button.Option is { IsProceed: true } ? " (proceed)" : string.Empty)}"));

        if (buttons.Count != 1 || buttons[0].Option is not { IsProceed: true } proceed) return false;

        room.OptionButtonClicked(proceed, 0);
        return true;
    }

    /// <summary>
    /// Puts the next panel up once the game has finished the frame it is in.
    ///
    /// Deferral rather than a wait, and the distinction is the whole of what the
    /// retail client taught this class. A decision resolves while the screen it
    /// changed is still rebuilding, and a panel added into the middle of that is
    /// created, logs its focus grab and is gone by the next frame - which is exactly
    /// what the second panel did. Waiting the transition out is the obvious repair and
    /// the one this process cannot do: nothing this mod ticks is ever called, measured
    /// three ways, so a wait for a frame that never arrives is a journey that stops
    /// with nothing in the log. <c>CallDeferred</c> is the one scheduling primitive
    /// proved to run here - the popup's own focus grab already relies on it - and
    /// end-of-frame is after the transition the decision started.
    /// </summary>
    private static void ShowWhenTheGameHasFinishedMoving(string creator, RecordedFightEntry entry) =>
        Callable.From(() =>
        {
            try
            {
                PrefightScreen.Show(creator, entry, Next, SkipToTheFight);
            }
            catch (Exception ex)
            {
                Abandon(ex.Message);
            }
        }).CallDeferred();

    /// <summary>
    /// Hands the fight over once the game has finished opening it.
    ///
    /// A deferral that re-defers itself, which is this host's frame loop: the one
    /// scheduling primitive proved to run in the client, applied once per frame until
    /// the fight is ready or the budget runs out. Needed rather than tidy - the map
    /// move's task completes when the combat room is built, and the opening hand is
    /// dealt over the frames after that, so the boundary asked immediately reads an
    /// empty hand and refuses a fight that is merely a moment young.
    /// </summary>
    private static async void HandOverWhenTheGameHasFinishedMoving()
    {
        try
        {
            var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");
            Log.Info(
                $"[{RunmobileMod.ModId}] letting the fight open; {entry.DescribeCombatReadiness()}", 2);

            await LetTheGameRun(OpeningTheFightSeconds);

            Log.Info(
                $"[{RunmobileMod.ModId}] after letting the game run; " +
                $"{_entry?.DescribeCombatReadiness() ?? "no run"}", 2);

            HandOverTheFight();
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
        }
    }

    /// <summary>
    /// Gives the game back to itself for a moment, on the scene tree's own timer.
    ///
    /// The measurement that produced this is worth keeping: a deferral that re-defers
    /// itself is not a frame loop. Godot drains its deferred queue until it is empty,
    /// so the loop ran seven thousand times in about eight seconds without the game
    /// drawing once - and the thing it was waiting for was the fight opening, which
    /// needs those frames. The wait was what prevented it. A timer hands the frames
    /// back.
    /// </summary>
    internal static async Task LetTheGameRun(double seconds)
    {
        var tree = Godot.Engine.GetMainLoop() as SceneTree
            ?? throw new InvalidOperationException("This process has no scene tree to wait in.");

        await tree.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>
    /// Proves the fight is the recorded one, and only then gives it to the player.
    ///
    /// The last gate, and the only place the phase becomes the player's. A refusal
    /// here abandons the run: a fight that opened somewhere else is a fight nothing
    /// downstream could compare, and leaving somebody in it would be the confident
    /// wrong answer this project exists to prevent.
    /// </summary>
    private static void HandOverTheFight()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");

        // No waiting for the fight to exist. The map move's own engine task is awaited
        // before this, and the game completes it when the room it entered is built, so
        // a run that is not in a fight here is not one that needs another frame - it is
        // one that did not reach the recording's fight, and CombatStartEquality says so.
        var equality = entry.VerifyBoundary();
        if (!equality.Matches)
        {
            Abandon(equality.Refusal ?? "This fight is not the recorded one.");
            return;
        }

        PrefightScreen.Close();
        Phase = RecordedFightPhase.InFight;
        Log.Info(
            $"[{RunmobileMod.ModId}] standing in the recorded fight; canonical state at combat start is " +
            $"{equality.ActualDigest}", 2);

        // The capture begins at the boundary just proved, and from nowhere else: it
        // carries the digest the comparison will require to be the recording's.
        var capture = entry.BeginCapture(equality);
        _observer = PlayerFightObserver.Start(entry, capture, TheFightEnded);
    }

    /// <summary>
    /// The player's fight is over. Computes what the result screen says, then puts
    /// it up once the game has finished drawing the ending.
    ///
    /// Computed first and shown later, on purpose. On a loss the game's own flow
    /// tears the run down on its way to the death screen, and this entry with it; a
    /// result computed after that would have nothing to read. The screen is data, so
    /// it survives the run it describes.
    /// </summary>
    private static async void TheFightEnded()
    {
        try
        {
            var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");
            var capture = entry.Capture
                ?? throw new InvalidOperationException("The fight ended before its capture began.");
            var screen = FightResultScreen.Of(
                RecordingIdentity.Creator(entry.Manifest), capture,
                CombatTrainerModule.Instance.RecordedFights.Projection(entry.Fight));
            _observer?.Dispose();
            _observer = null;
            Phase = RecordedFightPhase.Result;
            Log.Info(
                $"[{RunmobileMod.ModId}] result: " +
                (screen.HasComparison ? $"comparison, {screen.Rows.Count} row(s)" : screen.Notice), 2);

            await LetTheGameRun(EndingTheFightSeconds);
            PrefightScreen.ShowResult(screen, LeaveTheFight);
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
        }
    }

    /// <summary>
    /// Done. The run is discarded the way a refused entry's is: it was never the
    /// player's, it is never saved, and the game's own end-of-run path is what lowers
    /// the write barrier.
    /// </summary>
    private static void LeaveTheFight()
    {
        PrefightScreen.Close();
        try
        {
            if (RunManager.Instance is { IsInProgress: true }) RunManager.Instance.CleanUp();
            NGame.Instance?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not leave the finished fight: {ex.GetType().Name}: {ex.Message}",
                2);
        }
        finally
        {
            Finish();
        }
    }

    /// <summary>
    /// Tears the attempt down and says why.
    ///
    /// The run goes with it. A trainer run left in place after a refusal would be a
    /// run the player did not start, that the game would go on drawing, and that the
    /// barrier would go on suppressing writes for.
    /// </summary>
    private static void Abandon(string reason)
    {
        Log.Error($"[{RunmobileMod.ModId}] not entering the recorded fight: {reason}", 2);

        try
        {
            PrefightScreen.ShowRefusal(reason);

            if (RunManager.Instance is { IsInProgress: true }) RunManager.Instance.CleanUp();
            NGame.Instance?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not clear the refused run: {ex.GetType().Name}: {ex.Message}",
                2);
        }
        finally
        {
            Finish();
        }
    }

    /// <summary>
    /// Hands the run to the game's own start-run continuation.
    ///
    /// <c>NGame.StartRun</c> is the second half of the retail client's own
    /// <c>StartNewSingleplayerRun</c>: it preloads the run's assets, finalises the
    /// starting relics, launches, puts the run's scene on screen and enters the first
    /// act. Reached by name because it is private, and refused loudly when a build no
    /// longer has it - reimplementing those six steps would be this mod doing the
    /// game's job worse.
    /// </summary>
    private static Task LaunchThroughTheGame(RunState runState)
    {
        var game = NGame.Instance
            ?? throw new InvalidOperationException("This process has no game to start a run in.");

        var startRun = typeof(NGame).GetMethod(
            "StartRun", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                "NGame has no StartRun on this build, so the recording's run cannot be launched the way the " +
                "game launches its own.");

        return startRun.Invoke(game, [runState]) as Task
            ?? throw new InvalidOperationException("NGame.StartRun did not return a task on this build.");
    }

    internal static void Finish()
    {
        var entry = _entry;
        var observer = _observer;
        _entry = null;
        _observer = null;
        _authorising = false;
        Phase = RecordedFightPhase.None;
        try
        {
            // Disposing the entry abandons a capture still live, so a fight left
            // through the game's own menu is never read as one that finished.
            observer?.Dispose();
            entry?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not dispose the recorded fight entry: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
        finally
        {
            ProfileWriteBarrier.Lower();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    internal static class TrainerRunTeardown
    {
        [HarmonyPostfix]
        internal static void AfterRunEnds()
        {
            if (Phase == RecordedFightPhase.InFight)
            {
                _resultAfterMainMenu = FightResultScreen.Left();
                Finish();
                return;
            }

            if (Phase != RecordedFightPhase.None || ProfileWriteBarrier.IsActive) Finish();
        }
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame.ReturnToMainMenu))]
    internal static class MainMenuReturn
    {
        [HarmonyPostfix]
        internal static void AfterReturnStarts(Task __result) => ShowResultAfterReturn(__result);
    }

    private static async void ShowResultAfterReturn(Task returning)
    {
        try
        {
            await returning;
            if (_resultAfterMainMenu is not { } screen) return;

            _resultAfterMainMenu = null;
            PrefightScreen.ShowResult(screen, CloseResultOnMainMenu);
        }
        catch (Exception ex)
        {
            _resultAfterMainMenu = null;
            Log.Error(
                $"[{RunmobileMod.ModId}] could not show the abandoned fight result: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static void CloseResultOnMainMenu()
    {
        PrefightScreen.Close();
        Finish();
    }

    /// <summary>
    /// Keeps the decisions before the fight the recording's.
    ///
    /// Two prefixes on the two commands those decisions reach, which is where a lock
    /// belongs: a screen with its buttons hidden is a screen a controller, a hotkey
    /// or a mod can still reach, and the command is the thing that would actually
    /// change the run. While the recording is deciding, only this class may issue
    /// them; at every other moment - the player's own runs included - neither patch
    /// does anything.
    /// </summary>
    [HarmonyPatch]
    internal static class DeviationLock
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
        internal static bool OnlyTheRecordingChoosesAnEventOption() => Allowed("an event option");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
        internal static bool OnlyTheRecordingEntersAMapNode(ref Task? __result)
        {
            if (Allowed("a map node")) return true;
            __result = Task.CompletedTask;
            return false;
        }

        private static bool Allowed(string what)
        {
            if (!IsWatching || _authorising) return true;

            Log.Info(
                $"[{RunmobileMod.ModId}] ignoring an attempt to choose {what}: the recording owns every " +
                "decision before its fight.", 2);
            return false;
        }
    }
}
