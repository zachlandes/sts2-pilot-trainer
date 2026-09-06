using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.FormatReference;

/// <summary>What a manifest may say when it records one decision.</summary>
public sealed record VerbArguments
{
    public required string Verb { get; init; }

    /// <summary>Argument names the validator refuses the action without.</summary>
    public required IReadOnlyList<string> Required { get; init; }

    /// <summary>Every name it accepts, required ones included. Anything else is refused.</summary>
    public required IReadOnlyList<string> Allowed { get; init; }

    /// <summary>Names that must parse as a canonical non-negative Int32.</summary>
    public required IReadOnlyList<string> NonNegativeIntegers { get; init; }

    /// <summary>The comment the rule carries, or null. Why this verb records what it does.</summary>
    public required string? Note { get; init; }
}

/// <summary>
/// The per-verb argument rules, read out of <c>ManifestValidator</c>'s own source.
///
/// They are read rather than reflected because they live inside a private method's
/// switch, where each arm is three assignments and a comment. That is the right shape
/// for the validator - collapsing it into a table would hide the per-verb refusals
/// that come after it - and it is not a shape an assembly exposes. So the reference is
/// generated from the declaration itself, and the reader refuses any arm it cannot
/// read rather than emitting a plausible one.
///
/// What the parse claims is then checked against what the validator does:
/// <c>ManifestFormatReferenceTests</c> feeds the real validator an action per rule and
/// asserts it refuses exactly what this says it refuses.
/// </summary>
public static class ValidatorSource
{
    public const string RelativePath = "src/Sts2PilotTrainer.Replay/ManifestValidator.cs";

    private const string RulesMethod = "ValidateActionArguments";
    private const string ShopMethod = "ValidateShopPurchase";

    /// <summary>The placeholder a shop kind's own id argument stands behind until the kind is known.</summary>
    private const string IdArgumentPlaceholder = "idArgument";

    /// <summary>The rules, in the order the switch declares its arms.</summary>
    public static IReadOnlyList<VerbArguments> Read(string repositoryRoot)
    {
        var method = Method(repositoryRoot, RulesMethod);
        var verbSwitch = method.DescendantNodes().OfType<SwitchStatementSyntax>()
            .FirstOrDefault(candidate => candidate.Expression.ToString() == "action.Verb")
            ?? throw new SourceRefusal(
                $"{RelativePath}: {RulesMethod} has no `switch (action.Verb)`. The per-verb argument rules " +
                "are read from that switch, so a rewrite that moves them has to be read a different way.");

        var scope = new ConstantScope();
        var rules = new List<VerbArguments>();
        var sawDefault = false;

        foreach (var section in verbSwitch.Sections)
        {
            if (section.Labels.Any(label => label is DefaultSwitchLabelSyntax))
            {
                sawDefault = true;
                continue;
            }

            rules.Add(ReadArm(section, scope));
        }

        if (!sawDefault)
        {
            throw new SourceRefusal(
                $"{RelativePath}: the per-verb switch has no default arm. A verb nobody wrote a rule for " +
                "would be accepted with any arguments at all, and this reference would not say so.");
        }

        return rules;
    }

    /// <summary>
    /// What a shop purchase of each kind must name.
    ///
    /// Read differently from the rest on purpose: the kinds and the argument that names
    /// what was bought are <c>ShopPurchaseKinds</c>' own public answers, so they are
    /// asked for rather than parsed. Only the companion argument every purchase of a
    /// thing also carries is read from the source, because that one is written there.
    /// </summary>
    public static IReadOnlyList<(string Kind, IReadOnlyList<string> Required)> ReadShopPurchaseKinds(
        string repositoryRoot)
    {
        var method = Method(repositoryRoot, ShopMethod);
        var text = method.ToString();
        foreach (var expected in new[] { $"{nameof(ShopPurchaseKinds)}.{nameof(ShopPurchaseKinds.All)}",
                                         $"{nameof(ShopPurchaseKinds)}.{nameof(ShopPurchaseKinds.IdArgument)}" })
        {
            if (!text.Contains(expected, StringComparison.Ordinal))
            {
                throw new SourceRefusal(
                    $"{RelativePath}: {ShopMethod} no longer reads {expected}, so this reference cannot " +
                    "keep answering the per-kind question from ShopPurchaseKinds.");
            }
        }

        var declaration = method.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(candidate => candidate.Identifier.ValueText == "expected")
            ?? throw new SourceRefusal(
                $"{RelativePath}: {ShopMethod} declares no `expected`, which is the list of arguments a " +
                "purchase of a thing must carry.");

        // `idArgument is null ? Array.Empty<string>() : [idArgument, "option_index"]`.
        // Only the branch that buys something has names in it; the other one is empty
        // by construction and there is nothing to read.
        var companions = declaration.Initializer?.Value is ConditionalExpressionSyntax conditional
            ? Names(conditional.WhenTrue).Count == 0
                ? Names(conditional.WhenFalse)
                : throw SourceRefusal.At(conditional, "The empty branch of `expected` is no longer empty.")
            : throw SourceRefusal.At(
                declaration, "`expected` is not the conditional this reader knows how to read.");

        return ShopPurchaseKinds.All
            .Select(kind =>
            {
                var id = ShopPurchaseKinds.IdArgument(kind);
                var required = id is null
                    ? Array.Empty<string>()
                    : companions.Select(name => name == IdArgumentPlaceholder ? id : name).ToArray();
                return (kind, (IReadOnlyList<string>)required);
            })
            .ToList();
    }

    /// <summary>Argument names the validator refuses when present and blank.</summary>
    public static IReadOnlyList<string> ReadNonEmptyArguments(string repositoryRoot)
    {
        var method = Method(repositoryRoot, RulesMethod);
        var scope = new ConstantScope();
        var names = new List<string>();

        foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                     .Where(call => call.Expression.ToString() == "string.IsNullOrWhiteSpace"))
        {
            // Every one of these is the right half of
            // `action.Args.TryGetValue(<name>, out var x) && string.IsNullOrWhiteSpace(x)`.
            var guard = call.Parent as BinaryExpressionSyntax;
            var lookup = guard?.Left as InvocationExpressionSyntax;
            if (guard is not { RawKind: (int)SyntaxKind.LogicalAndExpression } ||
                lookup?.Expression.ToString() != "action.Args.TryGetValue")
            {
                throw SourceRefusal.At(
                    call, "A blank-argument check is not paired with the lookup that names the argument.");
            }

            names.Add(scope.String(lookup.ArgumentList.Arguments[0].Expression));
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Arguments whose value must be one of a set the assembly publishes, and that set.</summary>
    public static IReadOnlyList<(string Argument, IReadOnlyList<string> Values)> ReadEnumeratedArguments(
        string repositoryRoot)
    {
        var scope = new ConstantScope();
        var found = new List<(string, IReadOnlyList<string>)>();

        // `action.Args.TryGetValue("x", out var v)` somewhere, and `!Set.Contains(v, ...)`
        // somewhere after it, where Set is a public array on a type in the replay
        // assembly. Matched through the local the lookup binds rather than by adjacency,
        // because the shop reads its kind several statements before it checks it.
        foreach (var method in new[] { RulesMethod, ShopMethod }.Select(name => Method(repositoryRoot, name)))
        {
            var bound = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var lookup in method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                         .Where(call => call.Expression.ToString() == "action.Args.TryGetValue"))
            {
                if (lookup.ArgumentList.Arguments is [var key, { Expression: DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax local } }] &&
                    scope.TryString(key.Expression, out var argument))
                {
                    bound[local.Identifier.ValueText] = argument;
                }
            }

            foreach (var negated in method.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>()
                         .Where(node => node.IsKind(SyntaxKind.LogicalNotExpression)))
            {
                if (negated.Operand is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax contains } call ||
                    contains.Name.Identifier.ValueText != "Contains" ||
                    call.ArgumentList.Arguments is not [{ Expression: IdentifierNameSyntax subject }, ..] ||
                    !bound.TryGetValue(subject.Identifier.ValueText, out var restricted))
                {
                    continue;
                }

                found.Add((restricted, Published(contains.Expression)));
            }
        }

        return found;
    }

    /// <summary>The declared string array this expression names, asked of the loaded assembly.</summary>
    private static IReadOnlyList<string> Published(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        var (typeName, fieldName) = text.Contains('.', StringComparison.Ordinal)
            ? (text[..text.LastIndexOf('.')], text[(text.LastIndexOf('.') + 1)..])
            : (nameof(ManifestValidator), text);

        var type = typeof(ActionRecord).Assembly.GetType($"Sts2PilotTrainer.Replay.{typeName}");
        if (type?.GetField(fieldName)?.GetValue(null) is not string[] values)
        {
            throw SourceRefusal.At(
                expression,
                $"'{text}' is not a public string array on a type in Sts2PilotTrainer.Replay, so the values " +
                "this argument is restricted to cannot be read from the assembly.");
        }

        return values;
    }

    private static VerbArguments ReadArm(SwitchSectionSyntax section, ConstantScope scope)
    {
        if (section.Labels is not [CaseSwitchLabelSyntax label])
        {
            throw SourceRefusal.At(
                section, "A rule arm labels more than one verb, so its rules belong to no single verb.");
        }

        var verb = ConstantScope.MemberName(label.Value);
        if (!Enum.TryParse<ActionVerb>(verb, out _))
        {
            throw SourceRefusal.At(label, $"'{verb}' is not a verb the format names.");
        }

        var assigned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var statement in section.Statements.OfType<ExpressionStatementSyntax>())
        {
            if (statement.Expression is not AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                    Left: IdentifierNameSyntax name,
                } assignment)
            {
                throw SourceRefusal.At(statement, "A rule arm does something other than assign its lists.");
            }

            assigned[name.Identifier.ValueText] = Names(assignment.Right, assigned, scope);
        }

        return new VerbArguments
        {
            Verb = verb,
            Required = List(assigned, "required", section),
            Allowed = List(assigned, "allowed", section),
            NonNegativeIntegers = List(assigned, "nonNegativeIntegers", section),
            Note = Comment(section),
        };
    }

    private static IReadOnlyList<string> List(
        IReadOnlyDictionary<string, IReadOnlyList<string>> assigned, string name, SwitchSectionSyntax section) =>
        assigned.TryGetValue(name, out var value)
            ? value
            : throw SourceRefusal.At(section, $"A rule arm assigns no '{name}', so that rule is unstated.");

    private static IReadOnlyList<string> Names(ExpressionSyntax expression) =>
        Names(expression, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), new ConstantScope());

    /// <summary>
    /// A collection expression of argument names, flattened.
    ///
    /// Three forms appear: a list of names, `.. required` splicing a list already
    /// assigned in the same arm, and the bare identifier of such a list. An identifier
    /// with nothing behind it is kept as its own name, which is how the shop's
    /// `idArgument` reaches the caller that knows which kind is being bought.
    /// </summary>
    private static IReadOnlyList<string> Names(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, IReadOnlyList<string>> assigned,
        ConstantScope scope)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier when assigned.TryGetValue(identifier.Identifier.ValueText, out var prior):
                return prior;

            case InvocationExpressionSyntax invocation when invocation.ToString().StartsWith("Array.Empty", StringComparison.Ordinal):
                return [];

            case CollectionExpressionSyntax collection:
                var names = new List<string>();
                foreach (var element in collection.Elements)
                {
                    switch (element)
                    {
                        case SpreadElementSyntax spread:
                            names.AddRange(Names(spread.Expression, assigned, scope));
                            break;
                        case ExpressionElementSyntax { Expression: IdentifierNameSyntax bare }
                            when !assigned.ContainsKey(bare.Identifier.ValueText):
                            names.Add(bare.Identifier.ValueText);
                            break;
                        case ExpressionElementSyntax item:
                            names.AddRange(Names(item.Expression, assigned, scope));
                            break;
                        default:
                            throw SourceRefusal.At(element, "This is not an argument name this reader can read.");
                    }
                }

                return names;

            default:
                return [scope.String(expression)];
        }
    }

    /// <summary>The comment written above a rule arm, as one paragraph, or null.</summary>
    private static string? Comment(SwitchSectionSyntax section)
    {
        var lines = section.Statements.FirstOrDefault()?.GetLeadingTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            .Select(trivia => trivia.ToString().TrimStart('/').Trim())
            .ToList();

        return lines is { Count: > 0 } ? string.Join(' ', lines) : null;
    }

    private static MethodDeclarationSyntax Method(string repositoryRoot, string name)
    {
        var path = Path.Combine(repositoryRoot, RelativePath);
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
        return tree.GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                   .SingleOrDefault(method => method.Identifier.ValueText == name)
               ?? throw new SourceRefusal($"{RelativePath} declares no single method named {name}.");
    }
}
