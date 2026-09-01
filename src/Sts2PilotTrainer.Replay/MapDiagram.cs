using System.Globalization;
using System.Text;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Draws an observed map beside a generated one so a human can check the machine's
/// verdict rather than take it on trust.
///
/// Deliberately our own drawing rather than a captured frame: the video belongs to
/// its creator and none of it is reproduced by this project. What is drawn is the
/// transcription - the facts read from the video - which is also the honest thing
/// to show, since the transcription is what the comparison actually used.
/// </summary>
public static class MapDiagram
{
    private const int CellWidth = 46;
    private const int CellHeight = 34;
    private const int Margin = 56;
    private const int PanelGap = 64;

    /// <summary>Node type to glyph and colour. Distinct in shape as well as hue, so
    /// the diagram survives being printed, screenshotted, or read by someone who
    /// does not separate red from green.</summary>
    private static readonly Dictionary<string, (string Glyph, string Fill)> Style = new(StringComparer.Ordinal)
    {
        ["Monster"] = ("m", "#7c8a9c"),
        ["Elite"] = ("E", "#b0506a"),
        ["Boss"] = ("B", "#8b3050"),
        ["RestSite"] = ("R", "#c07a4a"),
        ["Shop"] = ("$", "#5f8f6a"),
        ["Treasure"] = ("T", "#b39a4a"),
        ["Unknown"] = ("?", "#8a7f5f"),
        ["Ancient"] = ("A", "#6a5f8f"),
    };

    public static string Render(MapObservation observed, MapTopology generated, string candidateSeed, MapComparison comparison)
    {
        var mismatched = ParseMismatchedPositions(comparison);
        var gridWidth = observed.Columns * CellWidth;
        var gridHeight = observed.Rows * CellHeight;
        var width = Margin * 2 + gridWidth * 2 + PanelGap;
        var height = Margin + gridHeight + 108;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"""
             <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" font-family="ui-monospace, SFMono-Regular, Menlo, monospace">
             <rect width="{width}" height="{height}" fill="#fbfaf7"/>
             """);

        var verdict = comparison.Matches ? "MATCH" : "MISMATCH";
        var verdictColour = comparison.Matches ? "#2f7d4f" : "#b0324b";
        svg.Append(CultureInfo.InvariantCulture,
            $"""
             <text x="{Margin}" y="30" font-size="15" font-weight="700" fill="#22262b">Act 1 map topology &#183; seed {Escape(candidateSeed)}</text>
             <text x="{Margin}" y="50" font-size="12" fill="{verdictColour}" font-weight="700">{verdict}</text>
             <text x="{Margin + 90}" y="50" font-size="12" fill="#5a6169">{comparison.MatchedNodeCount} of {comparison.ObservedNodeCount} observed nodes agree &#183; {comparison.Problems.Count} problem(s)</text>
             """);

        DrawPanel(svg, Margin, Margin + 24, observed.Rows, observed.Columns,
            observed.Nodes, $"observed &#183; video {Escape(observed.Video.VideoId)}", mismatched);
        DrawPanel(svg, Margin + gridWidth + PanelGap, Margin + 24, observed.Rows, observed.Columns,
            generated.Nodes, "generated &#183; game engine", mismatched);

        var legendY = Margin + 24 + gridHeight + 34;
        svg.Append(CultureInfo.InvariantCulture, $"""<text x="{Margin}" y="{legendY}" font-size="11" fill="#5a6169">""");
        svg.Append(string.Join("&#160;&#160;", Style.Select(kv => $"{kv.Value.Glyph} {kv.Key}")));
        svg.Append("</text>");
        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{Margin}" y="{legendY + 18}" font-size="11" fill="#8a9098">Row 0 is the run start, at the bottom of both grids. Blank cells have no node.</text>""");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void DrawPanel(
        StringBuilder svg, int x0, int y0, int rows, int columns,
        IReadOnlyList<MapNode> nodes, string title, HashSet<(int Row, int Column)> mismatched)
    {
        var gridHeight = rows * CellHeight;
        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{x0}" y="{y0 - 8}" font-size="12" font-weight="600" fill="#3a4149">{title}</text>""");
        svg.Append(CultureInfo.InvariantCulture,
            $"""<rect x="{x0 - 6}" y="{y0}" width="{columns * CellWidth + 12}" height="{gridHeight + 8}" fill="#ffffff" stroke="#e2ded4"/>""");

        var byPosition = nodes.ToDictionary(n => (n.Row, n.Column));
        for (var row = 0; row < rows; row++)
        {
            // Row 0 at the bottom, matching how the game draws the climb.
            var y = y0 + (rows - 1 - row) * CellHeight + CellHeight / 2 + 4;
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{x0 - 14}" y="{y + 4}" font-size="9" fill="#b3aca0" text-anchor="end">{row}</text>""");

            for (var column = 0; column < columns; column++)
            {
                if (!byPosition.TryGetValue((row, column), out var node)) continue;
                var (glyph, fill) = Style.TryGetValue(node.PointType, out var s) ? s : ("&#183;", "#999999");
                var cx = x0 + column * CellWidth + CellWidth / 2;
                var flagged = mismatched.Contains((row, column));
                var stroke = flagged ? "#b0324b" : "none";
                var strokeWidth = flagged ? 2.5 : 0;
                svg.Append(CultureInfo.InvariantCulture,
                    $"""<circle cx="{cx}" cy="{y}" r="12" fill="{fill}" stroke="{stroke}" stroke-width="{strokeWidth.ToString(CultureInfo.InvariantCulture)}"/>""");
                svg.Append(CultureInfo.InvariantCulture,
                    $"""<text x="{cx}" y="{y + 4}" font-size="11" font-weight="700" fill="#ffffff" text-anchor="middle">{glyph}</text>""");
            }
        }
    }

    /// <summary>
    /// Pulls the grid positions back out of the comparison's problem lines so the
    /// diagram can ring them. Reading the report rather than recomputing keeps the
    /// picture and the verdict from being able to disagree.
    /// </summary>
    private static HashSet<(int Row, int Column)> ParseMismatchedPositions(MapComparison comparison)
    {
        var positions = new HashSet<(int, int)>();
        foreach (var problem in comparison.Problems)
        {
            var match = System.Text.RegularExpressions.Regex.Match(problem, @"^row (\d+) column (\d+):");
            if (match.Success)
            {
                positions.Add((int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                               int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
            }
        }
        return positions;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}
