namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The map comparison is the seed check that does not read any text. It has to be
/// discriminating in both directions: a matching map must pass, and every way a map
/// can differ must fail. A comparison that only ever says yes would "verify" every
/// candidate seed.
/// </summary>
public class MapObservationTests
{
    [Fact]
    public void AcceptsAGeneratedMapThatMatchesTheObservation()
    {
        var observation = Observation(("1|0|Monster"), ("1|3|Monster"), ("2|2|Elite"));
        var generated = Topology(16, 7, ("1|0|Monster"), ("1|3|Monster"), ("2|2|Elite"));

        var comparison = observation.CompareTo(generated);

        Assert.True(comparison.Matches, string.Join("; ", comparison.Problems));
        Assert.Equal(3, comparison.MatchedNodeCount);
    }

    [Fact]
    public void RejectsAnObservationWithNoTopologyEvidence()
    {
        var comparison = Observation().CompareTo(Topology(16, 7, "1|0|Monster"));

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, problem => problem.Contains("no nodes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"schema\":42}")]
    public void LoadRejectsMalformedOrMisshapenJson(string json)
    {
        var path = WriteJson(json);

        Assert.Throws<ManifestException>(() => MapObservation.Load(path));
    }

    [Fact]
    public void LoadRejectsAMissingSchema()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(Observation("1|0|Monster"), ManifestJson.Options))!.AsObject();
        document.Remove("schema");
        var path = WriteJson(document.ToJsonString());

        var error = Assert.Throws<ManifestException>(() => MapObservation.Load(path));

        Assert.Contains("has no 'schema'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsAnUnknownSchema()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(Observation("1|0|Monster"), ManifestJson.Options))!.AsObject();
        document["schema"] = "sts2-pilot-trainer/map-observation/v99";
        var path = WriteJson(document.ToJsonString());

        var error = Assert.Throws<ManifestException>(() => MapObservation.Load(path));

        Assert.Contains("Refusing to read it partially", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsMissingVideoIdentity()
    {
        var path = Write(Observation("1|0|Monster") with
        {
            Video = Observation("1|0|Monster").Video with { VideoId = "" },
        });

        var error = Assert.Throws<ManifestException>(() => MapObservation.Load(path));

        Assert.Contains("video_id is empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsANullRequiredMapMember()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(Observation("1|0|Monster"), ManifestJson.Options))!.AsObject();
        document["frames"] = null;
        var path = WriteJson(document.ToJsonString());

        var error = Assert.Throws<ManifestException>(() => MapObservation.Load(path));

        Assert.Contains("Map observation.frames is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsDuplicateNodeCoordinates()
    {
        var path = Write(Observation("1|0|Monster", "1|0|Elite"));

        var error = Assert.Throws<ManifestException>(() => MapObservation.Load(path));

        Assert.Contains("duplicate node coordinates", error.Message, StringComparison.Ordinal);
        Assert.Contains("row 1 column 0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsFrameTimestampsOutsideTheVideo()
    {
        var path = Write(Observation("1|0|Monster") with
        {
            Frames = [new ObservedFrame(2_050_000, [1])],
        });

        var error = Assert.Throws<ManifestException>(() => MapObservation.Load(path));

        Assert.Contains("outside video duration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesBindingToADifferentManifestVideo()
    {
        var observation = Observation("1|0|Monster");
        var other = observation.Video with { VideoId = "different-public-video" };

        var error = Assert.Throws<ManifestException>(() => observation.RequireSameVideo(other));

        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNodesThatAreNotBackedByARecordedFrame()
    {
        var observation = Observation("1|0|Monster") with
        {
            Frames = [new ObservedFrame(9000, [2])],
        };
        var generated = Topology(16, 7, "1|0|Monster", "2|3|Elite");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, problem =>
            problem.Contains("no supporting frame", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiresCompleteTopologyForEveryFrameBackedRow()
    {
        var observation = Observation("1|0|Monster") with
        {
            Frames = [new ObservedFrame(9000, [1, 2])],
        };
        var generated = Topology(16, 7, "1|0|Monster", "2|3|Elite");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, problem =>
            problem.Contains("row 2 column 3", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsANodeOfTheWrongType()
    {
        var observation = Observation("1|0|Monster");
        var generated = Topology(16, 7, "1|0|Elite");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, p => p.Contains("observed Monster, generated Elite", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAMissingNode()
    {
        var observation = Observation("1|0|Monster", "1|3|Monster");
        var generated = Topology(16, 7, "1|0|Monster");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, p => p.Contains("generated nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnExtraNodeInsideAnObservedRow()
    {
        // Without this, a map that is a superset of the observation would pass, and
        // "matches" would mean "does not contradict" rather than "is the same map".
        var observation = Observation("1|0|Monster");
        var generated = Topology(16, 7, "1|0|Monster", "1|3|Monster");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, p => p.Contains("observed nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAPopulatedGeneratedRowSilentlyOmittedFromTheObservation()
    {
        var observation = Observation("1|0|Monster") with
        {
            Frames = [new ObservedFrame(9000, [1])],
        };
        var generated = Topology(16, 7, "1|0|Monster", "9|4|Treasure");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, problem =>
            problem.Contains("generated row 9 has no frame coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnAuthorJustifiedOmissionOutsideTheStructuralAllowlist()
    {
        var observation = Observation("1|0|Monster") with
        {
            Frames = [new ObservedFrame(9000, [1])],
            NotObserved = ["Row 9, hidden behind the map scroll in every recorded frame."],
        };
        var generated = Topology(16, 7, "1|0|Monster", "9|4|Treasure");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, problem =>
            problem.Contains("not the structurally hidden run-start row 0", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowsOnlyTheStructurallyHiddenRunStartRow()
    {
        var observation = Observation("1|0|Monster") with
        {
            Frames = [new ObservedFrame(9000, [1])],
            NotObserved = ["edges"],
        };
        var generated = Topology(16, 7, "0|3|Unknown", "1|0|Monster");

        var comparison = observation.CompareTo(generated);

        Assert.True(comparison.Matches, string.Join("; ", comparison.Problems));
    }

    [Fact]
    public void RejectsAGridOfADifferentSize()
    {
        var observation = Observation("1|0|Monster");
        var generated = Topology(15, 7, "1|0|Monster");

        var comparison = observation.CompareTo(generated);

        Assert.False(comparison.Matches);
        Assert.Contains(comparison.Problems, p => p.Contains("grid size differs", StringComparison.Ordinal));
    }

    private static MapObservation Observation(params string[] nodes) => new()
    {
        Video = new VideoSource
        {
            Platform = "youtube",
            VideoId = "OJ-6QXhNgdg",
            ChannelId = "UCuuDxwofGcur0Lt6iP-aDww",
            ChannelName = "NaveGreed",
            DurationSeconds = 2049,
        },
        ActIndex = 0,
        Method = "test fixture",
        Frames = [new ObservedFrame(9000, [1, 2])],
        LegendMapping = new Dictionary<string, string>(StringComparer.Ordinal) { ["Enemy"] = "Monster" },
        Rows = 16,
        Columns = 7,
        Nodes = nodes.Select(Parse).ToList(),
        NotObserved = ["edges"],
    };

    private static MapTopology Topology(int rows, int columns, params string[] nodes) =>
        new(0, rows, columns, nodes.Select(Parse).ToList(), []);

    private static string Write(MapObservation observation) =>
        WriteJson(System.Text.Json.JsonSerializer.Serialize(observation, ManifestJson.Options));

    private static string WriteJson(string json)
    {
        var directory = Path.GetFullPath(Path.Combine("build", "test-scratch"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"map-observation-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static MapNode Parse(string spec)
    {
        var parts = spec.Split('|');
        return new MapNode(int.Parse(parts[0]), int.Parse(parts[1]), parts[2]);
    }
}
