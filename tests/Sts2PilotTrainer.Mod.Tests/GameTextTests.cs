using Godot;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the mod reads off the game's own labels, in a process with no game.
///
/// The reading is the whole of this change: every size Runmobile draws is taken from a
/// native element rather than written down, so the two things that can go wrong are
/// taking a size off something that is not the game's, and taking the wrong one off a
/// screen that has several. Both are asserted here on stock Godot nodes, which is what
/// the game's own labels look like to this code once their overrides are set.
/// </summary>
public sealed class GameTextTests
{
    private const string FontEntry = "font";

    private const string FontSizeEntry = "font_size";

    /// <summary>A control with no font of its own is a stock Godot control or one of
    /// ours, and copying its size would be copying Godot's default back onto
    /// ourselves.</summary>
    [Fact]
    public void ANodeWithNoFontOfItsOwnIsNotANativeRole()
    {
        Assert.Null(GameText.Of(new Label { Text = "plain" }));
        Assert.Null(GameText.Of(new Control()));
        Assert.Null(GameText.Of(null));
    }

    [Fact]
    public void ANativeLabelAnswersTheSizeItIsDrawnAt()
    {
        var style = GameText.Of(Native(28));

        Assert.NotNull(style);
        Assert.Equal(28, style!.Value.Size);
        Assert.NotNull(style.Value.Font);
    }

    /// <summary>
    /// The reading a screen gives is the middle of its labels and not the first one the
    /// walk reached.
    ///
    /// The failure this exists for is a real one: the run screens carry the version
    /// overlay, whose rows are the smallest text the client draws, and several carry a
    /// banner, which is the largest. Either would be the first found depending on where
    /// it sits, and either would size the whole of Runmobile wrongly.
    /// </summary>
    [Fact]
    public void TheScreensOrdinaryTextIsTheMiddleOfItsLabelsRatherThanTheFirstFound()
    {
        var screen = new Control();
        foreach (var size in new[] { 11, 24, 22, 23, 48 }) screen.AddChild(Native(size));

        Assert.Equal(23, GameText.On(screen).Size);
    }

    /// <summary>A caller that knows which piece of furniture it is sitting beside takes
    /// that one's own text, not the screen's average: the settings row is drawn at the
    /// size of the settings row above it.</summary>
    [Fact]
    public void OnePieceOfFurnitureAnswersTheRoleInsideIt()
    {
        var button = new Control();
        button.AddChild(Native(19));

        Assert.Equal(19, GameText.Under(button).Size);
    }

    [Fact]
    public void AScreenWithNoNativeTextAtAllFallsBackRatherThanGuessing()
    {
        var screen = new Control();
        screen.AddChild(new Label { Text = "ours" });

        Assert.Equal(GameTextStyle.Fallback, GameText.On(screen));
        Assert.Equal(GameTextStyle.Fallback, GameText.Under(screen));
    }

    /// <summary>The steps above and below the game's own size keep their order, so a
    /// heading is never drawn under a note.</summary>
    [Fact]
    public void TheDerivedRolesStayInOrderAroundTheGamesOwnSize()
    {
        var body = new GameTextStyle(null, 24);

        Assert.True(body.Heading.Size > body.Size);
        Assert.True(body.Supporting.Size < body.Size);
        Assert.True(body.Annotation.Size < body.Supporting.Size);
    }

    /// <summary>
    /// A label squeezed below anything readable is not a role and is not copied.
    ///
    /// The game's own labels shrink to fit their box, down to eight points, so one asked
    /// before its box was laid out reports a size nobody designed - and one role read
    /// wrongly would size a whole Runmobile surface.
    /// </summary>
    [Fact]
    public void ALabelSqueezedBelowReadingIsNotARole()
    {
        Assert.Null(GameText.Of(Native(GameTextStyle.SmallestReadable - 1)));
        Assert.NotNull(GameText.Of(Native(GameTextStyle.SmallestReadable)));

        var screen = new Control();
        screen.AddChild(Native(8));
        screen.AddChild(Native(22));
        screen.AddChild(Native(24));

        Assert.Equal(24, GameText.On(screen).Size);
    }

    /// <summary>A game drawing very small text still leaves Runmobile readable: the
    /// steps below it stop rather than following it down.</summary>
    [Fact]
    public void ADerivedRoleNeverFallsBelowWhatCanBeRead()
    {
        var tiny = new GameTextStyle(null, 8);

        Assert.Equal(GameTextStyle.SmallestReadable, tiny.Annotation.Size);
        Assert.Equal(GameTextStyle.SmallestReadable, tiny.Supporting.Annotation.Size);
    }

    /// <summary>Applying a role sets both entries on the node, because the game carries
    /// no project theme for one to be inherited from.</summary>
    [Fact]
    public void ApplyingARolePutsBothTheFontAndTheSizeOnTheNode()
    {
        var label = new GameTextStyle(new Font(), 21).ApplyTo(new Label());

        Assert.True(label.HasThemeFontOverride(FontEntry));
        Assert.Equal(21, label.GetThemeFontSize(FontSizeEntry, "Label"));
    }

    /// <summary>A role with no font still carries its size: a build whose font could not
    /// be read draws in Godot's own type at the game's own scale, which is worse-looking
    /// and still the right size.</summary>
    [Fact]
    public void ARoleWithNoFontStillSetsTheSize()
    {
        var label = new GameTextStyle(null, 19).ApplyTo(new Label());

        Assert.False(label.HasThemeFontOverride(FontEntry));
        Assert.Equal(19, label.GetThemeFontSize(FontSizeEntry, "Label"));
    }

    /// <summary>A label wearing the game's own font and size, which is how the game sets
    /// them: as overrides on the label itself rather than through a project theme.</summary>
    private static Label Native(int size)
    {
        var label = new Label();
        label.AddThemeFontOverride(FontEntry, new Font());
        label.AddThemeFontSizeOverride(FontSizeEntry, size);
        return label;
    }
}
