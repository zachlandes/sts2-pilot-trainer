using System.Globalization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
/// planned by cost before a step is taken, the fight blocks while the enemies'
/// displayed damage exceeds the block held and otherwise attacks the enemy with the
/// least health, the loot is taken - the card by its own type flags and cost - and
/// whatever is left is declined, a rest site forges when the run is healthy and heals
/// when it is not, and the shop is emptied cheapest first. Each of those rules is
/// here because without it the journey does not finish an act, and not one of them
/// is a claim about how to play. The fixture must not be read as one.
/// </summary>
public static partial class SyntheticFixtureGenerator
{
    /// <summary>
    /// The one node type this route will not enter unless the policy asks for it.
    ///
    /// A question mark resolves to whatever the run's own stream says when it is
    /// entered, and a fixture that walked into one and met something the seed was not
    /// chosen for would fail for a reason nobody is testing. A walk after an event
    /// (<see cref="WalkPolicy.EventId"/>) is the one that asks: its route ends at the
    /// first question mark it reaches, on a seed hunted so that one opens the event,
    /// and whatever the mark opens instead - a fight, a merchant, a chest - is a room
    /// the journey has rules for and the walk finishes without meeting its ask.
    /// </summary>
    private const MapPointType NotRouted = MapPointType.Unknown;

    /// <summary>Whether the route under way is allowed through a question mark: the
    /// policy names an event and the route is after the first mark, or the ask is in
    /// the act after this one and the route has to reach the boss whatever the map
    /// puts in the way, which on many seeds is a question mark on every path.</summary>
    private static bool RouteMayPassAQuestionMark => _requiredTypes.Contains(NotRouted) || _beforeTheAskedAct;

    /// <summary>Whether the walk under way is still in the act before the one its ask
    /// is in: the first act of a walk whose ask is in the next, walked through to
    /// its boss by the fixture's own rules.</summary>
    private static bool _beforeTheAskedAct;

    /// <summary>How many pages this journey answers in one event before it decides the
    /// event is not finishing: the longest event on this build is Slippery Bridge's
    /// eight holds; anything past this is an event that loops.</summary>
    private const int EventPageLimit = 24;

    /// <summary>The room types the journey has to visit for the verbs that only exist
    /// there to be exercised at all.</summary>
    private static readonly MapPointType[] RequiredTypes =
        [MapPointType.Shop, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Elite];

    /// <summary>The node types the route under way has to pass through, one coverage
    /// bit each: the fixture's four, the policy's own list, or none for a walk that
    /// only has to reach the boss.</summary>
    private static MapPointType[] _requiredTypes = RequiredTypes;

    /// <summary>The coverage the walk under way demands of its route: on an ordered
    /// route the whole list passed in order, counted as the prefix fulfilled, so a
    /// type listed twice is passed twice - two fights and then a question mark; on an
    /// unordered one every type's bit.</summary>
    private static int RequiredCoverage => RouteIsOrdered ? _requiredTypes.Length : (1 << _requiredTypes.Length) - 1;

    private static bool Complete(int covered) => RouteIsOrdered ? covered >= RequiredCoverage : (covered & RequiredCoverage) == RequiredCoverage;

    /// <summary>The choices the walk under way consults; the fixture's are the defaults.</summary>
    private static WalkPolicy _policy = WalkPolicy.Default;

    /// <summary>The rules the two committed act fixtures were produced under and are
    /// regenerated under: the journey's earlier fight rule, because each fixture's
    /// digests are committed and a regeneration has to reproduce them.</summary>
    private static readonly WalkPolicy CommittedFixtureRules = new() { Rule = SurvivalRule.AttackFirst };

    /// <summary>
    /// Whether the route under way passes its required types in the order the policy
    /// lists them and may end at the node that completes them, rather than passing
    /// them in any order on the way to the boss: a walk after a relic the bag deals is
    /// after the room that deals it and then the room its ask is met in - the chest
    /// and then a fight or a rest site, the shop and then a fight - and a map whose
    /// only way from there to the boss passes a question mark still has both rooms.
    /// A walk after a relic an ancient deals holds it from the first room and is
    /// after the one room its ask is met in, for the same reason; a walk after an
    /// event is after the first question mark and nothing past it.
    /// </summary>
    private static bool RouteIsOrdered =>
        !_beforeTheAskedAct && (_policy.Relic is not null || _policy.EventId is not null) && _policy.StopOnceMet &&
        _requiredTypes.Length > 0;

    /// <summary>Which of the policy's one-time asks the walk under way has met, and
    /// whether any ask has been, for a walk that stops there.</summary>
    private static bool _declinedACardReward;
    private static bool _drankOnTheMap;
    private static bool _discardedOnTheMap;
    private static bool _travelledFreely;
    private static bool _askMet;

    /// <summary>Whether the walk under way has chosen the event option its policy
    /// names, so a page that offers it again is answered by today's rule, unless
    /// the policy is after the run's end on it.</summary>
    private static bool _choseTheAskedOption;

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
        // The committed fixture was produced under the journey's earlier rule and its
        // digests are committed, so the generator keeps that rule for it; the measured
        // rule is the walks' and the rows'
        var actions = WalkTheAct(session, driver, checkpoints, policy: CommittedFixtureRules).Actions;

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
        // A walk into the next act has to have one: on a run of one act the transition
        // opens the victory room, and a Darv row satisfied there would be a row about
        // a room Darv is never rolled for
        if (_policy.AskInTheNextAct && session.RunState.Acts.Count < 2)
        {
            throw new EngineException(
                $"The walk's ask is in the act after the first, and the run's acts list is {session.RunState.Acts[0].Id} " +
                "alone: a run of one act opens the victory room past its boss, not a second act.");
        }

        // The first act of a walk whose ask is in the next is the fixture's own route,
        // every room type on the way to the boss: a line that reaches a second act
        // needs the chest's relic and the merchant's cards the cheapest route has
        // none of, and the every-room route is the one the journey's rules are
        // known to survive an act on
        (_afterEachDecision, _requiredTypes) =
            (afterEachDecision, _policy.AskInTheNextAct ? RequiredTypes
                : _policy.RouteThrough is { } through ? [.. through]
                : visitEveryRoomType ? RequiredTypes : []);
        (_declinedACardReward, _drankOnTheMap, _discardedOnTheMap, _travelledFreely, _askMet, _choseTheAskedOption) =
            (false, false, false, false, false, false);
        _beforeTheAskedAct = _policy.AskInTheNextAct;
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
        if (OpenTheActAndMeetTheAsk(driver, session, actions, theAskIsHere: !_policy.AskInTheNextAct)) return;
        if (!WalkTheRoute(session, driver, actions, checkpoints)) return;

        Apply(driver, actions, ActionVerb.ProceedToNextAct);
        checkpoints.Add(Capture("act-two-entry", actions[^1].Seq, session,
            "run.act_index", "run.total_floor", "player.hp", "player.deck_count", "player.relics"));

        // A walk whose ask is in the next act enters that act's starting point, the
        // one node the map offers a run that has visited none of the act's, and
        // opens the act the way it opened the run - on the ancient the act rolls,
        // Darv among them - then walks the act's own route to the ask, which is the
        // one way to what a run reaches only past its first act
        if (!_policy.AskInTheNextAct) return;
        _beforeTheAskedAct = false;
        var start = session.RunState.Map?.StartingMapPoint
            ?? throw new EngineException("The act the run moved on to has no generated map.");
        Apply(driver, actions, ActionVerb.MapMove,
            ("act", session.RunState.CurrentActIndex.ToString(CultureInfo.InvariantCulture)),
            ("row", start.coord.row.ToString(CultureInfo.InvariantCulture)),
            ("column", start.coord.col.ToString(CultureInfo.InvariantCulture)));
        checkpoints.Add(Capture(
            $"floor-{Field(session, "run.total_floor")}-entry", actions[^1].Seq, session,
            "run.total_floor", "run.map_coord", "player.hp", "player.gold"));

        _requiredTypes = _policy.RouteThrough is { } through ? [.. through] : [];
        if (OpenTheActAndMeetTheAsk(driver, session, actions, theAskIsHere: true)) return;
        WalkTheRoute(session, driver, actions, checkpoints);
    }

    /// <summary>
    /// Answers the room an act opens on and what the answer offered, and says whether
    /// the walk is over there: the ask met and the walk one that stops on it.
    ///
    /// A blessing or an ancient's offer that grants a relic can put a rewards set on
    /// offer from the relic's own work - Kaleidoscope's cards, Small Capsule's
    /// relic, Toy Box's - which is answered before the run moves, the way a player
    /// answers it before the map opens.
    /// </summary>
    /// <param name="theAskIsHere">Whether this is the act the policy's ask is in:
    /// on the first act of a walk whose ask is in the next, the ancient's first
    /// option is taken and nothing is the ask yet.</param>
    private static bool OpenTheActAndMeetTheAsk(RunDriver driver, GameSession session, List<ActionRecord> actions, bool theAskIsHere)
    {
        // The act a walk was carried into opens on whichever ancient the run rolled
        // for it, which no reading of the seed's first room shows and the walk is the
        // one thing that reaches: an ancient that does not offer the relic the walk
        // is after is refused by name here, rather than answered by today's rule and
        // reported as an ask met nowhere
        if (theAskIsHere && _policy.AskInTheNextAct && _policy.AncientRelic is { } wanted)
        {
            var opening = RunManager.Instance.EventSynchronizer?.GetLocalEvent()
                ?? throw new EngineException("The act journey is not standing in the act's opening event.");
            var offered = opening.CurrentOptions.Select(option => option.Relic?.Id.ToString()).OfType<string>().ToList();
            if (!offered.Contains(wanted, StringComparer.Ordinal))
            {
                throw new EngineException(
                    $"The second act opens on {opening.Id} offering {string.Join(", ", offered)}, not on an ancient " +
                    $"offering {wanted}: the seed was hunted for a second act that deals it, and this one does not.");
            }
        }

        OpenTheRun(driver, session, actions, theAskIsHere ? _policy.AncientRelic : null);
        MeetTheAskIf(session, theAskIsHere && (_policy.ObtainingIsTheAsk || _policy.OpeningTheNextActIsTheAsk));
        TakeWhatWasOffered(driver, session, actions);
        return _policy.StopOnceMet && _askMet;
    }

    /// <summary>Walks the act's planned route room by room. False where the walk is
    /// over before the boss: its ask met on a walk that stops there, or an ordered
    /// route that ended at the room completing it.</summary>
    private static bool WalkTheRoute(
        GameSession session, RunDriver driver, List<ActionRecord> actions, List<Checkpoint> checkpoints)
    {
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
            if (_policy.StopOnceMet && _askMet) return false;
            if (flown is not null) route = PlannedRoute(session);
        }

        // A route that ended at the room that completed its coverage stands nowhere
        // an act can be left from; the walk is over, its ask met or not
        return !RouteIsOrdered || session.RunState.CurrentRoom is { RoomType: RoomType.Boss };
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

    /// <summary>Counts the ask met where the condition holds, the policy's relic, if
    /// it names one, is held, and the walk is in the act the ask is in: on the first
    /// act of a walk whose ask is in the next, every decision is the fixture's own
    /// and none of them is what the walk is for.</summary>
    private static void MeetTheAskIf(GameSession session, bool condition)
    {
        if (condition && !_beforeTheAskedAct && HoldsTheRelic(session)) _askMet = true;
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
    /// <param name="ancientRelic">The relic to take where the room is an ancient's,
    /// or null for the first option.</param>
    private static void OpenTheRun(RunDriver driver, GameSession session, List<ActionRecord> actions, string? ancientRelic)
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

        var index = OpeningOption(options, ancientRelic);
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
            best = Complete(covered) ? new RoutePlan(0, []) : null;
        }
        else if (RouteIsOrdered && Complete(covered))
        {
            best = new RoutePlan(0, []);
        }
        else
        {
            foreach (var child in node.Children
                         .Where(child => child.PointType != MapPointType.Unassigned)
                         .Where(child => child.PointType != NotRouted || RouteMayPassAQuestionMark)
                         .OrderBy(child => child.coord.col))
            {
                var onward = BestRoute(child, Coverage(child.PointType, covered), memo);
                if (onward is null) continue;

                var cost = Cost(child.PointType) + onward.Cost;
                if (best is not null && cost >= best.Cost) continue;
                best = new RoutePlan(cost, [child, .. onward.Path]);
            }
        }

        memo[(node, covered)] = best;
        return best;
    }

    /// <summary>What the route has covered once it passes a node of this type, given
    /// what it had covered: on an ordered route the next entry of the list is
    /// fulfilled where this is its type and nothing otherwise, so a fight before the
    /// chest is not the fight the walk is after; on an unordered one the type's bit.</summary>
    private static int Coverage(MapPointType type, int covered)
    {
        if (RouteIsOrdered)
        {
            return covered < _requiredTypes.Length && _requiredTypes[covered] == type ? covered + 1 : covered;
        }

        var index = Array.IndexOf(_requiredTypes, type);
        return index < 0 ? covered : covered | (1 << index);
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
        // What a fight costs, on the routes that may pass one: one mark in seven
        // opens a fight, and an event can cost health too
        MapPointType.Unknown => 3,
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

            case RoomType.Event:
                AnswerTheEvent(driver, session, actions, checkpoints, entered);
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

        // Where the walk stood is read before the fight, because the projection
        // carries nothing of a fight once it is over: a walk that dies says the floor
        // and the encounter, which is what a seed is hunted on
        var floor = Field(session, "run.total_floor");
        var encounter = Field(session, "combat.encounter");
        try
        {
            PlayToTheEndOfTheFight(driver, session, actions, SurvivingIndex, TargetIndex);
        }
        catch (FightLostException lost)
        {
            throw new EngineException(
                $"The act journey died on floor {floor} in {encounter}, a {name} fight, as " +
                $"{session.RunState.Players[0].Character.Id}: {lost.Message}");
        }

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
    /// The gold, one card of each card reward, and a potion when the belt has room.
    /// Which card is <see cref="CardRewardIndex"/>'s rule over the offered cards' own
    /// type flags and cost, and not an opinion about them. The deck does have to grow -
    /// measured, a journey that declined every card reward ran out of health on the
    /// fifth floor, because eleven starter cards do not finish an act.
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

        // Every reward of the kind the policy is after, by position where the screen
        // offers more than one, which no card reward is: the special card a thief
        // died holding, the removal a power earned
        if (_policy.RewardKindToClaim is { } wantedKind && wantedKind != DecisionFacts.CardRewardKind)
        {
            var offered = driver.UnclaimedRewardPositions(wantedKind).Zip(driver.UnclaimedRewardIds(wantedKind)).ToList();
            foreach (var (position, id) in offered)
            {
                Apply(driver, actions, ActionVerb.ClaimReward,
                [
                    ("reward_type", wantedKind),
                    .. RewardKinds.IdArgument(wantedKind) is { } idArgument && id is not null
                        ? new[] { (idArgument, id) }
                        : [],
                    .. offered.Count > 1
                        ? new[] { (RewardKinds.IndexArgument, position.ToString(CultureInfo.InvariantCulture)) }
                        : [],
                ]);
                MeetTheAskIf(session, true);
            }
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

        // Every card reward the screen offers, one card of each by the reward rule,
        // by position where the screen offers more than one - Kaleidoscope's, or a
        // relic's second beside the fight's own - and by kind alone otherwise
        while (driver.OpenCardReward is { } cardReward && cardReward.Cards.Any())
        {
            var offered = cardReward.Cards.ToList();
            var pick = _policy.Rule == SurvivalRule.BlockWhenThreatened ? CardRewardIndex(offered) : 0;
            var cards = driver.UnclaimedRewardPositions(DecisionFacts.CardRewardKind);
            Apply(driver, actions, ActionVerb.TakeCard,
            [
                ("card_id", offered[pick].Id.ToString()),
                ("option_index", pick.ToString(CultureInfo.InvariantCulture)),
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
        // A rest asked for after an event counts only once the event's option was
        // chosen, because the site the route passes on the way there is not the one
        // the walk is after
        MeetTheAskIf(session, index >= 0 && (_policy.EventOptionKey is null || _choseTheAskedOption));
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

        while (BuyOneThing(driver, session, room.GetLocalInventory(), actions))
        {
            checkpoints.Add(Capture(
                $"shop-purchase-{actions[^1].Seq.ToString(CultureInfo.InvariantCulture)}", actions[^1].Seq,
                session, "player.gold", "player.deck_count", "player.potions", "player.relics"));
        }
    }

    /// <summary>Buys the cheapest affordable thing on any shelf of the inventory - the
    /// merchant room's, or the Fake Merchant's own - or nothing.</summary>
    private static bool BuyOneThing(
        RunDriver driver, GameSession session, MerchantInventory inventory, List<ActionRecord> actions)
    {
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

    // ── The event ───────────────────────────────────────────────────────────

    /// <summary>
    /// Answers the event a question mark opened, page by page, until it is finished.
    ///
    /// Each page is one decision, made by a fixed rule over the options the engine
    /// offers: the policy's option where the page offers it, which is the ask met;
    /// one the policy names on the way to it where the page offers that; and
    /// otherwise the last option that is not locked and does not kill the player, a
    /// proceed before any other - last rather than first because the option that
    /// leaves is written last more often than not, and this is a rule over the page
    /// rather than an opinion about the event. A locked option is never taken, because
    /// the game's own button refuses the press.
    ///
    /// What an option does is the engine's: one that opens a fight without leaving the
    /// event is played to its end and looted like any other and the event resumed the
    /// way the retail proceed resumes it (<see cref="RunDriver.ResumeTheEventTheFightWasFoughtIn"/>);
    /// one that opens the Crystal Sphere's own screen is played by revealing hidden
    /// cells until its divination is spent; one whose work offers rewards has them
    /// answered before the next page; one that opens a card screen is answered from
    /// the front by the driver like every other screen a generated history opens.
    /// </summary>
    private static void AnswerTheEvent(
        RunDriver driver, GameSession session, List<ActionRecord> actions, List<Checkpoint> checkpoints,
        MapPointType entered)
    {
        var pages = 0;
        while (true)
        {
            // A fight the last option opened, inside the event: played, looted, and
            // the event resumed where it resumes
            if (session.RunState.CurrentRoom is CombatRoom)
            {
                FightAndTakeTheLoot(driver, session, actions, checkpoints, entered);
                driver.ResumeTheEventTheFightWasFoughtIn();
                if (session.RunState.CurrentRoom is CombatRoom) return;
                TakeWhatWasOffered(driver, session, actions);
                continue;
            }

            if (ScreenStandIns.OpenMinigame is not null)
            {
                RevealTheCrystalSphere(driver, session, actions);
                continue;
            }

            var local = RunManager.Instance.EventSynchronizer?.GetLocalEvent();
            if (session.RunState.CurrentRoom is not EventRoom) return;
            // The Fake Merchant offers no option and stands finished from its first
            // page: it draws a shop of its own, and its decisions are the purchases
            // made from it, which the driver replays through the same member as a
            // merchant room's. The walk empties it the way it empties a shop, and a
            // walk after this event is after a purchase
            if (local is FakeMerchant fake)
            {
                var bought = false;
                while (BuyOneThing(driver, session, fake.Inventory, actions)) bought = true;
                MeetTheAskIf(session, bought && local.Id.ToString() == _policy.EventId && _policy.EventOptionKey is null);
                return;
            }

            if (local is null || local.IsFinished) return;
            // An event whose option ended the run may still have set its next page:
            // the game is over and nothing on it is a decision
            if (RunEnding.Reading is not null) return;

            if (++pages > EventPageLimit)
            {
                throw new EngineException(
                    $"The act journey has answered {EventPageLimit.ToString(CultureInfo.InvariantCulture)} pages of " +
                    $"{local.Id} and the event is not finished; this journey has no rule that ends it.");
            }

            var options = local.CurrentOptions;
            var index = EventOptionToTake(local, options, session);
            if (index < 0)
            {
                throw new EngineException(
                    $"{local.Id} offers no option this journey can take " +
                    $"({string.Join(", ", options.Select(RunDriver.OptionKey))}): every one is locked or kills the run.");
            }

            var key = RunDriver.OptionKey(options[index]);
            Apply(driver, actions, ActionVerb.ChooseEventOption,
                ("event_id", local.Id.ToString()),
                ("option_index", index.ToString(CultureInfo.InvariantCulture)),
                ("option_key", key));
            // The option is the ask, unless the policy is after something past it - a
            // reward the option's fight earns, claimed off that fight's loot screen, a
            // rest taken after the event - or after the run's end on it, which the
            // game announces from inside the option's own work
            var chosenTheAskedOption = IsTheAskedEvent(local) && key == _policy.EventOptionKey;
            if (chosenTheAskedOption) _choseTheAskedOption = true;
            MeetTheAskIf(session, chosenTheAskedOption && !_policy.AsksPastTheEvent && !_policy.ChooseItUntilTheRunEnds);
            if (chosenTheAskedOption && _policy.ChooseItUntilTheRunEnds && RunEnding.Reading is not null)
            {
                MeetTheAskIf(session, true);
                return;
            }

            // An option whose own work offers rewards - a courier's potions, a
            // trader's relic - is answered before the next page, the way a blessing's
            // offer is answered before the map; the next page is then the option's
            // work's to produce, and is waited for
            if (session.RunState.CurrentRoom is EventRoom)
            {
                TakeWhatWasOffered(driver, session, actions);
                driver.WaitForTheOptionsWork();
            }
        }
    }

    /// <summary>The option this journey takes on a page, by the rule above, or -1
    /// where the page offers none it can take.</summary>
    private static int EventOptionToTake(EventModel local, IReadOnlyList<EventOption> options, GameSession session)
    {
        var player = session.RunState.Players[0];
        bool Takeable(EventOption option) => !option.IsLocked && option.WillKillPlayer?.Invoke(player) != true;
        int IndexOfKey(string? wanted) =>
            wanted is null ? -1 : options.ToList().FindIndex(option => Takeable(option) && RunDriver.OptionKey(option) == wanted);

        if (IsTheAskedEvent(local))
        {
            // The option asked for is taken once, whatever it says it does to the
            // player - a row that asks for the Trial's double-down asks for the
            // abandon it opens - and never a second time where the page keeps
            // offering it, as that page does; a walk after the run's end takes it
            // every time the page offers it, until the game ends the run on it
            var takenAlready = _askMet || (_choseTheAskedOption && !_policy.ChooseItUntilTheRunEnds);
            var asked = takenAlready || _policy.EventOptionKey is null
                ? -1
                : options.ToList().FindIndex(option => !option.IsLocked && RunDriver.OptionKey(option) == _policy.EventOptionKey);
            if (asked >= 0) return asked;
            foreach (var onTheWay in _policy.EventOptionsOnTheWay ?? [])
            {
                var step = IndexOfKey(onTheWay);
                if (step >= 0) return step;
            }

            // The page the key is on is reached, more often than not, through the
            // option named for it - Punch Off's challenge opens the page its fight is
            // on, a dig opens the deeper page - so that option is the way where no
            // way is named
            if (PageOf(_policy.EventOptionKey) is { } page)
            {
                var towards = options.ToList().FindIndex(option =>
                    Takeable(option) && RunDriver.OptionKey(option).EndsWith($".options.{page}", StringComparison.Ordinal));
                if (towards >= 0) return towards;
            }
        }

        var proceed = options.ToList().FindIndex(option => Takeable(option) && option.IsProceed);
        if (proceed >= 0) return proceed;
        return options.ToList().FindLastIndex(Takeable);
    }

    /// <summary>Whether an event on the page under way is the one the policy names,
    /// in the act the policy's ask is in: a shared event the first act of a walk into
    /// the second rolls as well is answered there by today's rule, because the
    /// option taken there is the fixture's own and would otherwise stand as the ask
    /// taken before the act the row is about.</summary>
    private static bool IsTheAskedEvent(EventModel local) =>
        !_beforeTheAskedAct && local.Id.ToString() == _policy.EventId;

    /// <summary>The page an option key names, as an event's code writes one -
    /// <c>EVENT.pages.PAGE.options.OPTION</c> - or null for a key of another shape.</summary>
    private static string? PageOf(string? key)
    {
        if (key is null) return null;
        var pages = key.IndexOf(".pages.", StringComparison.Ordinal);
        var options = key.IndexOf(".options.", StringComparison.Ordinal);
        return pages >= 0 && options > pages ? key[(pages + ".pages.".Length)..options] : null;
    }

    /// <summary>
    /// Plays the Crystal Sphere's screen the way a player clicks it: the first hidden
    /// cell in reading order with the small tool, until the divination is spent and
    /// the minigame ends itself. Which cells hold what is the engine's roll; nothing
    /// here decides anything.
    /// </summary>
    private static void RevealTheCrystalSphere(RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        while (ScreenStandIns.OpenMinigame is { IsFinished: false } minigame)
        {
            var size = minigame.GridSize;
            var hidden = Enumerable.Range(0, size.Y)
                .SelectMany(y => Enumerable.Range(0, size.X).Select(x => (X: x, Y: y)))
                .FirstOrDefault(cell => minigame.cells[cell.X, cell.Y].IsHidden, (X: -1, Y: -1));
            if (hidden.X < 0)
            {
                throw new EngineException("The Crystal Sphere has divination left and no hidden cell to spend it on.");
            }

            Apply(driver, actions, ActionVerb.RevealCrystalSphereCell,
                ("x", hidden.X.ToString(CultureInfo.InvariantCulture)),
                ("y", hidden.Y.ToString(CultureInfo.InvariantCulture)),
                ("tool", CrystalSphereTools.Small));
        }

        // The minigame's end offers what was uncovered as a rewards set
        TakeWhatWasOffered(driver, session, actions);
    }

    // ── The fight ───────────────────────────────────────────────────────────

    /// <summary>
    /// The hand position an act journey plays next, by the policy's
    /// <see cref="SurvivalRule"/>, read off <see cref="SurvivalPlayRule"/> - the one
    /// owner of the rule, which the retail soak plays through the same call.
    ///
    /// A different mechanical rule from the first-fight journey's, and it is here for
    /// one reason: hand order alone loses the act. Measured on the fixture seed,
    /// playing the first playable card every turn takes the run to nothing before the
    /// eighth floor, and a fixture that dies half way through an act produces none of
    /// the boundaries this journey exists to produce. The block-first form replaced
    /// attack-first after it was measured across every character and both act-one
    /// routes (docs/release-bar.md): on 300 hunted seeds per configuration it beats
    /// the act's boss on roughly twice as many as attack-first did, and it is what
    /// lets a walk of any character reach a second act at all.
    ///
    /// It is still a rule rather than a judgement, and it must not be read as one: it
    /// is not how to play, it is the cheapest rule that finishes an act.
    /// </summary>
    private static int SurvivingIndex(GameSession session) =>
        SurvivalPlayRule.Next(_policy.Rule, session.RunState.Players[0], Enemies())?.HandIndex ?? -1;

    /// <summary>The living enemy an act journey aims a card at where more than one is
    /// alive, by position among the living, which is the position the driver resolves;
    /// the same rule's target as <see cref="SurvivingIndex"/> plays at.</summary>
    private static int TargetIndex(GameSession session) =>
        SurvivalPlayRule.TargetIndex(_policy.Rule, SurvivalPlayRule.Living(Enemies()));

    private static IReadOnlyList<Creature> Enemies() =>
        CombatManager.Instance?.DebugOnlyGetState()?.Enemies ?? [];

    /// <summary>The offered card an act journey takes under the measured rule: a card
    /// the game says gains block or an attack before anything else, the cheaper first
    /// by the card's canonical cost, and the offer's own order last. A rule over each
    /// card's type flags and cost, the same for every character, and not an opinion
    /// about the cards; measured, it is the single rule that moves survival most. The
    /// earlier rule takes the first card offered.</summary>
    private static int CardRewardIndex(IReadOnlyList<CardModel> offered) =>
        Enumerable.Range(0, offered.Count)
            .OrderBy(index => offered[index].GainsBlock || offered[index].Type == CardType.Attack ? 0 : 1)
            .ThenBy(index => offered[index].EnergyCost.Canonical)
            .ThenBy(index => index)
            .First();

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];
}
