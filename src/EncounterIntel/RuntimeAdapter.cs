using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
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

    private sealed record Registry(int Schema, IReadOnlyList<BuildIdentity> Builds);

    public static BuildIdentity IdentifyHash(string sha256)
    {
        using var stream = typeof(SupportedBuild).Assembly.GetManifestResourceStream("EncounterIntel.builds.json")
            ?? throw new IntelRefusal("The inspected-build registry is absent.");
        var registry = JsonSerializer.Deserialize<Registry>(stream, PatternJson.Options)
            ?? throw new IntelRefusal("The inspected-build registry is unreadable.");
        if (registry.Schema != 1 || registry.Builds.Select(build => build.AssemblySha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() != registry.Builds.Count)
            throw new IntelRefusal("The inspected-build registry has an unknown schema or duplicate identity.");
        return registry.Builds.SingleOrDefault(build => string.Equals(build.AssemblySha256, sha256, StringComparison.OrdinalIgnoreCase))
            ?? throw new IntelRefusal("This game assembly has no inspected Encounter Intel adapter; beta and retail builds require their own identity and evidence.");
    }

    public static void RequireHash(string sha256) => _ = IdentifyHash(sha256);

    private static BuildIdentity ReadIdentity()
    {
        var path = typeof(ActModel).Assembly.Location;
        if (string.IsNullOrEmpty(path)) throw new IntelRefusal("The loaded game assembly cannot be identified.");
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return IdentifyHash(hash);
    }
}

public static partial class RuntimeAdapter
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static RunState? CurrentRun()
    {
        SupportedBuild.Require();
        RefuseChangedReadMethods();
        if (!ModelDb.All.Any())
            throw new IntelUnavailable(LookupState.NotReady, "The game's model database is not ready for lookup.");
        var manager = RunManager.Instance;
        if (!manager.IsInProgress) return null;
        var run = typeof(RunManager).GetProperty("State", InstanceMembers)?.GetValue(manager) as RunState
            ?? throw new IntelRefusal("RunManager.State could not be read on this build.");
        if (run.CurrentActIndex < 0 || run.CurrentActIndex >= run.Acts.Count)
            throw new IntelUnavailable(LookupState.NotReady, "The current act is not available yet.");
        RequireNative(run.Act);
        // RoomSet.Boss refuses until GenerateRooms has completed; never generate it for lookup
        try { _ = run.Act.BossEncounter; }
        catch (InvalidOperationException error) when (error.Message == "RoomSet.Boss not set! You must call GenerateRooms")
        {
            throw new IntelUnavailable(LookupState.NotReady, "The current act's public bosses are not ready yet.");
        }
        return run;
    }

    public static IntelReading ReadCurrent() =>
        Read(CurrentRun() ?? throw new IntelRefusal("There is no current run to look up."));

    private static IntelReading Read(RunState run)
    {
        var build = SupportedBuild.Require();
        var act = run.Act;
        RequireNative(act);

        var selected = SelectedBossesFor(act).Select(boss => boss.Id.ToString()).ToArray();

        var actReading = new ActIntel(act.Id.ToString(), run.CurrentActIndex,
            Definitions(act.AllWeakEncounters), Definitions(act.AllRegularEncounters),
            Definitions(act.AllEliteEncounters), Definitions(act.AllBossEncounters), selected);
        var board = VisibleBoard(run);
        if (board.State is not { } state)
            return new IntelReading(build, run.AscensionLevel, actReading, new BoardReading(BoardKind.None, null, []));
        RequireBoardSemantics(run, state);
        var targets = state.PlayerCreatures.ToArray();
        var enemies = state.Enemies.Select(enemy => ReadEnemy(enemy, targets)).ToArray();
        return new IntelReading(build, run.AscensionLevel, actReading,
            new BoardReading(board.Kind, state.Encounter?.Id.ToString(), enemies));
    }

    private static IReadOnlyList<EncounterModel> SelectedBossesFor(ActModel act)
    {
        // The map shows these identities, unlike the normal and elite deal queues
        var selected = new List<EncounterModel> { act.BossEncounter };
        if (act.HasSecondBoss)
            selected.Add(act.SecondBossEncounter ?? throw new IntelRefusal("The current act's second boss is missing."));
        return selected;
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
        if (run.CurrentRoom is not EventRoom eventRoom || eventRoom.IsPreFinished) return (BoardKind.None, null);
        RequireNative(eventRoom.CanonicalEvent);
        if (eventRoom.CanonicalEvent.LayoutType != MegaCrit.Sts2.Core.Events.EventLayoutType.Combat)
            return (BoardKind.None, null);
        var events = RunManager.Instance.EventSynchronizer;
        if (events is null) return (BoardKind.None, null);
        // This is the already-drawn event board, not a hypothetical encounter factory
        var field = typeof(EventSynchronizer).GetField("_combatSynchronizer", InstanceMembers)
            ?? throw new IntelRefusal("The event combat-layout accessor is absent on this build.");
        var value = field.GetValue(events);
        if (value is null) return (BoardKind.None, null);
        var synchronizer = value as EventCombatSynchronizer
            ?? throw new IntelRefusal("The current event's combat-layout synchronizer could not be read.");
        return synchronizer.CombatStateForLayout is { } layout
            ? (BoardKind.EventLayout, layout) : (BoardKind.None, null);
    }

    private static void RequireBoardSemantics(RunState run, ICombatState state)
    {
        if (state.Encounter is { } encounter) RequireNative(encounter);
        if (run.IterateHookListeners(state).Any(model => model.GetType().Assembly != typeof(ActModel).Assembly))
            throw new IntelRefusal("An unrecognized model can change intent values; this board is unsupported.");
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
            AnnouncedMoveId(monster), intents);
    }

    private static string? AnnouncedMoveId(MonsterModel model) =>
        // The event layout's default MoveState is a sentinel, not an assigned intent
        string.IsNullOrEmpty(model.NextMove.StateId) || model.NextMove.StateId == "UNSET_MOVE" ? null : model.NextMove.StateId;

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
               method.DeclaringType == typeof(EventCombatSynchronizer) || method.DeclaringType == typeof(ModelDb)) && method.Name.StartsWith("get_", StringComparison.Ordinal)) ||
             (method.DeclaringType == typeof(RoomSet) && method.Name is "get_Boss" or "get_SecondBoss" or "get_HasSecondBoss") ||
             method.DeclaringType.FullName is "MegaCrit.Sts2.Core.Models.Monsters.Nibbit" or
                "MegaCrit.Sts2.Core.Models.Monsters.PhrogParasite" or "MegaCrit.Sts2.Core.Models.Monsters.Wriggler" or
                "MegaCrit.Sts2.Core.Models.Monsters.SludgeSpinner" or "MegaCrit.Sts2.Core.Models.Powers.InfestedPower" ||
             method.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.MonsterMoves", StringComparison.Ordinal) == true ||
             (method.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true &&
              (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name == "GenerateAllEncounters" ||
               method.Name.Contains("ModifyDamage", StringComparison.Ordinal) || method.Name is "GenerateMoveStateMachine" or "GetBestiaryMoveName")) ||
             method.DeclaringType.Namespace == "MegaCrit.Sts2.Core.Hooks" ||
             method.DeclaringType.FullName == "MegaCrit.Sts2.Core.Helpers.AscensionHelper"));
        if (patched is not null)
            throw new IntelRefusal($"Lookup semantics are unsupported: {patched.DeclaringType?.Name}.{patched.Name} has Harmony patches.");
    }
}
