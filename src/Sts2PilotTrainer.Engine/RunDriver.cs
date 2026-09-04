using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
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
///
/// Inside the retail client neither stand-in is installed and neither is wanted,
/// because the screens they answer are on a player's screen. The driver is narrowed
/// there to the recording's decisions before a fight, and it hands the engine's work
/// back to the host to wait for rather than draining it - a frame loop is what
/// drains the queue in there, and blocking for it on the frame thread wedges the
/// game. See <see cref="VerbsAllowedInRunningGame"/> and <see cref="Pending"/>.
/// </summary>
public sealed class RunDriver : IDisposable
{
    /// <summary>
    /// The verbs this driver will issue inside a running retail client.
    ///
    /// Everything before the fight, and nothing in it. Two reasons, and both are
    /// about not taking something away from the player: the decisions before a fight
    /// are the recording's and the fight is theirs, and the stand-ins this driver
    /// installs for the loot and card screens exist because a headless process has no
    /// UI - the retail client has one, and intercepting it would answer a screen the
    /// player was looking at. See docs/headless-fidelity.md.
    /// </summary>
    private static readonly ActionVerb[] VerbsAllowedInRunningGame =
        [ActionVerb.ChooseNeowBlessing, ActionVerb.ChooseEventOption, ActionVerb.MapMove];

    private readonly GameSession _session;
    private readonly ManifestCardSelector _selector = new();
    private readonly IDisposable? _selectorScope;
    private readonly Func<RewardsSet, Task>? _previousRewardsSelector;

    /// <summary>
    /// Whether this driver is standing in for a UI that does not exist, or issuing
    /// commands inside one that does.
    ///
    /// Read from <see cref="EngineHost.Origin"/> rather than passed in, so a host
    /// cannot claim to be the other one.
    /// </summary>
    private readonly bool _insideRunningGame;

    /// <summary>
    /// Engine work the last action started and this driver deliberately did not wait
    /// for, or null.
    ///
    /// Only ever set inside a running game. The headless host drains the engine to
    /// idle after every action, because it owns the process and there are no frames
    /// to do it. The retail client's action executor runs on its frame loop, on the
    /// thread this call arrives on, so blocking for it there would wedge the game
    /// rather than settle it. The host awaits this across its own frames instead.
    /// </summary>
    public Task? Pending { get; private set; }

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

    /// <summary>The treasure room whose chest has already been opened, for the same
    /// reason: opening it twice would roll its gold twice.</summary>
    private AbstractRoom? _chestOpenedForRoom;

    /// <summary>The treasure room whose relic the manifest has decided about, so that
    /// leaving one undecided can be refused.</summary>
    private AbstractRoom? _chestRelicDecidedForRoom;

    /// <summary>Sequence numbers of card selections a screen has already consumed,
    /// so the action that records each one can insist it was used.</summary>
    private readonly HashSet<int> _consumedSelections = [];

    /// <summary>
    /// How a map move is issued inside a running game, or null headlessly.
    ///
    /// Supplied by the host rather than called from here, because in the client a map
    /// move is a screen's command and this project keeps screens out of the engine
    /// owner. It is not optional there: measured, entering the coord directly leaves
    /// the client standing on the map with the next room built behind it and its
    /// combat never dealt, because the screen transition that a clicked node runs
    /// never happens. See docs/in-game-host.md.
    /// </summary>
    private readonly Func<MapCoord, Task>? _travelInRunningGame;

    public RunDriver(GameSession session, Func<MapCoord, Task>? travelInRunningGame = null)
    {
        _session = session;
        _travelInRunningGame = travelInRunningGame;
        _insideRunningGame = EngineHost.Origin == EngineOrigin.RunningGame;

        // Neither stand-in is installed inside the retail client. Both of them answer
        // a screen on the player's behalf, and in there the player is the one looking
        // at it.
        if (_insideRunningGame) return;

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

        // A chest's relic is awarded by the relic screen, not by the synchronizer that
        // decides who gets it. The synchronizer announces the outcome and the screen
        // hands the relic over; with no screen the run would pick a relic and never
        // receive one. Subscribed here for the run's lifetime because the synchronizer
        // outlives any one room.
        RunManager.Instance.TreasureRoomRelicSynchronizer.RelicsAwarded += AwardChestRelics;
        _chestRelicsSubscribed = true;
    }

    /// <summary>Whether this driver subscribed to the chest's award announcement, so
    /// that disposing one that did not cannot unsubscribe another's handler.</summary>
    private bool _chestRelicsSubscribed;

    /// <summary>
    /// Hands over the relics the chest's own synchronizer just awarded, exactly as
    /// <c>NTreasureRoomRelicCollection</c> does when it animates them onto the belt.
    ///
    /// It decides nothing: which relic went to whom is in the results, and a result
    /// the synchronizer marked skipped is not obtained here either.
    /// </summary>
    private static void AwardChestRelics(List<RelicPickingResult> results)
    {
        foreach (var result in results
                     .Where(result => result.type != RelicPickingResultType.Skipped)
                     .Where(result => result.player is not null))
        {
            RelicCmd.Obtain(result.relic.ToMutable(), result.player!).GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        if (_chestRelicsSubscribed)
        {
            RunManager.Instance.TreasureRoomRelicSynchronizer.RelicsAwarded -= AwardChestRelics;
            _chestRelicsSubscribed = false;
        }

        if (_selectorScope is null) return;
        RewardsSet.testSelector = _previousRewardsSelector;
        _selectorScope.Dispose();
    }

    /// <summary>
    /// Lets the engine finish what an action started.
    ///
    /// Headlessly that means waiting for it, here, because nothing else will. Inside
    /// the retail client it means handing the task back to a host that can wait for
    /// it on the game's own frames; see <see cref="Pending"/>.
    /// </summary>
    private void Settle(Task work)
    {
        if (_insideRunningGame)
        {
            Pending = work;
            return;
        }

        work.GetAwaiter().GetResult();
        Pump.Drain();
    }

    /// <summary>The same, for an engine command that returns nothing and leaves its
    /// work on the queue.</summary>
    private void Settle()
    {
        if (_insideRunningGame)
        {
            Pending = Task.CompletedTask;
            return;
        }

        Pump.Drain();
    }

    private Player Player => _session.RunState.Players[0];

    /// <summary>
    /// Enters the run's first room. On a new run that is Neow's event.
    ///
    /// Headless only. The retail client enters the first act from inside its own
    /// start-run continuation, which also loads the scene the room is drawn on, and a
    /// second entry would be this host doing the game's job worse.
    /// </summary>
    public void EnterFirstRoom()
    {
        if (_insideRunningGame)
        {
            throw new EngineException(
                "The running game enters the first act itself, as part of starting a run. Entering it again " +
                "from here would generate the first room a second time.");
        }

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
        Pending = null;

        if (_insideRunningGame && !VerbsAllowedInRunningGame.Contains(action.Verb))
        {
            throw new EngineException(
                $"Action {action.Seq} uses verb '{action.Verb}', which this driver does not issue inside a " +
                "running game. Only the recording's decisions before a fight are replayed there; the fight " +
                "itself is the player's, and answering a screen they are looking at would take a decision " +
                "away from them.");
        }

        switch (action.Verb)
        {
            case ActionVerb.ChooseNeowBlessing:
                QueueFollowingCardSelections(action, upcoming);
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
                PlayCard(action, upcoming);
                break;

            case ActionVerb.EndTurn:
                EndTurn(action, upcoming);
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

            case ActionVerb.ChooseRestSiteOption:
                ChooseRestSiteOption(action, upcoming);
                break;

            case ActionVerb.TakeChestRelic:
                TakeChestRelic(action);
                break;

            case ActionVerb.SkipChestRelic:
                SkipChestRelic(action);
                break;

            case ActionVerb.ProceedToNextAct:
                ProceedToNextAct();
                break;

            case ActionVerb.ShopPurchase:
                ShopPurchase(action, upcoming);
                break;

            case ActionVerb.UsePotion:
                UsePotion(action, upcoming);
                break;

            case ActionVerb.DiscardPotion:
                DiscardPotion(action);
                break;

            default:
                throw Unhandled(action);
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
                $"Action {action.Seq} ({action.Verb}) queued card selection(s) that no screen asked for: " +
                $"{_selector.DescribePending()}. A recorded selection the engine never consumed means the " +
                "manifest describes a screen this run does not open.");
        }
    }

    /// <summary>
    /// The refusal for a verb no case above handles.
    ///
    /// Which refusal depends on <see cref="EngineCommands"/>, because the two ways of
    /// arriving here are different defects. A verb the table does not map is one this
    /// build has not implemented, and the reason it has not is written down there. A
    /// verb the table does map and the switch does not handle is drift between the
    /// two, which is a defect in this file rather than a limit of this build, and
    /// saying so is what makes the table's coverage checkable at all.
    /// </summary>
    private static EngineException Unhandled(ActionRecord action) =>
        EngineCommands.For(action.Verb) is { } mapped
            ? new EngineException(
                $"Action {action.Seq} uses verb '{action.Verb}', which EngineCommands maps onto " +
                $"{mapped.Describe()} and this driver {EngineCommands.SwitchDriftMarker}. The table and " +
                "the switch have drifted; one of the two is wrong.")
            : new EngineException(
                $"Action {action.Seq} uses verb '{action.Verb}', which the format names but this build " +
                "does not implement. Refusing: a verb that silently does nothing would produce a replay " +
                $"that looks complete and is not. {EngineCommands.UnmappedReason(action.Verb)}");

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

    /// <summary>
    /// Answers a card screen the manifest is silent about from the front of what it
    /// offered, and remembers what it answered.
    ///
    /// The fixture generator's, and nothing else's: see
    /// <see cref="ManifestCardSelector.AnswersFromTheFrontWhenSilent"/>. A driver
    /// standing in for a recording refuses instead, because a screen nobody wrote
    /// down is a decision nobody made.
    /// </summary>
    internal void ImproviseUnrecordedCardSelections() =>
        _selector.AnswersFromTheFrontWhenSilent = true;

    /// <summary>The screen answers the last action improvised, as the arguments a
    /// <see cref="ActionVerb.SelectCardFromScreen"/> records, in order.</summary>
    internal IReadOnlyList<(string CardId, int OptionIndex)> TakeImprovisedCardSelections() =>
        _selector.TakeImprovised().Select(pick => (pick.CardId, pick.OptionIndex)).ToList();

    /// <summary>
    /// The kinds still unclaimed on the loot screen, or empty when none is open.
    ///
    /// For the fixture generator, which has to decide what a mechanically generated
    /// history claims and cannot ask the engine directly: the set is parked here by
    /// the stand-in above rather than held anywhere the run state can see.
    /// </summary>
    internal IReadOnlyList<string> UnclaimedRewardKinds =>
        _openRewards is { } set && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(set)
            ? set.Rewards.Where(reward => !reward.SuccessfullySelected).Select(KindOf).ToList()
            : [];

    /// <summary>
    /// The cards the loot screen's unclaimed card reward is offering, or empty when it
    /// has none.
    ///
    /// For the fixture generator, and for the same reason as above: a card reward is
    /// answered from inside the call that opens it, so a generated history has to be
    /// able to name the card it took before making that call.
    /// </summary>
    internal IReadOnlyList<string> OfferedCardIds =>
        _openRewards is { } set && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(set)
            ? set.Rewards.OfType<CardReward>().FirstOrDefault(reward => !reward.SuccessfullySelected)
                ?.Cards.Select(card => card.Id.ToString()).ToList() ?? []
            : [];

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

        var localEvent = LocalEvent()
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

    /// <summary>
    /// The event this player is in, or null when there is none.
    ///
    /// The engine's own reader indexes a per-player list and throws out of range when
    /// the room has no event for this player, which is what a run that diverged
    /// before this floor looks like from here. Turned into the refusal it is: the
    /// alternative is an index exception escaping a replay, which says nothing about
    /// the recording and reads as a defect in this tool rather than a divergence in
    /// the run.
    /// </summary>
    private static EventModel? LocalEvent()
    {
        var synchronizer = RunManager.Instance.EventSynchronizer;
        if (synchronizer is null) return null;

        try
        {
            return synchronizer.GetLocalEvent();
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new EngineException(
                "This room has no event for this player, so there is no option list to choose from. The run " +
                "generated here is not the one the recording describes; a divergence before this floor is " +
                "what reaches this point.");
        }
    }

    private void ChooseEventOption(int optionIndex)
    {
        var synchronizer = RunManager.Instance.EventSynchronizer
            ?? throw new EngineException("No event synchronizer: the run is not in an event room.");

        var localEvent = LocalEvent()
            ?? throw new EngineException("No event is in progress, so no option can be chosen.");

        var options = localEvent.CurrentOptions;
        if (optionIndex < 0 || optionIndex >= options.Count)
        {
            throw new EngineException(
                $"Event option {optionIndex} does not exist; this event offers {options.Count} " +
                $"({string.Join(", ", options.Select((o, i) => $"{i}:{o.GetType().Name}"))}).");
        }

        synchronizer.ChooseLocalOption(optionIndex);
        Settle();
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
    /// Drinks one potion off the belt.
    ///
    /// The slot is what the video shows and the potion id is what makes a belt that
    /// has drifted fail here rather than drink whatever is in that slot. Targeting is
    /// the same question a played card asks and is answered the same way.
    ///
    /// A potion can open a screen over the deck or the hand, so the manifest's
    /// following selections are queued before the engine is asked to drink it.
    /// </summary>
    private void UsePotion(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
    {
        var potion = PotionInSlot(action);
        // Only an enemy is ever named. Everything else the potion aims at is the
        // engine's own default - EnqueueManualUse fills in the drinker where that is
        // a target the potion accepts - and second-guessing it here would refuse
        // potions the retail client drinks without asking anybody anything.
        var target = ResolveTarget(action, potion.TargetType, potion.Id.ToString());

        QueueFollowingCardSelections(action, upcoming);

        potion.EnqueueManualUse(target);
        Pump.Drain();

        if (Player.PotionSlots[Arg.Int(action, "slot_index")] == potion)
        {
            throw new EngineException(
                $"Action {action.Seq} drank {potion.Id} and it is still on the belt afterwards, so the " +
                "potion was not used.");
        }

        OfferRoomEndRewardsIfCombatEnded();
    }

    /// <summary>
    /// Throws one potion away.
    ///
    /// A decision, and a real one: the belt has three slots and a fourth potion is
    /// only reachable by giving one up. Issued the way the potion popup's discard
    /// button issues it, as an action on the run's own queue.
    /// </summary>
    private void DiscardPotion(ActionRecord action)
    {
        var potion = PotionInSlot(action);
        var slot = Arg.Int(action, "slot_index");

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new DiscardPotionGameAction(Player, (uint)slot, CombatManager.Instance?.IsInProgress ?? false));
        Pump.Drain();

        if (Player.PotionSlots[slot] == potion)
        {
            throw new EngineException(
                $"Action {action.Seq} discarded {potion.Id} from slot " +
                $"{slot.ToString(System.Globalization.CultureInfo.InvariantCulture)} and it is still there " +
                "afterwards, so the discard did not take effect.");
        }
    }

    /// <summary>The potion the manifest names, in the slot the manifest names.</summary>
    private PotionModel PotionInSlot(ActionRecord action)
    {
        var slot = Arg.Int(action, "slot_index");
        var expectedId = Arg.String(action, "potion_id");
        var belt = Player.PotionSlots;

        if (slot < 0 || slot >= belt.Count)
        {
            throw new EngineException(
                $"Action {action.Seq} ({action.Verb}) names potion slot {slot}, but the belt has " +
                $"{belt.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} slot(s): " +
                $"{DescribeBelt(belt)}.");
        }

        var potion = belt[slot]
            ?? throw new EngineException(
                $"Action {action.Seq} ({action.Verb}) names potion slot {slot}, which is empty. The belt " +
                $"is {DescribeBelt(belt)}.");

        return potion.Id.ToString() == expectedId
            ? potion
            : throw new EngineException(
                $"Action {action.Seq} ({action.Verb}) expects {expectedId} in potion slot {slot}, but the " +
                $"belt holds {potion.Id}. The belt is {DescribeBelt(belt)}. The replay has diverged from " +
                "the recorded history before this point.");
    }

    private static string DescribeBelt(IReadOnlyList<PotionModel?> belt) =>
        string.Join(", ", belt.Select((potion, index) => $"{index}:{potion?.Id.ToString() ?? "empty"}"));

    /// <summary>
    /// Buys one thing from the merchant.
    ///
    /// The kind names which shelf it came off, and the index is the position on that
    /// shelf. Not one flat position across the whole shop: the merchant's inventory is
    /// four separate lists plus a card removal, and flattening them would invent an
    /// ordering the engine does not have. The identity is checked as well, for the
    /// reason a played card's is - a shop that stocked differently means the run has
    /// already diverged, and buying whatever sits in that slot would hide it.
    ///
    /// A card removal buys a service rather than a thing, and opens a screen over the
    /// deck; the card that came off it is a separate recorded selection, answered
    /// through the same selector as every other card screen.
    /// </summary>
    private void ShopPurchase(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
    {
        if (_session.RunState.CurrentRoom is not MerchantRoom shop)
        {
            throw new EngineException(
                $"Action {action.Seq} buys from the merchant, but this floor is a " +
                $"{_session.RunState.CurrentRoom?.RoomType.ToString() ?? "no"} room, not a shop.");
        }

        var inventory = shop.GetLocalInventory();
        var kind = Arg.String(action, "kind");
        var entry = kind == ShopPurchaseKinds.CardRemoval
            ? CardRemovalEntry(action, inventory)
            : StockedEntry(action, inventory, kind);

        if (!entry.EnoughGold)
        {
            throw new EngineException(
                $"Action {action.Seq} buys a '{kind}' costing " +
                $"{entry.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)} and the run has " +
                $"{Player.Gold.ToString(System.Globalization.CultureInfo.InvariantCulture)} gold. The " +
                "replay has diverged from the recorded history before this point.");
        }

        QueueFollowingCardSelections(action, upcoming);

        var bought = entry.OnTryPurchaseWrapper(inventory).GetAwaiter().GetResult();
        Pump.Drain();

        // A refusal the selector already recorded names the card that disagreed, and
        // Apply raises it; reporting the purchase failure over the top would bury it.
        if (!bought && _selector.Refusal is null)
        {
            throw new EngineException(
                $"Action {action.Seq} bought a '{kind}' from the merchant and the engine refused the " +
                "purchase.");
        }
    }

    /// <summary>The card removal on offer, or a refusal saying it is not.</summary>
    private static MerchantEntry CardRemovalEntry(ActionRecord action, MerchantInventory inventory)
    {
        var removal = inventory.CardRemovalEntry
            ?? throw new EngineException(
                $"Action {action.Seq} buys a card removal, and this merchant is not offering one.");

        return removal.IsStocked
            ? removal
            : throw new EngineException(
                $"Action {action.Seq} buys a card removal that this merchant has already sold. A shop " +
                "removes one card per visit.");
    }

    /// <summary>
    /// The stocked item at a shelf position, checked against the id the manifest
    /// names.
    /// </summary>
    private static MerchantEntry StockedEntry(
        ActionRecord action, MerchantInventory inventory, string kind)
    {
        var shelf = Shelf(action, inventory, kind);
        var index = Arg.Int(action, "option_index");
        var expectedId = Arg.String(action, ShopPurchaseKinds.IdArgument(kind)!);

        if (index < 0 || index >= shelf.Count)
        {
            throw new EngineException(
                $"Action {action.Seq} buys '{kind}' {index}, but this merchant stocks {shelf.Count}: " +
                $"{DescribeShelf(shelf)}.");
        }

        var entry = shelf[index];
        if (!entry.IsStocked)
        {
            throw new EngineException(
                $"Action {action.Seq} buys '{kind}' {index}, which this merchant has already sold. The " +
                $"shelf is {DescribeShelf(shelf)}.");
        }

        if (IdOf(entry) != expectedId)
        {
            throw new EngineException(
                $"Action {action.Seq} expects {expectedId} at '{kind}' {index}, but the merchant stocks " +
                $"{IdOf(entry)}. The shelf is {DescribeShelf(shelf)}. The replay has diverged from the " +
                "recorded history before this point.");
        }

        return entry;
    }

    private static IReadOnlyList<MerchantEntry> Shelf(
        ActionRecord action, MerchantInventory inventory, string kind) => kind switch
    {
        ShopPurchaseKinds.CharacterCard => [.. inventory.CharacterCardEntries],
        ShopPurchaseKinds.ColorlessCard => [.. inventory.ColorlessCardEntries],
        ShopPurchaseKinds.Relic => [.. inventory.RelicEntries],
        ShopPurchaseKinds.Potion => [.. inventory.PotionEntries],
        _ => throw new EngineException(
            $"Action {action.Seq} buys a '{kind}', which is not something a merchant sells. Known kinds: " +
            $"{string.Join(", ", ShopPurchaseKinds.All)}."),
    };

    /// <summary>What a stocked shelf entry is, as the model id the video shows.</summary>
    private static string IdOf(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry card => card.CreationResult?.Card.Id.ToString() ?? "(sold)",
        MerchantRelicEntry relic => relic.Model?.Id.ToString() ?? "(sold)",
        MerchantPotionEntry potion => potion.Model?.Id.ToString() ?? "(sold)",
        _ => entry.GetType().Name,
    };

    private static string DescribeShelf(IReadOnlyList<MerchantEntry> shelf) =>
        shelf.Count == 0
            ? "empty"
            : string.Join(", ", shelf.Select((entry, index) =>
                $"{index}:{IdOf(entry)}{(entry.IsStocked ? "" : " (sold)")}"));

    /// <summary>
    /// Refuses to leave a room that is still holding a decision the manifest has not
    /// made.
    ///
    /// The engine discards both of these on the way out and says nothing - the rest of
    /// a loot screen in <c>RewardsSetSynchronizer.BeforeLeavingRoom</c>, an untaken
    /// chest relic when the treasure room exits. A history that simply omitted the
    /// decision would therefore replay into exactly the state of one that declined it,
    /// which is the difference between a reconstruction and a plausible story. Both
    /// declines have a verb.
    /// </summary>
    private void RefuseToLeaveAnUndecidedRoom(string leaving)
    {
        if (_openRewards is { } open &&
            !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(open))
        {
            throw new EngineException(
                $"{leaving} leaves the room while its loot screen is still open ({DescribeRewards(open)}). " +
                "Every reward is either taken or explicitly skipped; leaving would discard the rest with no " +
                "record that anybody decided to.");
        }

        if (_session.RunState.CurrentRoom is TreasureRoom chest &&
            !ReferenceEquals(_chestRelicDecidedForRoom, chest) &&
            RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics is { Count: > 0 } offered)
        {
            throw new EngineException(
                $"{leaving} leaves a treasure room whose chest is still offering {DescribeRelics(offered)}. " +
                "The relic is either taken or explicitly left behind; leaving would discard it with no " +
                "record that anybody decided to.");
        }
    }

    /// <summary>
    /// Says the run is finished with this act and moves it on to the next.
    ///
    /// The engine's own path, which is a vote rather than a call: the client marks the
    /// local player ready and the synchronizer enters the next act once everybody is.
    /// In a single-player run that is the same frame, and going straight to
    /// <c>EnterNextAct</c> instead would skip the act floor the vote advances.
    ///
    /// It is a decision because the run stays on the boss's floor until somebody makes
    /// it, and because everything still on offer there is discarded by it.
    /// </summary>
    private void ProceedToNextAct()
    {
        RefuseToLeaveAnUndecidedRoom("Proceeding to the next act");

        var from = _session.RunState.CurrentActIndex;
        var acts = _session.RunState.Acts.Count;
        if (from + 1 >= acts)
        {
            throw new EngineException(
                $"Proceeding to the next act, but this run's last act is " +
                $"{(acts - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} and it is " +
                "already in it. There is no next act to enter.");
        }

        RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
        Pump.Drain();

        if (_session.RunState.CurrentActIndex == from)
        {
            throw new EngineException(
                $"The run said it was ready to leave act " +
                $"{from.ToString(System.Globalization.CultureInfo.InvariantCulture)} and the engine did not " +
                "move it on. An act transition is only offered once the act's boss is beaten.");
        }
    }

    /// <summary>
    /// Opens the chest a treasure room puts in front of the player, exactly where the
    /// retail client does.
    ///
    /// The third screen with no engine command behind it. <c>NTreasureRoom.OpenChest</c>
    /// is what calls <c>TreasureRoom.DoNormalRewards</c> and
    /// <c>TreasureRoom.DoExtraRewardsIfNeeded</c>, and nothing else does, so a headless
    /// replay that walked into a treasure room would find an unopened chest and refuse
    /// every decision about it. This calls the same two methods at the same point and
    /// generates nothing itself.
    ///
    /// Opening is not a decision and so is not an action; the relic and any rewards set
    /// it puts up are, and both are refused where the manifest is silent. The relics
    /// themselves were already rolled when the room was entered, by the engine's own
    /// <c>BeginRelicPicking</c>. See docs/headless-fidelity.md.
    /// </summary>
    private void OpenTreasureChestIfEntered()
    {
        if (_session.RunState.CurrentRoom is not TreasureRoom room) return;
        if (ReferenceEquals(_chestOpenedForRoom, room)) return;

        _chestOpenedForRoom = room;
        room.DoNormalRewards().GetAwaiter().GetResult();
        Pump.Drain();
        room.DoExtraRewardsIfNeeded().GetAwaiter().GetResult();
        Pump.Drain();
    }

    /// <summary>The relics a treasure chest is offering, or a refusal naming the verb
    /// that needed one.</summary>
    private IReadOnlyList<RelicModel> OpenChestRelics(ActionRecord action)
    {
        if (_session.RunState.CurrentRoom is TreasureRoom room &&
            !ReferenceEquals(_chestRelicDecidedForRoom, room) &&
            RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics is { } relics)
        {
            return relics;
        }

        throw new EngineException(
            $"Action {action.Seq} ({action.Verb}) decides about a treasure chest's relic, and no chest is " +
            "offering one. A chest offers its relic from the moment the room is entered until the run " +
            "takes it or leaves it behind, and only once.");
    }

    /// <summary>
    /// Takes the relic a chest offered.
    ///
    /// Named as well as indexed, for the reason a played card is: the relic a chest
    /// rolls is a consequence of the whole run before it, and taking whatever sits at
    /// that position would hide a run that had already diverged.
    /// </summary>
    private void TakeChestRelic(ActionRecord action)
    {
        var relics = OpenChestRelics(action);
        var index = Arg.Int(action, "option_index");
        var expectedId = Arg.String(action, "relic_id");

        if (index < 0 || index >= relics.Count)
        {
            throw new EngineException(
                $"Action {action.Seq} takes chest relic {index}, but this chest offers {relics.Count}: " +
                $"{DescribeRelics(relics)}.");
        }

        if (relics[index].Id.ToString() != expectedId)
        {
            throw new EngineException(
                $"Action {action.Seq} expects {expectedId} at chest position {index}, but the engine " +
                $"offers {relics[index].Id}. The chest is {DescribeRelics(relics)}. The replay has " +
                "diverged from the recorded history before this point.");
        }

        _chestRelicDecidedForRoom = _session.RunState.CurrentRoom;
        RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(index);
        Pump.Drain();

        if (!Player.Relics.Any(relic => relic.Id.ToString() == expectedId))
        {
            throw new EngineException(
                $"Action {action.Seq} took {expectedId} from the chest and the run does not have it " +
                "afterwards, so the pick did not take effect.");
        }
    }

    /// <summary>
    /// Leaves a chest's relic behind.
    ///
    /// A decision, not an absence, for exactly the reason <see cref="SkipRewards"/>
    /// is: the engine discards an undecided relic when the room is left and says
    /// nothing, so a history that simply omitted the decision would replay into the
    /// same state as one that declined it.
    /// </summary>
    private void SkipChestRelic(ActionRecord action)
    {
        OpenChestRelics(action);
        _chestRelicDecidedForRoom = _session.RunState.CurrentRoom;
        RunManager.Instance.TreasureRoomRelicSynchronizer.SkipRelicLocally();
        Pump.Drain();
    }

    private static string DescribeRelics(IReadOnlyList<RelicModel> relics) =>
        relics.Count == 0
            ? "empty"
            : string.Join(", ", relics.Select((relic, index) => $"{index}:{relic.Id}"));

    /// <summary>
    /// Takes one of the rest site's options.
    ///
    /// Named as well as indexed, for the reason <see cref="PlayCard"/> is: the index
    /// is what the video shows somebody click, and the option id is what makes a rest
    /// site whose options came out differently fail here rather than take whatever is
    /// in that position. Which options a rest site offers depends on the run that
    /// reached it - relics add them and a hook can remove them - so position alone
    /// says nothing.
    ///
    /// Some options open a screen over the deck; upgrading a card is the ordinary one.
    /// Those are answered from the manifest through the same selector every other card
    /// screen goes through, which is why the following selections are queued first.
    /// </summary>
    private void ChooseRestSiteOption(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
    {
        if (_session.RunState.CurrentRoom is not RestSiteRoom)
        {
            throw new EngineException(
                $"Action {action.Seq} takes a rest site option, but this floor is a " +
                $"{_session.RunState.CurrentRoom?.RoomType.ToString() ?? "no"} room, not a rest site.");
        }

        var synchronizer = RunManager.Instance.RestSiteSynchronizer
            ?? throw new EngineException(
                $"Action {action.Seq} takes a rest site option, but no rest site is in progress.");

        var options = synchronizer.GetLocalOptions();
        var index = Arg.Int(action, "option_index");
        var expectedId = Arg.String(action, "option_id");

        if (index < 0 || index >= options.Count)
        {
            throw new EngineException(
                $"Action {action.Seq} takes rest site option {index}, but this rest site offers " +
                $"{options.Count}: {DescribeRestSiteOptions(options)}.");
        }

        if (options[index].OptionId != expectedId)
        {
            throw new EngineException(
                $"Action {action.Seq} expects rest site option {expectedId} at position {index}, but the " +
                $"engine offers {options[index].OptionId}. The rest site is " +
                $"{DescribeRestSiteOptions(options)}. The replay has diverged from the recorded history " +
                "before this point.");
        }

        QueueFollowingCardSelections(action, upcoming);

        var taken = synchronizer.ChooseLocalOption(index).GetAwaiter().GetResult();
        Pump.Drain();

        // A refusal the selector already recorded names the card that disagreed, and
        // Apply raises it; reporting "the engine refused it" over the top would bury
        // the useful message.
        if (!taken && _selector.Refusal is null)
        {
            throw new EngineException(
                $"Action {action.Seq} took rest site option {expectedId} and the engine refused it.");
        }
    }

    private static string DescribeRestSiteOptions(IReadOnlyList<RestSiteOption> options) =>
        options.Count == 0
            ? "empty"
            : string.Join(", ", options.Select((option, index) => $"{index}:{option.OptionId}"));

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
        RefuseToLeaveAnUndecidedRoom("A map move");

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

        var coord = new MapCoord(column, row);

        if (!_insideRunningGame)
        {
            Settle(RunManager.Instance.EnterMapCoord(coord));
            OpenTreasureChestIfEntered();
            return;
        }

        // Refused rather than approximated. The engine's own EnterMapCoord is the
        // middle of what a clicked node does, and doing only the middle produces a run
        // that has entered the room and a client that has not - which reads as a fight
        // that never opens.
        var travel = _travelInRunningGame
            ?? throw new EngineException(
                "A map move inside a running game has to go through the screen that owns it, and no way to " +
                "do that was supplied. Entering the map coordinate alone leaves the client on the map with " +
                "the next room built behind it.");

        Settle(travel(coord));
    }

    private void PlayCard(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
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

        // A card that prompts over the hand or the deck pulls its answer from inside
        // this call, exactly as an event option's enchantment screen does.
        QueueFollowingCardSelections(action, upcoming);

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

    private Creature? ResolveTarget(ActionRecord action, CardModel card) =>
        ResolveTarget(action, card.TargetType, card.Id.ToString());

    /// <summary>
    /// Which enemy this decision was aimed at, or null when it aims at nothing the
    /// player chose.
    ///
    /// Asked of the model's own target type rather than of the verb, because a card
    /// and a potion ask the same question and the engine answers it the same way.
    /// </summary>
    private Creature? ResolveTarget(ActionRecord action, TargetType targetType, string modelId)
    {
        if (targetType != TargetType.AnyEnemy)
        {
            if (action.Args.ContainsKey("target_index"))
            {
                throw new EngineException(
                    $"Action {action.Seq} supplies target_index for {modelId}, but that does not target an enemy.");
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
            0 => throw new EngineException(
                $"Action {action.Seq} aims {modelId} at an enemy and none is alive."),
            _ => throw new EngineException(
                $"Action {action.Seq} uses {modelId}, which targets one enemy, and {enemies.Count} are alive. " +
                "'target_index' is required - choosing one here would be inventing a decision the player made."),
        };
    }

    private void EndTurn(ActionRecord action, IReadOnlyList<ActionRecord> upcoming)
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

        // An end of turn can prompt too - a power that discards down to a hand size
        // asks which cards go - and the prompt is answered from inside this call.
        QueueFollowingCardSelections(action, upcoming);

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
