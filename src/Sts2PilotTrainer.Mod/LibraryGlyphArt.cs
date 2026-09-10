using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The library's own glyphs: what a floor of the strip held, what this player has
/// done with it, and the marks the rows carry.
///
/// The same family as the transport's - <see cref="GlyphArt"/>'s 32-unit box, its
/// stroke, and the rule that a filled shape moves the run while a hollow one only
/// looks. The strip is the clearest case of that rule: a floor's kind is drawn hollow
/// because it is a fact about the run, and the tick over it is filled because it is
/// something this player did.
///
/// They are the mod's own art and not the game's for the reason the transport's are:
/// the game has no crown that is not part of a run-summary scene and no free-standing
/// tick. Where the game does have the art - a relic's icon, a card's portrait, the
/// run-history screen's floor icons - this mod uses the game's, which is
/// <c>ModelArt</c>'s and <c>FloorMarkerArt</c>'s job and not this one; the hollow
/// floor glyphs here stand in only for a kind nothing established or an icon a build
/// has not got.
/// </summary>
internal enum LibraryGlyph
{
    /// <summary>A floor that held a fight: a hollow blade.</summary>
    Combat,

    /// <summary>A floor that held a shop: a hollow coin.</summary>
    Shop,

    /// <summary>A floor that held a rest site: a hollow flame.</summary>
    Rest,

    /// <summary>A floor that held an event: a hollow question.</summary>
    Event,

    /// <summary>A floor that held a chest: a hollow chest.</summary>
    Treasure,

    /// <summary>A floor whose recording says nothing about what it held. A hollow ring
    /// and no more: the place exists and nothing was established about it.</summary>
    Unknown,

    /// <summary>Filled: this player has stood in this floor's fight.</summary>
    Played,

    /// <summary>A filled disc, the backing the played badge sits on so it reads over
    /// whatever marker it hangs off.</summary>
    Disc,

    /// <summary>The ring round the strip's selected cell. Hollow: selecting only
    /// looks.</summary>
    Selected,

    /// <summary>A won run.</summary>
    Crown,

    /// <summary>A run that ended any other way: the crown, struck through.</summary>
    CrownStruck,

    /// <summary>A run this mod has a recording of. The mark at a history row's
    /// end.</summary>
    Recorded,

    /// <summary>The disclosure chevron: this row opens a deeper screen about the same
    /// thing. Hollow, because drilling in only looks.</summary>
    Chevron,

    /// <summary>Removes something, through the game's own confirm.</summary>
    Bin,

    /// <summary>
    /// A bookmark tab: a ribbon left in a book at this page. Filled, in gold, because
    /// the recording's own player marked this fight - a fact about the run, and the one
    /// filled shape on the strip that is not this player's own doing, which is why it
    /// hangs off the opposite corner from the tick and wears a different colour.
    /// </summary>
    Bookmark,

    /// <summary>The same tab as a hollow stroke: the bookmark tag's control at rest,
    /// and the ink hairline round the filled tab so it holds on parchment and on
    /// charcoal alike.</summary>
    BookmarkOutline,
}

/// <inheritdoc cref="LibraryGlyph"/>
internal static class LibraryGlyphArt
{
    /// <summary>Draws one glyph into a control of the given size.</summary>
    internal static Control Of(LibraryGlyph glyph, string name, float size, Color colour) =>
        GlyphArt.Draw(name, Shapes(glyph), size, colour);

    /// <summary>
    /// Which glyph a floor's kind is drawn as.
    ///
    /// Total over the kinds, and an unknown kind is a glyph of its own rather than a
    /// missing one: the strip has a cell for every floor the run reached, and a cell
    /// with nothing in it would read as a floor that is not there.
    /// </summary>
    internal static LibraryGlyph For(FloorKind kind) => kind switch
    {
        FloorKind.Combat => LibraryGlyph.Combat,
        FloorKind.Shop => LibraryGlyph.Shop,
        FloorKind.Rest => LibraryGlyph.Rest,
        FloorKind.Event => LibraryGlyph.Event,
        FloorKind.Treasure => LibraryGlyph.Treasure,
        FloorKind.Unknown => LibraryGlyph.Unknown,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "There is no strip glyph for it."),
    };

    private static readonly Vector2[] BookmarkTab =
        [new(9, 4), new(23, 4), new(23, 28), new(16, 21), new(9, 28)];

    private static IEnumerable<GlyphArt.Shape> Shapes(LibraryGlyph glyph) => glyph switch
    {
        // A blade: a hollow point over a hollow guard. It is what happened on the
        // floor, so it is hollow.
        LibraryGlyph.Combat =>
        [
            GlyphArt.Shape.Outline("Blade", [new(16, 4), new(20, 12), new(20, 21), new(12, 21), new(12, 12)], closed: true),
            GlyphArt.Shape.Fill("Guard", GlyphArt.Rect(8, 21, 16, 2.6f)),
        ],

        LibraryGlyph.Shop =>
        [
            GlyphArt.Shape.Outline("Coin", GlyphArt.Circle(16, 16, 10), closed: true),
            GlyphArt.Shape.Outline("Slot", GlyphArt.Rect(14.5f, 10, 3, 12), closed: true),
        ],

        // A flame: a teardrop the wrong way up, hollow.
        LibraryGlyph.Rest =>
        [
            GlyphArt.Shape.Outline(
                "Flame",
                [new(16, 4), new(23, 14), new(23, 22), new(16, 27), new(9, 22), new(9, 14)],
                closed: true),
        ],

        // A question: the hook and the dot, both hollow strokes.
        LibraryGlyph.Event =>
        [
            GlyphArt.Shape.Outline("Hook", GlyphArt.Arc(16, 12, 6, startDegrees: 170, sweepDegrees: 240)),
            GlyphArt.Shape.Outline("Stem", [new(16, 18), new(16, 21)]),
            GlyphArt.Shape.Outline("Dot", GlyphArt.Circle(16, 25, 1.4f), closed: true),
        ],

        LibraryGlyph.Treasure =>
        [
            GlyphArt.Shape.Outline("Body", GlyphArt.Rect(5, 12, 22, 14), closed: true),
            GlyphArt.Shape.Outline("Lid", [new(5, 12), new(9, 6), new(23, 6), new(27, 12)], closed: true),
            GlyphArt.Shape.Fill("Latch", GlyphArt.Rect(14.5f, 15, 3, 6)),
        ],

        LibraryGlyph.Unknown => [GlyphArt.Shape.Outline("Ring", GlyphArt.Circle(16, 16, 7), closed: true)],

        // Filled: it is something this player did.
        LibraryGlyph.Played => [GlyphArt.Shape.Fill("Tick", [new(6, 16), new(13, 23), new(26, 8), new(13, 19.5f)])],

        LibraryGlyph.Disc => [GlyphArt.Shape.Fill("Disc", GlyphArt.Circle(16, 16, 15))],

        LibraryGlyph.Selected =>
            [GlyphArt.Shape.Outline("Ring", GlyphArt.Circle(16, 16, 14), closed: true)],

        LibraryGlyph.Crown =>
        [
            GlyphArt.Shape.Fill(
                "Crown",
                [new(4, 24), new(4, 9), new(10, 15), new(16, 6), new(22, 15), new(28, 9), new(28, 24)]),
        ],

        // The crown with one stroke through it. The struck crown is the run that ended
        // any other way, and it is the same shape so the two read as one column.
        LibraryGlyph.CrownStruck =>
        [
            GlyphArt.Shape.Outline(
                "Crown",
                [new(4, 24), new(4, 9), new(10, 15), new(16, 6), new(22, 15), new(28, 9), new(28, 24)],
                closed: true),
            GlyphArt.Shape.Outline("Strike", [new(4, 27), new(28, 5)]),
        ],

        // A filled dot inside a hollow ring: something exists here, and the mark itself
        // does nothing.
        LibraryGlyph.Recorded =>
        [
            GlyphArt.Shape.Outline("Ring", GlyphArt.Circle(16, 16, 9), closed: true),
            GlyphArt.Shape.Fill("Centre", GlyphArt.Circle(16, 16, 4)),
        ],

        LibraryGlyph.Chevron => [GlyphArt.Shape.Outline("Chevron", [new(12, 6), new(22, 16), new(12, 26)])],

        LibraryGlyph.Bin =>
        [
            GlyphArt.Shape.Outline("Body", [new(8, 10), new(24, 10), new(22, 27), new(10, 27)], closed: true),
            GlyphArt.Shape.Fill("Lid", GlyphArt.Rect(5, 7, 22, 2.6f)),
            GlyphArt.Shape.Fill("Handle", GlyphArt.Rect(13, 4, 6, 2.6f)),
        ],

        // A vertical tab with a swallow-tail foot, about 14 wide by 24 tall in the box
        LibraryGlyph.Bookmark => [GlyphArt.Shape.Fill("Tab", BookmarkTab)],

        LibraryGlyph.BookmarkOutline => [GlyphArt.Shape.Outline("Tab", BookmarkTab, closed: true)],

        _ => throw new InvalidOperationException($"There is no drawing for the {glyph} glyph."),
    };
}
