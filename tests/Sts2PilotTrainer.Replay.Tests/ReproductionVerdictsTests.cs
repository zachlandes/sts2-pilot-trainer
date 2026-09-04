namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The catalogue of what each build did with one recording.
///
/// Its whole job is to keep answers apart. A verdict is about a build, an older
/// answer for a different build is not stale, and a file that says two things about
/// one build leaves the reader to pick - which is the failure this project exists to
/// prevent, in miniature.
/// </summary>
public sealed class ReproductionVerdictsTests
{
    private static ReplayManifest Manifest() => Fixtures.ValidManifest();

    private static ReproductionVerdict Verdict(
        string build, string contentHash = "999", ReproductionStatus status = ReproductionStatus.Reproduces) =>
        new()
        {
            RunId = Manifest().RunId,
            ActionHistoryHash = "sha256:abc",
            RecordedBuild = Manifest().Environment.BuildVersion.Value,
            VerifiedBuild = build,
            RecordedContentHash = Manifest().Environment.ContentHash.Value,
            VerifiedContentHash = contentHash,
            Status = status,
            Note = "for a test",
        };

    [Fact]
    public void AnAnswerForANewBuildJoinsTheOnesAlreadyThere()
    {
        var catalogue = ReproductionVerdicts.For(Manifest())
            .With(Verdict("v0.111.0"))
            .With(Verdict("v0.112.0"));

        Assert.Equal(["v0.111.0", "v0.112.0"], catalogue.Verdicts.Select(v => v.VerifiedBuild));
    }

    /// <summary>A build asked again has one current answer, and it is the new one.</summary>
    [Fact]
    public void AskingOneBuildAgainReplacesThatBuildsAnswerAndNoOther()
    {
        var catalogue = ReproductionVerdicts.For(Manifest())
            .With(Verdict("v0.111.0"))
            .With(Verdict("v0.112.0"))
            .With(Verdict("v0.112.0", status: ReproductionStatus.Diverges));

        Assert.Equal(2, catalogue.Verdicts.Count);
        Assert.Equal(ReproductionStatus.Reproduces, catalogue.Verdicts[0].Status);
        Assert.Equal(ReproductionStatus.Diverges, catalogue.Verdicts[1].Status);
    }

    /// <summary>
    /// Two builds can share a version string across a hotfix, and the content hash is
    /// what tells them apart. Keying by the version alone would let a hotfix silently
    /// overwrite the answer for the build before it.
    /// </summary>
    [Fact]
    public void TheSameVersionWithADifferentContentHashIsADifferentBuild()
    {
        var catalogue = ReproductionVerdicts.For(Manifest())
            .With(Verdict("v0.112.0", contentHash: "111"))
            .With(Verdict("v0.112.0", contentHash: "222"));

        Assert.Equal(2, catalogue.Verdicts.Count);
    }

    [Fact]
    public void ACatalogueAboutAnotherRecordingIsRefused()
    {
        var catalogue = ReproductionVerdicts.For(Manifest()) with { RunId = "somebody-elses-run" };

        var refusal = Assert.Throws<ManifestException>(() => catalogue.Bind(Manifest()));

        Assert.Contains("somebody-elses-run", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manifest is the authority on which build the recording was made on. A
    /// verdict that disagrees was measured against something else, and a catalogue
    /// holding it would attribute that measurement to this recording.
    /// </summary>
    [Fact]
    public void AVerdictNamingADifferentRecordedBuildIsRefused()
    {
        var catalogue = ReproductionVerdicts.For(Manifest())
            .With(Verdict("v0.112.0") with { RecordedBuild = "v0.109.0" });

        var refusal = Assert.Throws<ManifestException>(() => catalogue.Bind(Manifest()));

        Assert.Contains("v0.109.0", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoAnswersForOneBuildAreRefused()
    {
        var catalogue = ReproductionVerdicts.For(Manifest()) with
        {
            Verdicts = [Verdict("v0.112.0"), Verdict("v0.112.0", status: ReproductionStatus.Diverges)],
        };

        var refusal = Assert.Throws<ManifestException>(() => catalogue.Bind(Manifest()));

        Assert.Contains("more than once", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASchemaThisBuildDoesNotReadIsRefusedByName()
    {
        var catalogue = ReproductionVerdicts.For(Manifest()) with { SchemaId = "something/else/v9" };

        var refusal = Assert.Throws<ManifestException>(() => catalogue.Bind(Manifest()));

        Assert.Contains("something/else/v9", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(ReproductionVerdicts.Schema, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACatalogueSurvivesBeingWrittenDownAndReadBack()
    {
        var catalogue = ReproductionVerdicts.For(Manifest())
            .With(Verdict("v0.111.0"))
            .With(Verdict("v0.112.0", status: ReproductionStatus.Blocked));

        var read = ReproductionVerdicts.Deserialize(catalogue.Serialize());

        read.Bind(Manifest());
        Assert.Equal(catalogue.Verdicts.Count, read.Verdicts.Count);
        Assert.Equal(ReproductionStatus.Blocked, read.Verdicts[1].Status);
    }

    /// <summary>The verdicts live beside the manifest, under the same name, so nothing
    /// has to be told where to look.</summary>
    [Fact]
    public void TheVerdictsLiveBesideTheManifest()
    {
        Assert.Equal(
            Path.Combine("manifests", "navegreed.verdicts.json"),
            ReproductionVerdicts.PathFor(Path.Combine("manifests", "navegreed.replay.json")));
    }
}
