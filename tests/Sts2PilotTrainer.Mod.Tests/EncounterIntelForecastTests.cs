using System.Reflection;
using System.Text.Json;
using EncounterIntel;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed partial class EncounterIntelTests
{
    [Fact]
    public void PureRoomProjectionKeepsFilteringOrderingSaturationAndFallbackSeparate()
    {
        var all = Enum.GetValues<ForecastRoomType>().ToHashSet();
        var fresh = UnknownRoomRules.Project(new(.1f, -1, .02f, .03f), all, false, 0);
        Assert.Equal(.1f, fresh.Chances[0].Probability, 6);
        Assert.Equal(.85, fresh.Chances[4].Probability, 6);
        all.Remove(ForecastRoomType.Monster);
        all.Remove(ForecastRoomType.Shop);
        var filtered = UnknownRoomRules.Project(new(.95f, -1, .02f, .8f), all, false, 0);
        Assert.Equal(.98, filtered.Chances[4].Probability, 6);
        var saturated = UnknownRoomRules.Project(new(.7f, .6f, .2f, .3f), Enum.GetValues<ForecastRoomType>().ToHashSet(), false, 0);
        Assert.Equal(.7, saturated.Chances[0].Probability, 6);
        Assert.Equal(.3, saturated.Chances[1].Probability, 6);
        Assert.All(saturated.Chances.Skip(2), chance => Assert.Equal(0, chance.Probability));
        var noEvent = UnknownRoomRules.Project(new(-1, -1, .02f, .03f), new HashSet<ForecastRoomType> { ForecastRoomType.Treasure, ForecastRoomType.Shop }, false, 0);
        Assert.Equal(.97, noEvent.Chances[2].Probability, 6);
        Assert.Equal(ForecastRoomType.Treasure, noEvent.OutcomeAt(.99f));
        Assert.Throws<IntelRefusal>(() => UnknownRoomRules.Project(new(float.NaN, 0, 0, 0), all, false, 0));
        Assert.Throws<IntelRefusal>(() => UnknownRoomRules.Project(new(0, 0, 0, 0), new HashSet<ForecastRoomType>(), false, 0));
        Assert.Throws<IntelRefusal>(() => UnknownRoomRules.Project(new(0, 0, 0, 0), all, false, -1));
    }

    [GameTheory]
    [InlineData(false, false, false, .1f, -1f, .02f, .03f)]
    [InlineData(true, false, false, .1f, -1f, .02f, .03f)]
    [InlineData(false, true, false, .7f, -1f, .2f, .8f)]
    [InlineData(true, true, false, .95f, -1f, .02f, .8f)]
    [InlineData(false, false, false, .7f, .6f, .2f, .3f)]
    [InlineData(false, false, false, 0f, -1f, 0f, 0f)]
    [InlineData(false, false, true, .1f, .1f, .02f, .03f)]
    public void NextVisitIntervalsMatchTheRealRollWithoutTouchingTheRunsRandomnessOrCounters(
        bool juzu, bool shopChildren, bool deadly, float monster, float elite, float treasure, float shop)
    {
        WithForecastRun(false, deadly, session =>
        {
            var candidate = StageNextQuestionMark(session, shopChildren);
            if (juzu) RelicCmd.Obtain(ModelDb.Relic<JuzuBracelet>().ToMutable(), session.RunState.Players[0]).GetAwaiter().GetResult();
            SetOdds(session.RunState.Odds.UnknownMapPoint, new(monster, elite, treasure, shop));
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var forecast = Ready(EncounterIntelMod.NextUnknownRoom(candidate));
            Assert.Equal(shopChildren, forecast.ShopBlacklisted);
            Assert.Equal(juzu, forecast.RecognizedHooks.Contains(typeof(JuzuBracelet).FullName!));
            Assert.Equal(deadly, forecast.RecognizedHooks.Contains(typeof(DeadlyEvents).FullName!));
            Assert.Equal(1, forecast.Projection.Chances.Sum(chance => chance.Probability), 12);
            var blacklist = RunManager.BuildRoomTypeBlacklist(session.RunState.CurrentMapPointHistoryEntry, candidate.Children);
            MatchRoll(session.RunState, forecast, blacklist);
            if (juzu && monster == .1f) Assert.Equal(.95, forecast.Projection.Chances.Single(chance => chance.Type == ForecastRoomType.Event).Probability, 6);
            if (juzu && shopChildren) Assert.Equal(0, session.RunState.Odds.UnknownMapPoint.EventOdds);
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Evidence($"forecast-juzu-{juzu}-shop-{shopChildren}-deadly-{deadly}-{monster}", forecast);
        });
    }

    [GameTheory]
    [InlineData(0, "Event")]
    [InlineData(1, "Event")]
    [InlineData(2, "Monster")]
    [InlineData(3, "Event")]
    public void FirstRunReturnsBeforeJuzuBlacklistAndAdaptiveUpdates(int visits, string expected)
    {
        WithForecastRun(true, false, session =>
        {
            var candidate = StageNextQuestionMark(session, true);
            RelicCmd.Obtain(ModelDb.Relic<JuzuBracelet>().ToMutable(), session.RunState.Players[0]).GetAwaiter().GetResult();
            for (var i = 0; i < visits; i++) session.RunState.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, null);
            var forecast = Ready(EncounterIntelMod.NextUnknownRoom(candidate));
            Assert.True(forecast.FirstRun);
            Assert.Equal(visits, forecast.PriorUnknownVisits);
            Assert.Equal(expected, forecast.Projection.OutcomeAt(.5f).ToString());
            MatchRoll(session.RunState, forecast, [RoomType.Shop]);
            if (visits <= 2)
            {
                var clone = CloneOdds(forecast.RawOdds);
                var before = ReadOdds(clone);
                Assert.Equal(expected, clone.Roll([RoomType.Shop], session.RunState).ToString());
                Assert.Equal(before, ReadOdds(clone));
            }
        });
    }

    [GameFact]
    public void FilteredFallbackWithoutEventMatchesTheNativeRollInsteadOfRenormalizing()
    {
        WithForecastRun(false, false, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            var original = Ready(EncounterIntelMod.NextUnknownRoom(candidate));
            var projection = UnknownRoomRules.Project(original.RawOdds, new HashSet<ForecastRoomType> { ForecastRoomType.Treasure, ForecastRoomType.Shop }, false, 0);
            var before = StateReceipt(session);
            MatchRoll(session.RunState, original with { Projection = projection }, [RoomType.Monster, RoomType.Elite, RoomType.Event]);
            Assert.Equal(.97, projection.Chances[2].Probability, 6);
            Assert.Equal(before, StateReceipt(session));
        });
    }

    [GameFact]
    public void TheFirstRunUnknownOrdinalContinuesAcrossActsWhileTheNormalPhaseResets()
    {
        WithForecastRun(true, false, session =>
        {
            session.RunState.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, null);
            session.RunState.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, null);
            RunManager.Instance.SetActInternal(1).GetAwaiter().GetResult();
            var start = session.RunState.Map.StartingMapPoint;
            start.PointType = MapPointType.Unknown;
            var forecast = Ready(EncounterIntelMod.NextUnknownRoom(start));
            Assert.Equal(2, forecast.PriorUnknownVisits);
            Assert.Equal(ForecastRoomType.Monster, forecast.Projection.OutcomeAt(.9f));
            Assert.Equal(0, Ready(EncounterIntelMod.NextEncounterPools()).NormalVisits);
            MatchRoll(session.RunState, forecast, []);
        });
    }

    [GameFact]
    public void TheBlacklistUsesEveryRoomAtThePreviousNodeAndOnlyTheCandidatesChildren()
    {
        WithForecastRun(false, false, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            Assert.False(Ready(EncounterIntelMod.NextUnknownRoom(candidate)).ShopBlacklisted);
            candidate.Children.Clear();
            Assert.False(Ready(EncounterIntelMod.NextUnknownRoom(candidate)).ShopBlacklisted);
            candidate.AddChildPoint(new MapPoint(0, candidate.coord.row + 1) { PointType = MapPointType.Shop });
            Assert.True(Ready(EncounterIntelMod.NextUnknownRoom(candidate)).ShopBlacklisted);
            candidate.AddChildPoint(new MapPoint(1, candidate.coord.row + 1) { PointType = MapPointType.Monster });
            Assert.False(Ready(EncounterIntelMod.NextUnknownRoom(candidate)).ShopBlacklisted);
            // A shop drawn inside an event still blacklists a following random shop
            session.RunState.CurrentMapPointHistoryEntry!.Rooms.Add(new() { RoomType = RoomType.Shop });
            Assert.True(Ready(EncounterIntelMod.NextUnknownRoom(candidate)).ShopBlacklisted);
            AssertUnavailable(EncounterIntelMod.NextUnknownRoom(new MapPoint(candidate.coord.col, candidate.coord.row) { PointType = MapPointType.Unknown }), LookupState.Refused);
            var distant = candidate.Children.First();
            distant.PointType = MapPointType.Unknown;
            AssertUnavailable(EncounterIntelMod.NextUnknownRoom(distant), LookupState.Refused);
        });
    }

    [GameFact]
    public void AdaptiveReadsAndActResetMatchTheEngineIncludingDeadlyEvents()
    {
        WithForecastRun(false, true, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            Assert.Equal(.1f, session.RunState.Odds.UnknownMapPoint.EliteOdds);
            var harmony = new Harmony("encounter-intel-test.adaptive-draw");
            try
            {
                InstallFixedDraw(harmony);
                var clone = CloneOdds(ReadOdds(session.RunState.Odds.UnknownMapPoint));
                clone.SetBaseOdds(RoomType.Elite, .1f);
                foreach (var draw in new[] { .9f, .9f, .01f, .99f, .99f, .5f })
                {
                    SetOdds(session.RunState.Odds.UnknownMapPoint, ReadOdds(clone));
                    var before = StateReceipt(session);
                    var forecast = Ready(EncounterIntelMod.NextUnknownRoom(candidate));
                    ForecastDraw = draw;
                    var actual = clone.Roll([], session.RunState);
                    Assert.Equal(actual.ToString(), forecast.Projection.OutcomeAt(draw).ToString());
                    Assert.Equal(before, StateReceipt(session));
                    if (draw == .9f) Assert.True(clone.TreasureOdds >= .06f);
                }
                ForecastDraw = null;
                RunManager.Instance.SetActInternal(1).GetAwaiter().GetResult();
                Assert.Equal(1, session.RunState.CurrentActIndex);
                Assert.Empty(session.RunState.VisitedMapCoords);
                var resetPool = Ready(EncounterIntelMod.NextEncounterPools());
                Assert.Equal("ACT.HIVE", resetPool.ActId);
                Assert.Equal(0, resetPool.NormalVisits);
                Assert.Equal(NormalEncounterPhase.Weak, resetPool.NormalPhase);
                var start = session.RunState.Map.StartingMapPoint;
                start.PointType = MapPointType.Unknown;
                var reset = Ready(EncounterIntelMod.NextUnknownRoom(start));
                Assert.Equal(new RawRoomOdds(.1f, .1f, .02f, .03f), reset.RawOdds);
                Assert.Equal(.75, reset.Projection.Chances[4].Probability, 6);
            }
            finally { ForecastDraw = null; harmony.UnpatchAll(harmony.Id); }
        });
    }

    [GameTheory]
    [InlineData("ACT.OVERGROWTH", 3)]
    [InlineData("ACT.UNDERDOCKS", 3)]
    [InlineData("ACT.HIVE", 2)]
    [InlineData("ACT.GLORY", 2)]
    public void VisibleBaseRoomHistorySelectsTheUpcomingPhaseWithoutReadingADeal(string actId, int weak)
    {
        WithRun(0, [actId], session =>
        {
            for (var count = 0; count <= weak; count++)
            {
                var before = StateReceipt(session);
                var pool = Ready(EncounterIntelMod.NextEncounterPools());
                Assert.Equal(count, pool.NormalVisits);
                Assert.Equal(weak, pool.WeakEncounterCount);
                Assert.Equal(count < weak ? NormalEncounterPhase.Weak : NormalEncounterPhase.Regular, pool.NormalPhase);
                var expected = count < weak ? session.RunState.Act.AllWeakEncounters : session.RunState.Act.AllRegularEncounters;
                Assert.Equal(expected.Select(model => model.Id.ToString()), pool.NormalPool.Select(row => row.Reference.Id));
                Assert.Equal(session.RunState.Act.AllEliteEncounters.Select(model => model.Id.ToString()), pool.ElitePool.Select(row => row.Reference.Id));
                Assert.Equal(PoolBasis.PhaseDefinitions, pool.NormalBasis);
                Assert.Equal(before, StateReceipt(session));
                session.RunState.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Monster, null);
                session.RunState.Act.MarkRoomVisited(RoomType.Monster);
            }
            session.RunState.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, null);
            session.RunState.CurrentMapPointHistoryEntry!.Rooms.Add(new() { RoomType = RoomType.Monster });
            session.RunState.AppendToMapPointHistory(MapPointType.Elite, RoomType.Elite, null);
            session.RunState.AppendToMapPointHistory(MapPointType.Boss, RoomType.Boss, null);
            Assert.Equal(weak + 1, Ready(EncounterIntelMod.NextEncounterPools()).NormalVisits);
            Evidence($"forecast-pools-{actId}", Ready(EncounterIntelMod.NextEncounterPools()));
        });
    }

    [GameFact]
    public void ADirectQuestionMarkConsumesTheNormalSequenceButAnActualNestedEventFightDoesNot()
    {
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unknown, ModelDb.Encounter<NibbitsWeak>().ToMutable(), false).GetAwaiter().GetResult();
            Pump.Drain();
            Assert.Equal(1, Ready(EncounterIntelMod.NextEncounterPools()).NormalVisits);
            EnterLanternBoard();
            using var driver = new RunDriver(session);
            var local = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            driver.Apply(HeadlessRuns.Record(0, ActionVerb.ChooseEventOption, ("event_id", local.Id.ToString()), ("option_index", "1"), ("option_key", RunDriver.OptionKey(local.CurrentOptions[1]))));
            driver.Apply(HeadlessRuns.Record(1, ActionVerb.ChooseEventOption, ("event_id", local.Id.ToString()), ("option_index", "0"), ("option_key", RunDriver.OptionKey(local.CurrentOptions[0]))));
            Assert.True(CombatManager.Instance.IsInProgress);
            Assert.Equal(RoomType.Event, session.RunState.CurrentMapPointHistoryEntry!.Rooms[0].RoomType);
            Assert.Contains(session.RunState.CurrentMapPointHistoryEntry.Rooms.Skip(1), room => room.RoomType == RoomType.Monster);
            Assert.Equal(1, Ready(EncounterIntelMod.NextEncounterPools()).NormalVisits);
        });
    }

    [GameFact]
    public void FirstRunScriptForecastsMatchTheEncountersTheEngineDealsThroughExhaustion()
    {
        WithForecastRun(true, false, session =>
        {
            RoomType[] visits = [RoomType.Monster, RoomType.Elite, RoomType.Monster, RoomType.Monster, RoomType.Elite, RoomType.Monster,
                RoomType.Monster, RoomType.Monster, RoomType.Monster, RoomType.Elite, RoomType.Monster];
            var scripted = new Dictionary<RoomType, int> { [RoomType.Monster] = 0, [RoomType.Elite] = 0 };
            foreach (var type in visits)
            {
                var before = StateReceipt(session);
                var pools = Ready(EncounterIntelMod.NextEncounterPools());
                Assert.Equal(before, StateReceipt(session));
                var (basis, pool) = type == RoomType.Monster ? (pools.NormalBasis, pools.NormalPool) : (pools.EliteBasis, pools.ElitePool);
                RunManager.Instance.EnterRoomDebug(type, type == RoomType.Monster ? MapPointType.Monster : MapPointType.Elite, null, false).GetAwaiter().GetResult();
                Pump.Drain();
                var dealt = HeadlessRuns.Field(session, "combat.encounter");
                if (basis == PoolBasis.FirstRunScript)
                {
                    scripted[type]++;
                    Assert.Equal(dealt, Assert.Single(pool).Reference.Id);
                }
                else Assert.Contains(dealt, pool.Select(row => row.Reference.Id));
                foreach (var enemy in CombatManager.Instance.DebugOnlyGetState()!.Enemies.ToArray()) CreatureCmd.Kill(enemy).GetAwaiter().GetResult();
                CombatManager.Instance.CheckWinCondition().GetAwaiter().GetResult();
                Pump.Drain();
            }
            Assert.Equal(7, scripted[RoomType.Monster]);
            Assert.Equal(2, scripted[RoomType.Elite]);
            var exhausted = Ready(EncounterIntelMod.NextEncounterPools());
            Assert.Equal(PoolBasis.PhaseDefinitions, exhausted.NormalBasis);
            Assert.Equal(PoolBasis.PhaseDefinitions, exhausted.EliteBasis);
        });
    }

    [GameTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ContinueKeepsAdaptiveOddsAndVisibleHistoryWithoutCountingTheRestoredRoomTwice(bool firstRun, bool deadly)
    {
        WithForecastRun(firstRun, deadly, session =>
        {
            // Stage a native arrival without the first-account FTUE's scene-only opening event
            RunManager.Instance.GenerateMap().GetAwaiter().GetResult();
            var entry = session.RunState.Map.StartingMapPoint.Children.First();
            RunManager.Instance.EnterMapCoordDebug(entry.coord, RoomType.Monster, MapPointType.Monster,
                ModelDb.Encounter<NibbitsWeak>().ToMutable(), false).GetAwaiter().GetResult();
            Pump.Drain();
            foreach (var enemy in CombatManager.Instance.DebugOnlyGetState()!.Enemies.ToArray()) CreatureCmd.Kill(enemy).GetAwaiter().GetResult();
            CombatManager.Instance.CheckWinCondition().GetAwaiter().GetResult();
            Pump.Drain();
            RelicCmd.Obtain(ModelDb.Relic<JuzuBracelet>().ToMutable(), session.RunState.Players[0]).GetAwaiter().GetResult();
            var candidate = session.RunState.CurrentMapPoint!.Children.First();
            candidate.PointType = MapPointType.Unknown;
            SetOdds(session.RunState.Odds.UnknownMapPoint, new(.4f, deadly ? .3f : -1, .08f, .15f));
            var pools = Ready(EncounterIntelMod.NextEncounterPools());
            var forecast = Ready(EncounterIntelMod.NextUnknownRoom(candidate));
            var save = RunManager.Instance.ToSave(session.RunState.CurrentRoom);
            var json = JsonSerializationUtility.ToJson(save);
            HeadlessRuns.EndAnyRun();
            var continued = new GameSession();
            continued.RestoreSavedRun(json);
            Pump.Drain();
            var restored = continued.RunState.Map.GetPoint(candidate.coord)!;
            var before = StateReceipt(continued);
            Assert.Equal(JsonSerializer.Serialize(pools), JsonSerializer.Serialize(Ready(EncounterIntelMod.NextEncounterPools())));
            Assert.Equal(JsonSerializer.Serialize(forecast), JsonSerializer.Serialize(Ready(EncounterIntelMod.NextUnknownRoom(restored))));
            Assert.Equal(before, StateReceipt(continued));
            AssertUnavailable(EncounterIntelMod.NextUnknownRoom(candidate), LookupState.Refused);
            Evidence($"forecast-continue-first-{firstRun}-deadly-{deadly}", Ready(EncounterIntelMod.NextUnknownRoom(restored)));
            if (deadly)
            {
                RunManager.Instance.SetActInternal(1).GetAwaiter().GetResult();
                Assert.Equal(.1f, continued.RunState.Odds.UnknownMapPoint.EliteOdds);
                Assert.Equal(0, Ready(EncounterIntelMod.NextEncounterPools()).NormalVisits);
            }
        });
    }

    [GameFact]
    public void PatchedForecastHooksAndRollSemanticsRefuseUntilTheyAreRemoved()
    {
        WithForecastRun(false, false, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            var methods = new[]
            {
                typeof(JuzuBracelet).GetMethod(nameof(AbstractModel.ModifyUnknownMapPointRoomTypes))!,
                typeof(DeadlyEvents).GetMethod(nameof(AbstractModel.ModifyOddsIncreaseForUnrolledRoomType))!,
                typeof(UnknownMapPointOdds).GetMethod(nameof(UnknownMapPointOdds.Roll))!,
                typeof(RunManager).GetMethod(nameof(RunManager.BuildRoomTypeBlacklist))!,
                typeof(MapPoint).GetProperty(nameof(MapPoint.PointType))!.GetMethod!,
            };
            var harmony = new Harmony("encounter-intel-test.forecast-unknown-hook");
            foreach (var method in methods)
            {
                var before = StateReceipt(session);
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(RejectForecastHook), BindingFlags.NonPublic | BindingFlags.Static)!));
                    AssertUnavailable(EncounterIntelMod.NextUnknownRoom(candidate), LookupState.Refused);
                    AssertUnavailable(EncounterIntelMod.NextEncounterPools(), LookupState.Refused);
                }
                finally { harmony.UnpatchAll(harmony.Id); }
                Assert.Equal(before, StateReceipt(session));
                Ready(EncounterIntelMod.NextUnknownRoom(candidate));
            }
        });
    }

    [GameFact]
    public void AnUnknownModifierCannotBorrowARecognizedParentsForecastRules()
    {
        WithForecastRun(false, false, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            session.RunState.AddModifierDebug(new UnknownForecastModifier());
            var before = StateReceipt(session);
            AssertUnavailable(EncounterIntelMod.NextUnknownRoom(candidate), LookupState.Refused);
            AssertUnavailable(EncounterIntelMod.NextEncounterPools(), LookupState.Refused);
            Assert.Equal(before, StateReceipt(session));
        });
    }

    private sealed class UnknownForecastModifier : DeadlyEvents
    {
        public override IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(IReadOnlySet<RoomType> roomTypes) =>
            throw new InvalidOperationException("An unknown model's hook must not execute");
    }

    [GameFact]
    public void UnknownRunHookSubscriptionsAreRefusedBeforeTheirDelegatesExecute()
    {
        WithForecastRun(false, false, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            var subscriptions = (System.Collections.IList)typeof(ModHelper).GetField("_runHookSubscribers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var count = subscriptions.Count;
            ModHelper.SubscribeForRunStateHooks("encounter-intel-test.unknown-subscription", _ => throw new InvalidOperationException("An arbitrary subscriber must not execute"));
            try
            {
                AssertUnavailable(EncounterIntelMod.NextUnknownRoom(candidate), LookupState.Refused);
                AssertUnavailable(EncounterIntelMod.NextEncounterPools(), LookupState.Refused);
            }
            finally
            {
                var subscription = subscriptions.Cast<object>().Single(value => (string)value.GetType().GetField("id")!.GetValue(value)! == "encounter-intel-test.unknown-subscription");
                subscriptions.Remove(subscription);
            }
            Assert.Equal(count, subscriptions.Count);
            Ready(EncounterIntelMod.NextUnknownRoom(candidate));
        });
    }

    [GameFact]
    public void ForecastLookupsCallNoGameplayCommandRandomStreamOrHiddenDeal()
    {
        WithForecastRun(false, false, session =>
        {
            var candidate = StageNextQuestionMark(session, false);
            var harmony = new Harmony("encounter-intel-test.forecast-command-guard");
            var prefix = new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(RejectGraphCommand), BindingFlags.NonPublic | BindingFlags.Static)!);
            try
            {
                foreach (var method in WatchedGameplayMembers()) harmony.Patch(method, prefix: prefix);
                var before = StateReceipt(session);
                var files = ProfileReceipt();
                for (var i = 0; i < 5; i++)
                {
                    Ready(EncounterIntelMod.NextUnknownRoom(candidate));
                    Ready(EncounterIntelMod.NextEncounterPools());
                }
                Assert.Equal(before, StateReceipt(session));
                Assert.Equal(files, ProfileReceipt());
                Assert.Throws<IntelRefusal>(() => session.RunState.Rng.UnknownMapPoint.NextFloat());
            }
            finally { harmony.UnpatchAll(harmony.Id); }
        });
    }

    private static void RejectForecastHook() => throw new InvalidOperationException("An unknown forecast hook must not execute");
    private static float? ForecastDraw;
    private static bool FixedForecastDraw(ref float __result)
    {
        if (ForecastDraw is not { } draw) return true;
        __result = draw;
        return false;
    }
    private static void InstallFixedDraw(Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(FixedForecastDraw), BindingFlags.NonPublic | BindingFlags.Static)!);
        foreach (var method in typeof(Rng).GetMethods().Where(method => method.Name == nameof(Rng.NextFloat)))
            harmony.Patch(method, prefix: prefix);
    }

    private static void MatchRoll(RunState run, UnknownRoomForecast forecast, IEnumerable<RoomType> blacklist)
    {
        var harmony = new Harmony("encounter-intel-test.forecast-oracle");
        try
        {
            InstallFixedDraw(harmony);
            var boundaries = forecast.Projection.Intervals.Select(interval => interval.InclusiveThreshold).Where(value => value is >= 0 and <= 1);
            var draws = new[] { 0f, float.Epsilon, 1f, .999999f }.Concat(Enumerable.Range(0, 101).Select(i => i / 100f))
                .Concat(boundaries.SelectMany(value => new[] { MathF.BitDecrement(value), value, MathF.BitIncrement(value) }))
                .Where(value => value is >= 0 and <= 1).Distinct();
            foreach (var draw in draws)
            {
                ForecastDraw = draw;
                var clone = CloneOdds(forecast.RawOdds);
                Assert.Equal(clone.Roll(blacklist, run).ToString(), forecast.Projection.OutcomeAt(draw).ToString());
            }
        }
        finally { ForecastDraw = null; harmony.UnpatchAll(harmony.Id); }
    }
    private static UnknownMapPointOdds CloneOdds(RawRoomOdds raw)
    {
        var odds = new UnknownMapPointOdds(new Rng(7));
        SetOdds(odds, raw);
        return odds;
    }
    private static RawRoomOdds ReadOdds(UnknownMapPointOdds odds) => new(odds.MonsterOdds, odds.EliteOdds, odds.TreasureOdds, odds.ShopOdds);
    private static void SetOdds(UnknownMapPointOdds odds, RawRoomOdds raw)
    {
        odds.MonsterOdds = raw.Monster;
        odds.EliteOdds = raw.Elite;
        odds.TreasureOdds = raw.Treasure;
        odds.ShopOdds = raw.Shop;
    }
    private static MapPoint StageNextQuestionMark(GameSession session, bool shopChildren)
    {
        RunManager.Instance.GenerateMap().GetAwaiter().GetResult();
        var start = session.RunState.Map.StartingMapPoint;
        session.RunState.AddVisitedMapCoord(start.coord);
        session.RunState.AppendToMapPointHistory(MapPointType.Ancient, RoomType.Event, null);
        var candidate = start.Children.First();
        candidate.PointType = MapPointType.Unknown;
        foreach (var child in candidate.Children) child.PointType = shopChildren ? MapPointType.Shop : MapPointType.Monster;
        return candidate;
    }
    private static void WithForecastRun(bool firstRun, bool deadly, Action<GameSession> body)
    {
        HeadlessRuns.EndAnyRun();
        try
        {
            var session = new GameSession();
            session.StartRun("P1L0TTRA1NER", "CHARACTER.IRONCLAD", 0, "standard", ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"],
                firstRun ? PlayerProgress.NoneUnlocked : PlayerProgress.AllUnlocked, deadly ? [typeof(DeadlyEvents).FullName!] : []);
            body(session);
        }
        finally { HeadlessRuns.EndAnyRun(); HeadlessEngine.Forget(); }
    }
}
