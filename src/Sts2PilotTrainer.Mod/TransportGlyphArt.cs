using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The transport's glyphs, drawn.
///
/// They are the mod's own art and not the game's, because the game has none: no
/// play, pause, step or skip shape appears anywhere in the resources
/// <c>sts2.dll</c> references. Borrowing was the first thing looked for and the
/// answer was that there is nothing to borrow.
///
/// Each glyph is laid out in the design's own 32-unit box and scaled to whatever
/// size it is asked for, so the family stays consistent between a 20-unit button and
/// a 40-unit tooltip. They carry one rule with meaning rather than decoration: a
/// filled shape moves the run, a hollow shape only looks. That is the whole
/// difference between look back, which re-shows a decision, and step, which commits
/// one - and it is visible before the tooltip is read.
///
/// Built from stock Godot nodes for the same reason every other surface in this mod
/// is: this assembly compiles without Godot's source generators, so a
/// <c>Control</c> subclass of ours would never have its overrides called, and stock
/// nodes are what let the whole family be assembled and asserted on with no game.
/// </summary>
internal static class TransportGlyphArt
{
    /// <summary>The box every glyph is drawn in before it is scaled.</summary>
    private const float Box = 32f;

    /// <summary>How thick a hollow stroke is, in box units. The design's inking.</summary>
    private const float Stroke = 2.6f;

    /// <summary>
    /// Draws one glyph into a control of the given size.
    /// </summary>
    /// <param name="name">The node's name, so a test and a client can find it.</param>
    internal static Control Of(TransportGlyph glyph, string name, float size, Color colour)
    {
        var root = new Control
        {
            Name = name,
            Size = new Vector2(size, size),
            CustomMinimumSize = new Vector2(size, size),
            // A glyph is a picture inside a button; the button takes the click.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var unit = size / Box;
        foreach (var shape in Shapes(glyph))
        {
            root.AddChild(shape.Build(root.Name, unit, colour));
        }

        return root;
    }

    /// <summary>What each glyph is made of, in the design's own 32-unit box.</summary>
    private static IEnumerable<Shape> Shapes(TransportGlyph glyph) => glyph switch
    {
        // Hollow triangle and bar. Hollow: it only looks.
        TransportGlyph.Back =>
        [
            Shape.Outline("Triangle", [new(23, 8.5f), new(11.5f, 16), new(23, 23.5f)]),
            Shape.Fill("Bar", Rect(7, 8, 2.6f, 16)),
        ],

        // Filled triangle. Filled: it moves the run.
        TransportGlyph.Play => [Shape.Fill("Triangle", [new(11, 7.5f), new(25.5f, 16), new(11, 24.5f)])],

        TransportGlyph.Pause =>
        [
            Shape.Fill("Left", Rect(9.5f, 8, 4.5f, 16)),
            Shape.Fill("Right", Rect(18, 8, 4.5f, 16)),
        ],

        TransportGlyph.Step =>
        [
            Shape.Fill("Triangle", [new(8.5f, 8), new(20.5f, 16), new(8.5f, 24)]),
            Shape.Fill("Bar", Rect(22.5f, 8, 2.6f, 16)),
        ],

        // The trainer's mark is the game's selection reticle shrunk to a glyph: the
        // reveal lights that ring on the game's own screen, so the mod is marked by
        // the thing it does.
        TransportGlyph.Mark =>
        [
            Shape.Outline("Ring", Circle(16, 16, 9), closed: true),
            Shape.Fill("Centre", Circle(16, 16, 2.4f)),
            Shape.Fill("TickTop", Rect(14.9f, 3.5f, 2.2f, 4)),
            Shape.Fill("TickBottom", Rect(14.9f, 24.5f, 2.2f, 4)),
            Shape.Fill("TickLeft", Rect(3.5f, 14.9f, 4, 2.2f)),
            Shape.Fill("TickRight", Rect(24.5f, 14.9f, 4, 2.2f)),
        ],

        // A circular arrow with a filled head. It restarts the fight, so it is filled.
        TransportGlyph.Again =>
        [
            Shape.Outline("Arc", Arc(16, 16, 9, startDegrees: -40, sweepDegrees: 285)),
            Shape.Fill("Head", [new(20.5f, 8.5f), new(27, 13.5f), new(20, 16.5f)]),
        ],

        TransportGlyph.Jump =>
        [
            Shape.Fill("First", [new(5, 8.5f), new(14, 16), new(5, 23.5f)]),
            Shape.Fill("Second", [new(14, 8.5f), new(23, 16), new(14, 23.5f)]),
            Shape.Fill("Bar", Rect(24.5f, 8, 2.6f, 16)),
        ],

        TransportGlyph.Warn =>
        [
            Shape.Fill("Triangle", [new(16, 5), new(28, 26), new(4, 26)]),
            Shape.Inked("Stroke", Rect(14.5f, 12, 3, 7)),
            Shape.Inked("Dot", Circle(16, 22.5f, 1.7f)),
        ],

        _ => throw new InvalidOperationException($"There is no drawing for the {glyph} glyph."),
    };

    private static Vector2[] Rect(float x, float y, float width, float height) =>
        [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)];

    private static Vector2[] Circle(float x, float y, float radius) => Arc(x, y, radius, 0, 360);

    /// <summary>
    /// A circle or part of one, as points.
    ///
    /// Enough segments that the ring reads as round at the sizes this family is drawn
    /// at, and not so many that a glyph becomes a mesh: measured against the mark,
    /// which is the largest circle here.
    /// </summary>
    private static Vector2[] Arc(float x, float y, float radius, float startDegrees, float sweepDegrees)
    {
        const int segments = 28;
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.DegToRad(startDegrees + (sweepDegrees * i / segments));
            points[i] = new Vector2(x + (radius * Mathf.Cos(angle)), y + (radius * Mathf.Sin(angle)));
        }

        return points;
    }

    /// <summary>The ink every filled shape is outlined in, so a glyph reads against
    /// the game's own art the way the game's own icons do.</summary>
    private static readonly Color InkColour = new(0x1b / 255f, 0x16 / 255f, 0x11 / 255f);

    private readonly record struct Shape(string Name, Vector2[] Points, bool Filled, bool Closed, bool UseInk)
    {
        internal static Shape Fill(string name, Vector2[] points) => new(name, points, true, true, false);

        internal static Shape Outline(string name, Vector2[] points, bool closed = false) =>
            new(name, points, false, closed, false);

        /// <summary>A shape drawn in the ink colour rather than the glyph's, for the
        /// marks that sit inside a filled body.</summary>
        internal static Shape Inked(string name, Vector2[] points) => new(name, points, true, true, true);

        internal Node Build(StringName glyphName, float unit, Color colour)
        {
            var scaled = Points.Select(point => point * unit).ToArray();
            if (Filled)
            {
                return new Polygon2D
                {
                    Name = $"{glyphName}.{Name}",
                    Polygon = scaled,
                    Color = UseInk ? InkColour : colour,
                };
            }

            var line = new Line2D
            {
                Name = $"{glyphName}.{Name}",
                Width = Stroke * unit,
                DefaultColor = colour,
                Points = Closed ? [.. scaled, scaled[0]] : scaled,
            };

            return line;
        }
    }
}
