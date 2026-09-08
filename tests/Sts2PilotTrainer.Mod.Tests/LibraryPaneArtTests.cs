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

    [Fact]
    public void FullRunStripFocusReachesEveryFloor()
    {
        const int floors = 50;
        var layout = LibraryPaneArt.LayoutStrip(floors, 500f);
        var visited = new List<int>();
        var index = 0;

        while (!visited.Contains(index))
        {
            visited.Add(index);
            index = layout.RightOf(index, floors);
        }

        Assert.Equal(Enumerable.Range(0, floors), visited);
        Assert.Equal(layout.Columns, layout.Below(0, floors));
        Assert.Equal(0, layout.Above(layout.Columns));
    }
}
