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
