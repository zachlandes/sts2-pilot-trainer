using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace EncounterIntel;

public static class SupportedBuild
{
    public const string AssemblySha256 = "9cb4f1ad8c9f284aa8fec3122ffd6d780bbf543d875c817abdd12ff63fbf12b4";
    private static readonly Lazy<BuildIdentity> Identity = new(ReadIdentity);
    public static BuildIdentity Require() => Identity.Value;

    public static void RequireHash(string sha256)
    {
        if (!string.Equals(sha256, AssemblySha256, StringComparison.OrdinalIgnoreCase))
            throw new IntelRefusal("Encounter Intel supports v0.111.0 / 41cef1ea only; this game assembly is not the inspected build.");
    }

    private static BuildIdentity ReadIdentity()
    {
        var path = typeof(ActModel).Assembly.Location;
        if (string.IsNullOrEmpty(path)) throw new IntelRefusal("The loaded game assembly cannot be identified.");
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        RequireHash(hash);
        return new BuildIdentity("v0.111.0", "2026.08.14", "41cef1ea", hash);
    }
}

public static partial class RuntimeAdapter
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static RunState? CurrentRun()
    {
        SupportedBuild.Require();
        RefuseChangedReadMethods();
        var manager = RunManager.Instance;
        if (!manager.IsInProgress) return null;
        return typeof(RunManager).GetProperty("State", InstanceMembers)?.GetValue(manager) as RunState
            ?? throw new IntelRefusal("RunManager.State could not be read on this build.");
    }

    public static IntelReading ReadCurrent() =>
        Read(CurrentRun() ?? throw new IntelRefusal("There is no current run to look up."));

    private static IntelReading Read(RunState run)
    {
        var build = SupportedBuild.Require();
        var act = run.Act;
        RequireNative(act);

        // The map shows these identities, unlike the normal and elite deal queues
        var selected = new List<string> { act.BossEncounter.Id.ToString() };
        if (act.HasSecondBoss)
        {
            selected.Add((act.SecondBossEncounter ?? throw new IntelRefusal("The current act's second boss is missing.")).Id.ToString());
        }

        var actReading = new ActIntel(act.Id.ToString(), run.CurrentActIndex,
            Definitions(act.AllWeakEncounters), Definitions(act.AllRegularEncounters),
            Definitions(act.AllEliteEncounters), Definitions(act.AllBossEncounters), selected.ToArray());
        var board = VisibleBoard(run);
        if (board.State is not { } state)
            return new IntelReading(build, run.AscensionLevel, actReading, new BoardReading(BoardKind.None, null, []));
        if (run.IterateHookListeners(state).Any(model => model.GetType().Assembly != typeof(ActModel).Assembly))
            throw new IntelRefusal("An unrecognized model can change intent values; this board is unsupported.");
        var targets = state.PlayerCreatures.ToArray();
        var enemies = state.Enemies.Select(enemy => ReadEnemy(enemy, targets)).ToArray();
        return new IntelReading(build, run.AscensionLevel, actReading,
            new BoardReading(board.Kind, state.Encounter?.Id.ToString(), enemies));
    }

    private static IReadOnlyList<EncounterDefinition> Definitions(IEnumerable<EncounterModel> pool) =>
        pool.Select(encounter =>
        {
            RequireNative(encounter);
            return new EncounterDefinition(encounter.Id.ToString(), encounter.AllPossibleMonsters.Select(monster =>
            {
                RequireNative(monster);
                return monster.Id.ToString();
            }).ToArray());
        }).ToArray();

    private static (BoardKind Kind, ICombatState? State) VisibleBoard(RunState run)
    {
        if (CombatManager.Instance.IsInProgress && run.CurrentRoom is CombatRoom room)
            return (BoardKind.Combat, room.CombatState);
        if (run.CurrentRoom is not EventRoom) return (BoardKind.None, null);
        var events = RunManager.Instance.EventSynchronizer;
        if (events is null) return (BoardKind.None, null);
        // This is the already-drawn event board, not a hypothetical encounter factory
        var field = typeof(EventSynchronizer).GetField("_combatSynchronizer", InstanceMembers)
            ?? throw new IntelRefusal("The event combat-layout accessor is absent on this build.");
        var synchronizer = field.GetValue(events) as EventCombatSynchronizer
            ?? throw new IntelRefusal("The current event's combat-layout synchronizer could not be read.");
        return synchronizer.CombatStateForLayout is { } layout
            ? (BoardKind.EventLayout, layout) : (BoardKind.None, null);
    }

    private static EnemyReading ReadEnemy(Creature enemy, IReadOnlyList<Creature> targets)
    {
        var monster = enemy.Monster ?? throw new IntelRefusal("A visible enemy has no monster model.");
        RequireNative(monster);
        var move = monster.NextMove;
        var intents = move.Intents.Select(intent =>
        {
            if (intent.GetType().Assembly != typeof(ActModel).Assembly)
                throw new IntelRefusal("An unrecognized intent is present on the board.");
            if (intent is not AttackIntent attack)
                return new IntentReading(intent.IntentType.ToString(), null, null);
            if (attack.DamageCalc?.Method.DeclaringType?.Assembly != typeof(ActModel).Assembly)
                throw new IntelRefusal("An unrecognized damage calculation is present on the board.");
            return new IntentReading(intent.IntentType.ToString(), attack.GetTotalDamage(targets, enemy), attack.Repeats);
        }).ToArray();
        return new EnemyReading(monster.Id.ToString(), enemy.SlotName, enemy.CurrentHp, enemy.MaxHp,
            enemy.Block, enemy.Powers.Select(power => new PowerReading(power.Id.ToString(), power.Amount)).ToArray(),
            string.IsNullOrEmpty(move.StateId) ? null : move.StateId, intents);
    }

    internal static void RequireNative(AbstractModel model)
    {
        if (model.GetType().Assembly != typeof(ActModel).Assembly)
            throw new IntelRefusal($"Model {model.Id} is not part of the inspected game build.");
    }

    internal static void RefuseChangedReadMethods()
    {
        // Patched commands used by the headless test harness are not lookup members
        var patched = Harmony.GetAllPatchedMethods().FirstOrDefault(method =>
            Harmony.GetPatchInfo(method)?.Owners.Count > 0 &&
            method.DeclaringType?.Assembly == typeof(ActModel).Assembly &&
            (((method.DeclaringType == typeof(RunManager) || method.DeclaringType == typeof(RunState) ||
               method.DeclaringType == typeof(Creature) || method.DeclaringType == typeof(CombatState) ||
               method.DeclaringType == typeof(CombatManager) || method.DeclaringType == typeof(CombatRoom) ||
               method.DeclaringType == typeof(EventCombatSynchronizer)) && method.Name.StartsWith("get_", StringComparison.Ordinal)) ||
             (method.DeclaringType == typeof(RoomSet) && method.Name is "get_Boss" or "get_SecondBoss" or "get_HasSecondBoss") ||
             method.DeclaringType.FullName is "MegaCrit.Sts2.Core.Models.Monsters.Nibbit" or
                "MegaCrit.Sts2.Core.Models.Monsters.PhrogParasite" or "MegaCrit.Sts2.Core.Models.Monsters.Wriggler" or
                "MegaCrit.Sts2.Core.Models.Monsters.SludgeSpinner" or "MegaCrit.Sts2.Core.Models.Powers.InfestedPower" ||
             method.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.MonsterMoves", StringComparison.Ordinal) == true ||
             (method.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true &&
              (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name == "GenerateAllEncounters" ||
               method.Name.Contains("ModifyDamage", StringComparison.Ordinal))) ||
             method.DeclaringType.Namespace == "MegaCrit.Sts2.Core.Hooks" ||
             method.DeclaringType.FullName == "MegaCrit.Sts2.Core.Helpers.AscensionHelper"));
        if (patched is not null)
            throw new IntelRefusal($"Lookup semantics are unsupported: {patched.DeclaringType?.Name}.{patched.Name} has Harmony patches.");
    }
}
