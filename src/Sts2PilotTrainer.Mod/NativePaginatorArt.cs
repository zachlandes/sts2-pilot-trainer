using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Sts2PilotTrainer.Mod;

internal static class NativePaginatorArt
{
    internal const string PaginatorScene = "res://scenes/screens/paginator.tscn";
    private const string RunHistoryScene =
        "res://scenes/screens/run_history_screen/run_history_arrow.tscn";

    /// <summary>The paginator's own arrow control: the column the game gives a page
    /// arrow beside a paged row, narrower than the row's own items.</summary>
    internal const string ArrowNode = "LeftArrow";

    private static Texture2D? _paginator;
    private static Texture2D? _runHistory;
    private static float? _arrowWidth;

    internal static Texture2D Texture(bool _) =>
        _paginator ??= Read(PaginatorScene, "LeftArrow/Image");

    internal static Texture2D RunHistoryTexture(bool _) =>
        _runHistory ??= Read(RunHistoryScene, "TextureRect");

    /// <summary>
    /// How wide the game draws a page arrow's column beside a paged row, read off the
    /// paginator's own scene the way its art is: the settings paginator is the game's
    /// one paged row, and its arrows stand in a column narrower than the row's items.
    /// The run strip's arrows take that column, so a page keeps a floor a full marker
    /// column per arrow would have cost it.
    /// </summary>
    internal static float ArrowWidth() => _arrowWidth ??= ReadWidth(PaginatorScene, ArrowNode);

    private static float ReadWidth(string scenePath, string nodePath)
    {
        var scene = ResourceLoader.Load<PackedScene>(scenePath)
            ?? throw new InvalidOperationException($"This build has no '{scenePath}' paginator resource.");
        var root = scene.Instantiate();
        try
        {
            var control = root.GetNodeOrNull<Control>(nodePath)
                ?? throw new InvalidOperationException(
                    $"This build's '{scenePath}' has no '{nodePath}' arrow.");
            var width = control.Size.X;
            if (width <= 0f)
            {
                throw new InvalidOperationException(
                    $"This build's '{scenePath}' draws its '{nodePath}' arrow {width} wide.");
            }

            return width;
        }
        finally
        {
            root.Free();
        }
    }

    private static Texture2D Read(string scenePath, string nodePath)
    {
        var scene = ResourceLoader.Load<PackedScene>(scenePath)
            ?? throw new InvalidOperationException($"This build has no '{scenePath}' paginator resource.");
        var root = scene.Instantiate();
        try
        {
            return root.GetNodeOrNull<TextureRect>(nodePath)?.Texture
                ?? throw new InvalidOperationException(
                    $"This build's '{scenePath}' has no '{nodePath}' arrow image.");
        }
        finally
        {
            root.Free();
        }
    }

    internal static Button AddButton(
        Control parent, string name, string tooltip, bool previous, Rect2 bounds,
        Func<bool, Texture2D?> image, Action press)
    {
        var button = new Button
        {
            Name = name,
            Flat = true,
            Position = bounds.Position,
            Size = bounds.Size,
            CustomMinimumSize = bounds.Size,
            TooltipText = tooltip,
        };
        button.Pressed += press;
        button.Connect(
            "gui_input",
            Callable.From<InputEvent>(input =>
            {
                if (!input.IsActionPressed(MegaInput.confirm) &&
                    !input.IsActionPressed(MegaInput.select)) return;
                button.AcceptEvent();
                press();
            }));

        var side = Math.Min(bounds.Size.X, bounds.Size.Y);
        var picture = new TextureRect
        {
            Name = "Image",
            Texture = image(previous),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new Vector2((bounds.Size.X - side) / 2f, (bounds.Size.Y - side) / 2f),
            Size = new Vector2(side, side),
            PivotOffset = new Vector2(side / 2f, side / 2f),
            Scale = new Vector2(previous ? 1f : -1f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        button.AddChild(picture);
        parent.AddChild(button);
        return button;
    }
}
