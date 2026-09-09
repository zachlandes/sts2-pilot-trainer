using System.Globalization;
using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

internal sealed record FightResultText(
    GameTextStyle Heading,
    GameTextStyle Body,
    GameTextStyle ListHeading,
    GameTextStyle FigureLabel,
    GameTextStyle FigureValue,
    GameTextStyle SectionHeading,
    GameTextStyle ListNumeral,
    GameTextStyle Secondary,
    GameTextStyle ChartNumeral,
    GameTextStyle CardCaption,
    GameTextStyle Button);

/// <summary>
/// The player's fight beside the recording's, drawn.
///
/// This is the result the captain asked for after reading the first one: the fight
/// as pictures rather than as prose. The cards each side played are the game's own
/// card art in turn order, the potions are the game's own bottles at the turn they
/// were spent, the summary is two side-by-side columns of figures, and the chart
/// puts both lines' enemy health lost and player health lost against the turn. The
/// one large popup the previous slice used is gone rather than wrapped around this.
///
/// It draws and decides nothing. Every value comes from
/// <see cref="FightResultScreen"/>, every word from it too, and a measurement the
/// projection could not derive is drawn as a gap in the line rather than as a zero -
/// the chart never invents a point, and the chronology says "fight over" in the
/// panel's own words where a side had already finished.
///
/// Built from stock Godot nodes on purpose. This assembly compiles without Godot's
/// source generators, so a <c>Control</c> subclass of ours would have no generated
/// bridge and none of its overrides would ever be called; every node below is one
/// the engine already knows how to drive, which is also what lets the whole panel be
/// assembled and asserted on in a process with no game.
/// </summary>
internal static class FightResultPanel
{
    internal const string RootName = "RunmobileFightResult";

    // ── The palette ────────────────────────────────────────────────────────
    //
    // The two lines are told apart twice over: by colour, and by the shape of the
    // marker on the chart. Colour alone would be one accessibility setting away from
    // a chart with two identical lines on it.

    private static readonly Color PanelFill = Rgb(0x10, 0x12, 0x14, 0.97f);
    private static readonly Color PanelEdge = Rgb(0x5c, 0x63, 0x6b);
    private static readonly Color TitleText = Rgb(0xf1, 0xdf, 0xae);
    private static readonly Color PrimaryText = Rgb(0xe8, 0xe4, 0xda);
    private static readonly Color SecondaryText = Rgb(0xa9, 0xb3, 0xbd);
    private static readonly Color DimText = Rgb(0x7d, 0x85, 0x90);
    private static readonly Color Rule = Rgb(0x2b, 0x30, 0x36);

    /// <summary>The player's line: the game's own gold.</summary>
    private static readonly Color YouLine = Rgb(0xd9, 0xb2, 0x5f);

    private static readonly Color YouFill = Rgb(0x2a, 0x21, 0x15);

    /// <summary>The recording's line: a blue nothing else on this panel uses.</summary>
    private static readonly Color TheirLine = Rgb(0x6a, 0xa7, 0xe6);

    private static readonly Color TheirFill = Rgb(0x15, 0x21, 0x31);

    private static readonly Color TheirText = Rgb(0x9c, 0xca, 0xfc);

    // ── The layout ─────────────────────────────────────────────────────────

    private const float MaxPanelWidth = 1240f;
    private const float MaxPanelHeight = 780f;

    /// <summary>The panel a notice gets: wide enough for a sentence of the engine's
    /// own, tall enough for it and the button.</summary>
    private const float NoticePanelWidth = 860f;

    private const float NoticePanelHeight = 260f;
    private const float ScreenMargin = 48f;
    private const float Pad = 26f;
    private const float HeaderHeight = 86f;
    private const float FooterHeight = 76f;
    private const float CardWidth = 26f;
    private const float CardHeight = 34f;
    private const float PotionSize = 22f;
    private const float ChipGap = 4f;

    /// <summary>The space between one line's turn and the next line's, so a numeral
    /// at the end of a column is not read as a label on the column beside it.</summary>
    private const float ColumnGutter = 16f;

    /// <summary>
    /// The size this panel's own labels were drawn at when its measurements were taken.
    ///
    /// Every box below was laid out around text this size, so it is what the game's own
    /// size is compared against: a client drawing larger text gets the same panel in the
    /// same proportions, one size larger, rather than these boxes with bigger words
    /// spilling out of them. It is the panel's ordinary label and not its title, because
    /// the ordinary label is what most of the panel is.
    /// </summary>
    private const float ReferenceTextSize = 15f;

    /// <summary>
    /// Assembles the panel.
    /// </summary>
    /// <param name="screen">The result, already computed.</param>
    /// <param name="viewport">The size of the surface it is drawn over.</param>
    /// <param name="art">The game's artwork for a model id, or null where a build has
    /// none. Injected so the panel assembles in a process with no model database.</param>
    /// <param name="text">The native heading, body, figure, and button styles.</param>
    /// <param name="done">What the one button does.</param>
    internal static FightResultPanelNodes Build(
        FightResultScreen screen, Vector2 viewport, Func<string, Texture2D?> art, FightResultText text, Action done,
        Func<bool, Texture2D?>? pageArrow = null)
    {
        var painter = new Painter(art, text, pageArrow ?? NativePaginatorArt.Texture);
        var u = painter.Unit;
        var pad = Pad * u;
        var header = HeaderHeight * u;
        var footer = FooterHeight * u;

        // A comparison fills a panel; a notice is one sentence and a button, and a
        // sentence adrift in the middle of a panel this size reads as a page that
        // failed to load.
        var width = Math.Min(
            (screen.HasComparison ? MaxPanelWidth : NoticePanelWidth) * u,
            viewport.X - (2 * ScreenMargin * u));
        var height = Math.Min(
            (screen.HasComparison ? MaxPanelHeight : NoticePanelHeight) * u,
            viewport.Y - (2 * ScreenMargin * u));

        var root = new Control
        {
            Name = RootName,
            Position = Vector2.Zero,
            Size = viewport,
            // The screen underneath is finished with; nothing on it should take a
            // click meant for this panel.
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        var edge = Box(PanelEdge, ((viewport.X - width) / 2) - 2, ((viewport.Y - height) / 2) - 2, width + 4, height + 4);
        edge.Name = NodeName("PanelEdge");
        root.AddChild(edge);

        var panel = Box(PanelFill, (viewport.X - width) / 2, (viewport.Y - height) / 2, width, height);
        panel.Name = NodeName("Panel");
        root.AddChild(panel);

        painter.Text(panel, "Title", screen.Title, pad, 18 * u, width - (2 * pad), 34 * u, painter.Title, TitleText);

        Button button;
        if (!screen.HasComparison)
        {
            painter.Wrapped(
                panel, "Notice", screen.Notice, pad, 66 * u, width - (2 * pad), height - (66 * u) - footer,
                painter.Body, PrimaryText);
            button = painter.DoneButton(screen.DoneButton, width, height, done);
            panel.AddChild(button);
            return new FightResultPanelNodes(root, button);
        }

        painter.Text(
            panel, "SameBoundaryNote", screen.SameBoundaryNote, pad, 56 * u, width - (2 * pad), 20 * u,
            painter.Body, DimText);

        var columnWidth = (width - (3 * pad)) / 2;
        var right = pad + columnWidth + pad;
        var content = height - header - footer;

        var divider = Box(Rule, pad + columnWidth + (pad / 2), header, 1, content);
        divider.Name = NodeName("Divider");
        panel.AddChild(divider);

        painter.Summary(panel, screen, pad, header, columnWidth, content);
        var chronology = new Control
        {
            Name = NodeName("ChronologyPage"),
            Position = new Vector2(right, header),
            Size = new Vector2(columnWidth, content),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddChild(chronology);
        Action<int>? drawPage = null;
        drawPage = page =>
        {
            foreach (var child in chronology.GetChildren().ToList())
            {
                chronology.RemoveChild(child);
                child.QueueFree();
            }
            painter.Chronology(chronology, screen, 0, 0, columnWidth, content, page, drawPage!);
        };
        drawPage(0);
        button = painter.DoneButton(screen.DoneButton, width, height, done);
        panel.AddChild(button);
        return new FightResultPanelNodes(root, button);
    }

    private static ColorRect Box(Color color, float x, float y, float width, float height) => new()
    {
        Color = color,
        Position = new Vector2(x, y),
        Size = new Vector2(width, height),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static Color Rgb(int red, int green, int blue, float alpha = 1f) =>
        new(red / 255f, green / 255f, blue / 255f, alpha);

    /// <summary>
    /// A node name Godot will keep as it is given.
    ///
    /// The engine rewrites a name containing any of <c>. : @ / " %</c>, and a model
    /// id is full of dots. Rewritten names would still draw, and the tree in the
    /// client would stop matching the tree the tests read.
    /// </summary>
    private static StringName NodeName(string name) => name.Replace('.', '_');

    /// <summary>
    /// Everything that needs the font and the artwork to draw, in one place so that
    /// neither has to be threaded through every helper.
    /// </summary>
    private sealed class Painter(
        Func<string, Texture2D?> art, FightResultText text, Func<bool, Texture2D?> pageArrow)
    {
        /// <summary>How much larger the game draws its text than this panel's
        /// measurements assumed. Every box here is multiplied by it, so the panel keeps
        /// its proportions and grows around its words.</summary>
        internal float Unit { get; } = text.Body.Size / ReferenceTextSize;

        internal GameTextStyle Title { get; } = text.Heading;
        internal GameTextStyle Body { get; } = text.Body;
        internal GameTextStyle ListHeading { get; } = text.ListHeading;
        internal GameTextStyle FigureLabel { get; } = text.FigureLabel;
        internal GameTextStyle FigureValue { get; } = text.FigureValue;
        internal GameTextStyle SectionHeading { get; } = text.SectionHeading;
        internal GameTextStyle ListNumeral { get; } = text.ListNumeral;
        internal GameTextStyle Secondary { get; } = text.Secondary;
        internal GameTextStyle ChartNumeral { get; } = text.ChartNumeral;
        internal GameTextStyle CardCaption { get; } = text.CardCaption;
        internal GameTextStyle Button { get; } = text.Button;

        /// <summary>
        /// The summary: the two lines named once, then the compared figures under
        /// them, then the caveats.
        ///
        /// A figure whose two sides agree is dimmed and one that differs is not.
        /// That is the only emphasis there is, and it is a statement about two values
        /// rather than about which of them is better.
        /// </summary>
        internal void Summary(Control panel, FightResultScreen screen, float x, float y, float width, float height)
        {
            var labelWidth = width * 0.42f;
            var columnWidth = (width - labelWidth) / 2;
            var yours = x + labelWidth;
            var theirs = yours + columnWidth;

            var legendHeight = ListHeading.Size;
            Legend(panel, "Legend.You", screen.Columns[0], YouLine, YouFill, yours, y, columnWidth, legendHeight);
            Legend(panel, "Legend.Them", screen.Columns[1], TheirLine, TheirFill, theirs, y, columnWidth, legendHeight);

            var rowCount = Math.Max(1, screen.Rows.Count);
            var noteCount = screen.Notes.Count;
            var noteHeight = noteCount == 0
                ? 0f
                : Math.Min(Body.Size, Math.Max(1f, (height - legendHeight - rowCount) / noteCount));
            var notesHeight = noteHeight * noteCount;
            var rowHeight = Math.Max(
                1f,
                Math.Min(44f * Unit, (height - legendHeight - notesHeight) / rowCount));
            var row = y + legendHeight;
            foreach (var figure in screen.Rows)
            {
                var value = figure.Matches ? DimText : TitleText;
                Text(panel, $"Figure.{figure.Label}", figure.Label, x, row, labelWidth, rowHeight,
                    FigureLabel, figure.Matches ? DimText : SecondaryText);
                Text(panel, $"Figure.{figure.Label}.Yours", figure.Yours, yours, row, columnWidth, rowHeight,
                    FigureValue, value, HorizontalAlignment.Center);
                Text(panel, $"Figure.{figure.Label}.Theirs", figure.Theirs, theirs, row, columnWidth, rowHeight,
                    FigureValue, value, HorizontalAlignment.Center);

                var rule = Box(Rule, x, row + rowHeight - 1, width, 1);
                rule.Name = NodeName($"Figure.{figure.Label}.Rule");
                panel.AddChild(rule);
                row += rowHeight;
            }

            // The caveats follow the figures rather than sitting at the foot of the
            // column: each is a rule about how to read the numbers above it, and a
            // caveat marooned under a gap reads as a footnote to nothing.
            var note = row;
            for (var index = 0; index < screen.Notes.Count; index++)
            {
                Wrapped(panel, $"Note.{index}", screen.Notes[index], x, note, width, noteHeight, Body, DimText);
                note += noteHeight;
            }
        }

        /// <summary>
        /// One of the two lines, named once at the top of its column of figures.
        ///
        /// The swatch is the colour that line is drawn in everywhere else on the
        /// panel - its card borders, its chart line, its markers - so the column, the
        /// icons and the chart are read as one line rather than as three.
        /// </summary>
        private void Legend(
            Control panel, string name, string label, Color line, Color fill, float x, float y, float width,
            float height)
        {
            var swatchSize = Math.Min(14 * Unit, height);
            var swatchY = y + ((height - swatchSize) / 2f);
            var swatch = Box(line, x + (10 * Unit), swatchY, swatchSize, swatchSize);
            swatch.Name = NodeName($"{name}.Swatch");
            panel.AddChild(swatch);

            var inside = Box(
                fill, x + (10 * Unit) + 1f, swatchY + 1f,
                Math.Max(1f, swatchSize - 2f), Math.Max(1f, swatchSize - 2f));
            inside.Name = NodeName($"{name}.Swatch.Inside");
            panel.AddChild(inside);

            Text(panel, name, label, x + (30 * Unit), y, width - (30 * Unit), height, ListHeading, line);
        }

        /// <summary>
        /// The turn chronology and the chart of the same turns: what each side played,
        /// and what each turn cost them.
        /// </summary>
        internal void Chronology(
            Control panel, FightResultScreen screen, float x, float y, float width, float height,
            int requestedPage, Action<int> drawPage)
        {
            var headingHeight = SectionHeading.Size;
            var columnHeadingHeight = ListHeading.Size;
            var preferredRowHeight = Math.Max(ListNumeral.Size, CardCaption.Size);
            var headingsHeight = headingHeight + columnHeadingHeight;
            var chartHeight = Math.Max(
                1f,
                Math.Min(
                    250f * Unit,
                    height - headingsHeight - (ScreenPage.MinimumPerPage * preferredRowHeight)));
            var rowsHeight = height - headingsHeight - chartHeight;
            var fits = Math.Max(
                ScreenPage.MinimumPerPage,
                (int)Math.Floor(rowsHeight / preferredRowHeight));
            var page = ScreenPage.For(screen.Turns.Count, fits, requestedPage);
            var rowHeight = Math.Min(44f * Unit, rowsHeight / fits);
            var turnWidth = 46f * Unit;
            var columnWidth = (width - turnWidth) / 2;
            var yours = x + turnWidth;
            var theirs = yours + columnWidth;

            Text(panel, "Chronology", screen.TurnDetailHeading, x, y, width, headingHeight,
                SectionHeading, SecondaryText);
            var headings = y + headingHeight;
            Text(panel, "Chronology.Turn", screen.Chart.TurnLabel, x, headings, turnWidth, columnHeadingHeight,
                ListHeading, DimText);
            Text(panel, "Chronology.You", screen.Columns[0], yours, headings, columnWidth, columnHeadingHeight,
                ListHeading, YouLine);
            Text(panel, "Chronology.Them", screen.Columns[1], theirs + (ColumnGutter * Unit), headings,
                columnWidth, columnHeadingHeight, ListHeading, TheirText);

            var row = headings + columnHeadingHeight;
            if (page.HasPrevious)
            {
                PageButton(panel, "Chronology.Previous", true, x, row, width, rowHeight,
                    () => drawPage(page.Index - 1));
                row += rowHeight;
            }

            var card = Math.Min(CardHeight * Unit, rowHeight);
            foreach (var turn in screen.Turns.Skip(page.First).Take(page.Count))
            {
                Text(panel, $"Turn.{turn.Turn}", turn.Turn.ToString(CultureInfo.InvariantCulture),
                    x, row, turnWidth, rowHeight, ListNumeral, SecondaryText);
                Side(panel, $"Turn.{turn.Turn}.Yours", turn.Yours, screen.FightOverLabel, YouLine, YouFill,
                    yours, row, columnWidth - (ColumnGutter * Unit), rowHeight, card);
                Side(panel, $"Turn.{turn.Turn}.Theirs", turn.Theirs, screen.FightOverLabel, TheirLine, TheirFill,
                    theirs + (ColumnGutter * Unit), row, columnWidth - (ColumnGutter * Unit), rowHeight, card);
                row += rowHeight;
            }

            if (page.HasNext)
            {
                PageButton(panel, "Chronology.Next", false, x, row, width, rowHeight,
                    () => drawPage(page.Index + 1));
            }

            Chart(panel, screen.Chart, x, y + height - chartHeight, width, chartHeight);
        }

        private void PageButton(
            Control panel, string name, bool previous, float x, float y, float width, float height, Action press) =>
            NativePaginatorArt.AddButton(
                panel, NodeName(name), string.Empty, previous,
                new Rect2(x, y, width, height), pageArrow, press);

        /// <summary>
        /// One side of one turn: the cards it played, the potions it spent, and what
        /// the turn cost it.
        /// </summary>
        private void Side(
            Control panel, string name, FightResultTurnSide? side, string fightOver, Color line, Color fill,
            float x, float y, float width, float height, float cardHeight)
        {
            if (side is null)
            {
                Text(panel, $"{name}.FightOver", fightOver, x, y, width, height, Secondary, DimText);
                return;
            }

            var cardWidth = cardHeight * CardWidth / CardHeight;
            var potion = Math.Min(PotionSize * Unit, cardHeight);
            var healthWidth = Math.Min(42 * Unit, width);
            var healthX = x + width - healthWidth;
            var chipRight = healthX - (ChipGap * Unit);
            var available = chipRight - x;
            if (available <= 0f && side.CardModelIds.Count + side.PotionModelIds.Count > 0)
            {
                throw new InvalidOperationException("This result row has no room beside its health value.");
            }

            var chips = side.CardModelIds
                .Select(id => (Kind: "Card", Id: id, Width: cardWidth, Height: cardHeight))
                .Concat(side.PotionModelIds.Select(id => (Kind: "Potion", Id: id, Width: potion, Height: potion)))
                .ToList();
            var widths = chips.Sum(item => item.Width);
            var gap = chips.Count > 1 ? ChipGap * Unit : 0f;
            var scale = 1f;
            if (widths + (gap * Math.Max(0, chips.Count - 1)) > available)
            {
                gap = chips.Count > 1 && widths < available
                    ? (available - widths) / (chips.Count - 1)
                    : 0f;
                if (widths > available) scale = available / widths;
            }

            var chip = x;
            foreach (var item in chips)
            {
                var chipWidth = item.Width * scale;
                var chipHeight = item.Height * scale;
                Chip(panel, $"{name}.{item.Kind}.{item.Id}", item.Id, line, fill, chip,
                    y + ((height - chipHeight) / 2), chipWidth, chipHeight);
                chip += chipWidth + gap;
            }

            // The turn's own cost, beside what was played. The same number the chart's
            // lower plot draws, where a player reads it while looking at the cards.
            Text(panel, $"{name}.HealthLost", Loss(side.HealthLost), healthX, y, healthWidth, height,
                ListNumeral, side.HealthLost > 0 ? line : DimText, HorizontalAlignment.Right);
        }

        /// <summary>
        /// The chart: both measures, both lines, against the turn.
        ///
        /// Two plots rather than four lines on one, and one ceiling for both, so a
        /// height on the upper plot means the same as a height on the lower one.
        /// </summary>
        private void Chart(Control panel, FightResultChart chart, float x, float y, float width, float height)
        {
            var headingHeight = Math.Min(SectionHeading.Size, Math.Max(1f, height - 3f));
            Text(panel, "Chart", chart.Heading, x, y, width, headingHeight, SectionHeading, SecondaryText);
            if (!chart.HasTurns) return;

            var plotLeft = x + (128 * Unit);
            var plotWidth = width - (128 * Unit);
            var remaining = height - headingHeight;
            var axisHeight = Math.Min(ListHeading.Size, Math.Max(1f, remaining - 3f));
            var potionHeight = Math.Min(
                Math.Min(PotionSize * Unit, ChartNumeral.Size),
                Math.Max(1f, remaining - axisHeight - 2f));
            var plotHeight = Math.Max(1f, (remaining - axisHeight - potionHeight) / 2f);
            var firstPlot = y + headingHeight;

            Plot(panel, "Chart.Enemy", chart, point => point.EnemyHealthLost, chart.EnemyMeasureLabel,
                x, firstPlot, plotLeft, plotWidth, plotHeight);
            Plot(panel, "Chart.Player", chart, point => point.HealthLost, chart.PlayerMeasureLabel,
                x, firstPlot + plotHeight, plotLeft, plotWidth, plotHeight);

            var axis = firstPlot + (2 * plotHeight);
            Text(panel, "Chart.TurnAxis", chart.TurnLabel, x, axis, 108 * Unit, axisHeight, ListHeading, DimText,
                HorizontalAlignment.Right);
            for (var index = 0; index < chart.Turns.Count; index++)
            {
                var at = X(plotLeft, plotWidth, index, chart.Turns.Count);
                Text(panel, $"Chart.Turn.{chart.Turns[index]}", chart.Turns[index].ToString(CultureInfo.InvariantCulture),
                    at - (14 * Unit), axis, 28 * Unit, axisHeight, ChartNumeral, SecondaryText, HorizontalAlignment.Center);
                Potions(panel, chart, index, at, axis + axisHeight, potionHeight);
            }
        }

        /// <summary>One measure, both lines.</summary>
        private void Plot(
            Control panel, string name, FightResultChart chart, Func<FightResultPoint, int?> measure, string label,
            float x, float y, float plotLeft, float plotWidth, float height)
        {
            var labelHeight = Math.Min(ListHeading.Size, Math.Max(1f, height));
            Text(panel, name, label, x, y + ((height - labelHeight) / 2f), 108 * Unit, labelHeight,
                ListHeading, DimText, HorizontalAlignment.Right);

            var baseline = Box(Rule, plotLeft - (8 * Unit), y + height, plotWidth + (8 * Unit), 1);
            baseline.Name = NodeName($"{name}.Baseline");
            panel.AddChild(baseline);

            Line(panel, $"{name}.Line.You", chart, chart.Yours, measure, YouLine, plotLeft, plotWidth, y, height,
                marker: false);
            Line(panel, $"{name}.Line.Them", chart, chart.Theirs, measure, TheirLine, plotLeft, plotWidth, y, height,
                marker: true);
        }

        /// <summary>
        /// One line of one plot: a polyline through the turns this side reached, a
        /// marker at each of them, and the value beside it.
        ///
        /// A turn this side never reached breaks the line rather than pulling it to
        /// the axis. The break is the fact: there was no turn to measure.
        /// </summary>
        private void Line(
            Control panel, string name, FightResultChart chart, FightResultSeries series,
            Func<FightResultPoint, int?> measure, Color color, float plotLeft, float plotWidth, float y, float height,
            bool marker)
        {
            var ceiling = Math.Max(1, chart.Ceiling);
            var plotted = new List<(int Turn, int Value, Vector2 At)>();
            for (var index = 0; index < series.Points.Count; index++)
            {
                if (measure(series.Points[index]) is not { } value) continue;

                plotted.Add((
                    series.Points[index].Turn,
                    value,
                    new Vector2(
                        X(plotLeft, plotWidth, index, chart.Turns.Count), y + height - (height * value / ceiling))));
            }

            if (plotted.Count == 0) return;

            // The line first, so its own markers and numerals sit on top of it rather
            // than under it: children draw in the order they are added.
            var line = new Line2D
            {
                Name = NodeName(name),
                Points = plotted.Select(point => point.At).ToArray(),
                Width = 2.5f * Unit,
                DefaultColor = color,
            };
            panel.AddChild(line);

            foreach (var (turn, value, at) in plotted)
            {
                var dot = Box(color, at.X - (4 * Unit), at.Y - (4 * Unit), 8 * Unit, 8 * Unit);
                dot.Name = NodeName($"{name}.Point.{turn}");
                // The recording's markers are turned forty-five degrees. Two lines
                // that differ only in colour are one colour-blind player away from
                // being the same line.
                if (marker)
                {
                    dot.PivotOffset = new Vector2(4 * Unit, 4 * Unit);
                    dot.Rotation = float.Pi / 4;
                }

                panel.AddChild(dot);
                var valueHeight = Math.Min(ChartNumeral.Size, Math.Max(1f, height));
                var wantedY = marker ? at.Y : at.Y - valueHeight;
                var valueY = Math.Clamp(wantedY, y, y + height - valueHeight);
                Text(panel, $"{name}.Value.{turn}", value.ToString(CultureInfo.InvariantCulture),
                    at.X - (20 * Unit), valueY, 40 * Unit, valueHeight,
                    ChartNumeral, color, HorizontalAlignment.Center);
            }
        }

        /// <summary>The potions either side spent on this turn, under the axis and
        /// bordered by the line that spent them.</summary>
        private void Potions(Control panel, FightResultChart chart, int index, float at, float y, float size)
        {
            var lane = at - (size / 2);
            foreach (var (series, color, fill) in new[]
                     {
                         (chart.Yours, YouLine, YouFill), (chart.Theirs, TheirLine, TheirFill),
                     })
            {
                foreach (var potion in series.Points[index].PotionModelIds)
                {
                    Chip(
                        panel, $"Chart.Potion.{series.Points[index].Turn}.{series.Label}.{potion}", potion, color,
                        fill, lane, y, size, size);
                    lane += size + (ChipGap * Unit);
                }
            }
        }

        /// <summary>
        /// One card or potion, as the game draws it: its own artwork inside a border
        /// in the colour of the line that played it. Where this build has no artwork
        /// for the id, the name goes in its place - never a picture of something else.
        /// </summary>
        private void Chip(
            Control panel, string name, string modelId, Color line, Color fill, float x, float y,
            float width, float height)
        {
            var chip = Box(line, x, y, width, height);
            chip.Name = NodeName(name);
            chip.TooltipText = ModelIdNames.Display(modelId);
            panel.AddChild(chip);

            var inside = Box(fill, x + 1, y + 1, Math.Max(1f, width - 2), Math.Max(1f, height - 2));
            inside.Name = NodeName($"{name}.Inside");
            panel.AddChild(inside);

            if (art(modelId) is { } texture)
            {
                var picture = new TextureRect
                {
                    Name = NodeName($"{name}.Art"),
                    // Told to ignore the texture's own size before it is given one.
                    // A texture rect's minimum size is its texture, and card art is
                    // hundreds of pixels tall: sized first, it is clamped back up to
                    // the portrait's own size and drawn over half the panel. Measured
                    // in the client, where it did exactly that.
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    Texture = texture,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                picture.Position = new Vector2(x + 1, y + 1);
                picture.CustomMinimumSize = Vector2.Zero;
                picture.Size = new Vector2(Math.Max(1f, width - 2), Math.Max(1f, height - 2));
                panel.AddChild(picture);
                return;
            }

            // The smallest thing on the panel: a card's name standing in for art this
            // build has not got, inside a chip the size of a card.
            Wrapped(
                panel, $"{name}.Name", ModelIdNames.Display(modelId), x + 1, y + 1,
                Math.Max(1f, width - 2), Math.Max(1f, height - 2), CardCaption, line);
        }

        /// <summary>The one control on the panel, and the one thing left to do.</summary>
        internal Button DoneButton(string label, float width, float height, Action done)
        {
            var button = new Button
            {
                Name = NodeName("Done"),
                Text = label,
                Position = new Vector2(
                    width - (Pad * Unit) - (176 * Unit), height - (FooterHeight * Unit) + (12 * Unit)),
                Size = new Vector2(176 * Unit, 46 * Unit),
                FocusMode = Control.FocusModeEnum.All,
            };

            var style = new StyleBoxFlat { BgColor = YouLine, BorderColor = YouLine };
            style.SetCornerRadiusAll(8);
            style.SetBorderWidthAll(2);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
            {
                button.AddThemeStyleboxOverride(state, style);
            }

            button.AddThemeColorOverride("font_color", PanelFill);
            button.AddThemeColorOverride("font_hover_color", PanelFill);
            button.AddThemeColorOverride("font_pressed_color", PanelFill);
            Button.ApplyTo(button);
            button.Pressed += () => done();
            return button;
        }

        internal Label Text(
            Control panel, string name, string line, float x, float y, float width, float height,
            GameTextStyle style, Color color, HorizontalAlignment alignment = HorizontalAlignment.Left)
        {
            var label = Styled(name, line, style, color);
            label.HorizontalAlignment = alignment;
            label.VerticalAlignment = VerticalAlignment.Center;
            // A figure or a label that would not fit is cut off inside its own box
            // rather than drawn over the one beside it.
            label.ClipText = true;
            Place(panel, label, x, y, width, height);
            return label;
        }

        internal Label Wrapped(
            Control panel, string name, string line, float x, float y, float width, float height,
            GameTextStyle style, Color color)
        {
            var label = Styled(name, line, style, color);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Place(panel, label, x, y, width, height);
            return label;
        }

        /// <summary>
        /// Gives a label its box, after it has been told how to lay text out inside
        /// one.
        ///
        /// The order is load-bearing and was measured on a screen rather than
        /// reasoned about: a Control's size is clamped up to its minimum, and a
        /// label's minimum width is its whole unwrapped line, so a width set before
        /// the wrap mode is simply widened back and the sentence runs off the panel.
        /// The engine's own refusals are the longest text this panel ever draws, and
        /// that is exactly the case it went wrong in.
        /// </summary>
        private static void Place(Control panel, Label label, float x, float y, float width, float height)
        {
            label.Position = new Vector2(x, y);
            label.CustomMinimumSize = new Vector2(width, 0);
            label.Size = new Vector2(width, height);
            panel.AddChild(label);
        }

        private static Label Styled(string name, string line, GameTextStyle style, Color color)
        {
            var label = new Label
            {
                Name = NodeName(name),
                Text = line,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };

            // Both the font and the size come from this element's native role.
            style.ApplyTo(label);
            label.AddThemeColorOverride("font_color", color);
            return label;
        }

        /// <summary>Health lost, as a numeral: a loss is signed and nothing lost is
        /// a plain zero.</summary>
        private static string Loss(int lost) =>
            lost > 0 ? "-" + lost.ToString(CultureInfo.InvariantCulture) : "0";
    }

    /// <summary>Where a turn sits along a plot. One turn sits in the middle rather
    /// than at the left edge, because a single point has no run to spread over.</summary>
    private static float X(float plotLeft, float plotWidth, int index, int turns) =>
        turns <= 1 ? plotLeft + (plotWidth / 2) : plotLeft + (index * plotWidth / (turns - 1));
}

/// <summary>The panel, and the one control on it that has to be focused.</summary>
internal readonly record struct FightResultPanelNodes(Control Root, Button Done);
