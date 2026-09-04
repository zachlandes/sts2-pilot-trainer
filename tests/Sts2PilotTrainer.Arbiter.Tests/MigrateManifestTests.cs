using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one on-disk rewriter. A caller who named an output path asked for that file
/// to exist, whatever format the input was already in; the read path still edits
/// nothing.
///
/// Known gap: these are behaviour tests against a prepared game, and CI runs the
/// game-free domain filter, so CI never executes them. Closing that is the
/// build/test-scope mismatch tracked separately on main, not this change.
/// </summary>
public class MigrateManifestTests
{
    private static string ScratchDirectory()
    {
        var path = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"migrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ScratchPath()
    {
        var path = Path.Combine(ScratchDirectory(), "out.replay.json");
        return path;
    }

    private static string CurrentText =>
        ManifestJson.Serialize(ManifestJson.Load(Arbiter.Manifest)) + "\n";

    [GameFact]
    public void WritesTheOutputEvenWhenTheInputIsAlreadyCurrent()
    {
        var outPath = ScratchPath();

        var result = Arbiter.Run("migrate-manifest", Arbiter.Manifest, "--out", outPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outPath), result.All);
        Assert.Equal(CurrentText, File.ReadAllText(outPath));
    }

    [GameFact]
    public void CreatesAnOutputDirectoryThatDoesNotExistYet()
    {
        var outPath = Path.Combine(ScratchDirectory(), "nested", "deeper", "out.replay.json");

        var result = Arbiter.Run("migrate-manifest", Arbiter.Manifest, "--out", outPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outPath), result.All);
        Assert.Equal(CurrentText, File.ReadAllText(outPath));
    }

    [GameFact]
    public void LeavesAnAlreadyCurrentManifestAloneWhenRewritingItInPlace()
    {
        var path = ScratchPath();
        File.WriteAllText(path, CurrentText);
        var before = File.ReadAllText(path);

        var result = Arbiter.Run("migrate-manifest", path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("unchanged", result.All, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [GameFact]
    public void RewritesInPlaceWithoutLosingTheManifestItRead()
    {
        var path = ScratchPath();
        var expected = ManifestJson.Load(Arbiter.Manifest);
        File.WriteAllText(path, CurrentText.TrimEnd('\n'));

        var result = Arbiter.Run("migrate-manifest", path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("migrated", result.All, StringComparison.Ordinal);
        Assert.Equal(CurrentText, File.ReadAllText(path));
        Assert.Equal(expected.RunId, ManifestJson.Load(path).RunId);
    }
}
