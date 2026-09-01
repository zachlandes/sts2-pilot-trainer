using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Checks on the manifest this repository actually ships, as opposed to a fixture.
/// These need no game: they read the file and apply the same rules the arbiter does.
/// </summary>
public class ShippedManifestTests
{
    private static ReplayManifest Manifest => ManifestJson.Load(Arbiter.Manifest);

    [Fact]
    public void PassesEveryStructuralRule()
    {
        var result = ManifestValidator.Validate(Manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void ShippedMapObservationPassesIngestion()
    {
        var observation = MapObservation.Load(Arbiter.MapObservation);

        Assert.NotEmpty(observation.Nodes);
    }

    [Fact]
    public void ShowsTheRecordingBeginsAtTheRunsStart()
    {
        // The specific defence against a run resumed from history. One of the three
        // mods in this creator's environment does exactly that, and a resumed run
        // matches on seed, build, content hash and acts.
        var start = Manifest.Source.RunStart;
        Assert.NotNull(start);
        Assert.False(start.EnteredFromRunHistory.Value);
        Assert.False(start.ResumeModalSeen.Value);
        Assert.Equal(1, start.FirstObservedFloor.Value);
        Assert.InRange(start.FirstObservedRunTimeSeconds.Value, 0, RunStartEvidence.MaxRunTimeSecondsAtStart);
    }

    [Fact]
    public void ReadsTheEnvironmentAgainFromTheOtherEndOfTheRecording()
    {
        var summary = Manifest.Source.RunSummary;
        Assert.NotNull(summary);

        // Far apart on purpose: two readings that agree across most of an hour of
        // footage cannot both be one drifted glance, and a recording spliced from two
        // runs could not agree at both ends.
        var firstReadingMs = Manifest.Environment.Seed.Evidence!.VideoTimeMs!.Value;
        Assert.True(
            summary.VideoTimeMs - firstReadingMs > 1_000_000,
            "the two environment readings are too close together to corroborate each other");

        Assert.Equal(Manifest.Environment.Seed.Value, summary.Seed.Value);
        Assert.Equal(Manifest.Environment.ContentHash.Value, summary.ContentHash.Value);
        Assert.NotEmpty(summary.NotShown);
    }

    /// <summary>
    /// The comparison contract computes over a finished fight, so a manifest whose
    /// history stopped mid-combat could never be one side of a comparison. The gate
    /// computes this from a real replay; this reads it off the manifest, needs no
    /// game, and so runs everywhere.
    /// </summary>
    [Fact]
    public void CoversTheFirstCombatThroughItsEnd()
    {
        var manifest = Manifest;
        var last = manifest.Actions[^1];
        var final = Assert.Single(manifest.Checkpoints, c => c.AfterSeq == last.Seq);

        Assert.Equal("combat_end", final.Kind);
        Assert.Equal("victory", final.Expect["combat.outcome"].Value);
        Assert.Equal("false", final.Expect["combat.in_progress"].Value);
        Assert.All(final.Expect.Values, fact => Assert.Equal(FactSource.Observed, fact.Source));
    }

    /// <summary>
    /// The negative controls damage a nominated play. Without a nomination they take
    /// the last one, which in a fight replayed to its end is the killing blow - and
    /// omitting the killing blow leaves a shorter history that is self-consistent, so
    /// the control would stop being a control.
    /// </summary>
    [Fact]
    public void NominatesAPlayForTheNegativeControlsThatIsNotTheKillingBlow()
    {
        var manifest = Manifest;
        var nominated = Corruption.NominatedPlay(manifest.Actions);

        Assert.True(nominated.Args.ContainsKey("negative_control_substitute_card_id"));
        Assert.NotEqual(manifest.Actions[^1].Seq, nominated.Seq);
        Assert.NotEqual(
            nominated.Args["card_id"],
            nominated.Args["negative_control_substitute_card_id"]);
        Assert.Contains(
            manifest.Checkpoints,
            checkpoint => checkpoint.AfterSeq == nominated.Seq);
    }

    [Fact]
    public void IdentifiesEveryModTheOverlayReported()
    {
        var mods = Manifest.Environment.Mods.Value;

        Assert.Equal(mods.ReportedCount, mods.Mods.Count);
        Assert.All(mods.Mods, m => Assert.False(string.IsNullOrWhiteSpace(m.ReplayRisk)));

        // The identities are not readable from the video, which names no mod. Marking
        // them observed would claim a source that does not exist.
        Assert.Equal(FactSource.Inferred, Manifest.Environment.Mods.Source);
    }

}
