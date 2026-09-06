using System.Text;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.FormatReference;

/// <summary>
/// The per-verb format reference, derived from the code that enforces it.
///
/// An internal engineering reference and nothing else: it is not published, it is not
/// linked from anything a player reads, and no wording in it reaches the mod. What it
/// answers is the question that was previously answered by reading a private switch in
/// the validator and a table in an assembly that needs the game to load - which
/// arguments each recorded decision carries, and which of the game's own commands the
/// arbiter issues for it.
///
/// It is generated because a hand-written copy of those two declarations would be wrong
/// within a patch or two and nothing would say so. <c>scripts/format-reference.sh
/// --check</c> regenerates it and compares, and CI fails when the committed file is not
/// what the code produces.
/// </summary>
public static class ManifestFormatReference
{
    public const string RelativePath = "docs/manifest-format.md";

    /// <summary>The whole reference, as it belongs on disk.</summary>
    public static string Render(string repositoryRoot)
    {
        var rules = ValidatorSource.Read(repositoryRoot);
        var (mapped, unmapped) = EngineCommandSource.Read(repositoryRoot);
        Reconcile(rules, mapped, unmapped);

        var nonEmpty = ValidatorSource.ReadNonEmptyArguments(repositoryRoot);
        var enumerated = ValidatorSource.ReadEnumeratedArguments(repositoryRoot);
        var shopKinds = ValidatorSource.ReadShopPurchaseKinds(repositoryRoot);
        var rewardKinds = ValidatorSource.ReadClaimRewardKinds(repositoryRoot);
        var controls = ControlArguments();
        var commands = mapped.ToDictionary(row => row.Verb, StringComparer.Ordinal);

        var page = new StringBuilder();
        Preamble(page);
        Summary(page, rules, commands);

        foreach (var rule in rules)
        {
            Verb(page, rule, commands[rule.Verb], nonEmpty, enumerated, controls);
            if (rule.Verb == nameof(ActionVerb.ShopPurchase))
            {
                Kinds(page, "kind", "purchase", "bought", shopKinds);
            }

            if (rule.Verb == nameof(ActionVerb.ClaimReward))
            {
                Kinds(page, "reward_type", "claim", "claimed", rewardKinds);
            }
        }

        Refused(page, rules, unmapped);
        return page.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void Preamble(StringBuilder page)
    {
        page.Append(
            $"""
             <!-- Generated from the code. Do not edit; see the note below. -->

             # The manifest format, verb by verb

             Internal engineering reference.
             It is not part of what this project publishes and nothing a player sees is written from it.

             Every word below is derived from the two declarations that decide the answers,
             so it cannot describe a format the code does not enforce:

             * `{ValidatorSource.RelativePath}` decides which arguments a recorded decision may carry.
             * `{EngineCommandSource.RelativePath}` decides which of the game's own commands the arbiter issues for it.

             It answers one question per verb: which arguments that decision may carry, and which
             command runs it.
             Rules that relate two arguments to each other, and everything in a manifest outside
             `actions[]`, are the validator's and are not restated here.

             Regenerate it with `./scripts/format-reference.sh`, and commit the result in the
             change that moved either declaration.
             CI regenerates and compares, so a stale copy fails the build rather than misleading a reader.
             Editing this file by hand only moves that failure to somebody else's branch.


             """);
    }

    private static void Summary(
        StringBuilder page, IReadOnlyList<VerbArguments> rules, IReadOnlyDictionary<string, EngineCommandRow> commands)
    {
        page.AppendLine("## Every verb this build replays");
        page.AppendLine();
        page.AppendLine("| Verb | Required arguments | Engine command |");
        page.AppendLine("|---|---|---|");
        foreach (var rule in rules)
        {
            var required = rule.Required.Count == 0 ? "none" : Code(rule.Required);
            var command = commands[rule.Verb];
            page.AppendLine($"| [`{rule.Verb}`](#{Anchor(rule.Verb)}) | {required} | `{command.Type}.{command.Member}` |");
        }

        page.AppendLine();
    }

    private static void Verb(
        StringBuilder page,
        VerbArguments rule,
        EngineCommandRow command,
        IReadOnlyList<string> nonEmpty,
        IReadOnlyList<(string Argument, IReadOnlyList<string> Values)> enumerated,
        IReadOnlyList<string> controls)
    {
        page.AppendLine($"## `{rule.Verb}`");
        page.AppendLine();

        if (rule.Note is { } note)
        {
            page.AppendLine(note);
            page.AppendLine();
        }

        if (rule.Allowed.Count == 0)
        {
            page.AppendLine("Carries no arguments. Any argument at all is refused.");
        }
        else
        {
            page.AppendLine("| Argument | Presence | Value |");
            page.AppendLine("|---|---|---|");
            foreach (var name in rule.Allowed)
            {
                var presence = rule.Required.Contains(name, StringComparer.Ordinal)
                    ? "required"
                    : controls.Contains(name, StringComparer.Ordinal) ? "negative control" : "optional";
                page.AppendLine($"| `{name}` | {presence} | {Shape(name, rule, nonEmpty, enumerated)} |");
            }
        }

        page.AppendLine();
        page.AppendLine($"`{command.Type}.{command.Member}`, {Kind(command.Kind)}. {command.Note}");
        page.AppendLine();
    }

    /// <summary>What the validator will accept as this argument's value.</summary>
    private static string Shape(
        string name,
        VerbArguments rule,
        IReadOnlyList<string> nonEmpty,
        IReadOnlyList<(string Argument, IReadOnlyList<string> Values)> enumerated)
    {
        if (rule.NonNegativeIntegers.Contains(name, StringComparer.Ordinal))
        {
            return "non-negative integer, canonical and within Int32";
        }

        if (enumerated.FirstOrDefault(pair => pair.Argument == name) is { Values: not null } restricted)
        {
            return $"one of {Code(restricted.Values)}";
        }

        return nonEmpty.Contains(name, StringComparer.Ordinal) ? "non-empty string" : "string";
    }

    private static void Kinds(
        StringBuilder page,
        string argument,
        string decision,
        string named,
        IReadOnlyList<(string Kind, IReadOnlyList<string> Required)> kinds)
    {
        page.AppendLine($"What a {decision} must name depends on what it {named}.");
        page.AppendLine("An argument a kind does not have is refused as firmly as a missing one.");
        page.AppendLine();
        page.AppendLine($"| `{argument}` | Also required |");
        page.AppendLine("|---|---|");

        foreach (var (kind, required) in kinds)
        {
            page.AppendLine($"| `{kind}` | {(required.Count == 0 ? "nothing" : Code(required))} |");
        }

        page.AppendLine();
    }

    private static void Refused(
        StringBuilder page, IReadOnlyList<VerbArguments> rules, IReadOnlyDictionary<string, string> unmapped)
    {
        page.AppendLine("## Verbs the format names and this build refuses");
        page.AppendLine();
        page.AppendLine(
            "The alphabet is closed, so a decision nobody implemented has a name rather than being absent.");
        page.AppendLine("A manifest that uses one is refused at validation, before an engine is spent on it.");
        page.AppendLine();

        foreach (var verb in Enum.GetValues<ActionVerb>()
                     .Select(verb => verb.ToString())
                     .Where(verb => !rules.Any(rule => rule.Verb == verb)))
        {
            page.AppendLine($"### `{verb}`");
            page.AppendLine();
            page.AppendLine(unmapped[verb]);
            page.AppendLine();
        }
    }

    /// <summary>
    /// Everything the two declarations disagree about, refused before anything is written.
    ///
    /// The reference is the first reader that sees both of them at once, so it is the
    /// first thing able to notice that they have drifted apart. A verb the validator
    /// accepts arguments for and no command executes would pass validation and refuse in
    /// the middle of a replay; a verb with a command and no argument rule is refused by
    /// the validator's own default and can never reach it.
    /// </summary>
    private static void Reconcile(
        IReadOnlyList<VerbArguments> rules,
        IReadOnlyList<EngineCommandRow> mapped,
        IReadOnlyDictionary<string, string> unmapped)
    {
        var problems = new List<string>();
        var ruled = rules.Select(rule => rule.Verb).ToHashSet(StringComparer.Ordinal);
        var commanded = mapped.Select(row => row.Verb).ToHashSet(StringComparer.Ordinal);

        problems.AddRange(mapped.GroupBy(row => row.Verb, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key} has {group.Count()} rows in the engine-command table. One verb runs one command, " +
                "so there is no answer to which one it runs, and keying the rows by verb would have died on the " +
                "duplicate before this reference printed anything at all."));

        problems.AddRange(ruled.Except(commanded).Select(verb =>
            $"{verb} has argument rules and no engine command. A manifest using it would validate and then " +
            "refuse partway through a replay."));

        problems.AddRange(commanded.Except(ruled).Select(verb =>
            $"{verb} has an engine command and no argument rule, so the validator's default refuses it and " +
            "the command can never run."));

        problems.AddRange(Enum.GetValues<ActionVerb>().Select(value => value.ToString())
            .Where(verb => !commanded.Contains(verb) && !unmapped.ContainsKey(verb))
            .Select(verb => $"{verb} is in the format's alphabet and neither table says anything about it."));

        problems.AddRange(commanded.Intersect(unmapped.Keys)
            .Select(verb => $"{verb} is both mapped and listed as unmapped. One of the two is stale."));

        foreach (var rule in rules)
        {
            problems.AddRange(rule.Required.Concat(rule.NonNegativeIntegers)
                .Where(name => !rule.Allowed.Contains(name, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(name => $"{rule.Verb} constrains '{name}', which it does not allow."));
        }

        if (problems.Count > 0)
        {
            throw new SourceRefusal(
                "The validator and the engine-command table do not describe the same format:" +
                string.Concat(problems.Select(problem => $"{Environment.NewLine}  - {problem}")));
        }
    }

    /// <summary>The argument names that exist only so a negative control can nominate an alternative.</summary>
    private static IReadOnlyList<string> ControlArguments() =>
        typeof(Corruption).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

    private static string Kind(string kind) => kind switch
    {
        "Issued" => "called by the driver",
        "Answered" => "answered rather than called",
        _ => throw new SourceRefusal(
            $"'{kind}' is a kind of engine command this reference has no sentence for. Add one rather than " +
            "printing the enum member at a reader."),
    };

    private static string Code(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"`{name}`"));

    private static string Anchor(string verb) => verb.ToLowerInvariant();
}
