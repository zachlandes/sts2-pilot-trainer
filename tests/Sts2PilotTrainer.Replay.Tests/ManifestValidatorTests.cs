namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// One test per rule, each fed an input that breaks exactly that rule.
///
/// The positive test at the top is not a formality: a validator that rejects
/// everything passes every negative test there is, and would look perfect here
/// without it.
/// </summary>
public class ManifestValidatorTests
{
    [Fact]
    public void AcceptsAWellFormedManifest()
    {
        var result = ManifestValidator.Validate(Fixtures.ValidManifest());
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsASeedContainingLettersTheGameNeverGenerates()
    {
        // O and I are absent from the game's seed alphabet, which is exactly why an
        // OCR reader produces them. Accepting one keys an artifact to a run that
        // cannot exist.
        var manifest = WithSeed("SEXT47K77REK".Replace('E', 'O'));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("'O' is not in the alphabet", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0.111.0")]
    [InlineData("v0.111")]
    [InlineData("latest")]
    public void RejectsABuildVersionThatIsNotAVersion(string version)
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Environment = manifest.Environment with
            {
                BuildVersion = Fact<string>.Observed(version, FactEvidence.AtVideoTime(1, "test")),
            },
        };

        Assert.False(ManifestValidator.Validate(manifest).IsValid);
    }

    [Fact]
    public void RejectsABuildDateThatIsNotTheOverlaysFormat()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Environment = manifest.Environment with
            {
                BuildDateUtc = Fact<string>.Observed("2026-08-14", FactEvidence.AtVideoTime(1, "test")),
            },
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("YYYY.MM.DD", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnUnknownGameMode()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Environment = manifest.Environment with
            {
                GameMode = Fact<string>.Inferred("endless", FactEvidence.Reasoning("test")),
            },
        };

        Assert.False(ManifestValidator.Validate(manifest).IsValid);
    }

    [Fact]
    public void RejectsAnEmptyActList()
    {
        // The acts are identity: this build ships two acts at index 0, and the wrong
        // one generates different content from the same seed without changing the map.
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Environment = manifest.Environment with
            {
                Acts = Fact<IReadOnlyList<string>>.Inferred([], FactEvidence.Reasoning("test")),
            },
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("environment.acts is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsEnvironmentIdentityThatClaimsToComeFromTheEngine()
    {
        // Circular: environment identity states what the engine must be, so it cannot
        // be something the engine reported.
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Environment = manifest.Environment with { Seed = Fact<string>.Engine("SFXT47K77RFK") },
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("circular", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAGapInTheActionSequence()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Actions = [manifest.Actions[0], manifest.Actions[1] with { Seq = 5 }],
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("dense", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnObservedActionWithNoVideoTimestamp()
    {
        // An observation nobody can re-check is not an observation.
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Actions = [manifest.Actions[0] with { Evidence = null }, manifest.Actions[1]],
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("cannot be re-checked", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAManifestWithNoCheckpoints()
    {
        var manifest = Fixtures.ValidManifest() with { Checkpoints = [] };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("proves only that it ran", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACheckpointThatExpectsNothing()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Checkpoints =
            [
                manifest.Checkpoints[0] with { Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal) },
            ],
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("can never fail", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACheckpointComparingTheEngineAgainstItself()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Checkpoints =
            [
                manifest.Checkpoints[0] with
                {
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.energy"] = Fact<string>.Engine("3"),
                    },
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("always passes and means nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACheckpointBoundToAnActionThatDoesNotExist()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Checkpoints = [manifest.Checkpoints[0] with { AfterSeq = 99 }],
        };

        Assert.False(ManifestValidator.Validate(manifest).IsValid);
    }

    [Fact]
    public void RejectsASourceThatSaysItIsAVideoAndNamesNone()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with { Source = manifest.Source with { Video = null } };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("re-check any observation", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAManifestThatDoesNotSayHowFarItGoes()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with { Source = manifest.Source with { Coverage = "  " } };

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("does not say where it stops", StringComparison.Ordinal));
    }

    private static ReplayManifest WithSeed(string seed)
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with
        {
            Environment = manifest.Environment with
            {
                Seed = Fact<string>.Observed(seed, FactEvidence.AtVideoTime(1, "test")),
            },
        };
    }
}
