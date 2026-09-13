using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The game's own player-choice entry points and who reaches them, read from the game
/// assembly's IL rather than from a list anybody writes.
///
/// Three questions <c>RunRecorderTests</c> asks of a build, all of them about whether
/// the funnel <see cref="CardPrompts"/> is built on still holds: which public members
/// of <see cref="CardSelectCmd"/> and <see cref="RelicSelectCmd"/> hand back a chosen
/// card, cards or relic; which method bodies anywhere in the game create or show a
/// card-selection screen, or put the hand into its selection mode; and which model
/// types - cards, relics, potions, powers, events, monsters - call each entry point.
/// The first is reflection over two types. The other two walk every method body in
/// the assembly and resolve every call token, which is a few seconds and needs no
/// scene tree: <c>MethodBody.GetILAsByteArray</c> and <c>Module.ResolveMethod</c> work
/// on a loaded assembly whether or not the game has started.
///
/// The assembly is loaded against the vendored Godot stubs, which lack the nested
/// <c>PropertyName</c> and <c>MethodName</c> classes some node types carry, so
/// <c>Assembly.GetTypes</c> throws and the loadable types are kept. That tolerance is
/// what makes <see cref="LoadedScreenTypes"/> necessary: a screen the stubs could not
/// load would scan as a screen nothing opens, and a caller check over that would pass
/// on nothing.
/// </summary>
internal static class ChoiceEntryPoints
{
    /// <summary>The namespace every card-selection screen lives in on this build.</summary>
    internal const string ScreenNamespace = "MegaCrit.Sts2.Core.Nodes.Screens.CardSelection";

    /// <summary>The namespace segment the game's own test doubles live under, kept out
    /// of the enumeration and the caller check: they reach the entry points, and
    /// nothing a player does goes through them.</summary>
    private const string MocksSegment = "Mocks";

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(op => op.Value, op => op);

    private static readonly Lazy<IReadOnlyDictionary<MethodBase, IReadOnlySet<Type>>> CallerIndex = new(BuildCallerIndex);

    /// <summary>
    /// Loads the engine assembly before any member here names a game type. Its module
    /// initializer is what teaches the runtime where the prepared game lives, and a
    /// static field typed over the game would run before it: this is why nothing
    /// static here holds a game type.
    /// </summary>
    static ChoiceEntryPoints()
    {
        _ = EngineHost.StartupPhase();
    }

    internal static Assembly Game => typeof(CardSelectCmd).Assembly;

    /// <summary>The two types whose public statics are the choice entry points.</summary>
    internal static IReadOnlyList<Type> CommandTypes => [typeof(CardSelectCmd), typeof(RelicSelectCmd)];

    /// <summary>
    /// Every public static member of a command type that hands back a chosen card,
    /// cards or relic, in a stable order.
    /// </summary>
    internal static IReadOnlyList<MethodInfo> All() =>
        CommandTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => ReturnsAChoice(method.ReturnType))
            .OrderBy(method => method.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(CardPrompts.Signature, StringComparer.Ordinal)
            .ToList();

    /// <summary>An entry point named the way <see cref="CardPrompts.Forwarders"/> names one.</summary>
    internal static string Signature(MethodBase method) => CardPrompts.Signature(method);

    /// <summary>The same, with the declaring type in front.</summary>
    internal static string QualifiedSignature(MethodBase method) =>
        $"{method.DeclaringType!.Name}.{Signature(method)}";

    /// <summary>
    /// Every method a Harmony patch class in the shell or the recorder attaches to,
    /// resolved on this build. A class whose targets are computed rather than declared
    /// contributes nothing, which is correct for the question asked here: none of those
    /// are entry points.
    /// </summary>
    internal static IReadOnlySet<MethodBase> Patched() =>
        RunRecorder.PatchClasses
            .Concat(CardScreensUp.PatchClasses)
            .Concat(CardPrompts.PatchClasses)
            .SelectMany(patchClass => patchClass
                .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                .OfType<HarmonyPatch>()
                .Select(attribute => Resolve(attribute.info)))
            .OfType<MethodBase>()
            .ToHashSet();

    /// <summary>
    /// The entry points a method's own body calls - the body, its async state machine's
    /// <c>MoveNext</c> and the lambdas it captured - which is one level of forwarding
    /// and no more.
    /// </summary>
    internal static IReadOnlyList<MethodBase> EntryPointsCalledBy(MethodInfo method)
    {
        var entryPoints = All().ToHashSet<MethodBase>();
        return OwnCallees(method).Where(entryPoints.Contains).Distinct().ToList();
    }

    /// <summary>
    /// Whether a callee is one of the ways a card prompt is put in front of a player: a
    /// card-selection screen created or shown, or the hand told to select.
    /// </summary>
    internal static bool OpensAPrompt(MethodBase callee)
    {
        var type = callee.DeclaringType;
        if (type is null) return false;
        if (type == typeof(NPlayerHand) && callee.Name == nameof(NPlayerHand.SelectCards)) return true;
        return type.Namespace == ScreenNamespace
               && type.Name.EndsWith("Screen", StringComparison.Ordinal)
               && callee.Name is "Create" or "ShowScreen";
    }

    /// <summary>
    /// Every prompt-opening call in the game assembly with the outermost types whose
    /// bodies make it, the game's own mocks left out.
    /// </summary>
    internal static IReadOnlyList<(MethodBase Opener, IReadOnlyList<Type> Callers)> PromptOpenings() =>
        CallerIndex.Value
            .Where(entry => OpensAPrompt(entry.Key))
            .Select(entry => (entry.Key, (IReadOnlyList<Type>)entry.Value
                .Where(type => !IsMock(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList()))
            .OrderBy(entry => QualifiedSignature(entry.Key), StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The outermost types whose bodies call an entry point directly, other than the
    /// command types themselves and the game's own mocks.
    /// </summary>
    internal static IReadOnlyList<Type> ReachedBy(MethodBase entryPoint) =>
        CallerIndex.Value.TryGetValue(entryPoint, out var callers)
            ? callers
                .Where(type => !CommandTypes.Contains(type) && !IsMock(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>
    /// The top-level types of the screen namespace this build could load, plus the hand,
    /// by name - the set <see cref="OpensAPrompt"/> can see at all.
    /// </summary>
    internal static IReadOnlyList<string> LoadedScreenTypes() =>
        LoadableTypes()
            .Where(type => !type.IsNested && type.Namespace == ScreenNamespace
                                          && type.Name.EndsWith("Screen", StringComparison.Ordinal))
            .Select(type => type.Name)
            .Append(nameof(NPlayerHand))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The committed enumeration: every model that reaches each entry point, grouped by
    /// the last segment of its namespace, one entry point per block.
    /// </summary>
    internal static string Enumeration()
    {
        var text = new StringBuilder();
        text.AppendLine("# Every game type whose own code calls a player-choice entry point on this build,");
        text.AppendLine("# read from the game assembly's IL by RunRecorderTests and grouped by the entry");
        text.AppendLine("# point it reaches. The game's own mocks are left out. Regenerate with");
        text.AppendLine("#   ./scripts/choice-entry-points.sh --update");
        text.AppendLine("# so a card or relic that gains a prompt on a game update shows as a diff in the");
        text.AppendLine("# change that adopted the build.");
        foreach (var entryPoint in All())
        {
            text.AppendLine();
            text.AppendLine(QualifiedSignature(entryPoint));
            foreach (var group in ReachedBy(entryPoint)
                         .GroupBy(type => type.Namespace?.Split('.').LastOrDefault() ?? string.Empty)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                text.AppendLine(
                    $"  {group.Key}: {string.Join(' ', group.Select(type => type.Name).Order(StringComparer.Ordinal))}");
            }
        }

        return text.ToString();
    }

    private static bool IsMock(Type type) => type.Namespace?.Split('.').Contains(MocksSegment, StringComparer.Ordinal) == true;

    private static bool ReturnsAChoice(Type returnType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>)) return false;
        var inner = returnType.GetGenericArguments()[0];
        if (inner == typeof(CardModel) || inner == typeof(RelicModel)) return true;
        return inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(IEnumerable<>)
               && inner.GetGenericArguments()[0] == typeof(CardModel);
    }

    private static MethodBase? Resolve(HarmonyMethod patch)
    {
        if (patch.declaringType is null) return null;
        if (patch.methodType == MethodType.Constructor || patch.methodName is null)
        {
            return AccessTools.Constructor(patch.declaringType, patch.argumentTypes);
        }

        return AccessTools.Method(patch.declaringType, patch.methodName, patch.argumentTypes);
    }

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }

    private static IEnumerable<Type> LoadableTypes()
    {
        Type?[] types;
        try
        {
            types = Game.GetTypes();
        }
        catch (ReflectionTypeLoadException incomplete)
        {
            types = incomplete.Types;
        }

        return types.OfType<Type>();
    }

    /// <summary>For each method called anywhere in the game, the outermost types whose
    /// bodies call it; compiler-generated state machines and closures are nested types
    /// and count for the type that declares them.</summary>
    private static IReadOnlyDictionary<MethodBase, IReadOnlySet<Type>> BuildCallerIndex()
    {
        var callers = new Dictionary<MethodBase, HashSet<Type>>();
        foreach (var type in LoadableTypes())
        {
            var outer = Outermost(type);
            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                foreach (var callee in Callees(method))
                {
                    if (!callers.TryGetValue(callee, out var set)) callers[callee] = set = [];
                    set.Add(outer);
                }
            }
        }

        return callers.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<Type>)entry.Value);
    }

    /// <summary>The callees of a method's own body, its async state machine's MoveNext,
    /// and the display classes its lambdas were compiled into.</summary>
    private static IEnumerable<MethodBase> OwnCallees(MethodInfo method)
    {
        foreach (var callee in Callees(method)) yield return callee;
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (stateMachine is null) yield break;
        var moveNext = stateMachine.StateMachineType.GetMethod(
            "MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (moveNext is not null)
        {
            foreach (var callee in Callees(moveNext)) yield return callee;
        }

        foreach (var nested in stateMachine.StateMachineType.DeclaringType!
                     .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (!nested.Name.Contains("DisplayClass", StringComparison.Ordinal)) continue;
            foreach (var lambda in nested.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                foreach (var callee in Callees(lambda)) yield return callee;
            }
        }
    }

    /// <summary>Every method a body's call sites resolve to, read off the raw IL.</summary>
    private static IEnumerable<MethodBase> Callees(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
#pragma warning disable CA1031 // The stubs are a partial mirror of GodotSharp and the runtime reports a gap in them as whichever exception the failing load happened to raise
        catch (Exception)
        {
            // A body whose signature names a Godot member the stubs have not got cannot
            // be read here. Which screens loaded is asserted separately, and the funnel
            // test holds every one of them found opened, so a swallowed body cannot
            // pass as a screen nothing opens
            yield break;
        }
#pragma warning restore CA1031

        if (il is null) yield break;
        var module = method.Module;
        var typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var at = 0;
        while (at < il.Length)
        {
            short value = il[at++];
            if (value == 0xFE) value = (short)(0xFE00 | il[at++]);
            if (!OpCodesByValue.TryGetValue(value, out var op)) yield break;
            var operandSize = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                _ => 4,
            };
            if (op.OperandType is OperandType.InlineMethod)
            {
                MethodBase? callee = null;
                try
                {
                    callee = module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                }
#pragma warning disable CA1031 // Same gap, met at the call site rather than the signature
                catch (Exception)
                {
                    // A call into a member the Godot stubs have not got resolves to
                    // nothing, for the reason above
                }
#pragma warning restore CA1031

                if (callee is not null) yield return callee;
            }

            at += operandSize;
        }
    }
}
