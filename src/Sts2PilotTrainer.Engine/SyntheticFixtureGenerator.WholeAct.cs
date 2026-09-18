using System.Globalization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The journey that walks an act from Neow to the far side of its boss.
///
/// Everything in the decision alphabet past a fight is only reachable from a history
/// that has been somewhere - a shop has to be walked into, a chest has to be opened,
/// an act transition needs a beaten boss. A recording of a real run has all of that
/// and cannot be used to test the machinery that reads recordings, because then the
/// expected values would come from the thing under test. This is the same shape of
/// history with no video and no player behind it.
///
/// Every choice it makes is a fixed rule over what the engine reports: the route is
/// planned by cost before a step is taken, the fight plays its first playable attack,
/// the loot is taken and whatever is left is declined, a rest site forges when the run
/// is healthy and heals when it is not, and the shop is emptied cheapest first. Each
/// of those rules is here because without it the journey does not finish an act, and
/// not one of them is a claim about how to play. The fixture must not be read as one.
/// </summary>
public static partial class SyntheticFixtureGenerator
{
    /// <summary>
    /// The one node type this route will not enter.
    ///
    /// A question mark resolves to whatever the run's own stream says when it is
    /// entered, and what it resolves to can open a room this journey has no rules for.
    /// A history that walked into one and then refused would be a fixture that fails
    /// for a reason nobody is testing.
    /// </summary>
    private const MapPointType NotRouted = MapPointType.Unknown;

    /// <summary>The room types the journey has to visit for the verbs that only exist
    /// there to be exercised at all.</summary>
    private static readonly MapPointType[] RequiredTypes =
        [MapPointType.Shop, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Elite];

    /// <summary>The node types the route under way has to pass through, one coverage
    /// bit each: the fixture's four, the policy's own list, or none for a walk that
    /// only has to reach the boss.</summary>
    private static MapPointType[] _requiredTypes = RequiredTypes;

    /// <summary>The coverage the walk under way demands of its route: every bit of
    /// <see cref="_requiredTypes"/>.</summary>
    private static int RequiredCoverage => (1 << _requiredTypes.Length) - 1;

    /// <summary>The choices the walk under way consults; the fixture's are the defaults.</summary>
    private static WalkPolicy _policy = WalkPolicy.Default;

    /// <summary>
    /// Whether the route under way passes its required types in the order the policy
    /// lists them and may end at the node that completes them, rather than passing
    /// them in any order on the way to the boss: a walk after a relic the bag deals is
    /// after the room that deals it and then the room its ask is met in - the chest
    /// and then a fight or a rest site, the shop and then a fight - and a map whose
    /// only way from there to the boss passes a question mark still has both rooms.
    /// A walk after a relic an ancient deals holds it from the first room and is
    /// after the one room its ask is met in, for the same reason.
    /// </summary>
    private static bool RouteIsOrdered => _policy.Relic is not null && _policy.StopOnceMet && _policy.RouteThrough is not null;

    /// <summary>Which of the policy's one-time asks the walk under way has met, and
    /// whether any ask has been, for a walk that stops there.</summary>
    private static bool _declinedACardReward;
    private static bool _drankOnTheMap;
    private static bool _discardedOnTheMap;
    private static bool _travelledFreely;
    private static bool _askMet;

    private static ReplayManifest GenerateWholeAct()
    {
        var identity = RequireSupportedBuild();
        string[] acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];
        var session = new GameSession();
        session.StartRun(WholeActSeed, "CHARACTER.IRONCLAD", 0, "standard", acts);
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();

        var checkpoints = new List<Checkpoint>();
        var actions = WalkTheAct(session, driver, checkpoints).Actions;

        return new ReplayManifest
        {
            RunId = "synthetic-v0111-whole-act",
            Environment = new EnvironmentIdentity
            {
                BuildVersion = Fact<string>.Declared(identity.BuildVersion),
                BuildDateUtc = Fact<string>.Declared(identity.BuildDateUtc),
                GameMode = Fact<string>.Declared("standard"),
                Seed = Fact<string>.Declared(WholeActSeed),
                ContentHash = Fact<string>.Declared(identity.ContentHash),
                Ascension = Fact<int>.Declared(0),
                Unlocks = Fact<UnlockRequirement>.Declared(UnlockRequirement.Complete(
                    "Generated by this arbiter against UnlockState.all, so the requirement is a property of " +
                    "how the fixture was produced rather than a claim about any player.")),
                Character = Fact<string>.Declared("CHARACTER.IRONCLAD"),
                Acts = Fact<IReadOnlyList<string>>.Declared(acts),
                Mods = Fact<ModEnvironment>.Declared(new ModEnvironment
                {
                    Name = "vanilla-headless-v0.111.0",
                    ReportedCount = 0,
                    Mods = [],
                }),
            },
            Source = new SourceProvenance
            {
                Kind = "synthetic-engine",
                Synthetic = new SyntheticSource
                {
                    FixtureId = "v0111-whole-act",
                    FixtureVersion = FixtureVersion,
                    Generator = "sts2-pilot-trainer",
                    GeneratedBuild = identity.BuildVersion,
                },
                ExtractionMethod = "engine-generated",
                Coverage =
                    "Mechanically generated first act: every fight on one planned route played to its end " +
                    "and its loot taken, a shop emptied, a treasure chest opened, rest sites rested and " +
                    "forged at, an elite, and the act's boss, through the act transition into the next act.",
            },
            Actions = actions,
            Checkpoints = checkpoints,
        };
    }

    /// <summary>
    /// The journey itself, from Neow's blessing through the act transition, on a run
    /// already stood in its first room.
    ///
    /// Separate from the fixture so the same walk can be watched: the recorder's
    /// headless proof of a won run plays this act on a run of this act alone, where
    /// the transition opens the victory room instead of an act, through the recorder.
    /// </summary>
    /// <param name="afterEachDecision">Run after every decision the walk applies,
    /// including the screen answers the driver improvised for it; a recorder watching
    /// the walk settles each decision here before the next is made.</param>
    /// <param name="visitEveryRoomType">Whether the route has to visit a shop, a rest
    /// site, a treasure room and an elite on the way, which the fixture needs for the
    /// verbs that only exist there; a walk that only has to reach the boss takes the
    /// cheapest route there instead.</param>
    /// <param name="policy">The choices the walk consults at the decisions it has a
    /// rule for; today's rules where none is given. See <see cref="WalkPolicy"/>.</param>
    /// <returns>The decisions made, and whether the policy's ask was met on the way:
    /// a walk that finished without meeting it made the decision nowhere, and a
    /// test that asked for one has to be told so rather than find the verb elsewhere.</returns>
    internal static ActWalk WalkTheAct(
        GameSession session, RunDriver driver, List<Checkpoint> checkpoints,
        Action? afterEachDecision = null, bool visitEveryRoomType = true, WalkPolicy? policy = null)
    {
        var actions = new List<ActionRecord>();
        var previous = (_afterEachDecision, _requiredTypes, _policy);
        _policy = policy ?? WalkPolicy.Default;
        (_afterEachDecision, _requiredTypes) =
            (afterEachDecision, _policy.RouteThrough is { } through
                ? [.. through]
                : visitEveryRoomType ? RequiredTypes : []);
        (_declinedACardReward, _drankOnTheMap, _discardedOnTheMap, _travelledFreely, _askMet) =
            (false, false, false, false, false);
        try
        {
            WalkTheActFrom(session, driver, actions, checkpoints);
            return new ActWalk(actions, _askMet);
        }
        finally
        {
            (_afterEachDecision, _requiredTypes, _policy) = previous;
        }
    }

    /// <summary>What a walk of the act produced: its decisions in order, and whether
    /// the policy's one ask was met at any of them.</summary>
    internal sealed record ActWalk(List<ActionRecord> Actions, bool AskMet);

    private static void WalkTheActFrom(
        GameSession session, RunDriver driver, List<ActionRecord> actions, List<Checkpoint> checkpoints)
    {
        OpenTheRun(driver, session, actions);

        // A blessing or an ancient's offer that grants a relic can put a rewards set on
        // offer from the relic's own work - Kaleidoscope's cards, Small Capsule's
        // relic, Toy Box's - which is answered before the run moves, the way a player
        // answers it before the map opens
        MeetTheAskIf(session, _policy.ObtainingIsTheAsk);
        TakeWhatWasOffered(driver, session, actions);
        if (_policy.StopOnceMet && _askMet) return;

        var route = PlannedRoute(session);
        while (route.Count > 0)
        {
            var next = route.Dequeue();

            // A node the game offers and the planned route could not: the route is
            // planned again from wherever the flight landed
            var flown = FreeTravelTarget(session);
            if (flown is not null)
            {
                next = flown;
                _travelledFreely = true;
                MeetTheAskIf(session, true);
            }

            Apply(driver, actions, ActionVerb.MapMove,
                ("act", session.RunState.CurrentActIndex.ToString(CultureInfo.InvariantCulture)),
                ("row", next.coord.row.ToString(CultureInfo.InvariantCulture)),
                ("column", next.coord.col.ToString(CultureInfo.InvariantCulture)));

            checkpoints.Add(Capture(
                $"floor-{Field(session, "run.total_floor")}-entry", actions[^1].Seq, session,
                "run.total_floor", "run.map_coord", "player.hp", "player.gold"));

            HandleRoom(driver, session, actions, checkpoints, next.PointType);
            UseTheBeltOnTheMap(driver, session, actions);
            if (_policy.StopOnceMet && _askMet) return;
            if (flown is not null) route = PlannedRoute(session);
        }

        // A route that ended at the room that completed its coverage stands nowhere
        // an act can be left from; the walk is over, its ask met or not
        if (RouteIsOrdered && session.RunState.CurrentRoom is not { RoomType: RoomType.Boss }) return;

        Apply(driver, actions, ActionVerb.ProceedToNextAct);
        checkpoints.Add(Capture("act-two-entry", actions[^1].Seq, session,
            "run.act_index", "run.total_floor", "player.hp", "player.deck_count", "player.relics"));
    }

    private static Queue<MapPoint> PlannedRoute(GameSession session)
    {
        var route = PlanRoute(session);
        if (route.Count > MapMoveLimit)
        {
            throw new EngineException(
                $"The planned act route is {route.Count.ToString(CultureInfo.InvariantCulture)} moves long, " +
                $"past the {MapMoveLimit.ToString(CultureInfo.InvariantCulture)} this journey allows. An act " +
                "is sixteen rows and its boss; anything longer is a routing defect rather than a long act.");
        }

        return new Queue<MapPoint>(route);
    }

    /// <summary>Whether the run holds the relic the policy names, or the policy names
    /// none: the condition under which an ask past the relic counts, read off the
    /// run rather than remembered, because the run is what holds it.</summary>
    private static bool HoldsTheRelic(GameSession session) =>
        _policy.Relic is not { } relic ||
        session.RunState.Players[0].Relics.Any(held => held.Id.ToString() == relic);

    /// <summary>Counts the ask met where the condition holds and the policy's relic,
    /// if it names one, is held.</summary>
    private static void MeetTheAskIf(GameSession session, bool condition)
    {
        if (condition && HoldsTheRelic(session)) _askMet = true;
    }

    /// <summary>Answers a rewards set a decision's own work put on offer - a relic's
    /// <c>AfterObtained</c>, a heal's potions - the way a fight's loot is answered,
    /// so the room is left holding no undecided offer.</summary>
    private static void TakeWhatWasOffered(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        if (driver.UnclaimedRewardKinds.Count > 0) TakeTheLoot(driver, session, actions);
    }

    /// <summary>
    /// The run's opening decision: Neow's blessing where the run opens on Neow's room,
    /// which every run of the default progression does, and the ancient's offer where
    /// it opens on an act's ancient - a run whose acts list begins at act 2 or 3, which
    /// the engine builds the way it builds the won-run proof's one-act run. The
    /// recorder writes the two as different verbs, so the walk does too: the blessing
    /// carries no event id and the ancient's page is answered by
    /// <see cref="ActionVerb.ChooseEventOption"/> naming the ancient and the key.
    /// </summary>
    private static void OpenTheRun(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        var opening = RunManager.Instance.EventSynchronizer?.GetLocalEvent()
            ?? throw new EngineException("The act journey is not standing in the opening event.");
        var options = opening.CurrentOptions;

        if (opening is Neow)
        {
            Apply(driver, actions, ActionVerb.ChooseNeowBlessing,
                ("option_index", OpeningOption(options, _policy.NeowRelic).ToString(CultureInfo.InvariantCulture)));
            return;
        }

        if (opening is not AncientEventModel)
        {
            throw new EngineException(
                $"The act journey opened on {opening.Id}, which is neither Neow nor an act's ancient, and it has " +
                "no rule for the first room of such a run.");
        }

        var index = OpeningOption(options, _policy.AncientRelic);
        Apply(driver, actions, ActionVerb.ChooseEventOption,
            ("event_id", opening.Id.ToString()),
            ("option_index", index.ToString(CultureInfo.InvariantCulture)),
            ("option_key", RunDriver.OptionKey(options[index])));
    }

    /// <summary>The opening option the walk takes: the one granting the relic the
    /// policy names where the opening offers it, and the first option otherwise.</summary>
    private static int OpeningOption(IReadOnlyList<EventOption> options, string? relic)
    {
        if (relic is null) return 0;
        var index = options.ToList().FindIndex(option => option.Relic?.Id.ToString() == relic);
        return index < 0 ? 0 : index;
    }

    /// <summary>
    /// The node a free-travel policy walks to from where the run stands, or null.
    ///
    /// The nodes the game's own travel rule offers that the node being left does not
    /// lead to, which is empty without a live free-travel hook; among them the
    /// cheapest by the route's own weighting the journey has room rules for, leftmost
    /// first, and never a question mark, an elite or the boss. Once per walk, because
    /// the ask is one flight and the walk is after that decision.
    /// </summary>
    private static MapPoint? FreeTravelTarget(GameSession session)
    {
        if (!_policy.TravelFreely || _travelledFreely) return null;
        if (session.RunState.Map is not { } map || session.RunState.CurrentMapCoord is not { } coord) return null;
        if (map.GetPoint(coord.col, coord.row) is not { } current) return null;

        return MapTravelRule.TravelableFrom(session.RunState, map, current)
            .Where(point => !current.Children.Contains(point))
            .Where(point => point.PointType is MapPointType.Monster or MapPointType.RestSite
                or MapPointType.Shop or MapPointType.Treasure)
            .OrderBy(point => Cost(point.PointType))
            .ThenBy(point => point.coord.col)
            .FirstOrDefault();
    }

    // ── The route ───────────────────────────────────────────────────────────

    /// <summary>
    /// The whole route through the act, chosen before a step is taken.
    ///
    /// Planned rather than walked greedily, because greedy does not work: taking the
    /// most interesting node in front of you leads into rows whose only continuation
    /// is an elite fought on a quarter of the run's health, and no local rule sees that
    /// coming. The map is a layered graph, so the cheapest route that visits every
    /// required room type is a small search rather than an estimate.
    ///
    /// The cost is a fixed weighting over node types, not an opinion about the act:
    /// fights cost, an elite costs more, a rest site pays for itself, and everything
    /// else is free. Ties break towards the leftmost node, so the route is a function
    /// of the map and nothing else.
    /// </summary>
    private static IReadOnlyList<MapPoint> PlanRoute(GameSession session)
    {
        var map = session.RunState.Map
            ?? throw new EngineException("The current act has no generated map.");
        var coord = session.RunState.CurrentMapCoord
            ?? throw new EngineException("The run has no current map node.");
        var start = map.GetPoint(coord.col, coord.row)
            ?? throw new EngineException($"The current map node {coord} does not exist in this act.");

        var memo = new Dictionary<(MapPoint Node, int Covered), RoutePlan?>();
        var required = _requiredTypes.Length == 0
            ? "nothing in particular"
            : string.Join(RouteIsOrdered ? " and then " : ", ", _requiredTypes.Select(type => type.ToString().ToLowerInvariant()));
        return (BestRoute(start, 0, memo)
            ?? throw new EngineException(
                $"No route through this act {(RouteIsOrdered ? "passes" : "reaches the boss while visiting")} " +
                $"{required} without passing through a question mark. A seed is chosen because one does; either " +
                $"the map generation changed or {NotRouted} nodes block it on this seed."))
            .Path;
    }

    /// <summary>The cheapest route on from this node, or null when none from here
    /// reaches the boss having covered every required type.</summary>
    private static RoutePlan? BestRoute(
        MapPoint node, int covered, Dictionary<(MapPoint, int), RoutePlan?> memo)
    {
        if (memo.TryGetValue((node, covered), out var known)) return known;

        // Entered before the children are searched, so a cycle - which a well-formed
        // act map does not have and a malformed one would - resolves to "no route from
        // here" rather than to a stack overflow.
        memo[(node, covered)] = null;

        RoutePlan? best = null;
        if (node.PointType == MapPointType.Boss)
        {
            best = (covered & RequiredCoverage) == RequiredCoverage ? new RoutePlan(0, []) : null;
        }
        else if (RouteIsOrdered && (covered & RequiredCoverage) == RequiredCoverage)
        {
            best = new RoutePlan(0, []);
        }
        else
        {
            foreach (var child in node.Children
                         .Where(child => child.PointType != MapPointType.Unassigned)
                         .Where(child => child.PointType != NotRouted)
                         .OrderBy(child => child.coord.col))
            {
                var onward = BestRoute(child, covered | Coverage(child.PointType, covered), memo);
                if (onward is null) continue;

                var cost = Cost(child.PointType) + onward.Cost;
                if (best is not null && cost >= best.Cost) continue;
                best = new RoutePlan(cost, [child, .. onward.Path]);
            }
        }

        memo[(node, covered)] = best;
        return best;
    }

    /// <summary>The coverage bit passing a node of this type earns, given what the
    /// route has covered so far: on an ordered route a type counts only once every
    /// type listed before it has been passed, so a fight before the chest is not the
    /// fight the walk is after.</summary>
    private static int Coverage(MapPointType type, int covered)
    {
        var index = Array.IndexOf(_requiredTypes, type);
        if (index < 0) return 0;
        var earlier = (1 << index) - 1;
        return RouteIsOrdered && (covered & earlier) != earlier ? 0 : 1 << index;
    }

    /// <summary>
    /// What passing through a node costs the route.
    ///
    /// A weighting, deliberately crude: a fight costs health, an elite costs a lot more
    /// of it, and a rest site gives some back. It exists to keep the route survivable,
    /// not to model the act.
    /// </summary>
    private static int Cost(MapPointType type) => type switch
    {
        MapPointType.Monster => 3,
        MapPointType.Elite => 12,
        MapPointType.RestSite => -6,
        _ => 0,
    };

    private sealed record RoutePlan(int Cost, IReadOnlyList<MapPoint> Path);

    /// <summary>Whether the run has lost more than half of what it can take.</summary>
    private static bool Hurt(GameSession session)
    {
        var player = session.RunState.Players[0];
        return player.Creature.CurrentHp * 2 < player.Creature.MaxHp;
    }

    // ── The rooms ───────────────────────────────────────────────────────────

    /// <summary>
    /// Makes the decisions the room the run has just entered asks for.
    ///
    /// Every branch is one of the engine's own room types, and every one of them leaves
    /// the room holding no undecided offer - which is what lets the next map move
    /// happen at all, because the driver refuses to walk away from one.
    /// </summary>
    private static void HandleRoom(
        RunDriver driver, GameSession session, List<ActionRecord> actions, List<Checkpoint> checkpoints,
        MapPointType entered)
    {
        switch (session.RunState.CurrentRoom?.RoomType)
        {
            case RoomType.Monster or RoomType.Elite or RoomType.Boss:
                FightAndTakeTheLoot(driver, session, actions, checkpoints, entered);
                break;

            case RoomType.RestSite:
                Rest(driver, session, actions);
                break;

            case RoomType.Treasure:
                OpenTheChest(driver, session, actions);
                break;

            case RoomType.Shop:
                MeetTheAskIf(session, _policy.ShopWhileHoldingIt);
                BuyEverythingAffordable(driver, session, actions, checkpoints);
                break;

            default:
                throw new EngineException(
                    "The act journey entered a " +
                    $"{session.RunState.CurrentRoom?.RoomType.ToString() ?? "missing"} room from a " +
                    $"{entered} node, which it has no decisions for.");
        }
    }

    private static void FightAndTakeTheLoot(
        RunDriver driver, GameSession session, List<ActionRecord> actions, List<Checkpoint> checkpoints,
        MapPointType entered)
    {
        var name = entered.ToString().ToLowerInvariant();
        checkpoints.Add(Capture(
            $"{name}-{actions[^1].Seq.ToString(CultureInfo.InvariantCulture)}-combat-start",
            actions[^1].Seq, session,
            "combat.turn", "combat.energy", "combat.player_hp", "combat.hand", "combat.encounter",
            "combat.enemy_count"));

        // Read at the fight's start, because a relic the loot deals is held by the
        // next fight and not this one
        var heldOnEntry = _policy.FightWhileHoldingIt && HoldsTheRelic(session);
        DrinkPotions(driver, session, actions, entered);
        PlayToTheEndOfTheFight(driver, session, actions, SurvivingIndex);

        checkpoints.Add(Capture(
            $"{name}-{actions[^1].Seq.ToString(CultureInfo.InvariantCulture)}-combat-end",
            actions[^1].Seq, session,
            "combat.outcome", "combat.in_progress", "player.hp", "run.act_floor"));

        TakeTheLoot(driver, session, actions);
        if (heldOnEntry) _askMet = true;
    }

    /// <summary>
    /// Drinks what is on the belt when the fight opening is one this journey does not
    /// expect to survive otherwise: an elite, the act's boss, or any fight the run
    /// arrives at already hurt.
    ///
    /// The only place a generated history uses a potion at all, which matters: a verb
    /// nothing exercises is a verb whose refusals are the only thing anybody has seen
    /// work. It is also what gets the run through its act's boss.
    /// </summary>
    private static void DrinkPotions(
        RunDriver driver, GameSession session, List<ActionRecord> actions, MapPointType entered)
    {
        var everything = entered is MapPointType.Elite or MapPointType.Boss;
        if (!everything && !Hurt(session)) return;

        while (true)
        {
            var belt = session.RunState.Players[0].PotionSlots;
            var slot = Enumerable.Range(0, belt.Count).FirstOrDefault(index => belt[index] is not null, -1);
            if (slot < 0) return;

            var potion = belt[slot]!;
            var alive = CombatManager.Instance.DebugOnlyGetState()?.Enemies
                .Count(enemy => enemy is { IsAlive: true }) ?? 0;

            Apply(driver, actions, ActionVerb.UsePotion,
            [
                ("potion_id", potion.Id.ToString()),
                ("slot_index", slot.ToString(CultureInfo.InvariantCulture)),
                .. potion.TargetType == TargetType.AnyEnemy && alive > 1
                    ? new[] { ("target_index", "0") }
                    : [],
            ]);

            if (!everything) return;
        }
    }

    /// <summary>
    /// Takes what a won fight put on offer, and declines the rest explicitly.
    ///
    /// The gold, the first card, and a potion when the belt has room. First rather than
    /// best: which card is offered is the run's own business and taking it by position
    /// is a rule rather than an opinion. The deck does have to grow - measured, a
    /// journey that declined every card reward ran out of health on the fifth floor,
    /// because eleven starter cards do not finish an act.
    ///
    /// Whatever is left is declined with a verb rather than walked away from, which is
    /// the rule the driver enforces on the way out of the room.
    /// </summary>
    private static void TakeTheLoot(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        // Every gold reward, by position where the screen offers more than one - a
        // relic's second beside the fight's own - and by kind alone otherwise, which is
        // the one form a recording written before the position can hold
        var gold = driver.UnclaimedRewardPositions(RewardKinds.Gold);
        MeetTheAskIf(session, gold.Count > 1 && _policy.ClaimTwoOfAKind);
        foreach (var position in gold)
        {
            Apply(driver, actions, ActionVerb.ClaimReward,
            [
                ("reward_type", RewardKinds.Gold),
                .. gold.Count > 1
                    ? new[] { (RewardKinds.IndexArgument, position.ToString(CultureInfo.InvariantCulture)) }
                    : [],
            ]);
        }

        // The relic the policy is after is claimed wherever a loot screen offers it;
        // any relic is, where the policy asks for the claim itself - the first of
        // them, by position where a set offers two, as Neow's Bones does
        var relics = driver.UnclaimedRewardPositions(RewardKinds.Relic);
        if (driver.OfferedRelicId is { } relicId && (_policy.ClaimTheRelicReward || relicId == _policy.BagRelic))
        {
            Apply(driver, actions, ActionVerb.ClaimReward,
            [
                ("reward_type", RewardKinds.Relic),
                ("relic_id", relicId),
                .. relics.Count > 1
                    ? new[] { (RewardKinds.IndexArgument, relics[0].ToString(CultureInfo.InvariantCulture)) }
                    : [],
            ]);
            MeetTheAskIf(session, _policy.ClaimTheRelicReward || _policy.ObtainingIsTheAsk);
        }

        // The alternative the policy is after, past the reward's cards, on the first
        // card reward that offers it: the loot screen's Skip is the first alternative
        // of every card reward that can be skipped, and the reward stays on the screen
        // for the TakeCard that follows, which is what a player who changed their mind
        // does; a relic's own alternative - Pael's Wing's sacrifice - ends the reward
        // and the screen offers no card after it
        if (_policy.CardRewardAlternative is { } wanted && !_declinedACardReward && driver.OpenCardReward is { } reward)
        {
            var alternatives = CardRewardAlternative.Generate(reward);
            var offered = alternatives.ToList().FindIndex(alternative => alternative.OptionId == wanted);
            if (offered >= 0)
            {
                _declinedACardReward = true;
                MeetTheAskIf(session, true);
                Apply(driver, actions, ActionVerb.TakeCardRewardAlternative,
                    ("option_id", wanted),
                    ("option_index", (reward.Cards.Count() + offered).ToString(CultureInfo.InvariantCulture)));
            }
        }

        // Every card reward the screen offers, first card of each, by position where
        // the screen offers more than one - Kaleidoscope's, or a relic's second beside
        // the fight's own - and by kind alone otherwise
        while (driver.OfferedCardIds is [var firstCard, ..])
        {
            var cards = driver.UnclaimedRewardPositions(DecisionFacts.CardRewardKind);
            Apply(driver, actions, ActionVerb.TakeCard,
            [
                ("card_id", firstCard),
                ("option_index", "0"),
                .. cards.Count > 1
                    ? new[] { (RewardKinds.IndexArgument, cards[0].ToString(CultureInfo.InvariantCulture)) }
                    : [],
            ]);
        }

        // Every potion the belt has room for, by position where the screen offers
        // more than one - Cauldron's five - and by kind alone otherwise
        while (driver.UnclaimedRewardPositions(RewardKinds.Potion) is { Count: > 0 } potions &&
               session.RunState.Players[0].HasOpenPotionSlots)
        {
            Apply(driver, actions, ActionVerb.ClaimReward,
            [
                ("reward_type", RewardKinds.Potion),
                .. potions.Count > 1
                    ? new[] { (RewardKinds.IndexArgument, potions[0].ToString(CultureInfo.InvariantCulture)) }
                    : [],
            ]);
        }

        if (driver.UnclaimedRewardKinds.Count > 0)
        {
            Apply(driver, actions, ActionVerb.SkipRewards);
        }
    }

    /// <summary>
    /// Takes a rest site's healing when the run is hurt and its forge when it is not.
    ///
    /// Two options rather than one because a journey that only ever rests arrives at
    /// the act's boss with unupgraded starter cards and loses. Which card the forge
    /// offers is answered from the front of the screen it opens and written down as the
    /// selection it is, so that choice ends up in the history rather than in this code.
    /// </summary>
    private static void Rest(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().ToList();
        var wanted = Hurt(session) ? RestSiteHeal : RestSiteSmith;
        var index = _policy.RestOption is { } asked ? options.FindIndex(option => option.OptionId == asked) : -1;
        MeetTheAskIf(session, index >= 0);
        if (index < 0) index = options.FindIndex(option => option.OptionId == wanted);
        if (index < 0) index = options.FindIndex(option => option.OptionId == RestSiteHeal);

        if (index < 0)
        {
            throw new EngineException(
                $"This rest site offers neither {RestSiteHeal} nor {RestSiteSmith} " +
                $"({string.Join(", ", options.Select(option => option.OptionId))}), and this journey has no " +
                "rule for the rest.");
        }

        Apply(driver, actions, ActionVerb.ChooseRestSiteOption,
            ("option_id", options[index].OptionId),
            ("option_index", index.ToString(CultureInfo.InvariantCulture)));

        // A heal under Tiny Mailbox offers potions from inside the option's own work
        TakeWhatWasOffered(driver, session, actions);
    }

    private static void OpenTheChest(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        if (relics is not { Count: > 0 })
        {
            throw new EngineException(
                "The act journey entered a treasure room whose chest offers no relic, so there is no " +
                "decision to record there.");
        }

        if (_policy.SkipTheChest)
        {
            MeetTheAskIf(session, true);
            Apply(driver, actions, ActionVerb.SkipChestRelic);
        }
        else
        {
            Apply(driver, actions, ActionVerb.TakeChestRelic,
                ("relic_id", relics[0].Id.ToString()),
                ("option_index", "0"));
            MeetTheAskIf(session, _policy.TakeTheChest || _policy.ObtainingIsTheAsk);
        }

        if (driver.UnclaimedRewardKinds.Count > 0)
        {
            Apply(driver, actions, ActionVerb.SkipRewards);
        }
    }

    /// <summary>
    /// Buys everything the run can afford, cheapest first.
    ///
    /// Cheapest first rather than best, for the same reason the fight plays attacks
    /// first: it is a rule over what the shop stocked rather than an opinion about it.
    /// Spending the purse rather than saving it, because the act has to be survivable
    /// and a fixture that walks past a merchant exercises its verb once.
    /// </summary>
    private static void BuyEverythingAffordable(
        RunDriver driver, GameSession session, List<ActionRecord> actions, List<Checkpoint> checkpoints)
    {
        var room = (MerchantRoom)session.RunState.CurrentRoom!;

        while (BuyOneThing(driver, session, room, actions))
        {
            checkpoints.Add(Capture(
                $"shop-purchase-{actions[^1].Seq.ToString(CultureInfo.InvariantCulture)}", actions[^1].Seq,
                session, "player.gold", "player.deck_count", "player.potions", "player.relics"));
        }
    }

    /// <summary>Buys the cheapest affordable thing on any shelf, or nothing.</summary>
    private static bool BuyOneThing(
        RunDriver driver, GameSession session, MerchantRoom room, List<ActionRecord> actions)
    {
        var inventory = room.GetLocalInventory();
        var player = session.RunState.Players[0];
        var gold = player.Gold;

        var shelves = new (string Kind, IReadOnlyList<MerchantEntry> Entries)[]
        {
            (ShopPurchaseKinds.CharacterCard, [.. inventory.CharacterCardEntries]),
            (ShopPurchaseKinds.ColorlessCard, [.. inventory.ColorlessCardEntries]),
            (ShopPurchaseKinds.Relic, [.. inventory.RelicEntries]),
            (ShopPurchaseKinds.Potion, [.. inventory.PotionEntries]),
        };

        var affordable = shelves
            .SelectMany(shelf => shelf.Entries.Select((entry, index) => (shelf.Kind, entry, index)))
            .Where(candidate => candidate.entry.IsStocked && candidate.entry.Cost <= gold)
            // A potion nobody can carry is bought and immediately lost, which would be
            // a purchase this history could not explain.
            .Where(candidate => candidate.Kind != ShopPurchaseKinds.Potion || player.HasOpenPotionSlots)
            // The relic the policy is after before anything, then everything
            // affordable on the shelf the policy asks for before any other shelf,
            // since the sort is redone per purchase; then the purse's own order
            .OrderBy(candidate => candidate.entry is MerchantRelicEntry { Model: { } model } && model.Id.ToString() == _policy.BagRelic ? 0 : 1)
            .ThenBy(candidate => candidate.Kind == _policy.ShopKind ? 0 : 1)
            .ThenBy(candidate => candidate.entry.Cost)
            .ThenBy(candidate => candidate.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.index)
            .ToList();

        if (affordable.Count == 0) return false;

        var (kind, chosen, position) = affordable[0];
        var purchasedId = PurchasedId(chosen);
        Apply(driver, actions, ActionVerb.ShopPurchase,
            ("kind", kind),
            (ShopPurchaseKinds.IdArgument(kind)!, purchasedId),
            ("option_index", position.ToString(CultureInfo.InvariantCulture)));
        MeetTheAskIf(session, kind == _policy.ShopKind || _policy.ObtainingIsTheAsk);

        // A relic bought can put a rewards set on offer from its own work - Orrery's
        // cards, Cauldron's potions - answered before the next purchase
        TakeWhatWasOffered(driver, session, actions);
        return true;
    }

    /// <summary>
    /// Drinks or discards a potion on the map when the policy asks for one and the
    /// belt has it, once each. A discard is a decision no fight carries; a drink on
    /// the map is the same verb a fight's <see cref="DrinkPotions"/> issues, recorded
    /// by the run recorder rather than the fight observer, and only where the game
    /// lets the potion be drunk outside a fight, which is the potion's own say.
    /// </summary>
    private static void UseTheBeltOnTheMap(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        if (session.RunState.CurrentRoom is { RoomType: RoomType.Monster or RoomType.Elite or RoomType.Boss }) return;
        var belt = session.RunState.Players[0].PotionSlots;

        if (_policy.DrinkAPotionOnTheMap && !_drankOnTheMap)
        {
            var slot = Enumerable.Range(0, belt.Count).FirstOrDefault(
                index => belt[index] is { Usage: PotionUsage.AnyTime }, -1);
            if (slot >= 0)
            {
                _drankOnTheMap = true;
                MeetTheAskIf(session, true);
                Apply(driver, actions, ActionVerb.UsePotion,
                    ("potion_id", belt[slot]!.Id.ToString()),
                    ("slot_index", slot.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (_policy.DiscardAPotionOnTheMap && !_discardedOnTheMap)
        {
            var slot = Enumerable.Range(0, belt.Count).FirstOrDefault(index => belt[index] is not null, -1);
            if (slot >= 0)
            {
                _discardedOnTheMap = true;
                MeetTheAskIf(session, true);
                Apply(driver, actions, ActionVerb.DiscardPotion,
                    ("potion_id", belt[slot]!.Id.ToString()),
                    ("slot_index", slot.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private static string PurchasedId(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry card => card.CreationResult!.Card.Id.ToString(),
        MerchantRelicEntry relic => relic.Model!.Id.ToString(),
        MerchantPotionEntry potion => potion.Model!.Id.ToString(),
        _ => throw new EngineException($"A {entry.GetType().Name} has no id this journey can record."),
    };

    // ── The fight ───────────────────────────────────────────────────────────

    /// <summary>
    /// The hand position an act journey plays next: the first playable attack, and
    /// otherwise the first playable card at all.
    ///
    /// A different mechanical rule from the first-fight journey's, and it is here for
    /// one reason: hand order alone loses the act. Measured on this seed, playing the
    /// first playable card every turn takes the run to nothing before the eighth floor,
    /// and a fixture that dies half way through an act produces none of the boundaries
    /// this journey exists to produce.
    ///
    /// It is still a rule over the hand the engine dealt rather than a judgement, and
    /// it must not be read as one: it is not how to play, it is the cheapest rule that
    /// finishes an act.
    /// </summary>
    private static int SurvivingIndex(GameSession session)
    {
        var hand = session.RunState.Players[0].PlayerCombatState?.Hand.Cards;
        if (hand is null) return -1;

        var playable = Enumerable.Range(0, hand.Count)
            .Where(index => hand[index].CanPlay(out _, out _))
            .ToList();

        var attack = playable.FirstOrDefault(index => hand[index].Type == CardType.Attack, -1);
        return attack >= 0 ? attack : playable.Count > 0 ? playable[0] : -1;
    }

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];
}
