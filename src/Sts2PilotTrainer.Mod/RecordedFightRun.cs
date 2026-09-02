using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
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
    /// player's.</summary>
    InFight,
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
    /// <summary>How many frames to let the engine settle in before giving up on it.
    /// Generous: this is a correctness backstop, and a journey that took an extra
    /// second is fine where one that carried on from a half-applied state is not.</summary>
    private const int SettleFrameBudget = 600;

    private static RecordedFightEntry? _entry;

    /// <summary>Set only while this class is issuing one of the recording's own
    /// decisions, so the lock below can tell it from a player's click.</summary>
    [ThreadStatic]
    private static bool _authorising;

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
            Log.Warn($"[{CombatTrainerMod.ModId}] a recorded fight is already under way; ignoring.", 2);
            return;
        }

        // Raised before the run exists rather than after, so there is no moment in
        // which a trainer run could reach a write.
        ProfileWriteBarrier.Raise();
        Phase = RecordedFightPhase.Starting;

        try
        {
            var creator = RecordingIdentity.Creator(recording);
            _entry = RecordedFightEntry.PrepareInRunningGame(recording);
            await LaunchThroughTheGame(_entry.PreparedRun);
            await SettleUntilTheNextDecisionCanBeDescribed();

            Phase = RecordedFightPhase.Watching;
            PrefightScreen.Show(creator, _entry, Next, SkipToTheFight);
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
            if (_entry is { AtCombatStart: false } entry)
            {
                PrefightScreen.Show(RecordingIdentity.Creator(entry.Manifest), entry, Next, SkipToTheFight);
                return;
            }

            await HandOverTheFight();
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
            while (_entry is { AtCombatStart: false }) await AdvanceOne();
            await HandOverTheFight();
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
        _authorising = true;
        try
        {
            entry.AdvanceOneStep();
        }
        finally
        {
            _authorising = false;
        }

        if (entry.Pending is { } pending) await pending;
        await (entry.AtCombatStart ? Settle() : SettleUntilTheNextDecisionCanBeDescribed());
    }

    /// <summary>
    /// Proves the fight is the recorded one, and only then gives it to the player.
    ///
    /// The last gate, and the only place the phase becomes the player's. A refusal
    /// here abandons the run: a fight that opened somewhere else is a fight nothing
    /// downstream could compare, and leaving somebody in it would be the confident
    /// wrong answer this project exists to prevent.
    /// </summary>
    private static async Task HandOverTheFight()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");

        await SettleUntilTheFightIsLive();

        var equality = entry.VerifyCombatStart();
        if (!equality.Matches)
        {
            Abandon(equality.Refusal ?? "This fight is not the recorded one.");
            return;
        }

        PrefightScreen.Close();
        Phase = RecordedFightPhase.InFight;
        Log.Info(
            $"[{CombatTrainerMod.ModId}] standing in the recorded fight; canonical state at combat start is " +
            $"{equality.ActualDigest}", 2);
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
        Log.Error($"[{CombatTrainerMod.ModId}] not entering the recorded fight: {reason}", 2);

        try
        {
            PrefightScreen.ShowRefusal(reason);

            // CleanUp is postfixed by TrainerRunTeardown, which clears the same state
            // this method's finally clears. Both are idempotent, and having the
            // teardown on the game's own end-of-run path rather than only here is
            // what covers the ways a run ends that never come through this method.
            if (RunManager.Instance is { IsInProgress: true }) RunManager.Instance.CleanUp();
            NGame.Instance?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{CombatTrainerMod.ModId}] could not clear the refused run: {ex.GetType().Name}: {ex.Message}",
                2);
        }
        finally
        {
            _entry?.Dispose();
            _entry = null;
            Phase = RecordedFightPhase.None;
            ProfileWriteBarrier.Lower();
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

    /// <summary>Lets the engine finish the work a decision queued, on the game's own
    /// frames.</summary>
    private static Task Settle() => WaitForFrames(() => RunManager.Instance?.ActionExecutor is not { IsRunning: true });

    /// <summary>
    /// The same, waiting until the screen the recording's next decision is about
    /// actually exists.
    ///
    /// An idle action queue is not the same thing as a built room: the game finishes
    /// entering an act over several frames, and the event whose option the recording
    /// took is one of the things it builds. Asking the entry to describe the decision
    /// is the honest test of whether it can be shown yet, because it is the same
    /// question the screen is about to ask.
    /// </summary>
    private static Task SettleUntilTheNextDecisionCanBeDescribed() =>
        WaitForFrames(() =>
        {
            if (RunManager.Instance?.ActionExecutor is { IsRunning: true }) return false;
            if (_entry is not { } entry) return false;

            try
            {
                _ = entry.DescribeNextStep();
                return true;
            }
            catch (EngineException)
            {
                // Not yet. The same call is what the screen makes a moment later, so a
                // failure that outlasts the frame budget surfaces there as its own
                // refusal rather than being swallowed here.
                return false;
            }
        });

    /// <summary>The same, waiting for the fight itself. The engine reports a combat
    /// live only once it has built it, and comparing the boundary before then would
    /// compare an empty room.</summary>
    private static Task SettleUntilTheFightIsLive() =>
        WaitForFrames(() =>
            RunManager.Instance?.ActionExecutor is not { IsRunning: true } &&
            CombatManager.Instance is { IsInProgress: true });

    private static async Task WaitForFrames(Func<bool> until)
    {
        var tree = Godot.Engine.GetMainLoop() as SceneTree
            ?? throw new InvalidOperationException("This process has no frame loop to wait on.");

        for (var frame = 0; frame < SettleFrameBudget; frame++)
        {
            if (until()) return;
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        if (until()) return;

        throw new InvalidOperationException(
            "The game did not finish what the recording's last decision started. Refusing to carry on from a " +
            "half-applied state.");
    }

    /// <summary>
    /// Forgets the trainer run once the game has torn it down, whichever way it
    /// ended.
    ///
    /// Load-bearing rather than tidy. The barrier stops every write while a trainer
    /// run is live, and a player who finished or abandoned the recorded fight is back
    /// in their own game a moment later - so a barrier that stayed raised would
    /// silently stop saving their next run. `RunManager.CleanUp` is where a run
    /// stops existing, on every path there is: quitting to the menu, losing, and
    /// abandoning. Postfixed rather than replaced, and it does nothing at all unless
    /// the run being torn down is the trainer's.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    internal static class TrainerRunTeardown
    {
        [HarmonyPostfix]
        internal static void TheTrainerRunIsOver()
        {
            if (Phase == RecordedFightPhase.None) return;

            try
            {
                PrefightScreen.Close();
                _entry?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[{CombatTrainerMod.ModId}] could not clear the recorded fight after the run ended: " +
                    $"{ex.GetType().Name}: {ex.Message}", 2);
            }
            finally
            {
                _entry = null;
                Phase = RecordedFightPhase.None;
                ProfileWriteBarrier.Lower();
                Log.Info(
                    $"[{CombatTrainerMod.ModId}] the recorded fight's run is over; saving behaves normally " +
                    "again", 2);
            }
        }
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
                $"[{CombatTrainerMod.ModId}] ignoring an attempt to choose {what}: the recording owns every " +
                "decision before its fight.", 2);
            return false;
        }
    }
}
