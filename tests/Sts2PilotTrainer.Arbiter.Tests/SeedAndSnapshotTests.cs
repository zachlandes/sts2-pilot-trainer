using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class SeedVerificationTests
{
    [GameFact]
    public void ExactlyOneCandidateSeedReproducesTheMapTheVideoShows()
    {
        // The four candidates are the readings of the overlay that are visually
        // indistinguishable: E and F are separable in neither position at the
        // resolution the video offers. Resolving them by regenerating the map is the
        // whole point - nothing here reads a character.
        var outDir = TempDir();

        var result = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--candidates", "SEXT47K77REK,SFXT47K77RFK,SEXT47K77RFK,SFXT47K77REK",
            "--out", outDir);

        Assert.True(result.Verified, result.All);

        var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "seed-verification-summary.json"))).RootElement;

        Assert.True(summary.GetProperty("resolved").GetBoolean());
        Assert.Equal("SFXT47K77RFK", summary.GetProperty("resolved_seed").GetString());

        // The other three must genuinely fail, and by a wide margin. A check that
        // barely separated the candidates would not be strong enough to overturn a
        // reading that came with full confidence attached.
        foreach (var candidate in summary.GetProperty("results").EnumerateArray())
        {
            var seed = candidate.GetProperty("candidate_seed").GetString();
            var comparison = candidate.GetProperty("comparison");
            var matched = comparison.GetProperty("matched_node_count").GetInt32();
            var observed = comparison.GetProperty("observed_node_count").GetInt32();

            if (seed == "SFXT47K77RFK")
            {
                Assert.True(comparison.GetProperty("matches").GetBoolean());
                Assert.Equal(observed, matched);
            }
            else
            {
                Assert.False(comparison.GetProperty("matches").GetBoolean());
                Assert.True(matched < observed / 2, $"{seed} matched {matched} of {observed} nodes, which is too close to agreement");
            }
        }
    }

    [GameFact]
    public void AWrongSingleCandidateReturnsFailure()
    {
        var result = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--seed", "SEXT47K77REK",
            "--out", TempDir());

        Assert.False(result.Verified);
        Assert.Contains("MISMATCH", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void TheMatchingSeedIsTheOneTheManifestRecords()
    {
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        Assert.Equal("SFXT47K77RFK", manifest.Environment.Seed.Value);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class SnapshotLineTests
{
    [GameFact]
    public void RestoresTheSameVerifiedSnapshotForEachLineAndReportsOnlyDeltas()
    {
        var outDir = TempDir();
        var cacheDir = Path.Combine(outDir, "snapshots");

        var result = Arbiter.Run(
            "snapshot-lines", Arbiter.VanillaReplayFixture(),
            "--at", "1",
            "--line", Path.Combine(Arbiter.RepoRoot, "manifests", "lines", "streamer.line.json"),
            "--line", Path.Combine(Arbiter.RepoRoot, "manifests", "lines", "aggressive.line.json"),
            "--out", outDir, "--cache", cacheDir);

        Assert.True(result.Verified, result.All);

        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "snapshot-lines.json"))).RootElement;

        var lines = report.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.True(l.GetProperty("restore_verified").GetBoolean()));

        // Both lines start from the same state and end somewhere different. If the
        // deltas were equal, the comparison would be showing nothing.
        var deltas = lines.Select(l => l.GetProperty("deltas").ToString()).ToList();
        Assert.NotEqual(deltas[0], deltas[1]);
        Assert.All(lines, l => Assert.NotEmpty(l.GetProperty("deltas").EnumerateArray()));

        // Objective deltas only. No score, no ranking, no verdict about which line was
        // better - that is a question about a game, not about a replay.
        Assert.DoesNotContain("\"score\"", report.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"better\"", report.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"rank\"", report.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [GameFact]
    public void ReusesTheSnapshotOnASecondRunAndKeysItToTheHistoryThatProducedIt()
    {
        var outDir = TempDir();
        var cacheDir = Path.Combine(outDir, "snapshots");
        var lines = new[]
        {
            Path.Combine(Arbiter.RepoRoot, "manifests", "lines", "streamer.line.json"),
            Path.Combine(Arbiter.RepoRoot, "manifests", "lines", "aggressive.line.json"),
        };

        var first = Arbiter.Run(
            "snapshot-lines", Arbiter.VanillaReplayFixture(), "--at", "1",
            "--line", lines[0], "--line", lines[1], "--out", outDir, "--cache", cacheDir);
        Assert.Contains("materialised now", first.Output, StringComparison.Ordinal);

        var second = Arbiter.Run(
            "snapshot-lines", Arbiter.VanillaReplayFixture(), "--at", "1",
            "--line", lines[0], "--line", lines[1], "--out", outDir, "--cache", cacheDir);
        Assert.Contains("cache hit", second.Output, StringComparison.Ordinal);

        // Changing an action before the snapshot point must produce a different key,
        // so the cached snapshot cannot be served for a run that would not produce it.
        var altered = Path.Combine(outDir, "altered.json");
        var manifest = ManifestJson.Load(Arbiter.VanillaReplayFixture());
        var move = manifest.Actions.Single(a => a.Verb == ActionVerb.MapMove);
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in move.Args) args[k] = v;
        args["column"] = "0";
        var alteredActions = manifest.Actions.Select(a => a.Seq == move.Seq ? a with { Args = args } : a).ToList();
        ManifestJson.Save(manifest with { Actions = alteredActions }, altered);

        var third = Arbiter.Run(
            "snapshot-lines", altered, "--at", "1",
            "--line", lines[0], "--line", lines[1], "--out", outDir, "--cache", cacheDir);

        Assert.Contains("materialised now", third.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyOf(second.Output), KeyOf(third.Output), StringComparison.Ordinal);
    }

    private static string KeyOf(string output) =>
        output.Split('\n').First(l => l.StartsWith("snapshot key", StringComparison.Ordinal)).Split(':', 2)[1].Trim();

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
