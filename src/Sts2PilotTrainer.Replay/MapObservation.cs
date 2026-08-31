using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A map as read off a video, with the timestamps that let anyone re-check it.
///
/// This is the out-of-band check on the seed. A seed can only ever reach us as
/// text that something read off a low-contrast overlay, and a text reader that
/// agrees with itself is not the same as a text reader that is right. Regenerating
/// the map from a candidate seed and comparing it against the map the video shows
/// tests the seed against the game's own generator instead - no character
/// recognition anywhere in the loop.
///
/// Only facts are stored: node types and grid positions, plus the public video id
/// and the timestamps they were read at. No frames, no stills, no footage.
/// </summary>
public sealed record MapObservation
{
    public const string CurrentSchema = "sts2-pilot-trainer/map-observation/v1";

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = CurrentSchema;

    [JsonPropertyName("video")]
    public required VideoSource Video { get; init; }

    [JsonPropertyName("act_index")]
    public required int ActIndex { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>Which frames were read, and which rows each one settled. The map
    /// scrolls, so no single frame shows the whole act.</summary>
    [JsonPropertyName("frames")]
    public required IReadOnlyList<ObservedFrame> Frames { get; init; }

    /// <summary>
    /// How the map screen's own legend labels translate to the engine's node types.
    /// Recorded because it is an interpretation step, not an observation: the video
    /// says "Merchant" and "Enemy" where the engine says "Shop" and "Monster", and a
    /// reader deserves to see that substitution rather than discover it.
    /// </summary>
    [JsonPropertyName("legend_mapping")]
    public required IReadOnlyDictionary<string, string> LegendMapping { get; init; }

    [JsonPropertyName("rows")]
    public required int Rows { get; init; }

    [JsonPropertyName("columns")]
    public required int Columns { get; init; }

    [JsonPropertyName("nodes")]
    public required IReadOnlyList<MapNode> Nodes { get; init; }

    /// <summary>
    /// What was deliberately not read, and why. Edges are the notable one: the
    /// connecting paths are thin dashed curves that cross each other, and a
    /// transcription of them by eye would carry error the node reading does not.
    /// Recording the omission keeps a partial observation from being mistaken for
    /// a complete one.
    /// </summary>
    [JsonPropertyName("not_observed")]
    public required IReadOnlyList<string> NotObserved { get; init; }

    public static MapObservation Load(string path)
    {
        var observation = JsonSerializer.Deserialize<MapObservation>(File.ReadAllText(path), ManifestJson.Options)
            ?? throw new ManifestException($"Map observation at {Path.GetFileName(path)} deserialized to null.");

        if (observation.Schema != CurrentSchema)
        {
            throw new ManifestException(
                $"Map observation schema '{observation.Schema}' is not '{CurrentSchema}'. Refusing to read it partially.");
        }

        var videoProblems = observation.VideoProblems();
        if (videoProblems.Count > 0)
        {
            throw new ManifestException("Map observation video provenance is invalid: " + string.Join("; ", videoProblems));
        }

        if (observation.Nodes.Count == 0 || observation.Frames.SelectMany(frame => frame.RowsSettled).Distinct().Any() == false)
        {
            throw new ManifestException(
                "Map observation has no topology evidence. At least one node and one observed row are required.");
        }

        var frameBackedRows = observation.Frames.SelectMany(frame => frame.RowsSettled).ToHashSet();
        var unbackedRows = observation.Nodes.Select(node => node.Row)
            .Where(row => !frameBackedRows.Contains(row))
            .Distinct()
            .OrderBy(row => row)
            .ToList();
        if (unbackedRows.Count > 0)
        {
            throw new ManifestException(
                $"Map observation contains node rows with no supporting frame: {string.Join(", ", unbackedRows)}.");
        }

        return observation;
    }

    public void RequireSameVideo(VideoSource expected)
    {
        if (!string.Equals(Video.Platform, expected.Platform, StringComparison.Ordinal) ||
            !string.Equals(Video.VideoId, expected.VideoId, StringComparison.Ordinal) ||
            !string.Equals(Video.ChannelId, expected.ChannelId, StringComparison.Ordinal) ||
            Video.DurationSeconds != expected.DurationSeconds)
        {
            throw new ManifestException(
                $"Map observation video '{Video.VideoId}' does not match manifest video '{expected.VideoId}'.");
        }
    }

    private IReadOnlyList<string> VideoProblems()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Video.Platform)) problems.Add("platform is empty");
        if (string.IsNullOrWhiteSpace(Video.VideoId)) problems.Add("video_id is empty");
        if (string.IsNullOrWhiteSpace(Video.ChannelId)) problems.Add("channel_id is empty");
        if (Video.DurationSeconds <= 0) problems.Add("duration_s must be positive");

        var durationMs = (long)Video.DurationSeconds * 1000;
        foreach (var frame in Frames)
        {
            if (frame.VideoTimeMs < 0 || frame.VideoTimeMs > durationMs)
            {
                problems.Add($"frame timestamp {frame.VideoTimeMs}ms is outside video duration {durationMs}ms");
            }
        }
        return problems;
    }

    /// <summary>
    /// Compares a generated map against this observation, on nodes only.
    ///
    /// Grid size must agree and every observed node must be present with the same
    /// type. Generated nodes in rows the observation never covered are not counted
    /// against the match - but rows it did cover must match completely, so a
    /// generated map cannot pass by being a superset.
    /// </summary>
    public MapComparison CompareTo(MapTopology generated)
    {
        var problems = new List<string>();

        if (Nodes.Count == 0)
        {
            problems.Add("observation contains no nodes, so it cannot verify a generated map");
        }

        var frameBackedRows = Frames.SelectMany(frame => frame.RowsSettled).ToHashSet();
        if (frameBackedRows.Count == 0)
        {
            problems.Add("observation contains no observed rows");
        }

        var unbackedRows = Nodes.Select(node => node.Row)
            .Where(row => !frameBackedRows.Contains(row))
            .Distinct()
            .OrderBy(row => row)
            .ToList();
        if (unbackedRows.Count > 0)
        {
            problems.Add($"node rows have no supporting frame: {string.Join(", ", unbackedRows)}");
        }

        if (generated.Rows != Rows || generated.Columns != Columns)
        {
            problems.Add($"grid size differs: observed {Rows}x{Columns}, generated {generated.Rows}x{generated.Columns}");
        }

        var observedRows = frameBackedRows;
        var generatedByPosition = generated.Nodes.ToDictionary(n => (n.Row, n.Column), n => n.PointType);
        var observedByPosition = Nodes.ToDictionary(n => (n.Row, n.Column), n => n.PointType);

        var matched = 0;
        foreach (var node in Nodes.OrderBy(n => n.Row).ThenBy(n => n.Column))
        {
            if (!generatedByPosition.TryGetValue((node.Row, node.Column), out var generatedType))
            {
                problems.Add($"row {node.Row} column {node.Column}: observed {node.PointType}, generated nothing");
            }
            else if (generatedType != node.PointType)
            {
                problems.Add($"row {node.Row} column {node.Column}: observed {node.PointType}, generated {generatedType}");
            }
            else
            {
                matched++;
            }
        }

        // A generated node inside an observed row that the observation does not have
        // is a mismatch too - otherwise a map with extra nodes everywhere would pass.
        foreach (var node in generated.Nodes.OrderBy(n => n.Row).ThenBy(n => n.Column))
        {
            if (!observedRows.Contains(node.Row)) continue;
            if (!observedByPosition.ContainsKey((node.Row, node.Column)))
            {
                problems.Add($"row {node.Row} column {node.Column}: generated {node.PointType}, observed nothing");
            }
        }

        return new MapComparison(
            Matches: problems.Count == 0,
            ObservedNodeCount: Nodes.Count,
            GeneratedNodeCount: generated.Nodes.Count,
            MatchedNodeCount: matched,
            ObservedRows: observedRows.OrderBy(r => r).ToList(),
            Problems: problems);
    }
}

public sealed record ObservedFrame(
    [property: JsonPropertyName("video_t_ms")] int VideoTimeMs,
    [property: JsonPropertyName("rows_settled")] IReadOnlyList<int> RowsSettled);

public sealed record MapComparison(
    [property: JsonPropertyName("matches")] bool Matches,
    [property: JsonPropertyName("observed_node_count")] int ObservedNodeCount,
    [property: JsonPropertyName("generated_node_count")] int GeneratedNodeCount,
    [property: JsonPropertyName("matched_node_count")] int MatchedNodeCount,
    [property: JsonPropertyName("observed_rows")] IReadOnlyList<int> ObservedRows,
    [property: JsonPropertyName("problems")] IReadOnlyList<string> Problems);
