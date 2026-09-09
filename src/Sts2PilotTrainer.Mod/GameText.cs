using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// One native text role, measured: the font the game draws it in and the size it draws
/// it at.
///
/// Every piece of Runmobile's own text is sized from one of these rather than from a
/// number written here, because a number written here is a number that is right on one
/// window and on one build. The game's own labels already carry the answer: they are
/// sized in the same viewport units this mod's nodes are laid out in, so a size read
/// off one is correct wherever the player has put their window.
///
/// <para>The three derived roles are ratios rather than sizes for the same reason. The
/// game offers one ordinary text size per screen and Runmobile's surfaces need a
/// hierarchy above and below it - a heading over a pane, a supporting line under a row,
/// a numeral inside a glyph - so what is written down is the relationship, and the size
/// still comes from the game.</para>
/// </summary>
/// <param name="Font">The game's own font, or null where none could be read - which
/// leaves Godot's default in place: worse-looking, and still readable.</param>
/// <param name="Size">The size the game draws this role at, in engine units.</param>
internal readonly record struct GameTextStyle(Font? Font, int Size)
{
    /// <summary>A heading over a Runmobile surface, where the screen it is on offers no
    /// heading of its own to copy.</summary>
    private const float HeadingRatio = 1.35f;

    /// <summary>A supporting line: the second line of a row, a subtitle, a footnote.
    /// It reads after the line above it and is drawn smaller to say so.</summary>
    private const float SupportingRatio = 0.82f;

    /// <summary>A numeral or a count drawn inside a piece of art - a floor number in a
    /// strip cell, a copy count under a card tile. It is part of the glyph rather than a
    /// line of text.</summary>
    private const float AnnotationRatio = 0.68f;

    /// <summary>The smallest this mod will draw anything. A line nobody can read is not
    /// a line, and the ratios are applied to whatever the game gave us.</summary>
    internal const int SmallestReadable = 10;

    /// <summary>
    /// What this mod shipped with, used where no game label could be read at all: in a
    /// process with no game, and on a build whose furniture moved.
    ///
    /// A fallback rather than a design. Godot's own default label size, which is what
    /// the surrounding stock nodes would draw at anyway.
    /// </summary>
    internal static GameTextStyle Fallback => new(null, 16);

    internal GameTextStyle Heading => Scaled(HeadingRatio);

    internal GameTextStyle Supporting => Scaled(SupportingRatio);

    internal GameTextStyle Annotation => Scaled(AnnotationRatio);

    /// <summary>The same font at a size in proportion to this one.</summary>
    internal GameTextStyle Scaled(float by) =>
        this with { Size = Math.Max(SmallestReadable, (int)Math.Round(Size * by)) };

    /// <summary>
    /// Puts this role on one of the mod's own nodes.
    ///
    /// Both entries are set as overrides on the node itself, which is how the game sets
    /// its own: it carries no project theme, so a control that is merely inside the tree
    /// inherits nothing.
    /// </summary>
    internal T ApplyTo<T>(T control)
        where T : Control
    {
        if (Font is { } font) control.AddThemeFontOverride(GameText.FontEntry, font);
        control.AddThemeFontSizeOverride(GameText.FontSizeEntry, Size);
        return control;
    }
}

/// <summary>
/// The text the game itself is drawing, read off the game's own labels.
///
/// Wanted because every Runmobile surface is this mod's own nodes: a stock Godot label
/// with no font and no size of its own is drawn in Godot's default sans at Godot's
/// default size, which on a screen of the game's own card art reads as a debug overlay.
/// Asking the theme is not enough - the game sets both its font and its size as
/// overrides on the labels themselves rather than through a project theme, so a control
/// that is merely inside the tree inherits nothing.
///
/// <para>So this asks labels that are already on screen. It reads and changes nothing,
/// and it answers <see cref="GameTextStyle.Fallback"/> rather than guessing when it
/// cannot find one.</para>
///
/// <para><b>A caller with a particular native element passes it.</b> That is the whole
/// rule: a Runmobile settings row is sized from the game's own settings row beside it,
/// a pane heading from the popup's own header, a plate row from the list rows it hangs
/// under. <see cref="Of"/> is that reading. <see cref="On"/> is for the surfaces whose
/// screen offers no single equivalent - the transport hanging under the top bar, the
/// result panel over a finished fight - and answers what the screen draws ordinary text
/// at.</para>
/// </summary>
internal static class GameText
{
    /// <summary>The theme entries a Godot label takes its font and size from.</summary>
    internal static readonly StringName FontEntry = "font";

    internal static readonly StringName FontSizeEntry = "font_size";

    private static readonly StringName LabelType = "Label";

    /// <summary>The same two entries on a rich text label, which names them
    /// differently.</summary>
    private static readonly StringName RichFontEntry = "normal_font";

    private static readonly StringName RichFontSizeEntry = "normal_font_size";

    private static readonly StringName RichTextLabelType = "RichTextLabel";

    /// <summary>How much of the scene tree to walk before giving up. The labels wanted
    /// are the screen's own, which are near the root; a budget keeps a deep tree from
    /// turning a font lookup into a frame.</summary>
    private const int Budget = 4000;

    /// <summary>
    /// The font and size one native node draws its text at, or null where it draws none
    /// of the game's own.
    ///
    /// A control carrying no font override is not a game-styled control - it is a stock
    /// Godot one, or one of this mod's - and copying its size would be copying Godot's
    /// default back onto ourselves. The game asserts that override on its own labels as
    /// they become ready, which is what makes its presence the test.
    ///
    /// <para>The size taken is the one the node is drawn at rather than the ceiling its
    /// scene was designed to. The game shrinks a label to fit its own box, and the size
    /// on screen beside this mod's words is the one a player is comparing them against -
    /// so a Runmobile line that took the design ceiling would read visibly larger than
    /// the game's own line next to it.</para>
    ///
    /// <para>Rich text names its entries differently and is asked second rather than by
    /// type, so nothing here needs the game assembly: this reading is the same reading in
    /// a process with no game.</para>
    ///
    /// <para>A size below what anybody could read is not answered at all. The game's own
    /// labels shrink themselves to fit their box and will go down to eight points to do
    /// it, so a label asked before its box was laid out can report a size that is not a
    /// role at all - and one role read wrongly would size a whole Runmobile surface.</para>
    /// </summary>
    internal static GameTextStyle? Of(Node? node)
    {
        if (node is not Control control) return null;

        if (control.HasThemeFontOverride(FontEntry))
        {
            return Readable(
                control.GetThemeFont(FontEntry, LabelType),
                control.GetThemeFontSize(FontSizeEntry, LabelType));
        }

        if (control.HasThemeFontOverride(RichFontEntry))
        {
            return Readable(
                control.GetThemeFont(RichFontEntry, RichTextLabelType),
                control.GetThemeFontSize(RichFontSizeEntry, RichTextLabelType));
        }

        return null;
    }

    /// <inheritdoc cref="Of"/>
    private static GameTextStyle? Readable(Font? font, int size) =>
        size >= GameTextStyle.SmallestReadable ? new GameTextStyle(font, size) : null;

    /// <summary>
    /// The first native role found under a node, breadth first, or the fallback.
    ///
    /// For a caller that knows which piece of the game's furniture it is sitting beside
    /// but not which node inside it carries the text - the game's own settings button
    /// holds its label a level down, and the run-history pane holds its rows several.
    /// </summary>
    internal static GameTextStyle Under(Node? root) => Search(root, first: true) ?? GameTextStyle.Fallback;

    /// <summary>
    /// What the screen this node is on draws ordinary text at.
    ///
    /// The median of the native labels on it rather than the first one found, and that
    /// is the whole point: the first is whichever the walk reached, which on a screen
    /// carrying the version overlay is a debug row and on one carrying a banner is a
    /// title. A screen's ordinary text is what most of its labels are, and the middle of
    /// them is the reading that neither the largest nor the smallest can move.
    /// </summary>
    /// <remarks>The screen is the scene tree's own root where the node is in one. A node
    /// that is not yet in a tree is read from itself downwards instead, which is what a
    /// surface assembled before it is parented gets - and is the only reading available
    /// in a process with no game.</remarks>
    internal static GameTextStyle On(Node? node) =>
        Search(node?.GetTree()?.Root, first: false)
        ?? Search(node, first: false)
        ?? GameTextStyle.Fallback;

    /// <summary>
    /// Walks the tree collecting native roles.
    ///
    /// <paramref name="first"/> stops at the first one found; otherwise every role
    /// within the budget is collected and the middle one is answered. The font comes
    /// from the first role found either way: a screen draws one font and several sizes.
    /// </summary>
    private static GameTextStyle? Search(Node? root, bool first)
    {
        if (root is null) return null;

        var found = new List<GameTextStyle>();
        var queue = new Queue<Node>();
        queue.Enqueue(root);
        for (var seen = 0; queue.Count > 0 && seen < Budget; seen++)
        {
            var node = queue.Dequeue();
            if (Of(node) is { } style)
            {
                if (first) return style;
                found.Add(style);
            }

            // Indexed rather than enumerated: Godot's own child collection is a type the
            // game-free stubs mirror by index and not by enumerator, and this walk is the
            // one place in the mod that runs against both.
            var children = node.GetChildren();
            for (var index = 0; index < children.Count; index++) queue.Enqueue(children[index]);
        }

        if (found.Count == 0) return null;

        var sizes = found.Select(style => style.Size).Order().ToList();
        return new GameTextStyle(found[0].Font, sizes[sizes.Count / 2]);
    }
}
