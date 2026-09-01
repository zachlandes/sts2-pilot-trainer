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

    [Fact]
    public void RefusesAVersionThisBuildDoesNotRead()
    {
        // Reading a newer manifest partially is how a replay ends up exact-looking and
        // wrong: the fields this build understands would all agree.
        var thrown = Assert.Throws<ManifestException>(
            () => ManifestJson.Deserialize("""{"manifest_version":99,"run_id":"x"}"""));
        Assert.Contains("not supported", thrown.Message, StringComparison.Ordinal);
    }
}
