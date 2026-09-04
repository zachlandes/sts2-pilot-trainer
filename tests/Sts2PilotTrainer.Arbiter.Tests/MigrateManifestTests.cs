using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one on-disk rewriter. A caller who named an output path asked for that file
/// to exist, whatever format the input was already in; the read path still edits
/// nothing.
/// </summary>
public class MigrateManifestTests
{
    private static string ScratchPath()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"migrate-{Guid.NewGuid():N}.replay.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    [GameFact]
    public void WritesTheOutputEvenWhenTheInputIsAlreadyCurrent()
    {
        var outPath = ScratchPath();

        var result = Arbiter.Run("migrate-manifest", Arbiter.Manifest, "--out", outPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outPath), result.All);
        Assert.Equal(
            ManifestJson.Serialize(ManifestJson.Load(Arbiter.Manifest)) + "\n",
            File.ReadAllText(outPath));
    }

    [GameFact]
    public void LeavesAnAlreadyCurrentManifestAloneWhenRewritingItInPlace()
    {
        var path = ScratchPath();
        File.WriteAllText(path, ManifestJson.Serialize(ManifestJson.Load(Arbiter.Manifest)) + "\n");
        var before = File.ReadAllText(path);

        var result = Arbiter.Run("migrate-manifest", path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("unchanged", result.All, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));
    }
}
