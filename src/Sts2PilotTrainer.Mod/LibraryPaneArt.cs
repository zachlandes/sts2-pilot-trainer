using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
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
/// relic is the game's own icon, a card is the game's own portrait, and a floor's
/// marker is the run-history screen's own icon, through <see cref="ModelArt"/> and
/// <see cref="FloorMarkerArt"/>, so this mod ships no resource pack. A tick, a ring
/// and a crown are <see cref="LibraryGlyphArt"/>'s, because the game has no
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
    /// <summary>How many card tiles a row of the deck holds before it wraps.</summary>
    private const int TilesPerRow = 8;

    /// <summary>
    /// The narrowest a strip column may be: the run-history screen's own 60-unit floor
    /// entry. A marker narrower than the game draws it stops being the game's icon and
    /// becomes a speck - measured in the client at 46, where the played badge was a dot -
    /// so the strip pages sooner rather than draw one.
    /// </summary>
    private const float MinimumStripPitch = 60f;

    /// <summary>The icon box as a share of the column. The rest is the gap between
    /// neighbours, so two markers never touch.</summary>
    private const float IconShare = 0.78f;

    /// <summary>The icon box is capped against the numeral under it, so a short strip
    /// spread across a wide pane does not draw markers the size of relics.</summary>
    private const float IconCapRatio = 2.6f;

    /// <summary>The numeral's line, as a multiple of its size - the same line height
    /// every other line of the mod's text stands at.</summary>
    private const float NumeralLineRatio = 1.3f;

    /// <summary>Clear space between the icon box and the numeral, as a share of the
    /// box. The numeral is under the marker, never on it.</summary>
    private const float NumeralGapShare = 0.2f;

    /// <summary>The played badge's side as a share of the icon box, and how far past
    /// the box's corner it sits: the run-history entry's own quest badge hangs off the
    /// icon's top-right corner, and so does this.</summary>
    private const float BadgeShare = 0.62f;
    private const float BadgeOverhang = 0.4f;

    /// <summary>The selected ring stands off the icon box by this share of it.</summary>
    private const float RingStandoff = 0.1f;

    /// <summary>The icon inside its box, and its outline behind it, as shares of the
    /// box: the icon fills 0.8 of it and the outline 1.0, so the outline is a quarter
    /// larger than the icon. Raised from the history entry's own 0.7 in a 60 box
    /// because the strip's box is a share of a column and so smaller than 60.</summary>
    private const float IconInBox = 0.8f;
    private const float OutlineInBox = 1f;

    private const float StripBottomSpace = 1.25f;

    internal readonly record struct StripLayout(
        int First, int Count, bool HasPrevious, bool HasNext, int Index, int Pages,
        float Pitch, float Cell, float Height, float Offset)
    {
        internal int SlotOf(int index) => (HasPrevious ? 1 : 0) + index - First;

        internal int NextSlot => (HasPrevious ? 1 : 0) + Count;
    }

    /// <summary>
    /// Where each part of one strip cell is drawn, inside a box <c>Pitch</c> wide and
    /// <c>Height</c> tall.
    ///
    /// The numeral is below the icon box with clear space between, the played badge
    /// hangs off the box's top-right corner, and the selected ring stands off the box
    /// - so nothing is drawn over the marker, which is what made the tick vanish and the
    /// numeral collide with its ring before.
    /// </summary>
    internal readonly record struct StripCellGeometry(Rect2 Icon, Rect2 Numeral, Rect2 Badge, Rect2 Ring);

    internal static StripLayout LayoutStrip(
        int count, float width, int anchor, int lineSize, int? requestedPage = null)
    {
        var places = Math.Max(
            ScreenPage.MinimumPerPage,
            (int)Math.Floor(width / MinimumStripPitch));
        var page = requestedPage is { } requested
            ? ScreenPage.For(count, places, requested)
            : ScreenPage.Containing(count, places, anchor);
        var pitch = page.Pages == 1 ? width / page.Count : width / places;
        var cell = Math.Min(pitch * IconShare, lineSize * IconCapRatio);
        var geometry = CellGeometry(pitch, cell, lineSize);
        var height = geometry.Numeral.End.Y;
        var offset = (width - (page.Drawn * pitch)) / 2f;
        return new StripLayout(
            page.First, page.Count, page.HasPrevious, page.HasNext, page.Index,
            page.Pages, pitch, cell, height, offset);
    }

    internal static StripCellGeometry CellGeometry(StripLayout layout, int lineSize) =>
        CellGeometry(layout.Pitch, layout.Cell, lineSize);

    private static StripCellGeometry CellGeometry(float pitch, float cell, int lineSize)
    {
        var badge = cell * BadgeShare;
        // The box starts under the badge's overhang and the ring's standoff, whichever
        // reaches higher, so neither is clipped at the strip's top edge.
        var top = Math.Max(badge * BadgeOverhang, cell * RingStandoff);
        var icon = new Rect2((pitch - cell) / 2f, top, cell, cell);
        var numeral = new Rect2(
            0f, icon.End.Y + (cell * NumeralGapShare), pitch, lineSize * NumeralLineRatio);
        // Off the corner, but never into the next column: the overhang is clamped at
        // the column's edge so two neighbours' badges cannot meet
        var badgeBox = new Rect2(
            Math.Min(icon.End.X - (badge * (1f - BadgeOverhang)), pitch - badge),
            icon.Position.Y - (badge * BadgeOverhang),
            badge, badge);
        var standoff = cell * RingStandoff;
        var ring = new Rect2(
            icon.Position.X - standoff, icon.Position.Y - standoff,
            cell + (standoff * 2f), cell + (standoff * 2f));
        return new StripCellGeometry(icon, numeral, badgeBox, ring);
    }

    /// <summary>
    /// Draws the pane into the area it was given, and returns the first control a
    /// player can press there - which is where focus goes when the list has nothing
    /// pressable.
    /// </summary>
    internal static Control? Add(NVerticalPopup content, ScreenPane pane, Rect2 at)
    {
        // The native roles for each kind of line. The identity line is a heading over
        // the pane and not over the screen - the parchment's own title above it is the
        // screen's - so it uses a row title rather than the popup header, which would
        // put two titles on one parchment.
        var heading = GameText.Scene(NativeTextRole.RowTitle);
        var secondary = GameText.Scene(NativeTextRole.Secondary);
        var factStyle = GameText.Scene(NativeTextRole.Fact);
        var card = GameText.Scene(NativeTextRole.CardCaption);
        var floor = GameText.Scene(NativeTextRole.FloorNumeral);
        var y = at.Position.Y;
        y = LibraryScreen.AddLine(
            content, pane.Heading, new Vector2(at.Position.X, y), at.Size.X,
            LibraryPalette.Muted, heading);

        if (pane.Subtitle is { Length: > 0 } subtitle)
        {
            y = LibraryScreen.AddLine(
                content, subtitle, new Vector2(at.Position.X, y), at.Size.X,
                LibraryPalette.Muted, secondary);
        }

        // Relics and the deck count top right, which is where the accepted layout puts
        // them: they are what the run carried, read across the top rather than down the
        // pane.
        y = AddRelics(content, pane, new Vector2(at.Position.X, y), at.Size.X, card);
        if (pane.DeckCount is { } cards)
        {
            y = LibraryScreen.AddLine(
                content,
                LibraryCopy.DeckCount(cards),
                new Vector2(at.Position.X, y),
                at.Size.X,
                LibraryPalette.Muted,
                factStyle,
                HorizontalAlignment.Right);
        }

        var strip = AddStrip(content, pane, new Vector2(at.Position.X, y), at.Size.X, floor);
        y = strip.Bottom;
        // Keep the deck in the opened-run pane when browser actions need its room
        if (pane.Plate.Count == 0)
            y = AddDeck(content, pane, new Vector2(at.Position.X, y), at.Size.X, card);

        foreach (var fact in pane.Facts)
        {
            y = LibraryScreen.AddLine(
                content, fact, new Vector2(at.Position.X, y), at.Size.X,
                LibraryPalette.Muted, factStyle);
        }

        if (pane.Verdict is { Length: > 0 } verdict)
        {
            y = LibraryScreen.AddLine(
                content, verdict, new Vector2(at.Position.X, y), at.Size.X,
                pane.VerdictPassed ? LibraryPalette.Green : LibraryPalette.Red, factStyle);
        }

        var plateFocus = AddPlate(
            content, pane, new Vector2(at.Position.X, y), at.Size.X, at.End.Y);
        if (strip.Last is { } stripLast && plateFocus is not null)
        {
            stripLast.FocusNeighborBottom = plateFocus.GetPath();
            plateFocus.FocusNeighborTop = stripLast.GetPath();
        }

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
    private static float AddRelics(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width, GameTextStyle line)
    {
        if (pane.Relics.Count == 0) return at.Y;

        var size = line.Size * 1.7f;
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
                    LibraryPalette.Muted, line);
            }
        }

        return y + (size * 1.85f);
    }

    /// <summary>
    /// The run strip, full width: one readable page of the floors the run reached, in
    /// each floor's own glyph, with a tick over the ones this player has stood in and a
    /// ring round the selected one.
    ///
    /// A cell is pressable where the pane can move the selection - the run view's strip
    /// can, and the browser pane's cannot, because a pane's one way forward is its
    /// ribbon. A cell whose floor is not a place to stand is drawn and not pressable:
    /// the strip says what the run did, and where a player can be stood is the rows'
    /// answer.
    /// </summary>
    private static (float Bottom, Control? Focus, Control? Last) AddStrip(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width, GameTextStyle line)
    {
        if (pane.Strip.Count == 0) return (at.Y, null, null);

        var anchor = 0;
        for (var index = 0; index < pane.Strip.Count; index++)
        {
            if (pane.Strip[index].Selected)
            {
                anchor = index;
                break;
            }

            if (pane.Strip[index].Played) anchor = index;
        }

        var layout = LayoutStrip(pane.Strip.Count, width, anchor, line.Size, pane.StripPage);
        var controls = new List<Control>();
        if (layout.HasPrevious && pane.SelectStripPage is { } previousPage)
        {
            controls.Add(AddStripPageButton(
                content, LibraryCopy.PreviousPage, "Previous", true,
                new Vector2(at.X + layout.Offset, at.Y),
                layout.Pitch, layout.Height, NativePaginatorArt.RunHistoryTexture,
                () => LibraryScreen.Navigate(
                    LibraryCopy.PreviousPage, () => previousPage(layout.Index - 1))));
        }

        var geometry = CellGeometry(layout, line.Size);
        for (var index = layout.First; index < layout.First + layout.Count; index++)
        {
            var floor = pane.Strip[index];
            var x = at.X + layout.Offset + (layout.SlotOf(index) * layout.Pitch);
            var y = at.Y;
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

            AddMarker(box, floor, geometry.Icon);
            AddStripNumber(box, floor.Floor, geometry.Numeral, line);

            // The ring stands off the marker rather than round the whole cell, so it
            // reads as "this one" and not as a border on the numeral too.
            if (floor.Selected)
            {
                var ring = LibraryGlyphArt.Of(
                    LibraryGlyph.Selected, "Selected", geometry.Ring.Size.X, LibraryPalette.Ink);
                ring.Position = geometry.Ring.Position;
                box.AddChild(ring);
            }

            // Off the marker's corner, the way the history entry's own quest badge
            // hangs off its icon: a teal disc, because it is something this player did
            // and teal is that colour everywhere on this surface, with the tick in the
            // game's own cream on it. A dark disc vanished against the parchment.
            if (floor.Played)
            {
                var disc = LibraryGlyphArt.Of(
                    LibraryGlyph.Disc, "PlayedDisc", geometry.Badge.Size.X, LibraryPalette.Teal);
                disc.Position = geometry.Badge.Position;
                box.AddChild(disc);
                var tickSize = geometry.Badge.Size.X;
                var tick = LibraryGlyphArt.Of(LibraryGlyph.Played, "Played", tickSize, LibraryPalette.Cream);
                tick.Position = geometry.Badge.Position + ((geometry.Badge.Size - new Vector2(tickSize, tickSize)) / 2f);
                box.AddChild(tick);
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

        if (layout.HasNext && pane.SelectStripPage is { } nextPage)
        {
            controls.Add(AddStripPageButton(
                content, LibraryCopy.NextPage, "Next", false,
                new Vector2(
                    at.X + layout.Offset + (layout.NextSlot * layout.Pitch), at.Y),
                layout.Pitch, layout.Height, NativePaginatorArt.RunHistoryTexture,
                () => LibraryScreen.Navigate(
                    LibraryCopy.NextPage, () => nextPage(layout.Index + 1))));
        }

        for (var index = 0; index < controls.Count; index++)
        {
            controls[index].FocusNeighborLeft =
                controls[index > 0 ? index - 1 : index].GetPath();
            controls[index].FocusNeighborRight =
                controls[index + 1 < controls.Count ? index + 1 : index].GetPath();
            controls[index].FocusNeighborTop = controls[index].GetPath();
            controls[index].FocusNeighborBottom = controls[index].GetPath();
        }

        return (
            at.Y + (layout.Height * StripBottomSpace),
            controls.FirstOrDefault(),
            controls.LastOrDefault());
    }

    /// <summary>
    /// The floor's marker: the game's own run-history icon with its outline behind it,
    /// or the mod's hollow ring where the kind is not established or the build has no
    /// icon for it. Dimmed where the floor is not a place to stand.
    /// </summary>
    private static void AddMarker(Control box, RunStripCell floor, Rect2 icon)
    {
        var alpha = floor.Playable ? 1f : 0.45f;
        if (FloorMarkerArt.Of(floor.Kind) is { } art)
        {
            var side = icon.Size.X * IconInBox;
            var outlineSide = icon.Size.X * OutlineInBox;
            var centre = icon.Position + (icon.Size / 2f);
            if (art.Outline is { } outline)
            {
                // Ignore intrinsic size before assigning art or Godot keeps the texture's minimum
                box.AddChild(new TextureRect
                {
                    Name = "Outline",
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Texture = outline,
                    Position = centre - new Vector2(outlineSide, outlineSide) / 2f,
                    Size = new Vector2(outlineSide, outlineSide),
                    // The history entry's own quarter of black behind its icon
                    Modulate = new Color(0f, 0f, 0f, 0.25f * alpha),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }

            box.AddChild(new TextureRect
            {
                Name = "Kind",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Texture = art.Icon,
                Position = centre - new Vector2(side, side) / 2f,
                Size = new Vector2(side, side),
                Modulate = new Color(1f, 1f, 1f, alpha),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            return;
        }

        var glyph = LibraryGlyphArt.Of(
            LibraryGlyphArt.For(floor.Kind), "Kind", icon.Size.X,
            LibraryPalette.Line with { A = alpha });
        glyph.Position = icon.Position;
        box.AddChild(glyph);
    }

    /// <summary>The floor's number under its marker, in the native floor-numeral style.</summary>
    private static void AddStripNumber(Control box, int floor, Rect2 at, GameTextStyle style)
    {
        var label = new Label
        {
            Name = $"{box.Name}Floor",
            Text = floor.ToString(CultureInfo.InvariantCulture),
            Position = at.Position,
            Size = at.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        style.ApplyTo(label);
        label.AddThemeColorOverride("font_color", LibraryPalette.Muted);
        box.AddChild(label);
    }

    internal static Button AddStripPageButton(
        Control content, string tooltip, string direction, bool previous, Vector2 at,
        float width, float height, Func<bool, Texture2D?> image, Action press)
    {
        return NativePaginatorArt.AddButton(
            content,
            $"RunmobileStrip{direction}",
            tooltip,
            previous,
            new Rect2(at, new Vector2(width, height)),
            image,
            press);
    }

    /// <summary>
    /// The deck at this position's start, as card tiles.
    ///
    /// The game's own portrait per card with its count beside it, wrapped into rows. A
    /// deck the recording says nothing about draws nothing at all rather than an empty
    /// frame, because "no deck was recorded here" and "the deck was empty" are different
    /// facts and a frame would state the second.
    /// </summary>
    private static float AddDeck(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width, GameTextStyle line)
    {
        if (pane.Deck is not { Count: > 0 } deck) return at.Y;

        var tile = width / TilesPerRow;
        var height = tile * 0.82f;
        var pitch = DeckRowPitch(tile, line.Size);
        var y = at.Y;
        for (var index = 0; index < deck.Count; index++)
        {
            var card = deck[index];
            var column = index % TilesPerRow;
            if (column == 0 && index > 0) y += pitch;

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
                        line,
                        HorizontalAlignment.Center);
                }
            }
            else
            {
                LibraryScreen.AddLine(
                    content, text, position, tile * 0.92f, LibraryPalette.Muted, line);
            }
        }

        return y + pitch;
    }

    internal static float DeckRowPitch(float tile, int captionSize)
    {
        var tileHeight = tile * 0.82f;
        var portraitPitch = tileHeight * 1.05f;
        var captionBottom = (tileHeight * 0.76f) + (captionSize * 1.3f);
        return Math.Max(portraitPitch, captionBottom);
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
