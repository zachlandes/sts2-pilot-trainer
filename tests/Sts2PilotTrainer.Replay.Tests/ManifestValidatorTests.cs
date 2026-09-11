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
    public void RejectsARecordingWithNoCombatStartBoundary()
    {
        var manifest = Fixtures.ValidManifest() with { Boundaries = [] };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("boundaries names no combat_start", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FactSource.Observed, Fixtures.Digest)]
    [InlineData(FactSource.Inferred, Fixtures.Digest)]
    [InlineData(FactSource.Engine, "sha256:not-a-digest")]
    public void RejectsAnUnprovenBoundaryDigest(FactSource source, string digest)
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, new Fact<string>(digest, source))],
        };

        Assert.False(ManifestValidator.Validate(manifest).IsValid);
    }

    [Fact]
    public void RejectsAnEngineBoundaryDigestCarryingSourceEvidence()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(
                    1, 1,
                    new Fact<string>(
                        Fixtures.Digest,
                        FactSource.Engine,
                        FactEvidence.AtVideoTime(75600, "not an engine production coordinate"))),
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("engine-produced digest", StringComparison.Ordinal) &&
            problem.Contains("must carry no evidence", StringComparison.Ordinal) &&
            problem.Contains("reading nobody took", StringComparison.Ordinal));
    }

    /// <summary>A digest a recorder read out of the live game is the other half of
    /// what a boundary may be established by, and is accepted.</summary>
    [Fact]
    public void AcceptsACapturedBoundaryDigest()
    {
        var manifest = Fixtures.NativeManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(
                    1, 1, Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(1))),
            ],
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>A video reconstruction had no recorder inside the game, so a boundary
    /// digest there is what this project's own replay produced.</summary>
    [Fact]
    public void RejectsACapturedBoundaryDigestOnAVideoRecording()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(
                    1, 1, Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(1))),
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("digest at the start of fight 1", StringComparison.Ordinal) &&
            problem.Contains("is marked source=captured and this is a vod recording", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsAnEngineBoundaryDigestOnAVideoRecording()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest))],
        };

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsACapturedBoundaryDigestWithNoCoordinate()
    {
        var manifest = Fixtures.NativeManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(
                    1, 1, new Fact<string>(Fixtures.Digest, FactSource.Captured)),
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("digest at the start of fight 1", StringComparison.Ordinal) &&
            problem.Contains("names no action_ordinal", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACapturedBoundaryDigestFromAnotherAction()
    {
        var manifest = Fixtures.NativeManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(
                    1, 1, Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(0))),
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("captured at action ordinal 0", StringComparison.Ordinal) &&
            problem.Contains("belongs after action 1", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsABoundaryKindOutsideTheClosedSet()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                new ReplayBoundary
                {
                    Kind = "shop_entry",
                    AfterSeq = 1,
                    Digest = Fact<string>.Engine(Fixtures.Digest),
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("which is not one of", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsABoundaryOutsideTheActionRange()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 99, Fact<string>.Engine(Fixtures.Digest))],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("outside the action range", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACombatStartThatDoesNotSayWhichFight()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                new ReplayBoundary
                {
                    Kind = ReplayBoundary.CombatStartKind,
                    AfterSeq = 1,
                    Digest = Fact<string>.Engine(Fixtures.Digest),
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must name which fight of the run it starts", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTheSameBoundaryDeclaredTwice()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsFloorEntryAndTurnStartBoundaries()
    {
        var manifest = FloorArrival.WithArrivalCheckpoints(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.TurnStart(1, 2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsAFloorEntryThatNamesAFight()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)) with { Fight = 1 },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("names a fight or a turn, which a floor entry is not", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATurnStartThatDoesNotSayWhichTurn()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                new ReplayBoundary
                {
                    Kind = ReplayBoundary.TurnStartKind,
                    AfterSeq = 1,
                    Fight = 1,
                    Digest = Fact<string>.Engine(Fixtures.Digest),
                },
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must name both the fight it is in and the turn it starts", StringComparison.Ordinal));
    }

    /// <summary>
    /// A recording whose verified trace holds three fights and declares a boundary for
    /// one of them silently offers less than it holds. The rule only has something to
    /// say once a replay has produced a trace; a manifest as authored has no trace, so
    /// this is a check on a result rather than on a claim.
    /// </summary>
    [Fact]
    public void RejectsAVerifiedRecordingWithNowhereToEnterAFightItHolds()
    {
        var manifest = WithVerifiedTwoFightTrace(Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest))],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("holds fight 2 and boundaries declares no combat_start", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsAVerifiedRecordingWithABoundaryForEveryFightItHolds()
    {
        var manifest = WithVerifiedTwoFightTrace(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 0, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsAVerifiedRecordingWhoseFightOrdinalPointsToAnotherBoundary()
    {
        var manifest = WithVerifiedTwoFightTrace(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(2, 0, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("fight 1 after action 0", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAVerifiedRecordingWithABoundaryForAFightItDoesNotHold()
    {
        var manifest = WithVerifiedTwoFightTrace(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 0, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(2, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(3, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("start of fight 3", StringComparison.Ordinal) &&
            problem.Contains("holds no fight with that ordinal", StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotTreatAPartialTraceAsRecordingAuthority()
    {
        var verified = WithVerifiedTwoFightTrace(Fixtures.ValidManifest());
        var manifest = verified with
        {
            Verification = verified.Verification! with { Status = VerificationStatus.Partial },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsACombatStartForAFightTheRecordingNeverFinishes()
    {
        var manifest = WithTwoFinishedFightsAndAnUnfinishedThird(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 0, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(2, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(3, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("the start of fight 3", StringComparison.Ordinal) &&
            problem.Contains("never finishes that fight", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsBoundariesForOnlyTheFightsTheRecordingFinishes()
    {
        var manifest = WithTwoFinishedFightsAndAnUnfinishedThird(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 0, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void AcceptsBoundariesTheVerifiedTraceReallyReaches()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(
            Walked(Fixtures.ValidManifest() with { Boundaries = WalkedBoundaries }));

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>
    /// The one this exists for. A floor entry the run never arrives on used to be
    /// checked for shape only, so it passed validate and gate and was refused later as
    /// an aborted entry, in front of a player - and it is now a floor a host may
    /// restore from a cache rather than walk to.
    /// </summary>
    [Fact]
    public void RejectsAFloorEntryForAFloorTheRecordingNeverReaches()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                .. WalkedBoundaries,
                ReplayBoundary.FloorEntry(7, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("arrival on floor 7", StringComparison.Ordinal) &&
            problem.Contains("never reaches that floor", StringComparison.Ordinal));
    }

    /// <summary>The floor a run opens on is not arrived at, so no map move enters it
    /// and no plan could put anybody there. It is the one floor the trace holds that
    /// is not a boundary.</summary>
    [Fact]
    public void RejectsAFloorEntryOnTheFloorTheRunStartsOn()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                .. WalkedBoundaries,
                ReplayBoundary.FloorEntry(1, -1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("arrival on floor 1", StringComparison.Ordinal) &&
            problem.Contains("starts on that floor rather than arriving on it", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAFloorEntryAtAnActionOtherThanTheArrival()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 2, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("enters floor 2 after action 1", StringComparison.Ordinal) &&
            problem.Contains("names action 2", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATurnStartForAFightTheRecordingDoesNotHold()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                .. WalkedBoundaries,
                ReplayBoundary.TurnStart(2, 1, 2, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("turn 1 of fight 2", StringComparison.Ordinal) &&
            problem.Contains("holds no fight with that ordinal", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATurnStartForATurnTheFightNeverReaches()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                .. WalkedBoundaries,
                ReplayBoundary.TurnStart(1, 5, 2, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("turn 5 of fight 1", StringComparison.Ordinal) &&
            problem.Contains("never reaches turn 5 of fight 1", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATurnStartAtAnActionOtherThanTheTurnItNames()
    {
        var manifest = WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.TurnStart(1, 2, 0, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("starts turn 2 of fight 1 after action 2", StringComparison.Ordinal) &&
            problem.Contains("names action 0", StringComparison.Ordinal));
    }

    /// <summary>Same rule the combat_start check applies, asked of a turn: a fight the
    /// recording stops in the middle of has no completed line to compare against, so a
    /// turn of it is a place nobody could be stood either.</summary>
    [Fact]
    public void RejectsATurnStartInAFightTheRecordingNeverFinishes()
    {
        var manifest = WithVerifiedTrace(Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(3, 2, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.TurnStart(1, 1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.TurnStart(2, 1, 2, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }),
            RunStep(-1, floor: 1, outcome: "none"),
            RunStep(1, floor: 2, outcome: "in_progress", turn: 1),
            RunStep(1, floor: 2, outcome: "victory"),
            RunStep(2, floor: 3, outcome: "in_progress", turn: 1));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("turn 1 of fight 2", StringComparison.Ordinal) &&
            problem.Contains("never finishes that fight", StringComparison.Ordinal));
    }

    /// <summary>
    /// What <see cref="FloorEntryPlan.For"/> aborts on, refused where a submission is
    /// judged instead of in front of a player: a floor is arrived on by moving on the
    /// map, so a boundary naming any other decision is not the moment it claims.
    /// </summary>
    [Fact]
    public void RejectsAFloorEntryAtAnActionThatIsNotAMapMove()
    {
        var manifest = Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 0, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("arrival on floor 2", StringComparison.Ordinal) &&
            problem.Contains("arrived on by moving on the map", StringComparison.Ordinal));
    }

    /// <summary>
    /// Before any action is a legal place for a boundary that means it, and no map
    /// move is there - so a floor entry naming it names no decision at all, and
    /// <see cref="FloorEntryPlan.For"/> aborts on it in front of a player.
    /// </summary>
    [Fact]
    public void RejectsAFloorEntryAtAnActionTheHistoryDoesNotContain()
    {
        var manifest = WalkedHistory(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(3, -1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("arrival on floor 3", StringComparison.Ordinal) &&
            problem.Contains("which is not in this history", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAFloorEntryWithNoArrivalCheckpointAtIt()
    {
        var manifest = WalkedHistory(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("arrival on floor 2", StringComparison.Ordinal) &&
            problem.Contains("run.total_floor and run.map_coord", StringComparison.Ordinal));
    }

    /// <summary>A checkpoint naming both fields is not enough: the plan requires it to
    /// be about the floor the boundary names, and refuses at entry otherwise.</summary>
    [Fact]
    public void RejectsAnArrivalCheckpointForAnotherFloor()
    {
        var manifest = WalkedHistory(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(3, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }) with
        {
            Checkpoints = [ObservedArrival("floor-3-arrival", 1, floor: 2, coord: "r1c3")],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("arrival on floor 3", StringComparison.Ordinal) &&
            problem.Contains("run.total_floor is 2", StringComparison.Ordinal));
    }

    /// <summary>Where several checkpoints sit at one action, the plan takes the first
    /// that names every field it needs. So does this, or a manifest could satisfy the
    /// validator with a checkpoint the plan would never reach.</summary>
    [Fact]
    public void ReadsTheArrivalCheckpointAFloorPlanWouldTake()
    {
        var manifest = WalkedHistory(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }) with
        {
            Checkpoints =
            [
                ObservedArrival("first-arrival", 1, floor: 9, coord: "r1c3"),
                ObservedArrival("second-arrival", 1, floor: 2, coord: "r1c3"),
            ],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("run.total_floor is 9", StringComparison.Ordinal));
    }

    /// <summary>An arrival nobody watched is admissible because the history gives it.
    /// One the history does not give is a value nobody took.</summary>
    [Fact]
    public void RejectsAnInferredArrivalTheHistoryDoesNotGive()
    {
        var manifest = Walked(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });
        var arrival = manifest.Checkpoints.Single(checkpoint => checkpoint.Id == "floor-2-arrival");

        var result = ManifestValidator.Validate(manifest with
        {
            Checkpoints =
            [
                .. manifest.Checkpoints.Where(checkpoint => checkpoint != arrival),
                arrival with
                {
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["run.total_floor"] = arrival.Expect["run.total_floor"],
                        ["run.map_coord"] = Fact<string>.Inferred(
                            "r9c9", FactEvidence.Reasoning("the map move at action 1")),
                    },
                },
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("inferred as 'r9c9'", StringComparison.Ordinal) &&
            problem.Contains("gives 'r1c3'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The deriver's own output validates.
    ///
    /// <c>migrate-manifest --derive-boundaries</c> writes floor entries and, through
    /// this same owner, the arrival each one needs. Without that it would emit
    /// manifests its own validator refuses, and the guard and the deriver would
    /// disagree about one history.
    /// </summary>
    [Fact]
    public void DerivingArrivalsMakesADerivedFloorEntryAdmissible()
    {
        var derivedBoundariesOnly = WithVerifiedFloorAndTurnTrace(
            WalkedHistory(Fixtures.ValidManifest() with { Boundaries = WalkedBoundaries }));

        var before = ManifestValidator.Validate(derivedBoundariesOnly);
        Assert.False(before.IsValid);

        var after = ManifestValidator.Validate(FloorArrival.WithArrivalCheckpoints(derivedBoundariesOnly));
        Assert.True(after.IsValid, after.Describe());
    }

    /// <summary>
    /// The shape a recording written before the recorder sampled where the run stands
    /// has: a floor entry, and a checkpoint at it naming the floor and not the
    /// coordinate. Deriving the arrival is that recording's one repair, and what it
    /// writes is the coordinate the recorded map move moved to - never a value nobody
    /// derived. The reading already there is kept beside it.
    /// </summary>
    [Fact]
    public void DerivingAnArrivalRepairsACheckpointNamingOnlyTheFloor()
    {
        var recorded = WalkedHistory(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }) with
        {
            Checkpoints =
            [
                new Checkpoint
                {
                    Id = "floor-2-entry",
                    AfterSeq = 1,
                    Kind = "floor_entry",
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["run.total_floor"] = Fact<string>.Observed(
                            "2", FactEvidence.AtVideoTime(75600, "floor counter")),
                    },
                },
            ],
        };
        Assert.False(ManifestValidator.Validate(recorded).IsValid);

        var repaired = FloorArrival.WithArrivalCheckpoints(recorded);

        var result = ManifestValidator.Validate(repaired);
        Assert.True(result.IsValid, result.Describe());
        Assert.Equal(
            "r1c3",
            repaired.Checkpoints.Single(checkpoint => checkpoint.Id == "floor-2-arrival")
                .Expect["run.map_coord"].Value);
        Assert.Contains(repaired.Checkpoints, checkpoint => checkpoint.Id == "floor-2-entry");
    }

    /// <summary>
    /// The arrival's derivation is admissible in the arrival, and nowhere else.
    ///
    /// Several checkpoints stand at the action a floor is arrived after - a fight's
    /// start, a turn's, the recorder's own reading of the floor - and every one of them
    /// may name the floor. Only the one a floor plan would take is the arrival, so only
    /// there is a value nobody read admissible; in any of the others it is a reading
    /// that was never taken, and what a video shows is what a video checkpoint owes.
    /// </summary>
    [Fact]
    public void RefusesAnInferredReadingInAnotherCheckpointStandingAtTheArrival()
    {
        var manifest = FloorArrival.WithArrivalCheckpoints(WalkedHistory(Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        }));

        var result = ManifestValidator.Validate(manifest with
        {
            Checkpoints = [.. manifest.Checkpoints, NeighbouringFloorReading("floor-2-entry", 1, floor: 2)],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("checkpoint 'floor-2-entry' field 'run.total_floor'", StringComparison.Ordinal) &&
            problem.Contains("must be source=observed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, problem =>
            problem.Contains("floor-2-arrival", StringComparison.Ordinal));
    }

    /// <summary>The same, for a recorder's own reading: what a live game read is what a
    /// captured checkpoint owes, and the arrival beside it does not excuse it.</summary>
    [Fact]
    public void RefusesAnInferredReadingInAnotherCapturedCheckpointStandingAtTheArrival()
    {
        var manifest = FloorArrival.WithArrivalCheckpoints(Fixtures.NativeManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ],
        });
        Assert.True(ManifestValidator.Validate(manifest).IsValid, ManifestValidator.Validate(manifest).Describe());

        var result = ManifestValidator.Validate(manifest with
        {
            Checkpoints = [.. manifest.Checkpoints, NeighbouringFloorReading("floor-2-entry", 1, floor: 2)],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("checkpoint 'floor-2-entry' field 'run.total_floor'", StringComparison.Ordinal) &&
            problem.Contains("must be source=captured", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, problem =>
            problem.Contains("floor-2-arrival", StringComparison.Ordinal));
    }

    /// <summary>A checkpoint standing where a floor was arrived at and naming the floor,
    /// as a reading nobody took. It carries no coordinate, so it is not the arrival a
    /// floor plan would take.</summary>
    private static Checkpoint NeighbouringFloorReading(string id, int afterSeq, int floor) => new()
    {
        Id = id,
        AfterSeq = afterSeq,
        Kind = "floor_entry",
        Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["run.total_floor"] = Fact<string>.Inferred(
                Number(floor), FactEvidence.Reasoning("the boundary beside it names this floor")),
        },
    };

    /// <summary>
    /// The verdict the publication gate reads for its declared-boundaries condition.
    ///
    /// The file on disk carries no trace, so the cross-checks sit idle there; the copy
    /// a verified replay writes is where they fire, and this is the question the gate
    /// asks of it.
    /// </summary>
    [Fact]
    public void RefusesAVerifiedManifestWhoseBoundariesDisagreeWithItsTrace()
    {
        var refusal = ManifestValidator.RefusalForVerified(
            WithVerifiedFloorAndTurnTrace(Walked(Fixtures.ValidManifest() with
            {
                Boundaries =
                [
                    .. WalkedBoundaries,
                    ReplayBoundary.FloorEntry(7, 1, Fact<string>.Engine(Fixtures.Digest)),
                ],
            })));

        Assert.NotNull(refusal);
        Assert.Contains("never reaches that floor", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsAVerifiedManifestWhoseBoundariesAgreeWithItsTrace()
    {
        Assert.Null(ManifestValidator.RefusalForVerified(
            WithVerifiedFloorAndTurnTrace(
                Walked(Fixtures.ValidManifest() with { Boundaries = WalkedBoundaries }))));
    }

    /// <summary>An arrival a video reading took, for a rule that is about the floor it
    /// names rather than about how it was established.</summary>
    private static Checkpoint ObservedArrival(string id, int afterSeq, int floor, string coord) => new()
    {
        Id = id,
        AfterSeq = afterSeq,
        Kind = "floor_entry",
        Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["run.total_floor"] = Fact<string>.Observed(
                Number(floor), FactEvidence.AtVideoTime(75600, "floor counter")),
            ["run.map_coord"] = Fact<string>.Observed(
                coord, FactEvidence.AtVideoTime(75600, "ringed node")),
        },
    };

    /// <summary>The same manifest over a history its floor entries can be declared
    /// against: a second map move, so there is a floor to arrive on after one.</summary>
    private static ReplayManifest WalkedHistory(ReplayManifest manifest) => manifest with
    {
        Actions =
        [
            .. manifest.Actions,
            new ActionRecord
            {
                Seq = 2,
                Verb = ActionVerb.MapMove,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["act"] = "0",
                    ["row"] = "2",
                    ["column"] = "4",
                },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(120000, "ringed node on the map"),
            },
        ],
    };

    /// <summary>That history with the arrival every floor_entry in it needs, built
    /// through the owner the validator re-derives with.</summary>
    private static ReplayManifest Walked(ReplayManifest manifest) =>
        FloorArrival.WithArrivalCheckpoints(WalkedHistory(manifest));

    /// <summary>Every boundary the trace below really reaches, which is what the
    /// derive path would produce from it.</summary>
    private static readonly ReplayBoundary[] WalkedBoundaries =
    [
        ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
        ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
        ReplayBoundary.TurnStart(1, 1, 1, Fact<string>.Engine(Fixtures.Digest)),
        ReplayBoundary.TurnStart(1, 2, 2, Fact<string>.Engine(Fixtures.Digest)),
    ];

    /// <summary>A verified result whose trace starts on floor 1, moves to floor 2 and
    /// wins a two-turn fight there.</summary>
    private static ReplayManifest WithVerifiedFloorAndTurnTrace(ReplayManifest manifest) =>
        WithVerifiedTrace(manifest,
            RunStep(-1, floor: 1, outcome: "none"),
            RunStep(1, floor: 2, outcome: "in_progress", turn: 1),
            RunStep(2, floor: 2, outcome: "in_progress", turn: 2),
            RunStep(2, floor: 2, outcome: "victory"));

    /// <summary>A step carrying the floor and turn as well as the outcome. Only the
    /// after side is set: coverage reads the sample after each action, and a before
    /// side written here would say something no rule under test asks.</summary>
    private static ReplayStep RunStep(int seq, int floor, string outcome, int? turn = null)
    {
        var after = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.outcome"] = outcome,
            ["run.total_floor"] = Number(floor),
        };
        if (turn is { } number) after["combat.turn"] = Number(number);

        return new ReplayStep
        {
            Seq = seq,
            Verb = "PlayCard",
            Before = new Dictionary<string, string>(StringComparer.Ordinal),
            After = after,
        };
    }

    private static string Number(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A verified result whose trace won two fights and stopped inside a
    /// third, the shape of a recording that ends mid-fight.</summary>
    private static ReplayManifest WithTwoFinishedFightsAndAnUnfinishedThird(ReplayManifest manifest) =>
        WithVerifiedTrace(manifest,
            TraceStep(-1, "none", "none"),
            TraceStep(0, "none", "in_progress"),
            TraceStep(0, "in_progress", "victory"),
            TraceStep(1, "victory", "in_progress"),
            TraceStep(1, "in_progress", "victory"),
            TraceStep(1, "victory", "in_progress"));

    /// <summary>
    /// A recording that stops two turns into a fight offers nowhere to be stood in it:
    /// there is no completed recorded line to compare a player against, and
    /// recorded-fight cuts only finished fights. Demanding a boundary for it would
    /// make a rule nobody could satisfy.
    /// </summary>
    [Fact]
    public void DoesNotDemandABoundaryForAFightTheRecordingStopsInTheMiddleOf()
    {
        var manifest = WithVerifiedTrace(Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 0, Fact<string>.Engine(Fixtures.Digest))],
        },
            TraceStep(-1, "none", "none"),
            TraceStep(0, "none", "in_progress"),
            TraceStep(1, "in_progress", "victory"),
            TraceStep(1, "victory", "in_progress"));

        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>
    /// The coverage rule is only ever asked of a manifest that already carries a
    /// verified trace, and nothing re-validates what `replay --out` writes, so a
    /// manifest as published is not subject to it. That gap is deliberate for now:
    /// deriving the remaining boundaries and putting this check on the publication
    /// path is the next phase's work.
    /// </summary>
    [Fact]
    public void DoesNotAskForBoundaryCoverageOfAManifestWithNoVerification()
    {
        var underCovered = Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest))],
        };
        Assert.False(ManifestValidator.Validate(WithVerifiedTwoFightTrace(underCovered)).IsValid);

        var result = ManifestValidator.Validate(underCovered with { Verification = null });
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>A verified result whose trace entered combat, won, entered a second
    /// and won that too. Only the outcome samples matter to the rule under test, so
    /// the steps carry nothing else.</summary>
    private static ReplayManifest WithVerifiedTwoFightTrace(ReplayManifest manifest) =>
        WithVerifiedTrace(manifest,
            TraceStep(-1, "none", "none"),
            TraceStep(0, "none", "in_progress"),
            TraceStep(1, "in_progress", "victory"),
            TraceStep(1, "victory", "in_progress"),
            TraceStep(1, "in_progress", "victory"));

    private static ReplayManifest WithVerifiedTrace(
        ReplayManifest manifest, params ReplayStep[] steps) => manifest with
        {
            Verification = new VerificationReport
            {
                Status = VerificationStatus.Verified,
                ArbiterVersion = "test",
                Preflight = new PreflightResult(true, []),
                Trace = new ReplayTrace { Steps = steps },
            },
        };

    private static ReplayStep TraceStep(int seq, string before, string after) => new()
    {
        Seq = seq,
        Verb = "PlayCard",
        Before = new Dictionary<string, string>(StringComparer.Ordinal) { ["combat.outcome"] = before },
        After = new Dictionary<string, string>(StringComparer.Ordinal) { ["combat.outcome"] = after },
    };

    /// <summary>
    /// A generated fixture may declare boundaries. What may be published is the gate's
    /// question and it refuses a synthetic source outright; what a boundary means is
    /// where a host can start, which is exactly what a fixture with no video behind it
    /// is for testing.
    /// </summary>
    [Fact]
    public void AcceptsASyntheticFixtureThatDeclaresABoundary()
    {
        var manifest = Fixtures.SyntheticManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest))],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>And is not required to: most fixtures reach no fight worth standing
    /// anybody in, and a recording is what has to carry one.</summary>
    [Fact]
    public void AcceptsASyntheticFixtureThatDeclaresNoBoundaryAtAll()
    {
        var result = ManifestValidator.Validate(Fixtures.SyntheticManifest());

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
    [InlineData("channel_name")]
    public void RejectsAnEmptyVodVideoIdentityField(string field)
    {
        var manifest = Fixtures.ValidManifest();
        var video = manifest.Source.Video!;
        var changed = field switch
        {
            "platform" => video with { Platform = "" },
            "video_id" => video with { VideoId = "" },
            "channel_id" => video with { ChannelId = "" },
            "channel_name" => video with { ChannelName = "" },
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
    [InlineData("linked_set")]
    public void RejectsAKindOfRewardThatIsNotClaimedWithOneClick(string kind)
    {
        // 'card' is on this list on purpose: the card reward opens a second screen, so
        // taking it is TakeCard, which records which card came back. Letting it through
        // here would lose that. A linked set is a container over other rewards and no
        // singleplayer path on this build constructs one, so it is not a kind either.
        var manifest = WithActions(Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", kind)));

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("'reward_type'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The two kinds that claim a thing a build could have changed name it, and the
    /// three that do not may not carry a name.
    ///
    /// The same rule a shop purchase follows, for the same reason: a loot screen
    /// stocked differently means the run has already diverged, and taking whatever
    /// sits there would hide it. Gold, a potion and a card removal have nothing to
    /// name; the card that comes off a removal's screen is a separate selection.
    /// </summary>
    [Theory]
    [InlineData(RewardKinds.Relic, "relic_id")]
    [InlineData(RewardKinds.SpecialCard, "card_id")]
    public void AClaimedThingIsNamed(string kind, string idArgument)
    {
        var unnamed = ManifestValidator.Validate(
            WithActions(Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", kind))));
        Assert.False(unnamed.IsValid);
        Assert.Contains(unnamed.Problems, p =>
            p.Contains($"claims a '{kind}' reward and is missing required argument '{idArgument}'", StringComparison.Ordinal));

        var named = ManifestValidator.Validate(
            WithActions(Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", kind), (idArgument, "SOME.ID"))));
        Assert.True(named.IsValid, named.Describe());
    }

    [Theory]
    [InlineData(RewardKinds.Gold)]
    [InlineData(RewardKinds.Potion)]
    [InlineData(RewardKinds.CardRemoval)]
    public void AClaimWithNothingToNameMayNotNameAnything(string kind)
    {
        var bare = ManifestValidator.Validate(
            WithActions(Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", kind))));
        Assert.True(bare.IsValid, bare.Describe());

        var named = ManifestValidator.Validate(
            WithActions(Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", kind), ("relic_id", "RELIC.ANCHOR"))));
        Assert.False(named.IsValid);
        Assert.Contains(named.Problems, p =>
            p.Contains($"claims a '{kind}' reward and carries 'relic_id'", StringComparison.Ordinal));
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
        // that quietly did nothing would be the worst of both worlds, so the one with
        // no mapping has to fail at ingestion rather than at replay. Selecting hand
        // cards is it: every card screen is answered through the one seam by position,
        // which SelectCardFromScreen already names, so there is nothing behind the
        // verb to run.
        var manifest = WithActions(Fixtures.Action(0, ActionVerb.SelectHandCards));

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

/// <summary>
/// The rules for a recording this project's own recorder made.
///
/// The two that cannot move downstream are the point of this file. A recorder that
/// joined a run late, and one that stopped and started again, both produce a history
/// that replays perfectly against a run that is not the one it describes - the native
/// counterparts of the resumed run and the missing end-of-run reading.
/// </summary>
public class NativeManifestValidatorTests
{
    [Fact]
    public void AcceptsAWellFormedNativeRecording()
    {
        var result = ManifestValidator.Validate(Fixtures.NativeManifest());
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsANativeRecordingWithoutExactUnlocks()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                Unlocks = Fact<UnlockRequirement>.Captured(
                    UnlockRequirement.Complete("inferred complete"),
                    FactEvidence.AtActionOrdinal(0)),
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must be 'exact' for a native recording", StringComparison.Ordinal) &&
            problem.Contains("reads the unlock state", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordingWhoseRecorderDidNotSeeTheRunBegin()
    {
        var result = Validate(Fixtures.NativeSourceBlock(witnessedStart: false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("witnessed_run_start is false", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordingWithAHoleInIt()
    {
        var result = Validate(Fixtures.NativeSourceBlock(continuity: NativeSource.BrokenContinuity));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("stopped and started again", StringComparison.Ordinal));
    }

    /// <summary>A rewound recording is whole - every decision from run start was
    /// watched and the reload's branch is kept beside them - so it is not refused
    /// here; that it may never be shared is the gate's. What it must carry is the
    /// branch, and a branch marked as a reload's must be on a rewound recording.</summary>
    [Fact]
    public void ARewoundRecordingMustCarryTheReloadsBranchAndTheBranchMustBeOnARewoundRecording()
    {
        var rewoundWithoutBranch = Validate(Fixtures.NativeSourceBlock(continuity: NativeSource.RewoundContinuity));
        Assert.DoesNotContain(rewoundWithoutBranch.Problems, problem =>
            problem.Contains("stopped and started again", StringComparison.Ordinal));
        Assert.Contains(rewoundWithoutBranch.Problems, problem =>
            problem.Contains("no discarded branch is marked as the reload's", StringComparison.Ordinal));
    }

    /// <summary>Giving up is a completed recording: the run is over, the history is
    /// whole, and the fights in it were really played.</summary>
    [Theory]
    [InlineData("won")]
    [InlineData("lost")]
    [InlineData("abandoned")]
    public void AcceptsEveryWayARunCanEnd(string outcome)
    {
        var result = Validate(Fixtures.NativeSourceBlock(outcome: outcome));
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsAnOutcomeOutsideTheClosedSet()
    {
        var result = Validate(Fixtures.NativeSourceBlock(outcome: "in progress"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native.outcome", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAContinuityOutsideTheClosedSet()
    {
        var result = Validate(Fixtures.NativeSourceBlock() with { Continuity = "mostly" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native.continuity 'mostly'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordingWithNoRecorderVersion()
    {
        var result = Validate(Fixtures.NativeSourceBlock() with { RecorderVersion = "  " });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("recorder_version is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsANativeSourceWithNoNativeBlockAtAll()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Source = manifest.Source with { Native = null },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native is absent", StringComparison.Ordinal));
    }

    /// <summary>A recorder's account of itself is not a reading off a video, and a
    /// manifest carrying both is claiming two different provenances for one run.</summary>
    [Fact]
    public void RejectsANativeSourceCarryingVideoEvidence()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Source = manifest.Source with { RunStart = Fixtures.RunStart() },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("cannot carry source.run_start", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsANativeSourceWithAnotherExtractionMethod()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Source = manifest.Source with { ExtractionMethod = "manual" },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("extraction_method 'captured'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAVodSourceCarryingANativeBlock()
    {
        var manifest = Fixtures.ValidManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Source = manifest.Source with { Native = Fixtures.NativeSourceBlock() },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native must be absent", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnActionTheRecorderDidNotCapture()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Actions =
            [
                manifest.Actions[0] with { Source = FactSource.Inferred },
                .. manifest.Actions.Skip(1),
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must be source=captured for a native recording", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACapturedActionWithNoActionOrdinal()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Actions =
            [
                manifest.Actions[0] with { Evidence = FactEvidence.Reasoning("no coordinate") },
                .. manifest.Actions.Skip(1),
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("names no action_ordinal", StringComparison.Ordinal));
    }

    /// <summary>The ordinal is the run's own coordinate for the moment, so one that
    /// disagrees with the sequence number means the history was reordered after it was
    /// recorded - which is a different run.</summary>
    [Fact]
    public void RejectsAnOrdinalThatDisagreesWithTheSequenceNumber()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Actions =
            [
                manifest.Actions[0] with { Evidence = FactEvidence.AtActionOrdinal(1) },
                .. manifest.Actions.Skip(1),
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("captured at action ordinal 1", StringComparison.Ordinal) &&
            problem.Contains("belongs after action 0", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsACheckpointFieldCapturedAtItsCheckpointSequence()
    {
        var result = ManifestValidator.Validate(Fixtures.NativeManifest());
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsACheckpointFieldTheRecorderDidNotCapture()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Checkpoints =
            [
                manifest.Checkpoints[0] with
                {
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.energy"] = Fact<string>.Observed("3", FactEvidence.AtVideoTime(1000, "orb")),
                    },
                },
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must be source=captured", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACheckpointFieldCapturedAtAnotherAction()
    {
        var manifest = Fixtures.NativeManifest();
        var checkpoint = manifest.Checkpoints[0];
        var result = ManifestValidator.Validate(manifest with
        {
            Checkpoints =
            [
                checkpoint with
                {
                    Expect = checkpoint.Expect.ToDictionary(
                        entry => entry.Key,
                        entry => Fact<string>.Captured(entry.Value.Value, FactEvidence.AtActionOrdinal(0)),
                        StringComparer.Ordinal),
                },
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("captured at action ordinal 0", StringComparison.Ordinal) &&
            problem.Contains("belongs after action 1", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnEnvironmentFieldTheRecorderDidNotCapture()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                Seed = Fact<string>.Inferred("SFXT47K77RFK", FactEvidence.Reasoning("guessed")),
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("environment.seed in a native recording", StringComparison.Ordinal));
    }

    /// <summary>
    /// Captured is what a recorder read inside the game it was running in, and a video
    /// reconstruction had no recorder. A captured coordinate on one names a reading
    /// nobody could have taken, and it would sidestep the video timestamp every other
    /// vod fact is re-checked at.
    /// </summary>
    [Fact]
    public void RejectsACapturedEnvironmentFieldOnAVideoRecording()
    {
        var manifest = Fixtures.ValidManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                BuildVersion = Fact<string>.Captured("v0.111.0", FactEvidence.AtActionOrdinal(0)),
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains(
                "environment.build_version is marked source=captured and this is a vod recording",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACapturedEnvironmentFieldOnASyntheticFixture()
    {
        var manifest = Fixtures.SyntheticManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                BuildVersion = Fact<string>.Captured(
                    manifest.Environment.BuildVersion.Value, FactEvidence.AtActionOrdinal(0)),
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains(
                "is marked source=captured and this is a synthetic-engine recording",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACapturedActionOnAVideoRecording()
    {
        var manifest = Fixtures.ValidManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Actions =
            [
                manifest.Actions[0] with
                {
                    Source = FactSource.Captured,
                    Evidence = FactEvidence.AtActionOrdinal(0),
                },
                .. manifest.Actions.Skip(1),
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must be source=observed for a VOD replay", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACapturedCheckpointFieldOnAVideoRecording()
    {
        var manifest = Fixtures.ValidManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Checkpoints =
            [
                manifest.Checkpoints[0] with
                {
                    Expect = manifest.Checkpoints[0].Expect.ToDictionary(
                        entry => entry.Key,
                        entry => Fact<string>.Captured(entry.Value.Value, FactEvidence.AtActionOrdinal(1)),
                        StringComparer.Ordinal),
                },
            ],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("must be source=observed because it is evidence about what the video shows",
                StringComparison.Ordinal));
    }

    /// <summary>A run has no public clock, so a captured value's coordinate is the
    /// run's own ordered history. Without one nobody could go back and look.</summary>
    [Fact]
    public void RejectsACapturedEnvironmentFieldWithNoActionOrdinal()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                Seed = Fact<string>.Captured("SFXT47K77RFK", FactEvidence.Reasoning("no coordinate")),
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("environment.seed is captured and names no action_ordinal", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACapturedCoordinateOutsideTheActionRange()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                Seed = Fact<string>.Captured("SFXT47K77RFK", FactEvidence.AtActionOrdinal(999)),
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("environment.seed was captured at action ordinal 999", StringComparison.Ordinal) &&
            problem.Contains("outside the action range [-1, 1]", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsANegativeCapturedRunClock()
    {
        var native = Fixtures.NativeSourceBlock() with
        {
            WitnessedRunStart = Fact<bool>.Captured(
                true, FactEvidence.AtActionOrdinal(0, runClockMs: -1)),
        };

        var result = Validate(native);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native.witnessed_run_start", StringComparison.Ordinal) &&
            problem.Contains("run_clock_ms=-1", StringComparison.Ordinal));
    }

    /// <summary>An identifier this project chose is declared rather than captured, and
    /// stays acceptable.</summary>
    [Fact]
    public void AcceptsADeclaredEnvironmentConstant()
    {
        var manifest = Fixtures.NativeManifest();
        var result = ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with { GameMode = Fact<string>.Declared("standard") },
        });

        Assert.True(result.IsValid, result.Describe());
    }

    // ── Bookmarks ──────────────────────────────────────────────────────────

    /// <summary>The fixture's one fight has a combat_start after action 1 and the
    /// history holds actions 0 and 1, so a declared mark pressed at action 1 is the
    /// shape the recorder writes.</summary>
    [Fact]
    public void AcceptsADeclaredBookmarkOnAFightTheRecordingFinishes()
    {
        var result = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [Bookmark(1, pressedAt: 1)] });

        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsABookmarkOnAFightWithNoCombatStart()
    {
        var result = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [Bookmark(2, pressedAt: 1)] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native.bookmarks[0] names fight 2", StringComparison.Ordinal) &&
            problem.Contains("declares no combat_start for it", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsABookmarkThatIsNotDeclared()
    {
        var captured = new FightBookmark
        {
            Fight = 1,
            Bookmarked = Fact<bool>.Captured(true, FactEvidence.AtActionOrdinal(1)),
        };

        var result = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [captured] });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native.bookmarks[0].bookmarked is captured", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsABookmarkPressedBeforeItsFightOrAfterTheHistoryEnds()
    {
        var early = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [Bookmark(1, pressedAt: 0)] });
        Assert.Contains(early.Problems, problem =>
            problem.Contains("A fight cannot be bookmarked before it happened", StringComparison.Ordinal));

        var late = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [Bookmark(1, pressedAt: 7)] });
        Assert.Contains(late.Problems, problem =>
            problem.Contains("was pressed after action 7", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsBookmarksOutOfOrderOrRepeated()
    {
        var result = Validate(Fixtures.NativeSourceBlock() with
        {
            Bookmarks = [Bookmark(1, pressedAt: 1), Bookmark(1, pressedAt: 1)],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("source.native.bookmarks[1] names fight 1 after fight 1", StringComparison.Ordinal));
    }

    /// <summary>A mark taken off is a journal line and not a manifest entry, and a
    /// recording with none leaves the field absent.</summary>
    [Fact]
    public void RejectsABookmarkThatSaysOffAndAnEmptyList()
    {
        var off = new FightBookmark { Fight = 1, Bookmarked = Fact<bool>.Declared(false) };
        var result = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [off] });
        Assert.Contains(result.Problems, problem =>
            problem.Contains("says the fight is not bookmarked", StringComparison.Ordinal));
        Assert.Contains(result.Problems, problem =>
            problem.Contains("carries no action_ordinal", StringComparison.Ordinal));

        var empty = Validate(Fixtures.NativeSourceBlock() with { Bookmarks = [] });
        Assert.Contains(empty.Problems, problem =>
            problem.Contains("source.native.bookmarks is empty", StringComparison.Ordinal));
    }

    private static FightBookmark Bookmark(int fight, int pressedAt) => new()
    {
        Fight = fight,
        Bookmarked = new Fact<bool>(true, FactSource.Declared, FactEvidence.AtActionOrdinal(pressedAt, 812_340)),
    };

    private static ManifestValidator.ValidationResult Validate(NativeSource native)
    {
        var manifest = Fixtures.NativeManifest();
        return ManifestValidator.Validate(manifest with
        {
            Source = manifest.Source with { Native = native },
        });
    }
}

/// <summary>
/// The exact unlock requirement: named ids rather than a count, because two states
/// with the same number of cards unlocked draw from different pools.
/// </summary>
public class ExactUnlockValidatorTests
{
    [Fact]
    public void AcceptsAnExactRequirementNamingEveryCategory()
    {
        var result = ManifestValidator.Validate(Fixtures.NativeManifest());
        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void RejectsAnExactRequirementWithNoInventory()
    {
        var result = WithUnlocks(new UnlockRequirement
        {
            Completeness = UnlockRequirement.ExactCompleteness,
            Basis = "read from the player's own profile",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("no inventory is present", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsAnExactRequirementForAFreshPlayer()
    {
        var result = WithUnlocks(UnlockRequirement.Exact(
            "read from the player's own profile",
            Fixtures.UnlockInventory() with { Epochs = [], EncountersSeen = [], Runs = 0 }));

        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>The run count is one of the three values the game's unlock state is
    /// constructed from, so a state cannot be built from a negative one.</summary>
    [Fact]
    public void RejectsANegativeRunCount()
    {
        var result = WithUnlocks(UnlockRequirement.Exact(
            "read from the player's own profile", Fixtures.UnlockInventory() with { Runs = -1 }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("inventory.runs is -1", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnInventoryThatNamesTheSameIdTwice()
    {
        var result = WithUnlocks(UnlockRequirement.Exact(
            "read from the player's own profile",
            Fixtures.UnlockInventory() with { Epochs = ["EPOCH.ONE", "EPOCH.ONE"] }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("names the same id more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnInventoryCarryingAnEmptyId()
    {
        var result = WithUnlocks(UnlockRequirement.Exact(
            "read from the player's own profile",
            Fixtures.UnlockInventory() with { EncountersSeen = ["ENCOUNTER.TEST", "  "] }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("contains an empty id", StringComparison.Ordinal));
    }

    /// <summary>Completeness against the build and an enumerated inventory are two
    /// different requirements, and a manifest carrying both leaves the reader to
    /// decide which one was meant.</summary>
    [Fact]
    public void RejectsACompleteRequirementThatAlsoNamesAnInventory()
    {
        var result = WithUnlocks(UnlockRequirement.Complete("experienced creator") with
        {
            Inventory = Fixtures.UnlockInventory(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("names an inventory alongside completeness 'complete'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACompletenessOutsideTheExpressibleSet()
    {
        var result = WithUnlocks(new UnlockRequirement { Completeness = "partial", Basis = "test" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("is not one of: complete, exact", StringComparison.Ordinal));
    }

    private static ManifestValidator.ValidationResult WithUnlocks(UnlockRequirement requirement)
    {
        var manifest = Fixtures.NativeManifest();
        return ManifestValidator.Validate(manifest with
        {
            Environment = manifest.Environment with
            {
                Unlocks = Fact<UnlockRequirement>.Captured(requirement, FactEvidence.AtActionOrdinal(0)),
            },
        });
    }
    /// <summary>
    /// A native recording states its integrity, and states one of the three the
    /// format names.
    ///
    /// Required rather than optional from version 6: a file that states none is a
    /// version-5 file, and the migration is what says it is complete - by reading, not
    /// by this validator inventing a reading nothing took.
    /// </summary>
    [Fact]
    public void ANativeRecordingStatesItsIntegrityAndMayNotStateAnUnknownOne()
    {
        var manifest = Fixtures.NativeManifest();

        Assert.True(ManifestValidator.Validate(
            WithIntegrity(manifest, NativeSource.CompleteIntegrity)).IsValid);

        var invented = ManifestValidator.Validate(WithIntegrity(manifest, "probably-fine"));
        Assert.False(invented.IsValid);
        Assert.Contains(invented.Problems, problem =>
            problem.Contains("source.native.integrity 'probably-fine' is not one of", StringComparison.Ordinal));
    }

    private static ReplayManifest WithIntegrity(ReplayManifest manifest, string integrity) =>
        manifest with
        {
            Source = manifest.Source with
            {
                Native = manifest.Source.Native! with { Integrity = integrity },
            },
        };

}
