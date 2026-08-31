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
    public void IgnoresGeneratedRowsTheObservationNeverCovered()
    {
        // The map scrolls, so a transcription legitimately covers only part of it.
        // Rows nobody read are not evidence against a candidate.
        var observation = Observation("1|0|Monster");
        var generated = Topology(16, 7, "1|0|Monster", "9|4|Treasure");

        Assert.True(observation.CompareTo(generated).Matches);
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

    private static string Write(MapObservation observation)
    {
        var directory = Path.GetFullPath(Path.Combine("build", "test-scratch"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"map-observation-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(observation, ManifestJson.Options));
        return path;
    }

    private static MapNode Parse(string spec)
    {
        var parts = spec.Split('|');
        return new MapNode(int.Parse(parts[0]), int.Parse(parts[1]), parts[2]);
    }
}
