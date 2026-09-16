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
    private const float MinimumStripPitch = FloorMarkerArt.EntryBox;

    /// <summary>The icon box as a share of the column. The rest is the gap between
    /// neighbours, so two markers never touch.</summary>
    private const float IconShare = 0.78f;

    /// <summary>
    /// The icon box at which the marker inside it is the run-history screen's own size:
    /// the history entry draws its icon at 44.8 units and the box draws its icon at
    /// <see cref="IconInBox"/> of itself. A short strip across a wide pane is capped
    /// here, so it draws the game's markers at the game's size rather than markers the
    /// size of relics; the cap used to be a multiple of the numeral's font size, which
    /// tied a picture's size to a font's and drew a marker half again the game's.
    /// </summary>
    internal const float NativeCell = FloorMarkerArt.EntryIconSide / IconInBox;

    /// <summary>
    /// The smallest icon box the strip will draw when the pane is short of room:
    /// seven tenths of the history entry's own marker, which keeps the played badge
    /// above the size at which it read as a dot. Under that the marker is the speck the
    /// 60-unit column exists to prevent, and a pane that cannot give the strip even
    /// this refuses to lay out rather than drawing the strip over the plate.
    /// </summary>
    internal const float MinimumCell = NativeCell * 0.7f;

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

    /// <summary>The room a strip takes, as a multiple of its cell's height: the air
    /// under the numerals before the next line.</summary>
    internal const float StripBottomSpace = 1.12f;

    /// <summary>The air under the relic rows before the strip, as a share of a relic's
    /// box. The rows themselves are paced at the box with nothing between, which is
    /// how the run-history screen's own flow lays its holders.</summary>
    private const float RelicRowSpace = 0.3f;

    /// <summary>Clear space between the last relic on the first row and the deck
    /// count that shares its line, as a share of a relic's box.</summary>
    internal const float CountGapShare = 0.25f;

    internal readonly record struct StripLayout(
        int First, int Count, bool HasPrevious, bool HasNext, int Index, int Pages,
        float Pitch, float Cell, float Height, float Offset)
    {
        internal int SlotOf(int index) => (HasPrevious ? 1 : 0) + index - First;

        internal int NextSlot => (HasPrevious ? 1 : 0) + Count;

        /// <summary>The room the strip takes on the pane, air included.</summary>
        internal float Room => Height * StripBottomSpace;
    }

    /// <summary>
    /// Where the pane's parts go, top to bottom, inside a pane <c>Height</c> tall.
    ///
    /// The one place the pane is measured against the room it was given. Before this,
    /// the strip was placed under the relics at its own size, the facts under the
    /// strip, and the plate wherever the bottom left it - and on a pane too short for
    /// all three the plate was drawn upward over the facts and the strip, which is
    /// what a player saw as "Open the run" across the floor numerals and the version
    /// line under "Share this run". The plate keeps the bottom, because it is the
    /// pane's controls and they sit by the panel's own ribbon. Two parts give, in
    /// order: the relic rows page, down to one row, with the game's own arrows in the
    /// first and last place of the page the way the strip pages; and then the strip,
    /// down to <see cref="MinimumCell"/>. The relics give first because the strip is
    /// what the pane is for and its marker is the game's own size, and a row of
    /// relics behind an arrow is still every relic at that size. A pane with no room
    /// for one relic row beside the smallest strip refuses by name rather than
    /// overlapping. The numbers are measured, so a build that changes a font moves
    /// the layout rather than the collision.
    /// </summary>
    /// <param name="RelicsTop">Where the relic block starts, under the identity.</param>
    /// <param name="Relics">The relic rows across the pane, with the deck count on the
    /// first row's line.</param>
    /// <param name="RelicPage">Which of the relics this pane shows: one page where the
    /// rows all fit, else a page of <see cref="RelicRows"/> rows with a place spent on
    /// each arrow it offers.</param>
    /// <param name="RelicRows">How many rows of relics are drawn.</param>
    /// <param name="RelicsWindow">The height the relic block takes: the drawn rows,
    /// the count's own line where it has one, and the air under them.</param>
    /// <param name="StripTop">Where the strip starts, under the relics.</param>
    /// <param name="Strip">The strip at whatever cell the room allows, or null for a
    /// run with no floors.</param>
    /// <param name="AfterStrip">Where the deck, the facts and the verdict start.</param>
    /// <param name="PlateTop">Where the plate's ribbons sit, or null for a pane with no
    /// plate. Never above where the facts end.</param>
    internal readonly record struct PaneLayout(
        float RelicsTop, RelicBlock Relics, ScreenPage RelicPage, int RelicRows, float RelicsWindow,
        float StripTop, StripLayout? Strip, float AfterStrip, float? PlateTop)
    {
        internal float StripRoom => Strip?.Room ?? 0f;

        /// <summary>The place a relic takes on its page: after the Previous arrow
        /// where the page has one.</summary>
        internal int SlotOf(int index) => (RelicPage.HasPrevious ? 1 : 0) + index - RelicPage.First;

        internal int NextSlot => (RelicPage.HasPrevious ? 1 : 0) + RelicPage.Count;
    }

    /// <summary>
    /// Lays the pane out; see <see cref="PaneLayout"/>.
    /// </summary>
    /// <param name="above">The height of everything over the relics: the heading and
    /// a subtitle.</param>
    /// <param name="relics">The relic rows, laid across the pane's width.</param>
    /// <param name="box">A relic's box, the run-history holder's own.</param>
    /// <param name="below">The height of everything between the strip and the plate:
    /// the facts and the verdict.</param>
    /// <param name="bounded">Whether the pane is held to its height at all. The
    /// browser's pane is; the opened run's pane flows, its deck deciding how far, and
    /// nothing in it is asked to give.</param>
    /// <param name="plateRows">How many ribbons the plate holds side by side.</param>
    /// <param name="ribbon">A ribbon's size, measured off the panel's own.</param>
    /// <param name="height">The pane's height.</param>
    internal static PaneLayout Lay(
        float above, RelicBlock relics, float box, float below, int stripCount, float width,
        int anchor, int numeralSize, int? stripPage, int? relicPage, bool bounded, int plateRows,
        Vector2 ribbon, float height)
    {
        var plate = PlateHeight(plateRows, ribbon.Y);
        PlateColumns(plateRows, width, ribbon.X);
        var room = bounded ? height - above - below - plate : (float?)null;
        var oneRow = RelicRowsHeight(relics, Math.Min(1, relics.Rows), box);
        StripLayout? strip = null;
        if (stripCount > 0)
        {
            strip = LayoutStrip(stripCount, width, anchor, numeralSize, stripPage);
            if (room is { } held && strip.Value.Room + oneRow > held)
            {
                strip = LayoutStrip(
                    stripCount, width, anchor, numeralSize, stripPage,
                    Math.Max(held - oneRow, StripRoom(MinimumCell, numeralSize)));
            }
        }

        var relicRoom = room is { } bound ? bound - (strip?.Room ?? 0f) : (float?)null;
        if (relicRoom < oneRow - 0.01f)
        {
            throw new InvalidOperationException(
                $"This pane leaves {relicRoom!.Value.ToString("0", CultureInfo.InvariantCulture)} units for " +
                "its relics after its lines, its plate and the smallest run strip, which is short of one " +
                "row, and refuses rather than overlapping.");
        }

        var rows = relics.Rows == 0 || relicRoom is null
            ? relics.Rows
            : Math.Clamp(
                (int)Math.Floor((relicRoom.Value - relics.CountLine - (box * RelicRowSpace)) / box),
                1, relics.Rows);
        var page = rows == relics.Rows
            ? ScreenPage.For(relics.Count, relics.Count, 0)
            : ScreenPage.For(relics.Count, relics.PlacesIn(rows), relicPage ?? 0);
        var window = RelicRowsHeight(relics, rows, box);
        var stripTop = above + window;
        return new PaneLayout(
            above, relics, page, rows, window, stripTop, strip, stripTop + (strip?.Room ?? 0f),
            plateRows == 0 ? null : height - plate);
    }

    /// <summary>The plate's height: one row of ribbons side by side.</summary>
    internal static float PlateHeight(int rows, float ribbon) => rows == 0 ? 0f : ribbon;

    /// <summary>
    /// Where each of the plate's ribbons starts across the pane, at the ribbon's own
    /// width: the first at the pane's left edge, the last at its right, the rest
    /// spread evenly between. A pane too narrow for them side by side refuses, because
    /// two ribbons drawn over each other is two controls under one press.
    /// </summary>
    internal static IReadOnlyList<float> PlateColumns(int rows, float width, float ribbon)
    {
        if (rows == 0) return [];
        if (rows == 1) return [0f];

        var gap = (width - (rows * ribbon)) / (rows - 1);
        if (gap < 0f)
        {
            throw new InvalidOperationException(
                $"This pane is {width.ToString("0", CultureInfo.InvariantCulture)} wide and its plate needs " +
                $"{(rows * ribbon).ToString("0", CultureInfo.InvariantCulture)} for {rows} ribbons side by side.");
        }

        return Enumerable.Range(0, rows).Select(index => index * (ribbon + gap)).ToList();
    }

    /// <summary>The height this many relic rows take, the count's own line and the
    /// air under the last row included.</summary>
    internal static float RelicRowsHeight(RelicBlock relics, int rows, float box) =>
        relics.CountLine + (rows * box) + (rows == 0 ? 0f : box * RelicRowSpace);

    /// <summary>
    /// The relic rows and the deck count on the first one's line.
    ///
    /// The rows are the run-history screen's own flow: one relic per box across the
    /// width, no separation, wrapped to the next row at the width. The count is
    /// right-aligned on the first row's line, and that row holds only as many relics
    /// as leave the count's own measured width clear - it used to fill the row edge to
    /// edge and the count was drawn over its last icons. A count wider than the row
    /// can spare beside even one relic takes a line of its own over the rows instead.
    /// </summary>
    /// <param name="Count">How many relics the run carries.</param>
    /// <param name="PerRow">Places per row from the second row on.</param>
    /// <param name="FirstRow">Places on the first row, beside the count.</param>
    /// <param name="Rows">Rows the relics take; zero for none.</param>
    /// <param name="CountLine">The height of the count's own line over the rows, or
    /// zero where it shares the first row or there is no count.</param>
    internal readonly record struct RelicBlock(
        int Count, int PerRow, int FirstRow, int Rows, float CountLine)
    {
        /// <summary>The row a place is on, counting places across the rows.</summary>
        internal int RowOf(int slot) => slot < FirstRow ? 0 : 1 + ((slot - FirstRow) / PerRow);

        internal int ColumnOf(int slot) => slot < FirstRow ? slot : (slot - FirstRow) % PerRow;

        /// <summary>How many places this many rows hold.</summary>
        internal int PlacesIn(int rows) => rows == 0 ? 0 : FirstRow + ((rows - 1) * PerRow);
    }

    /// <param name="countWidth">The count's measured width, or null for no count.</param>
    /// <param name="countLine">The height the count takes on a line of its own.</param>
    internal static RelicBlock LayoutRelics(
        int relics, float width, float box, float? countWidth, float countLine)
    {
        var perRow = Math.Max(1, (int)Math.Floor(width / box));
        if (relics == 0)
        {
            return new RelicBlock(0, perRow, perRow, 0, countWidth is null ? 0f : countLine);
        }

        var firstRow = countWidth is { } reserved
            ? Math.Clamp((int)Math.Floor((width - reserved - (box * CountGapShare)) / box), 0, perRow)
            : perRow;
        var ownLine = countWidth is not null && firstRow == 0;
        if (ownLine) firstRow = perRow;
        var rest = Math.Max(0, relics - firstRow);
        var rows = 1 + ((rest + perRow - 1) / perRow);
        return new RelicBlock(relics, perRow, firstRow, rows, ownLine ? countLine : 0f);
    }

    /// <summary>
    /// Where each part of one strip cell is drawn, inside a box <c>Pitch</c> wide and
    /// <c>Height</c> tall.
    ///
    /// The numeral is below the icon box with clear space between, the played badge
    /// hangs off the box's top-right corner, the bookmark tab off its top-left, and the
    /// selected ring stands off the box - so nothing is drawn over the marker, which is
    /// what made the tick vanish and the numeral collide with its ring before, and a
    /// floor that is both played and bookmarked shows both with clear air between.
    /// </summary>
    internal readonly record struct StripCellGeometry(
        Rect2 Icon, Rect2 Numeral, Rect2 Badge, Rect2 Ring, Rect2 Mark);

    /// <summary>
    /// The strip's page and cell for this many floors across this width.
    /// </summary>
    /// <param name="room">The height the pane can give the strip, air included, or
    /// null where nothing bounds it. A cell is shrunk to fit the room down to
    /// <see cref="MinimumCell"/>; a room shorter than that refuses.</param>
    internal static StripLayout LayoutStrip(
        int count, float width, int anchor, int lineSize, int? requestedPage = null,
        float? room = null)
    {
        var places = Math.Max(
            ScreenPage.MinimumPerPage,
            (int)Math.Floor(width / MinimumStripPitch));
        var page = requestedPage is { } requested
            ? ScreenPage.For(count, places, requested)
            : ScreenPage.Containing(count, places, anchor);
        var pitch = page.Pages == 1 ? width / page.Count : width / places;
        var cell = Math.Min(pitch * IconShare, NativeCell);
        if (room is { } bounded)
        {
            var fits = CellFitting(bounded, lineSize);
            if (fits < MinimumCell - 0.01f)
            {
                throw new InvalidOperationException(
                    "This pane leaves the run strip " +
                    $"{bounded.ToString("0", CultureInfo.InvariantCulture)} units of room, " +
                    "which is short of the smallest marker it draws; the strip is refused " +
                    "rather than drawn over the plate.");
            }

            cell = Math.Min(cell, Math.Max(fits, MinimumCell));
        }

        var geometry = CellGeometry(pitch, cell, lineSize);
        var height = geometry.Numeral.End.Y;
        var offset = (width - (page.Drawn * pitch)) / 2f;
        return new StripLayout(
            page.First, page.Count, page.HasPrevious, page.HasNext, page.Index,
            page.Pages, pitch, cell, height, offset);
    }

    internal static StripCellGeometry CellGeometry(StripLayout layout, int lineSize) =>
        CellGeometry(layout.Pitch, layout.Cell, lineSize);

    /// <summary>
    /// The largest icon box whose cell, air included, fits in this much room: the
    /// inverse of <see cref="CellGeometry(float, float, int)"/>'s height, which is the
    /// box, what hangs over its top, the gap under it and the numeral's line.
    /// </summary>
    internal static float CellFitting(float room, int lineSize)
    {
        var overhang = Math.Max(BadgeShare * BadgeOverhang, RingStandoff);
        var perCell = overhang + 1f + NumeralGapShare;
        var numeral = lineSize * NumeralLineRatio;
        return ((room / StripBottomSpace) - numeral) / perCell;
    }

    /// <summary>The room a strip at this cell takes, air included: what
    /// <see cref="CellFitting"/> inverts.</summary>
    internal static float StripRoom(float cell, int lineSize) =>
        CellGeometry(cell, cell, lineSize).Numeral.End.Y * StripBottomSpace;

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
        // The mirror of the badge, off the other corner and clamped at the column's
        // other edge for the same reason
        var mark = new Rect2(
            Math.Max(icon.Position.X - (badge * BadgeOverhang), 0f),
            icon.Position.Y - (badge * BadgeOverhang),
            badge, badge);
        return new StripCellGeometry(icon, numeral, badgeBox, ring, mark);
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
        // pane. The count shares the first relic row's line; it used to take a line of
        // its own under the relics, which was a line the strip and the plate then fought
        // over. The rows are measured here and drawn once the pane has said what window
        // it can give them.
        var holder = RelicHolderArt.Metrics();
        var count = pane.DeckCount is { } cards ? LibraryCopy.DeckCount(cards) : null;
        var relics = RelicBlockFor(pane, at.Size.X, holder, count, factStyle);

        // Everything between the strip and the plate is measured before the strip is
        // placed, so the strip is sized to the room those leave rather than the room it
        // would like. A fact that wraps is measured wrapped.
        var lines = new List<(string Text, Color Colour)>();
        lines.AddRange(pane.Facts.Select(fact => (fact, LibraryPalette.Muted)));
        if (pane.Verdict is { Length: > 0 } verdict)
        {
            lines.Add((verdict, pane.VerdictPassed ? LibraryPalette.Green : LibraryPalette.Red));
        }

        var below = lines.Sum(line => LibraryScreen.LineHeight(line.Text, at.Size.X, factStyle));
        // The opened run's pane has neither a plate nor a way forward of its own: it
        // flows, and its deck is what decides how far
        var flows = pane.Plate.Count == 0 && pane.Ribbon is null;
        var layout = Lay(
            y - at.Position.Y, relics, holder.Box, below, pane.Strip.Count, at.Size.X,
            StripAnchor(pane), floor.Size, pane.StripPage, pane.RelicPage, !flows, pane.Plate.Count,
            content.YesButton.Size, at.Size.Y);

        var relicControls = AddRelics(
            content, pane, new Vector2(at.Position.X, at.Position.Y + layout.RelicsTop), at.Size.X,
            layout, holder, count, card, factStyle);
        y = at.Position.Y + layout.StripTop;
        var strip = AddStrip(content, pane, new Vector2(at.Position.X, y), at.Size.X, floor, layout.Strip);
        y = at.Position.Y + layout.AfterStrip;
        if (flows && pane.Deck is { Count: > 0 })
        {
            y = AddDeck(content, pane, new Vector2(at.Position.X, y), at.Size.X, card);
        }

        foreach (var (text, colour) in lines)
        {
            y = LibraryScreen.AddLine(
                content, text, new Vector2(at.Position.X, y), at.Size.X, colour, factStyle);
        }

        var plateFocus = layout.PlateTop is { } plateTop
            ? AddPlate(content, pane, new Vector2(at.Position.X, at.Position.Y + plateTop), at.Size.X)
            : null;

        // One column to walk down: the relic arrows, the strip, the plate
        var stops = new List<Control>();
        if (relicControls.Last is { } relicLast) stops.Add(relicLast);
        if (strip.Focus is { } stripFirst) stops.Add(stripFirst);
        if (strip.Last is { } stripLast && stripLast != strip.Focus) stops.Add(stripLast);
        if (plateFocus is not null) stops.Add(plateFocus);
        for (var index = 0; index + 1 < stops.Count; index++)
        {
            stops[index].FocusNeighborBottom = stops[index + 1].GetPath();
            stops[index + 1].FocusNeighborTop = stops[index].GetPath();
        }

        return relicControls.Focus ?? strip.Focus ?? plateFocus;
    }

    /// <summary>The relic block across this width, with the count's measured width
    /// kept clear on the first row.</summary>
    private static RelicBlock RelicBlockFor(
        ScreenPane pane, float width, RelicHolderMetrics holder, string? count, GameTextStyle fact)
    {
        float? countWidth = count is null ? null : fact.Width(count, fact.Size * 0.6f * count.Length);
        return LayoutRelics(
            pane.Relics.Count, width, holder.Box, countWidth,
            count is null ? 0f : LibraryScreen.LineHeight(count, width, fact));
    }

    /// <summary>
    /// The relic block: the game's own icons at the run-history holder's own size, in
    /// the order the run found them, laid across the top of the pane in rows, with the
    /// deck count on the first row's line.
    ///
    /// The pane carries every relic rather than the row's handful, which is why there
    /// is no hover here: each icon is the game's own art and the row above is where the
    /// scanning happens. A relic this build has no icon for is written by name instead,
    /// so the pane never has a hole where a relic was. Where the pane holds fewer rows
    /// than the run has, the rows page the way the strip pages - the game's own
    /// run-history arrows in the first and last place of the page - rather than
    /// scrolling or drawing over the strip; the count stays on the first row's line
    /// whichever page is up, because it is about the deck and not about a page.
    /// </summary>
    private static (Control? Focus, Control? Last) AddRelics(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width, PaneLayout layout,
        RelicHolderMetrics holder, string? count, GameTextStyle line, GameTextStyle fact)
    {
        var block = layout.Relics;
        var y = at.Y;
        if (count is { } cards)
        {
            // On the first relic row's line, right-aligned and centred on the icons;
            // on a line of its own only where the row cannot spare the width, or
            // there are no relics to share one with
            var lineHeight = fact.Size * LibraryScreen.LabelLineRatio;
            var ownLine = block.Rows == 0 || block.CountLine > 0f;
            var countY = ownLine ? y : y + ((holder.Box - lineHeight) / 2f);
            var next = LibraryScreen.AddLine(
                content, cards, new Vector2(at.X, countY), width, LibraryPalette.Muted, fact,
                HorizontalAlignment.Right);
            if (ownLine) y = next;
        }

        var page = layout.RelicPage;
        Vector2 PlaceOf(int slot) => new(
            at.X + (block.ColumnOf(slot) * holder.Box), y + (block.RowOf(slot) * holder.Box));
        var controls = new List<Control>();
        if (page.HasPrevious && pane.SelectRelicPage is { } previousPage)
        {
            controls.Add(AddStripPageButton(
                content, LibraryCopy.PreviousPage, "RelicsPrevious", true, PlaceOf(0),
                holder.Box, holder.Box, NativePaginatorArt.RunHistoryTexture,
                () => LibraryScreen.Navigate(
                    LibraryCopy.PreviousPage, () => previousPage(page.Index - 1))));
        }

        for (var index = page.First; index < page.First + page.Count; index++)
        {
            var id = pane.Relics[index];
            var position = PlaceOf(layout.SlotOf(index));
            if (ModelArt.Of(id) is { } icon)
            {
                var inset = (holder.Box - holder.Icon) / 2f;
                // Ignore intrinsic size before assigning art or Godot keeps the texture's minimum
                var art = new TextureRect
                {
                    Name = $"RunmobileRelic{index.ToString(CultureInfo.InvariantCulture)}",
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    Texture = icon,
                    Position = position + new Vector2(inset, inset),
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(holder.Icon, holder.Icon),
                    CustomMinimumSize = new Vector2(holder.Icon, holder.Icon),
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
                    content, ModelIdNames.Display(id), position, holder.Box,
                    LibraryPalette.Muted, line);
            }
        }

        if (page.HasNext && pane.SelectRelicPage is { } nextPage)
        {
            controls.Add(AddStripPageButton(
                content, LibraryCopy.NextPage, "RelicsNext", false, PlaceOf(layout.NextSlot),
                holder.Box, holder.Box, NativePaginatorArt.RunHistoryTexture,
                () => LibraryScreen.Navigate(
                    LibraryCopy.NextPage, () => nextPage(page.Index + 1))));
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

        return (controls.FirstOrDefault(), controls.LastOrDefault());
    }

    /// <summary>The floor the strip opens on: the selected one, else the last played.</summary>
    private static int StripAnchor(ScreenPane pane)
    {
        var anchor = 0;
        for (var index = 0; index < pane.Strip.Count; index++)
        {
            if (pane.Strip[index].Selected) return index;
            if (pane.Strip[index].Played) anchor = index;
        }

        return anchor;
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
    private static (Control? Focus, Control? Last) AddStrip(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width, GameTextStyle line,
        StripLayout? laidOut)
    {
        if (pane.Strip.Count == 0 || laidOut is not { } layout) return (null, null);

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
                TooltipText = LibraryCopy.FloorLine(floor.Floor, floor.Kind, floor.Bookmarked),
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

            // Off the other corner: the recording's own player marked this fight. Gold
            // rather than teal because it is about the run and not about this player,
            // and drawn in every strip for the same reason. The ink hairline round it is
            // what holds it on the parchment.
            if (floor.Bookmarked)
            {
                var tab = LibraryGlyphArt.Of(
                    LibraryGlyph.Bookmark, "Bookmark", geometry.Mark.Size.X, LibraryPalette.Gold);
                tab.Position = geometry.Mark.Position;
                box.AddChild(tab);
                var hairline = LibraryGlyphArt.Of(
                    LibraryGlyph.BookmarkOutline, "BookmarkOutline", geometry.Mark.Size.X, LibraryPalette.Ink);
                hairline.Position = geometry.Mark.Position;
                box.AddChild(hairline);
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

        return (controls.FirstOrDefault(), controls.LastOrDefault());
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
    /// The flat plate under the pane: its ribbons side by side at the panel's own
    /// ribbon size.
    ///
    /// Flat and hung under the pane rather than drawn as another modal: it is about the
    /// run the pane is showing, so it belongs to the pane. Each ribbon is the panel's
    /// primary ribbon at its own width - not the cancel ribbon, whose art is the red
    /// of a refusal, and not widened, so its art is the game's untouched. Where it
    /// sits is <see cref="Lay"/>'s answer; this draws it there.
    /// </summary>
    private static Control? AddPlate(
        NVerticalPopup content, ScreenPane pane, Vector2 at, float width)
    {
        if (pane.Plate.Count == 0) return null;

        var ribbon = content.YesButton.Size;
        var columns = PlateColumns(pane.Plate.Count, width, ribbon.X);
        var placed = new List<Control>();
        for (var index = 0; index < pane.Plate.Count; index++)
        {
            var control = LibraryScreen.AddRow(
                content,
                pane.Plate[index],
                $"RunmobilePlate{index.ToString(CultureInfo.InvariantCulture)}",
                new Vector2(at.X + columns[index], at.Y),
                ribbon.X,
                content.YesButton);
            if (control is not null) placed.Add(control);
        }

        // Joined into their own row, so a controller that has crossed to the pane can
        // walk it. Refused ribbons are skipped rather than stepped over: a control that
        // takes no focus is not a stop on the way across, and pointing a neighbour at
        // one would strand a player mid-row.
        var focusable = placed.Where(row => row.FocusMode != Control.FocusModeEnum.None).ToList();
        for (var index = 0; index < focusable.Count; index++)
        {
            focusable[index].FocusNeighborLeft =
                (index > 0 ? focusable[index - 1] : focusable[index]).GetPath();
            focusable[index].FocusNeighborRight =
                (index + 1 < focusable.Count ? focusable[index + 1] : focusable[index]).GetPath();
        }

        return focusable.FirstOrDefault();
    }
}
