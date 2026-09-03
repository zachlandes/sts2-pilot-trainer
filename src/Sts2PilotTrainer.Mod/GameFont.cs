using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The font the game's own labels are drawn in.
///
/// Wanted because the result panel is this mod's own nodes: a stock Godot label with
/// no font of its own is drawn in Godot's default sans, which on a screen of the
/// game's own card art reads as a debug overlay. Asking the theme is not enough - the
/// game sets its fonts as overrides on the labels themselves rather than through a
/// project theme, so a control that is merely inside the tree inherits nothing.
///
/// So this asks a label that is already on screen. It reads and changes nothing, and
/// it answers null rather than guessing when it cannot find one, which leaves the
/// panel in the default font: worse-looking, and still readable.
/// </summary>
internal static class GameFont
{
    /// <summary>The theme entry a Godot label takes its font from.</summary>
    private static readonly StringName FontEntry = "font";

    private static readonly StringName LabelType = "Label";

    /// <summary>How much of the scene tree to walk before giving up. The labels
    /// wanted are the top bar's and the screen's own, which are near the root; a
    /// budget keeps a deep tree from turning a font lookup into a frame.</summary>
    private const int Budget = 4000;

    /// <summary>
    /// The font of the first label on screen that carries one, breadth first from the
    /// scene tree's root so the outermost screens are asked before their contents.
    /// </summary>
    internal static Font? Of(Node? root)
    {
        if (root is null) return null;

        var queue = new Queue<Node>();
        queue.Enqueue(root);
        for (var seen = 0; queue.Count > 0 && seen < Budget; seen++)
        {
            var node = queue.Dequeue();
            if (node is Label label && label.HasThemeFontOverride(FontEntry))
            {
                return label.GetThemeFont(FontEntry, LabelType);
            }

            foreach (var child in node.GetChildren()) queue.Enqueue(child);
        }

        return null;
    }
}
