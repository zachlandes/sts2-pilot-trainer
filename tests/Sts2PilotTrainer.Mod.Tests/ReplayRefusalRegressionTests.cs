using System.Globalization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Ordinary singleplayer paths a recording of which the driver refused on v0.111.0,
/// each played through the real recorder, replayed, and held to parity.
///
/// The first two were found by reading the game assembly for what a player can reach
/// rather than by a recording: the driver enforced a rule of its own where the game
/// has one, and the format could not say which of two rewards of one kind was taken.
/// The third was found by the Stage 3 ancient row for Lord's Parasol: the recorder
/// wrote the purchases the relic makes for itself as the merchant is entered as the
/// player's, before the move that opened the shop. The fourth was found by the event
/// rows' walk into a second act: every act after the first opens on the map with
/// nothing visited, and the driver refused the move to the act's starting point as
/// unreachable from a node the run was not standing on. The fifth was found in
/// review of those rows: the replay's before-reading of the first decision after a
/// fight fought inside an event was taken in the finished combat room, one resume
/// short of where the recorder reads it. Each row is a recording that the driver
/// before its change refused - as <c>Map node ... is not reachable</c>, as <c>2 of
/// them are on offer</c>, as <c>buys from a merchant, but this floor is a Monster
/// room</c>, as <c>The run has no current map node</c>, as <c>combat.outcome: none
/// -> victory</c> at parity - and that now replays
/// decision for decision through the same oracle <c>parity</c> uses, on a seed chosen
/// because its opening event, its relic bag or its survival puts the producer on the
/// walk's route. The rows live apart from <c>GeneratedCoverageTests</c> because they
/// exercise no excused decision point of their own: a map move is not a point, a gold
/// reward is one the corpus reaches, the shop entered under Lord's Parasol is the
/// ancient row's, and the second act's ancient is one of the ancient rows'.
/// </summary>
public sealed class ReplayRefusalRegressionTests
{
    /// <summary>Before any test method here is prepared, so the game assembly the
    /// harness names resolves; the harness's own teardown forgets it again.</summary>
    public ReplayRefusalRegressionTests() => EngineHost.Start();

    /// <summary>A seed whose opening event offers Winged Boots as its second option,
    /// so the walk has to pick the relic by name rather than take the first option.</summary>
    private const string WingedBootsSeed = "52UG7QD5X4";

    /// <summary>A seed whose shared relic bag has Amethyst Aubergine at the front of
    /// its commons and whose first chest rolls a common, so the chest hands it over,
    /// and whose route past the chest survives to a fight: every fight from then on
    /// offers the fight's own gold and the relic's beside it.</summary>
    private const string AubergineSeed = "KSP00HAL6M";

    /// <summary>A seed of Glory alone whose ancient is Vakuu offering Lord's Parasol,
    /// and whose route past the ancient survives to a merchant.</summary>
    private const string LordsParasolSeed = "5U7CT06HNS";

    /// <summary>
    /// Winged Boots lets the player walk to any node of the next row, and the game
    /// decides that through <c>MapTravel.GetTravelablePointsFrom</c>; the driver read
    /// the node's own children and refused the flight as unreachable.
    /// </summary>
    [GameFact]
    public void AFlightToANodeThePathDoesNotLeadToReplays()
    {
        using var harness = new RecordedActWalk();

        var recorded = harness.Walk(
            new WalkPolicy { NeowRelic = "RELIC.WINGED_BOOTS", TravelFreely = true },
            WingedBootsSeed, visitEveryRoomType: false);
        Assert.True(recorded.AskMet, "the walk finished without flying to a node the path did not lead to");
        RecordedActWalk.AssertWhole(recorded);

        var blessing = Assert.Single(recorded.Manifest.Actions, action => action.Verb == ActionVerb.ChooseNeowBlessing);
        Assert.Equal("RELIC.WINGED_BOOTS", blessing.Args["option_key"]);
        Assert.Equal("1", blessing.Args["option_index"]);
        Assert.True(recorded.Manifest.Actions.Count(action => action.Verb == ActionVerb.MapMove) >= 2,
            "the flight is the second move: every node of the first row is on the starting point's own paths");

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>
    /// Amethyst Aubergine adds a second gold reward to every fight's loot, and a claim
    /// named only its kind; the driver refused the set as offering two of a kind, and
    /// the recorder had no position to write. Both golds are now claimed by the
    /// position the game's own <c>RewardSelectedMessage</c> carries.
    /// </summary>
    [GameFact]
    public void TwoGoldRewardsOnOneLootScreenReplay()
    {
        using var harness = new RecordedActWalk();

        var recorded = harness.Walk(
            new WalkPolicy { ClaimTwoOfAKind = true, RouteThrough = [MapPointType.Treasure] },
            AubergineSeed, visitEveryRoomType: false);
        Assert.True(recorded.AskMet, "the walk finished without a loot screen offering two rewards of one kind");
        RecordedActWalk.AssertWhole(recorded);

        var chest = Assert.Single(recorded.Manifest.Actions, action => action.Verb == ActionVerb.TakeChestRelic);
        Assert.Equal("RELIC.AMETHYST_AUBERGINE", chest.Args["relic_id"]);

        // The last loot screen is the one with two: consecutive gold claims, each at
        // its own position in the set, both written by the recorder off the reward it
        // saw clicked
        var golds = recorded.Manifest.Actions
            .Where(action => action.Verb == ActionVerb.ClaimReward && action.Args["reward_type"] == RewardKinds.Gold)
            .TakeLast(2)
            .ToList();
        Assert.Equal(2, golds.Count);
        Assert.Equal(golds[0].Seq + 1, golds[1].Seq);
        Assert.NotEqual(golds[0].Args[RewardKinds.IndexArgument], golds[1].Args[RewardKinds.IndexArgument]);
        Assert.All(recorded.Manifest.Actions.Where(action => action.Verb is ActionVerb.ClaimReward or ActionVerb.TakeCard),
            action => Assert.Contains(RewardKinds.IndexArgument, action.Args.Keys));

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>
    /// Lord's Parasol buys the whole shop as the merchant is entered, through the
    /// purchase member with <c>ignoreCost</c> set from inside the map move's own work;
    /// the recorder wrote each as a purchase the player made, ahead of the move that
    /// opened the shop, and the replay refused the first in the room the move left. A
    /// purchase the engine makes for itself is not a decision and is recorded nowhere;
    /// the replay's own move reproduces it, and the removal the relic then opens is a
    /// card selection behind the move.
    /// </summary>
    [GameFact]
    public void TheShopLordsParasolBuysOnEntryIsNotARecordedDecision()
    {
        using var harness = new RecordedActWalk();

        var recorded = harness.Walk(
            new WalkPolicy { AncientRelic = "RELIC.LORDS_PARASOL", ShopWhileHoldingIt = true, RouteThrough = [MapPointType.Shop] },
            LordsParasolSeed, visitEveryRoomType: false, acts: ["ACT.GLORY"]);
        Assert.True(recorded.AskMet, "the walk finished without entering a merchant holding Lord's Parasol");
        RecordedActWalk.AssertWhole(recorded);

        var offer = Assert.Single(recorded.Manifest.Actions, action => action.Verb == ActionVerb.ChooseEventOption);
        Assert.Equal("RELIC.LORDS_PARASOL", offer.Args["option_key"]);
        Assert.DoesNotContain(recorded.Manifest.Actions, action => action.Verb == ActionVerb.ShopPurchase);

        // The move that opened the shop is followed by the relic's own removal,
        // answered on the screen it opened, and by nothing bought
        var intoTheShop = recorded.Manifest.Actions.Last(action => action.Verb == ActionVerb.MapMove);
        var after = recorded.Manifest.Actions.Where(action => action.Seq > intoTheShop.Seq).Select(action => action.Verb).ToList();
        Assert.Contains(ActionVerb.SelectCardFromScreen, after);

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>
    /// Every act after the first opens on the map with nothing visited - the act
    /// change clears the run's visited coordinates and <c>RunManager.EnterAct</c>
    /// enters the starting point itself for the first act only - so a run's first
    /// decision in act 2 is a move to the act's starting point from no node at all,
    /// which the map screen alone offers there. The driver refused it as unreachable,
    /// which refused every recording of a run past its first act at that move. Walked
    /// on the whole-act fixture's own seed, the one the journey's rules are known to
    /// carry through a first act, into the second act's opening ancient, which is the
    /// walk's ask (<see cref="WalkPolicy.OpeningTheNextActIsTheAsk"/>).
    /// </summary>
    [GameFact]
    public void TheMoveToTheSecondActsStartingPointReplays()
    {
        using var harness = new RecordedActWalk();

        var recorded = harness.Walk(
            new WalkPolicy { AskInTheNextAct = true, StopOnceMet = true },
            RecordedActWalk.FixtureSeed);
        Assert.True(recorded.AskMet, "the walk finished without answering the second act's ancient");
        RecordedActWalk.AssertWhole(recorded);

        var proceed = Assert.Single(recorded.Manifest.Actions, action => action.Verb == ActionVerb.ProceedToNextAct);
        var opening = recorded.Manifest.Actions.First(action => action.Seq > proceed.Seq);
        Assert.Equal(ActionVerb.MapMove, opening.Verb);
        Assert.Equal("1", opening.Args["act"]);
        var ancient = recorded.Manifest.Actions.First(action => action.Seq > opening.Seq);
        Assert.Equal(ActionVerb.ChooseEventOption, ancient.Verb);
        Assert.Contains(ancient.Args["event_id"], DecisionSurface.ActAncients().Select(pair => pair.AncientId).Append("EVENT.DARV"));

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>
    /// The first decision after a fight fought inside an event is made back in the
    /// event: the retail proceed off the fight's loot screen resumes it before the
    /// player can decide anything, and the recorder reads that decision there, with
    /// no fight current. The replay samples a decision's before-reading between
    /// <see cref="RunDriver.Approach"/> and <see cref="RunDriver.Apply"/>, and a
    /// resume made in <c>Apply</c> alone left that reading in the finished combat
    /// room, where <c>combat.outcome</c> reads <c>victory</c> against the recorder's
    /// <c>none</c>. The event rows stop at the end of the event's floor, so the walk
    /// is carried one decision further here, to the move that leaves the event.
    /// </summary>
    [GameFact]
    public void TheFirstDecisionAfterAFightInsideAnEventReadsInTheResumedEvent()
    {
        using var harness = new RecordedActWalk();
        var row = GeneratedCoverageTests.EventRowFor("ACT.GLORY", "EVENT.BATTLEWORN_DUMMY", "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_1");

        var recorded = harness.Walk(
            GeneratedCoverageTests.PolicyFor(row), row.Seed, visitEveryRoomType: false, GeneratedCoverageTests.EventRowActs["ACT.GLORY"],
            then: (session, driver, settle) =>
            {
                Assert.IsType<EventRoom>(session.RunState.CurrentRoom);
                var map = session.RunState.Map!;
                var coord = session.RunState.CurrentMapCoord!.Value;
                var next = MapTravelRule.TravelableFrom(session.RunState, map, map.GetPoint(coord.col, coord.row))[0];
                driver.Apply(new ActionRecord
                {
                    Seq = RunRecorder.Active!.Capture.Actions.Count,
                    Verb = ActionVerb.MapMove,
                    Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["act"] = session.RunState.CurrentActIndex.ToString(CultureInfo.InvariantCulture),
                        ["row"] = next.coord.row.ToString(CultureInfo.InvariantCulture),
                        ["column"] = next.coord.col.ToString(CultureInfo.InvariantCulture),
                    },
                    Source = FactSource.Declared,
                });
                settle();
            });
        Assert.True(recorded.AskMet, "the walk finished without choosing the dummy's first setting");
        RecordedActWalk.AssertWhole(recorded);

        var lastLoot = recorded.Manifest.Actions.Last(action => action.Verb is ActionVerb.ClaimReward or ActionVerb.TakeCard or ActionVerb.SkipRewards);
        var leaving = recorded.Manifest.Actions.Last();
        Assert.Equal(ActionVerb.MapMove, leaving.Verb);
        Assert.True(leaving.Seq > lastLoot.Seq, "the move that leaves the event was not recorded after the fight's loot");

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>The game's own rule at an act's start, as <c>MapTravelRule</c> reads
    /// it: a run standing on no node of this map is offered the starting point and
    /// nothing else.</summary>
    [GameFact]
    public void AtAnActsStartTheStartingPointIsTheOneTravelableNode()
    {
        if (MegaCrit.Sts2.Core.Runs.RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(RecordedActWalk.FixtureSeed, "CHARACTER.IRONCLAD", 0, "standard", RecordedActWalk.Acts);
        try
        {
            session.EnterActForMap(0);
            var map = session.RunState.Map!;
            Assert.Equal([map.StartingMapPoint], MapTravelRule.TravelableFrom(session.RunState, map, null));
        }
        finally
        {
            if (MegaCrit.Sts2.Core.Runs.RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        }
    }
}
