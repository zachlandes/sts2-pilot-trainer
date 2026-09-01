using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// An act's map reduced to the part a video can corroborate: which node types sit
/// at which grid positions, and which nodes connect to which.
///
/// The map is generated from the run's upfront RNG stream, so it is a function of
/// the seed and the environment. That makes it the one piece of a run that can be
/// checked against a video without reading a single character of text - which is
/// exactly what is needed, because the seed itself is only ever available as text
/// that something had to read.
/// </summary>
public sealed record MapTopology(
    [property: JsonPropertyName("act_index")] int ActIndex,
    [property: JsonPropertyName("rows")] int Rows,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("nodes")] IReadOnlyList<MapNode> Nodes,
    [property: JsonPropertyName("edges")] IReadOnlyList<MapEdge> Edges)
{
    /// <summary>Compact per-row rendering, e.g. <c>row 3: c0:Shop c1:Monster</c>.
    /// Same text a reader would write down while looking at the video, so the two
    /// can be compared by eye as well as by machine.</summary>
    public IEnumerable<string> RenderRows() =>
        Nodes.GroupBy(n => n.Row)
            .OrderBy(g => g.Key)
            .Select(g => $"row {g.Key,2}: " +
                         string.Join(" ", g.OrderBy(n => n.Column).Select(n => $"c{n.Column}:{n.PointType}")));
}

public sealed record MapNode(
    [property: JsonPropertyName("row")] int Row,
    [property: JsonPropertyName("column")] int Column,
    [property: JsonPropertyName("type")] string PointType);

public sealed record MapEdge(
    [property: JsonPropertyName("from_row")] int FromRow,
    [property: JsonPropertyName("from_column")] int FromColumn,
    [property: JsonPropertyName("to_row")] int ToRow,
    [property: JsonPropertyName("to_column")] int ToColumn);
