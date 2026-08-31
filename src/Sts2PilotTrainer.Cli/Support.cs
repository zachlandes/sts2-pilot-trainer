using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.IO;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static class Args
{
    /// <summary>The nth argument that is not a flag or a flag's value.</summary>
    internal static string Positional(string[] args, int index, string what)
    {
        var positionals = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) { i++; continue; }
            positionals.Add(args[i]);
        }
        return index < positionals.Count
            ? positionals[index]
            : throw new ManifestException($"Missing required argument: {what}.");
    }

    internal static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>All values for a flag that may be repeated.</summary>
    internal static IReadOnlyList<string> Values(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) values.Add(args[i + 1]);
        }
        return values;
    }
}

internal static class Json
{
    internal static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

internal sealed class EvidenceArtifact
{
    private EvidenceArtifact(string path) => Path = path;

    internal string Path { get; }

    internal static EvidenceArtifact PreparePath(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        return Prepare(System.IO.Path.GetDirectoryName(full)!, System.IO.Path.GetFileName(full));
    }

    internal static EvidenceArtifact Prepare(string directory, string fileName)
    {
        if (System.IO.Path.GetFileName(fileName) != fileName)
        {
            throw new ManifestException("Evidence artifact name must be a file name.");
        }

        Directory.CreateDirectory(directory);
        var root = System.IO.Path.GetFullPath(directory);
        var path = PathContainment.RequireContained(root, System.IO.Path.Combine(root, fileName));
        if (File.Exists(path)) File.Delete(path);
        return new EvidenceArtifact(path);
    }

    internal void WriteAtomic(string content)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var temporary = PathContainment.RequireContained(
            directory, System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp"));
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal static class Paths
{
    /// <summary>
    /// A path fit to print. Relative to the working directory where possible, so
    /// that a home directory never ends up in a log, a screenshot, or a demo
    /// document that gets published.
    /// </summary>
    internal static string Display(string path)
    {
        var full = Path.GetFullPath(path);
        var cwd = Directory.GetCurrentDirectory();
        return full.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full[(cwd.Length + 1)..]
            : Path.GetFileName(full);
    }
}
