using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// One native text role, measured from the game element that Runmobile's element
/// stands in for.
/// </summary>
/// <param name="Font">The font the native element draws.</param>
/// <param name="Size">The size the native element draws, in engine units.</param>
internal readonly record struct GameTextStyle(Font? Font, int Size)
{
    /// <summary>
    /// Puts this native role on one of the mod's own nodes.
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
/// Reads the font and size from a particular native game element.
/// </summary>
internal static class GameText
{
    internal static readonly StringName FontEntry = "font";

    internal static readonly StringName FontSizeEntry = "font_size";

    private static readonly StringName LabelType = "Label";

    private static readonly StringName RichFontEntry = "normal_font";

    private static readonly StringName RichFontSizeEntry = "normal_font_size";

    private static readonly StringName RichTextLabelType = "RichTextLabel";

    private const int Budget = 4000;

    /// <summary>
    /// The font and size one native node draws its text at, or null when the node does
    /// not carry a text role.
    /// </summary>
    internal static GameTextStyle? Of(Node? node)
    {
        if (node is not Control control) return null;

        if (control.HasThemeFontOverride(FontEntry))
        {
            return new GameTextStyle(
                control.GetThemeFont(FontEntry, LabelType),
                control.GetThemeFontSize(FontSizeEntry, LabelType));
        }

        if (control.HasThemeFontOverride(RichFontEntry))
        {
            return new GameTextStyle(
                control.GetThemeFont(RichFontEntry, RichTextLabelType),
                control.GetThemeFontSize(RichFontSizeEntry, RichTextLabelType));
        }

        return null;
    }

    /// <summary>
    /// Reads the first native text element under a known piece of game furniture.
    /// </summary>
    internal static GameTextStyle? Under(Node? root)
    {
        if (root is null) return null;

        var queue = new Queue<Node>();
        queue.Enqueue(root);
        for (var seen = 0; queue.Count > 0 && seen < Budget; seen++)
        {
            var node = queue.Dequeue();
            if (Of(node) is { } style) return style;

            var children = node.GetChildren();
            for (var index = 0; index < children.Count; index++) queue.Enqueue(children[index]);
        }

        return null;
    }

    /// <summary>
    /// Requires a native role where drawing without one would make Runmobile look unlike
    /// the screen it belongs to.
    /// </summary>
    internal static GameTextStyle Require(Node? node, string role) =>
        Of(node) ?? throw new InvalidOperationException($"This build's {role} has no native text style.");

    /// <inheritdoc cref="Require"/>
    internal static GameTextStyle RequireUnder(Node? node, string role) =>
        Under(node) ?? throw new InvalidOperationException($"This build's {role} has no native text style.");
}
