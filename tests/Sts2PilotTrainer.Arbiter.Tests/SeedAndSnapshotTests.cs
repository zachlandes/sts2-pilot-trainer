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
    public void RejectsATraversingCandidateBeforeTouchingItsDerivedPath()
    {
        var outDir = TempDir();
        const string candidate = "x/../../../victim";
        Directory.CreateDirectory(Path.Combine(outDir, "seed-verification-x"));
        var victim = Path.GetFullPath(Path.Combine(outDir, $"seed-verification-{candidate}.json"));
        File.WriteAllText(victim, "must remain");

        var result = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--candidates", candidate,
            "--out", outDir);

        Assert.False(result.Verified);
        Assert.Equal("must remain", File.ReadAllText(victim));
    }

    [GameFact]
    public void CandidateCoordinatorRejectsAnOperationalFailureInsteadOfReadingAStaleResult()
    {
        var outDir = TempDir();
        var candidate = "SFXT47K77RFK";
        var initial = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--seed", candidate,
            "--out", outDir);
        Assert.True(initial.Verified, initial.All);

        var malformedObservation = Path.Combine(outDir, "malformed-map-observation.json");
        File.WriteAllText(malformedObservation, "{");
        var result = Arbiter.Run(
            "verify-seed", malformedObservation,
            "--candidates", candidate,
            "--out", outDir);

        Assert.False(result.Verified);
        Assert.False(File.Exists(Path.Combine(outDir, $"seed-verification-{candidate}.json")));
        Assert.False(File.Exists(Path.Combine(outDir, "seed-verification-summary.json")));
    }

    [GameFact]
    public void SingleCandidateFailureClearsEarlierResultArtifacts()
    {
        var outDir = TempDir();
        var candidate = "SFXT47K77RFK";
        var initial = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--seed", candidate,
            "--out", outDir);
        Assert.True(initial.Verified, initial.All);

        var malformedObservation = Path.Combine(outDir, "malformed-single-observation.json");
        File.WriteAllText(malformedObservation, "{");
        var result = Arbiter.Run(
            "verify-seed", malformedObservation,
            "--seed", candidate,
            "--out", outDir);

        Assert.False(result.Verified);
        Assert.False(File.Exists(Path.Combine(outDir, $"seed-verification-{candidate}.json")));
        Assert.False(File.Exists(Path.Combine(outDir, $"seed-verification-{candidate}.svg")));
    }

    [GameFact]
    public void BoundSeedEvidenceRequiresARejectedAlternativeCandidate()
    {
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        var outDir = TempDir();
        var result = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--candidates", manifest.Environment.Seed.Value,
            "--manifest", Arbiter.Manifest,
            "--acts", string.Join(",", manifest.Environment.Acts.Value),
            "--character", manifest.Environment.Character.Value,
            "--ascension", manifest.Environment.Ascension.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "--game-mode", manifest.Environment.GameMode.Value,
            "--out", outDir);

        Assert.False(result.Verified);
        var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "seed-verification-summary.json"))).RootElement;
        Assert.False(summary.GetProperty("resolved").GetBoolean());
        Assert.False(summary.GetProperty("rejected_alternative_demonstrated").GetBoolean());
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
    public void BoundSeedEvidenceRefusesAnObservationFromAnotherAct()
    {
        var observation = MapObservation.Load(Arbiter.MapObservation) with { ActIndex = 1 };
        var observationPath = WriteObservation(observation);
        var manifest = ManifestJson.Load(Arbiter.Manifest);

        var result = Arbiter.Run(
            "verify-seed", observationPath,
            "--seed", manifest.Environment.Seed.Value,
            "--manifest", Arbiter.Manifest,
            "--out", TempDir());

        Assert.False(result.Verified);
        Assert.Contains("must observe Act 1 at act_index 0", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void BoundSeedEvidenceRefusesDifferentDeclaredActs()
    {
        var manifest = ManifestJson.Load(Arbiter.Manifest);

        var result = Arbiter.Run(
            "verify-seed", Arbiter.MapObservation,
            "--seed", manifest.Environment.Seed.Value,
            "--manifest", Arbiter.Manifest,
            "--acts", "ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY",
            "--out", TempDir());

        Assert.False(result.Verified);
        Assert.Contains("acts do not match the manifest", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void TheMatchingSeedIsTheOneTheManifestRecords()
    {
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        Assert.Equal("SFXT47K77RFK", manifest.Environment.Seed.Value);
    }

    private static string WriteObservation(MapObservation observation)
    {
        var path = Path.Combine(TempDir(), "map-observation.json");
        File.WriteAllText(path, JsonSerializer.Serialize(observation, ManifestJson.Options));
        return path;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>
/// The combat-start snapshot: that it is derived, cached, re-derived to be read, and
/// keyed to the history that produced it.
///
/// The boundary is combat start. There is no mid-combat restore here and nothing
/// designed around one, which is a product decision recorded in
/// docs/comparison-direction.md rather than a gap.
/// </summary>
public class CombatSnapshotTests
{
    [GameFact]
    public void MaterialisesTheCombatStartSnapshotAndBoundsTheCoveredHistory()
    {
        var outDir = TempDir();
        var result = Arbiter.Run(
            "combat-snapshot", Arbiter.SyntheticReplayFixture(),
            "--out", outDir, "--cache", Path.Combine(outDir, "snapshots"));

        Assert.True(result.Verified, result.All);

        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "combat-snapshot.json"))).RootElement;

        Assert.True(report.GetProperty("restore_verified").GetBoolean());
        Assert.Equal("Verified", report.GetProperty("covered_history_status").GetString());

        // The fixture plays its fight to the end, so the covered history reaches a
        // finished combat. Reading that correctly is the whole point of the outcome
        // field: the player's combat state outlives the fight, and the report used to
        // call a won fight an active one.
        Assert.False(report.GetProperty("combat_active_at_history_end").GetBoolean());
        var actions = report.GetProperty("covered_action_count").GetInt32();
        Assert.Equal(actions - 1, report.GetProperty("covered_through_seq").GetInt32());
        Assert.True(report.GetProperty("turns").EnumerateArray().Count() > 1,
            "a completed fight covers more than one turn");

        // The boundary is a fact about what the engine did, so it is located rather
        // than declared: combat starts after the action that entered the room.
        Assert.True(report.GetProperty("boundary_seq").GetInt32() >= 0);

        // Description only. No score, no ranking, no verdict - and no alternative line,
        // because the supported boundary is combat start.
        foreach (var forbidden in new[] { "\"score\"", "\"better\"", "\"rank\"", "\"lines\"" })
        {
            Assert.DoesNotContain(forbidden, report.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [GameFact]
    public void RefusesAnOutOfWorktreeSnapshotCacheBeforeWriting()
    {
        var outDir = TempDir();
        var outside = Path.GetFullPath(Path.Combine(
            Arbiter.RepoRoot, "..", $"snapshot-escape-{Guid.NewGuid():N}"));

        var result = Arbiter.Run(
            "combat-snapshot", Arbiter.SyntheticReplayFixture(),
            "--out", outDir, "--cache", outside);

        Assert.False(result.Verified);
        Assert.Contains("resolves outside the allowed root", result.All, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outside));
    }

    [GameFact]
    public void ReusesTheSnapshotOnASecondRunAndKeysItToTheHistoryThatProducedIt()
    {
        var outDir = TempDir();
        var cacheDir = Path.Combine(outDir, "snapshots");
        var fixture = Arbiter.SyntheticReplayFixture();

        var first = Arbiter.Run("combat-snapshot", fixture, "--out", outDir, "--cache", cacheDir);
        Assert.Contains("materialised now", first.Output, StringComparison.Ordinal);

        var second = Arbiter.Run("combat-snapshot", fixture, "--out", outDir, "--cache", cacheDir);
        Assert.Contains("cache hit", second.Output, StringComparison.Ordinal);

        // A distinct declared environment receives its own cache entry even when the
        // zero-mod host produces the same state for both names.
        var altered = Path.Combine(outDir, "altered.json");
        var manifest = ManifestJson.Load(fixture);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    Mods = Fact<ModEnvironment>.Declared(
                        manifest.Environment.Mods.Value with { Name = "vanilla-headless-renamed" }),
                },
            },
            altered);

        var third = Arbiter.Run("combat-snapshot", altered, "--out", outDir, "--cache", cacheDir);
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
