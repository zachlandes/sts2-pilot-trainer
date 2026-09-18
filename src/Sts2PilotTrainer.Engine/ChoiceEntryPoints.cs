using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The game's own player-choice entry points and who reaches them, read from the game
/// assembly's IL rather than from a list anybody writes.
///
/// Three questions <c>RunRecorderTests</c> asks of a build, all of them about whether
/// the funnel the recorder's <c>CardPrompts</c> is built on still holds: which public members
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
/// on nothing. The same gap is met body by body, where a signature or a call site
/// names a stubbed-out member: those bodies are counted by <see cref="UnreadableBodies"/>
/// and held to a committed record, because a body the scan cannot read is a body that
/// could open a prompt unseen.
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

    private static readonly Lazy<Scan> Index = new(ScanAssembly);

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
            .ThenBy(EntryPointSignature.Of, StringComparer.Ordinal)
            .ToList();

    /// <summary>An entry point named the way the recorder's forwarder table names one.</summary>
    internal static string Signature(MethodBase method) => EntryPointSignature.Of(method);

    /// <summary>The same, with the declaring type in front.</summary>
    internal static string QualifiedSignature(MethodBase method) =>
        $"{method.DeclaringType!.Name}.{Signature(method)}";

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

    /// <summary>The prompt-opening calls a method's own body makes, read the same way
    /// and no deeper: a forwarder that opens a prompt directly is one that still
    /// forwards, and only this tells the two apart.</summary>
    internal static IReadOnlyList<MethodBase> PromptsOpenedBy(MethodInfo method) =>
        OwnCallees(method).Where(OpensAPrompt).Distinct().ToList();

    /// <summary>Whether a caller is the funnel itself. Asked here rather than with a
    /// <c>typeof</c> in a test body, which the runtime resolves at JIT time, before the
    /// engine's resolver has been taught where the game is.</summary>
    internal static bool IsTheFunnel(Type caller) => caller == typeof(CardSelectCmd);

    /// <summary>
    /// Whether a callee is one of the ways a card prompt is put in front of a player: a
    /// card-selection screen created or shown, or the hand told to select.
    /// </summary>
    internal static bool OpensAPrompt(MethodBase callee)
    {
        var type = callee.DeclaringType;
        if (type is null) return false;
        if (type == typeof(NPlayerHand) && callee.Name == nameof(NPlayerHand.SelectCards)) return true;
        return type.Namespace == ScreenNamespace && callee.Name is "Create" or "ShowScreen";
    }

    /// <summary>
    /// Every prompt-opening call in the game assembly with the outermost types whose
    /// bodies make it, the game's own mocks left out.
    /// </summary>
    internal static IReadOnlyList<(MethodBase Opener, IReadOnlyList<Type> Callers)> PromptOpenings() =>
        Index.Value.Callers
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
        Index.Value.Callers.TryGetValue(entryPoint, out var callers)
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
        Loaded.Value.Types
            .Where(type => !type.IsNested && type.Namespace == ScreenNamespace)
            .Select(type => type.Name)
            .Append(nameof(NPlayerHand))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Every type the game assembly defines that the runtime could load
    /// against the vendored stubs - the same set <see cref="MethodsNaming"/> and the
    /// caller scan read from. Exposed so another game-fact reading (<see cref="SavePoints"/>)
    /// can ask about types outside the card-selection namespace without loading the
    /// assembly a second time.</summary>
    internal static IReadOnlyList<Type> AllLoadedTypes => Loaded.Value.Types;

    /// <summary>
    /// Every outermost type with a method body the scan could not read, with how many
    /// of its bodies it could not read: a body whose signature or a call site names a
    /// Godot member the vendored stubs have not got. Each is a body that could open a
    /// prompt or reach an entry point unseen, which is why the set is committed and
    /// held rather than tolerated.
    /// </summary>
    internal static IReadOnlyList<(Type Type, int Bodies)> UnreadableBodies() =>
        Index.Value.UnreadableBodies
            .Select(entry => (entry.Key, entry.Value))
            .OrderBy(entry => entry.Key.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every type the game assembly defines that the runtime could not load against the
    /// stubs, by full name: the assembly's own type table read from its metadata, less
    /// the types that loaded. Not one of their bodies was scanned, so each is the same
    /// blind spot as an unreadable body and is held with them.
    /// </summary>
    internal static IReadOnlyList<string> UnloadableTypes() => Loaded.Value.Unloadable;

    /// <summary>Where the record of <see cref="UnreadableBodies"/> and
    /// <see cref="UnloadableTypes"/> is committed, relative to the repository root.</summary>
    internal const string UnreadableBodiesRecordPath = "scripts/unreadable-choice-scan-bodies.txt";

    /// <summary>The committed record of <see cref="UnreadableBodies"/> and
    /// <see cref="UnloadableTypes"/>, one type per line under two headings. Every type
    /// on it is a place a send or a sync could hide from <see cref="CallSites"/> as
    /// well as a prompt from the funnel check, so the ledger holds the build to the
    /// committed copy too.</summary>
    internal static string UnreadableBodiesRecord()
    {
        var text = new StringBuilder();
        text.AppendLine("# Every game type the choice-entry-point scan in RunRecorderTests could not read");
        text.AppendLine("# whole on this build, because a signature, a call site or the type itself names a");
        text.AppendLine("# Godot member the vendored stubs have not got. Each is code the funnel check does");
        text.AppendLine("# not see, so the set is held here and a change to it fails until it is looked at.");
        text.AppendLine("# Regenerate with");
        text.AppendLine("#   ./scripts/choice-entry-points.sh --update");
        text.AppendLine();
        text.AppendLine("# Types with method bodies the scan could not read, and how many:");
        foreach (var (type, bodies) in UnreadableBodies())
        {
            text.AppendLine($"{type.FullName} {bodies}");
        }

        text.AppendLine();
        text.AppendLine("# Types the runtime could not load, so none of their bodies was scanned:");
        foreach (var name in UnloadableTypes())
        {
            text.AppendLine(name);
        }

        return text.ToString();
    }

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

    /// <summary>
    /// Every method outside the game's own test doubles whose body names the callee -
    /// a call, a construction, or a function pointer taken of it - read off the raw IL
    /// of every loadable type. Method-level where <see cref="ReachedBy"/> is type-level,
    /// for a rule about where in a type something happens rather than which type.
    /// </summary>
    internal static IReadOnlyList<MethodBase> MethodsNaming(MethodBase callee)
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return Loaded.Value.Types
            .Where(type => !IsMock(Outermost(type)))
            .SelectMany(type => type.GetConstructors(every).Concat<MethodBase>(type.GetMethods(every)))
            .Where(method => Callees(method).Contains(callee))
            .ToList();
    }

    /// <summary>
    /// Every member whose own body names <paramref name="callee"/>, read by its declared
    /// name: an async method's compiler-generated state machine resolves back to the
    /// method that declared it, so a caller reads the way a person would name it. What
    /// a committed record lists under "members that call" - <see cref="SavePoints"/>
    /// reads its callers here.
    /// </summary>
    internal static IReadOnlyList<MethodBase> DeclaredCallersOf(MethodBase callee) =>
        MethodsNaming(callee)
            .Select(DeclaredMember)
            .Distinct()
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every method or constructor in <paramref name="assembly"/> whose body constructs
    /// <paramref name="constructed"/>, read off the raw IL: every spelling C# has for a
    /// construction - <c>new T(...)</c>, target-typed <c>new(...)</c>, a collection
    /// initializer - is one <c>newobj</c> here, so a rule about who constructs a type
    /// cannot be dodged by spelling. Every type is walked, the compiler-written closures
    /// and state machines included, and a body that cannot be read refuses by name.
    /// </summary>
    internal static IReadOnlyList<MethodBase> MethodsConstructing(Assembly assembly, Type constructed)
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return assembly.GetTypes()
            .SelectMany(type => type.GetConstructors(every).Concat<MethodBase>(type.GetMethods(every)))
            .Where(method => OperandsOf(method).Any(operand =>
                operand.IsConstruction && operand.Callee?.DeclaringType == constructed))
            .ToList();
    }

    /// <summary>
    /// Every (caller, callee) pair in the game where the callee satisfies
    /// <paramref name="callee"/>, the caller read by its declared member the way a
    /// person would name it and the game's own mocks left out. For a walk over who
    /// sends which message or syncs which choice, where the callee is a family of
    /// members rather than one.
    /// </summary>
    internal static IReadOnlyList<(MethodBase Caller, MethodBase Callee)> CallSites(Func<MethodBase, bool> callee)
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return Loaded.Value.Types
            .Where(type => !IsMock(Outermost(type)))
            .SelectMany(type => type.GetConstructors(every).Concat<MethodBase>(type.GetMethods(every)))
            .SelectMany(method => Callees(method).Where(callee).Select(called => (Caller: DeclaredMember(method), Callee: called)))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Of the outermost types that declare <paramref name="members"/>, the ones with a
    /// body the scan could not read whole, with how many: the reading
    /// <see cref="UnreadableBodies"/> makes, narrowed to the types a walk over
    /// <see cref="CallSites"/> produced a caller from. A call in one of those bodies is
    /// one the walk never saw, so a walk that counts what such a type calls says so
    /// beside its count rather than thinning it. This narrowing names only the types
    /// the walk already reached; a type whose only call sits in an unreadable body
    /// produces no caller and is not here, which is why the whole set is held to its
    /// committed record as well.
    /// </summary>
    internal static IReadOnlyList<(Type Type, int Bodies)> UnreadableBodiesAmong(IEnumerable<MethodBase> members)
    {
        var unreadable = Index.Value.UnreadableBodies;
        return members
            .Select(member => Outermost(member.DeclaringType!))
            .Distinct()
            .Where(unreadable.ContainsKey)
            .Select(type => (type, unreadable[type]))
            .OrderBy(entry => entry.type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every method a body names, or none where it cannot be read.</summary>
    internal static IReadOnlyList<MethodBase> Callees(MethodBase method)
    {
        TryReadCallees(method, out var callees);
        return callees;
    }

    /// <summary>
    /// Every string literal a body loads, in the order it loads them: the same walk
    /// over the same opcode table, reading <c>ldstr</c> tokens where the callee walk
    /// reads call tokens. Refuses rather than answers where the body cannot be read,
    /// because a walk that names a decision's identities off a body it read half of
    /// would be a denominator with a hole nobody could see.
    /// </summary>
    internal static IReadOnlyList<string> StringLiterals(MethodBase method) =>
        OperandsOf(method).Select(operand => operand.Literal).OfType<string>().ToList();

    /// <summary>
    /// The literal each construction of <paramref name="constructed"/> in a body is
    /// named with: the last string loaded before each <c>newobj</c> of that type, in
    /// order. How an id is read off a body that writes <c>new Thing("ID", ...)</c>
    /// without executing it, and without taking every other literal the body loads -
    /// an exception message, a log line - for an id.
    /// </summary>
    internal static IReadOnlyList<string> LiteralsConstructing(MethodBase method, Type constructed) =>
        ConstructionsIn(method, constructed).Select(construction => construction.Literal).OfType<string>().ToList();

    /// <summary>
    /// The literal loaded last before each call a body makes to a callee satisfying
    /// <paramref name="callee"/>, in order: how a key built by a helper such as
    /// <c>InitialOptionKey("X")</c> is read off the body that calls it.
    /// </summary>
    internal static IReadOnlyList<string> LiteralsBefore(MethodBase method, Func<MethodBase, bool> callee)
    {
        var literals = new List<string>();
        string? last = null;
        foreach (var operand in OperandsOf(method))
        {
            if (operand.Literal is { } literal) last = literal;
            if (operand.Callee is { } called && !operand.IsConstruction && callee(called) && last is not null)
            {
                literals.Add(last);
            }
        }

        return literals;
    }

    /// <summary>
    /// Every member of the game assembly whose body constructs <paramref name="constructed"/>,
    /// over the types the runtime could load: <see cref="MethodsConstructing"/> for the
    /// game, which cannot be asked for its whole type table against the stubs. A body
    /// that cannot be read is read as far as it goes, the way the caller scan reads it.
    /// </summary>
    internal static IReadOnlyList<MethodBase> GameMembersConstructing(Type constructed)
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return Loaded.Value.Types
            .SelectMany(type => type.GetConstructors(every).Concat<MethodBase>(type.GetMethods(every)))
            .Where(method =>
            {
                TryReadOperands(method, out var operands);
                return operands.Any(operand => operand.IsConstruction && operand.Callee?.DeclaringType == constructed);
            })
            .ToList();
    }

    /// <summary>One construction of a type in a body: every string loaded since the
    /// previous construction of that type, in order, with the last of them and the
    /// last integer constant loaded in the same span beside it, either null where the
    /// body loaded none in between, and whether the instruction before the last
    /// literal loaded null - the shape of <c>new EventOption(this, null, "KEY")</c>,
    /// an option constructed with no work. <paramref name="Template"/> is the string
    /// the construction is keyed by where the body built it by interpolation just
    /// before constructing - its literal parts with <c>{}</c> at each hole, the shape
    /// of <c>new EventOption(this, Dig, $"FLOWER.pages.DIG_{digs}")</c> - and
    /// <paramref name="KeyLiteral"/> the literal it is keyed by where that literal was
    /// the last string the body produced before constructing, the shape of
    /// <c>new EventOption(this, Done, "PROCEED")</c>; each null where a call came
    /// between, because then the key is what the call returned.</summary>
    internal sealed record Construction(
        IReadOnlyList<string> Literals, string? Literal, int? Constant, bool NullBeforeLiteral = false,
        string? Template = null, string? KeyLiteral = null);

    /// <summary>
    /// Every construction of <paramref name="constructed"/> in a body, each with the
    /// last string and the last integer constant loaded since the construction before
    /// it, in order: how an id and an enum argument are read off a body that writes
    /// <c>new Thing("ID", ..., Kind.Value)</c> without executing it. Both start over at
    /// each construction, so a second one that loads neither reads as carrying neither
    /// rather than inheriting the first's. Which of the two a caller trusts is the
    /// caller's, because a body may load either for another reason on its way to the
    /// construction. The key each is constructed with is read beside them where the
    /// body's shape says what it is: an interpolation the body finishes right before
    /// the construction is its <see cref="Construction.Template"/>, and a literal it
    /// loads right before is its <see cref="Construction.KeyLiteral"/>; a call in
    /// between - a helper, a concatenation, a raw text read - leaves both null, because
    /// the key is then whatever that call returned. Refuses a construction whose key
    /// cannot be attributed to one literal: a bare key literal loaded beside another
    /// literal - <c>done ? "PROCEED" : "DECLINE"</c> loads both with no call between -
    /// or beside a string read from a field, an argument or a local no literal was
    /// stored in, because reading the literal as the key would list one option where
    /// the body offers two.
    /// </summary>
    internal static IReadOnlyList<Construction> ConstructionsIn(MethodBase method, Type constructed)
    {
        var constructions = new List<Construction>();
        var literals = new List<string>();
        int? constant = null;
        var previousLoadedNull = false;
        var nullBeforeLastLiteral = false;
        string? lastLiteral = null;
        StringBuilder? interpolation = null;
        string? template = null;
        string? keyLiteral = null;
        var candidateKeys = new List<string>();
        var literalFed = new HashSet<string>(StringComparer.Ordinal);
        string? fedByNoLiteral = null;
        Operand? previous = null;
        foreach (var operand in OperandsOf(method))
        {
            // A slot stored right after a literal holds that literal, and a key loaded
            // from it is the literal; loaded from any other slot it is not one
            if (operand.StoresTo is { } stored)
            {
                if (previous?.Literal is not null && interpolation is null) literalFed.Add(stored);
                else literalFed.Remove(stored);
            }

            previous = operand;
            if (operand.LoadsStringFrom is { } slot && !literalFed.Contains(slot) && interpolation is null)
            {
                fedByNoLiteral = slot;
            }

            if (operand.Literal is { } loaded)
            {
                literals.Add(loaded);
                nullBeforeLastLiteral = previousLoadedNull;
                lastLiteral = loaded;
                // A literal inside an interpolation is a part of the string being
                // built, appended by the call that follows it, never a key on its own
                if (interpolation is null)
                {
                    keyLiteral = loaded;
                    candidateKeys.Add(loaded);
                    template = null;
                }
            }

            if (operand.Constant is { } value) constant = value;
            if (operand.Callee is { } called)
            {
                if (called.DeclaringType == typeof(DefaultInterpolatedStringHandler))
                {
                    switch (called.Name)
                    {
                        case ".ctor":
                            interpolation = new StringBuilder();
                            break;
                        case nameof(DefaultInterpolatedStringHandler.AppendLiteral):
                            interpolation?.Append(lastLiteral);
                            break;
                        case nameof(DefaultInterpolatedStringHandler.AppendFormatted):
                            interpolation?.Append("{}");
                            break;
                        case nameof(DefaultInterpolatedStringHandler.ToStringAndClear):
                            template = interpolation?.ToString();
                            interpolation = null;
                            break;
                    }

                    // The handler's own calls are how the string is built and produce
                    // no other string, so the last-produced reading is theirs to set
                    if (interpolation is not null || template is not null)
                    {
                        keyLiteral = null;
                        candidateKeys.Clear();
                        fedByNoLiteral = null;
                        previousLoadedNull = operand.LoadsNull;
                        continue;
                    }
                }

                if (called.IsConstructor && operand.IsConstruction && called.DeclaringType == constructed)
                {
                    var candidates = candidateKeys.Distinct(StringComparer.Ordinal).ToList();
                    var bareKeyAmong = candidates.Any(key => BareKey.IsMatch(key));
                    if (bareKeyAmong && candidates.Count > 1)
                    {
                        throw new InvalidOperationException(
                            $"{method.DeclaringType?.Name}.{method.Name} constructs {constructed.Name} after loading " +
                            $"{string.Join(", ", candidates.Select(key => $"'{key}'"))} with no call between, so which is " +
                            "its key cannot be read off the body.");
                    }

                    if (bareKeyAmong && fedByNoLiteral is { } source)
                    {
                        throw new InvalidOperationException(
                            $"{method.DeclaringType?.Name}.{method.Name} constructs {constructed.Name} after loading " +
                            $"{string.Join(", ", candidates.Select(key => $"'{key}'"))} and a string from {source} with no " +
                            "call between, so its key cannot be attributed to a literal on the way to it.");
                    }

                    constructions.Add(new Construction(
                        literals, literals.LastOrDefault(), constant, nullBeforeLastLiteral, template, keyLiteral));
                    literals = [];
                    constant = null;
                    nullBeforeLastLiteral = false;
                    template = null;
                    keyLiteral = null;
                    candidateKeys.Clear();
                    fedByNoLiteral = null;
                }
                else if (TouchesAString(called))
                {
                    // A call that returns a string may have produced the key, and one
                    // that takes a string may have consumed the literal; neither
                    // reading survives it. One that does neither - the empty hover-tip
                    // array a construction's params default loads - leaves the key
                    // where it was
                    template = null;
                    keyLiteral = null;
                    candidateKeys.Clear();
                    fedByNoLiteral = null;
                }
            }

            previousLoadedNull = operand.LoadsNull;
        }

        return constructions;
    }

    /// <summary>A bare key: the <c>PROCEED</c> an event constructs an option with
    /// directly, which is neither a whole key nor a name <c>InitialOptionKey</c>
    /// completes. The one shape a construction is refused for carrying two of.</summary>
    internal static readonly Regex BareKey = new(@"^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant);

    private static bool TouchesAString(MethodBase called) =>
        called is MethodInfo { ReturnType: var returns } && returns == typeof(string) ||
        called.GetParameters().Any(parameter => parameter.ParameterType == typeof(string));

    /// <summary>
    /// Every integer constant a body compares what <paramref name="read"/> returns
    /// with, each with the comparison's opcode: the operands in order, the call, then
    /// the constant, then the comparison - <c>NumberOfDigs &lt; 2</c> compiled to the
    /// getter, <c>ldc.i4.2</c> and the <c>bge</c> that jumps past the block. How a
    /// bound an event's own code keeps in a constant is read off the build rather
    /// than written down, so a build that moves it moves the reading.
    /// </summary>
    internal static IReadOnlyList<(int Constant, string Comparison)> ConstantsComparedWith(MethodBase method, MethodBase read)
    {
        var operands = OperandsOf(method);
        var comparisons = new List<(int, string)>();
        for (var i = 0; i + 2 < operands.Count; i++)
        {
            if (operands[i].Callee == read && operands[i + 1].Constant is { } constant &&
                operands[i + 2].Comparison is { } comparison)
            {
                comparisons.Add((constant, comparison));
            }
        }

        return comparisons;
    }

    private static IReadOnlyList<Operand> OperandsOf(MethodBase method)
    {
        if (!TryReadOperands(method, out var operands))
        {
            throw new InvalidOperationException(
                $"{method.DeclaringType?.FullName}.{method.Name} could not be read whole against the vendored " +
                "stubs, so what it loads cannot be enumerated.");
        }

        return operands;
    }

    /// <summary>One operand a body's instruction carries: a method token resolved, a
    /// string token resolved, an integer constant loaded, whether the instruction
    /// constructs, and the comparison an instruction makes where it is one of
    /// <see cref="ComparisonOpCodes"/>, by the name of its long signed form.</summary>
    private sealed record Operand(
        MethodBase? Callee, string? Literal, bool IsConstruction, int? Constant = null, string? Comparison = null,
        bool LoadsNull = false, string? LoadsStringFrom = null, string? StoresTo = null);

    /// <summary>
    /// Whether a body reads the run's players and compares their count as greater
    /// than one: the shape of a rule that admits only a run with another player in it
    /// - <c>runState.Players.Count &gt; 1</c> - read off the operands in order, the
    /// player read, then the count, then the constant, then the comparison. The
    /// opposite rule, <c>== 1</c>, compares with <c>ceq</c> and is not this.
    /// </summary>
    internal static bool ComparesPlayerCountAsMoreThanOne(MethodBase method)
    {
        var operands = OperandsOf(method);
        for (var i = 0; i + 3 < operands.Count; i++)
        {
            if (operands[i].Callee?.Name == "get_Players" && operands[i + 1].Callee?.Name == "get_Count"
                && operands[i + 2].Constant == 1 && operands[i + 3].Comparison == nameof(OpCodes.Cgt))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The instructions that compare two values, by the name of the long
    /// signed form: the three that leave a boolean, and the conditional branches,
    /// which are how a compiled <c>if</c> compares - <c>x &lt; 2</c> is <c>ldc.i4.2</c>
    /// and a <c>bge</c> past the block, never a <c>clt</c>. The short and unsigned
    /// forms of a branch are the same comparison.</summary>
    private static readonly Dictionary<short, string> ComparisonOpCodes = new()
    {
        [OpCodes.Cgt.Value] = nameof(OpCodes.Cgt),
        [OpCodes.Clt.Value] = nameof(OpCodes.Clt),
        [OpCodes.Ceq.Value] = nameof(OpCodes.Ceq),
        [OpCodes.Beq.Value] = nameof(OpCodes.Beq),
        [OpCodes.Beq_S.Value] = nameof(OpCodes.Beq),
        [OpCodes.Bge.Value] = nameof(OpCodes.Bge),
        [OpCodes.Bge_S.Value] = nameof(OpCodes.Bge),
        [OpCodes.Bge_Un.Value] = nameof(OpCodes.Bge),
        [OpCodes.Bge_Un_S.Value] = nameof(OpCodes.Bge),
        [OpCodes.Bgt.Value] = nameof(OpCodes.Bgt),
        [OpCodes.Bgt_S.Value] = nameof(OpCodes.Bgt),
        [OpCodes.Bgt_Un.Value] = nameof(OpCodes.Bgt),
        [OpCodes.Bgt_Un_S.Value] = nameof(OpCodes.Bgt),
        [OpCodes.Ble.Value] = nameof(OpCodes.Ble),
        [OpCodes.Ble_S.Value] = nameof(OpCodes.Ble),
        [OpCodes.Ble_Un.Value] = nameof(OpCodes.Ble),
        [OpCodes.Ble_Un_S.Value] = nameof(OpCodes.Ble),
        [OpCodes.Blt.Value] = nameof(OpCodes.Blt),
        [OpCodes.Blt_S.Value] = nameof(OpCodes.Blt),
        [OpCodes.Blt_Un.Value] = nameof(OpCodes.Blt),
        [OpCodes.Blt_Un_S.Value] = nameof(OpCodes.Blt),
        [OpCodes.Bne_Un.Value] = nameof(OpCodes.Bne_Un),
        [OpCodes.Bne_Un_S.Value] = nameof(OpCodes.Bne_Un),
    };

    // The short forms of ldc.i4 carry their value in the opcode rather than as an operand
    private static readonly Dictionary<short, int> InlineIntegerOpCodes = new()
    {
        [OpCodes.Ldc_I4_M1.Value] = -1,
        [OpCodes.Ldc_I4_0.Value] = 0,
        [OpCodes.Ldc_I4_1.Value] = 1,
        [OpCodes.Ldc_I4_2.Value] = 2,
        [OpCodes.Ldc_I4_3.Value] = 3,
        [OpCodes.Ldc_I4_4.Value] = 4,
        [OpCodes.Ldc_I4_5.Value] = 5,
        [OpCodes.Ldc_I4_6.Value] = 6,
        [OpCodes.Ldc_I4_7.Value] = 7,
        [OpCodes.Ldc_I4_8.Value] = 8,
    };

    /// <summary>
    /// The member the game's author wrote, for a method the compiler wrote on their
    /// behalf: an async state machine's <c>MoveNext</c> resolves to the method that
    /// declares the state machine, and a lambda to the method whose body takes its
    /// address; anything else is its own author's.
    /// </summary>
    internal static MethodBase DeclaredMember(MethodBase method)
    {
        var declaring = method.DeclaringType;
        if (declaring is null || !declaring.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) return method;

        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var outer = declaring.DeclaringType ?? throw new InvalidOperationException(
            $"{declaring.FullName} is compiler-generated and nested in nothing; this reading is out of date.");
        var stateMachineOwner = outer.GetMethods(every).Concat<MethodBase>(outer.GetConstructors(every))
            .FirstOrDefault(candidate => candidate.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType == declaring);
        if (stateMachineOwner is not null) return DeclaredMember(stateMachineOwner);

        var pointerTaker = MethodsNaming(method).FirstOrDefault(candidate => candidate.DeclaringType != declaring)
            ?? throw new InvalidOperationException(
                $"{declaring.FullName}.{method.Name} is compiler-generated and nothing takes its address or declares it.");
        return DeclaredMember(pointerTaker);
    }

    private static bool ReturnsAChoice(Type returnType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>)) return false;
        var inner = returnType.GetGenericArguments()[0];
        if (inner == typeof(CardModel) || inner == typeof(RelicModel)) return true;
        return inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(IEnumerable<>)
               && inner.GetGenericArguments()[0] == typeof(CardModel);
    }

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }

    private sealed record LoadedTypes(IReadOnlyList<Type> Types, IReadOnlyList<string> Unloadable);

    /// <summary>The game's types the runtime could load against the stubs.</summary>
    internal static IReadOnlyList<Type> LoadedGameTypes() => Loaded.Value.Types;

    /// <summary>Every method a body calls, as far as the body could be read: the
    /// reading the caller scan makes, for a walk that patches rather than counts.</summary>
    internal static IReadOnlyList<MethodBase> CalleesAsFarAsReadable(MethodBase method)
    {
        TryReadCallees(method, out var callees);
        return callees;
    }


    private static readonly Lazy<LoadedTypes> Loaded = new(LoadAllTypes);

    /// <summary>The game's types against the stubs: the ones that loaded, and the full
    /// names of the ones that did not. The loader's own exceptions name the Godot member
    /// it could not find rather than the game type that needed it, so the failed types
    /// are found by reading the assembly's type table from its metadata - no loading -
    /// and taking away what loaded.</summary>
    private static LoadedTypes LoadAllTypes()
    {
        Type[] loaded;
        try
        {
            loaded = Game.GetTypes();
        }
        catch (ReflectionTypeLoadException incomplete)
        {
            loaded = incomplete.Types.OfType<Type>().ToArray();
        }

        var loadedNames = loaded.Select(type => type.FullName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var unloadable = DefinedTypeNames()
            .Where(name => !loadedNames.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();
        return new LoadedTypes(loaded, unloadable);
    }

    /// <summary>Every type the game assembly defines, by the full name reflection would
    /// give it, read from the file's metadata without loading anything.</summary>
    private static IEnumerable<string> DefinedTypeNames()
    {
        using var stream = File.OpenRead(Game.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var names = new Dictionary<TypeDefinitionHandle, string>();

        string NameOf(TypeDefinitionHandle handle)
        {
            if (names.TryGetValue(handle, out var known)) return known;
            var definition = metadata.GetTypeDefinition(handle);
            var name = metadata.GetString(definition.Name);
            var declaring = definition.GetDeclaringType();
            var full = declaring.IsNil
                ? definition.Namespace.IsNil ? name : $"{metadata.GetString(definition.Namespace)}.{name}"
                : $"{NameOf(declaring)}+{name}";
            names[handle] = full;
            return full;
        }

        foreach (var handle in metadata.TypeDefinitions)
        {
            var full = NameOf(handle);
            if (full != "<Module>") yield return full;
        }
    }

    private sealed record Scan(
        IReadOnlyDictionary<MethodBase, IReadOnlySet<Type>> Callers,
        IReadOnlyDictionary<Type, int> UnreadableBodies);

    /// <summary>One pass over every method and constructor body in the game: for each
    /// method called anywhere, the outermost types whose bodies call it, and for each
    /// outermost type, how many of its bodies could not be read. Compiler-generated
    /// state machines and closures are nested types and count for the type that
    /// declares them.</summary>
    private static Scan ScanAssembly()
    {
        var callers = new Dictionary<MethodBase, HashSet<Type>>();
        var unreadable = new Dictionary<Type, int>();
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in Loaded.Value.Types)
        {
            var outer = Outermost(type);
            foreach (var method in type.GetConstructors(every).Concat<MethodBase>(type.GetMethods(every)))
            {
                if (!TryReadCallees(method, out var callees))
                {
                    unreadable[outer] = unreadable.GetValueOrDefault(outer) + 1;
                }

                foreach (var callee in callees)
                {
                    if (!callers.TryGetValue(callee, out var set)) callers[callee] = set = [];
                    set.Add(outer);
                }
            }
        }

        return new Scan(
            callers.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<Type>)entry.Value),
            unreadable);
    }

    /// <summary>The callees of a member's own body, its state machine and the lambdas
    /// it takes the address of, for a walk that reads what a caller constructs; a
    /// constructor has a body and no state machine.</summary>
    internal static IReadOnlyList<MethodBase> OwnCalleesOf(MethodBase member) =>
        member is MethodInfo method ? OwnCallees(method).Distinct().ToList() : Callees(member);

    /// <summary>The callees of a method's own body, its async state machine's MoveNext,
    /// and of each lambda either of those takes the address of, read the same way. Only
    /// the lambdas this method's code reaches are followed: a closure class is shared by
    /// every lambda its declaring member wrote, and the game's compiler-generated lambda
    /// class is shared by every member of the type.</summary>
    private static IEnumerable<MethodBase> OwnCallees(MethodInfo method)
    {
        var own = BodyAndStateMachineCallees(method).ToList();
        foreach (var callee in own) yield return callee;
        foreach (var lambda in own.OfType<MethodInfo>().Where(callee => IsLambdaOf(method, callee)).Distinct())
        {
            foreach (var callee in BodyAndStateMachineCallees(lambda)) yield return callee;
        }
    }

    private static bool IsLambdaOf(MethodInfo method, MethodInfo callee)
    {
        var closure = callee.DeclaringType;
        return closure is not null
               && closure.DeclaringType == method.DeclaringType
               && closure.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
    }

    private static IEnumerable<MethodBase> BodyAndStateMachineCallees(MethodInfo method)
    {
        TryReadCallees(method, out var callees);
        foreach (var callee in callees) yield return callee;
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        var moveNext = stateMachine?.StateMachineType.GetMethod(
            "MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (moveNext is null) yield break;
        TryReadCallees(moveNext, out callees);
        foreach (var callee in callees) yield return callee;
    }

    /// <summary>
    /// Every method a body's call sites resolve to, read off the raw IL. False where the
    /// body or one of its call sites could not be read, which is a body whose signature
    /// or callee names a Godot member the vendored stubs have not got: the runtime
    /// reports the gap as whichever exception the failing load happened to raise, and
    /// the callees read before it are still returned.
    /// </summary>
    private static bool TryReadCallees(MethodBase method, out List<MethodBase> callees)
    {
        var readable = TryReadOperands(method, out var operands);
        callees = operands.Select(operand => operand.Callee).OfType<MethodBase>().ToList();
        return readable;
    }

    private static bool TryReadOperands(MethodBase method, out List<Operand> operands)
    {
        operands = [];
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
#pragma warning disable CA1031
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031

        if (il is null) return true;
        var readable = true;
        var module = method.Module;
        var typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var slots = new Slots(method, module, typeArguments, methodArguments);
        var at = 0;
        while (at < il.Length)
        {
            short value = il[at++];
            if (value == 0xFE) value = (short)(0xFE00 | il[at++]);
            if (!OpCodesByValue.TryGetValue(value, out var op)) return false;
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
                try
                {
                    operands.Add(new Operand(
                        module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!,
                        null, op == OpCodes.Newobj));
                }
#pragma warning disable CA1031
                catch (Exception)
                {
                    readable = false;
                }
#pragma warning restore CA1031
            }
            else if (op.OperandType is OperandType.InlineString)
            {
                operands.Add(new Operand(null, module.ResolveString(BitConverter.ToInt32(il, at)), false));
            }
            else if (op == OpCodes.Ldc_I4)
            {
                operands.Add(new Operand(null, null, false, BitConverter.ToInt32(il, at)));
            }
            else if (op == OpCodes.Ldc_I4_S)
            {
                operands.Add(new Operand(null, null, false, (sbyte)il[at]));
            }
            else if (InlineIntegerOpCodes.TryGetValue(op.Value, out var inline))
            {
                operands.Add(new Operand(null, null, false, inline));
            }
            else if (ComparisonOpCodes.TryGetValue(op.Value, out var comparison))
            {
                operands.Add(new Operand(null, null, false, Comparison: comparison));
            }
            else if (op == OpCodes.Ldnull)
            {
                operands.Add(new Operand(null, null, false, LoadsNull: true));
            }
            else if (slots.Load(op, il, at) is { } load)
            {
                operands.Add(new Operand(null, null, false, LoadsStringFrom: load));
            }
            else if (slots.Store(op, il, at) is { } store)
            {
                operands.Add(new Operand(null, null, false, StoresTo: store));
            }

            at += operandSize;
        }

        return readable;
    }

    /// <summary>The places a body keeps a value between instructions - its locals,
    /// its arguments and the fields it reads - named so a key loaded from one can be
    /// attributed to the literal stored there, or refused as fed by something that is
    /// not a literal. A field, a local or a signature the stubs cannot resolve is a
    /// Godot member's and no key, and leaves the body as readable as it was.</summary>
    private sealed class Slots(MethodBase method, Module module, Type[]? typeArguments, Type[]? methodArguments)
    {
        private readonly Lazy<IList<LocalVariableInfo>> locals = new(() =>
            Resolved(() => method.GetMethodBody()?.LocalVariables) ?? []);

        private readonly Lazy<ParameterInfo[]> parameters = new(() => Resolved(method.GetParameters) ?? []);

        private static T? Resolved<T>(Func<T?> read) where T : class
        {
            try
            {
                return read();
            }
#pragma warning disable CA1031
            catch (Exception)
            {
                return null;
            }
#pragma warning restore CA1031
        }

        public string? Load(OpCode op, byte[] il, int at)
        {
            if (op == OpCodes.Ldloc_0) return Local(0);
            if (op == OpCodes.Ldloc_1) return Local(1);
            if (op == OpCodes.Ldloc_2) return Local(2);
            if (op == OpCodes.Ldloc_3) return Local(3);
            if (op == OpCodes.Ldloc_S) return Local(il[at]);
            if (op == OpCodes.Ldloc) return Local(BitConverter.ToUInt16(il, at));
            if (op == OpCodes.Ldarg_0) return Argument(0);
            if (op == OpCodes.Ldarg_1) return Argument(1);
            if (op == OpCodes.Ldarg_2) return Argument(2);
            if (op == OpCodes.Ldarg_3) return Argument(3);
            if (op == OpCodes.Ldarg_S) return Argument(il[at]);
            if (op == OpCodes.Ldarg) return Argument(BitConverter.ToUInt16(il, at));
            if (op == OpCodes.Ldfld || op == OpCodes.Ldsfld) return Field(BitConverter.ToInt32(il, at), typed: true);
            return null;
        }

        public string? Store(OpCode op, byte[] il, int at)
        {
            if (op == OpCodes.Stloc_0) return $"local {0}";
            if (op == OpCodes.Stloc_1) return $"local {1}";
            if (op == OpCodes.Stloc_2) return $"local {2}";
            if (op == OpCodes.Stloc_3) return $"local {3}";
            if (op == OpCodes.Stloc_S) return $"local {il[at]}";
            if (op == OpCodes.Stloc) return $"local {BitConverter.ToUInt16(il, at)}";
            if (op == OpCodes.Stfld || op == OpCodes.Stsfld) return Field(BitConverter.ToInt32(il, at), typed: false);
            return null;
        }

        private string? Local(int index)
        {
            var known = locals.Value;
            return index < known.Count && Resolved(() => known[index].LocalType) == typeof(string) ? $"local {index}" : null;
        }

        private string? Argument(int index)
        {
            if (!method.IsStatic)
            {
                if (index == 0) return null;
                index--;
            }

            var known = parameters.Value;
            return index < known.Length && Resolved(() => known[index].ParameterType) == typeof(string)
                ? $"argument {known[index].Name}"
                : null;
        }

        private string? Field(int token, bool typed)
        {
            var field = Resolved(() => module.ResolveField(token, typeArguments, methodArguments));
            if (field is null) return null;
            return !typed || Resolved(() => field.FieldType) == typeof(string) ? $"field {field.Name}" : null;
        }
    }
}
