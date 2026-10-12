using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
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
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed partial class EncounterIntelTests
{
    [Fact]
    public void WikiCatalogueKeepsPinnedSourcesAndAttribution()
    {
        var wiki = WikiCatalogue.Read();
        Assert.Equal("CC BY-SA 4.0", wiki.Attribution.License);
        Assert.Equal(8, wiki.Sources.Count);
        Assert.All(wiki.Sources, source =>
        {
            Assert.True(source.Revision > 0);
            Assert.StartsWith("https://slaythespire.wiki.gg/wiki/Module:Enemies/", source.Url);
            Assert.Equal(64, source.Sha256.Length);
        });
        Assert.Equal(50310, wiki.Sources.Single(source => source.Module.EndsWith("/Overgrowth", StringComparison.Ordinal)).Revision);
        var monsters = ReviewedExamples.Monsters(wiki);
        Assert.Equal(4, monsters.Count);
        var nibbit = monsters.Single(rule => rule.Id == "MONSTER.NIBBIT");
        Assert.Equal(5, nibbit.Moves.Single(move => move.StateId == "SLICE_MOVE").Effects.Single(effect => effect.Kind == EffectKind.Block).Amount.At(7));
        Assert.Equal(6, nibbit.Moves.Single(move => move.StateId == "SLICE_MOVE").Effects.Single(effect => effect.Kind == EffectKind.Block).Amount.At(8));
        Assert.Equal(2, nibbit.Moves.Single(move => move.StateId == "HISS_MOVE").Effects.Single().Amount.At(8));
        Assert.Equal(3, nibbit.Moves.Single(move => move.StateId == "HISS_MOVE").Effects.Single().Amount.At(9));
        Assert.Contains(monsters.Single(rule => rule.Id == "MONSTER.SLUDGE_SPINNER").Moves, move => move.StateId == "RAGE_MOVE");
        Assert.Equal(2, monsters.Single(rule => rule.Id == "MONSTER.PHROG_PARASITE").Transitions.Count);
        var phase = Assert.Single(ReviewedExamples.Encounters().Single(rule => rule.Id == "ENCOUNTER.PHROG_PARASITE_ELITE").LaterPhases);
        Assert.True(phase.PreventsCombatEnd);
        Assert.True(Assert.Single(phase.Roster).StartStunned);
        Assert.Equal(4, phase.Roster[0].Count);
    }

    [Fact]
    public void ChangedBuildAndUnrecognizedWikiEffectsRefuse()
    {
        SupportedBuild.RequireHash(SupportedBuild.AssemblySha256);
        Assert.Throws<IntelRefusal>(() => SupportedBuild.RequireHash(new string('0', 64)));
        var wiki = WikiCatalogue.Read();
        var nibbit = wiki.Enemy("Nibbit");
        var changed = nibbit with { Moves = nibbit.Moves.Select(move => move.Name == "Hiss" ? move with { Text = move.Text + " Heals 9 HP." } : move).ToArray() };
        Assert.Throws<IntelRefusal>(() => ReviewedExamples.Monsters(wiki with
        {
            Enemies = wiki.Enemies.Select(enemy => enemy.Name == changed.Name ? changed : enemy).ToArray(),
        }));
    }

    [GameTheory]
    [InlineData("ACT.OVERGROWTH", "ENCOUNTER.PHROG_PARASITE_ELITE", "ENCOUNTER.BYGONE_EFFIGY_ELITE")]
    [InlineData("ACT.UNDERDOCKS", "ENCOUNTER.TERROR_EEL_ELITE", "ENCOUNTER.PHANTASMAL_GARDENERS_ELITE")]
    public void ActualActVariantAndPoolsAreReadWithoutChangingState(string variant, string elite, string otherElite)
    {
        WithRun(0, [variant, "ACT.HIVE", "ACT.GLORY"], session =>
        {
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var reading = RuntimeAdapter.ReadCurrent();
            Assert.Equal(variant, reading.Act.Id);
            Assert.Equal(0, reading.Act.Index);
            Assert.Contains(reading.Act.Elites, item => item.Id == elite);
            Assert.Contains(reading.Act.Elites, item => item.Id == otherElite);
            Assert.Equal(3, reading.Act.Elites.Count);
            Assert.NotEmpty(reading.Act.Weak);
            Assert.NotEmpty(reading.Act.Regular);
            Assert.Equal(3, reading.Act.BossPool.Count);
            Assert.Single(reading.Act.SelectedBosses);
            Assert.Equal(BoardKind.None, reading.Board.Kind);
            for (var i = 0; i < 10; i++) _ = RuntimeAdapter.ReadCurrent();
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Evidence(variant, reading);
        });
    }

    [GameTheory]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    public void OnlyTheCurrentActsPublicBossesAreReturned(int ascension, int bosses)
    {
        WithRun(ascension, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            Assert.Single(RuntimeAdapter.ReadCurrent().Act.SelectedBosses);
            session.RunState.CurrentActIndex = 2;
            var before = StateReceipt(session);
            var reading = RuntimeAdapter.ReadCurrent();
            Assert.Equal("ACT.GLORY", reading.Act.Id);
            Assert.Equal(bosses, reading.Act.SelectedBosses.Count);
            Assert.Equal(bosses, reading.Act.SelectedBosses.Distinct().Count());
            Assert.Equal(session.RunState.Act.BossEncounter.Id.ToString(), reading.Act.SelectedBosses[0]);
            if (bosses == 2) Assert.Equal(session.RunState.Act.SecondBossEncounter!.Id.ToString(), reading.Act.SelectedBosses[1]);
            Assert.Equal(before, StateReceipt(session));
            Evidence($"bosses-a{ascension}", reading);
        });
    }

    [GameTheory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void WikiQuantitiesAreCheckedAgainstTheRunningGamesGetters(int ascension)
    {
        WithRun(ascension, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var examples = RuntimeAdapter.ReadWorkedExamples();
            Assert.All(examples, monster => Assert.False(monster.Mismatch));
            Assert.Contains(examples.SelectMany(monster => monster.Quantities), value => value.Authority == QuantityAuthority.LiveGetter);
            Assert.Contains(examples.SelectMany(monster => monster.Quantities), value => value.Authority == QuantityAuthority.GameConstant);
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Evidence($"examples-a{ascension}", examples);
        });
    }

    [GameFact]
    public void ARecognizedWikiNumberThatDisagreesWithTheGameHasAVisibleMismatch()
    {
        WithRun(9, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], _ =>
        {
            var wiki = WikiCatalogue.Read();
            var changed = wiki with { Enemies = wiki.Enemies.Select(enemy => enemy.Name == "Nibbit" ? enemy with { AscHp = "99-100" } : enemy).ToArray() };
            var nibbit = RuntimeAdapter.ReadWorkedExamples(changed).Single(monster => monster.Rule.Id == "MONSTER.NIBBIT");
            Assert.True(nibbit.Mismatch);
            Assert.Equal(99, nibbit.Quantities.Single(value => value.Field == "min_hp").Wiki);
            Assert.Equal(44, nibbit.Quantities.Single(value => value.Field == "min_hp").Game);
            Evidence("synthetic-wiki-mismatch", new { Note = "Negative control: edited in-memory wiki HP range", Reading = nibbit });
        });
    }

    [GameFact]
    public void ExistingCombatRosterAndAnnouncedIntentsDoNotAdvanceTheFight()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.SLUDGE_SPINNER_WEAK", (session, _) =>
        {
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var reading = RuntimeAdapter.ReadCurrent();
            Assert.Equal(BoardKind.Combat, reading.Board.Kind);
            var spinner = Assert.Single(reading.Board.Enemies);
            Assert.Equal("MONSTER.SLUDGE_SPINNER", spinner.Id);
            Assert.Equal("OIL_SPRAY_MOVE", spinner.AnnouncedMove);
            Assert.Contains(spinner.Intents, intent => intent.Kind == "Attack" && intent.TotalDamage == 8);
            var graph = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies).Monster!.MoveStateMachine!;
            Assert.Contains("RAGE_MOVE", graph.States.Keys);
            var random = Assert.IsType<RandomBranchState>(graph.States["RAND"]);
            Assert.Equal(3, random.States.Count);
            Assert.All(random.States, branch =>
            {
                Assert.Equal(MoveRepeatType.CannotRepeat, branch.repeatType);
                Assert.Equal(1f, branch.GetWeight());
            });
            for (var i = 0; i < 10; i++) RuntimeAdapter.ReadCurrent();
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Evidence("announced-spinner", reading);
        });
    }

    [GameFact]
    public void AnnouncedAttackUsesTheGamesStrengthAndWeakHooksWithoutChangingThem()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.SLUDGE_SPINNER_WEAK", (session, _) =>
        {
            var enemy = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies);
            PowerCmd.Apply<StrengthPower>(new BlockingPlayerChoiceContext(), enemy, 3, enemy, null).GetAwaiter().GetResult();
            Pump.Drain();
            var before = StateReceipt(session);
            Assert.Equal(11, RuntimeAdapter.ReadCurrent().Board.Enemies.Single().Intents.Single(intent => intent.Kind == "Attack").TotalDamage);
            Assert.Equal(before, StateReceipt(session));
            PowerCmd.Apply<WeakPower>(new BlockingPlayerChoiceContext(), enemy, 1, enemy, null).GetAwaiter().GetResult();
            Pump.Drain();
            before = StateReceipt(session);
            var reading = RuntimeAdapter.ReadCurrent();
            Assert.Equal(8, reading.Board.Enemies.Single().Intents.Single(intent => intent.Kind == "Attack").TotalDamage);
            Assert.Equal(before, StateReceipt(session));
            Evidence("modified-announced-intent", reading);
        });
    }

    [GameFact]
    public void ThePhrogDeathHookReallyProducesFourStunnedWrigglers()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.PHROG_PARASITE_ELITE", (session, driver) =>
        {
            var opening = RuntimeAdapter.ReadCurrent();
            var parent = Assert.Single(opening.Board.Enemies);
            Assert.Equal("MONSTER.PHROG_PARASITE", parent.Id);
            Assert.Equal(4, parent.Powers.Single(power => power.Id == "POWER.INFESTED_POWER").Amount);
            var phrog = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies);
            CreatureCmd.Kill(phrog).GetAwaiter().GetResult();
            Pump.Drain();
            var before = StateReceipt(session);
            var spawned = RuntimeAdapter.ReadCurrent();
            var children = spawned.Board.Enemies.Where(enemy => enemy.Hp > 0).ToArray();
            Assert.Equal(4, children.Length);
            Assert.All(children, child =>
            {
                Assert.Equal("MONSTER.WRIGGLER", child.Id);
                Assert.Equal("SPAWNED_MOVE", child.AnnouncedMove);
                Assert.Contains(child.Intents, intent => intent.Kind == "Stun");
            });
            Assert.Equal(new[] { "wriggler1", "wriggler2", "wriggler3", "wriggler4" }, children.Select(child => child.Slot).Order().ToArray());
            Assert.Equal(before, StateReceipt(session));
            Evidence("phrog-spawn", spawned);
            driver.Apply(HeadlessRuns.Record(20, ActionVerb.EndTurn));
            var afterStun = RuntimeAdapter.ReadCurrent().Board.Enemies.Where(enemy => enemy.Hp > 0).ToArray();
            Assert.All(afterStun, child => Assert.Equal(child.Slot is "wriggler1" or "wriggler3" ? "NASTY_BITE_MOVE" : "WRIGGLE_MOVE", child.AnnouncedMove));
        });
    }

    [GameFact]
    public void AnAlreadyDisplayedEventBoardIsReadBeforeCombatBegins()
    {
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            RunManager.Instance.EnterRoomDebug(RoomType.Event, MapPointType.Unknown, ModelDb.Event<TheLanternKey>(), false)
                .GetAwaiter().GetResult();
            Pump.Drain();
            Assert.False(CombatManager.Instance.IsInProgress);
            var before = StateReceipt(session);
            var reading = RuntimeAdapter.ReadCurrent();
            Assert.Equal(BoardKind.EventLayout, reading.Board.Kind);
            Assert.Equal("MONSTER.MYSTERIOUS_KNIGHT", Assert.Single(reading.Board.Enemies).Id);
            Assert.Equal(before, StateReceipt(session));
            Evidence("event-board", reading);
        });
    }

    [GameFact]
    public void LookupAssemblyHasNoRunmobileDependencies()
    {
        var assembly = typeof(RuntimeAdapter).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("Sts2PilotTrainer", StringComparison.Ordinal) == true || reference.Name == "Runmobile");
    }

    [GameFact]
    public void LookupsCallNoWatchedCommandRandomStreamOrDeal()
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", "ENCOUNTER.SLUDGE_SPINNER_WEAK", (session, _) =>
        {
            var harmony = new Harmony("encounter-intel-test.forbidden-calls");
            var prefix = new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(RecordForbiddenCall), BindingFlags.Static | BindingFlags.NonPublic)!);
            try
            {
                foreach (var method in WatchedGameplayMembers()) harmony.Patch(method, prefix: prefix);
                var before = StateReceipt(session);
                Assert.Empty(Watching(() =>
                {
                    Assert.Equal(BoardKind.Combat, RuntimeAdapter.ReadCurrent().Board.Kind);
                    Assert.NotEmpty(RuntimeAdapter.ReadWorkedExamples());
                    Assert.NotEmpty(RuntimeAdapter.ReadCatalogue().Coverage);
                }));
                Assert.Equal(before, StateReceipt(session));
                var spinner = Assert.Single(CombatManager.Instance.DebugOnlyGetState()!.Enemies);
                Assert.Contains(Watching(() => spinner.Monster!.RollMove(CombatManager.Instance.DebugOnlyGetState()!.PlayerCreatures)),
                    call => call.Name == nameof(MonsterModel.RollMove));
            }
            finally { harmony.UnpatchAll(harmony.Id); }
        });
    }
    [GameFact]
    public void APatchedReadGetterIsRefusedRatherThanStatingVanillaFacts()
    {
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], _ =>
        {
            var harmony = new Harmony("encounter-intel-test.read-guard");
            var getter = typeof(Nibbit).GetProperty(nameof(Nibbit.MinInitialHp))!.GetMethod!;
            try
            {
                harmony.Patch(getter, postfix: new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(ChangeNumber), BindingFlags.Static | BindingFlags.NonPublic)!));
                Assert.Throws<IntelRefusal>(() => RuntimeAdapter.ReadWorkedExamples());
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Assert.NotEmpty(RuntimeAdapter.ReadWorkedExamples());
        });
    }

    [GameTheory]
    [InlineData("ENCOUNTER.NIBBITS_WEAK", 1)]
    [InlineData("ENCOUNTER.NIBBITS_NORMAL", 2)]
    public void NibbitOpeningsAndAllCycleMovesMatchTheEngine(string encounter, int count)
    {
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", encounter, (session, _) =>
        {
            var before = StateReceipt(session);
            var enemies = RuntimeAdapter.ReadCurrent().Board.Enemies;
            Assert.Equal(count, enemies.Count);
            Assert.All(enemies, enemy => Assert.Equal(count == 1 ? "BUTT_MOVE" : enemy.Slot == "front" ? "SLICE_MOVE" : "HISS_MOVE", enemy.AnnouncedMove));
            foreach (var enemy in CombatManager.Instance.DebugOnlyGetState()!.Enemies)
            {
                var graph = enemy.Monster!.MoveStateMachine!;
                foreach (var edge in ReviewedExamples.Monsters(WikiCatalogue.Read()).Single(rule => rule.Id == "MONSTER.NIBBIT").Transitions)
                    Assert.Equal(edge.To, Assert.IsType<MoveState>(graph.States[edge.From]).FollowUpState!.Id);
            }
            Assert.Equal(before, StateReceipt(session));
        });
    }

    [GameFact]
    public void IndependentModTypesLoadWithRunmobileSiblingsUnreachable()
    {
        _ = ChoiceEntryPoints.Game;
        var context = new WithoutRunmobile();
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(RuntimeAdapter).Assembly.Location);
            Assert.NotEmpty(assembly.GetTypes());
        }
        finally { context.Unload(); }
    }

    private sealed class WithoutRunmobile() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name?.StartsWith("Sts2PilotTrainer", StringComparison.Ordinal) == true || name.Name == "Runmobile"
            ? throw new FileNotFoundException("Runmobile siblings deliberately unavailable") : null;
    }

    private static void ChangeNumber(ref int __result) => __result += 1;
    private static List<MethodBase>? ForbiddenCalls;
    private static void RecordForbiddenCall(MethodBase __originalMethod) => ForbiddenCalls?.Add(__originalMethod);

    private static IReadOnlyList<MethodBase> Watching(Action body)
    {
        ForbiddenCalls = [];
        try
        {
            body();
            return ForbiddenCalls;
        }
        finally { ForbiddenCalls = null; }
    }

    private static IReadOnlyList<MethodBase> WatchedGameplayMembers()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var commands = ChoiceEntryPoints.LoadedGameTypes()
            .Where(type => type is { IsNested: false, ContainsGenericParameters: false, Namespace: "MegaCrit.Sts2.Core.Commands" })
            .SelectMany(type => type.GetMethods(flags))
            .Where(method => !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() is not null);
        var streams = typeof(MegaCrit.Sts2.Core.Random.Rng).GetMethods(flags)
            .Where(method => method.Name.StartsWith("Next", StringComparison.Ordinal) && !method.ContainsGenericParameters);
        var deals = new[]
        {
            typeof(ActModel).GetMethod(nameof(ActModel.GenerateRooms))!,
            typeof(ActModel).GetMethod(nameof(ActModel.PullNextEncounter))!,
            typeof(ActModel).GetMethod(nameof(ActModel.PullNextEvent))!,
            typeof(EncounterModel).GetMethod(nameof(EncounterModel.GenerateMonstersWithSlots))!,
            typeof(MonsterModel).GetMethod(nameof(MonsterModel.RollMove))!,
        };
        var watched = commands.Concat(streams).Cast<MethodBase>().Concat(deals).ToArray();
        Assert.Contains(watched, method => method.DeclaringType == typeof(CreatureCmd));
        Assert.Contains(watched, method => method.DeclaringType == typeof(MegaCrit.Sts2.Core.Random.Rng));
        return watched;
    }

    private static void WithRun(int ascension, string[] acts, Action<GameSession> body)
    {
        HeadlessRuns.EndAnyRun();
        try
        {
            var session = new GameSession();
            session.StartRun("P1L0TTRA1NER", "CHARACTER.IRONCLAD", ascension, "standard", acts);
            body(session);
        }
        finally
        {
            HeadlessRuns.EndAnyRun();
            HeadlessEngine.Forget();
        }
    }

    private static string StateReceipt(GameSession session) => CanonicalStateProjection.Project(session.RunState).Digest();
    private static string ProfileReceipt() => string.Join("\n", Directory.EnumerateFiles(Godot.HeadlessSandbox.Root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => $"{path}:{File.GetLastWriteTimeUtc(path).Ticks}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}"));

    private static void Evidence(string name, object reading)
    {
        if (Environment.GetEnvironmentVariable("ENCOUNTER_INTEL_DEMO") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(reading, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) + "\n");
    }
}
