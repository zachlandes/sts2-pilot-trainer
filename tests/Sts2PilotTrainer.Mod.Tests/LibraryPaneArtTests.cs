using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class LibraryPaneArtTests
{
    [Fact]
    public void FullRunStripWrapsIntoReadableRows()
    {
        const int floors = 50;
        const float width = 500f;

        var layout = LibraryPaneArt.LayoutStrip(floors, width);

        Assert.Equal(17, layout.Columns);
        Assert.Equal(3, layout.Rows);
        Assert.True(layout.Pitch >= 28f);
        Assert.True(layout.Cell >= 16f);
        Assert.Equal(0f, layout.RowOffset(0, floors, width));
        Assert.True(layout.RowOffset(49, floors, width) > 0f);
    }
}
