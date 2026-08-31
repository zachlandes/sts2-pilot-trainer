using System.Globalization;
using System.Text;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Draws two lines played from the same restored snapshot, side by side.
///
/// Deltas and nothing else. No score, no ranking, no highlight on the "better"
/// outcome - which line is better is a question about a game, and answering it here
/// would quietly turn a measurement into an opinion. The reader is given the same
/// starting state, the actions each line took, and exactly what changed.
/// </summary>
public static class LineDiagram
{
    // Column geometry, in a monospace face at 10px where a character is very close to
    // 6px wide. The three columns are sized so the widest permitted value in each
    // cannot reach the next one: field 22 chars ends at 146, the before value is
    // right-anchored at 252 and 14 chars long so it starts no earlier than 168.
    private const int PanelWidth = 452;
    private const int FieldX = 14;
    private const int FieldChars = 22;
    private const int BeforeRightX = 252;
    private const int BeforeChars = 14;
    private const int ArrowX = 260;
    private const int AfterX = 276;
    private const int AfterChars = 27;
    private const int Margin = 40;
    private const int RowHeight = 19;

    public sealed record Line(string Name, IReadOnlyList<string> Actions, IReadOnlyList<Delta> Deltas);

    public sealed record Delta(string Field, string Before, string After);

    public static string Render(string snapshotKey, string snapshotDigest, IReadOnlyList<Line> lines)
    {
        var rows = lines.Max(l => l.Actions.Count + l.Deltas.Count);
        var width = Margin * 2 + PanelWidth * lines.Count + 24 * (lines.Count - 1);
        var height = 150 + rows * RowHeight + 60;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"""
             <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" font-family="ui-monospace, SFMono-Regular, Menlo, monospace">
             <rect width="{width}" height="{height}" fill="#fbfaf7"/>
             <text x="{Margin}" y="34" font-size="15" font-weight="700" fill="#22262b">Two lines from one verified snapshot</text>
             <text x="{Margin}" y="56" font-size="11" fill="#5a6169">snapshot {Escape(snapshotKey)}</text>
             <text x="{Margin}" y="74" font-size="11" fill="#8a9098">{Escape(snapshotDigest)}</text>
             <text x="{Margin}" y="96" font-size="11" fill="#5a6169">Both lines start from this identical state. Objective deltas only - no score, no ranking, no verdict.</text>
             """);

        for (var i = 0; i < lines.Count; i++)
        {
            DrawLine(svg, Margin + i * (PanelWidth + 24), 118, lines[i]);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void DrawLine(StringBuilder svg, int x0, int y0, Line line)
    {
        var height = 34 + (line.Actions.Count + line.Deltas.Count) * RowHeight + 18;
        svg.Append(CultureInfo.InvariantCulture,
            $"""
             <rect x="{x0}" y="{y0}" width="{PanelWidth}" height="{height}" fill="#ffffff" stroke="#e2ded4"/>
             <text x="{x0 + FieldX}" y="{y0 + 24}" font-size="12" font-weight="700" fill="#3a4149">{Escape(line.Name)}</text>
             """);

        var y = y0 + 46;
        foreach (var action in line.Actions)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""<text x="{x0 + FieldX}" y="{y}" font-size="10.5" fill="#4a5560">&#9656; {Escape(action)}</text>""");
            y += RowHeight;
        }

        y += 6;
        foreach (var delta in line.Deltas)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"""
                 <text x="{x0 + FieldX}" y="{y}" font-size="10" fill="#8a9098">{Escape(Shorten(delta.Field, FieldChars))}</text>
                 <text x="{x0 + BeforeRightX}" y="{y}" font-size="10" fill="#a6564f" text-anchor="end">{Escape(Shorten(delta.Before, BeforeChars))}</text>
                 <text x="{x0 + ArrowX}" y="{y}" font-size="10" fill="#b3aca0">&#8594;</text>
                 <text x="{x0 + AfterX}" y="{y}" font-size="10" fill="#2f6f52">{Escape(Shorten(delta.After, AfterChars))}</text>
                 """);
            y += RowHeight;
        }
    }

    /// <summary>Truncates for the diagram only. The full value is always in the JSON
    /// beside it, so nothing is lost - only the picture is made readable.</summary>
    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}
