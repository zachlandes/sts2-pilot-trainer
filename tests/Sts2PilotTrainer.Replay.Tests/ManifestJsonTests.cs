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
    [InlineData(7)]
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
    /// A version-5 video manifest reads as version 6 with only its version changed:
    /// nothing in it was missing, and the migration invents nothing.
    /// </summary>
    [Fact]
    public void ReadsAVersionFiveManifestAsItsVersionSixMeaning()
    {
        var original = Fixtures.ValidManifest();
        var migrated = ManifestJson.Deserialize(VersionFive(original));

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        Assert.Null(migrated.Source.Native);
        Assert.Equal(ManifestJson.Serialize(original), ManifestJson.Serialize(migrated));
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
    public void ReadsAVersionFiveNativeManifestAsItsVersionSixMeaning()
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

        // Byte-identical everywhere else: re-serialised, only the two fields differ.
        var expected = ManifestJson.Serialize(original with
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
    private static string VersionFive(ReplayManifest manifest)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(ManifestJson.Serialize(manifest))!.AsObject();
        document["manifest_version"] = ManifestJson.PreviousManifestVersion;
        return document.ToJsonString();
    }
}
