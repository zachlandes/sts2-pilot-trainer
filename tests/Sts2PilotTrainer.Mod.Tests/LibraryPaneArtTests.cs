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

    /// <summary>The largest cell that fits a room is the cell whose strip, air
    /// included, is that room: the inverse of the cell's own geometry.</summary>
    [Theory]
    [InlineData(100f, 22)]
    [InlineData(122.8f, 22)]
    [InlineData(200f, 16)]
    public void TheCellFittingARoomFillsIt(float room, int lineSize)
    {
        var cell = LibraryPaneArt.CellFitting(room, lineSize);
        var layout = LibraryPaneArt.LayoutStrip(3, 900f, anchor: 0, lineSize, room: room);

        Assert.True(layout.Cell <= cell + 0.001f);
        Assert.True(layout.Room <= room + 0.01f, $"a strip of {layout.Room} in a room of {room}");
        if (cell < LibraryPaneArt.NativeCell) Assert.Equal(room, layout.Room, 2);
    }

    /// <summary>
    /// What the captain saw: "Open the run" across the floor numerals and the version
    /// line under "Share this run". The plate keeps the pane's bottom, everything else
    /// is measured, and the strip gives before anything overlaps.
    /// </summary>
    [Fact]
    public void AShortPaneShrinksTheStripRatherThanDrawingThePlateOverIt()
    {
        var pane = MinePane();
        var roomy = LibraryPaneArt.Lay(
            pane.Above, pane.Below, 5, pane.Width, 0, pane.Numeral, null, 3, pane.Ribbon, pane.Height);
        var short_ = LibraryPaneArt.Lay(
            pane.Above, pane.Below, 5, pane.Width, 0, pane.Numeral, null, 3, pane.Ribbon, pane.Height - 30f);

        Assert.Equal(LibraryPaneArt.NativeCell, roomy.Strip!.Value.Cell, 3);
        Assert.True(short_.Strip!.Value.Cell < roomy.Strip.Value.Cell, "the strip gave");
        Assert.True(short_.Strip.Value.Cell >= LibraryPaneArt.MinimumCell);
        foreach (var layout in new[] { roomy, short_ })
        {
            Assert.True(layout.StripTop >= pane.Above);
            Assert.True(layout.AfterStrip >= layout.StripTop + layout.Strip!.Value.Room - 0.01f, "the facts start under the strip");
            Assert.True(layout.AfterStrip + pane.Below <= layout.PlateTop!.Value + 0.01f, "the plate starts under the facts");
        }

        Assert.Equal(pane.Height - LibraryPaneArt.PlateHeight(3, pane.Ribbon), roomy.PlateTop!.Value, 3);
    }

    /// <summary>A pane that cannot give the strip even its smallest marker refuses,
    /// by name, rather than drawing the strip under the plate. So does one with no
    /// strip and no room for its lines.</summary>
    [Fact]
    public void APaneTooShortForTheSmallestStripRefusesRatherThanOverlapping()
    {
        var pane = MinePane();

        var refused = Assert.Throws<InvalidOperationException>(() => LibraryPaneArt.Lay(
            pane.Above, pane.Below, 5, pane.Width, 0, pane.Numeral, null, 3, pane.Ribbon, pane.Height - 120f));
        Assert.Contains("run strip", refused.Message);

        var noStrip = Assert.Throws<InvalidOperationException>(() => LibraryPaneArt.Lay(
            pane.Above, pane.Below, 0, pane.Width, 0, pane.Numeral, null, 3, pane.Ribbon, pane.Above + pane.Below));
        Assert.Contains("short", noStrip.Message);
    }

    /// <summary>The opened run's pane has no plate and nothing at its foot to keep
    /// clear of: its strip stays at the game's marker however tall its deck is, and
    /// the pane flows rather than refusing a run for the size of its deck.</summary>
    [Fact]
    public void APaneWithNoPlateFlowsAndNeverGivesUpTheStrip()
    {
        var pane = MinePane();
        var layout = LibraryPaneArt.Lay(
            pane.Above, pane.Below + 400f, 5, pane.Width, 0, pane.Numeral, null, 0, pane.Ribbon, pane.Height);

        Assert.Null(layout.PlateTop);
        Assert.Equal(LibraryPaneArt.NativeCell, layout.Strip!.Value.Cell, 3);
    }

    /// <summary>
    /// The Mine pane at v0.111.0's own sizes, on the popup as the library expands it,
    /// fits with the strip at the game's marker and air to spare - the numbers the
    /// captain's screen was drawn from. <see cref="NativePaneSizes"/> holds them to the
    /// shipped scenes where the game is installed; this holds the arithmetic where it is
    /// not, so a spacing change that would re-collide fails here first.
    /// </summary>
    [Fact]
    public void TheMinePaneFitsAtThisBuildsSizesWithTheStripAtTheGamesOwnMarker()
    {
        AssertMinePaneFits(MinePane());
    }

    /// <summary>The Community pane carries a creator line and one ribbon; it too keeps
    /// the strip at the game's marker.</summary>
    [Fact]
    public void TheCommunityPaneFitsAtThisBuildsSizesWithTheStripAtTheGamesOwnMarker()
    {
        var pane = MinePane();
        var above = pane.Above + Line(pane.Secondary);
        var layout = LibraryPaneArt.Lay(
            above, pane.Below, 5, pane.Width, 0, pane.Numeral, null, 1, pane.Ribbon, pane.Height);

        Assert.Equal(LibraryPaneArt.NativeCell, layout.Strip!.Value.Cell, 3);
        Assert.True(layout.AfterStrip + pane.Below <= layout.PlateTop!.Value);
    }

    internal static void AssertMinePaneFits(NativePaneSizes pane)
    {
        var layout = LibraryPaneArt.Lay(
            pane.Above, pane.Below, 5, pane.Width, 0, pane.Numeral, null, 3, pane.Ribbon, pane.Height);

        var spare = layout.PlateTop!.Value - (layout.AfterStrip + pane.Below);
        Assert.True(
            layout.Strip!.Value.Cell >= LibraryPaneArt.NativeCell - 0.001f,
            $"the strip shrank to {layout.Strip.Value.Cell} on a pane {pane.Height} tall");
        Assert.True(spare >= 0f, $"the plate overlaps the facts by {-spare}");
    }

    /// <summary>
    /// The sizes the library reads off v0.111.0's scenes, as the client reads them:
    /// the design size of each native role the Mine pane draws with, the panel's ribbon
    /// and the popup's body. The pane's height follows from those the way
    /// <see cref="LibraryScreen.PaneHeight"/> derives it.
    /// </summary>
    internal sealed record NativePaneSizes(
        int RowTitle, int Secondary, int Fact, int CardCaption, int Numeral, int Body, float Ribbon, float BodyTop)
    {
        internal float Width => (LibraryScreen.PopupWidth - 140f) * (1f - 0.54f - 0.03f);

        internal float Height => LibraryScreen.PaneHeight(
            BodyTop, (Body * LibraryScreen.LabelLineRatio) + (Body * 0.15f), Ribbon);

        /// <summary>The heading and one row of relics with the deck count on it.</summary>
        internal float Above =>
            Line(RowTitle) + LibraryPaneArt.RelicRowsHeight(2, Width, CardCaption * 1.7f);

        /// <summary>Reached floor, and the version line.</summary>
        internal float Below => 2 * Line(Fact);
    }

    internal static NativePaneSizes MinePane() => new(
        RowTitle: 28, Secondary: 24, Fact: 24, CardCaption: 24, Numeral: 22, Body: 26, Ribbon: 72f, BodyTop: 115f);

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
