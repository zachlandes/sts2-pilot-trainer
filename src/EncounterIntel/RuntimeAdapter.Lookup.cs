using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace EncounterIntel;

public static partial class RuntimeAdapter
{
    // Sample on the game thread at each request; retain no run, board, scene or instance
    public static LookupResult<ActLookup> ReadActLookup() => Lookup(() =>
    {
        var run = LookupRun();
        var inputs = ReferenceInputs.Read();
        var act = run.Act;
        var bosses = SelectedBossesFor(act);
        return new ActLookup(inputs.Patterns.Build, run.AscensionLevel, inputs.Provenance, act.Id.ToString(), run.CurrentActIndex,
            Text(act.Title), act.AllEliteEncounters.Select(encounter => EncounterFor(encounter, inputs, run.AscensionLevel)).ToArray(),
            act.AllBossEncounters.Select(encounter => EncounterFor(encounter, inputs, run.AscensionLevel)).ToArray(),
            bosses.Select(encounter => EncounterFor(encounter, inputs, run.AscensionLevel)).ToArray());
    });

    public static LookupResult<BoardLookup> ReadBoardLookup() => Lookup(() =>
    {
        var run = LookupRun();
        var board = VisibleBoard(run);
        RequireBoardReady(run.CurrentRoom, board.State);
        if (board.State is not { } state)
            return new BoardLookup(SupportedBuild.Require(), run.AscensionLevel, BoardKind.None, null, null, []);
        RequireBoardSemantics(run, state);
        var inputs = ReferenceInputs.Read();
        var targets = state.PlayerCreatures.ToArray();
        return new BoardLookup(inputs.Patterns.Build, run.AscensionLevel, board.Kind, state.Encounter?.Id.ToString(), inputs.Provenance,
            state.Enemies.Select(enemy => EnemyFor(enemy, targets, board.Kind, state, inputs, run.AscensionLevel)).ToArray());
    });

    // A model id or slot cannot distinguish an old creature from its replacement
    public static LookupResult<EnemyLookup> ReadEnemyLookup(Creature enemy) => Lookup(() =>
    {
        ArgumentNullException.ThrowIfNull(enemy);
        var run = LookupRun();
        var board = VisibleBoard(run);
        RequireBoardReady(run.CurrentRoom, board.State);
        if (board.State is not { } state || !state.Enemies.Any(current => ReferenceEquals(current, enemy)))
            throw new IntelUnavailable(LookupState.NotOnBoard, "This enemy instance is no longer on the current board.");
        RequireBoardSemantics(run, state);
        return EnemyFor(enemy, state.PlayerCreatures.ToArray(), board.Kind, state, ReferenceInputs.Read(), run.AscensionLevel);
    });

    private static LookupResult<T> Lookup<T>(Func<T> read) where T : class
    {
        try { return new(read()); }
        catch (IntelUnavailable unavailable) { return new(unavailable.State, unavailable.Message); }
        catch (IntelRefusal refusal) { return new(LookupState.Refused, refusal.Message); }
    }

    private static MegaCrit.Sts2.Core.Runs.RunState LookupRun() =>
        CurrentRun() ?? throw new IntelUnavailable(LookupState.NoRun, "There is no current run to look up.");

    private static void RequireBoardReady(AbstractRoom? room, ICombatState? board)
    {
        if (board is not null) return;
        if (room is CombatRoom { IsPreFinished: false })
            throw new IntelUnavailable(LookupState.NotReady, "The current combat room's board is not open.");
        if (room is EventRoom { IsPreFinished: false } eventRoom &&
            eventRoom.CanonicalEvent.LayoutType == MegaCrit.Sts2.Core.Events.EventLayoutType.Combat)
            throw new IntelUnavailable(LookupState.NotReady, "The current event's combat-layout board is not ready.");
    }

    private static EncounterLookup EncounterFor(EncounterModel model, ReferenceInputs inputs, int ascension)
    {
        RequireNative(model);
        return new(Text(model.Title), model.MapNodeAssetPaths.ToArray(), CatalogueEncounterFor(model, inputs),
            model.AllPossibleMonsters.Select(monster => MonsterFor(monster, inputs, ascension)).ToArray());
    }

    private static MonsterLookup MonsterFor(MonsterModel model, ReferenceInputs inputs, int ascension)
    {
        RequireNative(model);
        // Reference getters must never inherit the current instance's mutable phase or buffs
        var canonical = model.CanonicalInstance;
        RequireNative(canonical);
        var reference = CatalogueMonsterFor(canonical, inputs, ascension);
        return new(NativeFor(canonical, reference), reference);
    }

    private static EnemyLookup EnemyFor(Creature enemy, IReadOnlyList<Creature> targets, BoardKind kind,
        ICombatState state, ReferenceInputs inputs, int ascension)
    {
        var model = enemy.Monster ?? throw new IntelRefusal("A visible enemy has no monster model.");
        var reference = MonsterFor(model, inputs, ascension);
        return new(inputs.Patterns.Build, ascension, inputs.Provenance, kind, state.Encounter?.Id.ToString(), reference,
            NativeFor(model, reference.Reference), ReadEnemy(enemy, targets));
    }

    private static NativeMonster NativeFor(MonsterModel model, CatalogueMonster reference)
    {
        RequireNative(model);
        // AssetPaths includes GetIntents, which constructs a graph; read only the visuals getter
        var visuals = typeof(MonsterModel).GetProperty("VisualsPath", InstanceMembers)?.GetValue(model) as string
            ?? throw new IntelRefusal("The native monster visuals accessor is absent on this build.");
        var names = (reference.Pattern?.Variants.SelectMany(variant => variant.Nodes)
            .Where(node => node.Kind == "move").Select(node => node.Id) ?? [])
            .Concat(AnnouncedMoveId(model) is { } announced ? [announced] : []).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => MoveName(model, id), StringComparer.Ordinal);
        return new(model.Id.ToString(), Text(model.Title), visuals, names);
    }

    private static NativeText MoveName(MonsterModel model, string stateId)
    {
        // The native bestiary strips the suffix before asking this name accessor
        var id = stateId.EndsWith("_MOVE", StringComparison.Ordinal) ? stateId[..^5] : stateId;
        var method = typeof(MonsterModel).GetMethod("GetBestiaryMoveName", InstanceMembers)
            ?? throw new IntelRefusal("The native move-name accessor is absent on this build.");
        return Text(method.Invoke(model, [id]) as LocString
            ?? throw new IntelRefusal("The native move name could not be read."));
    }

    private static NativeText Text(LocString text)
    {
        if (text.IsEmpty || text.Variables.Values.Any(value => value is not decimal))
            throw new IntelRefusal("A native name is empty or requires unrepresented localization variables.");
        return new(text.LocTable, text.LocEntryKey)
        {
            Numbers = text.Variables.ToDictionary(pair => pair.Key, pair => (decimal)pair.Value, StringComparer.Ordinal),
        };
    }
}
