using Godot;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class GameTextTests
{
    private const string FontEntry = "font";
    private const string FontSizeEntry = "font_size";

    [Fact]
    public void ANodeWithNoFontOfItsOwnIsNotANativeRole()
    {
        Assert.Null(GameText.Of(new Label { Text = "plain" }));
        Assert.Null(GameText.Of(new Control()));
        Assert.Null(GameText.Of(null));
    }

    [Fact]
    public void ANativeLabelAnswersTheExactStyleItDraws()
    {
        var native = Native(28);

        var style = GameText.Of(native);

        Assert.NotNull(style);
        Assert.Equal(28, style!.Value.Size);
        Assert.Same(native.GetThemeFont(FontEntry, "Label"), style.Value.Font);
    }

    [Fact]
    public void ASmallNativeLabelIsStillItsOwnRole()
    {
        Assert.Equal(8, GameText.Of(Native(8))?.Size);
    }

    [Fact]
    public void OnePieceOfFurnitureAnswersTheRoleInsideIt()
    {
        var furniture = new Control();
        furniture.AddChild(Native(19));

        Assert.Equal(19, GameText.Under(furniture)?.Size);
    }

    [Fact]
    public void MissingNativeTextHasNoSubstitute()
    {
        var furniture = new Control();
        furniture.AddChild(new Label { Text = "ours" });

        Assert.Null(GameText.Under(furniture));
        Assert.Throws<InvalidOperationException>(() => GameText.RequireUnder(furniture, "test row"));
    }

    [Fact]
    public void ApplyingARolePutsBothTheFontAndTheSizeOnTheNode()
    {
        var label = new GameTextStyle(new Font(), 21).ApplyTo(new Label());

        Assert.True(label.HasThemeFontOverride(FontEntry));
        Assert.Equal(21, label.GetThemeFontSize(FontSizeEntry, "Label"));
    }

    [Fact]
    public void ARoleWithNoFontStillSetsTheSize()
    {
        var label = new GameTextStyle(null, 19).ApplyTo(new Label());

        Assert.False(label.HasThemeFontOverride(FontEntry));
        Assert.Equal(19, label.GetThemeFontSize(FontSizeEntry, "Label"));
    }

    private static Label Native(int size)
    {
        var label = new Label();
        label.AddThemeFontOverride(FontEntry, new Font());
        label.AddThemeFontSizeOverride(FontSizeEntry, size);
        return label;
    }
}
