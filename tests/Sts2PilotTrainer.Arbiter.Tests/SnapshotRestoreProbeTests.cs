using System.Text.Json;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The measurement that decides whether a boundary can be cached as a serialized run.
///
/// These do not assert which answer the game gives - that is the thing being measured,
/// and a test that demanded agreement would have to be edited the day the engine
/// changed its mind, which is precisely when someone should be reading the result
/// instead. They assert that the answer is one the probe was entitled to give: both
/// states carried the act's generated content, the verdict follows from the digests
/// the probe itself reported, and a pair of states that agree while carrying nothing
/// is refused rather than reported as agreement.
/// </summary>
public class SnapshotRestoreProbeTests
{
    [GameTheory]
    [InlineData("--control")]
    [InlineData("--out")]
    [InlineData("--phase")]
    [InlineData("--save")]
    [InlineData("--stop-after")]
    public void RefusesAValueOptionWithoutAValueBeforeCreatingOutput(string option)
    {
        var outDir = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"missing-option-{Guid.NewGuid():N}");
        var args = option == "--out"
            ? new[] { "snapshot-restore-probe", "missing.replay.json", option }
            : new[] { "snapshot-restore-probe", "missing.replay.json", "--out", outDir, option };

        var result = Arbiter.Run(args);

        Assert.False(result.Verified);
        Assert.Contains($"Option {option} requires a value", result.All, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [GameFact]
    public void RefusesAValueOptionFollowedByAnotherFlagBeforeCreatingOutput()
    {
        var outDir = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"missing-option-{Guid.NewGuid():N}");

        var result = Arbiter.Run(
            "snapshot-restore-probe", "missing.replay.json", "--control", "--out", outDir);

        Assert.False(result.Verified);
        Assert.Contains("Option --control requires a value", result.All, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [GameFact]
    public void MeasuresTheSaveRoundTripAgainstStatesThatBothCarryTheActRoomSet()
    {
        var outDir = TempDir();
        var fixturePath = Arbiter.SyntheticReplayFixture();

        var result = Arbiter.Run("snapshot-restore-probe", fixturePath, "--out", outDir);

        Assert.True(result.Verified, result.All);
        var report = Report(outDir, "snapshot-restore-probe.json");

        // The false agreement this probe must never produce: act.room_set degrades to
        // "unavailable" when the engine's private _rooms cannot be read, and two states
        // that both degraded would agree on the sentinel while saying nothing about the
        // run. Asserted on both sides and at every restore stage, not on the summary
        // flag alone, so a flag that stopped being computed could not pass this.
        Assert.True(report.GetProperty("room_set_readable_on_both_sides").GetBoolean());
        Assert.True(report.GetProperty("replayed_act_room_set").GetProperty("present").GetBoolean());
        Assert.NotEmpty(report.GetProperty("stages").EnumerateArray());
        foreach (var stage in report.GetProperty("stages").EnumerateArray())
        {
            Assert.True(stage.GetProperty("restored_act_room_set").GetProperty("present").GetBoolean());
        }

        // Nothing was refused, so the probe answered rather than declining to.
        Assert.Empty(report.GetProperty("refusals").EnumerateArray());

        // Whatever the answer is, it has to be the answer its own numbers support: a
        // stage's digests agree exactly when none of its fields differ, and only the
        // terminal stage decides whether the retail restore is usable.
        var replayedDigest = report.GetProperty("replayed_digest").GetString();
        JsonElement? terminal = null;
        foreach (var stage in report.GetProperty("stages").EnumerateArray())
        {
            var agree = stage.GetProperty("digests_agree").GetBoolean();
            Assert.Equal(agree, stage.GetProperty("restored_digest").GetString() == replayedDigest);
            Assert.Equal(agree, stage.GetProperty("differing_fields").GetArrayLength() == 0);
            if (stage.GetProperty("stage").GetString() == "room_re_entered") terminal = stage;
        }

        Assert.True(terminal.HasValue);
        Assert.Equal(
            terminal.Value.GetProperty("digests_agree").GetBoolean(),
            report.GetProperty("restorable").GetBoolean());
        Assert.Contains("terminal stage 'room_re_entered'", report.GetProperty("answer").GetString());

        // The restore has to have started from the bytes the replay wrote and finished
        // the sequence it says it ran; a step that threw would leave a half-restored run
        // whose state is neither side's.
        Assert.Equal(
            report.GetProperty("save_sha256").GetString(),
            Report(outDir, "snapshot-restore-probe.restore.json").GetProperty("save_sha256").GetString());
        Assert.True(File.Exists(Path.Combine(outDir, "snapshot-restore-probe.capture.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "snapshot-restore-probe.run-save.json")));
        Assert.Empty(Directory.EnumerateDirectories(outDir, ".snapshot-restore-probe.*"));
        foreach (var step in report.GetProperty("restore_steps").EnumerateArray())
        {
            Assert.Equal("ran", step.GetProperty("outcome").GetString());
        }
    }

    [GameFact]
    public void ConcurrentProbesPublishOneCoherentArtifactSet()
    {
        var outDir = TempDir();
        var firstManifest = Fixture("concurrent-probe-first");
        var secondManifest = Fixture("concurrent-probe-second");

        var first = Task.Run(() =>
            Arbiter.Run("snapshot-restore-probe", firstManifest, "--out", outDir));
        var second = Task.Run(() =>
            Arbiter.Run("snapshot-restore-probe", secondManifest, "--out", outDir));
        Task.WaitAll(first, second);

        Assert.True(first.Result.Verified, first.Result.All);
        Assert.True(second.Result.Verified, second.Result.All);
        var report = Report(outDir, "snapshot-restore-probe.json");
        var capture = Report(outDir, "snapshot-restore-probe.capture.json");
        var restoration = Report(outDir, "snapshot-restore-probe.restore.json");
        Assert.Equal(report.GetProperty("run_id").GetString(), capture.GetProperty("run_id").GetString());
        Assert.Equal(report.GetProperty("save_sha256").GetString(), capture.GetProperty("save_sha256").GetString());
        Assert.Equal(report.GetProperty("save_sha256").GetString(), restoration.GetProperty("save_sha256").GetString());
        Assert.Empty(Directory.EnumerateDirectories(outDir, ".snapshot-restore-probe.*"));
    }

    /// <summary>
    /// The negative control, and the reason to believe the test above.
    ///
    /// Both states are damaged into the same unreadable room-set reading, which makes
    /// their digests agree by construction. That is the shape of a false agreement, and
    /// the probe must refuse it rather than call it a restorable boundary.
    /// </summary>
    [GameFact]
    public void RefusesTwoStatesThatAgreeBecauseNeitherCarriesTheActRoomSet()
    {
        var outDir = TempDir();
        var fixturePath = Arbiter.SyntheticReplayFixture();

        var result = Arbiter.Run(
            "snapshot-restore-probe", fixturePath, "--out", outDir,
            "--control", "unreadable-room-set");

        Assert.True(result.Verified, result.All);
        var report = Report(outDir, "snapshot-restore-probe.control-unreadable-room-set.json");

        Assert.False(report.GetProperty("room_set_readable_on_both_sides").GetBoolean());
        Assert.False(report.GetProperty("restorable").GetBoolean());
        Assert.NotEmpty(report.GetProperty("refusals").EnumerateArray());

        // The digests do agree. That is what makes this a control rather than a
        // different measurement: the guard has to fire on agreement, not instead of it.
        var replayedDigest = report.GetProperty("replayed_digest").GetString();
        foreach (var stage in report.GetProperty("stages").EnumerateArray())
        {
            Assert.Equal(replayedDigest, stage.GetProperty("restored_digest").GetString());
            Assert.True(stage.GetProperty("digests_agree").GetBoolean());
        }

        Assert.Contains("_rooms", string.Join(" ", report.GetProperty("refusals")
            .EnumerateArray().Select(refusal => refusal.GetString())), StringComparison.Ordinal);
    }

    private static JsonElement Report(string outDir, string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, fileName))).RootElement.Clone();

    private static string Fixture(string runId)
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"{runId}-{Guid.NewGuid():N}.replay.json");
        Sts2PilotTrainer.Replay.ManifestJson.Save(
            Sts2PilotTrainer.Replay.SyntheticReplayFixture.Create() with { RunId = runId }, path);
        return path;
    }

    private static string TempDir()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"snapshot-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
