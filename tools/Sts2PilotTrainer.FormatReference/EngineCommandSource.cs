using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.FormatReference;

/// <summary>One row of the engine-command table, as source text rather than as types.</summary>
public sealed record EngineCommandRow
{
    public required string Verb { get; init; }

    /// <summary>The game type's name, without its namespace, exactly as the row writes it.</summary>
    public required string Type { get; init; }

    public required string Member { get; init; }

    /// <summary>Issued by the driver, or answered when the engine asks.</summary>
    public required string Kind { get; init; }

    public required string Note { get; init; }
}

/// <summary>
/// <c>EngineCommands</c>' table and its list of verbs this build maps nothing onto,
/// read from source.
///
/// Reading it rather than asking the class is not a preference. The table is built
/// from <c>typeof</c> over game types, so the assembly that holds it does not load
/// without a copy of Slay the Spire 2, and the check that keeps this reference honest
/// runs on a machine that has no game. Source is the only form of that table available
/// there, and it is enough: every field this reference prints is a constant written on
/// the row.
///
/// The check the game does own stays where it is. <c>EngineCommands.Verify</c> asks the
/// loaded assembly whether each member still exists, which nothing here can do.
/// </summary>
public static class EngineCommandSource
{
    public const string RelativePath = "src/Sts2PilotTrainer.Engine/EngineCommands.cs";

    /// <summary>The mapped rows in declaration order, and why each unmapped verb is unmapped.</summary>
    public static (IReadOnlyList<EngineCommandRow> Mapped, IReadOnlyDictionary<string, string> Unmapped) Read(
        string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, RelativePath);
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
        var declaration = tree.GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                              .SingleOrDefault(candidate => candidate.Identifier.ValueText == "EngineCommands")
                          ?? throw new SourceRefusal($"{RelativePath} declares no class EngineCommands.");

        var scope = new ConstantScope(Constants(declaration));
        return (ReadTable(Initializer(declaration, "Table"), scope), ReadUnmapped(Initializer(declaration, "Unmapped"), scope));
    }

    /// <summary>String constants the class declares, which its own rows refer to by name.</summary>
    private static IReadOnlyDictionary<string, string> Constants(ClassDeclarationSyntax declaration)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in declaration.Members.OfType<FieldDeclarationSyntax>()
                     .Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword)))
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value is LiteralExpressionSyntax { Token.Value: string value })
                {
                    constants[variable.Identifier.ValueText] = value;
                }
            }
        }

        return constants;
    }

    private static IReadOnlyList<EngineCommandRow> ReadTable(ExpressionSyntax initializer, ConstantScope scope)
    {
        if (initializer is not CollectionExpressionSyntax collection)
        {
            throw SourceRefusal.At(initializer, "The engine-command table is not a collection of rows.");
        }

        return collection.Elements.Select(element =>
        {
            if (element is not ExpressionElementSyntax
                {
                    Expression: ImplicitObjectCreationExpressionSyntax { Initializer: { } properties },
                })
            {
                throw SourceRefusal.At(element, "This is not an engine-command row of the expected shape.");
            }

            var values = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
            foreach (var assignment in properties.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                values[ConstantScope.MemberName(assignment.Left)] = assignment.Right;
            }

            return new EngineCommandRow
            {
                Verb = Verb(Property(values, "Verb", element)),
                Type = TypeName(Property(values, "Type", element)),
                Member = Member(Property(values, "Member", element), scope),
                Kind = ConstantScope.MemberName(Property(values, "Kind", element)),
                Note = scope.String(Property(values, "Note", element)),
            };
        }).ToList();
    }

    private static IReadOnlyDictionary<string, string> ReadUnmapped(ExpressionSyntax initializer, ConstantScope scope)
    {
        if (initializer is not ObjectCreationExpressionSyntax { Initializer: { } entries })
        {
            throw SourceRefusal.At(initializer, "The unmapped-verb list is not a dictionary with entries.");
        }

        var unmapped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries.Expressions)
        {
            if (entry is not AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax key } assignment ||
                key.ArgumentList.Arguments is not [var only])
            {
                throw SourceRefusal.At(entry, "This is not a verb keyed to the reason it is unmapped.");
            }

            unmapped[Verb(only.Expression)] = scope.String(assignment.Right);
        }

        return unmapped;
    }

    private static ExpressionSyntax Property(
        IReadOnlyDictionary<string, ExpressionSyntax> values, string name, SyntaxNode row) =>
        values.TryGetValue(name, out var value)
            ? value
            : throw SourceRefusal.At(row, $"An engine-command row sets no {name}.");

    private static string Verb(ExpressionSyntax expression)
    {
        var verb = ConstantScope.MemberName(expression);
        return Enum.TryParse<ActionVerb>(verb, out _)
            ? verb
            : throw SourceRefusal.At(expression, $"'{verb}' is not a verb the format names.");
    }

    private static string TypeName(ExpressionSyntax expression) =>
        expression is TypeOfExpressionSyntax typeOf
            ? typeOf.Type.ToString()
            : throw SourceRefusal.At(expression, "A row's Type is not a typeof over a game type.");

    /// <summary>`nameof(Type.Member)`, or the constant standing in for a constructor.</summary>
    private static string Member(ExpressionSyntax expression, ConstantScope scope)
    {
        if (expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } nameOf)
        {
            return ConstantScope.MemberName(nameOf.ArgumentList.Arguments[0].Expression);
        }

        return scope.String(expression);
    }

    private static ExpressionSyntax Initializer(ClassDeclarationSyntax declaration, string field)
    {
        var variable = declaration.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(member => member.Declaration.Variables)
            .SingleOrDefault(candidate => candidate.Identifier.ValueText == field);

        return variable?.Initializer?.Value
               ?? throw new SourceRefusal($"{RelativePath}: EngineCommands declares no initialized {field}.");
    }
}
