using Godot;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class LibraryRibbonArtTests
{
    [Theory]
    [InlineData(600f)]
    [InlineData(1000f)]
    [InlineData(1600f)]
    public void WideRowsKeepTheSameNativeEndCaps(float width)
    {
        var image = new TextureRect { Texture = new Texture2D(), Size = new Vector2(300, 80) };
        var patch = LibraryRibbonArt.Create(image, 300, width);

        Assert.Equal(75, patch.PatchMarginLeft);
        Assert.Equal(75, patch.PatchMarginRight);
        Assert.Equal(0, patch.PatchMarginTop);
        Assert.Equal(0, patch.PatchMarginBottom);
        Assert.Equal(width, patch.Size.X);
        Assert.Equal(80, patch.Size.Y);
        Assert.Equal(NinePatchRect.AxisStretchMode.Stretch, patch.AxisStretchHorizontal);
        Assert.Equal(NinePatchRect.AxisStretchMode.Stretch, patch.AxisStretchVertical);
    }

    [Fact]
    public void NarrowRowsDoNotGrowToFitTheMargins()
    {
        var patch = LibraryRibbonArt.Create(new TextureRect(), 300, 100);
        Assert.Equal(25, patch.PatchMarginLeft);
        Assert.Equal(25, patch.PatchMarginRight);
        Assert.True(patch.PatchMarginLeft + patch.PatchMarginRight < patch.Size.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothLayersKeepTheMaterialsAndColoursTheRetailButtonAnimates(bool outline)
    {
        var image = new TextureRect
        {
            Name = outline ? "Outline" : "Image",
            Texture = new Texture2D(),
            Material = outline ? new CanvasItemMaterial() : new ShaderMaterial(),
            Modulate = new Color(1, 1, 1, 0.5f),
            SelfModulate = new Color(0.9f, 0.7f, 0, 1),
            Visible = false,
        };
        var patch = LibraryRibbonArt.Create(image, 300, 900);

        Assert.Equal(image.Name, patch.Name);
        Assert.Same(image.Texture, patch.Texture);
        Assert.Same(image.Material, patch.Material);
        Assert.Equal(image.Modulate, patch.Modulate);
        Assert.Equal(image.SelfModulate, patch.SelfModulate);
        Assert.False(patch.Visible);
        Assert.Equal(Control.MouseFilterEnum.Ignore, patch.MouseFilter);
    }
}
