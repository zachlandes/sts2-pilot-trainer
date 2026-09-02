using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
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
///
/// Two of the engine's surfaces do not take a command at all: the loot screen a won
/// fight puts up, and the card screens a reward or an enchantment opens. The retail
/// UI drives the first and answers the second, and there is no UI here. The driver
/// therefore stands in for both, and does so narrowly: it offers a finished fight's
/// room-end rewards through the game's own <c>CombatRoom.OfferRoomEndRewards</c>,
/// and it answers card screens from the manifest through the game's own
/// <c>ICardSelector</c> seam. Neither stand-in decides anything; the manifest does,
/// and where the manifest is silent both refuse. See docs/headless-fidelity.md.
/// </summary>
public sealed class RunDriver : IDisposable
{
    private readonly GameSession _session;
    private readonly ManifestCardSelector _selector = new();
    private readonly IDisposable _selectorScope;
    private readonly Func<RewardsSet, Task>? _previousRewardsSelector;

    /// <summary>
    /// The rewards set the current room is offering, once it has been offered and
    /// until the engine reports it complete. Captured through
    /// <see cref="RewardsSet.testSelector"/>, which is where the engine hands a set
    /// to a caller that is standing in for the reward screen.
    /// </summary>
    private RewardsSet? _openRewards;

    /// <summary>The room whose rewards have already been offered, so that draining
    /// the queue again after a later action cannot offer them twice.</summary>
    private AbstractRoom? _rewardsOfferedForRoom;

    /// <summary>Sequence numbers of card selections a screen has already consumed,
    /// so the action that records each one can insist it was used.</summary>
    private readonly HashSet<int> _consumedSelections = [];

    public RunDriver(GameSession session)
    {
        _session = session;

        // The game's own seam for answering a card screen without a scene tree.
        // Pushed rather than exclusively claimed, because a process that drives two
        // runs in turn would otherwise fail on the second.
        _selectorScope = CardSelectCmd.PushSelector(_selector);

        // What the retail client does with the loot screen, said in the one place the
        // engine offers: RewardsSet.Offer hands the set to this delegate when test
        // mode is on, in place of showing NRewardsScreen. Parking the set rather than
        // resolving it is what leaves the decisions to the manifest.
        //
        // ThrowInTestIfRewardsNotTaken is cleared for the same reason. It is a
        // test-only assertion - the single site that reads it is the line right after
        // this delegate returns - and it exists to catch a test that forgot to answer
        // a reward screen. Here the answer arrives later, from the next action.
        _previousRewardsSelector = RewardsSet.testSelector;
        RewardsSet.testSelector = set =>
        {
            set.ThrowInTestIfRewardsNotTaken = false;
            _openRewards = set;
            return Task.CompletedTask;
        };
    }

    public void Dispose()
    {
        RewardsSet.testSelector = _previousRewardsSelector;
        _selectorScope.Dispose();
    }

    private Player Player => _session.RunState.Players[0];

    /// <summary>Enters the run's first room. On a new run that is Neow's event.</summary>
    public void EnterFirstRoom()
    {
        RunManager.Instance.EnterAct(0, doTransition: false).GetAwaiter().GetResult();
        Pump.Drain();
    }

    /// <summary>Applies one action, with no history after it. Kept for callers that
    /// drive a single decision.</summary>
    public void Apply(ActionRecord action) => Apply(action, []);

    /// <summary>
    /// Applies one action.
    /// </summary>
    /// <param name="upcoming">
    /// The actions that follow this one, in order. Needed because two of the engine's
    /// screens pull the player's answer synchronously from inside the call that opens
    /// them: the enchantment screen an event option opens is answered while
    /// <c>ChooseLocalOption</c> is still running. The decisions themselves are ordinary
    /// actions, recorded after the one that opened the screen because that is when the
    /// player made them; this is what lets the driver hand them over at the moment the
    /// engine asks. Only a contiguous run of <see cref="ActionVerb.SelectCardFromScreen"/>
    /// immediately after the opening action is ever read.
    /// </param>
    public void Apply(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
    {
        switch (action.Verb)
        {
            case ActionVerb.ChooseNeowBlessing:
                ChooseEventOption(Arg.Int(action, "option_index"));
                break;

            case ActionVerb.ChooseEventOption:
                ChooseEventOption(action, upcoming);
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

            case ActionVerb.ClaimReward:
                ClaimReward(action);
                break;

            case ActionVerb.TakeCard:
                TakeCard(action);
                break;

            case ActionVerb.SkipRewards:
                SkipRewards(action);
                break;

            case ActionVerb.SelectCardFromScreen:
                ConfirmCardSelectionWasConsumed(action);
                break;

            default:
                throw new EngineException(
                    $"Action {action.Seq} uses verb '{action.Verb}', which the format names but this " +
                    "milestone does not implement. Refusing: a verb that silently does nothing would " +
                    "produce a replay that looks complete and is not.");
        }

        // A card screen answers inside the engine call the action above made, and the
        // engine runs those callbacks in tasks that swallow exceptions. The refusal is
        // therefore raised here, where it can actually stop the replay.
        // Raised unprefixed: the arbiter already names the action and verb, and the
        // selector's own message names the action whose selection disagreed, which is
        // not always this one.
        if (_selector.Refusal is { } refusal) throw new EngineException(refusal);

        if (_selector.PendingCount > 0)
        {
            throw new EngineException(
                $"Action {action.Seq} ({action.Verb}) queued {_selector.PendingCount} card selection(s) that " +
                "no screen asked for. A recorded selection the engine never consumed means the manifest " +
                "describes a screen this run does not open.");
        }
    }

    /// <summary>
    /// Puts up the loot a finished fight earned, exactly where the retail client does.
    ///
    /// <c>NCombatUi.ShowRewards</c> waits out the death animations and then calls
    /// <c>CombatRoom.OfferRoomEndRewards</c>; there is no UI here, so this calls the
    /// same method at the same point - after the action that ended the fight has
    /// drained. It generates nothing itself: reward generation, the reward hook and
    /// the streams they draw from are all the engine's, reached through the engine's
    /// own entry point.
    ///
    /// Offering is not a player decision and so is not an action. Taking anything is,
    /// and until the manifest says so the set sits open - which is what makes
    /// <see cref="MoveToMapNode"/> able to refuse a history that walks away from
    /// undeclared loot.
    /// </summary>
    private void OfferRoomEndRewardsIfCombatEnded()
    {
        if (_session.RunState.CurrentRoom is not CombatRoom room) return;
        if (ReferenceEquals(_rewardsOfferedForRoom, room)) return;
        if (CombatManager.Instance is not { IsInProgress: false }) return;
        if (Player.Creature is { IsAlive: false }) return;
        if (room.Encounter is not { ShouldGiveRewards: true }) return;

        _rewardsOfferedForRoom = room;
        room.OfferRoomEndRewards().GetAwaiter().GetResult();
        Pump.Drain();
    }

    /// <summary>The rewards set currently on offer, or a refusal naming the verb that
    /// needed one.</summary>
    private RewardsSet OpenRewards(ActionRecord action)
    {
        if (_openRewards is { } set && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(set))
        {
            return set;
        }

        throw new EngineException(
            $"Action {action.Seq} ({action.Verb}) acts on a reward screen, but no rewards are on offer. " +
            "Rewards are offered when a fight the encounter rewards is won, and stop being on offer once " +
            "every one of them has been taken or the set has been skipped.");
    }

    // ── Verbs ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Picks an option in the event the run is standing in, with the event named.
    ///
    /// The event id is required and checked, unlike the opening blessing's, because
    /// which event a floor generates is a consequence of everything before it. An
    /// option index is only meaningful against a particular event's option list, so a
    /// generated event that is not the one the video shows has to fail here rather
    /// than take whatever sits at that index.
    /// </summary>
    private void ChooseEventOption(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
    {
        var expectedEventId = Arg.String(action, "event_id");

        // Asked of the room rather than of the synchronizer, which keeps the last
        // event it ran and would otherwise answer with a stale one from a floor the
        // run has already left.
        if (_session.RunState.CurrentRoom is not EventRoom)
        {
            throw new EngineException(
                $"Action {action.Seq} chooses an option in event {expectedEventId}, but this floor is a " +
                $"{_session.RunState.CurrentRoom?.RoomType.ToString() ?? "no"} room, not an event.");
        }

        var localEvent = RunManager.Instance.EventSynchronizer?.GetLocalEvent()
            ?? throw new EngineException(
                $"Action {action.Seq} chooses an event option, but no event is in progress.");

        if (localEvent.Id.ToString() != expectedEventId)
        {
            throw new EngineException(
                $"Action {action.Seq} expects event {expectedEventId}, but this floor generated " +
                $"{localEvent.Id}. The replay has diverged from the recorded history before this point.");
        }

        QueueFollowingCardSelections(action, upcoming);
        ChooseEventOption(Arg.Int(action, "option_index"));
    }

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

    /// <summary>
    /// Takes one reward off the loot screen: the gold or the potion.
    ///
    /// The card reward is deliberately not reachable here - it opens a second screen
    /// and so has its own verb, <see cref="ActionVerb.TakeCard"/>, which records which
    /// card came back. Naming the kind rather than an index is what the video shows;
    /// a set that offers two of a kind is refused rather than resolved by position,
    /// because position on that screen is a layout detail and the choice would be ours.
    /// </summary>
    private void ClaimReward(ActionRecord action)
    {
        var set = OpenRewards(action);
        var kind = Arg.String(action, "reward_type");

        var matches = set.Rewards.Where(reward => !reward.SuccessfullySelected && KindOf(reward) == kind).ToList();
        if (matches.Count == 0)
        {
            throw new EngineException(
                $"Action {action.Seq} claims a '{kind}' reward, but this loot screen offers " +
                $"{DescribeRewards(set)}.");
        }

        if (matches.Count > 1)
        {
            throw new EngineException(
                $"Action {action.Seq} claims a '{kind}' reward and {matches.Count} of them are on offer " +
                $"({DescribeRewards(set)}). Which one was taken is not recorded, and choosing here would be " +
                "inventing a decision the player made.");
        }

        Select(action, set, matches[0]);
    }

    /// <summary>
    /// Takes a card off a combat's card reward.
    ///
    /// Two arguments, for the same reason <see cref="PlayCard"/> takes two: the index
    /// is what the video shows the player click, and the card id is what makes a
    /// drifted reward fail loudly instead of taking whatever is in that position.
    /// </summary>
    private void TakeCard(ActionRecord action)
    {
        var set = OpenRewards(action);
        var cardReward = set.Rewards.OfType<CardReward>().FirstOrDefault(reward => !reward.SuccessfullySelected)
            ?? throw new EngineException(
                $"Action {action.Seq} takes a card, but this loot screen offers no unclaimed card reward " +
                $"({DescribeRewards(set)}).");

        _selector.Enqueue(new ManifestCardSelector.Pick(
            action.Seq, Arg.String(action, "card_id"), Arg.Int(action, "option_index")));

        Select(action, set, cardReward);

        if (_selector.Refusal is null && !cardReward.SuccessfullySelected)
        {
            throw new EngineException(
                $"Action {action.Seq} selected the card reward but the engine did not complete it, so no " +
                "card was added to the deck.");
        }
    }

    /// <summary>
    /// Dismisses the loot screen with something still on it.
    ///
    /// This is a decision, not an absence, which is why it has to be written down.
    /// Leaving the room skips whatever is left over anyway - the engine does that in
    /// <c>RewardsSetSynchronizer.BeforeLeavingRoom</c> - so a history that simply
    /// omitted the reward would replay to the same state as one that declined it, and
    /// a dropped action would look exactly like a decision. <see cref="MoveToMapNode"/>
    /// refuses to walk away from an open set for the same reason.
    /// </summary>
    private void SkipRewards(ActionRecord action)
    {
        var set = OpenRewards(action);
        RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        Pump.Drain();

        if (!RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(set))
        {
            throw new EngineException(
                $"Action {action.Seq} skipped the loot screen but the engine still reports the set open.");
        }
    }

    /// <summary>
    /// Confirms a card the manifest picked off a screen was the card the engine asked
    /// for.
    ///
    /// The engine consumed this decision inside the action that opened the screen -
    /// see the <c>upcoming</c> parameter on <see cref="Apply(ActionRecord, IReadOnlyList{ActionRecord})"/> -
    /// so there is nothing left to send. What is left is to insist the decision was
    /// actually used: a selection that no screen consumed is an action recorded
    /// against a screen this run never opened.
    /// </summary>
    private void ConfirmCardSelectionWasConsumed(ActionRecord action)
    {
        if (_consumedSelections.Remove(action.Seq)) return;

        throw new EngineException(
            $"Action {action.Seq} selects a card from a screen, but no screen consumed it. A card selection " +
            "has to follow the action that opens its screen, with nothing else in between.");
    }

    /// <summary>
    /// Hands the manifest's card picks to the selector before an action that may open
    /// a screen over the deck.
    /// </summary>
    private void QueueFollowingCardSelections(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
    {
        foreach (var next in upcoming)
        {
            if (next.Verb != ActionVerb.SelectCardFromScreen) break;
            _selector.Enqueue(new ManifestCardSelector.Pick(
                next.Seq, Arg.String(next, "card_id"), Arg.Int(next, "option_index")));
            _consumedSelections.Add(next.Seq);
        }
    }

    private void Select(ActionRecord action, RewardsSet set, Reward reward)
    {
        var taken = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward)
            .GetAwaiter().GetResult();
        Pump.Drain();

        // A refusal the selector already recorded says exactly which card disagreed,
        // and is raised by Apply. Reporting "the engine refused it" over the top of it
        // would bury the useful message under a vaguer one.
        if (!taken && !reward.SuccessfullySelected && _selector.Refusal is null)
        {
            throw new EngineException(
                $"Action {action.Seq} selected the {KindOf(reward)} reward and the engine refused it. " +
                $"The loot screen is {DescribeRewards(set)}.");
        }
    }

    /// <summary>The reward kinds this milestone's history meets, named as the loot
    /// screen names them rather than by the engine's internal type.</summary>
    private static string KindOf(Reward reward) => reward switch
    {
        GoldReward => "gold",
        PotionReward => "potion",
        RelicReward => "relic",
        CardReward => "card",
        _ => reward.GetType().Name,
    };

    private static string DescribeRewards(RewardsSet set) =>
        set.Rewards.Count == 0
            ? "empty"
            : string.Join(", ", set.Rewards.Select(reward =>
                $"{KindOf(reward)}{(reward.SuccessfullySelected ? " (taken)" : "")}"));

    private void MoveToMapNode(int act, int row, int column)
    {
        if (_openRewards is { } open &&
            !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(open))
        {
            // The engine would skip the rest of the set on the way out and say nothing.
            // A history that walked away from unclaimed loot would then replay exactly
            // like one that declined it on purpose, which is the difference between a
            // reconstruction and a plausible story.
            throw new EngineException(
                $"A map move leaves the room while its loot screen is still open ({DescribeRewards(open)}). " +
                "Every reward is either taken or explicitly skipped; leaving would discard the rest with no " +
                "record that anybody decided to.");
        }

        if (act != _session.RunState.CurrentActIndex)
        {
            throw new EngineException(
                $"Map move names act {act}, but the run is in act {_session.RunState.CurrentActIndex}.");
        }

        var map = _session.RunState.Map
            ?? throw new EngineException("The current act has no map, so no node can be entered.");

        var point = map.GetPoint(column, row)
            ?? throw new EngineException($"Map node (row {row}, column {column}) does not exist in this act.");

        if (point.PointType == MapPointType.Unassigned)
        {
            throw new EngineException($"Map node (row {row}, column {column}) is empty in this act.");
        }

        var currentCoord = _session.RunState.CurrentMapCoord
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

        OfferRoomEndRewardsIfCombatEnded();
    }

    private Creature? ResolveTarget(ActionRecord action, CardModel card)
    {
        if (card.TargetType != TargetType.AnyEnemy)
        {
            if (action.Args.ContainsKey("target_index"))
            {
                throw new EngineException(
                    $"Action {action.Seq} supplies target_index for {card.Id}, but that card does not target an enemy.");
            }
            return null;
        }

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

        OfferRoomEndRewardsIfCombatEnded();
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
