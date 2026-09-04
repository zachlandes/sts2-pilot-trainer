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
        var manifestPath = ManifestCopy();
        var result = Arbiter.Run("gate", manifestPath, "--rekey", "v0.111.0");

        Assert.Contains("REPRODUCES", result.Output, StringComparison.Ordinal);

        var manifest = ManifestJson.Load(manifestPath);
        var catalogue = ReproductionVerdicts.Deserialize(
            File.ReadAllText(ReproductionVerdicts.PathFor(manifestPath)));

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
        var result = Arbiter.Run("gate", ManifestCopy(), "--rekey", "v0.112.0");

        Assert.False(result.Verified, result.All);
        Assert.Contains(
            "has to be the build installed", result.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recording's own fights are bound to the manifest per fight, so a re-key
    /// that left them alone would leave the two disagreeing. docs/ingestion.md
    /// requires them regenerated in the same step, beside the recording - which is
    /// the file the mod ships and <c>enter-fight --play</c> reads.
    ///
    /// The file on disk is made stale first, so a re-key that regenerated nothing
    /// fails here instead of passing on the copy that was already correct.
    /// </summary>
    [GameFact]
    public void AReproducingRekeyRegeneratesTheRecordingsOwnFights()
    {
        var manifestPath = ManifestCopy();
        var fightsPath = manifestPath.Replace(
            ".replay.json", ".recorded-fights.json", StringComparison.Ordinal);
        var stale = File.ReadAllText(ShippedFights)
            .Replace("navegreed-OJ-6QXhNgdg", "navegreed-STALE", StringComparison.Ordinal);
        File.WriteAllText(fightsPath, stale);

        var result = Arbiter.Run("gate", manifestPath, "--rekey", "v0.111.0");

        Assert.Contains("recorded fights:", result.Output, StringComparison.Ordinal);
        Assert.NotEqual(stale, File.ReadAllText(fightsPath));

        var fights = RecordedFights.Load(fightsPath);
        fights.Bind(ManifestJson.Load(manifestPath));
    }

    /// <summary>
    /// The recording ships a combat-start boundary for a fight whose enemy roster the
    /// trace cannot follow, so its own fights cannot all be summarised - and a re-key
    /// says so with a non-zero exit rather than reporting the recording carried
    /// forward. The verdict is measured and written either way: what the engine did
    /// with the history is a different question from whether every declared fight can
    /// be read back.
    /// </summary>
    [GameFact]
    public void ARecordingWhoseFightCannotBeSummarisedDoesNotCarryForward()
    {
        var manifestPath = ManifestCopy();

        var result = Arbiter.Run("gate", manifestPath, "--rekey", "v0.111.0");

        Assert.False(result.Verified, result.All);
        Assert.Contains("REPRODUCES", result.Output, StringComparison.Ordinal);
        Assert.Contains("cannot be summarised", result.All, StringComparison.Ordinal);
        Assert.Contains("does not carry forward", result.All, StringComparison.Ordinal);

        var catalogue = ReproductionVerdicts.Deserialize(
            File.ReadAllText(ReproductionVerdicts.PathFor(manifestPath)));
        catalogue.Bind(ManifestJson.Load(manifestPath));
        Assert.Equal(
            ReproductionStatus.Reproduces,
            Assert.Single(catalogue.Verdicts, v => v.VerifiedBuild == "v0.111.0").Status);
    }

    private static string ShippedFights =>
        Arbiter.Manifest.Replace(".replay.json", ".recorded-fights.json", StringComparison.Ordinal);

    /// <summary>
    /// The shipped recording, copied somewhere a re-key may write. A re-key writes a
    /// verdict catalogue and regenerates the recorded fights beside the manifest it is
    /// given, and a suite that did that to the tracked copy would teach people to
    /// ignore a dirty working tree.
    /// </summary>
    private static string ManifestCopy()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"rekey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, Path.GetFileName(Arbiter.Manifest));
        File.Copy(Arbiter.Manifest, copy);
        return copy;
    }
}
