using System.Globalization;
using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The player's fight beside the recording's, drawn.
///
/// This is the result the captain asked for after reading the first one: the fight
/// as pictures rather than as prose. The cards each side played are the game's own
/// card art in turn order, the potions are the game's own bottles at the turn they
/// were spent, the summary is four columns of figures, and the chart puts both
/// lines' enemy health lost and player health lost against the turn. The one large
/// popup the previous slice used is gone rather than wrapped around this.
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
    internal const string RootName = "CombatTrainerResult";

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

    private const int TitleFontSize = 26;
    private const int FigureFontSize = 20;
    private const int LabelFontSize = 15;
    private const int SmallFontSize = 12;

    /// <summary>
    /// Assembles the panel.
    /// </summary>
    /// <param name="screen">The result, already computed.</param>
    /// <param name="viewport">The size of the surface it is drawn over.</param>
    /// <param name="art">The game's artwork for a model id, or null where a build has
    /// none. Injected so the panel assembles in a process with no model database.</param>
    /// <param name="font">The font the game's own labels use, or null to leave the
    /// theme's default in place.</param>
    /// <param name="done">What the one button does.</param>
    internal static FightResultPanelNodes Build(
        FightResultScreen screen, Vector2 viewport, Func<string, Texture2D?> art, Font? font, Action done)
    {
        var painter = new Painter(art, font);

        // A comparison fills a panel; a notice is one sentence and a button, and a
        // sentence adrift in the middle of a panel this size reads as a page that
        // failed to load.
        var width = Math.Min(screen.HasComparison ? MaxPanelWidth : NoticePanelWidth, viewport.X - (2 * ScreenMargin));
        var height = Math.Min(
            screen.HasComparison ? MaxPanelHeight : NoticePanelHeight, viewport.Y - (2 * ScreenMargin));

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

        painter.Text(panel, "Title", screen.Title, Pad, 18, width - (2 * Pad), 34, TitleFontSize, TitleText);

        Button button;
        if (!screen.HasComparison)
        {
            painter.Wrapped(
                panel, "Notice", screen.Notice, Pad, 66, width - (2 * Pad), height - 66 - FooterHeight,
                LabelFontSize, PrimaryText);
            button = painter.DoneButton(screen.DoneButton, width, height, done);
            panel.AddChild(button);
            return new FightResultPanelNodes(root, button);
        }

        painter.Text(
            panel, "SameBoundaryNote", screen.SameBoundaryNote, Pad, 56, width - (2 * Pad), 20,
            SmallFontSize, DimText);

        var columnWidth = (width - (3 * Pad)) / 2;
        var right = Pad + columnWidth + Pad;
        var content = height - HeaderHeight - FooterHeight;

        var divider = Box(Rule, Pad + columnWidth + (Pad / 2), HeaderHeight, 1, content);
        divider.Name = NodeName("Divider");
        panel.AddChild(divider);

        painter.Summary(panel, screen, Pad, HeaderHeight, columnWidth, content);
        painter.Chronology(panel, screen, right, HeaderHeight, columnWidth, content);
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
    private sealed class Painter(Func<string, Texture2D?> art, Font? font)
    {
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

            Legend(panel, "Legend.You", screen.Columns[0], YouLine, YouFill, yours, y, columnWidth);
            Legend(panel, "Legend.Them", screen.Columns[1], TheirLine, TheirFill, theirs, y, columnWidth);

            var notes = (26f * screen.Notes.Count) + 18;
            var rowHeight = Math.Min(44f, (height - 36 - notes) / Math.Max(1, screen.Rows.Count));
            var row = y + 36;
            foreach (var figure in screen.Rows)
            {
                var value = figure.Matches ? DimText : TitleText;
                Text(panel, $"Figure.{figure.Label}", figure.Label, x, row, labelWidth, rowHeight,
                    LabelFontSize, figure.Matches ? DimText : SecondaryText);
                Text(panel, $"Figure.{figure.Label}.Yours", figure.Yours, yours, row, columnWidth, rowHeight,
                    FigureFontSize, value, HorizontalAlignment.Center);
                Text(panel, $"Figure.{figure.Label}.Theirs", figure.Theirs, theirs, row, columnWidth, rowHeight,
                    FigureFontSize, value, HorizontalAlignment.Center);

                var rule = Box(Rule, x, row + rowHeight - 1, width, 1);
                rule.Name = NodeName($"Figure.{figure.Label}.Rule");
                panel.AddChild(rule);
                row += rowHeight;
            }

            // The caveats follow the figures rather than sitting at the foot of the
            // column: each is a rule about how to read the numbers above it, and a
            // caveat marooned under a gap reads as a footnote to nothing.
            var note = row + 18;
            for (var index = 0; index < screen.Notes.Count; index++)
            {
                Wrapped(panel, $"Note.{index}", screen.Notes[index], x, note, width, 26, SmallFontSize, DimText);
                note += 26;
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
            Control panel, string name, string label, Color line, Color fill, float x, float y, float width)
        {
            var swatch = Box(line, x + 10, y + 8, 14, 14);
            swatch.Name = NodeName($"{name}.Swatch");
            panel.AddChild(swatch);

            var inside = Box(fill, x + 12, y + 10, 10, 10);
            inside.Name = NodeName($"{name}.Swatch.Inside");
            panel.AddChild(inside);

            Text(panel, name, label, x + 30, y, width - 30, 30, LabelFontSize, line);
        }

        /// <summary>
        /// The turn chronology and the chart of the same turns: what each side played,
        /// and what each turn cost them.
        /// </summary>
        internal void Chronology(Control panel, FightResultScreen screen, float x, float y, float width, float height)
        {
            var chartHeight = Math.Min(250f, height * 0.46f);
            var rows = height - chartHeight - 30;
            var turnWidth = 46f;
            var columnWidth = (width - turnWidth) / 2;
            var yours = x + turnWidth;
            var theirs = yours + columnWidth;

            Text(panel, "Chronology", screen.TurnDetailHeading, x, y, width, 20, LabelFontSize, SecondaryText);
            Text(panel, "Chronology.Turn", screen.Chart.TurnLabel, x, y + 24, turnWidth, 18, SmallFontSize, DimText);
            Text(panel, "Chronology.You", screen.Columns[0], yours, y + 24, columnWidth, 18, SmallFontSize, YouLine);
            Text(panel, "Chronology.Them", screen.Columns[1], theirs + ColumnGutter, y + 24, columnWidth, 18,
                SmallFontSize, TheirText);

            var rowHeight = Math.Min(44f, (rows - 46) / Math.Max(1, screen.Turns.Count));

            // A long fight gets more rows in the same space, and a card drawn at its
            // full height would then be drawn over the turn below it.
            var card = Math.Min(CardHeight, rowHeight - 6);
            var row = y + 46;
            foreach (var turn in screen.Turns)
            {
                Text(panel, $"Turn.{turn.Turn}", turn.Turn.ToString(CultureInfo.InvariantCulture),
                    x, row, turnWidth, rowHeight, LabelFontSize, SecondaryText);
                Side(panel, $"Turn.{turn.Turn}.Yours", turn.Yours, screen.FightOverLabel, YouLine, YouFill,
                    yours, row, columnWidth - ColumnGutter, rowHeight, card);
                Side(panel, $"Turn.{turn.Turn}.Theirs", turn.Theirs, screen.FightOverLabel, TheirLine, TheirFill,
                    theirs + ColumnGutter, row, columnWidth - ColumnGutter, rowHeight, card);
                row += rowHeight;
            }

            Chart(panel, screen.Chart, x, y + height - chartHeight, width, chartHeight);
        }

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
                Text(panel, $"{name}.FightOver", fightOver, x, y, width, height, SmallFontSize, DimText);
                return;
            }

            var cardWidth = cardHeight * CardWidth / CardHeight;
            var potion = Math.Min(PotionSize, cardHeight);
            var chip = x;
            foreach (var card in side.CardModelIds)
            {
                Chip(panel, $"{name}.Card.{card}", card, line, fill, chip, y + ((height - cardHeight) / 2),
                    cardWidth, cardHeight);
                chip += cardWidth + ChipGap;
            }

            foreach (var spent in side.PotionModelIds)
            {
                Chip(panel, $"{name}.Potion.{spent}", spent, line, fill, chip, y + ((height - potion) / 2),
                    potion, potion);
                chip += potion + ChipGap;
            }

            // The turn's own cost, beside what was played. The same number the chart's
            // lower plot draws, where a player reads it while looking at the cards.
            Text(panel, $"{name}.HealthLost", Loss(side.HealthLost), x + width - 46, y, 42, height,
                LabelFontSize, side.HealthLost > 0 ? line : DimText, HorizontalAlignment.Right);
        }

        /// <summary>
        /// The chart: both measures, both lines, against the turn.
        ///
        /// Two plots rather than four lines on one, and one ceiling for both, so a
        /// height on the upper plot means the same as a height on the lower one.
        /// </summary>
        private void Chart(Control panel, FightResultChart chart, float x, float y, float width, float height)
        {
            Text(panel, "Chart", chart.Heading, x, y, width, 20, LabelFontSize, SecondaryText);
            if (!chart.HasTurns) return;

            var plotLeft = x + 128;
            var plotWidth = width - 128;
            // What is left once the heading, the gap between the plots, the turn axis
            // and the lane the potions sit in have taken their share.
            var plotHeight = (height - 92) / 2;

            Plot(panel, "Chart.Enemy", chart, point => point.EnemyHealthLost, chart.EnemyMeasureLabel,
                x, y + 26, plotLeft, plotWidth, plotHeight);
            Plot(panel, "Chart.Player", chart, point => point.HealthLost, chart.PlayerMeasureLabel,
                x, y + 26 + plotHeight + 8, plotLeft, plotWidth, plotHeight);

            var axis = y + 26 + (2 * plotHeight) + 22;
            Text(panel, "Chart.TurnAxis", chart.TurnLabel, x, axis, 108, 20, SmallFontSize, DimText,
                HorizontalAlignment.Right);
            for (var index = 0; index < chart.Turns.Count; index++)
            {
                var at = X(plotLeft, plotWidth, index, chart.Turns.Count);
                Text(panel, $"Chart.Turn.{chart.Turns[index]}", chart.Turns[index].ToString(CultureInfo.InvariantCulture),
                    at - 14, axis, 28, 20, SmallFontSize, SecondaryText, HorizontalAlignment.Center);
                Potions(panel, chart, index, at, axis + 20);
            }
        }

        /// <summary>One measure, both lines.</summary>
        private void Plot(
            Control panel, string name, FightResultChart chart, Func<FightResultPoint, int?> measure, string label,
            float x, float y, float plotLeft, float plotWidth, float height)
        {
            Text(panel, name, label, x, y + (height / 2) - 10, 108, 20, SmallFontSize, DimText,
                HorizontalAlignment.Right);

            var baseline = Box(Rule, plotLeft - 8, y + height, plotWidth + 8, 1);
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
                Width = 2.5f,
                DefaultColor = color,
            };
            panel.AddChild(line);

            foreach (var (turn, value, at) in plotted)
            {
                var dot = Box(color, at.X - 4, at.Y - 4, 8, 8);
                dot.Name = NodeName($"{name}.Point.{turn}");
                // The recording's markers are turned forty-five degrees. Two lines
                // that differ only in colour are one colour-blind player away from
                // being the same line.
                if (marker)
                {
                    dot.PivotOffset = new Vector2(4, 4);
                    dot.Rotation = float.Pi / 4;
                }

                panel.AddChild(dot);
                Text(panel, $"{name}.Value.{turn}", value.ToString(CultureInfo.InvariantCulture),
                    at.X - 20, marker ? at.Y + 4 : at.Y - 22, 40, 18, SmallFontSize, color,
                    HorizontalAlignment.Center);
            }
        }

        /// <summary>The potions either side spent on this turn, under the axis and
        /// bordered by the line that spent them.</summary>
        private void Potions(Control panel, FightResultChart chart, int index, float at, float y)
        {
            var lane = at - (PotionSize / 2);
            foreach (var (series, color, fill) in new[]
                     {
                         (chart.Yours, YouLine, YouFill), (chart.Theirs, TheirLine, TheirFill),
                     })
            {
                foreach (var potion in series.Points[index].PotionModelIds)
                {
                    Chip(
                        panel, $"Chart.Potion.{series.Points[index].Turn}.{series.Label}.{potion}", potion, color,
                        fill, lane, y, PotionSize, PotionSize);
                    lane += PotionSize + ChipGap;
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

            var inside = Box(fill, x + 1, y + 1, width - 2, height - 2);
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
                picture.Size = new Vector2(width - 2, height - 2);
                panel.AddChild(picture);
                return;
            }

            Wrapped(panel, $"{name}.Name", ModelIdNames.Display(modelId), x + 1, y + 1, width - 2, height - 2, 9, line);
        }

        /// <summary>The one control on the panel, and the one thing left to do.</summary>
        internal Button DoneButton(string label, float width, float height, Action done)
        {
            var button = new Button
            {
                Name = NodeName("Done"),
                Text = label,
                Position = new Vector2(width - Pad - 176, height - FooterHeight + 12),
                Size = new Vector2(176, 46),
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
            button.AddThemeFontSizeOverride("font_size", LabelFontSize + 3);
            if (font is not null) button.AddThemeFontOverride("font", font);
            button.Pressed += () => done();
            return button;
        }

        internal Label Text(
            Control panel, string name, string text, float x, float y, float width, float height, int size,
            Color color, HorizontalAlignment alignment = HorizontalAlignment.Left)
        {
            var label = Styled(name, text, size, color);
            label.HorizontalAlignment = alignment;
            label.VerticalAlignment = VerticalAlignment.Center;
            // A figure or a label that would not fit is cut off inside its own box
            // rather than drawn over the one beside it.
            label.ClipText = true;
            Place(panel, label, x, y, width, height);
            return label;
        }

        internal Label Wrapped(
            Control panel, string name, string text, float x, float y, float width, float height, int size,
            Color color)
        {
            var label = Styled(name, text, size, color);
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

        private Label Styled(string name, string text, int size, Color color)
        {
            var label = new Label
            {
                Name = NodeName(name),
                Text = text,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };

            // The font is the game's own, taken from a label already on screen. Its
            // size and colour are ours, because the game's sizes are the sizes of its
            // own screens rather than of a table of figures.
            if (font is not null) label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", size);
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
