using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Re-keying: whether a recording still reproduces on the build installed now.
///
/// The rules that read a verdict out of a replay are proved without the game in
/// <c>Sts2PilotTrainer.Replay.Tests</c>. What these add is that the measurement is
/// real - that the boundaries compared were derived by an engine that just replayed
/// the history, and that the file written is one the reader accepts.
/// </summary>
public sealed class RekeyTests
{
    [GameFact]
    public void ARecordingOnItsOwnBuildReproducesAndEveryBoundaryIsCompared()
    {
        var result = Arbiter.Run("gate", Arbiter.Manifest, "--rekey", "v0.111.0");

        Assert.True(result.Verified, result.All);
        Assert.Contains("REPRODUCES", result.Output, StringComparison.Ordinal);

        var manifest = ManifestJson.Load(Arbiter.Manifest);
        var catalogue = ReproductionVerdicts.Deserialize(
            File.ReadAllText(ReproductionVerdicts.PathFor(Arbiter.Manifest)));

        catalogue.Bind(manifest);
        var verdict = Assert.Single(catalogue.Verdicts, v => v.VerifiedBuild == "v0.111.0");
        Assert.Equal(ReproductionStatus.Reproduces, verdict.Status);
        Assert.Empty(verdict.MovedBoundaries);

        // Every boundary the recording declares, and not merely the first fight's.
        // A verdict that compared one is not a verdict that found the rest unmoved.
        Assert.Equal(manifest.Boundaries.Count, verdict.BoundariesCompared);
        Assert.True(verdict.BoundariesCompared > 1, "the recording declares more than one boundary");
    }

    /// <summary>
    /// A verdict is what the engine actually did, so the build being asked about has
    /// to be the build installed. Answering for a build that is not here would be the
    /// confident wrong answer this project exists to prevent.
    /// </summary>
    [GameFact]
    public void AskingAboutABuildThisMachineDoesNotHaveIsRefused()
    {
        var result = Arbiter.Run("gate", Arbiter.Manifest, "--rekey", "v0.112.0");

        Assert.False(result.Verified, result.All);
        Assert.Contains(
            "has to be the build installed", result.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recording's own fights are bound to the manifest per fight, so a re-key
    /// that left them alone would leave the two disagreeing. docs/ingestion.md
    /// requires them regenerated in the same step.
    /// </summary>
    [GameFact]
    public void AReproducingRekeyRegeneratesTheRecordingsOwnFights()
    {
        var result = Arbiter.Run("gate", Arbiter.Manifest, "--rekey", "v0.111.0");

        Assert.True(result.Verified, result.All);
        Assert.Contains("recorded fights:", result.Output, StringComparison.Ordinal);

        var fights = RecordedFights.Load(
            Arbiter.Manifest.Replace(".replay.json", ".recorded-fights.json", StringComparison.Ordinal));
        fights.Bind(ManifestJson.Load(Arbiter.Manifest));
    }
}
