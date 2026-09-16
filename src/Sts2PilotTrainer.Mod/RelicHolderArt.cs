using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The size the run-history screen draws a relic at, read off the game's own scene.
///
/// <c>NRelicHistory</c> fills an <c>HFlowContainer</c> with one
/// <c>relic_basic_holder.tscn</c> per relic and no separation between them, so the
/// box a relic takes on that screen is the holder's own minimum size and the icon is
/// the holder's <c>Relic/Icon</c> inset from it. The pane's relic rows are the same
/// picture, so they are paced by the same box and draw the icon at the same side; a
/// number written here would be right on one build and a size of the mod's own the
/// moment the game redrew its holder.
///
/// Read the way <see cref="GameText"/> reads a text role: the scene is instantiated
/// once, the two measures are copied and the tree is freed. A build without the
/// scene refuses, and the surface that asked refuses with it, because a relic row at
/// a guessed size is the wrong picture.
/// </summary>
internal static class RelicHolderArt
{
    internal const string HolderScene = "res://scenes/relics/relic_basic_holder.tscn";

    /// <summary>The holder's relic, and the icon inside it whose offsets set the inset.</summary>
    internal const string IconNode = "%Relic/Icon";

    private static RelicHolderMetrics? read;

    /// <summary>The holder's box and the icon's side inside it, on this build.</summary>
    internal static RelicHolderMetrics Metrics() => read ??= Read();

    private static RelicHolderMetrics Read()
    {
        var scene = ResourceLoader.Load<PackedScene>(HolderScene)
            ?? throw new InvalidOperationException($"This build has no '{HolderScene}' relic holder.");
        var root = scene.Instantiate();
        try
        {
            var holder = root as Control
                ?? throw new InvalidOperationException($"This build's '{HolderScene}' is not a control.");
            var icon = root.GetNodeOrNull<Control>(IconNode)
                ?? throw new InvalidOperationException($"This build's '{HolderScene}' has no '{IconNode}' icon.");
            var box = holder.CustomMinimumSize.X;
            // The icon fills the holder less its offsets: a left offset in and a right
            // offset back from the far edge, which the scene spells as a negative
            var side = box - icon.OffsetLeft + icon.OffsetRight;
            if (box <= 0f || side <= 0f || side > box)
            {
                throw new InvalidOperationException(
                    $"This build's '{HolderScene}' draws a {side} icon in a {box} box, which is not a relic holder.");
            }

            return new RelicHolderMetrics(box, side);
        }
        finally
        {
            root.Free();
        }
    }
}

/// <summary>A relic's box on the run-history screen and the icon's side inside it.</summary>
internal readonly record struct RelicHolderMetrics(float Box, float Icon);
