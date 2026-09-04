using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Asking for a boundary by its own coordinate, through the commands a person types.
///
/// Every test here runs the real command against the engine-generated whole-act
/// history, because the thing being checked is which boundary comes back: a fight
/// after the first, a floor arrival, a turn of a later fight. A hand-built fixture
/// would prove the selector parses; only a history that really passes sixty-seven
/// boundaries proves the command reaches the one asked for.
///
/// Known gap, the same one <see cref="MigrateManifestTests"/> carries: these need a
/// prepared game, and CI runs the game-free domain filter, so CI never executes them.
/// </summary>
public sealed class BoundarySelectionTests
{
    private static ReplayManifest WholeAct => ManifestJson.Load(Arbiter.WholeAct);

    /// <summary>
    /// A fight after the first is walked to and named. The journey runs through that
    /// fight's predecessors - every card played and turn ended in the fights before it
    /// - and ends at the action the recording's own boundary names.
    /// </summary>
    [GameFact]
    public void EntersAFightAfterTheFirstAndNamesTheBoundaryItReached()
    {
        var declared = WholeAct.BoundaryAt(ReplayBoundary.CombatStartKind, fight: 3)!;

        var report = EnterFight(out var result, "--fight", "3");

        Assert.True(result.Verified, result.All);
        Assert.Equal("the start of fight 3", report.GetProperty("boundary").GetString());
        Assert.Equal(declared.AfterSeq, report.GetProperty("boundary_seq").GetInt32());
        Assert.True(report.GetProperty("boundary_matches").GetBoolean(), result.All);
        Assert.Equal(
            declared.Digest.Value, report.GetProperty("recorded_snapshot_digest").GetString());
        Assert.Equal(
            report.GetProperty("recorded_snapshot_digest").GetString(),
            report.GetProperty("this_game_digest").GetString());
    }

    /// <summary>
    /// A floor arrival is a different place to be stood: the run is on the map and no
    /// fight has started, so what is compared is where it stands.
    /// </summary>
    [GameFact]
    public void StandsAtAFloorArrivalRatherThanInAFight()
    {
        var declared = WholeAct.BoundaryAt(ReplayBoundary.FloorEntryKind, floor: 5)!;

        var report = EnterFight(out var result, "--floor", "5");

        Assert.True(result.Verified, result.All);
        Assert.Equal("arrival on floor 5", report.GetProperty("boundary").GetString());
        Assert.Equal(declared.AfterSeq, report.GetProperty("boundary_seq").GetInt32());
        Assert.True(report.GetProperty("boundary_matches").GetBoolean(), result.All);
        Assert.Contains(
            report.GetProperty("comparisons").EnumerateArray(),
            comparison => comparison.GetProperty("field").GetString() == "run.map_coord");
    }

    /// <summary>
    /// The floor a run starts on was never arrived at, so no boundary was derived
    /// there. It is refused in words rather than entered at whatever came first.
    /// </summary>
    [GameFact]
    public void RefusesAFloorTheRecordingNeverArrivedOn()
    {
        var result = Arbiter.Run("enter-fight", Arbiter.WholeAct, "--floor", "1");

        Assert.False(result.Verified);
        Assert.Contains(
            "declares no floor-entry boundary for floor 1", result.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every kind of boundary, snapshotted by its own coordinate. What is proved is
    /// that the replay re-derived the boundary asked for: the digest it produced there
    /// is the one the recording declares at that exact coordinate, and a different
    /// boundary would carry a different digest.
    /// </summary>
    [GameTheory]
    [InlineData("combat_start:2")]
    [InlineData("floor_entry:7")]
    [InlineData("turn_start:2.3")]
    public void SnapshotsTheBoundaryAskedForByItsOwnCoordinate(string coordinate)
    {
        var declared = BoundarySelector.Parse(coordinate).In(WholeAct.Boundaries)!;
        var outDir = Path.Combine("build", "test-scratch", $"boundary-snapshot-{Guid.NewGuid():N}");

        var result = Arbiter.Run(
            "combat-snapshot", Arbiter.WholeAct, "--boundary", coordinate,
            "--out", outDir, "--cache", Path.Combine(outDir, "snapshots"));

        Assert.True(result.Verified, result.All);
        var report = Report(Path.Combine(Arbiter.RepoRoot, outDir, "combat-snapshot.json"), result);
        Assert.Equal(coordinate, report.GetProperty("boundary").GetString());
        Assert.Equal(declared.AfterSeq, report.GetProperty("boundary_seq").GetInt32());
        Assert.Equal(declared.Digest.Value, report.GetProperty("snapshot_digest").GetString());
        Assert.True(report.GetProperty("restore_verified").GetBoolean());
    }

    /// <summary>
    /// A coordinate this history does not pass is refused, and the refusal lists the
    /// boundaries it does pass in the form somebody can paste back.
    /// </summary>
    [GameFact]
    public void RefusesACoordinateTheHistoryNeverReachesAndSaysWhatItDoesPass()
    {
        var beyond = WholeAct.Boundaries.Where(boundary => boundary.IsCombatStart).Max(b => b.Fight!.Value) + 1;
        var outDir = Path.Combine("build", "test-scratch", $"boundary-snapshot-{Guid.NewGuid():N}");

        var result = Arbiter.Run(
            "combat-snapshot", Arbiter.WholeAct, "--boundary", $"combat_start:{beyond}",
            "--out", outDir, "--cache", Path.Combine(outDir, "snapshots"));

        Assert.False(result.Verified);
        Assert.Contains($"reaches no combat_start:{beyond}", result.All, StringComparison.Ordinal);
        Assert.Contains("combat_start:1", result.All, StringComparison.Ordinal);
        Assert.Contains("turn_start:1.1", result.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deriving boundaries writes what the replay reached and nothing else. The
    /// manifest handed in carries one boundary, the way a version-4 recording did; what
    /// comes back is every boundary the engine passed, at the coordinates and with the
    /// digests the committed history independently records.
    /// </summary>
    [GameFact]
    public void DerivesEveryBoundaryTheReplayReached()
    {
        var complete = WholeAct;
        var inPath = Scratch("in.replay.json");
        var outPath = Path.Combine(Path.GetDirectoryName(inPath)!, "out.replay.json");
        ManifestJson.Save(
            complete with
            {
                Boundaries = [complete.BoundaryAt(ReplayBoundary.CombatStartKind, fight: 1)!],
            },
            inPath);

        var result = Arbiter.Run("migrate-manifest", inPath, "--derive-boundaries", "--out", outPath);

        Assert.True(result.Verified, result.All);
        Assert.Equal(Coordinates(complete), Coordinates(ManifestJson.Load(outPath)));
    }

    /// <summary>
    /// A declared boundary this build derives differently is the finding, so the
    /// rewrite refuses rather than replacing the older digest with today's.
    /// </summary>
    [GameFact]
    public void RefusesToRewriteABoundaryThisBuildDerivesDifferently()
    {
        var complete = WholeAct;
        var drifted = complete.BoundaryAt(ReplayBoundary.CombatStartKind, fight: 2)!;
        var inPath = Scratch("drifted.replay.json");
        ManifestJson.Save(
            complete with
            {
                Boundaries = [.. complete.Boundaries.Select(boundary =>
                    boundary == drifted
                        ? boundary with { Digest = Fact<string>.Engine("sha256:" + new string('b', 64)) }
                        : boundary)],
            },
            inPath);

        var result = Arbiter.Run("migrate-manifest", inPath, "--derive-boundaries", "--out", Scratch("out.json"));

        Assert.False(result.Verified);
        Assert.Contains(drifted.Describe(), result.All, StringComparison.Ordinal);
        Assert.Contains("erase the evidence", result.All, StringComparison.Ordinal);
    }

    /// <summary>Every boundary as the coordinate that names it and the digest at it,
    /// ordered so two lists can be compared without depending on the order either was
    /// written in.</summary>
    private static IEnumerable<string> Coordinates(ReplayManifest manifest) =>
        manifest.Boundaries
            .Select(boundary =>
                $"{boundary.Kind}|{boundary.Fight}|{boundary.Floor}|{boundary.Turn}|" +
                $"{boundary.AfterSeq}|{boundary.Digest.Value}")
            .Order(StringComparer.Ordinal);

    private static string Scratch(string name)
    {
        var directory = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"boundary-migrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }

    private static JsonElement EnterFight(out Arbiter.Result result, params string[] extra)
    {
        var outDir = Path.Combine("build", "test-scratch", $"boundary-entry-{Guid.NewGuid():N}");
        result = Arbiter.Run(["enter-fight", Arbiter.WholeAct, "--out", outDir, .. extra]);
        return Report(Path.Combine(Arbiter.RepoRoot, outDir, "enter-fight.json"), result);
    }

    private static JsonElement Report(string path, Arbiter.Result result)
    {
        Assert.True(File.Exists(path), result.All);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }
}
