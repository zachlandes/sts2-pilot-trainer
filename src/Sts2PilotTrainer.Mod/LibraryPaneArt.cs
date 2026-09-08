using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The right-hand pane, drawn: the run's identity, its relics, its deck as card tiles,
/// the run strip full width, and the flat plate under it.
///
/// Separate from <see cref="LibraryScreen"/> because they answer different questions.
/// That one owns the panel and the list - the parchment, the tabs, the row column, the
/// paging - and this owns what one run looks like. The run view and the browser's pane
/// are the same drawing with different parts filled in, which is what keeps a strip in
/// one of them and a strip in the other from drifting apart.
///
/// <para><b>The game's art where the game has it, the mod's where it does not.</b> A
/// relic is the game's own icon and a card is the game's own portrait, both through
/// <see cref="ModelArt"/>, so this mod ships no resource pack. A strip cell, a tick, a
/// ring and a crown are <see cref="LibraryGlyphArt"/>'s, because the game has no
/// free-standing glyph at that size to borrow. Where a piece of the game's art is
/// missing on this build, the name is written where the picture would have been -
/// which is the honest answer and never the wrong picture.</para>
///
/// <para><b>A value the recording does not carry is a gap, never a zero.</b> A deck
/// nothing recorded draws no tiles; a health nobody sampled is not written; a relic
/// list nothing said about is empty rather than "no relics". That is
/// <c>docs/comparison-direction.md</c>'s rule for the fight result, and it is the same
/// kind of claim here.</para>
/// </summary>
internal static class LibraryPaneArt
{
    /// <summary>How tall the identity line is, as a share of a ribbon's height.</summary>
    private const int HeadingFontSize = 21;

    /// <summary>Supporting lines: the subtitle, the facts, the verdict.</summary>
    private const int LineFontSize = 15;

    /// <summary>How big a strip cell is, as a share of the strip's own height. The
    /// selected ring is drawn in the rest of it.</summary>
    private const float CellShare = 0.62f;

    /// <summary>How many card tiles a row of the deck holds before it wraps.</summary>
    private const int TilesPerRow = 8;

    private const float MinimumStripPitch = 28f;
    private const float StripRowStep = 1.55f;

    internal readonly record struct StripLayout(
        int Columns, int Rows, float Pitch, float Cell, float Height)
    {
        internal int ColumnOf(int index) => index % Columns;

        internal int RowOf(int index) => index / Columns;

        internal int LeftOf(int index) => index > 0 ? index - 1 : index;

        internal int RightOf(int index, int count) => index + 1 < count ? index + 1 : index;

        internal int Above(int index) => index >= Columns ? index - Columns : index;

        internal int Below(int index, int count) => index + Columns < count ? index + Columns : index;

        internal float RowOffset(int index, int count, float width)
        {
            var first = RowOf(index) * Columns;
            var cells = Math.Min(Columns, count - first);
            return (width - (cells * Pitch)) / 2f;
        }
    }

    internal static StripLayout LayoutStrip(int count, float width)
    {
        var columns = Math.Max(1, Math.Min(count, (int)Math.Floor(width / MinimumStripPitch)));
        var pitch = width / columns;
        var cell = Math.Min(pitch * 0.9f, LineFontSize * 2.8f) * CellShare;
        var height = cell / CellShare;
        var rows = (count + columns - 1) / columns;
        return new StripLayout(columns, rows, pitch, cell, height);
    }

    /// <summary>
    /// Draws the pane into the area it was given, and returns the first control a
    /// player can press there - which is where focus goes when the list has nothing
    /// pressable.
    /// </summary>
    internal static Control? Add(NVerticalPopup content, ScreenPane pane, Rect2 at)
    {
        var y = at.Position.Y;
        y = LibraryScreen.AddLine(
            content, pane.Heading, new Vector2(at.Position.X, y), at.Size.X,
            LibraryPalette.Muted, HeadingFontSize);

        if (pane.Subtitle is { Length: > 0 } subtitle)
        {
            y = LibraryScreen.AddLine(
                content, subtitle, new Vector2(at.Position.X, y), at.Size.X,
                LibraryPalette.Muted, LineFontSize);
        }

        // Relics and the deck count top right, which is where the accepted layout puts
        // them: they are what the run carried, read across the top rather than down the
        // pane.
        y = AddRelics(content, pane, new Vector2(at.Position.X, y), at.Size.X);
        if (pane.DeckCount is { } cards)
        {
            y = LibraryScreen.AddLine(
                content,
                LibraryCopy.DeckCount(cards),
                new Vector2(at.Position.X, y),
                at.Size.X,
                LibraryPalette.Muted,
                LineFontSize,
                HorizontalAlignment.Right);
        }

        var strip = AddStrip(content, pane, new Vector2(at.Position.X, y), at.Size.X);
        y = strip.Bottom;
        // Keep the deck in the opened-run pane when browser actions need its room
        if (pane.Plate.Count == 0)
            y = AddDeck(content, pane, new Vector2(at.Position.X, y), at.Size.X);

        foreach (var fact in pane.Facts)
        {
            y = LibraryScreen.AddLine(
                content, fact, new Vector2(at.Position.X, y), at.Size.X,
                LibraryPalette.Muted, LineFontSize);
        }

        if (pane.Verdict is { Length: > 0 } verdict)
        {
            y = LibraryScreen.AddLine(
                content, verdict, new Vector2(at.Position.X, y), at.Size.X,
                pane.VerdictPassed ? LibraryPalette.Green : LibraryPalette.Red, LineFontSize);
        }

        var plateFocus = AddPlate(
            content, pane, new Vector2(at.Position.X, y), at.Size.X, at.End.Y);
        return strip.Focus ?? plateFocus;
    }

    /// <summary>
    /// The relic strip: the game's own icons, in the order the run found them, laid
    /// across the top of the pane.
    ///
    /// The pane carries every relic rather than the row's handful, which is why there
    /// is no hover here: each icon is the game's own art and the row above is where the
    /// scanning happens. A relic this build has no icon for is written by name instead,
    /// so the pane never has a hole where a relic was.
    /// </summary>
    private static float AddRelics(NVerticalPopup content, ScreenPane pane, Vector2 at, float width)
    {
        if (pane.Relics.Count == 0) return at.Y;

        var size = LineFontSize * 1.7f;
        var perRow = Math.Max(1, (int)Math.Floor(width / (size * 1.15f)));
        var y = at.Y;
        for (var index = 0; index < pane.Relics.Count; index++)
        {
            var id = pane.Relics[index];
            var column = index % perRow;
            if (column == 0 && index > 0) y += size * 1.15f;

            var position = new Vector2(at.X + (column * size * 1.15f), y);
            if (ModelArt.Of(id) is { } icon)
            {
                // Ignore intrinsic size before assigning art or Godot keeps the texture's minimum
                var art = new TextureRect
                {
                    Name = $"RunmobileRelic{index.ToString(CultureInfo.InvariantCulture)}",
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    Texture = icon,
                    Position = position,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(size, size),
                    CustomMinimumSize = new Vector2(size, size),
                    // The game's own tooltip is the relic's, and it needs the mouse.
                    MouseFilter = Control.MouseFilterEnum.Stop,
                    ClipContents = true,
                    TooltipText = ModelIdNames.Display(id),
                };
                content.AddChild(art);
            }
            else
            {
                LibraryScreen.AddLine(
                    content, ModelIdNames.Display(id), position, size * 1.1f,
                    LibraryPalette.Muted, LineFontSize - 3);
            }
        }

        return y + (size * 1.85f);
    }

    /// <summary>
    /// The run strip, full width: one cell per floor the run reached, in the floor's
    /// own glyph, with a tick over the ones this player has stood in and a ring round
    /// the selected one.
    ///
    /// A cell is pressable where the pane can move the selection - the run view's strip
    /// can, and the browser pane's cannot, because a pane's one way forward is its
    /// ribbon. A cell whose floor is not a place to stand is drawn and not pressable:
    /// the strip says what the run did, and where a player can be stood is the rows'
    /// answer.
    /// </summary>
    private static (float Bottom, Control? Focus) AddStrip(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width)
    {
        if (pane.Strip.Count == 0) return (at.Y, null);

        var layout = LayoutStrip(pane.Strip.Count, width);
        var controls = new List<Control>();
        for (var index = 0; index < pane.Strip.Count; index++)
        {
            var floor = pane.Strip[index];
            var x = at.X + layout.RowOffset(index, pane.Strip.Count, width) +
                    (layout.ColumnOf(index) * layout.Pitch);
            var y = at.Y + (layout.RowOf(index) * layout.Height * StripRowStep);
            var box = new Control
            {
                Name = $"RunmobileStrip{floor.Floor.ToString(CultureInfo.InvariantCulture)}",
                Position = new Vector2(x, y),
                Size = new Vector2(layout.Pitch, layout.Height),
                CustomMinimumSize = new Vector2(layout.Pitch, layout.Height),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                TooltipText = LibraryCopy.FloorLine(floor.Floor, floor.Kind),
            };
            content.AddChild(box);

            var inset = (layout.Height - layout.Cell) / 2f;
            var kind = LibraryGlyphArt.Of(
                LibraryGlyphArt.For(floor.Kind),
                "Kind",
                layout.Cell,
                floor.Playable ? LibraryPalette.Line : LibraryPalette.Line with { A = 0.45f });
            kind.Position = new Vector2((layout.Pitch - layout.Cell) / 2f, inset);
            box.AddChild(kind);
            LibraryScreen.AddLine(
                content,
                floor.Floor.ToString(CultureInfo.InvariantCulture),
                new Vector2(x, y + (layout.Height * 0.72f)),
                layout.Pitch,
                LibraryPalette.Muted,
                LineFontSize - 3,
                HorizontalAlignment.Center);

            // Filled, over the cell: it is something this player did.
            if (floor.Played)
            {
                var tick = LibraryGlyphArt.Of(
                    LibraryGlyph.Played, "Played", layout.Cell * 0.7f, LibraryPalette.Teal);
                tick.Position = new Vector2(
                    (layout.Pitch - (layout.Cell * 0.7f)) / 2f,
                    inset + (layout.Cell * 0.35f));
                box.AddChild(tick);
            }

            if (floor.Selected)
            {
                var ring = LibraryGlyphArt.Of(
                    LibraryGlyph.Selected, "Selected", layout.Height, LibraryPalette.Ink);
                ring.Position = new Vector2((layout.Pitch - layout.Height) / 2f, 0f);
                box.AddChild(ring);
            }

            if (pane.SelectFloor is not { } select) continue;

            // A cell that moves the selection is a button under the glyph rather than
            // the glyph itself: the glyph is a picture and the game's own hover, focus
            // and press behaviour belong to a button.
            var number = floor.Floor;
            var press = new Button
            {
                Name = $"{box.Name}Press",
                Flat = true,
                Position = Vector2.Zero,
                Size = box.Size,
                CustomMinimumSize = box.Size,
                TooltipText = box.TooltipText,
            };
            box.MouseFilter = Control.MouseFilterEnum.Pass;
            press.Pressed += () => LibraryScreen.Navigate(
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                () => select(number));
            box.AddChild(press);
            controls.Add(press);
        }

        for (var index = 0; index < controls.Count; index++)
        {
            controls[index].FocusNeighborLeft = controls[layout.LeftOf(index)].GetPath();
            controls[index].FocusNeighborRight = controls[layout.RightOf(index, controls.Count)].GetPath();
            controls[index].FocusNeighborTop = controls[layout.Above(index)].GetPath();
            controls[index].FocusNeighborBottom = controls[layout.Below(index, controls.Count)].GetPath();
        }

        return (
            at.Y + (layout.Rows * layout.Height * StripRowStep),
            controls.FirstOrDefault());
    }

    /// <summary>
    /// The deck at this position's start, as card tiles.
    ///
    /// The game's own portrait per card with its count beside it, wrapped into rows. A
    /// deck the recording says nothing about draws nothing at all rather than an empty
    /// frame, because "no deck was recorded here" and "the deck was empty" are different
    /// facts and a frame would state the second.
    /// </summary>
    private static float AddDeck(NVerticalPopup content, ScreenPane pane, Vector2 at, float width)
    {
        if (pane.Deck is not { Count: > 0 } deck) return at.Y;

        var tile = width / TilesPerRow;
        var height = tile * 0.82f;
        var y = at.Y;
        for (var index = 0; index < deck.Count; index++)
        {
            var card = deck[index];
            var column = index % TilesPerRow;
            if (column == 0 && index > 0) y += height * 1.05f;

            var position = new Vector2(at.X + (column * tile), y);
            var name = ModelIdNames.Display(card.CardId);
            var text = card.Count > 1
                ? $"{name} ×{card.Count.ToString(CultureInfo.InvariantCulture)}"
                : name;

            if (ModelArt.Of(card.CardId) is { } portrait)
            {
                // Ignore intrinsic size before assigning art or Godot keeps the texture's minimum
                var art = new TextureRect
                {
                    Name = $"RunmobileCard{index.ToString(CultureInfo.InvariantCulture)}",
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    Texture = portrait,
                    Position = position,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(tile * 0.92f, height * 0.74f),
                    CustomMinimumSize = new Vector2(tile * 0.92f, height * 0.74f),
                    MouseFilter = Control.MouseFilterEnum.Stop,
                    ClipContents = true,
                    TooltipText = text,
                };
                content.AddChild(art);

                // The count under the tile, and only where there is more than one:
                // "×1" under every single-copy card would be a column of noise.
                if (card.Count > 1)
                {
                    LibraryScreen.AddLine(
                        content,
                        $"×{card.Count.ToString(CultureInfo.InvariantCulture)}",
                        position with { Y = position.Y + (height * 0.76f) },
                        tile * 0.92f,
                        LibraryPalette.Muted,
                        LineFontSize - 3,
                        HorizontalAlignment.Center);
                }
            }
            else
            {
                LibraryScreen.AddLine(
                    content, text, position, tile * 0.92f, LibraryPalette.Muted, LineFontSize - 3);
            }
        }

        return y + (height * 1.2f);
    }

    /// <summary>
    /// The flat plate under the pane, and the pane's own ribbon above it.
    ///
    /// Flat and hung under the pane rather than drawn as another modal: it is about the
    /// run the pane is showing, so it belongs to the pane. It is a fixed width - the
    /// pane's own control column - so a row keeps its size when its label changes
    /// state.
    /// </summary>
    private static Control? AddPlate(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width, float bottom)
    {
        var rows = new List<ScreenRow>();
        if (pane.Ribbon is { } ribbon) rows.Add(ribbon);
        rows.AddRange(pane.Plate);
        if (rows.Count == 0) return null;

        var step = content.NoButton.Size.Y * 1.12f;
        var top = Math.Min(at.Y, bottom - (step * rows.Count));
        var placed = new List<Control>();
        for (var index = 0; index < rows.Count; index++)
        {
            var control = LibraryScreen.AddRow(
                content,
                rows[index],
                $"RunmobilePlate{index.ToString(CultureInfo.InvariantCulture)}",
                new Vector2(at.X, top + (step * index)),
                width);
            if (control is not null) placed.Add(control);
        }

        // Joined into their own column, so a controller that has crossed to the pane can
        // walk it. Refused rows are skipped rather than stepped over: a control that
        // takes no focus is not a stop on the way down, and pointing a neighbour at one
        // would strand a player mid-column.
        var focusable = placed.Where(row => row.FocusMode != Control.FocusModeEnum.None).ToList();
        for (var index = 0; index < focusable.Count; index++)
        {
            focusable[index].FocusNeighborTop =
                (index > 0 ? focusable[index - 1] : focusable[index]).GetPath();
            focusable[index].FocusNeighborBottom =
                (index + 1 < focusable.Count ? focusable[index + 1] : focusable[index]).GetPath();
        }

        return focusable.FirstOrDefault();
    }
}
