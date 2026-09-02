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
    public void RejectsAnUndefinedFactSource()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            ManifestJson.Serialize(Fixtures.ValidManifest()))!.AsObject();
        document["environment"]!["seed"]!["Source"] = 99;
        var manifest = ManifestJson.Deserialize(document.ToJsonString());

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("undefined fact source value 99", StringComparison.Ordinal));
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
    public void RejectsCheckpointEvidenceAfterTheNextAction()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Checkpoints =
            [
                manifest.Checkpoints[0] with
                {
                    AfterSeq = 0,
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.energy"] = Fact<string>.Observed(
                            "3", FactEvidence.AtVideoTime(100_000, "after the following action")),
                    },
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("later than action 1 timestamp", StringComparison.Ordinal));
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
                    AfterSeq = 0,
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

    [Theory]
    [InlineData("platform")]
    [InlineData("video_id")]
    [InlineData("channel_id")]
    public void RejectsAnEmptyVodVideoIdentityField(string field)
    {
        var manifest = Fixtures.ValidManifest();
        var video = manifest.Source.Video!;
        var changed = field switch
        {
            "platform" => video with { Platform = "" },
            "video_id" => video with { VideoId = "" },
            "channel_id" => video with { ChannelId = "" },
            _ => throw new InvalidOperationException(),
        };
        manifest = manifest with { Source = manifest.Source with { Video = changed } };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains($"source.video.{field} is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsRunStartEvidenceAtOrAfterTheFirstObservedAction()
    {
        var manifest = Fixtures.ValidManifest();
        var runStart = manifest.Source.RunStart!;
        var late = FactEvidence.AtVideoTime(90_000, "after the first action");
        manifest = manifest with
        {
            Source = manifest.Source with
            {
                RunStart = runStart with
                {
                    FirstObservedRunTimeSeconds = runStart.FirstObservedRunTimeSeconds with { Evidence = late },
                    FirstObservedFloor = runStart.FirstObservedFloor with { Evidence = late },
                    EnteredFromRunHistory = runStart.EnteredFromRunHistory with { Evidence = late },
                    ResumeModalSeen = runStart.ResumeModalSeen with { Evidence = late },
                },
            },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Problems.Count(problem =>
            problem.Contains("must precede the first observed action", StringComparison.Ordinal)));
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
    // ── The verbs that reach the loot screen, the event and the card screens ──

    [Fact]
    public void AcceptsTheRewardAndScreenVerbsWithTheirRequiredArguments()
    {
        // The positive case for the block of negatives below. Without it they would
        // all still pass against a validator that refused these verbs outright.
        var manifest = WithActions(
            Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", "gold")),
            Fixtures.Action(1, ActionVerb.TakeCard, ("card_id", "CARD.POMMEL_STRIKE"), ("option_index", "0")),
            Fixtures.Action(2, ActionVerb.SkipRewards),
            Fixtures.Action(3, ActionVerb.ChooseEventOption,
                ("event_id", "EVENT.WATERLOGGED_SCRIPTORIUM"), ("option_index", "2")),
            Fixtures.Action(4, ActionVerb.SelectCardFromScreen,
                ("card_id", "CARD.BASH"), ("option_index", "7")));

        var result = ManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, result.Describe());
    }

    [Theory]
    [InlineData("bloody-ink")]
    [InlineData("coins")]
    [InlineData("card")]
    [InlineData("relic")]
    public void RejectsAKindOfRewardThatIsNotClaimedWithOneClick(string kind)
    {
        // 'card' is on this list on purpose: the card reward opens a second screen, so
        // taking it is TakeCard, which records which card came back. Letting it through
        // here would lose that.
        var manifest = WithActions(Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", kind)));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("'reward_type'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnEventChoiceThatDoesNotNameItsEvent()
    {
        // An option index means nothing without the event it indexes, and which event
        // a floor generates is a consequence of the whole history before it.
        var manifest = WithActions(Fixtures.Action(0, ActionVerb.ChooseEventOption, ("option_index", "2")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("missing required argument 'event_id'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATakenCardThatDoesNotSayWhichPositionItCameFrom()
    {
        var manifest = WithActions(
            Fixtures.Action(0, ActionVerb.TakeCard, ("card_id", "CARD.POMMEL_STRIKE")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("missing required argument 'option_index'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATakenCardWithOnlyHalfOfItsNegativeControlAlternative()
    {
        var manifest = WithActions(Fixtures.Action(
            0, ActionVerb.TakeCard,
            ("card_id", "CARD.POMMEL_STRIKE"), ("option_index", "0"),
            (Corruption.AlternativeCardId, "CARD.BASH")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains(
            "alternative card and option index must appear together", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAScreenSelectionCarryingAnArgumentTheVerbDoesNotTake()
    {
        var manifest = WithActions(Fixtures.Action(
            0, ActionVerb.SelectCardFromScreen,
            ("card_id", "CARD.BASH"), ("option_index", "7"), ("hand_index", "1")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("unknown argument 'hand_index'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnEmptyEventId()
    {
        var manifest = WithActions(Fixtures.Action(
            0, ActionVerb.ChooseEventOption, ("event_id", "  "), ("option_index", "0")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("'event_id' is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsANegativeControlAlternativeThatIsTheDecisionThatWasMade()
    {
        // A control aimed at the decision the player actually made corrupts nothing,
        // and an arbiter that accepted it would be reported as having failed to reject
        // a corruption nobody made.
        var manifest = WithActions(Fixtures.Action(
            0, ActionVerb.SelectCardFromScreen,
            ("card_id", "CARD.DEFEND_IRONCLAD"), ("option_index", "5"),
            (Corruption.AlternativeOptionIndex, "5")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("corrupts nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void StillRefusesAVerbThisManifestVersionDoesNotImplement()
    {
        // The alphabet is deliberately larger than what is implemented. A named verb
        // that quietly did nothing would be the worst of both worlds, so the ones with
        // no mapping have to fail at ingestion rather than at replay.
        var manifest = WithActions(Fixtures.Action(0, ActionVerb.UsePotion, ("slot_index", "0")));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("does not implement", StringComparison.Ordinal));
    }

    /// <summary>
    /// The valid manifest with more actions appended, renumbered and timed after the
    /// ones it already has. Appending rather than replacing keeps the rest of the
    /// manifest coherent, so a failure is about the action under test rather than
    /// about the evidence timeline.
    /// </summary>
    private static ReplayManifest WithActions(params ActionRecord[] actions)
    {
        var manifest = Fixtures.ValidManifest();
        var last = manifest.Actions[^1];
        var appended = actions.Select((action, index) => action with
        {
            Seq = last.Seq + 1 + index,
            Evidence = FactEvidence.AtVideoTime(
                Math.Max(
                    last.Evidence!.VideoTimeMs!.Value,
                    manifest.Checkpoints.SelectMany(c => c.Expect.Values)
                        .Max(fact => fact.Evidence?.VideoTimeMs ?? 0))
                + 1000 * (index + 1),
                "test fixture"),
        });
        return manifest with { Actions = [.. manifest.Actions, .. appended] };
    }

}
