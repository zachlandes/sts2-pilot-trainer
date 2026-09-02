using System.Security.Cryptography;
using System.Text.Json;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class ModHostBoundaryTests
{
    [GameFact]
    public void AdoptLiveRefusesAConsoleProcessWithoutWritingGameInputs()
    {
        var before = GameInputSnapshot();

        var result = Arbiter.Run("adopt-live");

        var after = GameInputSnapshot();
        Assert.False(result.Verified, result.All);
        Assert.Contains("startup phase : None", result.Output, StringComparison.Ordinal);
        Assert.Contains("not a game whose state can be read honestly", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    [Fact]
    public void TheModManifestDeclaresItselfNonGameplayAndPackless()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "CombatTrainer.json");
        var manifest = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        Assert.False(manifest.GetProperty("affects_gameplay").GetBoolean());
        Assert.False(manifest.GetProperty("has_pck").GetBoolean());
        Assert.True(manifest.GetProperty("has_dll").GetBoolean());
        Assert.Empty(manifest.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("CombatTrainer", manifest.GetProperty("id").GetString());
    }

    private static IReadOnlyList<FileFingerprint> GameInputSnapshot()
    {
        var files = new[]
            {
                Path.Combine(Arbiter.RepoRoot, "build", "lib"),
                Path.Combine(Arbiter.RepoRoot, "build", "sandbox"),
            }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);

        return files.Select(path => new FileFingerprint(
                Path.GetRelativePath(Arbiter.RepoRoot, path),
                new FileInfo(path).Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToList();
    }

    private sealed record FileFingerprint(string Path, long Length, string Sha256);
}
