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
/// The primitives they are built out of are <see cref="GlyphArt"/>'s, shared with the
/// library's own family so the box, the stroke and the filled-versus-hollow rule have
/// one owner.
/// </summary>
internal static class TransportGlyphArt
{
    /// <summary>
    /// Draws one glyph into a control of the given size.
    /// </summary>
    /// <param name="name">The node's name, so a test and a client can find it.</param>
    internal static Control Of(TransportGlyph glyph, string name, float size, Color colour) =>
        GlyphArt.Draw(name, Shapes(glyph), size, colour);

    /// <summary>What each glyph is made of, in the design's own 32-unit box.</summary>
    private static IEnumerable<GlyphArt.Shape> Shapes(TransportGlyph glyph) => glyph switch
    {
        // Hollow triangle and bar. Hollow: it only looks.
        TransportGlyph.Back =>
        [
            GlyphArt.Shape.Outline("Triangle", [new(23, 8.5f), new(11.5f, 16), new(23, 23.5f)]),
            GlyphArt.Shape.Fill("Bar", GlyphArt.Rect(7, 8, 2.6f, 16)),
        ],

        // Filled triangle. Filled: it moves the run.
        TransportGlyph.Play => [GlyphArt.Shape.Fill("Triangle", [new(11, 7.5f), new(25.5f, 16), new(11, 24.5f)])],

        TransportGlyph.Pause =>
        [
            GlyphArt.Shape.Fill("Left", GlyphArt.Rect(9.5f, 8, 4.5f, 16)),
            GlyphArt.Shape.Fill("Right", GlyphArt.Rect(18, 8, 4.5f, 16)),
        ],

        TransportGlyph.Step =>
        [
            GlyphArt.Shape.Fill("Triangle", [new(8.5f, 8), new(20.5f, 16), new(8.5f, 24)]),
            GlyphArt.Shape.Fill("Bar", GlyphArt.Rect(22.5f, 8, 2.6f, 16)),
        ],

        // The trainer's mark is the game's selection reticle shrunk to a glyph: the
        // reveal lights that ring on the game's own screen, so the mod is marked by
        // the thing it does.
        TransportGlyph.Mark =>
        [
            GlyphArt.Shape.Outline("Ring", GlyphArt.Circle(16, 16, 9), closed: true),
            GlyphArt.Shape.Fill("Centre", GlyphArt.Circle(16, 16, 2.4f)),
            GlyphArt.Shape.Fill("TickTop", GlyphArt.Rect(14.9f, 3.5f, 2.2f, 4)),
            GlyphArt.Shape.Fill("TickBottom", GlyphArt.Rect(14.9f, 24.5f, 2.2f, 4)),
            GlyphArt.Shape.Fill("TickLeft", GlyphArt.Rect(3.5f, 14.9f, 4, 2.2f)),
            GlyphArt.Shape.Fill("TickRight", GlyphArt.Rect(24.5f, 14.9f, 4, 2.2f)),
        ],

        // The mark with nothing to point at yet: the same ring and ticks, no centre
        // dot. Drawn while a decision is considered and not revealed.
        TransportGlyph.MarkUnlit =>
        [
            GlyphArt.Shape.Outline("Ring", GlyphArt.Circle(16, 16, 9), closed: true),
            GlyphArt.Shape.Fill("TickTop", GlyphArt.Rect(14.9f, 3.5f, 2.2f, 4)),
            GlyphArt.Shape.Fill("TickBottom", GlyphArt.Rect(14.9f, 24.5f, 2.2f, 4)),
            GlyphArt.Shape.Fill("TickLeft", GlyphArt.Rect(3.5f, 14.9f, 4, 2.2f)),
            GlyphArt.Shape.Fill("TickRight", GlyphArt.Rect(24.5f, 14.9f, 4, 2.2f)),
        ],

        // A hollow eye: an almond outline round a hollow ring. Hollow because it only
        // looks - it shows the comparison and marks a fight shown this sitting.
        TransportGlyph.Reveal =>
        [
            GlyphArt.Shape.Outline("Lid", GlyphArt.Almond(16, 16, 11.5f, 6.5f), closed: true),
            GlyphArt.Shape.Outline("Iris", GlyphArt.Circle(16, 16, 3.4f), closed: true),
        ],

        // Play with a hollow bar after it: the run goes on and the hold is the
        // player's. The triangle is filled because it moves the run.
        TransportGlyph.Continue =>
        [
            GlyphArt.Shape.Fill("Triangle", [new(7, 8), new(20, 16), new(7, 24)]),
            GlyphArt.Shape.Outline("Bar", GlyphArt.Rect(22.5f, 8, 3.2f, 16), closed: true),
        ],

        // A circular arrow with a filled head. It restarts the fight, so it is filled.
        TransportGlyph.Again =>
        [
            GlyphArt.Shape.Outline("Arc", GlyphArt.Arc(16, 16, 9, startDegrees: -40, sweepDegrees: 285)),
            GlyphArt.Shape.Fill("Head", [new(20.5f, 8.5f), new(27, 13.5f), new(20, 16.5f)]),
        ],

        TransportGlyph.Jump =>
        [
            GlyphArt.Shape.Fill("First", [new(5, 8.5f), new(14, 16), new(5, 23.5f)]),
            GlyphArt.Shape.Fill("Second", [new(14, 8.5f), new(23, 16), new(14, 23.5f)]),
            GlyphArt.Shape.Fill("Bar", GlyphArt.Rect(24.5f, 8, 2.6f, 16)),
        ],

        TransportGlyph.Warn =>
        [
            GlyphArt.Shape.Fill("Triangle", [new(16, 5), new(28, 26), new(4, 26)]),
            GlyphArt.Shape.Inked("Stroke", GlyphArt.Rect(14.5f, 12, 3, 7)),
            GlyphArt.Shape.Inked("Dot", GlyphArt.Circle(16, 22.5f, 1.7f)),
        ],

        _ => throw new InvalidOperationException($"There is no drawing for the {glyph} glyph."),
    };
}
