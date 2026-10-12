using System.Collections;
using System.Reflection;
using System.Text.Json;
using EncounterIntel;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

internal static class EncounterIntelPatternGenerator
{
    internal sealed record Input(BuildIdentity Build, IReadOnlyDictionary<string, string[]> Conditions,
        IReadOnlyDictionary<string, Context[]> Contexts, IReadOnlyDictionary<string, Dictionary<string, string>> Strength,
        IReadOnlyDictionary<string, PatternPhase[]> Phases, IReadOnlyDictionary<string, string[]> WikiMoves,
        IReadOnlyDictionary<string, DynamicAttack> DynamicAttacks, IReadOnlyDictionary<string, string> WeightRules)
    {
        public IReadOnlyDictionary<string, GroupSummonCapBinding> GroupSummonCaps { get; init; } = new Dictionary<string, GroupSummonCapBinding>();
    }
    internal sealed record GroupSummonCapBinding(string Guard, string Counter, string Comparison, string SharedBy);
    internal sealed record DynamicAttack(string? DamageProperty, string? HitsProperty, int? Hits, string? Rule);
    internal sealed record Context(string Name, string[] Encounters, string? Slot, string? Opening,
        Dictionary<string, JsonElement> Set);

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int Ascension;

    internal static PatternAtlas Generate(Input input)
    {
        var identity = SupportedBuild.Require();
        if (input.Build != identity) throw new IntelRefusal("Pattern semantics belong to another game build.");
        Assert.False(RunManager.Instance.IsInProgress);
        var models = ModelDb.All.OfType<MonsterModel>().OrderBy(model => model.Id.ToString(), StringComparer.Ordinal).ToArray();
        var harmony = new Harmony("encounter-intel-pattern-generator.ascension");
        var columns = new List<PatternColumn>();
        var maximum = (int)typeof(AscensionManager).GetField("maxAscensionAllowed")!.GetRawConstantValue()!;
        try
        {
            harmony.Patch(typeof(RunManager).GetMethod(nameof(RunManager.HasAscension))!, prefix: new HarmonyMethod(typeof(EncounterIntelPatternGenerator).GetMethod(nameof(ReadAscension), BindingFlags.Static | BindingFlags.NonPublic)!));
            string? previous = null;
            for (Ascension = 0; Ascension <= maximum; Ascension++)
            {
                var enemies = models.Select(model => Capture(model, input)).ToArray();
                var signature = JsonSerializer.Serialize(enemies, PatternJson.Options);
                if (signature != previous) columns.Add(new(Ascension, enemies));
                previous = signature;
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Assert.False(RunManager.Instance.IsInProgress);
        return new(1, identity, "move-graph-v1", columns,
            ["Enemy-page pattern cross-check is a separate cached-source reading, not established by graph derivation.",
             "Schedules are reference damage before powers, allied modifiers and player interference; only inspected self-Strength changes are accumulated.",
             "No predicate, random branch, move body, spawn hook or encounter roster factory was executed."])
        {
            ReactiveCoverage = ReadReactiveCoverage(input),
            MaximumAscension = maximum,
        };
    }

    private static bool ReadAscension(AscensionLevel level, ref bool __result)
    {
        __result = Ascension >= (int)level;
        return false;
    }

    private static EnemyPattern Capture(MonsterModel canonical, Input input)
    {
        var type = canonical.GetType();
        var name = type.Name;
        var gaps = new List<string>();
        if (type.Namespace?.Contains("Mocks", StringComparison.Ordinal) == true || name is "DeprecatedMonster" or "BigDummy" or "Architect")
            gaps.Add("Registered debug, deprecated or non-combat definition; retained in the construction denominator.");
        var contexts = input.Contexts.GetValueOrDefault(name) ?? [];
        var variants = new List<PatternVariant>();
        var constructs = false;
        foreach (var context in new[] { new Context("Unconfigured definition (not an encounter opening)", [], null, null, []) }.Concat(contexts))
        {
            try
            {
                var monster = canonical.ToMutable();
                foreach (var (property, value) in context.Set)
                {
                    var member = type.GetProperty(property, Members) ?? throw new IntelRefusal($"Missing context setter {name}.{property}.");
                    member.SetValue(monster, JsonSerializer.Deserialize(value.GetRawText(), member.PropertyType));
                }
                // Generate definitions only; never attach a creature, roll a move or run a body
                monster.SetUpForCombat();
                var graph = monster.MoveStateMachine ?? throw new IntelRefusal("No state machine constructed.");
                var initial = (MonsterState)typeof(MonsterMoveStateMachine).GetField("_initialState", Members)!.GetValue(graph)!;
                var states = graph.States.Values.Append(initial).Distinct().ToArray();
                var nodes = states.Select(state => CaptureNode(state, monster, input)).OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
                var reachable = new HashSet<string>(StringComparer.Ordinal);
                void Visit(string id)
                {
                    if (!reachable.Add(id)) return;
                    var node = nodes.SingleOrDefault(node => node.Id == id) ?? throw new IntelRefusal($"Unregistered follow-up {id}.");
                    foreach (var edge in node.Edges) Visit(edge.To);
                }
                Visit(initial.Id);
                nodes = nodes.Select(node => node with { ReachableFromOpening = reachable.Contains(node.Id) }).ToArray();
                var opening = context.Opening ?? (initial is MoveState ? initial.Id : null);
                if (opening is not null && !reachable.Contains(opening)) throw new IntelRefusal("Context opening is not reachable from the constructed initial state.");
                variants.Add(new(context.Name, context.Encounters, context.Slot, initial.Id, opening, nodes,
                    PatternSchedules.ReferenceLine(opening, nodes), [.. nodes.SelectMany(node => node.Gaps).Distinct(StringComparer.Ordinal),
                    .. (opening is null ? new[] { "Opening is conditional or random; no predicate was evaluated and no move was rolled." } : Array.Empty<string>())]));
                if (context.Encounters.Length == 0 && context.Set.Count == 0) constructs = true;
            }
            catch (Exception error) when (error is not IntelRefusal)
            {
                gaps.Add($"{context.Name}: clean construction unavailable ({error.GetBaseException().GetType().Name}: {error.GetBaseException().Message}); no fight runner used; consult the separate wiki fallback receipt.");
            }
        }
        return new(canonical.Id.ToString(), constructs, variants, input.Phases.GetValueOrDefault(name) ?? [], gaps);
    }

    private static PatternNode CaptureNode(MonsterState state, MonsterModel model, Input input)
    {
        var gaps = new List<string>();
        var edges = new List<PatternEdge>();
        var attacks = new List<PatternAttack>();
        var intents = new List<string>();
        var mustPerform = false;
        int? strength = 0;
        PatternSummonCap? groupCap = null;
        var kind = "unsupported";
        if (state.GetType() == typeof(MoveState))
        {
            var move = (MoveState)state;
            kind = "move";
            mustPerform = move.MustPerformOnceBeforeTransitioning;
            if (input.GroupSummonCaps.TryGetValue(model.GetType().Name + ":" + move.Id, out var cap))
            {
                var type = model.GetType();
                var guard = type.GetMethod(cap.Guard, Members) ?? throw new IntelRefusal("Missing group summon guard.");
                var counter = type.GetProperty(cap.Counter, Members)?.GetMethod ?? throw new IntelRefusal("Missing group summon counter.");
                var sharedBy = type.GetMethod(cap.SharedBy, Members) ?? throw new IntelRefusal("Missing inspected counter-sharing member.");
                var bounds = ChoiceEntryPoints.ConstantsComparedWith(guard, counter);
                if (bounds.Count != 1 || bounds[0].Constant <= 0 || bounds[0].Comparison != cap.Comparison)
                    throw new IntelRefusal("Group summon cap guard has an unknown bound or comparison shape.");
                groupCap = new(bounds[0].Constant, $"{NameOf(guard)} compares {NameOf(counter)} ({cap.Comparison}); counter sharing inspected in {NameOf(sharedBy)}");
            }
            if ((move.FollowUpState?.Id ?? move.FollowUpStateId) is { } follow) edges.Add(new(follow));
            else gaps.Add("No fixed follow-up; the move may exit or rely on a reactive transition.");
            intents.AddRange(move.Intents.Select(intent => intent.GetType().Name));
            foreach (var attack in move.Intents.OfType<AttackIntent>())
            {
                // Only constructor-captured literals are safe without a combat context
                var calc = attack.DamageCalc ?? throw new IntelRefusal("Attack intent has no damage source.");
                var owner = calc.Target?.GetType().DeclaringType;
                var field = owner == typeof(SingleAttackIntent) || owner == typeof(MultiAttackIntent)
                    ? calc.Target!.GetType().GetField("damage", Members) : null;
                decimal? damage = field?.FieldType == typeof(int) ? (int)field.GetValue(calc.Target)! : null;
                var repeats = attack.GetType() == typeof(SingleAttackIntent) || attack.GetType() == typeof(DeathBlowIntent) ? 1
                    : attack.GetType() == typeof(MultiAttackIntent) && typeof(MultiAttackIntent).GetField("_repeatCalc", Members)!.GetValue(attack) is null
                        ? (int?)typeof(MultiAttackIntent).GetField("_repeat", Members)!.GetValue(attack) : null;
                var dynamic = input.DynamicAttacks.GetValueOrDefault(model.GetType().Name + ":" + move.Id);
                if (damage is null && dynamic?.DamageProperty is { } damageProperty) damage = ReadStrength(model, damageProperty);
                if (repeats is null && dynamic?.HitsProperty is { } hitsProperty) repeats = ReadStrength(model, hitsProperty);
                repeats ??= dynamic?.Hits;
                var gap = dynamic?.Rule ?? (damage is null || repeats is null ? "Dynamic damage or hit count requires combat context; delegate not evaluated." : null);
                attacks.Add(new PatternAttack(damage, repeats, gap) { ReferenceRule = dynamic?.Rule });
                if (gap is not null) gaps.Add(gap);
            }
            if (input.Strength.TryGetValue(model.GetType().Name, out var gains) && gains.TryGetValue(move.Id, out var getter))
                strength = ReadStrength(model, getter);
            else if (move.Intents.Any(intent => intent is BuffIntent))
            {
                strength = null;
                gaps.Add("Buff effects are not an inspected cumulative damage binding; later reference quantities remain gaps.");
            }
        }
        else if (state.GetType() == typeof(RandomBranchState))
        {
            kind = "random";
            foreach (var branch in ((RandomBranchState)state).States)
            {
                float? weight = null;
                string? rule = null;
                if (input.WeightRules.TryGetValue(model.GetType().Name + ":" + state.Id + ":" + branch.stateId, out var weightRule)) rule = weightRule;
                else
                {
                    // The float overload captures its argument; every other body must be a literal return
                    var target = branch.weightLambda.Target;
                    var field = target?.GetType().DeclaringType == typeof(RandomBranchState)
                        ? target.GetType().GetField("weight", Members) : null;
                    weight = field?.FieldType == typeof(float) ? (float)field.GetValue(target)!
                        : ChoiceEntryPoints.ConstantSingleReturn(branch.weightLambda.Method);
                    if (weight is null)
                    {
                        rule = "Uninterpreted context-dependent weight; delegate not evaluated.";
                        gaps.Add(rule);
                    }
                }
                if (!Enum.IsDefined(branch.repeatType)) throw new IntelRefusal("Unknown repeat policy.");
                edges.Add(new PatternEdge(branch.stateId, Weight: weight, WeightRule: rule, Repeat: branch.repeatType.ToString(), Cooldown: branch.cooldown, MaxRepeats: branch.maxTimes)
                {
                    SourceMember = NameOf(branch.weightLambda.Method),
                });
            }
        }
        else if (state.GetType() == typeof(ConditionalBranchState))
        {
            kind = "conditional";
            var branches = (IEnumerable)typeof(ConditionalBranchState).GetProperty("States", Members)!.GetValue(state)!;
            var index = 0;
            var binding = input.Conditions.GetValueOrDefault(model.GetType().Name + ":" + state.Id);
            foreach (var branch in branches)
            {
                var to = (string)branch.GetType().GetField("id", Members)!.GetValue(branch)!;
                var condition = binding is not null && index < binding.Length ? binding[index] : null;
                if (input.Conditions.TryGetValue(model.GetType().Name + ":" + state.Id + ":" + to, out var specific)) condition = specific.Single();
                if (condition is null) gaps.Add($"Uninterpreted predicate at {state.Id} branch {index}; no predicate evaluated.");
                var predicate = (Delegate)branch.GetType().GetField("_conditionalLambda", Members)!.GetValue(branch)!;
                edges.Add(new PatternEdge(to, condition ?? "Uninterpreted predicate") { SourceMember = NameOf(predicate.Method) });
                index++;
            }
        }
        else gaps.Add($"Unknown graph state shape {state.GetType().FullName}; not approximated.");
        return new PatternNode(state.Id, kind, edges, intents, attacks, mustPerform, false, strength, gaps)
        {
            WikiMoveNames = input.WikiMoves.GetValueOrDefault(model.GetType().Name + ":" + state.Id) ?? [],
            SourceMember = NameOf(model.GetType().GetMethod("GenerateMoveStateMachine", Members)!),
            GroupSummonCap = groupCap,
        };
    }

    private static string NameOf(MethodBase member) => member.DeclaringType!.FullName + "." + member.Name;

    private static IReadOnlyList<PatternHookCoverage> ReadReactiveCoverage(Input input)
    {
        var references = input.Phases.Values.SelectMany(phases => phases).Select(phase => phase.Authority).ToArray();
        return ChoiceEntryPoints.CallSites(member =>
                member.DeclaringType == typeof(CreatureCmd) && member.Name is "Add" or "Stun" ||
                member.DeclaringType == typeof(MonsterModel) && member.Name == "SetMoveImmediate" ||
                member.DeclaringType == typeof(MonsterMoveStateMachine) && member.Name == "ForceCurrentState")
            .Select(site => ChoiceEntryPoints.DeclaredMember(site.Caller)).Distinct()
            .Where(member => member.DeclaringType?.Namespace is "MegaCrit.Sts2.Core.Models.Monsters" or "MegaCrit.Sts2.Core.Models.Powers")
            .Select(member => new PatternHookCoverage(NameOf(member),
                references.Any(reference => reference.Contains(member.DeclaringType!.Name + "." + member.Name, StringComparison.Ordinal)) ? "reviewed-member" : "gap",
                references.Any(reference => reference.Contains(member.DeclaringType!.Name + "." + member.Name, StringComparison.Ordinal))
                    ? "A build-scoped source rule names this producer member; the hook was not executed."
                    : "Reactive producer is not interpreted; no fight runner used; consult the separate wiki fallback receipt."))
            .OrderBy(row => row.Member, StringComparer.Ordinal).ToArray();
    }

    private static int ReadStrength(MonsterModel model, string binding)
    {
        if (binding.StartsWith("literal:", StringComparison.Ordinal)) return int.Parse(binding[8..], System.Globalization.CultureInfo.InvariantCulture);
        for (var type = model.GetType(); type is not null; type = type.BaseType)
            if (type.GetProperty(binding, Members | BindingFlags.DeclaredOnly) is { } property) return (int)property.GetValue(model)!;
        throw new IntelRefusal($"Missing Strength binding {model.Id}.{binding}.");
    }
}
