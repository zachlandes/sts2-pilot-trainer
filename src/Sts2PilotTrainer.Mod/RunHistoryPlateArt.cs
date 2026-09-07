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
/// What the rows borrow instead is the game's own font, through <see cref="GameFont"/>,
/// the same way the fight result panel does. A row is a flat button in the parchment's
/// own ink, with the library's own glyph at its end where it carries one.</para>
///
/// <para><b>Every sentence and every rule is <see cref="RunHistoryPlate"/>'s.</b> This
/// draws what it was handed and decides nothing: which rows there are, which are
/// refused, whether there is a head line at all and what colour it is.</para>
/// </summary>
internal static class RunHistoryPlateArt
{
    /// <summary>A row's height, and the step between rows. Fixed rather than measured
    /// because there is no game node here to measure: the plate is the mod's own
    /// furniture hung under the game's.</summary>
    private const float RowHeight = 34f;

    /// <inheritdoc cref="RowHeight"/>
    private const float RowGap = 4f;

    /// <summary>The head line and a row's label.</summary>
    private const int RowFontSize = 17;

    /// <summary>The reason under the rows, and anything else supporting.</summary>
    private const int ReasonFontSize = 15;

    /// <summary>How far in from the pane's left edge the plate's rows start.</summary>
    private const float Inset = 8f;

    /// <summary>
    /// Hangs the plate under the pane and fills it in.
    ///
    /// It is added to <paramref name="parent"/> before anything is drawn into it and
    /// deliberately: every label here wears the game's own font, which
    /// <see cref="GameFont"/> finds by walking up from the scene tree - so a plate built
    /// outside the tree would come out in Godot's default sans. <paramref name="width"/>
    /// is the pane's own, so the plate is exactly as wide as the thing it belongs to.
    /// </summary>
    internal static Control Build(
        Node parent, RunHistoryPlate plate, IReadOnlyList<ScreenRow> rows, float width)
    {
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
            if (plate.Mark is { } mark) AddMark(root, mark, new Vector2(Inset, y), RowFontSize, colour);
            AddLabel(
                root, head, new Vector2(Inset + (RowFontSize * 1.5f), y),
                width - Inset - (RowFontSize * 1.5f), colour, RowFontSize);
            y += RowFontSize * 1.6f;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            AddRow(
                root, rows[index], index, new Vector2(Inset, y), width - (Inset * 2f));
            y += RowHeight + RowGap;
        }

        if (plate.Reason is { Length: > 0 } reason)
        {
            AddLabel(
                root, reason, new Vector2(Inset, y), width - (Inset * 2f),
                LibraryPalette.Muted, ReasonFontSize);
            y += ReasonFontSize * 1.6f;
        }

        // Said once, beside the rows, and never as a head line. The plate hands it over
        // only where one of its rows would actually stand a player somewhere.
        if (plate.NotSaved is { Length: > 0 } notSaved)
        {
            AddLabel(
                root, notSaved, new Vector2(Inset, y), width - (Inset * 2f),
                LibraryPalette.Muted, ReasonFontSize);
            y += ReasonFontSize * 1.6f;
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
    private static void AddRow(Control root, ScreenRow row, int index, Vector2 at, float width)
    {
        var button = new Button
        {
            Name = $"RunmobilePlateRow{index.ToString(CultureInfo.InvariantCulture)}",
            Text = row.Label,
            Flat = true,
            Position = at,
            Size = new Vector2(width, RowHeight),
            CustomMinimumSize = new Vector2(width, RowHeight),
            Alignment = HorizontalAlignment.Left,
        };

        if (GameFont.Of(root.GetTree()?.Root) is { } font) button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride("font_size", RowFontSize);
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

        var size = RowHeight * 0.5f;
        var art = LibraryGlyphArt.Of(glyph, $"{button.Name}Glyph", size, LibraryPalette.Muted);
        art.Position = new Vector2(width - size - Inset, (RowHeight - size) / 2f);
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
        Control root, string text, Vector2 at, float width, Color colour, int size)
    {
        var label = new Label
        {
            Name = "RunmobilePlateLine",
            Text = text,
            Position = at,
            CustomMinimumSize = new Vector2(width, 0f),
            Size = new Vector2(width, size * 1.4f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        if (GameFont.Of(root.GetTree()?.Root) is { } font) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        root.AddChild(label);
    }
}
