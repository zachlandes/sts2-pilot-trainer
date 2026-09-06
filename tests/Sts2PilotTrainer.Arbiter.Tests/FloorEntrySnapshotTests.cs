using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one boundary this project stores as a serialized run, and everything that has to
/// hold before it does.
///
/// These assert the conditions rather than the answer. Whether the game's own save
/// restores at a floor arrival is a measurement, and a test that demanded agreement
/// would have to be edited the day the engine changed its mind - which is precisely when
/// somebody should be reading the result instead. What they assert is that nothing
/// reaches the cache unverified, that the arrival kinds the measurement did not cover
/// are refused by a rule rather than noticed later, that restoring and walking arrive at
/// the same boundary, and that another floor's save is refused at this one.
/// </summary>
public sealed class FloorEntrySnapshotTests
{
    /// <summary>Arrivals of the whole-act history where a fight is live at the arrival,
    /// and one where the previous fight has already been won. The committed history with
    /// more than one of each, which is what this needs.</summary>
    private const string LiveCombatFloor = "5";

    private const string WonFightFloor = "4";

    [GameFact]
    public void CachesAFloorArrivalOnlyOnceARestoreHasReproducedIt()
    {
        var outDir = TempDir();
        var cache = TempDir();

        var result = Arbiter.Run(
            "floor-snapshot", Arbiter.WholeAct, "--floor", LiveCombatFloor,
            "--cache", cache, "--out", outDir);

        Assert.True(result.Verified, result.All);
        var report = Report(outDir, "floor-snapshot.json");

        // The verdict has to be the one its own numbers support: the replay reproduced
        // what the recording declares, the restore reproduced what the replay produced,
        // and both states carried the act's generated content.
        Assert.Empty(report.GetProperty("refusals").EnumerateArray());
        Assert.True(report.GetProperty("cached").GetBoolean());
        Assert.True(report.GetProperty("boundary_matches").GetBoolean());
        Assert.Equal("true", report.GetProperty("live_combat_at_arrival").GetString());
        Assert.Equal(
            report.GetProperty("declared_digest").GetString(),
            report.GetProperty("replayed_digest").GetString());
        Assert.Equal(
            report.GetProperty("declared_digest").GetString(),
            report.GetProperty("restored_digest").GetString());
        Assert.True(report.GetProperty("replayed_act_room_set").GetProperty("present").GetBoolean());
        Assert.True(report.GetProperty("restored_act_room_set").GetProperty("present").GetBoolean());

        // The save kept is the one taken on arriving at a floor, not the one taken on
        // leaving a room. Only the first is a floor-entry snapshot: the second carries a
        // finished room, which stops the engine generating the arrival's own.
        Assert.Equal("none", report.GetProperty("save_pre_finished_room").GetString());

        // Both files, and the metadata's key is the key of this history's prefix.
        var directory = Directory.EnumerateDirectories(cache).Single();
        Assert.True(File.Exists(Path.Combine(directory, "run-save.json")));
        var snapshot = JsonDocument
            .Parse(File.ReadAllText(Path.Combine(directory, "floor-entry-snapshot.json"))).RootElement;
        Assert.Equal(
            report.GetProperty("restored_digest").GetString(),
            snapshot.GetProperty("verified_digest").GetString());
        Assert.Equal(
            report.GetProperty("snapshot_key").GetProperty("ActionHistoryHash").GetString(),
            snapshot.GetProperty("key").GetProperty("ActionHistoryHash").GetString());

        // The quarantine the candidate was written into is gone, so an unverified save
        // is never left where anything could find it.
        Assert.Empty(Directory.EnumerateDirectories(outDir, ".floor-snapshot.*"));
    }

    /// <summary>
    /// The refusal the whole scope rests on. An arrival with a fight already won is the
    /// same run in every other respect - seed, stream positions, deck, gold, coordinate -
    /// and the live run still carries the finished fight that the game's own save has no
    /// representation of.
    /// </summary>
    [GameFact]
    public void RefusesAnArrivalWhereTheOnlyCombatIsAFightThatAlreadyEnded()
    {
        var outDir = TempDir();
        var cache = TempDir();

        var result = Arbiter.Run(
            "floor-snapshot", Arbiter.WholeAct, "--floor", WonFightFloor,
            "--cache", cache, "--out", outDir);

        Assert.False(result.Verified, result.All);
        Assert.Contains("No fight is live at this arrival", result.All, StringComparison.Ordinal);

        var report = Report(outDir, "floor-snapshot.json");
        Assert.False(report.GetProperty("cached").GetBoolean());
        Assert.NotEmpty(report.GetProperty("refusals").EnumerateArray());

        // Nothing was written. A refusal that still left a save behind would be a cache
        // somebody could later find and trust.
        Assert.Empty(Directory.EnumerateFileSystemEntries(cache));
    }

    /// <summary>
    /// The falsification control, and the reason to believe the test above. Restoring
    /// another floor's save has to be refused at this boundary; if it were not, the
    /// comparison would discriminate nothing and no snapshot could be trusted on it.
    /// </summary>
    [GameFact]
    public void RefusesAnotherFloorsSaveAtThisArrival()
    {
        var outDir = TempDir();
        var cache = TempDir();

        var result = Arbiter.Run(
            "floor-snapshot", Arbiter.WholeAct, "--floor", LiveCombatFloor,
            "--cache", cache, "--out", outDir, "--control", "wrong-floor");

        Assert.True(result.Verified, result.All);
        Assert.Contains("CONTROL HELD", result.All, StringComparison.Ordinal);

        var report = Report(outDir, "floor-snapshot.control-wrong-floor.json");
        Assert.False(report.GetProperty("boundary_matches").GetBoolean());
        Assert.False(report.GetProperty("cached").GetBoolean());
        Assert.True(report.GetProperty("observed_values_disagreeing").GetInt32() > 0);
        Assert.Contains(
            "run.total_floor", report.GetProperty("boundary_refusal").GetString()!,
            StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(cache));
    }

    /// <summary>
    /// The whole point of the cache: the same boundary, reached from bytes rather than
    /// by replaying the decisions that produced them, proved the same way.
    ///
    /// Both digests are read out of the two runs' own reports rather than pinned here,
    /// so this asserts that the two routes agree rather than that they agree on a value
    /// a test author wrote down.
    /// </summary>
    [GameFact]
    public void RestoringReachesTheSameBoundaryAsWalkingToIt()
    {
        var cache = TempDir();
        var materialise = TempDir();
        var walked = TempDir();
        var restored = TempDir();

        Assert.True(
            Arbiter.Run(
                "floor-snapshot", Arbiter.WholeAct, "--floor", LiveCombatFloor,
                "--cache", cache, "--out", materialise).Verified);

        var byWalking = Arbiter.Run(
            "enter-fight", Arbiter.WholeAct, "--floor", LiveCombatFloor,
            "--cache", cache, "--out", walked);
        var byRestoring = Arbiter.Run(
            "enter-fight", Arbiter.WholeAct, "--floor", LiveCombatFloor, "--restore",
            "--cache", cache, "--out", restored);

        Assert.True(byWalking.Verified, byWalking.All);
        Assert.True(byRestoring.Verified, byRestoring.All);

        var walkedReport = Report(walked, "enter-fight.json");
        var restoredReport = Report(restored, "enter-fight.json");

        Assert.Equal("replayed", walkedReport.GetProperty("entry_route").GetString());
        Assert.Equal("restored", restoredReport.GetProperty("entry_route").GetString());
        Assert.True(walkedReport.GetProperty("boundary_matches").GetBoolean());
        Assert.True(restoredReport.GetProperty("boundary_matches").GetBoolean());
        Assert.Equal(
            walkedReport.GetProperty("this_game_digest").GetString(),
            restoredReport.GetProperty("this_game_digest").GetString());
        Assert.Equal(
            walkedReport.GetProperty("recorded_snapshot_digest").GetString(),
            restoredReport.GetProperty("recorded_snapshot_digest").GetString());

        // Restoring makes every decision at once rather than skipping any: the plan is
        // the same plan and its steps are all made.
        Assert.Equal(
            walkedReport.GetProperty("boundary_seq").GetInt32(),
            restoredReport.GetProperty("boundary_seq").GetInt32());
        Assert.Empty(restoredReport.GetProperty("steps").EnumerateArray());

        // And it still writes nothing, which is the claim the walked route also makes.
        Assert.True(restoredReport.GetProperty("profile_unchanged").GetBoolean());
    }

    /// <summary>
    /// Asking for the cache where there is none is an ordinary thing rather than a
    /// defect: the row replays instead, and says which happened.
    /// </summary>
    [GameFact]
    public void ReplaysWhenNoSnapshotHasBeenMaterialised()
    {
        var outDir = TempDir();

        var result = Arbiter.Run(
            "enter-fight", Arbiter.WholeAct, "--floor", LiveCombatFloor, "--restore",
            "--cache", TempDir(), "--out", outDir);

        Assert.True(result.Verified, result.All);
        var report = Report(outDir, "enter-fight.json");
        Assert.Equal("replayed", report.GetProperty("entry_route").GetString());
        Assert.Contains(
            "no floor-entry snapshot has been materialised",
            report.GetProperty("entry_source").GetString()!, StringComparison.Ordinal);
        Assert.True(report.GetProperty("boundary_matches").GetBoolean());
    }

    /// <summary>
    /// A snapshot answers for one exact prefix of one exact recording and for nothing
    /// else. A recording damaged anywhere in that prefix hashes to a different history,
    /// so the snapshot cached for the undamaged one is not offered for it - which is
    /// checked here by reading the key the damaged run looked under and finding it is
    /// not the key the snapshot was filed at.
    ///
    /// Nothing is asserted about whether the damaged history then reaches the recorded
    /// boundary. Reordering two plays of the same card at the same target is state-
    /// neutral by the time this arrival comes round, and that is a fact about this
    /// control rather than about the cache; what a damaged history reaches is the
    /// arbiter's question and negative-controls is where it is asked.
    /// </summary>
    [GameFact]
    public void DoesNotAnswerForARecordingWhoseHistoryHasBeenDamaged()
    {
        var cache = TempDir();
        var materialise = TempDir();
        var outDir = TempDir();

        Assert.True(
            Arbiter.Run(
                "floor-snapshot", Arbiter.WholeAct, "--floor", LiveCombatFloor,
                "--cache", cache, "--out", materialise).Verified);
        var cached = Path.GetFileName(Directory.EnumerateDirectories(cache).Single());

        Arbiter.Run(
            "enter-fight", Arbiter.WholeAct, "--floor", LiveCombatFloor, "--restore",
            "--control", "reorder-plays", "--cache", cache, "--out", outDir);

        var report = Report(outDir, "enter-fight.json");
        var source = report.GetProperty("entry_source").GetString()!;
        Assert.Equal("replayed", report.GetProperty("entry_route").GetString());
        Assert.Contains("no floor-entry snapshot has been materialised", source, StringComparison.Ordinal);
        Assert.DoesNotContain(cached, source, StringComparison.Ordinal);
    }

    [GameFact]
    public void RefusesToSnapshotAFightsBoundary()
    {
        var result = Arbiter.Run("floor-snapshot", Arbiter.WholeAct, "--fight", "2");

        Assert.False(result.Verified);
        Assert.Contains("a fight's start is not one", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void RefusesToSnapshotAFloorTheRecordingDidNotArriveOn()
    {
        var result = Arbiter.Run(
            "floor-snapshot", Arbiter.WholeAct, "--floor", "99", "--out", TempDir());

        Assert.False(result.Verified);
        Assert.Contains("declares no floor-entry boundary for floor 99", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void RefusesToStopAfterOneDecisionWhenThereAreNoneLeftToMake()
    {
        var result = Arbiter.Run(
            "enter-fight", Arbiter.WholeAct, "--floor", LiveCombatFloor, "--restore", "--step",
            "--out", TempDir());

        Assert.False(result.Verified);
        Assert.Contains("no decisions left to stop after", result.All, StringComparison.Ordinal);
    }

    [GameTheory]
    [InlineData("--floor")]
    [InlineData("--cache")]
    [InlineData("--control")]
    [InlineData("--out")]
    public void RefusesAValueOptionWithoutAValueBeforeCreatingOutput(string option)
    {
        var outDir = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"floor-snapshot-missing-{Guid.NewGuid():N}");
        var args = option == "--out"
            ? new[] { "floor-snapshot", "missing.replay.json", option }
            : new[] { "floor-snapshot", "missing.replay.json", "--out", outDir, option };

        var result = Arbiter.Run(args);

        Assert.False(result.Verified);
        Assert.Contains($"Option {option} requires a value", result.All, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    private static JsonElement Report(string outDir, string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, fileName))).RootElement.Clone();

    private static string TempDir()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"floor-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
