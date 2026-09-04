namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Revalidation is the one place this project deliberately runs a history against a build it
/// was not recorded on, so its rules have to be tight in both directions. It must not call a
/// recording surviving when the fight moved, and it must not quietly rebase away a mismatch
/// that has nothing to do with the patch - an ascension or a character difference is a
/// refusal with its own remedy, and answering a question nobody asked would bury it.
/// </summary>
public class RevalidationTests
{
    private static ReplayManifest Manifest() => Fixtures.ValidManifest();

    /// <summary>
    /// The mechanism that was tried first and removed. Re-pointing the manifest's environment
    /// at a new build leaves <c>source.run_summary</c> - the second reading taken from the far
    /// end of the recording - still naming the old one, which is indistinguishable from a
    /// recording spliced out of two runs. This test pins the validator's refusal so nobody
    /// reintroduces the rebase: the manifest records what the recording was made on and is not
    /// edited, and the build under test travels on the verdict instead.
    /// </summary>
    [Fact]
    public void RewritingTheEnvironmentOntoANewBuildMakesTheManifestInconsistent()
    {
        var manifest = Manifest();
        var rebased = manifest with
        {
            Environment = manifest.Environment with
            {
                BuildVersion = Fact<string>.Declared("v0.112.0"),
            },
        };

        var result = ManifestValidator.Validate(rebased);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p =>
            p.Contains("run_summary reads build_version", StringComparison.Ordinal) &&
            p.Contains("two ends of the recording disagree", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new[] { "build_version" }, true)]
    [InlineData(new[] { "build_version", "build_date_utc", "content_hash" }, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "ascension" }, false)]
    [InlineData(new[] { "build_version", "unlocks" }, false)]
    [InlineData(new[] { "game_mode" }, false)]
    public void OnlyBuildFieldsCountAsPatchDrift(string[] mismatched, bool expected)
    {
        Assert.Equal(expected, Revalidation.IsBuildDriftOnly(mismatched));
    }

    [Fact]
    public void ACleanReplayOntoTheRecordedBoundarySurvivesThePatch()
    {
        var manifest = Manifest();

        var verdict = Revalidation.Decide(
            manifest, "v0.112.0", "999", "sha256:abc", replayedCleanly: true,
            firstDivergence: null, derivedBoundaries: manifest.Boundaries);

        Assert.Equal(ReproductionStatus.Reproduces, verdict.Status);
        Assert.Equal("v0.112.0", verdict.VerifiedBuild);
        Assert.Equal(manifest.Environment.BuildVersion.Value, verdict.RecordedBuild);
        Assert.Equal(manifest.Boundaries.Count, verdict.BoundariesCompared);
        Assert.Empty(verdict.MovedBoundaries);
        Assert.Contains("carries forward", verdict.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ADivergingReplayIsRetiredAndKeepsWhereItDiverged()
    {
        var verdict = Revalidation.Decide(
            Manifest(), "v0.112.0", "999", "sha256:abc", replayedCleanly: false,
            firstDivergence: "checkpoint 'turn2-start': combat.player_hp observed '60', engine produced '55'",
            derivedBoundaries: []);

        Assert.Equal(ReproductionStatus.Diverges, verdict.Status);
        Assert.Contains("combat.player_hp", verdict.FirstDivergence!, StringComparison.Ordinal);
        Assert.Contains("no longer reproduces", verdict.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case that would otherwise pass silently. Every observed value can still agree while
    /// the hidden state behind the fight has moved, and a player entering that fight would be
    /// standing somewhere the recording never was.
    /// </summary>
    [Fact]
    public void PassingEveryCheckpointIsNotEnoughIfTheFightMoved()
    {
        var manifest = Manifest();
        var moved = manifest.Boundaries
            .Select(boundary => boundary with { Digest = Fact<string>.Engine("sha256:" + new string('f', 64)) })
            .ToList();

        var verdict = Revalidation.Decide(
            manifest, "v0.112.0", "999", "sha256:abc", replayedCleanly: true,
            firstDivergence: null, derivedBoundaries: moved);

        Assert.Equal(ReproductionStatus.Diverges, verdict.Status);
        Assert.Contains("boundary/ies moved", verdict.Note, StringComparison.Ordinal);
        Assert.NotEmpty(verdict.MovedBoundaries);
    }

    /// <summary>
    /// A floor arrival retires a recording exactly as a moved combat start does. Both
    /// are places a player can be stood, and one that moved is a place the recording
    /// can no longer be entered at - which is the whole reason a boundary carries a
    /// digest.
    /// </summary>
    [Fact]
    public void AMovedFloorBoundaryRetiresTheRecordingAsAMovedCombatStartDoes()
    {
        var manifest = Manifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine("sha256:" + new string('a', 64))),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine("sha256:" + new string('b', 64))),
            ],
        };

        var verdict = Revalidation.Decide(
            manifest, "v0.112.0", "999", "sha256:abc", replayedCleanly: true, firstDivergence: null,
            derivedBoundaries:
            [
                manifest.Boundaries[0],
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine("sha256:" + new string('c', 64))),
            ]);

        Assert.Equal(ReproductionStatus.Diverges, verdict.Status);
        Assert.Equal(2, verdict.BoundariesCompared);
        Assert.Single(verdict.MovedBoundaries);
        Assert.Contains("arrival on floor 2", verdict.MovedBoundaries[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A boundary this build never reaches is a moved boundary, not an absent
    /// comparison. The recording says a player can be stood there and this build never
    /// gets there, which is the same finding read from the other end.
    /// </summary>
    [Fact]
    public void ABoundaryThisBuildNeverReachesIsAMovedBoundary()
    {
        var manifest = Manifest();

        var verdict = Revalidation.Decide(
            manifest, "v0.112.0", "999", "sha256:abc", replayedCleanly: true,
            firstDivergence: null, derivedBoundaries: []);

        Assert.Equal(ReproductionStatus.Diverges, verdict.Status);
        Assert.Equal(0, verdict.BoundariesCompared);
        Assert.Contains(
            verdict.MovedBoundaries,
            line => line.Contains("is not reached at all", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonBuildMismatchIsBlockedRatherThanMeasured()
    {
        var verdict = Revalidation.Blocked(Manifest(), "v0.112.0", "999", ["ascension", "unlocks"]);

        Assert.Equal(ReproductionStatus.Blocked, verdict.Status);
        Assert.Contains("ascension", verdict.Note, StringComparison.Ordinal);
        Assert.Contains("none is answered here", verdict.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AVerdictBindsToTheHistoryItWasMeasuredOver()
    {
        var verdict = Revalidation.Decide(
            Manifest(), "v0.112.0", "999", "sha256:deadbeef", replayedCleanly: true,
            firstDivergence: null, derivedBoundaries: Manifest().Boundaries);

        Assert.Equal("sha256:deadbeef", verdict.ActionHistoryHash);
    }
}
