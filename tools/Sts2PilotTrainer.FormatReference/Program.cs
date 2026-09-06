using Sts2PilotTrainer.FormatReference;
using Sts2PilotTrainer.IO;

// Writes docs/manifest-format.md from the declarations that enforce the format, or
// with --check says whether the committed copy is still what they produce.
//
// The check is what makes the reference trustworthy rather than decorative: it runs in
// CI, on a runner with no game, which is why both declarations are read as source. A
// generator nobody runs is a document that drifts.
try
{
    var check = args is ["--check"];
    if (args.Length > 0 && !check)
    {
        Console.Error.WriteLine("usage: Sts2PilotTrainer.FormatReference [--check]");
        return 2;
    }

    var root = WorktreeLocator.Find();
    var path = Path.Combine(root, ManifestFormatReference.RelativePath);
    var rendered = ManifestFormatReference.Render(root);

    if (!check)
    {
        File.WriteAllText(path, rendered);
        Console.WriteLine($"Wrote {ManifestFormatReference.RelativePath}");
        return 0;
    }

    var committed = File.Exists(path) ? File.ReadAllText(path) : null;
    if (string.Equals(committed, rendered, StringComparison.Ordinal))
    {
        Console.WriteLine($"{ManifestFormatReference.RelativePath} is what the code produces.");
        return 0;
    }

    Console.Error.WriteLine(Drift(committed, rendered));
    return 1;
}
catch (SourceRefusal refusal)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("The format reference cannot be generated from the code as it now stands.");
    Console.Error.WriteLine(refusal.Message);
    Console.Error.WriteLine();
    return 1;
}

// The first line that differs, because the whole file is long and the answer is
// usually one row.
static string Drift(string? committed, string rendered)
{
    if (committed is null)
    {
        return $"{ManifestFormatReference.RelativePath} is missing. Generate it with " +
               "./scripts/format-reference.sh and commit it.";
    }

    var was = committed.Split('\n');
    var now = rendered.Split('\n');
    var at = 0;
    while (at < was.Length && at < now.Length && was[at] == now[at]) at++;

    return $"""

            {ManifestFormatReference.RelativePath} is not what the code produces.

            It is generated from the validator's per-verb rules and the engine-command
            table, so this means one of those moved and the committed reference stayed
            where it was. Regenerate it in the same change:

                ./scripts/format-reference.sh

            First difference, line {at + 1}:

              committed: {(at < was.Length ? was[at] : "(end of file)")}
              generated: {(at < now.Length ? now[at] : "(end of file)")}

            """;
}
