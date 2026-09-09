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

        Assert.Equal(15, layout.Count);
        Assert.Equal(4, layout.Pages);
        Assert.False(layout.HasPrevious);
        Assert.True(layout.HasNext);
        Assert.True(layout.Pitch >= 28f);
        Assert.True(layout.Cell >= 16f);
        Assert.Equal(15, layout.NextSlot);
    }

    [Fact]
    public void FullRunStripOpensOnTheAnchoredPage()
    {
        var layout = LibraryPaneArt.LayoutStrip(50, 500f, anchor: 49, LineSize);

        Assert.Equal(3, layout.Index);
        Assert.Equal(45, layout.First);
        Assert.Equal(5, layout.Count);
        Assert.True(layout.HasPrevious);
        Assert.False(layout.HasNext);
        Assert.Equal(1, layout.SlotOf(45));
    }

    [Fact]
    public void FullRunStripCanMoveToAnAdjacentPage()
    {
        var layout = LibraryPaneArt.LayoutStrip(50, 500f, anchor: 49, LineSize, requestedPage: 1);

        Assert.Equal(1, layout.Index);
        Assert.Equal(15, layout.First);
        Assert.Equal(15, layout.Count);
        Assert.True(layout.HasPrevious);
        Assert.True(layout.HasNext);
        Assert.Equal(16, layout.NextSlot);
    }

    [Fact]
    public void StripPageButtonActivatesFromRetailKeyboardActions()
    {
        _ = EngineHost.StartupPhase();
        var presses = 0;
        var button = LibraryPaneArt.AddStripPageButton(
            new Control(), "Previous", "Previous", "‹", Vector2.Zero, 28f, 28f,
            new GameTextStyle(null, LineSize), () => presses++);

        foreach (var action in new[] { MegaInput.confirm, MegaInput.select })
        {
            var input = new InputEventAction { Action = action, Pressed = true };
            button.EmitSignal("gui_input", Variant.From<InputEvent>(input));
        }

        Assert.Equal(2, presses);
    }
}
