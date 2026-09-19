namespace Sts2PilotTrainer.IO;

/// <summary>
/// The one game build adopted by this checkout, as <c>scripts/game-build.txt</c>
/// records it and as the bootstrap writes it beside the prepared set.
///
/// <see cref="Branch"/> is the branch the game's own release info names for the
/// installation the pristine hash was taken from; the two Steam branches need not
/// ship the same assembly, so a record without it could not say which one it proves.
/// </summary>
public sealed record GameBuildRecord(
    string Version,
    string BuildDateUtc,
    string Commit,
    string Branch,
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
            ? value : throw new InvalidDataException($"Game build record {path} is missing '{key}'.");
        return new(Required("version"), Required("build_date_utc"), Required("commit"), Required("branch"),
            Required("pristine_assembly_sha256"), Required("id_database_hash"));
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,
            $"version={Version}\nbuild_date_utc={BuildDateUtc}\ncommit={Commit}\nbranch={Branch}\n" +
            $"pristine_assembly_sha256={PristineAssemblySha256}\nid_database_hash={IdDatabaseHash}\n");
    }

    public bool Matches(GameBuildRecord other) => this == other;

    /// <summary>Each field on which this record and <paramref name="other"/> disagree, named.</summary>
    public IReadOnlyList<string> DifferencesFrom(GameBuildRecord other)
    {
        var differences = new List<string>();
        void Compare(string field, string mine, string theirs)
        {
            if (mine != theirs) differences.Add($"{field}: {mine} != {theirs}");
        }
        Compare("version", Version, other.Version);
        Compare("build_date_utc", BuildDateUtc, other.BuildDateUtc);
        Compare("commit", Commit, other.Commit);
        Compare("branch", Branch, other.Branch);
        Compare("pristine_assembly_sha256", PristineAssemblySha256, other.PristineAssemblySha256);
        Compare("id_database_hash", IdDatabaseHash, other.IdDatabaseHash);
        return differences;
    }
}
