using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Applies an ordered action history to a live run.
///
/// Every verb refuses rather than improvises. A card that is not where the manifest
/// says it is, a map node that is not reachable, an event option that does not
/// exist - each of these is a defect in the reconstruction, and the whole value of
/// the arbiter is that it says so instead of finding something plausible to do.
/// </summary>
public sealed class RunDriver(GameSession session)
{
    private Player Player => session.RunState.Players[0];

    /// <summary>Enters the run's first room. On a new run that is Neow's event.</summary>
    public void EnterFirstRoom()
    {
        RunManager.Instance.EnterAct(0, doTransition: false).GetAwaiter().GetResult();
        Pump.Drain();
    }

    public void Apply(ActionRecord action)
    {
        switch (action.Verb)
        {
            case ActionVerb.ChooseNeowBlessing:
                ChooseEventOption(Arg.Int(action, "option_index"));
                break;

            case ActionVerb.MapMove:
                MoveToMapNode(
                    Arg.Int(action, "act"), Arg.Int(action, "row"), Arg.Int(action, "column"));
                break;

            case ActionVerb.PlayCard:
                PlayCard(action);
                break;

            case ActionVerb.EndTurn:
                EndTurn();
                break;

            default:
                throw new EngineException(
                    $"Action {action.Seq} uses verb '{action.Verb}', which the format names but this " +
                    "milestone does not implement. Refusing: a verb that silently does nothing would " +
                    "produce a replay that looks complete and is not.");
        }
    }

    // ── Verbs ───────────────────────────────────────────────────────────────

    private void ChooseEventOption(int optionIndex)
    {
        var synchronizer = RunManager.Instance.EventSynchronizer
            ?? throw new EngineException("No event synchronizer: the run is not in an event room.");

        var localEvent = synchronizer.GetLocalEvent()
            ?? throw new EngineException("No event is in progress, so no option can be chosen.");

        var options = localEvent.CurrentOptions;
        if (optionIndex < 0 || optionIndex >= options.Count)
        {
            throw new EngineException(
                $"Event option {optionIndex} does not exist; this event offers {options.Count} " +
                $"({string.Join(", ", options.Select((o, i) => $"{i}:{o.GetType().Name}"))}).");
        }

        synchronizer.ChooseLocalOption(optionIndex);
        Pump.Drain();
    }

    private void MoveToMapNode(int act, int row, int column)
    {
        if (act != session.RunState.CurrentActIndex)
        {
            throw new EngineException(
                $"Map move names act {act}, but the run is in act {session.RunState.CurrentActIndex}.");
        }

        var map = session.RunState.Map
            ?? throw new EngineException("The current act has no map, so no node can be entered.");

        var point = map.GetPoint(column, row)
            ?? throw new EngineException($"Map node (row {row}, column {column}) does not exist in this act.");

        if (point.PointType == MapPointType.Unassigned)
        {
            throw new EngineException($"Map node (row {row}, column {column}) is empty in this act.");
        }

        var currentCoord = session.RunState.CurrentMapCoord
            ?? throw new EngineException("The run has no current map node, so reachability cannot be established.");
        var currentPoint = map.GetPoint(currentCoord.col, currentCoord.row)
            ?? throw new EngineException($"The current map node {currentCoord} does not exist in this act.");
        if (!currentPoint.Children.Contains(point))
        {
            throw new EngineException(
                $"Map node (row {row}, column {column}) is not reachable from " +
                $"(row {currentCoord.row}, column {currentCoord.col}).");
        }

        RunManager.Instance.EnterMapCoord(new MapCoord(column, row)).GetAwaiter().GetResult();
        Pump.Drain();
    }

    private void PlayCard(ActionRecord action)
    {
        var combat = Player.PlayerCombatState
            ?? throw new EngineException($"Action {action.Seq} plays a card, but the run is not in combat.");

        var handIndex = Arg.Int(action, "hand_index");
        var expectedCardId = Arg.String(action, "card_id");
        var hand = combat.Hand.Cards;

        if (handIndex < 0 || handIndex >= hand.Count)
        {
            throw new EngineException(
                $"Action {action.Seq} plays hand index {handIndex}, but the hand holds {hand.Count} card(s): " +
                $"{string.Join(", ", hand.Select(c => c.Id.ToString()))}.");
        }

        var card = hand[handIndex];
        if (card.Id.ToString() != expectedCardId)
        {
            // The single most valuable refusal in the whole driver. A hand that has
            // drifted from the manifest means the replay has already diverged, and
            // playing whatever happens to be at that index would hide it.
            throw new EngineException(
                $"Action {action.Seq} expects {expectedCardId} at hand index {handIndex}, but the engine " +
                $"has {card.Id}. The hand is {string.Join(", ", hand.Select(c => c.Id.ToString()))}. " +
                "The replay has diverged from the recorded history before this point.");
        }

        var target = ResolveTarget(action, card);

        if (!card.CanPlay(out var reason, out _))
        {
            throw new EngineException($"Action {action.Seq} cannot play {card.Id}: {reason}.");
        }

        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(new PlayCardAction(card, target));
        Pump.Drain();

        // A card that is still sitting at the same index did not actually get played.
        var handAfter = combat.Hand.Cards;
        if (handAfter.Count > handIndex && ReferenceEquals(handAfter[handIndex], card))
        {
            throw new EngineException(
                $"Action {action.Seq} enqueued {card.Id} but it is still in hand afterwards, so the play " +
                "did not take effect.");
        }
    }

    private Creature? ResolveTarget(ActionRecord action, CardModel card)
    {
        if (card.TargetType != TargetType.AnyEnemy) return null;

        var enemies = CombatManager.Instance.DebugOnlyGetState()?.Enemies
            .Where(e => e is { IsAlive: true })
            .ToList() ?? [];

        if (action.Args.TryGetValue("target_index", out var raw))
        {
            var index = int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            return index >= 0 && index < enemies.Count
                ? enemies[index]
                : throw new EngineException(
                    $"Action {action.Seq} targets enemy {index}, but {enemies.Count} enemy/enemies are alive.");
        }

        return enemies.Count switch
        {
            1 => enemies[0],
            0 => throw new EngineException($"Action {action.Seq} plays a targeted card with no living enemy."),
            _ => throw new EngineException(
                $"Action {action.Seq} plays {card.Id}, which targets one enemy, and {enemies.Count} are alive. " +
                "'target_index' is required - choosing one here would be inventing a decision the player made."),
        };
    }

    private void EndTurn()
    {
        var combat = Player.PlayerCombatState
            ?? throw new EngineException("End turn requested, but the run is not in combat.");

        if (combat.Phase != PlayerTurnPhase.Play)
        {
            Pump.Drain();
            if (Player.PlayerCombatState?.Phase != PlayerTurnPhase.Play)
            {
                throw new EngineException(
                    $"End turn requested while the player's turn phase is {combat.Phase}, not Play. " +
                    "Only the Play phase accepts player decisions.");
            }
        }

        // The enemy turn is a long chain of awaits. With no frame loop, the ones the
        // engine posts back to the scheduler need to complete inline or the chain
        // stalls; suppressing Task.Yield for the duration is what makes that happen.
        // It changes when continuations run, not which ones or in what order.
        using (YieldSuppression.Enable())
        {
            PlayerCmd.EndTurn(Player, canBackOut: false);
            Pump.Drain();
        }
    }

    private static class Arg
    {
        internal static int Int(ActionRecord action, string name) =>
            action.Args.TryGetValue(name, out var raw)
                ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
                : throw new EngineException($"Action {action.Seq} ({action.Verb}) is missing required argument '{name}'.");

        internal static string String(ActionRecord action, string name) =>
            action.Args.TryGetValue(name, out var raw)
                ? raw
                : throw new EngineException($"Action {action.Seq} ({action.Verb}) is missing required argument '{name}'.");
    }
}
