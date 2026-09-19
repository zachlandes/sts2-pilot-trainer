using System.Globalization;

namespace Sts2PilotTrainer.IO;

/// <summary>The immutable game build adopted by this checkout.</summary>
public sealed record GameBuildRecord(
    string Version,
    string BuildDateUtc,
    string Commit,
    string PristineAssemblySha256,
    string IdDatabaseHash)
{
    public static GameBuildRecord Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Game build record is missing: {path}");
        var values = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts.Length == 2 ? parts[1].Trim() : "", StringComparer.Ordinal);
        string Required(string key) => values.TryGetValue(key, out var value) && value.Length > 0
            ? value : throw new InvalidDataException($"Game build record missing '{key}'.");
        return new(Required("version"), Required("build_date_utc"), Required("commit"),
            Required("pristine_assembly_sha256"), Required("id_database_hash"));
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,
            $"version={Version}\nbuild_date_utc={BuildDateUtc}\ncommit={Commit}\n" +
            $"pristine_assembly_sha256={PristineAssemblySha256}\nid_database_hash={IdDatabaseHash}\n");
    }

    public bool Matches(GameBuildRecord other) => this == other;
}
