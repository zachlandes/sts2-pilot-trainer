using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The run-history plate, drawn flat.
///
/// A plate rather than a panel: no parchment of its own, no border, no shadow. It hangs
/// under the game's own pane and reads as part of that screen, which is the whole
/// difference between this and the modal it replaces - a modal is a screen about a run
/// and this is a strip of rows under the run already on screen.
///
/// <para>It is built from stock Godot nodes rather than duplicated game ribbons for one
/// reason: the ribbons it would duplicate live on a popup, and there is no popup here.
/// What the rows borrow instead is the game's own text, through <see cref="GameText"/>:
/// a row is drawn at the size the run-history screen draws its own rows, so the plate
/// reads as more of that screen rather than as a smaller thing hung under it. A row is a
/// flat button in the parchment's own ink, with the library's own glyph at its end where
/// it carries one.</para>
///
/// <para><b>Every sentence and every rule is <see cref="RunHistoryPlate"/>'s.</b> This
/// draws what it was handed and decides nothing: which rows there are, which are
/// refused, whether there is a head line at all and what colour it is.</para>
/// </summary>
internal static class RunHistoryPlateArt
{
    /// <summary>A row's height, as a multiple of the text in it. Derived rather than
    /// written down: the text is the screen's own size, which changes with the window,
    /// and a fixed height would clip it on a large one and float in it on a small.</summary>
    private const float RowHeightRatio = 2f;

    /// <summary>The gap between rows, as the same multiple.</summary>
    private const float RowGapRatio = 0.24f;

    /// <summary>The step from one line to the next, as a multiple of its own size.</summary>
    private const float LineStep = 1.6f;

    /// <summary>How far in from the pane's left edge the plate's rows start.</summary>
    private const float Inset = 8f;

    /// <summary>
    /// Hangs the plate under the pane and fills it in.
    ///
    /// It is added to <paramref name="parent"/> before anything is drawn into it and
    /// deliberately: every label here wears the game's own font, which
    /// <see cref="GameText"/> finds by walking the scene tree - so a plate built outside
    /// the tree would come out in Godot's default sans. <paramref name="width"/> is the
    /// pane's own, so the plate is exactly as wide as the thing it belongs to.
    /// </summary>
    /// <param name="text">The style the run-history screen draws its own rows in.</param>
    internal static Control Build(
        Node parent, RunHistoryPlate plate, IReadOnlyList<ScreenRow> rows, float width,
        GameTextStyle text)
    {
        var reason = text;
        var rowHeight = text.Size * RowHeightRatio;
        var rowGap = text.Size * RowGapRatio;

        var root = new Control
        {
            Name = "RunmobilePlate",
            MouseFilter = Control.MouseFilterEnum.Pass,
            Size = new Vector2(width, 0f),
        };
        parent.AddChild(root);

        var y = 0f;
        if (plate.Head is { Length: > 0 } head)
        {
            // A head only where there is a status to state, and its mark says which.
            // The ordinary state has neither: the history row's own record mark already
            // says the run is recorded.
            var colour = plate.Mark == PlateMark.OtherVersion ? LibraryPalette.Red : LibraryPalette.Muted;
            if (plate.Mark is { } mark) AddMark(root, mark, new Vector2(Inset, y), text.Size, colour);
            AddLabel(
                root, head, new Vector2(Inset + (text.Size * 1.5f), y),
                width - Inset - (text.Size * 1.5f), colour, text);
            y += text.Size * LineStep;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            AddRow(
                root, rows[index], index, new Vector2(Inset, y), width - (Inset * 2f),
                rowHeight, text);
            y += rowHeight + rowGap;
        }

        if (plate.Reason is { Length: > 0 } why)
        {
            AddLabel(
                root, why, new Vector2(Inset, y), width - (Inset * 2f),
                LibraryPalette.Muted, reason);
            y += reason.Size * LineStep;
        }

        // Said once, beside the rows, and never as a head line. The plate hands it over
        // only where one of its rows would actually stand a player somewhere.
        if (plate.NotSaved is { Length: > 0 } notSaved)
        {
            AddLabel(
                root, notSaved, new Vector2(Inset, y), width - (Inset * 2f),
                LibraryPalette.Muted, reason);
            y += reason.Size * LineStep;
        }

        root.Size = new Vector2(width, y);
        root.CustomMinimumSize = root.Size;
        return root;
    }

    /// <summary>
    /// One row: a flat button in the parchment's ink, with its glyph at the end where
    /// it has one.
    ///
    /// A refused row keeps its place, dimmed, and takes no input - the same rule every
    /// refused row in this mod follows, because the affordance's position is how a
    /// player learns the feature exists.
    /// </summary>
    private static void AddRow(
        Control root, ScreenRow row, int index, Vector2 at, float width, float height,
        GameTextStyle text)
    {
        var button = new Button
        {
            Name = $"RunmobilePlateRow{index.ToString(CultureInfo.InvariantCulture)}",
            Text = row.Label,
            Flat = true,
            Position = at,
            Size = new Vector2(width, height),
            CustomMinimumSize = new Vector2(width, height),
            Alignment = HorizontalAlignment.Left,
        };

        text.ApplyTo(button);
        button.AddThemeColorOverride("font_color", LibraryPalette.Ink);
        root.AddChild(button);

        if (row.Enabled)
        {
            var press = row;
            button.Pressed += () => LibraryScreenPress(press);
        }
        else
        {
            button.Modulate = new Color(1f, 1f, 1f, 0.45f);
            button.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.FocusMode = Control.FocusModeEnum.None;
        }

        if (row.Glyph is not { } glyph) return;

        var size = height * 0.5f;
        var art = LibraryGlyphArt.Of(glyph, $"{button.Name}Glyph", size, LibraryPalette.Muted);
        art.Position = new Vector2(width - size - Inset, (height - size) / 2f);
        button.AddChild(art);
    }

    /// <summary>
    /// Runs a row's action without taking the screen down first.
    ///
    /// The one place this plate differs from every other press in the library: there is
    /// no modal to dismiss, because the plate is part of the screen a player is already
    /// on. The row itself takes the plate down where it needs to.
    /// </summary>
    private static void LibraryScreenPress(ScreenRow row)
    {
        try
        {
            row.Press();
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error(
                $"[{RunmobileMod.ModId}] could not act on '{row.Label}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>The head's mark. The warning triangle in both states that carry one -
    /// the other-version state is the same mark in the eligibility screen's red, which
    /// is what says it is the sharper of the two.</summary>
    private static void AddMark(Control root, PlateMark mark, Vector2 at, float size, Color colour)
    {
        var glyph = mark switch
        {
            PlateMark.Warning => TransportGlyph.Warn,
            PlateMark.OtherVersion => TransportGlyph.Warn,
            _ => throw new ArgumentOutOfRangeException(nameof(mark), mark, "There is no mark for it."),
        };

        var art = TransportGlyphArt.Of(glyph, "PlateMark", size, colour);
        art.Position = at;
        root.AddChild(art);
    }

    private static void AddLabel(
        Control root, string line, Vector2 at, float width, Color colour, GameTextStyle style)
    {
        var label = new Label
        {
            Name = "RunmobilePlateLine",
            Text = line,
            Position = at,
            CustomMinimumSize = new Vector2(width, 0f),
            Size = new Vector2(width, style.Size * 1.4f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        style.ApplyTo(label);
        label.AddThemeColorOverride("font_color", colour);
        root.AddChild(label);
    }
}
