using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class ReplayTests
{
    [GameFact]
    public void PreflightRefusesTheUnprovedHeadlessModMismatch()
    {
        var result = Arbiter.Run("preflight", Arbiter.Manifest);

        Assert.False(result.Verified);
        Assert.Contains("mod_environment", result.Output, StringComparison.Ordinal);
        Assert.Contains("does NOT match", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void PreflightAcceptsACompleteVanillaEnvironment()
    {
        var result = Arbiter.Run("preflight", Arbiter.SyntheticReplayFixture());

        Assert.True(result.Verified, result.All);
        Assert.Contains("environment matches", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void PreflightRefusesAManifestFromADifferentBuild()
    {
        // The negative input for the preflight checker. Replaying into a mismatched
        // environment does not fail - it succeeds at producing a different run - so
        // this refusal is the only thing standing between a mismatch and a confident
        // wrong answer.
        var path = Temp("wrong-build.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    BuildVersion = Fact<string>.Observed("v0.103.2", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run("preflight", path);

        Assert.False(result.Verified);
        Assert.Contains("does NOT match", result.Output, StringComparison.Ordinal);
        Assert.Contains("build_version", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void PreflightRefusesAManifestWithADifferentContentHash()
    {
        var path = Temp("wrong-hash.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    ContentHash = Fact<string>.Observed("1234567890", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run("preflight", path);

        Assert.False(result.Verified);
        Assert.Contains("content_hash", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void SyntheticReplayReproducesEveryPinnedEngineCheckpoint()
    {
        var result = Arbiter.Run("replay", Arbiter.SyntheticReplayFixture());

        Assert.True(result.Verified, result.All);
        Assert.Contains("status         : VERIFIED", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void ReplayingTwiceInFreshProcessesProducesByteIdenticalState()
    {
        var result = Arbiter.Run(
            "determinism", Arbiter.SyntheticReplayFixture(), "--runs", "2", "--out", TempDir());

        Assert.True(result.Verified, result.All);
        Assert.Contains("byte-identical canonical state", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void EveryCorruptedHistoryIsRejectedAndTheUncorruptedOneIsNot()
    {
        var outDir = TempDir();

        var result = Arbiter.Run(
            "negative-controls", Arbiter.SyntheticReplayFixture(), "--out", outDir);

        Assert.True(result.Verified, result.All);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json"))).RootElement;
        Assert.True(report.GetProperty("baseline_verified").GetBoolean());
        Assert.True(report.GetProperty("all_rejected").GetBoolean());

        var controls = report.GetProperty("controls").EnumerateArray().ToList();
        Assert.All(controls, c => Assert.True(c.GetProperty("arbiter_rejected").GetBoolean()));

        // The two corruptions arithmetic on the footage cannot see must both be here
        // and must both be rejected. Without them the suite would only demonstrate
        // what the cheaper checks already caught.
        foreach (var name in new[] { "reorder-plays", "substitute-same-cost" })
        {
            var control = controls.Single(c => c.GetProperty("name").GetString() == name);
            Assert.Equal("Undetected", control.GetProperty("video_only_verdict").GetString());
            Assert.True(control.GetProperty("arbiter_rejected").GetBoolean());
        }
    }

    [GameFact]
    public void ReorderingIsCaughtAtTheFirstDivergentCheckpoint()
    {
        // The reordered cards spend the same energy and produce the same visible
        // totals. The first bound checkpoint catches their order before the later
        // discard-pile ordering exposes it in canonical state.
        var outDir = TempDir();
        Arbiter.Run("negative-controls", Arbiter.SyntheticReplayFixture(), "--out", outDir);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json"))).RootElement;
        var reorder = report.GetProperty("controls").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "reorder-plays");

        Assert.True(reorder.GetProperty("arbiter_rejected").GetBoolean());
        Assert.Equal("Undetected", reorder.GetProperty("video_only_verdict").GetString());
        Assert.Contains("combat.block", reorder.GetProperty("first_divergence").GetString(), StringComparison.Ordinal);
    }

    private static string Temp(string name)
    {
        var dir = TempDir();
        return Path.Combine(dir, name);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>
/// The publication gate. Its whole job is to be hard to pass, so it needs a
/// demonstrated failure as much as a demonstrated pass.
/// </summary>
public class PublicationGateTests
{
    [GameFact]
    public void RefusesPublicationWithoutHeadlessModParityEvidence()
    {
        var result = Arbiter.Run("gate", Arbiter.Manifest, "--out", TempDir());

        Assert.False(result.Verified);
        Assert.Contains("NOT PUBLISHABLE", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void RefusesSyntheticEngineFixturesAsPublicationEvidence()
    {
        var outDir = TempDir();
        var result = Arbiter.Run("gate", Arbiter.SyntheticReplayFixture(), "--out", outDir);

        Assert.False(result.Verified);
        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        var source = report.GetProperty("conditions").EnumerateArray()
            .Single(condition => condition.GetProperty("name").GetString() == "publication-source");
        Assert.False(source.GetProperty("passed").GetBoolean());
    }

    [GameFact]
    public void RefusesWhenTheEnvironmentDoesNotMatch()
    {
        // The cheapest way to make a condition fail without touching the history.
        // A gate that passed here would be reporting on nothing.
        var outDir = TempDir();
        var path = Path.Combine(outDir, "wrong-build.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    BuildVersion = Fact<string>.Observed("v0.103.2", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run("gate", path, "--out", outDir);

        Assert.False(result.Verified);
        Assert.Contains("NOT PUBLISHABLE", result.Output, StringComparison.Ordinal);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        Assert.False(report.GetProperty("publishable").GetBoolean());

        var environment = report.GetProperty("conditions").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "environment");
        Assert.False(environment.GetProperty("passed").GetBoolean());
    }

    [GameFact]
    public void RecordsTheStandardItAppliedAlongsideTheVerdict()
    {
        // So an artifact can never be read as having met a weaker standard than the
        // one actually applied.
        var outDir = TempDir();
        Arbiter.Run("gate", Arbiter.Manifest, "--out", outDir);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        var standard = report.GetProperty("standard").GetString()!;

        Assert.Contains("real-engine", standard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No proxy is accepted", standard, StringComparison.Ordinal);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
