using System.Reflection;
using Godot;
using HarmonyLib;
using System.Globalization;
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
            Log.Info(
                $"[{CombatTrainerMod.ModId}] constructed the recording's run; watching " +
                $"{_entry.Plan.PrefightActions.Count.ToString(CultureInfo.InvariantCulture)} recorded " +
                "decision(s) before the fight", 2);
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
        var step = entry.StepsTaken + 1;
        _authorising = true;
        try
        {
            entry.AdvanceOneStep();
        }
        finally
        {
            _authorising = false;
        }

        Log.Info(
            $"[{CombatTrainerMod.ModId}] made recorded decision " +
            $"{step.ToString(CultureInfo.InvariantCulture)} of " +
            $"{entry.Plan.PrefightActions.Count.ToString(CultureInfo.InvariantCulture)}", 2);

        if (entry.Pending is { } pending) await pending;
        await Settle();
        await CarryOnPastAnyScreenWaitingToProceed();
        await (entry.AtCombatStart ? Settle() : SettleUntilTheNextDecisionCanBeDescribed());
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
    private static async Task CarryOnPastAnyScreenWaitingToProceed()
    {
        for (var dismissed = 0; dismissed < 2; dismissed++)
        {
            if (_entry is not { } entry) return;

            bool carriedOn;
            _authorising = true;
            try
            {
                carriedOn = entry.DismissScreenWaitingToProceed();
            }
            finally
            {
                _authorising = false;
            }

            if (!carriedOn) return;

            Log.Info($"[{CombatTrainerMod.ModId}] carried on past a screen that was waiting to proceed", 2);
            await Settle();
        }
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
    private static Task Settle() => WaitForFrames(
        () => RunManager.Instance?.ActionExecutor is not { IsRunning: true },
        "finish what the recording's last decision started");

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
        WaitForFrames(
            () =>
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
        },
            "reach the screen the recording's next decision is about");

    /// <summary>The same, waiting for the fight itself. The engine reports a combat
    /// live only once it has built it, and comparing the boundary before then would
    /// compare an empty room.</summary>
    private static Task SettleUntilTheFightIsLive() =>
        WaitForFrames(
            () => RunManager.Instance?.ActionExecutor is not { IsRunning: true } &&
                  CombatManager.Instance is { IsInProgress: true },
            "reach the recording's fight");

    /// <summary>
    /// Waits for the game to reach a condition, on the game's own frames.
    ///
    /// UNVERIFIED IN THE RETAIL CLIENT, and the next person to touch this should read
    /// why before trying a fourth variant. The journey makes its first recorded
    /// decision correctly and then stops: no further step runs, no timeout fires, and
    /// nothing is logged, which is what a wait that is never ticked looks like.
    /// Three mechanisms have been tried against the running game and none has been
    /// observed ticking - an await on the scene tree's <c>ProcessFrame</c> signal, a
    /// delegate on that signal's C# event, and this node's <c>_Process</c>. That they
    /// all fail the same way says the fault is more likely in how this mod's
    /// continuations are scheduled inside the game's process than in any one of them,
    /// so the next attempt should establish that a tick happens at all - a log line
    /// from <c>_Process</c> - before building anything on top of it.
    /// </summary>
    private static Task WaitForFrames(Func<bool> until, string what)
    {
        if (until()) return Task.CompletedTask;
        return JourneyPump.Attached().Wait(until, what, SettleFrameBudget);
    }

    /// <summary>
    /// The node that ticks the journey's waits.
    ///
    /// One, kept in a static field and left in the tree for the session: adding and
    /// removing a node around every wait is more moving parts than a mod needs, and
    /// an unattached node is the failure this class exists to avoid. It does nothing
    /// at all unless something is waiting on it.
    /// </summary>
    private sealed partial class JourneyPump : Node
    {
        private static JourneyPump? _attached;

        private Func<bool>? _until;
        private TaskCompletionSource? _completion;
        private string _what = string.Empty;
        private int _budget;
        private int _frames;

        internal static JourneyPump Attached()
        {
            if (_attached is { } existing) return existing;

            var tree = Godot.Engine.GetMainLoop() as SceneTree
                ?? throw new InvalidOperationException("This process has no scene tree to wait in.");

            var pump = new JourneyPump { Name = "CombatTrainerJourneyPump" };
            pump.SetProcess(true);

            // Deferred: this is reached from inside a button's signal callback, and
            // the tree refuses to be restructured while it is delivering one.
            tree.Root.CallDeferred(Node.MethodName.AddChild, pump);
            _attached = pump;
            return pump;
        }

        internal Task Wait(Func<bool> until, string what, int budget)
        {
            if (_completion is not null)
            {
                throw new InvalidOperationException(
                    "The recorded journey is already waiting for something; it does not wait for two things " +
                    "at once.");
            }

            _until = until;
            _what = what;
            _budget = budget;
            _frames = 0;
            _completion = new TaskCompletionSource();
            return _completion.Task;
        }

        public override void _Process(double delta)
        {
            if (_completion is not { } completion || _until is not { } until) return;

            try
            {
                if (until())
                {
                    Finish();
                    completion.SetResult();
                    return;
                }

                if (++_frames < _budget) return;

                var what = _what;
                var budget = _budget;
                Finish();
                completion.SetException(new InvalidOperationException(
                    $"The game did not {what} within {budget.ToString(CultureInfo.InvariantCulture)} frames. " +
                    "Refusing to carry on from a half-applied state."));
            }
            catch (Exception ex)
            {
                Finish();
                completion.SetException(ex);
            }
        }

        private void Finish()
        {
            _until = null;
            _completion = null;
            _frames = 0;
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
