using System.Reflection;
using System.Text.Json;
using EncounterIntel;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed partial class EncounterIntelTests
{
    [GameFact]
    public void LookupEntryPointsWaitForModelsAndRecoverAfterStartupAndTeardown()
    {
        if (Environment.GetEnvironmentVariable("ENCOUNTER_INTEL_LOOKUP_CHILD") != "1")
        {
            CheckIntelInFreshProcess(nameof(LookupEntryPointsWaitForModelsAndRecoverAfterStartupAndTeardown), "ENCOUNTER_INTEL_LOOKUP_CHILD");
            return;
        }
        _ = EngineHost.Origin;
        CheckStartupLookup();
    }

    private static void CheckStartupLookup()
    {
        Assert.Empty(ModelDb.All);
        EncounterIntelMod.Initialize();
        Assert.Empty(ModelDb.All);
        AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.NotReady);
        AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.NotReady);
        Assert.Empty(ModelDb.All);
        Assert.False(RunManager.Instance.IsInProgress);
        EngineHost.Start();
        AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.NoRun);
        AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.NoRun);
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            Assert.Equal("ACT.OVERGROWTH", Ready(EncounterIntelMod.CurrentAct()).Id);
            Assert.Equal(BoardKind.None, Ready(EncounterIntelMod.CurrentBoard()).Kind);
            var rooms = typeof(ActModel).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session.RunState.Act)!;
            var bossField = typeof(RoomSet).GetField("_boss", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var boss = bossField.GetValue(rooms);
            try
            {
                // Stage the engine's pre-GenerateRooms state without running generation at lookup
                bossField.SetValue(rooms, null);
                AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.NotReady);
                AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.NotReady);
            }
            finally { bossField.SetValue(rooms, boss); }
            Assert.NotEmpty(Ready(EncounterIntelMod.CurrentAct()).SelectedBosses);
        });
        AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.NoRun);
        AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.NoRun);
        Assert.False(RunManager.Instance.IsInProgress);
    }

    [GameTheory]
    [InlineData("ACT.OVERGROWTH", 0, 1)]
    [InlineData("ACT.UNDERDOCKS", 0, 1)]
    [InlineData("ACT.GLORY", 9, 1)]
    [InlineData("ACT.GLORY", 10, 2)]
    public void ActEntryJoinsTheCurrentPoolsAndPublicBossesToTheProvedCatalogue(string actId, int ascension, int bosses)
    {
        WithRun(ascension, [actId], session =>
        {
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var reading = Ready(EncounterIntelMod.CurrentAct());
            Assert.Equal(actId, reading.Id);
            Assert.Equal(ascension, reading.Ascension);
            Assert.Equal(bosses, reading.SelectedBosses.Count);
            Assert.Equal(session.RunState.Act.AllEliteEncounters.Select(model => model.Id.ToString()), reading.Elites.Select(row => row.Reference.Id));
            Assert.Equal(session.RunState.Act.AllBossEncounters.Select(model => model.Id.ToString()), reading.BossPool.Select(row => row.Reference.Id));
            Assert.Equal(session.RunState.Act.Title.LocEntryKey, reading.Name.Key);
            var catalogue = RuntimeAdapter.ReadCatalogue();
            Assert.Equal(catalogue.Attribution, reading.Provenance.Attribution);
            Assert.Equal(catalogue.Sources, reading.Provenance.Sources);
            Assert.Equal(catalogue.PatternGaps, reading.Provenance.Gaps);
            foreach (var encounter in reading.Elites.Concat(reading.BossPool).Concat(reading.SelectedBosses))
            {
                var model = ModelDb.All.OfType<EncounterModel>().Single(model => model.Id.ToString() == encounter.Reference.Id);
                Assert.Equal(model.Title.LocTable, encounter.Name.Table);
                Assert.Equal(model.Title.LocEntryKey, encounter.Name.Key);
                Assert.Equal(model.MapNodeAssetPaths, encounter.MapAssets);
                Assert.Equal(JsonSerializer.Serialize(catalogue.Encounters.Single(row => row.Id == encounter.Reference.Id)), JsonSerializer.Serialize(encounter.Reference));
                Assert.Equal(encounter.Reference.PossibleMonsterIds, encounter.PossibleMembers.Select(row => row.Reference.Id));
                Assert.All(encounter.PossibleMembers, member =>
                {
                    Assert.Equal(JsonSerializer.Serialize(catalogue.Monsters.Single(row => row.Id == member.Reference.Id)), JsonSerializer.Serialize(member.Reference));
                    Assert.StartsWith("res://", member.Native.VisualsScene);
                    Assert.Equal("monsters", member.Native.Name.Table);
                    var monster = model.AllPossibleMonsters.Single(monster => monster.Id.ToString() == member.Reference.Id);
                    Assert.Equal(monster.Title.Variables.ToDictionary(pair => pair.Key, pair => (decimal)pair.Value), member.Native.Name.Numbers);
                    Assert.NotNull(member.Reference.Pattern);
                });
            }
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Evidence($"lookup-{actId}-a{ascension}", reading);
        });
    }

    [GameFact]
    public void ActEntryRefreshesOnActAndRunChangesRatherThanRetainingTheEarlierRun()
    {
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            var first = Ready(EncounterIntelMod.CurrentAct());
            session.RunState.CurrentActIndex = 1;
            var before = StateReceipt(session);
            var second = Ready(EncounterIntelMod.CurrentAct());
            Assert.Equal("ACT.HIVE", second.Id);
            Assert.Equal(1, second.Index);
            Assert.DoesNotContain(second.Elites, row => first.Elites.Any(old => old.Reference.Id == row.Reference.Id));
            Assert.Equal(before, StateReceipt(session));
            session.RunState.CurrentActIndex = session.RunState.Acts.Count;
            AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.NotReady);
            session.RunState.CurrentActIndex = 0;
        });
        AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.NoRun);
        WithRun(9, ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"], _ =>
        {
            var current = Ready(EncounterIntelMod.CurrentAct());
            Assert.Equal("ACT.UNDERDOCKS", current.Id);
            Assert.Equal(9, current.Ascension);
        });
    }

    [GameFact]
    public void ExistingInstanceEntryKeepsTheNativeContextAndLiveValuesApartFromStaticRules()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.NIBBITS_NORMAL", (session, _) =>
        {
            var enemies = CombatManager.Instance.DebugOnlyGetState()!.Enemies;
            var front = enemies.Single(enemy => enemy.SlotName == "front");
            var back = enemies.Single(enemy => enemy.SlotName != "front");
            var opening = Ready(EncounterIntelMod.Enemy(front));
            Assert.Equal("SLICE_MOVE", opening.Instance.AnnouncedMove);
            Assert.Equal("HISS_MOVE", Ready(EncounterIntelMod.Enemy(back)).Instance.AnnouncedMove);
            Assert.Equal(JsonSerializer.Serialize(opening.Static), JsonSerializer.Serialize(Ready(EncounterIntelMod.Enemy(back)).Static));
            Assert.Equal("NIBBIT.moves.SLICE.title", opening.CurrentNative.MoveNames["SLICE_MOVE"].Key);
            PowerCmd.Apply<StrengthPower>(new BlockingPlayerChoiceContext(), front, 3, front, null).GetAwaiter().GetResult();
            Pump.Drain();
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var changed = Ready(EncounterIntelMod.Enemy(front));
            Assert.Equal(9, changed.Instance.Intents.Single(intent => intent.TotalDamage.HasValue).TotalDamage);
            Assert.Equal(3, changed.Instance.Powers.Single(power => power.Id == "POWER.STRENGTH_POWER").Amount);
            Assert.Equal(JsonSerializer.Serialize(opening.Static), JsonSerializer.Serialize(changed.Static));
            var board = Ready(EncounterIntelMod.CurrentBoard());
            Assert.Equal(2, board.Enemies.Count);
            Assert.Equal(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(board.Enemies.Single(enemy => enemy.Instance.Slot == "front")));
            AssertUnavailable(EncounterIntelMod.Enemy(session.RunState.Players[0].Creature), LookupState.NotOnBoard);
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Evidence("lookup-instance-and-reference", changed);
        });
    }

    [GameFact]
    public void InstanceEntryReadsAnEventBoardBeforeCombatAndRejectsTheOldBoardAfterReplacement()
    {
        Creature? previous = null;
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            EnterLanternBoard();
            var synchronizer = (EventCombatSynchronizer)typeof(EventSynchronizer).GetField("_combatSynchronizer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(RunManager.Instance.EventSynchronizer)!;
            var enemy = Assert.Single(synchronizer.CombatStateForLayout!.Enemies);
            previous = enemy;
            var before = StateReceipt(session);
            var layout = Ready(EncounterIntelMod.Enemy(enemy));
            Assert.Equal(BoardKind.EventLayout, layout.BoardKind);
            Assert.Equal("MONSTER.MYSTERIOUS_KNIGHT", layout.Instance.Id);
            Assert.Null(layout.Instance.AnnouncedMove);
            Assert.Empty(layout.Instance.Intents);
            Assert.NotEmpty(layout.Static.Reference.Pattern!.Variants);
            Assert.Equal(before, StateReceipt(session));
            Evidence("lookup-event-layout", layout);
            using var driver = new RunDriver(session);
            var localEvent = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            driver.Apply(HeadlessRuns.Record(0, ActionVerb.ChooseEventOption, ("event_id", localEvent.Id.ToString()),
                ("option_index", "1"), ("option_key", RunDriver.OptionKey(localEvent.CurrentOptions[1]))));
            driver.Apply(HeadlessRuns.Record(1, ActionVerb.ChooseEventOption, ("event_id", localEvent.Id.ToString()),
                ("option_index", "0"), ("option_key", RunDriver.OptionKey(localEvent.CurrentOptions[0]))));
            var active = Ready(EncounterIntelMod.Enemy(enemy));
            Assert.Equal(BoardKind.Combat, active.BoardKind);
            Assert.NotNull(active.Instance.AnnouncedMove);
            Assert.Equal(JsonSerializer.Serialize(layout.Static), JsonSerializer.Serialize(active.Static));
            EnterLanternBoard();
            AssertUnavailable(EncounterIntelMod.Enemy(enemy), LookupState.NotOnBoard);
            Assert.Single(Ready(EncounterIntelMod.CurrentBoard()).Enemies);
        });
        Assert.NotNull(previous);
        AssertUnavailable(EncounterIntelMod.Enemy(previous), LookupState.NoRun);
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], _ =>
        {
            EnterLanternBoard();
            AssertUnavailable(EncounterIntelMod.Enemy(previous), LookupState.NotOnBoard);
            Assert.Single(Ready(EncounterIntelMod.CurrentBoard()).Enemies);
        });
    }

    [GameFact]
    public void BoardEntryRefreshesTheSpawnedRosterAndStopsReadingAnEndedFight()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.PHROG_PARASITE_ELITE", (session, _) =>
        {
            var parent = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies);
            Assert.Equal("MONSTER.PHROG_PARASITE", Ready(EncounterIntelMod.Enemy(parent)).Static.Reference.Id);
            CreatureCmd.Kill(parent).GetAwaiter().GetResult();
            Pump.Drain();
            var before = StateReceipt(session);
            var children = CombatManager.Instance.DebugOnlyGetState()!.Enemies.Where(enemy => enemy.IsAlive).ToArray();
            Assert.Equal(4, children.Length);
            var board = Ready(EncounterIntelMod.CurrentBoard());
            Assert.Equal(4, board.Enemies.Count(enemy => enemy.Instance.Hp > 0));
            Assert.All(children, enemy =>
            {
                var reading = Ready(EncounterIntelMod.Enemy(enemy));
                Assert.Equal("MONSTER.WRIGGLER", reading.Static.Reference.Id);
                Assert.Equal("SPAWNED_MOVE", reading.Instance.AnnouncedMove);
                Assert.Contains(reading.Instance.Intents, intent => intent.Kind == "Stun");
                Assert.NotEmpty(reading.Static.Reference.Pattern!.Variants);
            });
            Assert.Equal(before, StateReceipt(session));
            Evidence("lookup-spawned-board", board);
            foreach (var child in children) CreatureCmd.Kill(child).GetAwaiter().GetResult();
            CombatManager.Instance.CheckWinCondition().GetAwaiter().GetResult();
            Pump.Drain();
            Assert.False(CombatManager.Instance.IsInProgress);
            AssertUnavailable(EncounterIntelMod.Enemy(children[0]), LookupState.NotOnBoard);
            Assert.Equal(BoardKind.None, Ready(EncounterIntelMod.CurrentBoard()).Kind);
            RunManager.Instance.EnterRoomDebug(RoomType.Event, MapPointType.Unknown, ModelDb.Event<BattlewornDummy>(), false).GetAwaiter().GetResult();
            Pump.Drain();
            AssertUnavailable(EncounterIntelMod.Enemy(children[0]), LookupState.NotOnBoard);
            Assert.Equal(BoardKind.None, Ready(EncounterIntelMod.CurrentBoard()).Kind);
        });
    }

    [GameFact]
    public void ACombatLayoutEventWithoutItsBoardIsNotReadyRatherThanASuccessfulEmptyReading()
    {
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            EnterLanternBoard();
            Assert.Single(Ready(EncounterIntelMod.CurrentBoard()).Enemies);
            var synchronizer = (EventCombatSynchronizer)typeof(EventSynchronizer).GetField("_combatSynchronizer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(RunManager.Instance.EventSynchronizer)!;
            var enemy = Assert.Single(synchronizer.CombatStateForLayout!.Enemies);
            synchronizer.ResetState();
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.NotReady);
            AssertUnavailable(EncounterIntelMod.Enemy(enemy), LookupState.NotReady);
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
        });
    }

    [GameFact]
    public void ACombatRoomWithoutItsOpenedBoardIsNotReadyAndNeverReturnsThePreviousRoster()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.SLUDGE_SPINNER_WEAK", (_, _) =>
        {
            var enemy = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies);
            Ready(EncounterIntelMod.Enemy(enemy));
            CombatManager.Instance.Reset(false);
            AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.NotReady);
            AssertUnavailable(EncounterIntelMod.Enemy(enemy), LookupState.NotReady);
        });
    }

    [GameFact]
    public void LookupEntriesRefusePatchedSemanticsAndCallNoCommandsDealsOrRandomness()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.SLUDGE_SPINNER_WEAK", (session, _) =>
        {
            var enemy = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies);
            var harmony = new Harmony("encounter-intel-test.lookup-guard");
            try
            {
                var prefix = new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(RejectGraphCommand), BindingFlags.Static | BindingFlags.NonPublic)!);
                foreach (var method in WatchedGameplayMembers()) harmony.Patch(method, prefix: prefix);
                Assert.Throws<IntelRefusal>(() => MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextInt(1));
                var before = StateReceipt(session);
                var files = ProfileReceipt();
                for (var i = 0; i < 3; i++)
                {
                    Ready(EncounterIntelMod.CurrentAct());
                    Ready(EncounterIntelMod.CurrentBoard());
                    Ready(EncounterIntelMod.Enemy(enemy));
                }
                Assert.Equal(before, StateReceipt(session));
                Assert.Equal(files, ProfileReceipt());
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            try
            {
                harmony.Patch(typeof(Nibbit).GetProperty(nameof(Nibbit.MinInitialHp))!.GetMethod!,
                    postfix: new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(ChangeNumber), BindingFlags.Static | BindingFlags.NonPublic)!));
                AssertUnavailable(EncounterIntelMod.CurrentAct(), LookupState.Refused);
                AssertUnavailable(EncounterIntelMod.CurrentBoard(), LookupState.Refused);
                AssertUnavailable(EncounterIntelMod.Enemy(enemy), LookupState.Refused);
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Ready(EncounterIntelMod.Enemy(enemy));
        });
    }

    private static void EnterLanternBoard()
    {
        RunManager.Instance.EnterRoomDebug(RoomType.Event, MapPointType.Unknown, ModelDb.Event<TheLanternKey>(), false).GetAwaiter().GetResult();
        Pump.Drain();
    }

    private static T Ready<T>(LookupResult<T> result) where T : class
    {
        Assert.True(result.State == LookupState.Ready, result.Reason);
        Assert.Null(result.Reason);
        return Assert.IsType<T>(result.Value);
    }

    private static void AssertUnavailable<T>(LookupResult<T> result, LookupState state) where T : class
    {
        Assert.Equal(state, result.State);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
