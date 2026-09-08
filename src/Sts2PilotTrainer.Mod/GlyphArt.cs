using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The geometry every glyph this mod draws is built out of.
///
/// One builder rather than one per family. The transport's glyphs and the library's
/// are the same family drawn at different sizes - the same 32-unit box, the same
/// stroke, the same rule that a filled shape moves the run and a hollow shape only
/// looks - and two copies of the primitives would be two places for that rule to
/// drift. What differs between the families is which shapes each glyph is made of,
/// and that stays with each of them.
///
/// Built from stock Godot nodes for the same reason every other surface in this mod
/// is: this assembly compiles without Godot's source generators, so a
/// <c>Control</c> subclass of ours would never have its overrides called, and stock
/// nodes are what let a whole family be assembled and asserted on with no game.
/// </summary>
internal static class GlyphArt
{
    /// <summary>The box every glyph is drawn in before it is scaled.</summary>
    internal const float Box = 32f;

    /// <summary>How thick a hollow stroke is, in box units. The design's inking.</summary>
    internal const float Stroke = 2.6f;

    /// <summary>
    /// Draws one glyph's shapes into a control of the given size.
    /// </summary>
    /// <param name="name">The node's name, so a test and a client can find it.</param>
    internal static Control Draw(string name, IEnumerable<Shape> shapes, float size, Color colour)
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
        foreach (var shape in shapes) root.AddChild(shape.Build(root.Name, unit, colour));
        return root;
    }

    internal static Vector2[] Rect(float x, float y, float width, float height) =>
        [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)];

    internal static Vector2[] Circle(float x, float y, float radius) => Arc(x, y, radius, 0, 360);

    /// <summary>An eye's lid: two arcs meeting at the corners, as points.</summary>
    internal static Vector2[] Almond(float x, float y, float halfWidth, float depth)
    {
        const int segments = 14;
        var points = new Vector2[segments * 2];
        for (var i = 0; i < segments; i++)
        {
            var t = (float)i / segments;
            var bulge = depth * Mathf.Sin(Mathf.Pi * t);
            points[i] = new Vector2(x - halfWidth + (2 * halfWidth * t), y - bulge);
            points[segments + i] = new Vector2(x + halfWidth - (2 * halfWidth * t), y + bulge);
        }

        return points;
    }

    /// <summary>
    /// A circle or part of one, as points.
    ///
    /// Enough segments that the ring reads as round at the sizes this family is drawn
    /// at, and not so many that a glyph becomes a mesh: measured against the mark,
    /// which is the largest circle here.
    /// </summary>
    internal static Vector2[] Arc(float x, float y, float radius, float startDegrees, float sweepDegrees)
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
    internal static readonly Color InkColour = new(0x1b / 255f, 0x16 / 255f, 0x11 / 255f);

    internal readonly record struct Shape(string Name, Vector2[] Points, bool Filled, bool Closed, bool UseInk)
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
