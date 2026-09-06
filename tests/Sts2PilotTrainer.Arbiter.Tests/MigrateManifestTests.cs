using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>The native recording this repository ships, as the version-5 file it
    /// was before this format bump: the version it was written at, and neither of the
    /// two fields version 6 added.</summary>
    private static string AVersionFiveNativeManifest()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "manifests", "native-9F8CY60C5BK7-20260906-005737.replay.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node["manifest_version"] = ManifestJson.PreviousManifestVersion;

        var native = node["source"]!.AsObject()["native"]!.AsObject();
        native.Remove("integrity");
        native.Remove("migrated_from_version");

        var scratch = Path.Combine(ScratchDirectory(), "version-five.replay.json");
        File.WriteAllText(scratch, node.ToJsonString() + "\n");
        return scratch;
    }

    /// <summary>
    /// The migration this change was made for, done on disk.
    ///
    /// A version-5 native recording could not state an integrity, and the note that it
    /// was migrated is the only thing that waives the option keys its recorder never
    /// read. Both are written here and nowhere else - a reader migrates in memory - so
    /// this is the one operation that has to produce them, and it is the one that was
    /// run against the two shipped native manifests.
    /// </summary>
    [GameFact]
    public void AVersionFiveNativeManifestGainsItsIntegrityAndTheNoteThatItWasMigrated()
    {
        var input = AVersionFiveNativeManifest();
        var outPath = ScratchPath();

        var result = Arbiter.Run("migrate-manifest", input, "--out", outPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("integrity: complete (migrated from version 5)", result.All, StringComparison.Ordinal);

        using var written = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = written.RootElement;
        Assert.Equal(ReplayManifest.CurrentManifestVersion, root.GetProperty("manifest_version").GetInt32());

        var native = root.GetProperty("source").GetProperty("native");
        Assert.Equal(NativeSource.CompleteIntegrity, native.GetProperty("integrity").GetString());
        Assert.Equal(
            ManifestJson.PreviousManifestVersion, native.GetProperty("migrated_from_version").GetInt32());

        // And nothing else. A recorder that never read an option key did not read one,
        // and a recording that stopped nowhere names no stop; inventing either would be
        // the plausible wrong answer rather than the migration.
        Assert.False(native.TryGetProperty("unmapped", out _));
        var migrated = ManifestJson.Load(outPath);
        Assert.All(
            migrated.Actions.Where(action =>
                action.Verb is ActionVerb.ChooseNeowBlessing or ActionVerb.ChooseEventOption),
            action => Assert.False(action.Args.ContainsKey("option_key")));

        // The history itself is untouched, which is what lets the digests it declares
        // still mean what they meant.
        var before = ManifestJson.Load(input);
        Assert.Equal(before.Actions.Count, migrated.Actions.Count);
        Assert.Equal(
            before.Boundaries.Select(boundary => boundary.Digest.Value),
            migrated.Boundaries.Select(boundary => boundary.Digest.Value));

        // A migrated file is in this format, so migrating it again changes nothing.
        Assert.True(ManifestValidator.Validate(migrated).IsValid);
    }

    /// <summary>A version-5 manifest whose source is a video gains the version and
    /// nothing else: integrity is a claim a native recorder makes about what it
    /// watched, and a reconstruction from footage has no recorder to make it.</summary>
    [GameFact]
    public void AVersionFiveVideoManifestGainsTheVersionAndNoIntegrity()
    {
        var node = JsonNode.Parse(File.ReadAllText(Arbiter.Manifest))!.AsObject();
        node["manifest_version"] = ManifestJson.PreviousManifestVersion;
        var input = Path.Combine(ScratchDirectory(), "version-five-video.replay.json");
        File.WriteAllText(input, node.ToJsonString() + "\n");
        var outPath = ScratchPath();

        var result = Arbiter.Run("migrate-manifest", input, "--out", outPath);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("integrity:", result.All, StringComparison.Ordinal);
        Assert.Equal(CurrentText, File.ReadAllText(outPath));
    }
}
