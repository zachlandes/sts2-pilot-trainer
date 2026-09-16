using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class LibraryPaneArtTests
{
    /// <summary>The pane's own line size in a process with no game, which is what the
    /// strip's cells are measured against. The paging assertions below were taken at
    /// this size.</summary>
    private const int LineSize = 16;

    [Fact]
    public void FullRunStripPagesIntoReadableWindows()
    {
        const int floors = 50;
        const float width = 500f;

        var layout = LibraryPaneArt.LayoutStrip(floors, width, anchor: 0, LineSize);

        // Six to a page at the history entry's own 60-unit box, not fifteen specks
        Assert.Equal(6, layout.Count);
        Assert.Equal(9, layout.Pages);
        Assert.False(layout.HasPrevious);
        Assert.True(layout.HasNext);
        Assert.True(layout.Pitch >= 60f);
        Assert.True(layout.Cell >= 32f);
        Assert.Equal(6, layout.NextSlot);
    }

    [Fact]
    public void FullRunStripOpensOnTheAnchoredPage()
    {
        var layout = LibraryPaneArt.LayoutStrip(50, 500f, anchor: 49, LineSize);

        Assert.Equal(8, layout.Index);
        Assert.Equal(48, layout.First);
        Assert.Equal(2, layout.Count);
        Assert.True(layout.HasPrevious);
        Assert.False(layout.HasNext);
        Assert.Equal(1, layout.SlotOf(48));
    }

    [Fact]
    public void FullRunStripCanMoveToAnAdjacentPage()
    {
        var layout = LibraryPaneArt.LayoutStrip(50, 500f, anchor: 49, LineSize, requestedPage: 1);

        Assert.Equal(1, layout.Index);
        Assert.Equal(6, layout.First);
        Assert.Equal(6, layout.Count);
        Assert.True(layout.HasPrevious);
        Assert.True(layout.HasNext);
        Assert.Equal(7, layout.NextSlot);
    }

    /// <summary>
    /// The floor-2 tick was swallowed by the marker it was drawn over and the floor-1
    /// ring collided with its numeral. Every part of a cell now has its own place: the
    /// numeral under the icon box with clear space, the played badge off the box's
    /// top-right corner, the ring standing off the box - and none of it outside the
    /// cell, whatever the strip is scaled to.
    /// </summary>
    [Theory]
    [InlineData(5, 500f, 16)]
    [InlineData(50, 500f, 16)]
    [InlineData(14, 480f, 24)]
    [InlineData(3, 900f, 30)]
    public void StripCellPartsDoNotOverlapAndStayInsideTheCell(int floors, float width, int lineSize)
    {
        var layout = LibraryPaneArt.LayoutStrip(floors, width, anchor: 0, lineSize);
        var cell = LibraryPaneArt.CellGeometry(layout, lineSize);

        Assert.True(cell.Numeral.Position.Y >= cell.Icon.End.Y, "the numeral sits under the marker");
        Assert.True(cell.Numeral.Position.Y >= cell.Ring.End.Y, "the ring stays clear of the numeral");
        Assert.True(cell.Numeral.Size.Y >= lineSize, "the numeral has its own line");

        var iconCentre = cell.Icon.Position + (cell.Icon.Size / 2f);
        Assert.False(cell.Badge.HasPoint(iconCentre), "the badge hangs off the corner rather than over the marker");
        Assert.True(cell.Badge.End.X > cell.Icon.End.X && cell.Badge.Position.Y < cell.Icon.Position.Y,
            "the badge is at the marker's top-right corner");
        Assert.True(cell.Badge.Size.X >= cell.Icon.Size.X * 0.4f, "the badge is big enough to read");

        Assert.True(cell.Ring.Position.X <= cell.Icon.Position.X && cell.Ring.End.X >= cell.Icon.End.X);
        Assert.True(cell.Ring.Position.Y <= cell.Icon.Position.Y && cell.Ring.End.Y >= cell.Icon.End.Y);

        // The bookmark tab mirrors the badge on the other corner, and the two never
        // meet: a floor that is both played and bookmarked shows both with air between
        Assert.False(cell.Mark.HasPoint(iconCentre), "the tab hangs off the corner rather than over the marker");
        Assert.True(cell.Mark.Position.X < cell.Icon.Position.X && cell.Mark.Position.Y < cell.Icon.Position.Y,
            "the tab is at the marker's top-left corner");
        Assert.Equal(cell.Badge.Size, cell.Mark.Size);
        Assert.True(cell.Mark.End.X <= cell.Badge.Position.X, "the tab and the badge do not overlap");
        Assert.True(cell.Numeral.Position.Y >= cell.Mark.End.Y, "the tab stays clear of the numeral");

        foreach (var (name, part) in new[] { ("icon", cell.Icon), ("numeral", cell.Numeral), ("badge", cell.Badge), ("ring", cell.Ring), ("tab", cell.Mark) })
        {
            Assert.True(part.Position.Y >= 0f, $"the {name} is not clipped at the top");
            Assert.True(part.End.Y <= layout.Height + 0.01f, $"the {name} is inside the cell's height");
            Assert.True(part.Position.X >= 0f && part.End.X <= layout.Pitch + 0.01f, $"the {name} is inside the column");
        }
    }

    /// <summary>
    /// The strip's marker is the run-history entry's own: the icon a player sees on
    /// the history screen is 44.8 units on a side, and that is the side the strip draws
    /// it at wherever the pane is wide enough. It used to be capped at a multiple of
    /// the numeral's font size, which drew a marker half again the game's and cost the
    /// pane the room the plate then took from it.
    /// </summary>
    [Theory]
    [InlineData(5, 456f, 22)]
    [InlineData(3, 900f, 30)]
    public void TheStripsMarkerIsTheRunHistoryEntrysOwnSize(int floors, float width, int lineSize)
    {
        var layout = LibraryPaneArt.LayoutStrip(floors, width, anchor: 0, lineSize);
        var cell = LibraryPaneArt.CellGeometry(layout, lineSize);

        Assert.Equal(LibraryPaneArt.NativeCell, layout.Cell, 3);
        Assert.Equal(FloorMarkerArt.EntryIconSide, cell.Icon.Size.X * 0.8f, 3);
    }

    /// <summary>
    /// What the captain saw: "Open the run" across the floor numerals and the version
    /// line under "Share this run". The plate keeps the pane's bottom, the strip keeps
    /// the game's marker, everything else is measured, and the relics page before
    /// anything overlaps.
    /// </summary>
    [Fact]
    public void AShortPanePagesTheRelicsRatherThanDrawingThePlateOverThem()
    {
        var pane = MinePane();
        var roomy = pane.Lay(relics: 10, plateRows: 2, height: pane.Height);
        var short_ = pane.Lay(relics: 10, plateRows: 2, height: pane.Height - 100f);

        Assert.Equal(1, roomy.RelicPage.Pages);
        Assert.Equal(LibraryPaneArt.NativeCell, roomy.Strip!.Value.Cell, 3);
        Assert.Equal(LibraryPaneArt.NativeCell, short_.Strip!.Value.Cell, 3);
        Assert.True(short_.RelicRows < roomy.RelicRows, "the relics gave a row");
        Assert.True(short_.RelicPage.Pages > 1, "the rows that gave are a page away");
        foreach (var layout in new[] { roomy, short_ })
        {
            AssertNothingOverlaps(pane, layout, relics: 10);
        }

        Assert.Equal(pane.Height - LibraryPaneArt.PlateHeight(2, pane.Ribbon.Y), roomy.PlateTop!.Value, 3);
        Assert.True(roomy.PlateTop.Value + pane.Ribbon.Y < pane.Height, "air under the plate before the panel's own ribbon");
    }

    /// <summary>
    /// A run with ten relics made a second relic row, and the second row left the
    /// strip less than its smallest marker, so the pane refused - and the library
    /// vanished when that run was pressed. The relic rows now page: they keep the
    /// holder's own size, the pane shows as many rows as fit beside the strip at the
    /// game's own marker, and the rest are a page away behind the game's own arrows.
    /// Nothing is drawn over anything, whatever the count.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(60)]
    public void TheMinePaneHoldsAnyRelicCountAtTheGamesOwnMarker(int relics)
    {
        var pane = MinePane();
        var layout = pane.Lay(relics, plateRows: 2, height: pane.Height);

        AssertNothingOverlaps(pane, layout, relics);
        Assert.Equal(LibraryPaneArt.NativeCell, layout.Strip!.Value.Cell, 3);
        var block = pane.RelicBlock(relics);
        if (block.Rows <= layout.RelicRows)
        {
            Assert.Equal(1, layout.RelicPage.Pages);
            Assert.Equal(relics, layout.RelicPage.Count);
        }
        else
        {
            Assert.True(layout.RelicPage.Pages > 1, $"{relics} relics in {block.Rows} rows page on this pane");
            Assert.True(layout.RelicPage.HasNext);
            Assert.True(layout.RelicPage.Drawn <= block.PlacesIn(layout.RelicRows), "the page's places fit its rows");
        }
    }

    /// <summary>
    /// The opened run's pane draws the deck under the strip and has no plate, and it is
    /// held to its height like the browser's: at the run-history holder's size a
    /// mid-act run's relics and an ordinary deck ran past the panel's ribbon, so the
    /// deck pages the way the relics do, and the relics keep a row for it. Every card
    /// and every relic is a page away at full size, whatever the counts.
    /// </summary>
    [Theory]
    [InlineData(2, 10)]
    [InlineData(15, 20)]
    [InlineData(36, 40)]
    [InlineData(60, 80)]
    public void TheOpenedRunPaneHoldsAnyDeckAndRelicCountWithoutOverflowing(int relics, int cards)
    {
        var pane = MinePane() with { SubtitleLines = 1, FactLines = 4 };
        var layout = pane.Lay(relics, plateRows: 0, height: pane.ViewHeight, cards: cards);

        AssertNothingOverlaps(pane, layout, relics, cards);
        Assert.Equal(LibraryPaneArt.NativeCell, layout.Strip!.Value.Cell, 3);
        Assert.True(layout.RelicRows >= 1);
        Assert.True(layout.DeckRows >= 1);
        Assert.True(layout.AfterDeck + pane.Below <= pane.ViewHeight + 0.01f, "the facts end inside the pane");
        var deck = LibraryPaneArt.LayoutDeck(cards);
        if (deck.Rows > layout.DeckRows)
        {
            Assert.True(layout.DeckPage.Pages > 1, "the deck pages");
            Assert.True(layout.DeckPage.Drawn <= deck.PlacesIn(layout.DeckRows));
        }
        else
        {
            Assert.Equal(cards, layout.DeckPage.Count);
        }
    }

    /// <summary>Every relic page of a long run is reachable, holds whole places, and
    /// the last one ends on the last relic.</summary>
    [Fact]
    public void RelicPagesWalkEveryRelic()
    {
        var pane = MinePane();
        const int relics = 60;
        var first = pane.Lay(relics, plateRows: 2, height: pane.Height);
        var seen = 0;
        for (var index = 0; index < first.RelicPage.Pages; index++)
        {
            var page = pane.Lay(relics, plateRows: 2, height: pane.Height, relicPage: index).RelicPage;
            Assert.Equal(seen, page.First);
            Assert.Equal(index > 0, page.HasPrevious);
            Assert.Equal(index < first.RelicPage.Pages - 1, page.HasNext);
            seen += page.Count;
        }

        Assert.Equal(relics, seen);
    }

    /// <summary>A pane that cannot give its relics and its deck one row each beside
    /// the strip at the game's marker refuses by name rather than drawing one part over
    /// another, or a marker smaller than the game's. So does one with no strip and no
    /// room for its lines.</summary>
    [Fact]
    public void APaneTooShortForOneRowOfEachRefusesRatherThanOverlapping()
    {
        var pane = MinePane();

        var refused = Assert.Throws<InvalidOperationException>(() =>
            pane.Lay(relics: 5, plateRows: 2, height: pane.Height - 200f));
        Assert.Contains("run strip", refused.Message);

        var noStrip = Assert.Throws<InvalidOperationException>(() =>
            pane.Lay(relics: 1, plateRows: 2, height: pane.Above + pane.Below, floors: 0));
        Assert.Contains("refuses", noStrip.Message);
    }

    /// <summary>
    /// The relic rows are the run-history screen's own flow: one holder box per relic,
    /// nothing between, wrapped at the width. The first row keeps the deck count's
    /// measured width clear - it used to fill the row edge to edge and the count was
    /// drawn over its last icons - and a count too wide to share a row with even one
    /// relic takes a line of its own over the rows.
    /// </summary>
    [Fact]
    public void TheFirstRelicRowKeepsTheDeckCountsWidthClear()
    {
        var pane = MinePane();
        var width = pane.Width;
        var box = pane.RelicBox;
        var perRow = (int)Math.Floor(width / box);
        const float countWidth = 100f;

        var block = LibraryPaneArt.LayoutRelics(20, width, box, countWidth, countLine: 34.8f);

        Assert.Equal(perRow, block.PerRow);
        Assert.True(block.FirstRow < perRow, "the first row gave the count its room");
        var gap = box * LibraryPaneArt.CountGapShare;
        Assert.True((block.FirstRow * box) + gap + countWidth <= width, "the count fits beside the first row's relics with air");
        Assert.True(((block.FirstRow + 1) * box) + gap + countWidth > width, "the first row holds every relic that leaves the count clear");
        Assert.Equal(0f, block.CountLine);
        Assert.Equal(1 + (int)Math.Ceiling((20 - block.FirstRow) / (float)perRow), block.Rows);
        Assert.Equal(0, block.RowOf(block.FirstRow - 1));
        Assert.Equal(1, block.RowOf(block.FirstRow));
        Assert.Equal(0, block.ColumnOf(block.FirstRow));
        Assert.Equal(perRow - 1, block.ColumnOf(block.FirstRow + perRow - 1));
        Assert.Equal(block.FirstRow + perRow, block.PlacesIn(2));

        var ownLine = LibraryPaneArt.LayoutRelics(3, width, box, width - (box / 2f), countLine: 34.8f);
        Assert.Equal(perRow, ownLine.FirstRow);
        Assert.Equal(34.8f, ownLine.CountLine, 3);
        Assert.Equal(1, ownLine.Rows);
        Assert.Equal(34.8f + box + (box * 0.3f), LibraryPaneArt.RelicRowsHeight(ownLine, 1, box), 3);

        var none = LibraryPaneArt.LayoutRelics(0, width, box, countWidth, countLine: 34.8f);
        Assert.Equal(0, none.Rows);
        Assert.Equal(34.8f, LibraryPaneArt.RelicRowsHeight(none, 0, box), 3);
        var nothing = LibraryPaneArt.LayoutRelics(0, width, box, null, countLine: 34.8f);
        Assert.Equal(0f, LibraryPaneArt.RelicRowsHeight(nothing, 0, box));
    }

    /// <summary>The plate's ribbons stand side by side at the ribbon's own width, the
    /// first at the pane's left edge and the last at its right, and a pane too narrow
    /// for them refuses rather than stacking two controls under one press.</summary>
    [Fact]
    public void ThePlatesRibbonsStandSideBySideAtTheirOwnWidth()
    {
        var pane = MinePane();
        var columns = LibraryPaneArt.PlateColumns(2, pane.Width, pane.Ribbon.X);

        Assert.Equal(2, columns.Count);
        Assert.Equal(0f, columns[0]);
        Assert.Equal(pane.Width - pane.Ribbon.X, columns[1], 3);
        Assert.True(columns[1] >= pane.Ribbon.X, "the ribbons do not overlap");
        Assert.Equal(pane.Ribbon.Y * (1f + LibraryPaneArt.PlateAir), LibraryPaneArt.PlateHeight(2, pane.Ribbon.Y), 3);
        Assert.Empty(LibraryPaneArt.PlateColumns(0, pane.Width, pane.Ribbon.X));
        Assert.Throws<InvalidOperationException>(() => LibraryPaneArt.PlateColumns(3, pane.Width, pane.Ribbon.X));
    }

    /// <summary>
    /// The Mine pane at v0.111.0's own sizes, on the popup as the library expands it,
    /// holds the most relic rows it can beside the strip at the game's own marker, and
    /// pages the rest - the numbers the captain's screen was drawn from.
    /// <see cref="NativePaneSizes"/> holds them to the shipped scenes where the game is
    /// installed; this holds the arithmetic where it is not, so a spacing change that
    /// would re-collide fails here first.
    /// </summary>
    [Fact]
    public void TheMinePaneFitsAtThisBuildsSizesWithTheStripAtTheGamesOwnMarker()
    {
        AssertMinePaneFits(MinePane());
    }

    /// <summary>The Community pane carries a creator line and no plate; its way
    /// forward is the panel's own ribbon, so its room is the pane's height less its
    /// lines, and it too keeps the strip at the game's marker with dozens of relics.</summary>
    [Fact]
    public void TheCommunityPaneFitsAtThisBuildsSizes()
    {
        var pane = MinePane() with { SubtitleLines = 1 };
        var dozens = pane.Lay(relics: 36, plateRows: 0, height: pane.Height);

        Assert.Equal(LibraryPaneArt.NativeCell, dozens.Strip!.Value.Cell, 3);
        AssertNothingOverlaps(pane, dozens, 36);
        Assert.True(dozens.RelicRows >= 3, $"{dozens.RelicRows} relic rows beside the strip");
        Assert.True(dozens.RelicPage.Pages > 1, "dozens of relics page");
    }

    internal static void AssertMinePaneFits(NativePaneSizes pane)
    {
        // The most relics the pane shows on one page, and a run past that
        var most = pane.Lay(relics: 60, plateRows: 2, height: pane.Height);
        var admitted = pane.RelicBlock(60).PlacesIn(most.RelicRows);
        var full = pane.Lay(relics: admitted, plateRows: 2, height: pane.Height);

        Assert.True(most.RelicRows >= 2, $"the pane holds {most.RelicRows} relic rows beside the strip");
        foreach (var (layout, relics) in new[] { (most, 60), (full, admitted) })
        {
            Assert.Equal(LibraryPaneArt.NativeCell, layout.Strip!.Value.Cell, 3);
            AssertNothingOverlaps(pane, layout, relics);
        }

        Assert.Equal(1, full.RelicPage.Pages);
        Assert.True(most.RelicPage.Pages > 1);
    }

    /// <summary>Every part under the one before it, and the plate under them all.</summary>
    private static void AssertNothingOverlaps(
        NativePaneSizes pane, LibraryPaneArt.PaneLayout layout, int relics, int cards = 0)
    {
        Assert.Equal(pane.Above, layout.RelicsTop, 3);
        var block = pane.RelicBlock(relics);
        Assert.True(layout.RelicRows <= block.Rows, "no more rows than the run has");
        Assert.Equal(LibraryPaneArt.RelicRowsHeight(block, layout.RelicRows, pane.RelicBox), layout.RelicsWindow, 3);
        Assert.True(layout.RelicPage.Drawn <= block.PlacesIn(Math.Max(layout.RelicRows, block.Rows == 0 ? 0 : 1)), "every place drawn is on a drawn row");
        Assert.Equal(layout.RelicsTop + layout.RelicsWindow, layout.StripTop, 3);
        Assert.Equal(layout.StripTop + layout.StripRoom, layout.AfterStrip, 3);
        var deck = LibraryPaneArt.LayoutDeck(cards);
        Assert.True(layout.DeckRows <= deck.Rows, "no more tile rows than the deck has");
        Assert.Equal(LibraryPaneArt.DeckRowsHeight(layout.DeckRows, pane.TilePitch), layout.DeckWindow, 3);
        Assert.True(layout.DeckPage.Drawn <= deck.PlacesIn(layout.DeckRows), "every tile drawn is on a drawn row");
        Assert.Equal(layout.AfterStrip + layout.DeckWindow, layout.AfterDeck, 3);
        if (layout.PlateTop is { } plateTop)
        {
            Assert.True(layout.AfterDeck + pane.Below <= plateTop + 0.01f, "the plate starts under the facts");
        }
    }

    /// <summary>
    /// The sizes the library reads off v0.111.0's scenes, as the client reads them:
    /// the design size of each native role the Mine pane draws with, the relic
    /// holder's box and icon, the panel's ribbon and the popup's body. The pane's height
    /// is composed the way <see cref="LibraryScreen.Show"/> composes it - the ribbons
    /// at <see cref="LibraryScreen.RibbonTop"/>, the body at
    /// <see cref="LibraryScreen.BodyRoom"/> under its scene offset, the panes under
    /// <see cref="LibraryScreen.BandBottom"/> - rather than restated.
    /// </summary>
    internal sealed record NativePaneSizes(
        int RowTitle, int Secondary, int Fact, int CardCaption, int Numeral, int Body, Vector2 Ribbon,
        float BodyTop, float RelicBox, float RelicIcon, int SubtitleLines = 0, int FactLines = 2)
    {
        internal float Width => (LibraryScreen.PopupWidth - 140f) * (1f - 0.54f - 0.03f);

        /// <summary>The browser's pane: under a body line and the tab band.</summary>
        internal float Height
        {
            get
            {
                var body = new GameTextStyle(null, Body);
                var areaTop = BodyTop + LibraryScreen.BodyRoom(body, Body * LibraryScreen.LabelLineRatio);
                return LibraryScreen.RibbonTop(Ribbon.Y) - LibraryScreen.BandBottom(areaTop, Ribbon.Y);
            }
        }

        /// <summary>The opened run's pane: no body, no band, so the area's own top.</summary>
        internal float ViewHeight => LibraryScreen.RibbonTop(Ribbon.Y) - BodyTop;

        /// <summary>The heading, and the creator line where the pane has one.</summary>
        internal float Above => Line(RowTitle) + (SubtitleLines * Line(Secondary));

        /// <summary>The fact lines under the strip: reached floor and the version line
        /// on the browser's pane, the fight's facts on the opened run's.</summary>
        internal float Below => FactLines * Line(Fact);

        internal float TilePitch => LibraryPaneArt.DeckRowPitch(Width / 8f, CardCaption);

        /// <summary>The relic rows with the deck count on the first, the count's width
        /// estimated the way a fontless style estimates it.</summary>
        internal LibraryPaneArt.GridBlock RelicBlock(int relics)
        {
            var count = "16 cards";
            var fact = new GameTextStyle(null, Fact);
            return LibraryPaneArt.LayoutRelics(
                relics, Width, RelicBox, fact.Width(count, Fact * 0.6f * count.Length),
                LibraryScreen.LineHeight(count, Width, fact));
        }

        internal LibraryPaneArt.PaneLayout Lay(
            int relics, int plateRows, float height, int? relicPage = null, int floors = 5, int cards = 0,
            int? deckPage = null) =>
            LibraryPaneArt.Lay(
                Above, RelicBlock(relics), RelicBox, LibraryPaneArt.LayoutDeck(cards), TilePitch, Below,
                floors, Width, 0, Numeral, null, relicPage, deckPage, plateRows, Ribbon, height);
    }

    internal static NativePaneSizes MinePane() => new(
        RowTitle: 28, Secondary: 24, Fact: 24, CardCaption: 24, Numeral: 22, Body: 26,
        Ribbon: new Vector2(180f, 72f), BodyTop: 115f, RelicBox: 68f, RelicIcon: 60f);

    private static float Line(int size) =>
        LibraryScreen.LineHeight("one line", 1000f, new GameTextStyle(null, size));

    [Fact]
    public void DeckRowsLeaveRoomForNativeCardCounts()
    {
        const float tile = 64.5f;
        const int caption = 24;

        var pitch = LibraryPaneArt.DeckRowPitch(tile, caption);
        var countBottom = (tile * 0.82f * 0.76f) + (caption * 1.3f);

        Assert.True(pitch >= countBottom);
    }

    [Fact]
    public void StripPageButtonActivatesFromRetailKeyboardActions()
    {
        _ = EngineHost.StartupPhase();
        var presses = 0;
        var image = new Texture2D();
        var button = LibraryPaneArt.AddStripPageButton(
            new Control(), "Previous", "Previous", true, Vector2.Zero, 28f, 28f,
            _ => image, () => presses++);

        Assert.Equal(string.Empty, button.Text);
        Assert.False(button.HasThemeFontOverride("font"));
        Assert.Same(image, button.GetChildren().OfType<TextureRect>().Single().Texture);

        foreach (var action in new[] { MegaInput.confirm, MegaInput.select })
        {
            var input = new InputEventAction { Action = action, Pressed = true };
            button.EmitSignal("gui_input", Variant.From<InputEvent>(input));
        }

        Assert.Equal(2, presses);
    }
}
