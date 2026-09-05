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

    /// <summary>The decisions the recording has already made, in order, described at
    /// the moment each one was revealed. Kept because Back re-shows a decision whose
    /// screen has since gone: the run cannot be asked about it again, and it must
    /// never be rewound to answer.</summary>
    private static readonly List<PrefightChoice> Shown = [];

    /// <summary>Which already-made decision the player is looking back at, counted
    /// from one, or null while the transport is on the decision about to happen.</summary>
    private static int? _lookingBackAt;

    /// <summary>Whether Play is running the sequence.</summary>
    private static bool _playing;

    /// <summary>Whether a decision is being made right now. A commit spans several of
    /// the game's frames, and every control on the strip stays pressable during them;
    /// two commits overlapping would put the plan's steps out of order.</summary>
    private static bool _committing;

    /// <summary>Whether the once-per-run sentence about how to read these screens has
    /// been said.</summary>
    private static bool _noteShown;

    /// <summary>
    /// How fast Play runs, as a position in <see cref="PlaybackSpeeds.All"/> rather
    /// than as the enum itself.
    ///
    /// The indirection is not style. The game enumerates this assembly's types with
    /// <c>Module.GetTypes()</c> before it calls the mod initializer, and computing a
    /// type's field layout forces its value types to load - so a field of an enum
    /// from a sibling assembly makes the whole mod fail to load, one startup phase
    /// before <see cref="SiblingAssemblies"/> has taught the runtime where its
    /// siblings are. A reference-typed field is fine, because its layout is a pointer.
    /// Measured: it took the mod down with a ReflectionTypeLoadException naming
    /// Sts2PilotTrainer.Trainer. See docs/in-game-host.md.
    /// </summary>
    private static int _speedIndex = 1;

    /// <summary>
    /// Which hold is current.
    ///
    /// A hold is a timer the game runs, and a timer cannot be recalled. Pause, Back
    /// and a commit that happened some other way all have to leave a hold already in
    /// flight unable to act, so every one of them moves this on and a hold that wakes
    /// up on an old number does nothing.
    /// </summary>
    private static int _hold;

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

    /// <summary>How long to give the fight to finish opening before giving up on it.
    /// A fight that opens slowly is fine; a boundary read half-open is not, and the
    /// refusal says what it saw either way.</summary>
    private const double OpeningTheFightSeconds = 20.0;

    /// <summary>How often the wait above looks at the fight it is waiting for.</summary>
    private const double OpeningTheFightPollSeconds = 0.1;

    /// <summary>How long to let the game finish the fight's ending - the last enemy's
    /// death, the loot appearing, the death screen - before the result goes over it.
    /// The result is computed before the wait; only the drawing is deferred.</summary>
    private const double EndingTheFightSeconds = 2.0;

    /// <summary>
    /// Where the journey has got to, held as a number.
    ///
    /// An <c>int</c> for the reason <see cref="_speedIndex"/> records: the phase enum
    /// lives in the Trainer, where the transport's derivation can be tested without a
    /// game, and a static field of a sibling assembly's value type makes the whole mod
    /// fail to load one startup phase before the runtime knows where its siblings are.
    /// Reading it back is a cast, which happens when the property runs rather than
    /// when this type's layout is computed.
    /// </summary>
    private static int _phase;

    /// <summary>
    /// Whether the recording's next decision is on the game's own screen yet.
    ///
    /// Cleared before a decision is committed and set once the next one has been
    /// revealed, with a re-derivation on each. Between those two points Step is
    /// refused: the window is a screen transition long, and a step taken inside it
    /// would make the next decision without anybody having been shown it, which is
    /// exactly what reveal, hold and commit exists to prevent.
    /// </summary>
    private static bool _revealed;

    internal static JourneyPhase Phase => (JourneyPhase)_phase;

    /// <summary>
    /// Moves the journey to a phase and re-derives what the transport says.
    ///
    /// The only way the phase changes. Every surface the mod draws is a function of
    /// the phase and the run's facts, so a phase set without this is a surface left
    /// saying what the last phase said - which is how a chip came to state a reason
    /// that had stopped being true and a menu came to hang under a tag that no longer
    /// offered it.
    /// </summary>
    private static void Transition(JourneyPhase phase)
    {
        _phase = (int)phase;
        ShowTransport();
    }

    /// <summary>
    /// Whether the recording, rather than the player, owns the decisions the game is
    /// currently asking for.
    ///
    /// <see cref="JourneyPhase.Starting"/> is deliberately not one of them, and
    /// this is load-bearing rather than a tidy boundary: the game's own
    /// <c>RunManager.EnterAct</c> enters the act's starting node with the same
    /// <c>EnterMapCoord</c> the lock below guards, while the run is still being put on
    /// screen. Locking then would stop the run entering its first room at all. There
    /// is nothing to lock during that phase either - no screen is up and no player has
    /// anything to click.
    /// </summary>
    internal static bool IsWatching => Phase == JourneyPhase.Watching;

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
        if (Phase != JourneyPhase.None)
        {
            Log.Warn($"[{RunmobileMod.ModId}] a recorded fight is already under way; ignoring.", 2);
            return;
        }

        // Raised before the run exists rather than after, so there is no moment in
        // which a trainer run could reach a write.
        ProfileWriteBarrier.Raise();
        Transition(JourneyPhase.Starting);

        RecordedFightEntry? entry = null;
        try
        {
            var creator = RecordingIdentity.Creator(recording);
            entry = RecordedFightEntry.PrepareInRunningGame(recording, TravelOnTheGamesMapScreen);
            _entry = entry;

            // Awaiting the game's own start-run task is what puts the run on screen and
            // in its first room; the task completes when it has.
            await LaunchThroughTheGame(entry.PreparedRun);

            if (!StillOurs(entry)) return;

            Transition(JourneyPhase.Watching);
            SweepWhileTheGameIsBetweenScreens();
            Log.Info(
                $"[{RunmobileMod.ModId}] constructed {creator}'s run; watching " +
                $"{entry.Plan.PrefixActions.Count.ToString(CultureInfo.InvariantCulture)} recorded " +
                "decision(s) before the fight", 2);
            RevealWhenTheGameHasFinishedMoving();
        }
        catch (Exception ex)
        {
            // The entry is still null where preparation itself failed, and that
            // refusal is this journey's own to report.
            if (entry is null || StillOurs(entry)) Abandon(ex);
        }
    }

    // ── The transport ──────────────────────────────────────────────────────
    //
    // Reveal, hold, commit. The reveal applies the game's own selected state to what
    // the recording is about to choose; the hold is the strip waiting, for the player
    // under Forward and for a timer under Play; the commit is the same call the popup
    // made before this existed. Back is not part of the cycle at all - it re-shows a
    // decision already made, and there is no path here that uncommits one.

    /// <summary>
    /// How long Play holds on a revealed decision before committing it.
    ///
    /// It has to be longer than the game's own select effect, because the watcher did
    /// not do the thinking and is reading the screen rather than confirming a
    /// conclusion they already reached.
    /// </summary>
    private const double HoldSeconds = 1.6;

    /// <summary>
    /// The same, on the map, where the game supplies a second of its own.
    ///
    /// Measured from the client's code rather than chosen: a map node's select effect
    /// holds for a second before the fade, so a hold of this length plus that one is
    /// the same pause as anywhere else.
    /// </summary>
    private const double MapHoldSeconds = 0.7;

    /// <summary>The shortest either hold may become however fast Play is set. The
    /// map's is the game's own select effect; the other is long enough for an event
    /// row's own hover animation to have finished.</summary>
    private const double HoldFloor = 0.5;

    private const double MapHoldFloor = 0.4;

    /// <summary>
    /// Forward.
    ///
    /// One press, one recorded action - unless the player is looking back, where the
    /// same press walks the ghost toward the present rather than moving the run. That
    /// is the only way out of looking back that does not skip anything.
    /// </summary>
    private static async void Forward()
    {
        if (_entry is not { } entry) return;

        try
        {
            if (_lookingBackAt is { } step)
            {
                _lookingBackAt = step < entry.StepsTaken ? step + 1 : null;
                if (_lookingBackAt is null) Relight();
                ShowTransport();
                return;
            }

            await CommitOne();
        }
        catch (Exception ex)
        {
            if (StillOurs(entry)) Abandon(ex);
        }
    }

    /// <summary>
    /// Back.
    ///
    /// Re-shows the decision before the one on screen and moves nothing. Play stops,
    /// because a sequence that carried on while somebody was reading the last step
    /// would be the reason they pressed this.
    /// </summary>
    private static void Back()
    {
        if (_entry is not { } entry || entry.StepsTaken == 0) return;

        Pause();
        _lookingBackAt = _lookingBackAt is { } step ? Math.Max(1, step - 1) : entry.StepsTaken;
        ShowTransport();
    }

    /// <summary>
    /// Play, and Pause.
    ///
    /// Play runs the recording's remaining decisions with a hold on each, which is
    /// what makes it watching rather than skipping: the same decisions in the same
    /// order, revealed before each one is made.
    /// </summary>
    private static void PlayOrPause()
    {
        if (_entry is null) return;

        if (_playing)
        {
            Pause();
            ShowTransport();
            return;
        }

        _playing = true;
        _lookingBackAt = null;
        Relight();
        ShowTransport();
        HoldThenCommit();
    }

    /// <summary>
    /// Puts the current decision's selected state back on the game's own screen.
    ///
    /// Needed because pressing a control on the strip is the player taking focus, and
    /// the game's own node responds to losing it by putting its highlight out - which
    /// is correct behaviour that would otherwise leave the transport holding on a
    /// decision nothing on screen is pointing at. Best effort: a screen that will not
    /// take it back says so in the log, and the caption on the strip still names the
    /// decision.
    /// </summary>
    private static void Relight()
    {
        if (_entry is not { AtBoundary: false } entry) return;

        try
        {
            RecordedFightReveal.Reveal(entry.DescribeNextTarget());
        }
        catch (Exception ex)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] could not put the reveal back after a control was pressed: " +
                $"{ex.Message}", 2);
        }
    }

    private static void Pause()
    {
        _playing = false;

        // Moved on so a hold already in flight cannot commit anything after this.
        _hold++;
        PlaybackTransportDock.Current?.HideHold();
    }

    /// <summary>
    /// Waits out the revealed decision, then makes it.
    ///
    /// The timer is the game's, on its own scene tree, so the client goes on drawing
    /// the reveal while it runs. Everything about the moment it wakes up is checked
    /// again: a hold that was paused, stepped past by Forward, belongs to a run that
    /// has since ended, or wakes before the next decision is revealed commits nothing.
    /// Reveal re-arms the chain while Play is running, so nothing is lost by waiting.
    /// </summary>
    private static async void HoldThenCommit()
    {
        var mine = _entry;
        try
        {
            var hold = ++_hold;
            var seconds = HoldFor(mine);

            // Drawn while it runs, not merely waited out. Without the line the tag
            // simply pauses under Play and a watcher cannot tell a hold from a stall,
            // which is the whole of "reveal, hold and commit should be visible".
            var steps = Math.Max(1, (int)Math.Round(seconds / HoldTick));
            for (var step = 0; step < steps; step++)
            {
                if (hold != _hold || !_playing || !ReferenceEquals(mine, _entry)) break;
                if (Phase != JourneyPhase.Watching || !_revealed) break;

                PlaybackTransportDock.Current?.ShowHold(1.0 - ((double)step / steps));
                await LetTheGameRun(seconds / steps);
            }

            PlaybackTransportDock.Current?.HideHold();

            if (hold != _hold || !_playing || !ReferenceEquals(mine, _entry)) return;
            if (Phase != JourneyPhase.Watching || !_revealed) return;

            await CommitOne();
        }
        catch (Exception ex)
        {
            if (StillOurs(mine)) Abandon(ex);
        }
    }

    /// <summary>How often the draining line is redrawn. Frequent enough to read as
    /// motion, rare enough that a hold is not a hundred timers.</summary>
    private const double HoldTick = 0.08;

    /// <summary>How long one pass of the between-screens line takes.</summary>
    private const double SweepSeconds = 1.1;

    private const double SweepTick = 0.05;

    /// <summary>Which between-screens line is current, for the reason
    /// <see cref="_hold"/> records: a timer cannot be recalled, so a pass that wakes
    /// up on an old number draws nothing.</summary>
    private static int _sweep;

    /// <summary>
    /// Runs the tag's line while the game is between screens.
    ///
    /// The two windows this covers are the ones where every control that would move
    /// the run is refused: the game putting the next screen up after a decision, and
    /// the fight opening after the last one. Both say nothing about why, by the
    /// captain's ruling that this surface shows rather than explains - so this is what
    /// does the showing, and it is the tag's own line rather than new ornament.
    ///
    /// One treatment for both, because the condition is one thing rather than two: the
    /// run is in a watched phase and nothing is revealed. That is the same condition
    /// that refuses Step, so what the player sees moving and what they find inert have
    /// the same cause rather than two that happen to coincide.
    /// </summary>
    private static async void SweepWhileTheGameIsBetweenScreens()
    {
        var sweep = ++_sweep;
        try
        {
            for (var step = 0; ; step++)
            {
                if (sweep != _sweep) return;
                if (_entry is null || Phase != JourneyPhase.Watching || _revealed) break;

                PlaybackTransportDock.Current?.ShowMoving(step * SweepTick / SweepSeconds % 1.0);
                await LetTheGameRun(SweepTick);
            }
        }
        catch (Exception ex)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] the between-screens line stopped: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
        finally
        {
            // Not while Play has the line: the reveal that ends this window starts the
            // hold on the same node in the same frame, and putting it out here would
            // blink it off for a tick on the way past.
            if (sweep == _sweep && !_playing) PlaybackTransportDock.Current?.HideHold();
        }
    }

    /// <summary>The speed in use. A property rather than a field for the reason
    /// <see cref="_speedIndex"/> records.</summary>
    private static PlaybackSpeed Speed => PlaybackSpeeds.All[_speedIndex];

    /// <summary>How long to hold on what is revealed now, by the kind of screen it is
    /// on. One duration with a floor per screen, shorter where the game already
    /// pauses.</summary>
    private static double HoldFor(RecordedFightEntry? entry)
    {
        try
        {
            // Speed divides the hold; the floor is what it may never divide past. On
            // the map the game runs a one-second select effect of its own before the
            // fade, so committing sooner than that would commit while the screen was
            // still showing the decision being made.
            return entry?.DescribeNextTarget() is PrefightTarget.MapNode
                ? Speed.Divide(MapHoldSeconds, MapHoldFloor)
                : Speed.Divide(HoldSeconds, HoldFloor);
        }
        catch (Exception)
        {
            // A target this host cannot name is refused at the reveal, loudly and with
            // the reason. Here it only decides a pause, so the longer one is right.
            return Speed.Divide(HoldSeconds, HoldFloor);
        }
    }

    /// <summary>Makes the revealed decision, then reveals the next one or hands the
    /// fight over.</summary>
    private static async Task CommitOne()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");
        if (_committing) return;

        // Nothing left to commit: the last decision is already made and the fight is
        // opening. Reached only by a press that beat the transport's own refusal.
        if (entry.AtBoundary) return;

        // Nothing is on the game's own screen yet, so committing here would make a
        // decision nobody was shown - the one thing reveal, hold and commit exists to
        // prevent. Reveal re-arms the chain when Play is running.
        if (!_revealed) return;

        // Any hold still in flight is invalidated: this decision is being made now,
        // and a timer that woke up afterwards would make the next one unrevealed.
        _hold++;
        _committing = true;
        try
        {

            // Recorded before the step, because after it the screen it was made on is
            // gone and the run must never be asked to go back and look.
            Shown.Add(entry.DescribeNextStep());
            RecordedFightReveal.Clear();

            // Nothing is revealed from here until the next reveal lands, and the tag
            // is re-derived to say so. The window between the two is a screen
            // transition long and a second Step pressed inside it used to make the
            // next decision unrevealed.
            _revealed = false;
            ShowTransport();
            SweepWhileTheGameIsBetweenScreens();

            await AdvanceOne();
        }
        finally
        {
            _committing = false;
        }

        if (!StillOurs(entry)) return;

        if (_entry is { AtBoundary: false })
        {
            RevealWhenTheGameHasFinishedMoving();
            return;
        }

        // Put on the tag before the wait rather than after it: the fight takes as long
        // to open as it takes, and until it has, the controls that would move the run
        // must be refused rather than left showing the decision just made.
        ShowTransport();
        HandOverWhenTheGameHasFinishedMoving();
    }

    /// <summary>
    /// Reveals the recording's next decision on the game's own screen and puts the
    /// transport on it.
    ///
    /// The refusal here is the one the whole design turns on. A decision this host
    /// cannot point at is a decision that would be committed unseen, which is the
    /// thing a watcher is here to prevent, so it ends the attempt with the reason
    /// rather than skipping the reveal and clicking anyway.
    /// </summary>
    private static async Task RevealNext()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");

        _lookingBackAt = null;
        var what = await RevealWhenTheScreenIsReady(entry.DescribeNextTarget());

        // The retry above runs for up to five seconds, which is long enough for the
        // journey underneath it to have ended and another to have started.
        if (!StillOurs(entry)) return;

        _revealed = true;
        Log.Info(
            $"[{RunmobileMod.ModId}] revealed decision " +
            $"{(entry.StepsTaken + 1).ToString(CultureInfo.InvariantCulture)} of " +
            $"{entry.Plan.PrefixActions.Count.ToString(CultureInfo.InvariantCulture)}: {what}", 2);

        ShowTransport();
        if (_playing) HoldThenCommit();
    }

    /// <summary>How long to keep offering a screen the chance to finish putting up
    /// the thing the recording chose. Long enough for the animations measured in the
    /// client - options flying in, a map fading - and short enough that a screen which
    /// never arrives is reported rather than waited on.</summary>
    private const double SettlingSeconds = 0.2;

    private const int SettlingAttempts = 25;

    /// <summary>
    /// Reveals as soon as the screen will take it.
    ///
    /// The retry is for one measured cause and no other: the game animates a screen's
    /// controls in and enables them at the end of it, so a reveal issued the moment
    /// the run reaches the screen is refused by a control that cannot yet take focus.
    /// A screen that cannot be driven at all is not retried - that refusal is passed
    /// straight out, and so is this one once the budget is spent.
    ///
    /// Each pass lights a control in the game's own screen, so the loop belongs to the
    /// journey that started it: five seconds is long enough for the player to have
    /// abandoned the run, and a retry landing after that would select a node in a run
    /// the trainer does not own. It gives up silently there - the run it was revealing
    /// for is gone, so there is nothing to refuse and nothing to say.
    /// </summary>
    private static async Task<string> RevealWhenTheScreenIsReady(PrefightTarget target)
    {
        var mine = _entry;
        for (var attempt = 1; ; attempt++)
        {
            if (!StillOurs(mine)) return string.Empty;

            try
            {
                return RecordedFightReveal.Reveal(target);
            }
            catch (RevealNotReadyException notReady) when (attempt < SettlingAttempts)
            {
                if (attempt == 1)
                {
                    Log.Info(
                        $"[{RunmobileMod.ModId}] waiting for the screen to finish: {notReady.Message}", 2);
                }

                await LetTheGameRun(SettlingSeconds);
            }
        }
    }

    /// <summary>
    /// Opens whatever the one press target offers.
    ///
    /// One button, because the tag and the chip are one node: while the recording is
    /// deciding it sets how long each choice is held, and once the fight is the
    /// player's it is the chip they press to leave it. Which of the two it is comes
    /// off the surface rather than being re-derived from the phase here - that
    /// re-derivation was one of the four places the mode was decided over again, and
    /// the one that read the phase a frame after it had changed.
    /// </summary>
    private static void OpenTheMenu()
    {
        try
        {
            if (PlaybackTransportDock.Current is not { } strip) return;

            if (strip.MenuIsOpen)
            {
                strip.CloseMenu();
                return;
            }

            strip.OpenMenu(strip.Surface.Speed.Press == Press.OpenChipMenu ? Jump : ChooseSpeed);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not open the transport's menu: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static void ChooseSpeed(int index)
    {
        _speedIndex = index;
        ShowTransport();
    }

    /// <summary>
    /// The two directions the chip offers, and the only destructive things the
    /// transport does.
    ///
    /// Both leave the attempt, so both ask first, through the game's own confirmation
    /// popup. Neither invents a comparison: jumping to the beginning rebuilds the run
    /// from the recording's history to the same proven combat start the entry already
    /// proves, and jumping to the end hands the fight to the result surface exactly as
    /// leaving it by any other route already does.
    /// </summary>
    private static void Jump(int row)
    {
        try
        {
            var entry = _entry;
            if (entry is null) return;
            var creator = RecordingIdentity.Creator(entry.Manifest);

            // Logged like every other decision this class makes. It is also the line
            // that says a press reached here at all, which is the thing no screenshot
            // of a menu can show.
            Log.Info(
                $"[{RunmobileMod.ModId}] the chip was asked to " +
                (row == 0 ? "start the fight again" : "finish the fight here"), 2);

            if (row == 0)
            {
                PrefightScreen.Confirm(
                    TrainerCopy.ConfirmJumpToTheBeginningTitle(creator),
                    TrainerCopy.ConfirmJumpToTheBeginningBody,
                    TrainerCopy.ConfirmGoBack,
                    TrainerCopy.ConfirmKeepFighting,
                    StartTheFightAgain);
                return;
            }

            PrefightScreen.Confirm(
                TrainerCopy.ConfirmJumpToTheEndTitle,
                TrainerCopy.ConfirmJumpToTheEndBody,
                TrainerCopy.ConfirmFinish,
                TrainerCopy.ConfirmKeepFighting,
                FinishHere);
        }
        catch (Exception ex)
        {
            // A control handler that throws into the game's signal dispatch is a
            // control that appears to do nothing. Every other handler here catches;
            // these two did not, which is why the first failure on this path was
            // silent in the client.
            Log.Error(
                $"[{RunmobileMod.ModId}] could not offer the chip's confirmation: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Back to the proven combat start.
    ///
    /// The attempt is discarded and the whole journey runs again, which is the one
    /// mechanism this project has for standing somebody in that fight: rebuild the run
    /// from the recording's history and prove the boundary. Nothing is injected and no
    /// state is restored, so the fight that comes back is the recorded one on the same
    /// terms it was the first time.
    /// </summary>
    private static async void StartTheFightAgain()
    {
        try
        {
            var recording = _entry?.Manifest;

            // The attempt is being discarded rather than left, so the result the
            // teardown queues for it is dropped before the return that would show it.
            await LeaveTheRun(keepTheResult: false);
            Finish();

            // Only once the menu is back: the game's own return task completing is the
            // signal the run it is tearing down has gone, and building the next run
            // over it is building it on the old one.
            if (recording is not null) await Start(recording);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not start the recorded fight again: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// End the attempt where it is and show the result.
    ///
    /// It adds no comparison kind. An unfinished fight has no completed line to set
    /// beside the recording's, and the result surface already says exactly that for a
    /// fight left by any other route; this is one more route to it. A partial line for
    /// the player is a change to the comparison contract and belongs to the comparison
    /// owner - see docs/comparison-direction.md.
    /// </summary>
    private static void FinishHere() => LeaveTheFightNow();

    private static void LeaveTheFightNow()
    {
        try
        {
            _resultAfterMainMenu = FightResultScreen.Left();
            _ = LeaveTheRun(keepTheResult: true);
            Finish();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not finish the fight here: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Tears the run down and returns to the main menu, answering when the menu is
    /// there.
    ///
    /// Cleaning the run up is what makes the teardown patch queue a result for the
    /// fight being left, so a caller that does not want one drops it here - between
    /// the clean-up that queues it and the return that shows it, which is the only
    /// point where the drop cannot race the return completing.
    /// </summary>
    /// <param name="keepTheResult">Whether the queued result is the one the player
    /// asked for. False where the attempt is being discarded.</param>
    private static Task LeaveTheRun(bool keepTheResult)
    {
        PlaybackTransportDock.Detach();
        if (RunManager.Instance is { IsInProgress: true }) RunManager.Instance.CleanUp();
        if (!keepTheResult) _resultAfterMainMenu = null;
        return NGame.Instance?.ReturnToMainMenu() ?? Task.CompletedTask;
    }

    /// <summary>
    /// Opens the recording at the moment the decision on screen was read.
    ///
    /// The captain's own correction to the first tag: the creator's name alone was not
    /// enough, and he wanted the video named and reachable. The timestamp is the
    /// action's own observation, so this opens where the move happens rather than at
    /// the start of a thirty-four minute run.
    /// </summary>
    private static void OpenTheVideo()
    {
        if (PlaybackTransportDock.Current?.State.Identity.VideoUrl is not { } url) return;

        try
        {
            OS.ShellOpen(url);
            Log.Info($"[{RunmobileMod.ModId}] opened the recording at {url}", 2);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not open the recording: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Puts the current state on the strip, attaching it the first time.
    ///
    /// Attached once and kept: the strip is a child of the run's own persistent
    /// interface, so it crosses the transitions the popup it replaces could not.
    /// </summary>
    private static void ShowTransport()
    {
        var state = _entry is { } entry ? PlaybackTransport.For(Phase, Facts(entry)) : null;

        // Nothing is docked in the phases that have no surface, and detaching is how
        // that is said. It is idempotent, so the paths that also tear the strip down
        // themselves are not doing it twice.
        if (state is null)
        {
            PlaybackTransportDock.Detach();
            return;
        }

        // Said once, and "once" means once it has been on the strip beside a decision
        // somebody could read. The tag is docked when the journey enters its watching
        // phase, which is before the first reveal lands, and consuming the note there
        // spent it on a window nothing was on screen for: measured in the client, it
        // was drawn and gone inside one deferred call.
        _noteShown |= state.Note.Length > 0 && _revealed;

        if (PlaybackTransportDock.Current is null)
        {
            // Attached while the recording is deciding, and at no other moment. Every
            // later phase either has a strip already or is tearing the run down, and
            // parenting one to a persistent interface that is being freed is the crash
            // this journey has already paid for once.
            if (Phase != JourneyPhase.Watching) return;

            PlaybackTransportDock.Attach(
                state, Back, PlayOrPause, Forward, OpenTheMenu, OpenTheVideo, ModelArt.Of);
            return;
        }

        PlaybackTransportDock.Apply(state);
    }

    /// <summary>
    /// Everything the transport is derived from, read off the run as it is now.
    ///
    /// Gathering rather than deciding: what any of these values <em>means</em> is
    /// <see cref="PlaybackTransport.For"/>'s, which is what lets the whole table be
    /// asserted without a game.
    ///
    /// The next decision is asked for only while the recording is the thing making
    /// decisions, and that guard is not an optimisation. An entry that has reached its
    /// fight throws when asked - and so does one whose next decision this host has no
    /// way to describe, which is the commonest reason a run is being refused at all.
    /// Asking during a refusal would throw inside the teardown that is trying to say
    /// so, and the tag would never get to show it. While watching it still throws, and
    /// still should: that is the refusal, arriving where it can be reported.
    /// </summary>
    private static TransportFacts Facts(RecordedFightEntry entry) => new(
        Identity(entry),
        Shown,
        Phase == JourneyPhase.Watching && !entry.AtBoundary ? entry.DescribeNextStep() : null,
        entry.StepsTaken,
        entry.Plan.PrefixActions.Count,
        entry.AtBoundary,
        _revealed,
        _lookingBackAt,
        _playing,
        _noteShown,
        Speed,
        AnythingPlayed(entry));

    /// <summary>
    /// Whose recording this is, and where in the video the decision being shown was
    /// made.
    ///
    /// Every value comes from the manifest. The video's title is absent until
    /// ingestion fills it, and the tag says the creator alone rather than inventing
    /// one; the timestamp is the action's own observation, so the link opens where the
    /// move actually happens rather than at the start of a thirty-four minute video.
    /// </summary>
    private static TransportIdentity Identity(RecordedFightEntry entry)
    {
        // A manifest with no video record still names its creator; what it loses is
        // the title and the link. Absent rather than invented, so the tag falls back
        // to the name alone and the block simply does not open anything.
        var video = entry.Manifest.Source.Video;
        var at = _lookingBackAt is null ? VideoTimeOf(entry.NextStep) : null;
        return new TransportIdentity(
            RecordingIdentity.Creator(entry.Manifest),
            video?.Title,
            video is null
                ? null
                : at is null
                    ? video.Url
                    : $"{video.Url}&t={(at.Value / 1000).ToString(CultureInfo.InvariantCulture)}s",
            at is null ? null : Timestamp(at.Value));
    }

    /// <summary>
    /// When in the video this action was observed, where the recording says.
    ///
    /// Absent is a real answer: an action whose provenance is inferred rather than
    /// observed has no timestamp, and the identity block links to the video's start
    /// rather than to a moment nobody read.
    /// </summary>
    private static int? VideoTimeOf(ActionRecord? action) => action?.Evidence?.VideoTimeMs;

    /// <summary>A video position as a player reads it on the platform's own scrubber.</summary>
    private static string Timestamp(int milliseconds)
    {
        var total = milliseconds / 1000;
        return $"{(total / 60).ToString(CultureInfo.InvariantCulture)}:" +
               $"{(total % 60).ToString("00", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Whether the player has taken an action of their own in their own fight yet.
    ///
    /// It decides only whether jumping to the end is offered: with nothing played there
    /// is no attempt to finish, and a control that would produce an empty result is
    /// refused rather than drawn as if it would work. One card is enough - the gate is
    /// an action, not a completed turn.
    /// </summary>
    private static bool AnythingPlayed(RecordedFightEntry entry) =>
        entry.Capture is { AnythingPlayed: true };

    /// <summary>
    /// Whether a continuation still belongs to the journey that started it.
    ///
    /// Every await in this class can outlive its own run: the player can abandon from
    /// the game's own pause menu at any frame, and the teardown clears the entry. A
    /// continuation that wakes into a different journey, or into none, does nothing at
    /// all - and "nothing" includes refusing, because abandoning tears down a run that
    /// is no longer the trainer's. Held to one predicate rather than an expression
    /// repeated per site, because the repeated one read <c>ReferenceEquals</c> alone
    /// and two nulls compare equal, so a journey that had ended looked current.
    /// </summary>
    private static bool StillOurs(RecordedFightEntry? mine) =>
        mine is not null && ReferenceEquals(mine, _entry);

    private static async Task AdvanceOne()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");

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

        if (!StillOurs(entry)) return;

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
    private static void RevealWhenTheGameHasFinishedMoving()
    {
        var mine = _entry;
        Callable.From(async void () =>
        {
            // The frame this waits for is a frame the player can act in, and abandoning
            // a run is one of the things they can do in it. A continuation that wakes
            // into a different journey, or none, does nothing at all.
            if (!StillOurs(mine)) return;

            try
            {
                await RevealNext();
            }
            catch (Exception ex)
            {
                if (StillOurs(mine)) Abandon(ex);
            }
        }).CallDeferred();
    }

    /// <summary>
    /// Hands the fight over once the game has finished opening it.
    ///
    /// The map move's task completes when the combat room is built, and the opening
    /// hand is dealt over the frames after that, so the boundary asked immediately
    /// reads an empty hand and refuses a fight that is merely a moment young.
    /// </summary>
    private static async void HandOverWhenTheGameHasFinishedMoving()
    {
        var entry = _entry;
        try
        {
            if (entry is null) throw new InvalidOperationException("There is no recorded fight under way.");

            Log.Info(
                $"[{RunmobileMod.ModId}] letting the fight open; {entry.DescribeCombatReadiness()}", 2);

            var opened = await WaitUntil(
                () => entry.IsReadyForThePlayer,
                LetTheGameRun(OpeningTheFightSeconds),
                () => LetTheGameRun(OpeningTheFightPollSeconds));

            Log.Info(
                $"[{RunmobileMod.ModId}] after letting the game run; " +
                $"{(opened ? "the fight opened" : "the fight did not open in time")}; " +
                $"{_entry?.DescribeCombatReadiness() ?? "no run"}", 2);

            // Twenty seconds is long enough that the world changing under this wait is
            // the ordinary case, not the exception: the player can abandon the run from
            // the game's own pause menu and be in a run of their own by now. Handing
            // over or refusing into that run would take it away from them.
            if (!StillOurs(entry)) return;

            HandOverTheFight();
        }
        catch (Exception ex)
        {
            if (!StillOurs(entry)) return;
            Abandon(ex);
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
    /// Waits for something the game is doing to be finished, rather than for a length
    /// of time. Answers whether it finished.
    ///
    /// Written because a flat wait is a race the retail client loses. The fight was
    /// handed over two seconds after the room opened, and at two seconds the client
    /// was still playing its Battle Start banner: the boundary read one card of the
    /// recording's five in hand and ten of its six in the draw pile, and a correct
    /// entry was refused. The engine already says when the player may act - a wait
    /// that asks it enters a fight that opens slowly and still refuses one that never
    /// opens, which is what the deadline is for.
    /// </summary>
    internal static async Task<bool> WaitUntil(Func<bool> done, Task deadline, Func<Task> nextPoll)
    {
        while (!done())
        {
            if (deadline.IsCompleted) return false;
            await nextPoll();
        }

        return true;
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

        Pause();
        RecordedFightReveal.Clear();

        // The strip collapses rather than closing. A player fighting wants nothing in
        // the way, and the chip is what a peek will be reached from later; nothing is
        // drawn unbidden from here until the fight has ended. Derived from the phase
        // like every other surface, so what the chip offers keeps up with the fight -
        // built by hand here, it stated at turn fifteen what had been true at turn one.
        Transition(JourneyPhase.InFight);
        Log.Info(
            $"[{RunmobileMod.ModId}] standing in the recorded fight; canonical state at combat start is " +
            $"{equality.ActualDigest}", 2);

        // The capture begins at the boundary just proved, and from nowhere else: it
        // carries the digest the comparison will require to be the recording's.
        var capture = entry.BeginCapture(equality);
        _observer = PlayerFightObserver.Start(entry, capture, TheFightEnded, ShowTransport);
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
        var entry = _entry;
        try
        {
            if (entry is null) throw new InvalidOperationException("There is no recorded fight under way.");

            var capture = entry.Capture
                ?? throw new InvalidOperationException("The fight ended before its capture began.");
            var screen = FightResultScreen.Of(
                RecordingIdentity.Creator(entry.Manifest), capture,
                CombatTrainerModule.Instance.RecordedFights.Projection(entry.Fight));
            _observer?.Dispose();
            _observer = null;
            Transition(JourneyPhase.Result);
            Log.Info(
                $"[{RunmobileMod.ModId}] result: " +
                (screen.HasComparison ? $"comparison, {screen.Rows.Count} row(s)" : screen.Notice), 2);

            await LetTheGameRun(EndingTheFightSeconds);

            // No stale-journey guard here, and that is the point of computing the
            // screen first: on a loss the game's own flow has torn the run down during
            // this wait, so a continuation that stopped because the entry had gone
            // would drop the comparison the fight was played for. Detaching is about
            // the transport rather than the journey and is idempotent, so it is safe
            // whether or not the strip is still there.
            PlaybackTransportDock.Detach();
            PrefightScreen.ShowResult(screen, LeaveTheFight);
        }
        catch (Exception ex)
        {
            if (StillOurs(entry)) Abandon(ex);
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
    private static void Abandon(Exception failure) =>
        Abandon(failure.Message, (failure as RevealRefusedException)?.Screen);

    private static void Abandon(string reason, string? screen = null)
    {
        // The engine's own sentence, verbatim, whatever the popup shows a player.
        Log.Error($"[{RunmobileMod.ModId}] not entering the recorded fight: {reason}", 2);

        var creator = _entry is { } entry ? RecordingIdentity.Creator(entry.Manifest) : TrainerCopy.Name;

        try
        {
            // The transport goes first. A refusal is the one thing this journey says
            // in a popup of its own, and leaving a strip offering Forward behind it
            // would be offering to go on with a run that is being torn down.
            Pause();

            // The refused state is applied and the tag is then detached in the same
            // call stack, so no frame is ever drawn with it: what a player sees is the
            // tag going, and then the popup's sentence. The transition stays because
            // it is what keeps the derivation total - every phase has an answer, and a
            // journey that ends is in one of them. Not drawing it is settled rather
            // than pending: holding the tag on screen instead would mean keeping it
            // alive across a return to the main menu that NRun.GlobalUi, its parent,
            // does not survive.
            Transition(JourneyPhase.Refused);
            PlaybackTransportDock.Detach();

            if (RunManager.Instance is { IsInProgress: true }) RunManager.Instance.CleanUp();
            ExplainOnceTheMenuIsBack(creator, screen, reason);
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
    /// Returns to the main menu, and only then says why.
    ///
    /// The order is the whole point of this method and it is not the obvious one. The
    /// refusal is a popup in the game's own modal container, and returning to the main
    /// menu frees what is in that container - so a refusal put up first was added,
    /// freed with the run it was explaining, and left the client's own deferred focus
    /// grab throwing on a disposed button. Measured in the retail client: the player
    /// was dropped at the main menu with no account of what had happened at all. The
    /// game's own return is awaited because completing it is the signal that the menu
    /// the popup will hang on is there.
    /// </summary>
    private static async void ExplainOnceTheMenuIsBack(string creator, string? screen, string reason)
    {
        try
        {
            await ExplainOnceTheMenuIsBack(
                NGame.Instance?.ReturnToMainMenu() ?? Task.CompletedTask,
                () => PrefightScreen.ShowRefusal(creator, screen, reason));
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not say why the recorded fight was refused: " +
                $"{ex.GetType().Name}: {ex.Message}",
                2);
        }
    }

    /// <summary>
    /// The order above, separated from the two calls that need a game to make.
    ///
    /// Neither the return nor the popup can be reached in a process with no client, but
    /// which happens first can be, and that is the whole of the fix: a refusal put up
    /// before the return was freed with the run it was explaining and left the player
    /// at the main menu with no account of what had happened. The same separation the
    /// hand-over's wait already uses, for the same reason - the bug was an ordering and
    /// an ordering is testable.
    /// </summary>
    internal static async Task ExplainOnceTheMenuIsBack(Task returned, Action explain)
    {
        await returned;
        explain();
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
        _playing = false;
        _committing = false;
        _speedIndex = 1;
        _hold++;
        _sweep++;
        _lookingBackAt = null;
        _noteShown = false;
        _revealed = false;
        Shown.Clear();

        // Last, and after the entry has gone: there is no surface for a journey that
        // has none, so this detaches rather than drawing.
        Transition(JourneyPhase.None);
        try
        {
            RecordedFightReveal.Clear();
            PlaybackTransportDock.Detach();
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
            if (Phase == JourneyPhase.InFight)
            {
                _resultAfterMainMenu = FightResultScreen.Left();
                Finish();
                return;
            }

            if (Phase != JourneyPhase.None || ProfileWriteBarrier.IsActive) Finish();
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
