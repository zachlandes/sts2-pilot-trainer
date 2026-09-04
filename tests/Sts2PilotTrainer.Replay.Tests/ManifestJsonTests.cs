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
    [InlineData(6)]
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
    /// Version 4 carried one combat-start digest on the source. It reads as the first
    /// entry of the boundary list, with its engine provenance intact - the value was
    /// produced by the engine and copying it does not make it less so.
    /// </summary>
    [Fact]
    public void ReadsAVersionFourManifestAsItsVersionFiveMeaning()
    {
        var migrated = ManifestJson.Deserialize(VersionFour(Fixtures.ValidManifest()));

        Assert.Equal(ReplayManifest.CurrentManifestVersion, migrated.ManifestVersion);
        var boundary = Assert.Single(migrated.Boundaries);
        Assert.Equal(ReplayBoundary.CombatStartKind, boundary.Kind);
        Assert.Equal(1, boundary.Fight);
        Assert.Equal(1, boundary.AfterSeq);
        Assert.Equal(FactSource.Engine, boundary.Digest.Source);
        Assert.Equal(Fixtures.Digest, boundary.Digest.Value);
        Assert.True(ManifestValidator.Validate(migrated).IsValid);
    }

    /// <summary>A version-4 fixture carried no digest, so it migrates to a manifest
    /// with no boundary rather than to one with an invented boundary.</summary>
    [Fact]
    public void MigratesAVersionFourManifestThatDeclaredNoBoundary()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(VersionFour(Fixtures.ValidManifest()))!.AsObject();
        document["source"]!.AsObject().Remove("combat_start_snapshot_digest");

        var migrated = ManifestJson.Deserialize(document.ToJsonString());

        Assert.Empty(migrated.Boundaries);
    }

    /// <summary>Migration happens in memory. The file on disk is only rewritten by
    /// the command that exists to rewrite it, so reading somebody's evidence never
    /// edits it.</summary>
    [Fact]
    public void MigratingDoesNotTouchTheProvenanceAroundTheBoundary()
    {
        var original = Fixtures.ValidManifest();
        var migrated = ManifestJson.Deserialize(VersionFour(original));

        Assert.Equal(original.RunId, migrated.RunId);
        Assert.Equal(
            original.Actions[1].Evidence!.Method, migrated.Actions[1].Evidence!.Method);
        Assert.Equal(
            original.Source.RunStart!.FirstObservedRunTimeSeconds.Value,
            migrated.Source.RunStart!.FirstObservedRunTimeSeconds.Value);
    }

    /// <summary>The version-4 shape of a manifest: version 4, one combat-start digest
    /// on the source, no boundary list.</summary>
    private static string VersionFour(ReplayManifest manifest)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            ManifestJson.Serialize(manifest with { Boundaries = [], Actions = ReachesAFight(manifest) }))!.AsObject();
        document["manifest_version"] = ManifestJson.PreviousManifestVersion;
        document["source"]!.AsObject()["combat_start_snapshot_digest"] =
            System.Text.Json.Nodes.JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(
                    Fact<string>.Engine(Fixtures.Digest), ManifestJson.Options));
        return document.ToJsonString();
    }

    /// <summary>The version-4 boundary was found by the first action that could only
    /// have been taken inside a fight, so a fixture being migrated has to reach one.</summary>
    private static IReadOnlyList<ActionRecord> ReachesAFight(ReplayManifest manifest) =>
    [
        .. manifest.Actions,
        Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")) with
        {
            Evidence = FactEvidence.AtVideoTime(80_000, "the card leaves the hand"),
        },
    ];
}
