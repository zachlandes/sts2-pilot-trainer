namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The checks that run before a recording is trusted at all.
///
/// These matter more than they look. Everything else in the validator, and every
/// gate in the preflight, is satisfied by a recording of a run that was *resumed*
/// half way through: same seed, same build, same content hash, same acts. The only
/// thing that separates the two is the recording itself.
/// </summary>
public class RunStartGateTests
{
    [Fact]
    public void AcceptsARecordingThatStartsAtTheRunsStart()
    {
        Assert.True(ManifestValidator.Validate(Fixtures.ValidManifest()).IsValid);
    }

    [Fact]
    public void RejectsAVideoSourceWithNoRunStartEvidence()
    {
        var manifest = WithRunStart(null);

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("run_start is absent", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARunEnteredFromRunHistory()
    {
        var manifest = WithRunStart(Fixtures.RunStart(fromHistory: true));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("resumed run", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordingShowingAResumeDialog()
    {
        var manifest = WithRunStart(Fixtures.RunStart(modal: true));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("picked up rather than started", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordingThatFirstShowsAFloorAboveTheFirst()
    {
        var manifest = WithRunStart(Fixtures.RunStart(floor: 7));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("observes floor 7", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordingWhoseRunTimerIsAlreadyWellUnderway()
    {
        // The clearest fingerprint of a resumed run: the timer carries the original
        // run's accumulated time rather than starting near zero.
        var manifest = WithRunStart(Fixtures.RunStart(runTimeSeconds: 900));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("outside the 0-15s", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsATimerReadingAtTheEdgeOfTheAllowance()
    {
        var manifest = WithRunStart(
            Fixtures.RunStart(runTimeSeconds: RunStartEvidence.MaxRunTimeSecondsAtStart));

        Assert.True(ManifestValidator.Validate(manifest).IsValid);
    }

    private static ReplayManifest WithRunStart(RunStartEvidence? evidence)
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with { Source = manifest.Source with { RunStart = evidence } };
    }
}

/// <summary>
/// The end-of-run screen is a second reading of the environment taken thousands of
/// seconds after the first. Requiring the two to agree catches a reading that
/// drifted and a recording assembled from more than one run — neither of which any
/// single reading can catch alone.
/// </summary>
public class RunSummaryGateTests
{
    [Fact]
    public void RejectsAVideoSourceWithNoRunSummary()
    {
        var result = ManifestValidator.Validate(WithSummary(null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("run_summary is absent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("build")]
    [InlineData("date")]
    [InlineData("hash")]
    public void RejectsASummaryThatDisagreesWithTheEnvironment(string field)
    {
        var summary = field switch
        {
            "seed" => Fixtures.RunSummary(seed: "MMWN3B7J2JL3"),
            "build" => Fixtures.RunSummary(build: "v0.110.0"),
            "date" => Fixtures.RunSummary(date: "2026.07.01"),
            _ => Fixtures.RunSummary(hash: "999999"),
        };

        var result = ManifestValidator.Validate(WithSummary(summary));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("two ends of the recording disagree", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsASummaryWhoseAscensionDisagrees()
    {
        var result = ManifestValidator.Validate(WithSummary(Fixtures.RunSummary(ascension: 0)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("reads ascension 0", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsASummaryThatDoesNotSayWhatItLeftOut()
    {
        // The game mode is not on this screen. An unstated absence reads as a value
        // that was checked.
        var summary = Fixtures.RunSummary() with { NotShown = [] };

        var result = ManifestValidator.Validate(WithSummary(summary));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("not_shown is empty", StringComparison.Ordinal));
    }

    private static ReplayManifest WithSummary(RunSummaryObservation? summary)
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with { Source = manifest.Source with { RunSummary = summary } };
    }
}

public class ModEnvironmentTests
{
    [Fact]
    public void RejectsAnEnvironmentThatIdentifiesFewerModsThanWereLoaded()
    {
        // An unidentified mod is exactly the gap the content hash cannot close, so
        // the shortfall has to be visible rather than rounded away.
        var manifest = WithMods(Fixtures.ModEnvironment(reportedCount: 3));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("but reports 3 were loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAModListedWithoutAReplayRiskAssessment()
    {
        var mods = Fixtures.ModEnvironment() with
        {
            Mods = [new InstalledMod("Nameless Risk", "does something", "  ")],
        };

        var result = ManifestValidator.Validate(WithMods(mods));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("no replay-risk assessment", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnUnnamedEnvironment()
    {
        var result = ManifestValidator.Validate(WithMods(Fixtures.ModEnvironment() with { Name = " " }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("mods.name is empty", StringComparison.Ordinal));
    }

    private static ReplayManifest WithMods(ModEnvironment mods)
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with
        {
            Environment = manifest.Environment with
            {
                Mods = Fact<ModEnvironment>.Inferred(mods, FactEvidence.Reasoning("test")),
            },
        };
    }
}

/// <summary>
/// Controls on the ingestion controls. Each corruption must genuinely damage the
/// manifest and must be refused; a "corruption" that left the manifest valid, or one
/// refused for an unrelated reason, would prove nothing about the gate it targets.
/// </summary>
public class IngestionCorruptionTests
{
    [Fact]
    public void EveryCorruptionIsRefused()
    {
        foreach (var corruption in IngestionCorruption.All)
        {
            var result = ManifestValidator.Validate(corruption.Apply(Fixtures.ValidManifest()));
            Assert.False(result.IsValid, $"'{corruption.Name}' was accepted");
        }
    }

    [Fact]
    public void TheUncorruptedManifestIsAccepted()
    {
        // Without this, a validator that rejected everything would pass the whole
        // suite above.
        Assert.True(ManifestValidator.Validate(Fixtures.ValidManifest()).IsValid);
    }

    [Fact]
    public void EveryCorruptionExplainsWhatItIsFor()
    {
        Assert.All(IngestionCorruption.All, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.What));
            Assert.False(string.IsNullOrWhiteSpace(c.WhyItMatters));
        });
    }

    [Fact]
    public void CoversTheResumedRunSpecifically()
    {
        // This is the one an engine replay cannot catch: a resumed run replays
        // perfectly, it is simply not the run the history describes. If the set ever
        // stops covering it, the gates lose the only thing they uniquely do.
        var names = IngestionCorruption.All.Select(c => c.Name).ToList();
        Assert.Contains("resumed-from-run-history", names);
        Assert.Contains("recording-starts-mid-run", names);
    }
}
