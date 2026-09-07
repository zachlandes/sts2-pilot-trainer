namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The rules format v6 added: the option key beside the index on a native recording,
/// the integrity a native source must state and what travels with it, the migration
/// note, and the arguments the five new verbs carry.
/// </summary>
public sealed class FormatSixValidatorTests
{
    // ── option_key ─────────────────────────────────────────────────────────

    [Fact]
    public void ANativeRecordingNamesEveryEventOptionByKey()
    {
        var manifest = WithoutOptionKeys(Fixtures.NativeManifest());

        var result = ManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p =>
            p.Contains("actions[0] (ChooseNeowBlessing) in a native recording names no option_key", StringComparison.Ordinal));
    }

    /// <summary>A file that says it was written before the key existed is excused,
    /// because absent there is the migration's doing rather than a recorder's
    /// omission, and nothing after the fact could capture the key.</summary>
    [Fact]
    public void AMigratedVersionFiveRecordingIsExcusedTheKeyItCouldNotHaveRead()
    {
        var manifest = WithoutOptionKeys(Fixtures.NativeManifest());
        manifest = manifest with
        {
            Source = manifest.Source with
            {
                Native = manifest.Source.Native! with { MigratedFromVersion = 5 },
            },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void AVideoRecordingMayNameAnOptionByKeyAndNeedNot()
    {
        var bare = Fixtures.ValidManifest();
        Assert.True(ManifestValidator.Validate(bare).IsValid);

        var keyed = bare with
        {
            Actions = [.. bare.Actions.Select(action => action.Verb == ActionVerb.ChooseNeowBlessing
                ? action with { Args = With(action.Args, ("option_key", "NEOW.BLESSING")) }
                : action)],
        };
        Assert.True(ManifestValidator.Validate(keyed).IsValid);
    }

    [Fact]
    public void AnEmptyOptionKeyIsRefused()
    {
        var manifest = Fixtures.NativeManifest();
        manifest = manifest with
        {
            Actions = [.. manifest.Actions.Select(action => action.Verb == ActionVerb.ChooseNeowBlessing
                ? action with { Args = With(action.Args, ("option_key", "  ")) }
                : action)],
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.Contains(result.Problems, p => p.Contains("argument 'option_key' is empty", StringComparison.Ordinal));
    }

    // ── integrity and the unmapped stop ────────────────────────────────────

    [Fact]
    public void AStopNamesWhatItStoppedAt()
    {
        var manifest = WithIntegrity(Fixtures.NativeManifest(), NativeSource.UnmappedIntegrity, unmapped: null);

        var result = ManifestValidator.Validate(manifest);

        Assert.Contains(result.Problems, p =>
            p.Contains("integrity is 'unmapped' and source.native.unmapped names nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void ANamedStopOnACompleteRecordingIsTwoClaims()
    {
        var manifest = Fixtures.NativeManifest();
        manifest = WithIntegrity(manifest, NativeSource.CompleteIntegrity, [Stop(manifest.Actions.Count)]);

        var result = ManifestValidator.Validate(manifest);

        Assert.Contains(result.Problems, p =>
            p.Contains("names 1 decision(s) and source.native.integrity is 'complete'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A recording that stopped validates as a recording and is refused for
    /// publication, with what the recorder met printed in the refusal: the one
    /// problem the validator has with it is that it may not be published.
    /// </summary>
    [Fact]
    public void AStoppedRecordingIsKeptAndRefusedForPublicationNamingWhatItMet()
    {
        var manifest = Fixtures.NativeManifest();
        manifest = WithIntegrity(manifest, NativeSource.UnmappedIntegrity, [Stop(manifest.Actions.Count)]);

        var result = ManifestValidator.Validate(manifest);

        var problem = Assert.Single(result.Problems);
        Assert.Contains("source.native.integrity is 'unmapped'", problem, StringComparison.Ordinal);
        Assert.Contains("stopped at a decision it could not name", problem, StringComparison.Ordinal);
        Assert.Contains("net_action NetMysteryAction (Mystery) with target=3", problem, StringComparison.Ordinal);
        Assert.Contains("not publishable", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AStopStandsAtTheDecisionAfterTheLast()
    {
        var manifest = Fixtures.NativeManifest();
        manifest = WithIntegrity(manifest, NativeSource.UnmappedIntegrity, [Stop(manifest.Actions.Count + 3)]);

        var result = ManifestValidator.Validate(manifest);

        Assert.Contains(result.Problems, p =>
            p.Contains($"would have been decision {manifest.Actions.Count + 3} and the history holds {manifest.Actions.Count}", StringComparison.Ordinal));
    }

    [Fact]
    public void AStopsSeamNameAndEvidenceAreChecked()
    {
        var manifest = Fixtures.NativeManifest();
        var stop = Stop(manifest.Actions.Count) with
        {
            Seam = "telepathy",
            Name = " ",
            Evidence = FactEvidence.AtActionOrdinal(0),
        };
        manifest = WithIntegrity(manifest, NativeSource.UnmappedIntegrity, [stop]);

        var result = ManifestValidator.Validate(manifest);

        Assert.Contains(result.Problems, p => p.Contains("names seam 'telepathy', which is not one of", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Contains("names nothing, so a later build could not say", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Contains("carries evidence at action ordinal 0", StringComparison.Ordinal));
    }

    [Fact]
    public void AMigrationNoteNamesAFormatThisBuildMigratesFrom()
    {
        var manifest = Fixtures.NativeManifest();
        manifest = manifest with
        {
            Source = manifest.Source with { Native = manifest.Source.Native! with { MigratedFromVersion = 4 } },
        };

        var result = ManifestValidator.Validate(manifest);

        Assert.Contains(result.Problems, p =>
            p.Contains("migrated_from_version is 4, which is not a format this build migrates from (5)", StringComparison.Ordinal));
    }

    // ── discarded fight branches ──────────────────────────────────────────

    [Fact]
    public void AVerifiedRollbackMatchesTheReplayedFightEntry()
    {
        var result = ManifestValidator.Validate(WithDiscardedRollback(1, Fixtures.Digest));

        Assert.True(result.IsValid, result.Describe());
    }

    [Theory]
    [InlineData(0, Fixtures.Digest)]
    [InlineData(1, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void AVerifiedRollbackMustIdentifyTheReplayedFightEntry(int rollbackToSeq, string rollbackDigest)
    {
        var result = ManifestValidator.Validate(WithDiscardedRollback(rollbackToSeq, rollbackDigest));

        Assert.Contains(result.Problems, problem =>
            problem.Contains("does not identify the verified room-entry state", StringComparison.Ordinal));
    }

    // ── the new verbs' arguments ───────────────────────────────────────────

    [Fact]
    public void ACrystalSphereRevealNamesAToolTheMinigameHas()
    {
        var known = ManifestValidator.Validate(WithActions(
            Fixtures.Action(0, ActionVerb.RevealCrystalSphereCell, ("tool", "big"), ("x", "3"), ("y", "4"))));
        Assert.True(known.IsValid, known.Describe());

        var unknown = ManifestValidator.Validate(WithActions(
            Fixtures.Action(0, ActionVerb.RevealCrystalSphereCell, ("tool", "medium"), ("x", "3"), ("y", "4"))));
        Assert.Contains(unknown.Problems, p =>
            p.Contains("argument 'tool' is 'medium'. Known tools: small, big", StringComparison.Ordinal));
    }

    [Fact]
    public void ABundleIsNamedByItsCards()
    {
        var empty = ManifestValidator.Validate(WithActions(
            Fixtures.Action(0, ActionVerb.SelectBundleFromScreen, ("card_ids", " "), ("option_index", "1"))));
        Assert.Contains(empty.Problems, p => p.Contains("argument 'card_ids' is empty", StringComparison.Ordinal));

        var named = ManifestValidator.Validate(WithActions(
            Fixtures.Action(0, ActionVerb.SelectBundleFromScreen,
                ("card_ids", "CARD.STRIKE_IRONCLAD,CARD.BASH,CARD.ANGER"), ("option_index", "1"),
                (Corruption.AlternativeOptionIndex, "0"))));
        Assert.True(named.IsValid, named.Describe());
    }

    [Fact]
    public void TheRemovedVerbsAreGoneFromTheAlphabet()
    {
        Assert.DoesNotContain(Enum.GetNames<ActionVerb>(), name => name is "CloseShop" or "ProceedToMap");
        Assert.Equal(22, Enum.GetValues<ActionVerb>().Length);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static ReplayManifest WithDiscardedRollback(int rollbackToSeq, string rollbackDigest)
    {
        var manifest = Fixtures.NativeManifest();
        var discardedAction = new ActionRecord
        {
            Seq = rollbackToSeq + 1,
            Verb = ActionVerb.PlayCard,
            Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["card_id"] = "CARD.STRIKE_IRONCLAD",
                ["hand_index"] = "0",
                ["target_index"] = "0",
            },
            Source = FactSource.Captured,
            Evidence = FactEvidence.AtActionOrdinal(rollbackToSeq + 1),
        };
        return manifest with
        {
            Source = manifest.Source with
            {
                Native = manifest.Source.Native! with
                {
                    Discarded =
                    [
                        new DiscardedBranch
                        {
                            RollbackToSeq = rollbackToSeq,
                            RollbackToDigest = rollbackDigest,
                            Actions = [discardedAction],
                        },
                    ],
                },
            },
            Verification = new VerificationReport
            {
                Status = VerificationStatus.Verified,
                ArbiterVersion = "test",
                Preflight = new PreflightResult(true, []),
                Trace = new ReplayTrace
                {
                    Steps =
                    [
                        TraceStep(-1, "none"),
                        TraceStep(0, "none"),
                        TraceStep(1, "in_progress"),
                        TraceStep(2, "victory"),
                    ],
                },
                Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest))],
            },
        };
    }

    private static ReplayStep TraceStep(int seq, string outcome) => new()
    {
        Seq = seq,
        Verb = "test",
        Before = new Dictionary<string, string>(StringComparer.Ordinal),
        After = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.outcome"] = outcome,
        },
    };

    private static UnmappedDecision Stop(int seq) => new()
    {
        Seq = seq,
        Seam = UnmappedDecision.NetActionSeam,
        Name = "NetMysteryAction",
        Discriminator = "Mystery",
        Args = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["target"] = "3" },
        Evidence = FactEvidence.AtActionOrdinal(seq, 12_000),
    };

    private static ReplayManifest WithIntegrity(
        ReplayManifest manifest, string integrity, IReadOnlyList<UnmappedDecision>? unmapped) =>
        manifest with
        {
            Source = manifest.Source with
            {
                Native = manifest.Source.Native! with { Integrity = integrity, Unmapped = unmapped },
            },
        };

    private static ReplayManifest WithoutOptionKeys(ReplayManifest manifest) => manifest with
    {
        Actions = [.. manifest.Actions.Select(action => action with
        {
            Args = new SortedDictionary<string, string>(
                action.Args.Where(arg => arg.Key != "option_key")
                    .ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        })],
    };

    private static IReadOnlyDictionary<string, string> With(
        IReadOnlyDictionary<string, string> args, params (string Key, string Value)[] extra)
    {
        var map = new SortedDictionary<string, string>(
            args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (key, value) in extra) map[key] = value;
        return map;
    }

    /// <summary>The valid video manifest with these actions appended, renumbered and
    /// timed after the ones it already has, so a failure is about the action under
    /// test rather than the evidence timeline.</summary>
    private static ReplayManifest WithActions(params ActionRecord[] actions)
    {
        var manifest = Fixtures.ValidManifest();
        var last = manifest.Actions[^1];
        var latest = Math.Max(
            last.Evidence!.VideoTimeMs!.Value,
            manifest.Checkpoints.SelectMany(c => c.Expect.Values).Max(fact => fact.Evidence?.VideoTimeMs ?? 0));
        var appended = actions.Select((action, index) => action with
        {
            Seq = last.Seq + 1 + index,
            Evidence = FactEvidence.AtVideoTime(latest + 1000 * (index + 1), "test fixture"),
        });
        return manifest with { Actions = [.. manifest.Actions, .. appended] };
    }
}
