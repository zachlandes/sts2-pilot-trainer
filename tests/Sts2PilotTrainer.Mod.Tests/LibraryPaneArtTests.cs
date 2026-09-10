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
