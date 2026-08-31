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
        Assert.Contains(result.Problems, p => p.Contains("cannot be produced by the engine", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("malformed")]
    public void RejectsInvalidVerbSpecificActionArguments(string defect)
    {
        var manifest = Fixtures.ValidManifest();
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in manifest.Actions[1].Args) args[name] = value;
        switch (defect)
        {
            case "missing":
                args.Remove("act");
                break;
            case "unknown":
                args["floor"] = "1";
                break;
            default:
                args["act"] = "01";
                break;
        }
        manifest = manifest with
        {
            Actions = [manifest.Actions[0], manifest.Actions[1] with { Args = args }],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem => problem.Contains("actions[1] (MapMove)", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsActionIntegersOutsideTheInt32Range()
    {
        var manifest = Fixtures.ValidManifest();
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["option_index"] = "999999999999999999999",
        };
        manifest = manifest with
        {
            Actions = [manifest.Actions[0] with { Args = args }, manifest.Actions[1]],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem => problem.Contains("Int32 range", StringComparison.Ordinal));
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
    public void RejectsActionTimestampsThatContradictSequenceOrder()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Actions =
            [
                manifest.Actions[0] with { Evidence = FactEvidence.AtVideoTime(80_000, "later frame") },
                manifest.Actions[1] with { Evidence = FactEvidence.AtVideoTime(70_000, "earlier frame") },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must be nondecreasing", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsCheckpointEvidenceBeforeItsAction()
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
                        ["combat.energy"] = Fact<string>.Observed(
                            "3", FactEvidence.AtVideoTime(70_000, "before the map move")),
                    },
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("earlier than its after_seq action", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsEqualActionAndCheckpointTimestamps()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Actions =
            [
                manifest.Actions[0] with { Evidence = FactEvidence.AtVideoTime(73_500, "same settled frame") },
                manifest.Actions[1] with { Evidence = FactEvidence.AtVideoTime(73_500, "same settled frame") },
            ],
            Checkpoints =
            [
                manifest.Checkpoints[0] with
                {
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.energy"] = Fact<string>.Observed(
                            "3", FactEvidence.AtVideoTime(73_500, "same settled frame")),
                    },
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsRunStartFactsThatWereNotObservedInTheVideo()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Source = manifest.Source with
            {
                RunStart = manifest.Source.RunStart! with
                {
                    FirstObservedFloor = Fact<int>.Declared(1),
                },
            },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("first_observed_floor must be source=observed", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsRunSummaryFactsWithoutVideoTimestamps()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Source = manifest.Source with
            {
                RunSummary = manifest.Source.RunSummary! with
                {
                    Seed = new Fact<string>(manifest.Environment.Seed.Value, FactSource.Observed),
                },
            },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("source.run_summary.seed", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsObservedCheckpointFactsWithoutVideoTimestamps()
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
                        ["combat.energy"] = new Fact<string>("3", FactSource.Observed),
                    },
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("has no video timestamp", StringComparison.Ordinal));
    }

    [Fact]
    public void DedicatedLineReplayAcceptsReasonedInferredSuffixes()
    {
        var manifest = Fixtures.SyntheticManifest();
        var prefix = manifest.Actions.Take(2).ToList();
        var lineAction = new ActionRecord
        {
            Seq = 2,
            Verb = ActionVerb.PlayCard,
            Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["card_id"] = "CARD.BASH",
                ["hand_index"] = "3",
            },
            Source = FactSource.Inferred,
            Evidence = FactEvidence.Reasoning("hypothetical line"),
        };
        manifest = manifest with
        {
            Actions = [.. prefix, lineAction],
            Checkpoints = manifest.Checkpoints.Where(checkpoint => checkpoint.AfterSeq < 2).ToList(),
        };

        Assert.False(ManifestValidator.Validate(manifest).IsValid);
        Assert.True(ManifestValidator.ValidateLineReplay(manifest, 2).IsValid);
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
        Assert.Contains(result.Problems, p => p.Contains("must be source=observed", StringComparison.Ordinal));
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
    public void AcceptsStrictSyntheticEngineProvenance()
    {
        var result = ManifestValidator.Validate(Fixtures.SyntheticManifest());

        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsSyntheticProvenanceWithVideoEvidence()
    {
        var manifest = Fixtures.SyntheticManifest();
        manifest = manifest with
        {
            Source = manifest.Source with
            {
                Video = Fixtures.ValidManifest().Source.Video,
            },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("cannot carry video", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsSyntheticFixtureFromADifferentBuild()
    {
        var manifest = Fixtures.SyntheticManifest();
        manifest = manifest with
        {
            Source = manifest.Source with
            {
                Synthetic = manifest.Source.Synthetic! with { GeneratedBuild = "v0.110.0" },
            },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("must match environment.build_version", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnsupportedSourceKinds()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with { Source = manifest.Source with { Kind = "declared" } };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("is unsupported", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDeclaredActionsInAVodReplay()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Actions = [manifest.Actions[0] with { Source = FactSource.Declared }, manifest.Actions[1]],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("must be source=observed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2_050_000)]
    public void RejectsActionTimestampsOutsideTheVideo(int timestamp)
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Actions =
            [
                manifest.Actions[0] with
                {
                    Evidence = FactEvidence.AtVideoTime(timestamp, "outside video"),
                },
                manifest.Actions[1],
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("outside the source video range", StringComparison.Ordinal));
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
