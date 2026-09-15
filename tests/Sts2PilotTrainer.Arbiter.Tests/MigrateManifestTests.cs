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
        node["manifest_version"] = ManifestJson.OldestMigratedVersion;

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
            ManifestJson.OldestMigratedVersion, native.GetProperty("migrated_from_version").GetInt32());

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
    /// nothing else but what every older version loses: integrity is a claim a native
    /// recorder makes about what it watched, and a reconstruction from footage has no
    /// recorder to make it, and what a format-6 checkpoint expected of a finished
    /// fight is taken away by the reader the way it is for any older file.</summary>
    [GameFact]
    public void AVersionFiveVideoManifestGainsTheVersionAndNoIntegrity()
    {
        var node = AsWrittenIn(JsonNode.Parse(File.ReadAllText(Arbiter.Manifest))!.AsObject(), ManifestJson.OldestMigratedVersion);
        var input = Path.Combine(ScratchDirectory(), "version-five-video.replay.json");
        File.WriteAllText(input, node.ToJsonString() + "\n");
        var outPath = ScratchPath();

        var result = Arbiter.Run("migrate-manifest", input, "--out", outPath);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("integrity:", result.All, StringComparison.Ordinal);
        Assert.Equal(
            ManifestJson.Serialize(FinishedFightResidue.ReadFromOlderFormat(
                ManifestJson.Load(Arbiter.Manifest), ManifestJson.OldestMigratedVersion)) + "\n",
            File.ReadAllText(outPath));
    }

    /// <summary>
    /// The two directions the file-level reading got wrong, on the video manifest.
    /// Migrated without a replay, the file is in the current format and still carries
    /// its format-6 arrival digest, and each boundary says so: the one repair the
    /// format promises still re-derives exactly that arrival from the verified replay.
    /// Once it has, every boundary is a claim in this projection, and a digest that
    /// disagrees at that same arrival is the finding and is refused.
    /// </summary>
    [GameFact]
    public void AnOlderArrivalDigestIsStillRederivedAfterAPlainMigrationAndNeverAgainAfterAReplay()
    {
        var committed = ManifestJson.Load(Arbiter.Manifest);
        var arrival = committed.Boundaries
            .First(boundary => FinishedFightResidue.PredatesThisProjection(committed, boundary with { Projection = 6 }));
        var stale = "sha256:" + new string('6', 64);

        var versionSix = Path.Combine(ScratchDirectory(), "version-six-video.replay.json");
        File.WriteAllText(versionSix, WithDigest(
            AsWrittenIn(JsonNode.Parse(File.ReadAllText(Arbiter.Manifest))!.AsObject(), ManifestJson.FinishedFightResidueManifestVersion),
            arrival, stale).ToJsonString() + "\n");

        var plain = ScratchPath();
        Assert.Equal(0, Arbiter.Run("migrate-manifest", versionSix, "--out", plain).ExitCode);
        var rewritten = ManifestJson.Load(plain);
        Assert.Equal(ReplayManifest.CurrentManifestVersion, rewritten.ManifestVersion);
        Assert.All(rewritten.Boundaries, boundary => Assert.Equal(ManifestJson.FinishedFightResidueManifestVersion, boundary.Projection));
        Assert.Equal(stale, rewritten.BoundaryAt(arrival.Kind, floor: arrival.Floor)!.Digest.Value);

        var derived = ScratchPath();
        var result = Arbiter.Run("migrate-manifest", plain, "--out", derived, "--derive-boundaries");
        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains($"rederived: {arrival.Describe()}", result.All, StringComparison.Ordinal);
        var verified = ManifestJson.Load(derived);
        Assert.All(committed.Boundaries, declared =>
            Assert.Equal(declared.Digest.Value, verified.BoundaryAt(
                declared.Kind, fight: declared.Fight, floor: declared.Floor, turn: declared.Turn)!.Digest.Value));
        Assert.All(verified.Boundaries, boundary => Assert.Equal(CanonicalState.Projection, boundary.Projection));

        var staleAgain = Path.Combine(ScratchDirectory(), "stale-again.replay.json");
        File.WriteAllText(staleAgain, WithDigest(
            JsonNode.Parse(File.ReadAllText(derived))!.AsObject(), arrival, stale).ToJsonString() + "\n");
        var refused = Arbiter.Run("migrate-manifest", staleAgain, "--out", ScratchPath(), "--derive-boundaries");
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("Overwriting the older digest would erase the evidence", refused.All, StringComparison.Ordinal);
        Assert.DoesNotContain("rederived:", refused.All, StringComparison.Ordinal);
    }

    /// <summary>The manifest as a file of that older version, with fields introduced
    /// after it removed rather than merely hidden behind an older version number.</summary>
    private static JsonObject AsWrittenIn(JsonObject node, int version)
    {
        node["manifest_version"] = version;
        if (version < 7)
        {
            foreach (var boundary in node["boundaries"]!.AsArray())
            {
                boundary!.AsObject().Remove("projection");
            }
        }

        if (version < PatchRoster.RunEndIntroducedInManifestVersion)
        {
            node["environment"]?["mods"]?["Value"]?["patch_roster"]?.AsObject().Remove("run_end");
        }
        return node;
    }

    private static JsonObject WithDigest(JsonObject node, ReplayBoundary boundary, string digest)
    {
        foreach (var candidate in node["boundaries"]!.AsArray())
        {
            var entry = candidate!.AsObject();
            if (entry["kind"]!.GetValue<string>() == boundary.Kind &&
                entry["after_seq"]!.GetValue<int>() == boundary.AfterSeq)
            {
                entry["digest"]!.AsObject()["Value"] = digest;
            }
        }
        return node;
    }

    /// <summary>
    /// The migration format 7 needs, done on disk.
    ///
    /// A format-6 recording's digest at a floor arrival with no live fight after a
    /// fight hashes the finished fight its projection carried, which this build never
    /// produces, so no replay can reproduce it and holding the file to it would refuse
    /// every such recording for a difference that is the format's. With
    /// <c>--derive-boundaries</c> exactly those digests are re-derived from the replay
    /// that verified everything else; every other kind of boundary is still held to
    /// what it declares, so a recording that really disagrees with this build is still
    /// refused rather than smoothed over.
    /// </summary>
    [GameFact]
    public void AVersionSixRecordingsNonFightArrivalsAreRederivedAndNothingElseIs()
    {
        var committed = ManifestJson.Load(Path.Combine(
            Arbiter.RepoRoot, "manifests", "native-9F8CY60C5BK7-20260906-005737.replay.json"));
        var arrival = committed.Boundaries.Single(boundary => boundary.IsFloorEntry && boundary.Floor == 4);
        var fight = committed.Boundaries.First(boundary => boundary.IsCombatStart);
        Assert.True(FinishedFightResidue.PredatesThisProjection(committed, arrival with { Projection = 6 }));

        var stale = "sha256:" + new string('6', 64);
        var input = AVersionSixNativeManifest(arrival, stale);
        var outPath = ScratchPath();

        var result = Arbiter.Run("migrate-manifest", input, "--out", outPath, "--derive-boundaries");

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains($"rederived: {arrival.Describe()}", result.All, StringComparison.Ordinal);
        var migrated = ManifestJson.Load(outPath);
        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        // What it records is where the file began, which was 5, through every migration since.
        Assert.Equal(ManifestJson.OldestMigratedVersion, migrated.Source.Native!.MigratedFromVersion);
        Assert.Equal(
            committed.Boundaries.Select(boundary => (boundary.Describe(), boundary.Digest.Value)),
            migrated.Boundaries.Select(boundary => (boundary.Describe(), boundary.Digest.Value)));
        Assert.Equal(FactSource.Engine, migrated.Boundaries.Single(boundary => boundary.IsFloorEntry && boundary.Floor == 4).Digest.Source);
        Assert.Equal(FactSource.Captured, migrated.Boundaries.First(boundary => boundary.IsCombatStart).Digest.Source);
        // Every digest the replay verified is this projection's now, the kept ones included.
        Assert.All(migrated.Boundaries, boundary => Assert.Equal(CanonicalState.Projection, boundary.Projection));

        // A combat start is read inside a live fight, whose projection did not
        // change, so a digest that disagrees there is the finding and is kept.
        var refused = Arbiter.Run(
            "migrate-manifest", AVersionSixNativeManifest(fight, stale), "--out", ScratchPath(), "--derive-boundaries");
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("Overwriting the older digest would erase the evidence", refused.All, StringComparison.Ordinal);
    }

    /// <summary>The shipped native recording as the version-6 file it was before this
    /// format bump, holding the given boundary with the given digest: what a format-6
    /// arbiter left on disk. Its own migration note stays, because the recorder that
    /// wrote it read no option keys and the note is what excuses that.</summary>
    private static string AVersionSixNativeManifest(ReplayBoundary boundary, string digest)
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "manifests", "native-9F8CY60C5BK7-20260906-005737.replay.json");
        var node = WithDigest(
            AsWrittenIn(JsonNode.Parse(File.ReadAllText(path))!.AsObject(), ManifestJson.FinishedFightResidueManifestVersion),
            boundary, digest);

        var scratch = Path.Combine(ScratchDirectory(), $"version-six-{boundary.Kind}-{boundary.AfterSeq}.replay.json");
        File.WriteAllText(scratch, node.ToJsonString() + "\n");
        return scratch;
    }
}
