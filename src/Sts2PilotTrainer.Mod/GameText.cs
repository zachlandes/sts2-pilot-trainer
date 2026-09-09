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
    /// Requires a native role where drawing without one would make Runmobile look unlike
    /// the screen it belongs to.
    /// </summary>
    internal static GameTextStyle Require(Node? node, string role) =>
        Of(node) ?? throw new InvalidOperationException($"This build's {role} has no native text style.");

}
