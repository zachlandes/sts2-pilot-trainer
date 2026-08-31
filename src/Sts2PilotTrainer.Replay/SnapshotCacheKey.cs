using System.Security.Cryptography;
using System.Text;
using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Identity of a materialised pre-turn snapshot.
///
/// A snapshot is a derived cache and never a source of truth: it is only ever the
/// result of replaying an action history from run start in a matching environment.
/// The key is therefore everything that determines that result - the environment
/// identity plus the exact ordered history - so a snapshot can never be served for
/// a run that would not actually produce it.
/// </summary>
public sealed record SnapshotCacheKey(
    string BuildVersion,
    string Seed,
    string ContentHash,
    string GameMode,
    string Character,
    int Ascension,
    string ActsHash,
    string ModEnvironmentHash,
    string ActionHistoryHash,
    int UpToSeq)
{
    /// <summary>Field separator that cannot occur in a verb, an arg name, or an arg
    /// value, so two different histories can never render to the same string.</summary>
    private const char Unit = '\u001f';

    /// <summary>
    /// Hash of the ordered action history: verb and args only.
    ///
    /// Provenance is deliberately excluded. Evidence timestamps, notes, and the
    /// declared RNG classification describe how we came to believe an action
    /// happened; they do not change what the engine does. If they were in the key,
    /// improving an annotation would throw away a correctly verified snapshot, and
    /// the pressure would be to stop improving annotations.
    ///
    /// Sequence numbers are included, so a reordering changes the hash even when the
    /// multiset of actions is identical. That is the point.
    /// </summary>
    public static string HashActions(IEnumerable<ActionRecord> actions)
    {
        var sb = new StringBuilder();
        foreach (var action in actions.OrderBy(a => a.Seq))
        {
            sb.Append(action.Seq).Append(Unit).Append(action.Verb);
            foreach (var (k, v) in action.Args.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append(Unit).Append(k).Append('=').Append(v);
            }
            sb.Append('\n');
        }
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static SnapshotCacheKey For(ReplayManifest manifest, int upToSeq) => new(
        manifest.Environment.BuildVersion.Value,
        manifest.Environment.Seed.Value,
        manifest.Environment.ContentHash.Value,
        manifest.Environment.GameMode.Value,
        manifest.Environment.Character.Value,
        manifest.Environment.Ascension.Value,
        HashParts(manifest.Environment.Acts.Value),
        HashModEnvironment(manifest.Environment.Mods.Value),
        HashActions(manifest.Actions.Where(a => a.Seq <= upToSeq)),
        upToSeq);

    /// <summary>Filesystem-safe rendering. Readable on purpose: a cache directory
    /// nobody can read is a cache nobody can audit.</summary>
    public string ToCacheDirectoryName()
    {
        var identityHash = HashParts(
        [
            BuildVersion,
            Seed,
            ContentHash,
            GameMode,
            Character,
            Ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ActsHash,
            ModEnvironmentHash,
            ActionHistoryHash,
            UpToSeq.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]).Replace("sha256:", "", StringComparison.Ordinal);

        return $"{ReadablePart(BuildVersion, 24)}_{ReadablePart(GameMode, 16)}_" +
               $"{ReadablePart(Character, 32)}_a{Ascension}_" +
               $"{ReadablePart(Seed, 24)}_{ReadablePart(ContentHash, 24)}_" +
               $"seq{UpToSeq}_{identityHash}";
    }

    public string ResolveCacheDirectory(string cacheRoot)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            throw new ArgumentException("Cache root cannot be empty.", nameof(cacheRoot));
        }

        var root = WorktreePath.Require(cacheRoot);
        var candidate = WorktreePath.RequireChild(root, ToCacheDirectoryName());

        var candidateEntry = new DirectoryInfo(candidate);
        if (candidateEntry.LinkTarget is not null)
        {
            throw new InvalidOperationException("Snapshot cache directory cannot be a symbolic link.");
        }

        return candidate;
    }

    public static string ResolveCacheArtifact(string cacheDirectory, string fileName)
    {
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("Cache artifact name must be a file name.", nameof(fileName));
        }

        var directory = WorktreePath.Require(cacheDirectory);
        var candidate = WorktreePath.RequireChild(directory, fileName);

        var entry = new FileInfo(candidate);
        if (entry.LinkTarget is not null)
        {
            throw new InvalidOperationException($"Snapshot cache artifact '{fileName}' cannot be a symbolic link.");
        }

        return candidate;
    }

    private static string HashModEnvironment(ModEnvironment mods) => HashParts(
        [mods.Name, mods.ReportedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
         .. mods.Mods.SelectMany(mod => new[] { mod.Name, mod.Role, mod.ReplayRisk })]);

    private static string HashParts(IEnumerable<string> parts) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join(Unit, parts))));

    private static string ReadablePart(string value, int maxLength)
    {
        var safe = new string(value
            .Take(maxLength)
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'
                ? character
                : '-')
            .ToArray());
        return string.IsNullOrEmpty(safe) ? "empty" : safe;
    }
}
