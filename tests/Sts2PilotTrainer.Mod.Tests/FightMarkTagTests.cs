using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The bookmark tag as stock nodes, assembled with no game: what a derived mark puts
/// on it, and what a press does.
/// </summary>
public sealed class FightMarkTagTests
{
    private static readonly Vector2 Viewport = new(1512, 916);
    private static readonly Vector2 Anchor = new(1358, 72);

    [Fact]
    public void ItIsBuiltHiddenAndDrawnOnlyWhereTheDerivationDrewItAndTheShellAllows()
    {
        var tag = FightMarkTag.Build(Viewport, Anchor, () => { });
        Assert.False(tag.Root.Visible);
        Assert.Equal(FightMarkTag.RootName, tag.Root.Name.ToString());

        tag.Apply(FightMark.For(new FightMarkFacts(true, RunCaptureState.Recording, 2, false, false)), mayDraw: true);
        Assert.True(tag.Root.Visible);
        Assert.False(tag.Control.Disabled);

        tag.Apply(FightMark.For(new FightMarkFacts(true, RunCaptureState.Recording, 2, false, false)), mayDraw: false);
        Assert.False(tag.Root.Visible);

        tag.Apply(FightMark.Nothing, mayDraw: true);
        Assert.False(tag.Root.Visible);
    }

    /// <summary>Hollow cream at rest, filled gold with an ink hairline once the mark is
    /// on, and the words a hover away rather than on the tag.</summary>
    [Fact]
    public void TheControlIsHollowAtRestAndFilledOnceBookmarkedAndCarriesNoText()
    {
        var tag = FightMarkTag.Build(Viewport, Anchor, () => { });

        tag.Apply(FightMark.For(new FightMarkFacts(true, RunCaptureState.Recording, 2, false, false)), mayDraw: true);
        Assert.False(tag.Bookmarked);
        Assert.Equal(["Glyph"], GlyphNames(tag.Control));
        Assert.Equal(RecorderCopy.BookmarkThisFight, tag.Control.TooltipText);
        Assert.Equal(string.Empty, tag.Control.Text);

        tag.Apply(FightMark.For(new FightMarkFacts(true, RunCaptureState.Recording, 2, false, true)), mayDraw: true);
        Assert.True(tag.Bookmarked);
        Assert.Equal(["GlyphFill", "GlyphEdge"], GlyphNames(tag.Control));
        Assert.Contains(RecorderCopy.PressToRemoveBookmark, tag.Control.TooltipText, StringComparison.Ordinal);
    }

    [Fact]
    public void PressingTheControlIsThePressItWasBuiltWith()
    {
        var presses = 0;
        var tag = FightMarkTag.Build(Viewport, Anchor, () => presses++);

        tag.Control.EmitPressed();

        Assert.Equal(1, presses);
    }

    /// <summary>The tag hangs from the anchor's top-right corner, inside the viewport,
    /// with the control at its right end.</summary>
    [Fact]
    public void TheTagHangsFromTheAnchorWithTheControlAtItsRightEnd()
    {
        var tag = FightMarkTag.Build(Viewport, Anchor, () => { });
        var plate = tag.Root.GetChildren().OfType<Polygon2D>().Single(node => node.Name == "Plate").Polygon;

        Assert.Equal(Anchor.X, plate.Max(point => point.X), 0.01f);
        Assert.Equal(Anchor.Y, plate.Min(point => point.Y), 0.01f);
        Assert.True(plate.Min(point => point.X) > 0);
        Assert.True(tag.Control.Position.X + tag.Control.Size.X <= Anchor.X);
        var mark = tag.Root.GetChildren().OfType<Control>().Single(node => node.Name == "Mark");
        Assert.True(tag.Control.Position.X > mark.Position.X);
    }

    private static IReadOnlyList<string> GlyphNames(Node host) =>
        [.. host.GetChildren().Select(child => child.Name.ToString()).Where(name => name.StartsWith("Glyph", StringComparison.Ordinal))];
}
