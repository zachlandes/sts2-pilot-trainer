using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sts2PilotTrainer.FormatReference;

/// <summary>
/// A shape in the source this reader will not guess at.
///
/// Everything here reads declarations that were written to be read by a person, so
/// there is always a shape it has not seen. Approximating one would publish a
/// reference that quietly disagrees with the code, which is the single failure this
/// whole file exists to prevent; refusing turns it into a red build with a sentence
/// naming the line.
/// </summary>
public sealed class SourceRefusal(string message) : Exception(message)
{
    public static SourceRefusal At(SyntaxNode node, string what)
    {
        var line = node.GetLocation().GetLineSpan();
        return new SourceRefusal(
            $"{Path.GetFileName(line.Path)}:{line.StartLinePosition.Line + 1}: {what} " +
            $"The reader will not guess at it. Source: {Condense(node.ToString())}");
    }

    private static string Condense(string text)
    {
        var single = string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim()));
        return single.Length <= 160 ? single : single[..160] + "...";
    }
}

/// <summary>
/// Turns the constant expressions these declarations are built from into the values
/// they stand for.
///
/// The two files it reads name their strings through constants - <c>Corruption</c>'s
/// argument names, <c>EngineCommands.ConstructorMember</c> - so a reader that only
/// understood literals would either miss them or restate them. Constants that live in
/// an assembly this tool can load are resolved by loading it, and constants declared
/// in the file being read are resolved from that file; nothing else is accepted.
/// </summary>
public sealed class ConstantScope
{
    private readonly IReadOnlyDictionary<string, string> local;

    public ConstantScope(IReadOnlyDictionary<string, string>? localConstants = null) =>
        local = localConstants ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The string this expression evaluates to, refusing anything else.</summary>
    public string String(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal when literal.Token.Value is string value => value,

        // A note broken across source lines with `+`, which is how every long one here
        // is written.
        BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } sum =>
            String(sum.Left) + String(sum.Right),

        IdentifierNameSyntax identifier when local.TryGetValue(identifier.Identifier.ValueText, out var value) =>
            value,

        MemberAccessExpressionSyntax member => Referenced(member),

        _ => throw SourceRefusal.At(expression, "This is not a string constant this reader can evaluate."),
    };

    /// <summary>
    /// The same, for a place where a non-constant is a legitimate answer rather than a
    /// refusal: a lookup keyed by a loop variable names no single argument, so there is
    /// nothing to say about it and nothing wrong with it.
    /// </summary>
    public bool TryString(ExpressionSyntax expression, out string value)
    {
        try
        {
            value = String(expression);
            return true;
        }
        catch (SourceRefusal)
        {
            value = string.Empty;
            return false;
        }
    }

    /// <summary>The last name in a dotted expression, for the enum members these tables index by.</summary>
    public static string MemberName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => throw SourceRefusal.At(expression, "This is not a named member."),
    };

    /// <summary>Every public constant string declared on a type in the replay assembly.</summary>
    private static string Referenced(MemberAccessExpressionSyntax member)
    {
        var typeName = member.Expression.ToString();
        var type = typeof(Replay.ActionRecord).Assembly.GetType($"Sts2PilotTrainer.Replay.{typeName}");
        var field = type?.GetField(member.Name.Identifier.ValueText);
        if (field is not { IsLiteral: true } || field.GetRawConstantValue() is not string value)
        {
            throw SourceRefusal.At(
                member,
                $"'{member}' is not a public string constant on a type in Sts2PilotTrainer.Replay.");
        }

        return value;
    }
}
