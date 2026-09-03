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
                $"[{RunmobileMod.ModId}] constructed {creator}'s run; watching " +
                $"{_entry.Plan.PrefixActions.Count.ToString(CultureInfo.InvariantCulture)} recorded " +
                "decision(s) before the fight", 2);
            RevealWhenTheGameHasFinishedMoving();
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
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

    /// <summary>
    /// Forward.
    ///
    /// One press, one recorded action - unless the player is looking back, where the
    /// same press walks the ghost toward the present rather than moving the run. That
    /// is the only way out of looking back that does not skip anything.
    /// </summary>
    private static async void Forward()
    {
        try
        {
            if (_entry is not { } entry) return;

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
            Abandon(ex.Message);
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
    }

    /// <summary>
    /// Waits out the revealed decision, then makes it.
    ///
    /// The timer is the game's, on its own scene tree, so the client goes on drawing
    /// the reveal while it runs. Everything about the moment it wakes up is checked
    /// again: a hold that was paused, stepped past by Forward, or belongs to a run
    /// that has since ended commits nothing.
    /// </summary>
    private static async void HoldThenCommit()
    {
        try
        {
            var hold = ++_hold;
            var mine = _entry;
            await LetTheGameRun(HoldFor(mine));

            if (hold != _hold || !_playing || !ReferenceEquals(mine, _entry)) return;
            if (Phase != RecordedFightPhase.Watching) return;

            await CommitOne();
        }
        catch (Exception ex)
        {
            Abandon(ex.Message);
        }
    }

    /// <summary>How long to hold on what is revealed now, by the kind of screen it is
    /// on. One duration with a floor per screen, shorter where the game already
    /// pauses.</summary>
    private static double HoldFor(RecordedFightEntry? entry)
    {
        try
        {
            return entry?.DescribeNextTarget() is PrefightTarget.MapNode ? MapHoldSeconds : HoldSeconds;
        }
        catch (Exception)
        {
            // A target this host cannot name is refused at the reveal, loudly and with
            // the reason. Here it only decides a pause, so the longer one is right.
            return HoldSeconds;
        }
    }

    /// <summary>Makes the revealed decision, then reveals the next one or hands the
    /// fight over.</summary>
    private static async Task CommitOne()
    {
        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");
        if (_committing) return;

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

            await AdvanceOne();
        }
        finally
        {
            _committing = false;
        }

        if (_entry is { AtBoundary: false })
        {
            RevealWhenTheGameHasFinishedMoving();
            return;
        }

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
    /// </summary>
    private static async Task<string> RevealWhenTheScreenIsReady(PrefightTarget target)
    {
        for (var attempt = 1; ; attempt++)
        {
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
    /// Puts the current state on the strip, attaching it the first time.
    ///
    /// Attached once and kept: the strip is a child of the run's own persistent
    /// interface, so it crosses the transitions the popup it replaces could not.
    /// </summary>
    private static void ShowTransport()
    {
        var state = TransportState();

        // Said once, and "once" means once it has been on the strip rather than once
        // it has been composed.
        _noteShown |= state.Note.Length > 0;

        if (PlaybackTransportDock.Current is null)
        {
            PlaybackTransportDock.Attach(state, Back, Forward, PlayOrPause);
            return;
        }

        PlaybackTransportDock.Apply(state);
    }

    private static PlaybackTransport TransportState()
    {
        if (Phase != RecordedFightPhase.Watching) return PlaybackTransport.DuringYourFight();

        var entry = _entry ?? throw new InvalidOperationException("There is no recorded fight under way.");
        var creator = RecordingIdentity.Creator(entry.Manifest);
        var count = entry.Plan.PrefixActions.Count;

        if (_lookingBackAt is { } step)
        {
            return PlaybackTransport.LookingBackAt(creator, Shown[step - 1], step, count);
        }

        return PlaybackTransport.Revealing(
            creator, entry.DescribeNextStep(), entry.StepsTaken + 1, count, _playing, _noteShown);
    }

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
    private static void RevealWhenTheGameHasFinishedMoving() =>
        Callable.From(async void () =>
        {
            try
            {
                await RevealNext();
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

        Pause();
        RecordedFightReveal.Clear();
        Phase = RecordedFightPhase.InFight;

        // The strip collapses rather than closing. A player fighting wants nothing in
        // the way, and the chip is what a peek will be reached from later; nothing is
        // drawn unbidden from here until the fight has ended.
        PlaybackTransportDock.Apply(PlaybackTransport.DuringYourFight());
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
            PlaybackTransportDock.Detach();
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
            // The transport goes first. A refusal is the one thing this journey says
            // in a popup of its own, and leaving a strip offering Forward behind it
            // would be offering to go on with a run that is being torn down.
            Pause();
            PlaybackTransportDock.Detach();
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
        _playing = false;
        _committing = false;
        _hold++;
        _lookingBackAt = null;
        _noteShown = false;
        Shown.Clear();
        Phase = RecordedFightPhase.None;
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
