namespace Sts2PilotTrainer.Replay.Tests;

public class ManifestJsonTests
{
    [Fact]
    public void RoundTripsAManifestWithoutLosingProvenance()
    {
        var original = Fixtures.ValidManifest();

        var restored = ManifestJson.Deserialize(ManifestJson.Serialize(original));

        Assert.Equal(original.RunId, restored.RunId);
        Assert.Equal(original.Environment.Seed.Value, restored.Environment.Seed.Value);
        Assert.Equal(FactSource.Observed, restored.Environment.Seed.Source);
        Assert.Equal(FactSource.Inferred, restored.Environment.GameMode.Source);
        Assert.Equal(
            original.Environment.Seed.Evidence!.VideoTimeMs,
            restored.Environment.Seed.Evidence!.VideoTimeMs);
        Assert.Equal(original.Actions.Count, restored.Actions.Count);
        Assert.Equal(original.Actions[1].Args["column"], restored.Actions[1].Args["column"]);
    }

    /// <summary>A bookmark travels with the run: the field round-trips with its
    /// provenance and its coordinates, and is absent from the text where there is
    /// none.</summary>
    [Fact]
    public void RoundTripsABookmarkAndOmitsTheFieldWhereThereIsNone()
    {
        var manifest = Fixtures.NativeManifest();
        Assert.DoesNotContain("\"bookmarks\"", ManifestJson.Serialize(manifest), StringComparison.Ordinal);

        var marked = manifest with
        {
            Source = manifest.Source with
            {
                Native = manifest.Source.Native! with
                {
                    Bookmarks =
                    [
                        new FightBookmark
                        {
                            Fight = 1,
                            Bookmarked = new Fact<bool>(
                                true, FactSource.Declared, FactEvidence.AtActionOrdinal(1, 812_340)),
                        },
                    ],
                },
            },
        };

        var restored = ManifestJson.Deserialize(ManifestJson.Serialize(marked));

        var bookmark = Assert.Single(restored.Source.Native!.Bookmarks!);
        Assert.Equal(1, bookmark.Fight);
        Assert.Equal(FactSource.Declared, bookmark.Bookmarked.Source);
        Assert.Equal(1, bookmark.Bookmarked.Evidence!.ActionOrdinal);
        Assert.Equal(812_340, bookmark.Bookmarked.Evidence.RunClockMs);
        Assert.True(restored.Source.Native.IsBookmarked(1));
        Assert.False(restored.Source.Native.IsBookmarked(2));
    }

    [Fact]
    public void OmitsAnEmptyRngClassificationFromAnAction()
    {
        var action = Fixtures.Action(0, ActionVerb.EndTurn) with { ConsumesRng = [] };

        var document = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(action, ManifestJson.Options))!.AsObject();

        Assert.False(document.ContainsKey("consumes_rng"));
    }

    [Fact]
    public void PreservesANonemptyRngClassificationOnAnAction()
    {
        var action = Fixtures.Action(0, ActionVerb.EndTurn) with { ConsumesRng = ["Shuffle"] };
        var json = System.Text.Json.JsonSerializer.Serialize(action, ManifestJson.Options);

        var restored = ManifestJson.DeserializeRequired<ActionRecord>(json, "Action");

        Assert.Equal(["Shuffle"], restored.ConsumesRng);
    }

    [Fact]
    public void RefusesANullRequiredManifestMember()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            ManifestJson.Serialize(Fixtures.ValidManifest()))!.AsObject();
        document["environment"] = null;

        var thrown = Assert.Throws<ManifestException>(() => ManifestJson.Deserialize(document.ToJsonString()));

        Assert.Contains("Manifest.environment is required", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"manifest_version\":\"two\"}")]
    public void RefusesMalformedOrMisshapenManifestJson(string json)
    {
        var thrown = Assert.Throws<ManifestException>(() => ManifestJson.Deserialize(json));

        Assert.Contains("Manifest JSON is invalid", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[null]")]
    [InlineData("[{\"seq\":0,\"verb\":\"Unknown\",\"source\":\"Observed\"}]")]
    public void RefusesMalformedLineArtifacts(string json)
    {
        var thrown = Assert.Throws<ManifestException>(() =>
            ManifestJson.DeserializeRequired<List<ActionRecord>>(json, "Line file test.line.json"));

        Assert.Contains("Line file test.line.json", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAManifestWithNoVersion()
    {
        var thrown = Assert.Throws<ManifestException>(() => ManifestJson.Deserialize("""{"run_id":"x"}"""));
        Assert.Contains("Refusing to guess", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(99)]
    public void RefusesAVersionThisBuildDoesNotRead(int version)
    {
        // Reading a newer manifest partially is how a replay ends up exact-looking and
        // wrong: the fields this build understands would all agree. An older one this
        // build has no migration for is refused for the mirror-image reason.
        var thrown = Assert.Throws<ManifestException>(
            () => ManifestJson.Deserialize(
                $$"""{"manifest_version":{{version}},"run_id":"x"}"""));
        Assert.Contains("not supported", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A version-5 video manifest reads as the current version with its version changed
    /// and each boundary marked as the format-5 claim its digest is: nothing in it was
    /// missing, and the migration invents nothing.
    /// </summary>
    [Fact]
    public void ReadsAVersionFiveManifestAsItsCurrentMeaning()
    {
        var original = Fixtures.ValidManifest();
        var migrated = ManifestJson.Deserialize(VersionFive(original));

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        Assert.Null(migrated.Source.Native);
        Assert.Equal(
            ManifestJson.Serialize(WithBoundariesHashedUnder(original, 5)),
            ManifestJson.Serialize(migrated));
        Assert.True(ManifestValidator.Validate(migrated).IsValid);
    }

    /// <summary>
    /// A version-5 native manifest gains the integrity it could not state and a note
    /// that it was migrated, and nothing else.
    ///
    /// <c>complete</c> is a reading rather than an invention: a version-5 recorder had
    /// no unmapped stop and refused rather than stopping at anything it could not name.
    /// The option keys a version-5 recorder never read are not invented either - the
    /// history hash covers the arguments, and adding one would unbind every recorded
    /// fight - which is what the migration note is for: the validator waives the key
    /// for a file that says it was written before the key existed.
    /// </summary>
    [Fact]
    public void ReadsAVersionFiveNativeManifestAsItsCurrentMeaning()
    {
        var original = Fixtures.NativeManifest() with
        {
            Actions = [.. Fixtures.NativeManifest().Actions.Select(action => action with
            {
                Args = new SortedDictionary<string, string>(
                    action.Args.Where(arg => arg.Key != "option_key")
                        .ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal),
            })],
        };
        var document = System.Text.Json.Nodes.JsonNode.Parse(VersionFive(original))!.AsObject();
        document["source"]!["native"]!.AsObject().Remove("integrity");

        var migrated = ManifestJson.Deserialize(document.ToJsonString());

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        Assert.Equal(NativeSource.CompleteIntegrity, migrated.Source.Native!.Integrity);
        Assert.Equal(5, migrated.Source.Native.MigratedFromVersion);
        Assert.Null(migrated.Source.Native.Unmapped);
        Assert.All(migrated.Actions, action => Assert.False(action.Args.ContainsKey("option_key")));

        // Byte-identical everywhere else: re-serialised, only the two fields and
        // the boundaries' own projection differ.
        var expected = ManifestJson.Serialize(WithoutRunEndRoster(WithBoundariesHashedUnder(original, 5)) with
        {
            Source = original.Source with
            {
                Native = original.Source.Native! with
                {
                    Integrity = NativeSource.CompleteIntegrity,
                    MigratedFromVersion = 5,
                },
            },
        });
        Assert.Equal(expected, ManifestJson.Serialize(migrated));

        var result = ManifestValidator.Validate(migrated);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>
    /// A version-5 native recording declares floor arrivals it cannot prove: its
    /// recorder sampled no map coordinate, so no checkpoint at the arrival names the
    /// fields a floor arrival is proved by, and the validator refuses it as written.
    /// The reading derives that checkpoint from the map move the boundary already
    /// names, through the one owner of the derivation, and says it was inferred. The
    /// same file read as version 6 stays refused: this is what a version-5 recorder
    /// could not write, not a waiver for anything a current one omits.
    /// </summary>
    [Fact]
    public void ReadsAVersionFiveNativeRecordingWithTheArrivalsItsRecorderCouldNotSample()
    {
        var native = Fixtures.NativeManifest();
        var arrival = ReplayBoundary.FloorEntry(2, 1, Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(1)));
        var withArrival = native with { Boundaries = [.. native.Boundaries, arrival] };
        Assert.False(ManifestValidator.Validate(withArrival).IsValid);

        var migrated = ManifestJson.Deserialize(VersionFive(withArrival));

        var checkpoint = Assert.Single(migrated.Checkpoints, checkpoint => checkpoint.Kind == FloorArrival.CheckpointKind);
        Assert.Equal(1, checkpoint.AfterSeq);
        Assert.Equal("2", checkpoint.Expect["run.total_floor"].Value);
        Assert.Equal("r1c3", checkpoint.Expect["run.map_coord"].Value);
        Assert.All(checkpoint.Expect.Values, fact => Assert.Equal(FactSource.Inferred, fact.Source));
        Assert.Equal(
            ManifestJson.Serialize(FloorArrival.WithArrivalCheckpoints(ManifestJson.Deserialize(VersionFive(withArrival)))),
            ManifestJson.Serialize(migrated));
        var result = ManifestValidator.Validate(migrated);
        Assert.True(result.IsValid, result.Describe());

        var current = ManifestJson.Deserialize(ManifestJson.Serialize(withArrival));
        Assert.DoesNotContain(current.Checkpoints, checkpoint => checkpoint.Kind == FloorArrival.CheckpointKind);
        Assert.False(ManifestValidator.Validate(current).IsValid);
    }

    /// <summary>A version-5 native file that already states an integrity keeps it:
    /// the console mark a version-5 recorder wrote is a reading, and the migration
    /// carries it as it was.</summary>
    [Fact]
    public void MigrationKeepsAnIntegrityAVersionFiveRecorderStated()
    {
        var original = Fixtures.NativeManifest() with
        {
            Source = Fixtures.NativeManifest().Source with
            {
                Native = Fixtures.NativeSourceBlock(integrity: NativeSource.NonStandardIntegrity),
            },
        };

        var migrated = ManifestJson.Deserialize(VersionFive(original));

        Assert.Equal(NativeSource.NonStandardIntegrity, migrated.Source.Native!.Integrity);
        Assert.Equal(5, migrated.Source.Native.MigratedFromVersion);
    }

    /// <summary>Migration happens in memory. The file on disk is only rewritten by
    /// the command that exists to rewrite it, so reading somebody's evidence never
    /// edits it.</summary>
    [Fact]
    public void MigratingDoesNotTouchTheProvenanceAroundTheBoundary()
    {
        var original = Fixtures.ValidManifest();
        var migrated = ManifestJson.Deserialize(VersionFive(original));

        Assert.Equal(original.RunId, migrated.RunId);
        Assert.Equal(
            original.Actions[1].Evidence!.Method, migrated.Actions[1].Evidence!.Method);
        Assert.Equal(
            original.Source.RunStart!.FirstObservedRunTimeSeconds.Value,
            migrated.Source.RunStart!.FirstObservedRunTimeSeconds.Value);
        Assert.Equal(original.Boundaries[0].Digest.Value, migrated.Boundaries[0].Digest.Value);
        Assert.Equal(original.Boundaries[0].Digest.Source, migrated.Boundaries[0].Digest.Source);
    }

    // ── A verification report, written and read back ───────────────────────

    /// <summary>
    /// A preflight verdict survives the round trip with its outcome intact, the third
    /// one included.
    ///
    /// A report is somebody's evidence and gets read again, so the finer answer has to
    /// come back as itself: a shortfall this build cannot fix reading back as an
    /// errand would put the false instruction into every later reader of the file.
    /// </summary>
    [Fact]
    public void APreflightOutcomeSurvivesBeingWrittenAndReadBack()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Verification = new VerificationReport
            {
                ArbiterVersion = "test",
                Status = VerificationStatus.Refused,
                Preflight = new PreflightResult(false,
                [
                    new PreflightField("build_version", "v0.111.0", "v0.111.0", PreflightOutcome.Met),
                    new PreflightField("ascension_unlocked", "10", "0", PreflightOutcome.NotMet, "go and play"),
                    new PreflightField("unlocks_epochs", "57", "54", PreflightOutcome.Unavailable, "not shipped"),
                ]),
                Checkpoints = [],
            },
        };

        var read = ManifestJson.Deserialize(ManifestJson.Serialize(manifest));
        var fields = read.Verification!.Preflight.Fields;

        Assert.Equal(
            [PreflightOutcome.Met, PreflightOutcome.NotMet, PreflightOutcome.Unavailable],
            fields.Select(field => field.Outcome));
        Assert.Equal([true, false, false], fields.Select(field => field.Matches));
    }

    /// <summary>
    /// A report written before the outcome existed carries only <c>matches</c>, and it
    /// reads back as the answer it recorded rather than as the enum's default.
    ///
    /// The direction that matters is the failing one: a recorded refusal defaulting to
    /// "met" would be an old report quietly turning into a pass.
    /// </summary>
    [Fact]
    public void AReportWrittenBeforeTheOutcomeExistedKeepsItsVerdict()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Verification = new VerificationReport
            {
                ArbiterVersion = "test",
                Status = VerificationStatus.Refused,
                Preflight = new PreflightResult(false,
                [
                    new PreflightField("build_version", "v0.111.0", "v0.110.0", false, "different build"),
                    new PreflightField("content_hash", "1", "1", true),
                ]),
                Checkpoints = [],
            },
        };

        var document = System.Text.Json.Nodes.JsonNode.Parse(ManifestJson.Serialize(manifest))!.AsObject();
        foreach (var field in document["verification"]!["preflight"]!["fields"]!.AsArray())
        {
            field!.AsObject().Remove("outcome");
        }

        var fields = ManifestJson.Deserialize(document.ToJsonString()).Verification!.Preflight.Fields;

        Assert.Equal(PreflightOutcome.NotMet, fields[0].Outcome);
        Assert.False(fields[0].Matches);
        Assert.Equal(PreflightOutcome.Met, fields[1].Outcome);
    }

    /// <summary>The version-5 shape of a manifest: the current shape with the version
    /// number it was written under. A native one may or may not state an integrity,
    /// which is the difference the migration reads.</summary>
    private static string VersionFive(ReplayManifest manifest) =>
        WrittenIn(manifest, ManifestJson.OldestMigratedVersion);

    private static string VersionSix(ReplayManifest manifest) =>
        WrittenIn(manifest, ManifestJson.FinishedFightResidueManifestVersion);

    private static string VersionSeven(ReplayManifest manifest) =>
        WrittenIn(manifest, ManifestJson.PreviousManifestVersion);

    /// <summary>The manifest as a file of that older version: no boundary of one
    /// says which projection its digest was hashed under, because the field arrived
    /// with format 7.</summary>
    private static string WrittenIn(ReplayManifest manifest, int version)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(ManifestJson.Serialize(manifest))!.AsObject();
        document["manifest_version"] = version;
        if (version < 7)
        {
            foreach (var boundary in document["boundaries"]!.AsArray())
            {
                boundary!.AsObject().Remove("projection");
            }
        }

        if (version < PatchRoster.RunEndIntroducedInManifestVersion)
        {
            document["environment"]?["mods"]?["Value"]?["patch_roster"]?.AsObject().Remove("run_end");
        }
        return document.ToJsonString();
    }

    private static ReplayManifest WithoutRunEndRoster(ReplayManifest manifest)
    {
        var mods = manifest.Environment.Mods;
        return mods.Value.Patches is not { } roster
            ? manifest
            : manifest with
            {
                Environment = manifest.Environment with
                {
                    Mods = mods with { Value = mods.Value with { Patches = roster with { AtRunEnd = null } } },
                },
            };
    }

    /// <summary>The manifest with every boundary digest marked as hashed under that
    /// older projection: what reading such a file produces.</summary>
    private static ReplayManifest WithBoundariesHashedUnder(ReplayManifest manifest, int version) =>
        manifest with
        {
            Boundaries = manifest.Boundaries.Select(boundary => boundary with { Projection = version }).ToList(),
        };

    /// <summary>
    /// A version-7 native recording reads without a run-end roster and says which
    /// recorder format could not have captured it.
    /// </summary>
    [Fact]
    public void ReadsAVersionSevenNativeManifestWithoutInventingARunEndRoster()
    {
        var original = Fixtures.NativeManifest();

        var migrated = ManifestJson.Deserialize(VersionSeven(original));

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        Assert.Equal(7, migrated.Source.Native!.MigratedFromVersion);
        Assert.Null(migrated.Environment.Mods.Value.Patches!.AtRunEnd);
        var result = ManifestValidator.Validate(migrated);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>
    /// A version-6 video manifest whose checkpoints were all taken inside a live
    /// fight reads as version 7 with its version changed and each boundary marked as
    /// the format-6 claim its digest is: nothing in it describes a finished fight,
    /// and the migration invents nothing.
    /// </summary>
    [Fact]
    public void ReadsAVersionSixManifestAsItsCurrentMeaning()
    {
        var original = Fixtures.ValidManifest();
        var migrated = ManifestJson.Deserialize(VersionSix(original));

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        Assert.Null(migrated.Source.Native);
        Assert.Equal(
            ManifestJson.Serialize(WithBoundariesHashedUnder(original, 6)),
            ManifestJson.Serialize(migrated));
        Assert.True(ManifestValidator.Validate(migrated).IsValid);
    }

    /// <summary>
    /// The mark is on the file once it is written in this format, so a file
    /// <c>migrate-manifest</c> rewrote without replaying still says which of its
    /// digests are older claims, where its version no longer can; a boundary this
    /// build wrote says it is this projection's without being asked.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void WhichProjectionADigestIsAClaimInSurvivesRewritingTheFileInThisFormat(int writtenIn)
    {
        var read = ManifestJson.Deserialize(WrittenIn(Fixtures.ValidManifest(), writtenIn));
        Assert.All(read.Boundaries, boundary => Assert.Equal(writtenIn, boundary.Projection));

        var rewritten = ManifestJson.Deserialize(ManifestJson.Serialize(read));
        Assert.Equal(ReplayManifest.CurrentManifestVersion, rewritten.ManifestVersion);
        Assert.All(rewritten.Boundaries, boundary => Assert.Equal(writtenIn, boundary.Projection));

        var current = ManifestJson.Deserialize(ManifestJson.Serialize(Fixtures.ValidManifest()));
        Assert.All(current.Boundaries, boundary => Assert.Equal(CanonicalState.Projection, boundary.Projection));
    }

    /// <summary>
    /// A version-6 checkpoint taken outside a live fight expects the finished fight
    /// its projection carried - its turn, its energy, a victory that outlived the
    /// room - and this projection never produces those, so the reading takes them
    /// away and keeps everything else the checkpoint observed. A checkpoint taken
    /// inside a live fight is untouched, and so is one that says nothing about
    /// whether a fight is live. A native recording says it was written in 6, so the
    /// gate and the migration can tell which of its boundaries predate this
    /// projection; a file that already said 5 keeps saying 5.
    /// </summary>
    [Fact]
    public void ReadingAVersionSixManifestTakesAwayWhatItExpectedOfAFinishedFight()
    {
        var native = Fixtures.NativeManifest();
        var original = native with
        {
            Checkpoints =
            [
                .. native.Checkpoints,
                new Checkpoint
                {
                    Id = "after-the-fight",
                    AfterSeq = 1,
                    Kind = "floor_entry",
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.in_progress"] = Fact<string>.Captured("false", FactEvidence.AtActionOrdinal(1)),
                        ["combat.outcome"] = Fact<string>.Captured("victory", FactEvidence.AtActionOrdinal(1)),
                        ["combat.turn"] = Fact<string>.Captured("4", FactEvidence.AtActionOrdinal(1)),
                        ["combat.enemy_count"] = Fact<string>.Captured("0", FactEvidence.AtActionOrdinal(1)),
                        ["player.hp"] = Fact<string>.Captured("63", FactEvidence.AtActionOrdinal(1)),
                        ["run.total_floor"] = Fact<string>.Captured("2", FactEvidence.AtActionOrdinal(1)),
                    },
                },
                new Checkpoint
                {
                    Id = "a-health-bar-mid-fight",
                    AfterSeq = 1,
                    Kind = "mid_turn",
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.player_hp"] = Fact<string>.Captured("63", FactEvidence.AtActionOrdinal(1)),
                    },
                },
            ],
        };

        var migrated = ManifestJson.Deserialize(VersionSix(original));

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        Assert.Equal(6, migrated.Source.Native!.MigratedFromVersion);
        var afterTheFight = Assert.Single(migrated.Checkpoints, checkpoint => checkpoint.Id == "after-the-fight");
        Assert.Equal(
            ["combat.in_progress", "player.hp", "run.total_floor"],
            afterTheFight.Expect.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal("false", afterTheFight.Expect["combat.in_progress"].Value);
        Assert.Equal(
            original.Checkpoints[0].Expect.Keys,
            Assert.Single(migrated.Checkpoints, checkpoint => checkpoint.Id == "combat-start").Expect.Keys);
        Assert.Equal(
            ["combat.player_hp"],
            Assert.Single(migrated.Checkpoints, checkpoint => checkpoint.Id == "a-health-bar-mid-fight").Expect.Keys);
        Assert.Equal(
            ManifestJson.Serialize(WithoutRunEndRoster(WithBoundariesHashedUnder(original, 6)) with { Checkpoints = [] }),
            ManifestJson.Serialize(migrated with
            {
                Checkpoints = [],
                Source = migrated.Source with { Native = migrated.Source.Native with { MigratedFromVersion = null } },
            }));
        var result = ManifestValidator.Validate(migrated);
        Assert.True(result.IsValid, result.Describe());

        var fromFive = ManifestJson.Deserialize(VersionFive(original));
        Assert.Equal(5, fromFive.Source.Native!.MigratedFromVersion);
        Assert.Equal(
            ["combat.in_progress", "player.hp", "run.total_floor"],
            Assert.Single(fromFive.Checkpoints, checkpoint => checkpoint.Id == "after-the-fight")
                .Expect.Keys.OrderBy(key => key, StringComparer.Ordinal));

        // A current file is read as written: the residue rule is the migration's.
        var current = ManifestJson.Deserialize(ManifestJson.Serialize(original));
        Assert.Equal(
            original.Checkpoints.Select(checkpoint => checkpoint.Expect.Count),
            current.Checkpoints.Select(checkpoint => checkpoint.Expect.Count));
    }
}
