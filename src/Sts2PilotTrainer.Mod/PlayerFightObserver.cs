using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Watches the player fight the recorded combat and feeds what it sees to the
/// capture.
///
/// The player's actions reach the engine as the game's own actions - a card play,
/// a potion, a discard, an ended turn - through the game's own action executor, and
/// that executor announces each one before it runs and after it finishes. Those two
/// announcements are the "either side" the trace needs, so this subscribes to them
/// and to nothing else. It issues no command, patches no method and decides nothing;
/// what it samples is the same canonical projection the headless arbiter samples,
/// and <see cref="FightCapture"/> owns every rule about what the samples mean.
///
/// The one thing owned here is <em>when</em> the after-sample is taken, because in
/// the retail client an action finishing is not the engine settling. A card's
/// effects run on the queue after the card's own action reports finished; an ended
/// turn hands the whole enemy turn to the combat manager and the player's next turn
/// begins frames later. So the after-sample waits for the moment the headless
/// driver's drain reaches: the queue empty and the executor idle, and for an ended
/// turn, the player's next turn started. The wait has the same 30-second bound as
/// the headless drain; timing out marks the capture incomplete instead of sampling
/// unsettled state. If the fight ends before settlement, the combat manager's own
/// event closes the sample instead, with the final state.
///
/// Waiting here uses only what docs/in-game-host.md records as working in this
/// process: a task the game completes, and the scene tree's timer.
/// </summary>
internal sealed class PlayerFightObserver : IDisposable
{
    /// <summary>How long to keep waiting for the engine to settle after an action.
    /// The headless drain gives the same budget.</summary>
    private const double SettleBudgetSeconds = 30.0;

    private const double SettlePollSeconds = 0.05;

    private readonly Func<IReadOnlyDictionary<string, string>> _sample;
    private readonly IFightSampleSink _sink;
    private readonly Player _player;
    private readonly CombatManager _combat;
    private readonly ActionExecutor _executor;
    private readonly Action _fightEnded;
    private readonly Action _sampled;

    private bool _awaitingPlayerTurn;

    /// <summary>How many steps have been opened. Used only to tell one open step from
    /// the next, so a wait that outlives its own action cannot close another's.</summary>
    private int _openedSteps;

    /// <summary>Whether the executor has reported the open step's action finished.
    /// What makes it safe to close that step with the next action's before-sample.</summary>
    private bool _openStepFinished;
    private bool _ended;
    private bool _disposed;

    private PlayerFightObserver(
        Player player,
        Func<IReadOnlyDictionary<string, string>> sample,
        IFightSampleSink sink,
        Action fightEnded,
        Action sampled)
    {
        _sample = sample;
        _sink = sink;
        _fightEnded = fightEnded;
        _sampled = sampled;
        _player = player;
        _combat = CombatManager.Instance
            ?? throw new InvalidOperationException("This build exposes no CombatManager to observe the fight through.");
        _executor = RunManager.Instance.ActionExecutor
            ?? throw new InvalidOperationException("This run has no action executor to observe the fight through.");
    }

    /// <summary>
    /// Starts observing a fight this player is about to play.
    /// </summary>
    /// <param name="player">Whose actions count. The executor announces the game's own
    /// bookkeeping actions through the same events, and this is what tells them
    /// apart.</param>
    /// <param name="sample">How to read the canonical state right now. Supplied rather
    /// than done here, because reading the game belongs to the engine and this class's
    /// one job is deciding <em>when</em> a reading is taken.</param>
    /// <param name="sink">Where the samples go: the Combat Trainer hands over the
    /// fight's own capture, and the recorder hands over the run it is keeping. One
    /// observer either way, so a fight is watched the same whoever is watching.</param>
    /// <param name="fightEnded">Called once, on the game's own combat-ended event,
    /// after the capture has been closed one way or the other.</param>
    /// <param name="sampled">Called whenever a step opens or closes. The transport is a
    /// function of the run's facts, and whether the player has played anything is one
    /// of them - it becomes true when a step closes, so a re-derivation only where one
    /// opens leaves the chip a whole action behind. The recorder draws nothing and
    /// passes a callback that does nothing.</param>
    internal static PlayerFightObserver Start(
        Player player,
        Func<IReadOnlyDictionary<string, string>> sample,
        IFightSampleSink sink,
        Action fightEnded,
        Action sampled)
    {
        var observer = new PlayerFightObserver(player, sample, sink, fightEnded, sampled);
        observer._executor.BeforeActionExecuted += observer.BeforeAction;
        observer._executor.AfterActionExecuted += observer.AfterAction;
        observer._combat.TurnStarted += observer.TurnStarted;
        observer._combat.CombatEnded += observer.CombatEnded;
        Log.Info($"[{RunmobileMod.ModId}] capturing the player's fight from its combat start", 2);
        return observer;
    }

    /// <summary>
    /// Samples the state an action is about to act on.
    ///
    /// Only the player's own actions, and only the four the fight is made of. The
    /// game's own bookkeeping actions - the enemy turn's readiness, a hook - are not
    /// decisions and the executor tells them apart for us.
    /// </summary>
    private void BeforeAction(GameAction action)
    {
        if (_ended || action.OwnerId != _player.NetId) return;

        // Whether the action still open had already finished executing. Only consulted
        // when one is open, which happens where two actions arrive with no frame
        // between them - one click that plays a held card and ends the turn does
        // exactly that. The executor runs its actions in order, so a finished previous
        // action means this action's before-sample is that one's after-sample.
        var previousFinished = _openStepFinished;
        var opened = false;

        try
        {
            switch (action)
            {
                case PlayCardAction play:
                    opened = Begin(nameof(ActionVerb.PlayCard), PlayCardArgs(play), previousFinished);
                    break;
                case UsePotionAction potion:
                    opened = Begin(nameof(ActionVerb.UsePotion), PotionArgs(potion.PotionIndex), previousFinished);
                    break;
                case DiscardPotionGameAction discard:
                    opened = Begin(nameof(ActionVerb.DiscardPotion), PotionArgs(SlotOf(discard)), previousFinished);
                    break;
                case EndPlayerTurnAction:
                    opened = Begin(nameof(ActionVerb.EndTurn), Empty, previousFinished);
                    break;
                case UndoEndPlayerTurnAction:
                    // The game took the ended turn back before the enemy turn began.
                    // Nothing happened, so nothing is recorded; the state it returns to
                    // is checked by the next action's before-sample like any other.
                    _awaitingPlayerTurn = false;
                    _sink.DiscardOpenStep();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[{RunmobileMod.ModId}] could not sample before {action}: {ex.GetType().Name}: {ex.Message}", 2);
        }

        // Outside the capture's own catch: the step is open by the time we get here, so
        // a re-derivation that throws is a surface problem and is reported as one.
        if (!opened) return;

        try
        {
            _sampled();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not re-derive the transport before {action}: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Opens a step, and counts it.
    ///
    /// The count is what keeps a late after-sample from closing the wrong action: an
    /// action that began while another was open closes that one here, and the wait
    /// still running for it must not then close this one with a state that is not its.
    /// </summary>
    private bool Begin(string verb, IReadOnlyDictionary<string, string> args, bool previousFinished)
    {
        _sink.BeginStep(verb, args, _sample(), previousFinished);
        _openedSteps++;
        _openStepFinished = false;
        return true;
    }

    private void AfterAction(GameAction action)
    {
        if (_ended || action.OwnerId != _player.NetId) return;

        switch (action)
        {
            case PlayCardAction or UsePotionAction or DiscardPotionGameAction:
                _openStepFinished = true;
                _ = CompleteWhenSettled();
                break;
            case EndPlayerTurnAction:
                // The turn's after-sample is the player's next turn, once the enemy
                // turn between them has resolved; TurnStarted says when.
                _openStepFinished = true;
                _awaitingPlayerTurn = true;
                break;
        }
    }

    private void TurnStarted(CombatState state)
    {
        if (_ended || !_awaitingPlayerTurn || state.CurrentSide != CombatSide.Player) return;
        _awaitingPlayerTurn = false;
        _ = CompleteWhenSettled();
    }

    /// <summary>
    /// The engine's own word that the fight is over, for a win and for a loss.
    ///
    /// It closes whichever action was open with the final state, which is how a
    /// capture completes at all: the killing blow, or the enemy turn the player did
    /// not survive, is the action the fight ended inside.
    /// </summary>
    private void CombatEnded(CombatRoom room)
    {
        if (_ended) return;
        _ended = true;
        _awaitingPlayerTurn = false;

        try
        {
            _sink.Finish(_sample());
            Log.Info($"[{RunmobileMod.ModId}] the player's fight ended and its capture has been closed", 2);
        }
        catch (Exception ex)
        {
            Log.Error($"[{RunmobileMod.ModId}] could not sample the end of the fight: {ex.GetType().Name}: {ex.Message}", 2);
        }

        // Outside the capture's own catch: the capture is closed by the line above, so
        // a re-derivation that throws is a surface problem and is reported as one.
        try
        {
            _sampled();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not re-derive the transport at the end of the fight: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }

        _fightEnded();
    }

    /// <summary>
    /// Takes the after-sample once the engine has finished with the action.
    ///
    /// Settled means the queue is empty and the executor idle, which is what the
    /// headless drain waits for. The complete wait is bounded; timing out marks the
    /// capture incomplete rather than sampling unsettled state. If the combat manager
    /// already regards the fight as over or ending, the sample is left to
    /// <see cref="CombatEnded"/>, which carries the final state; a sample taken here
    /// would read a fight half-ended.
    /// </summary>
    private async Task CompleteWhenSettled()
    {
        var waitingFor = _openedSteps;
        var closed = false;
        try
        {
            var queues = RunManager.Instance.ActionQueueSet;
            var settled = await WaitUntilSettled(
                _sink,
                queues.BecameEmpty(),
                RecordedFightRun.LetTheGameRun(SettleBudgetSeconds),
                () => !_executor.IsRunning && queues.IsEmpty,
                () => _ended || _disposed,
                () => RecordedFightRun.LetTheGameRun(SettlePollSeconds));
            if (!settled)
            {
                if (!_ended && !_disposed)
                {
                    Log.Warn(
                        $"[{RunmobileMod.ModId}] the engine did not settle within " +
                        $"{SettleBudgetSeconds.ToString(CultureInfo.InvariantCulture)}s after an action", 2);
                }
                return;
            }

            // The action this wait belongs to may already have been closed by the next
            // one beginning. Closing again would put this action's after-sample on the
            // action after it.
            if (_ended || _disposed || _combat.IsOverOrEnding || _openedSteps != waitingFor) return;
            _sink.CompleteStep(_sample());
            closed = true;
        }
        catch (Exception ex)
        {
            if (!_ended && !_disposed)
            {
                _sink.MarkIncomplete(
                    $"The engine could not settle after an action: {ex.GetType().Name}: {ex.Message}");
            }
            Log.Error($"[{RunmobileMod.ModId}] could not sample after an action: {ex.GetType().Name}: {ex.Message}", 2);
        }

        // Outside the wait's own catch: a re-derivation that throws is a surface
        // problem and must not mark the capture incomplete.
        if (!closed) return;

        try
        {
            _sampled();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not re-derive the transport after an action: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    internal static async Task<bool> WaitUntilSettled(
        IFightSampleSink sink,
        Task becameEmpty,
        Task deadline,
        Func<bool> isSettled,
        Func<bool> stopped,
        Func<Task> nextPoll)
    {
        if (deadline.IsCompleted || await Task.WhenAny(becameEmpty, deadline) != becameEmpty)
        {
            return TimedOut();
        }
        await becameEmpty;

        while (true)
        {
            if (stopped()) return false;
            if (deadline.IsCompleted) return TimedOut();
            if (isSettled()) return true;

            var poll = nextPoll();
            if (await Task.WhenAny(poll, deadline) != poll) return TimedOut();
            await poll;
        }

        bool TimedOut()
        {
            if (!stopped())
            {
                sink.MarkIncomplete(
                    $"The engine did not settle within " +
                    $"{SettleBudgetSeconds.ToString(CultureInfo.InvariantCulture)} seconds after an action.");
            }
            return false;
        }
    }

    /// <summary>
    /// What a played card is, in the names a manifest records it under: the card,
    /// where in the hand it was, and which enemy it was aimed at.
    ///
    /// The hand index is here because a replay needs it - <c>RunDriver.PlayCard</c>
    /// plays the card at that position and refuses when its id disagrees, which is the
    /// sharpest refusal in the whole driver. It is read while the action is only
    /// enqueued, which is the last moment the card is still in hand.
    /// </summary>
    private IReadOnlyDictionary<string, string> PlayCardArgs(PlayCardAction play)
    {
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["card_id"] = play.CardModelId.ToString(),
        };

        if (HandIndexOf(play) is { } handIndex)
        {
            args["hand_index"] = handIndex.ToString(CultureInfo.InvariantCulture);
        }

        // The same index the driver resolves a recorded target by: position among the
        // enemies alive at the moment of the play.
        if (play.TargetId is { } targetId)
        {
            var alive = _combat.DebugOnlyGetState()?.Enemies.Where(enemy => enemy is { IsAlive: true }).ToList() ?? [];
            var index = alive.FindIndex(enemy => enemy.CombatId == targetId);
            if (index >= 0) args["target_index"] = index.ToString(CultureInfo.InvariantCulture);
        }

        return args;
    }

    /// <summary>
    /// Which potion, and which belt slot it came off, in the names a manifest uses.
    ///
    /// Read while the potion is still on the belt, for the same reason the hand index
    /// is read while the card is still in hand: afterwards the slot is empty and the
    /// only honest answer would be the position of nothing.
    /// </summary>
    private IReadOnlyDictionary<string, string> PotionArgs(uint slot)
    {
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["slot_index"] = slot.ToString(CultureInfo.InvariantCulture),
        };
        if (slot < _player.PotionSlots.Count && _player.PotionSlots[(int)slot] is { } potion)
        {
            args["potion_id"] = potion.Id.ToString();
        }

        return args;
    }

    /// <summary>
    /// Where in the hand the card being played is, or null when the hand no longer
    /// holds it.
    ///
    /// By reference rather than by id: a hand with two copies of one card has two
    /// positions that name it, and only one of them is the object the engine is about
    /// to play. Null rather than a guess where the card has already gone, which is the
    /// honest answer and makes the missing argument visible in validation rather than
    /// wrong in a replay.
    /// </summary>
    private int? HandIndexOf(PlayCardAction play)
    {
        var card = play.NetCombatCard.ToCardModelOrNull();
        if (card is null) return null;

        var hand = _player.PlayerCombatState?.Hand.Cards;
        if (hand is null) return null;

        for (var index = 0; index < hand.Count; index++)
        {
            if (ReferenceEquals(hand[index], card)) return index;
        }

        return null;
    }

    /// <summary>The discard action keeps its slot private; it is read by name and
    /// refused loudly when a build no longer has it.</summary>
    private static uint SlotOf(DiscardPotionGameAction discard)
    {
        var field = typeof(DiscardPotionGameAction).GetField(
            "_potionSlotIndex",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DiscardPotionGameAction has no _potionSlotIndex on this build.");
        return (uint)field.GetValue(discard)!;
    }

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _executor.BeforeActionExecuted -= BeforeAction;
        _executor.AfterActionExecuted -= AfterAction;
        _combat.TurnStarted -= TurnStarted;
        _combat.CombatEnded -= CombatEnded;
    }
}
