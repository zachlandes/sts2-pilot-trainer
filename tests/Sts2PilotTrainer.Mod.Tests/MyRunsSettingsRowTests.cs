using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The settings row, assembled node by node in a process with no game.
///
/// Every node it puts up is a stock Godot node, so the whole row can be built here and
/// asked what it drew: the two sentences, the numeral, and which of its three controls
/// a player can reach.
///
/// What is asserted here is the projection and not the words - <c>MyRunsRowTests</c>
/// owns those. The properties this suite exists for are the ones a wrong drawing would
/// break silently: that the row reads what the derivation says rather than working
/// anything out, that its controls report rather than act, and that the number the
/// stepper stands on and the numeral beside it can never be two different answers.
/// </summary>
public sealed class MyRunsSettingsRowTests
{
    private const float Width = 520f;

    [Fact]
    public void TheRowCarriesWhatTheDerivationSaysAndNothingElse()
    {
        var row = Build(new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20));

        Assert.Equal("Keep my runs", Label(row, "KeepLabel").Text);
        Assert.Equal("20", Label(row, "KeepNumeral").Text);
        Assert.Equal("12 runs · 6 MB", Label(row, "Reading").Text);
        Assert.Equal("on this computer, in user://Runmobile/recordings", Label(row, "Detail").Text);
        Assert.Equal("Remove all my runs", row.Remove.Text);
    }

    /// <summary>
    /// Nothing to remove refuses the control rather than hiding it, so the row keeps its
    /// shape as the number changes and nothing moves about under the player's aim.
    /// </summary>
    [Fact]
    public void WithNothingToRemoveTheControlIsDrawnAndRefused()
    {
        var row = Build(new MyRunsFacts(Runs: 0, Bytes: 0, Keep: 20));

        Assert.True(row.Remove.Disabled);
        Assert.True(row.Remove.Visible);
        Assert.Equal(Control.FocusModeEnum.None, row.Remove.FocusMode);
    }

    [Fact]
    public void WithSomethingToRemoveTheControlCanBeReached()
    {
        var row = Build(new MyRunsFacts(Runs: 1, Bytes: 1024, Keep: 20));

        Assert.False(row.Remove.Disabled);
        Assert.Equal(Control.FocusModeEnum.All, row.Remove.FocusMode);
    }

    /// <summary>The row raises what a player asked for and removes nothing itself. What
    /// files a removal names is the retention owner's and always was.</summary>
    [Fact]
    public void PressingRemoveReportsItRatherThanActing()
    {
        var asked = 0;
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20), removePressed: () => asked++);

        row.Remove.EmitPressed();

        Assert.Equal(1, asked);
    }

    [Fact]
    public void FetchSettingReportsTheNewStateAndRefusesWhenSettingsAreUnreadable()
    {
        bool? reported = null;
        var row = Build(
            new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20),
            fetchChanged: value => reported = value);

        row.Fetch.EmitPressed();

        Assert.False(reported);

        row.Apply(MyRunsRow.For(new MyRunsFacts(
            Runs: 0, Bytes: 0, Keep: 20, SettingsReadable: false)), 20, true);
        Assert.True(row.Fetch.Disabled);
        Assert.Equal(Control.FocusModeEnum.None, row.Fetch.FocusMode);
    }

    [Fact]
    public void TheStepperReportsTheNumberOnePressWouldMoveThePolicyTo()
    {
        var reported = new List<int>();
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20), keepChanged: reported.Add);

        row.More.EmitPressed();
        row.Fewer.EmitPressed();

        Assert.Equal([21, 19], reported);
    }

    /// <summary>
    /// The stepper does not move itself. A control that advanced on the press would be
    /// a run ahead of the file the moment a write failed, and would then refuse an end
    /// the policy had never reached.
    /// </summary>
    [Fact]
    public void TheStepperDoesNotMoveUntilTheRowIsToldWhatTheFileNowSays()
    {
        var reported = new List<int>();
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20), keepChanged: reported.Add);

        row.More.EmitPressed();
        row.More.EmitPressed();

        Assert.Equal([21, 21], reported);
        Assert.Equal("20", Label(row, "KeepNumeral").Text);
    }

    [Fact]
    public void TheStepperMovesOnceTheRowIsToldWhatTheFileNowSays()
    {
        var reported = new List<int>();
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20), keepChanged: reported.Add);

        row.More.EmitPressed();
        Apply(row, new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 21));
        row.More.EmitPressed();

        Assert.Equal([21, 22], reported);
        Assert.Equal("21", Label(row, "KeepNumeral").Text);
    }

    /// <summary>The stepper refuses at its bottom and has no top: a policy keeps at
    /// least one run, and how many more than that is the player's own.</summary>
    [Fact]
    public void TheStepperRefusesAtTheBottomOfItsRangeOnly()
    {
        var bottom = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: MyRunsRow.MinimumKeep));
        Assert.True(bottom.Fewer.Disabled);
        Assert.False(bottom.More.Disabled);

        var large = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 500));
        Assert.False(large.Fewer.Disabled);
        Assert.False(large.More.Disabled);
    }

    /// <summary>
    /// A large policy written by hand is shown as the player wrote it and moves by one
    /// from where it stands. Nothing here may quietly rewrite a larger policy into a
    /// smaller one: a press that reported two hundred over a file saying five hundred
    /// would take three hundred runs nobody asked to lose.
    /// </summary>
    [Fact]
    public void ALargePolicyIsShownAsWrittenAndStepsByOne()
    {
        var reported = new List<int>();
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 500), keepChanged: reported.Add);

        Assert.Equal("500", Label(row, "KeepNumeral").Text);

        row.Fewer.EmitPressed();
        row.More.EmitPressed();
        Assert.Equal([499, 501], reported);
    }

    /// <summary>
    /// A settings file this build cannot read refuses every control that would write
    /// into it - both ends of the stepper and the removal, which records its request in
    /// that same file - and a press on any of them raises nothing. The second line is
    /// what says why, so none of the three is a dead control without a reason.
    /// </summary>
    [Fact]
    public void AnUnreadableSettingsFileRefusesEveryControlAndSaysWhy()
    {
        var reported = new List<int>();
        var asked = 0;
        var row = Build(
            new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 50, SettingsReadable: false),
            keepChanged: reported.Add,
            removePressed: () => asked++);

        Assert.True(row.Fewer.Disabled);
        Assert.True(row.More.Disabled);
        Assert.True(row.Remove.Disabled);
        Assert.Equal(
            "settings.json could not be read, so no runs are removed automatically until it is · " +
            "user://Runmobile/recordings",
            Label(row, "Detail").Text);

        row.Fewer.EmitPressed();
        row.More.EmitPressed();
        row.Remove.EmitPressed();
        Assert.Empty(reported);
        Assert.Equal(0, asked);
    }

    /// <summary>
    /// A standing purge written by hand is shown as the zero it is. The control cannot
    /// be moved there - asking for every run to go is the ribbon's job, and it asks
    /// first - but a row that read "1" over a file saying "0" would be misstating the
    /// policy.
    /// </summary>
    [Fact]
    public void AStandingPurgeWrittenByHandIsShownAsZero()
    {
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 0));

        Assert.Equal("0", Label(row, "KeepNumeral").Text);
        Assert.True(row.Fewer.Disabled);
    }

    /// <summary>
    /// The whole row is re-read on every change rather than the element a caller thinks
    /// moved. A receipt arrives with a new reading and a new policy in the same act.
    /// </summary>
    [Fact]
    public void ApplyingAReceiptRedrawsEveryLine()
    {
        var row = Build(new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20));

        Apply(row, new MyRunsFacts(Runs: 0, Bytes: 0, Keep: 20, RemovedJustNow: 12));

        Assert.Equal("0 runs · 0 MB", Label(row, "Reading").Text);
        Assert.Equal("12 runs removed just now · user://Runmobile/recordings", Label(row, "Detail").Text);
        Assert.True(row.Remove.Disabled);
    }

    /// <summary>
    /// The row lets a click through everywhere except on a control, which is what keeps
    /// the section it is hosted in working underneath it.
    /// </summary>
    [Fact]
    public void OnlyTheControlsTakeTheMouse()
    {
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20));

        Assert.Equal(Control.MouseFilterEnum.Ignore, row.Root.MouseFilter);
        foreach (var name in new[] { "KeepLabel", "KeepNumeral", "Reading", "Detail" })
        {
            Assert.Equal(Control.MouseFilterEnum.Ignore, Label(row, name).MouseFilter);
        }
    }

    /// <summary>Its controls take focus, which is what a controller needs to reach
    /// them.</summary>
    [Fact]
    public void TheControlsTakeFocus()
    {
        var row = Build(new MyRunsFacts(Runs: 3, Bytes: 1024, Keep: 20));

        Assert.Equal(Control.FocusModeEnum.All, row.Fewer.FocusMode);
        Assert.Equal(Control.FocusModeEnum.All, row.More.FocusMode);
        Assert.Equal(Control.FocusModeEnum.All, row.Remove.FocusMode);
    }

    /// <summary>The row is laid out inside the width the section gave it, so nothing it
    /// draws runs off the side of the panel hosting it.</summary>
    [Fact]
    public void EveryElementStaysInsideTheWidthItWasGiven()
    {
        var row = Build(new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20));

        foreach (var child in row.Root.GetChildren().OfType<Control>())
        {
            Assert.True(child.Position.X >= 0f, $"{child.Name} starts left of the row");
            Assert.True(
                child.Position.X + child.Size.X <= Width, $"{child.Name} runs past the width it was given");
        }
    }

    /// <summary>
    /// Its text is cut to the box it is in rather than widening the box. A Control is
    /// clamped up to its own minimum size and a Button's minimum is its unwrapped label,
    /// so a control that did not clip would push itself off the side of the row at any
    /// font the player's language happens to need.
    /// </summary>
    [Fact]
    public void EveryElementClipsItsTextRatherThanWideningItself()
    {
        var row = Build(new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20));

        foreach (var button in row.Root.GetChildren().OfType<Button>())
        {
            Assert.True(button.ClipText, $"{button.Name} would widen itself out of the row");
        }
    }

    /// <summary>
    /// The main-menu control states what the menu is doing, because a switch that only
    /// said what it would do next leaves a player guessing which way it is set.
    /// </summary>
    [Fact]
    public void TheMainMenuControlStatesWhatTheMenuIsDoing()
    {
        Assert.Equal(
            "Runmobile on the main menu: on",
            Build(new MyRunsFacts(Runs: 1, Bytes: 1024, Keep: 20, MainMenuRowShown: true)).MainMenu.Text);
        Assert.Equal(
            "Runmobile on the main menu: off",
            Build(new MyRunsFacts(Runs: 1, Bytes: 1024, Keep: 20, MainMenuRowShown: false)).MainMenu.Text);
    }

    /// <summary>It reports the flip and writes nothing itself, like every other control
    /// here.</summary>
    [Fact]
    public void TheMainMenuControlReportsTheFlipAndChangesNothing()
    {
        bool? asked = null;
        var row = Build(
            new MyRunsFacts(Runs: 1, Bytes: 1024, Keep: 20, MainMenuRowShown: true),
            mainMenuChanged: show => asked = show);

        row.MainMenu.EmitPressed();

        Assert.False(asked);
        Assert.Equal("Runmobile on the main menu: on", row.MainMenu.Text);
    }

    /// <summary>
    /// A settings file this build cannot read refuses the switch with the rest of them.
    /// The choice is a member of that file, so a press would write this build's meaning
    /// into a document written by another.
    /// </summary>
    [Fact]
    public void OverAnUnreadableSettingsFileTheMainMenuControlIsRefused()
    {
        var row = Build(
            new MyRunsFacts(Runs: 1, Bytes: 1024, Keep: 20, SettingsReadable: false));

        Assert.True(row.MainMenu.Disabled);
        Assert.True(row.MainMenu.Visible);
        Assert.Equal(Control.FocusModeEnum.None, row.MainMenu.FocusMode);
    }

    /// <summary>The row is as tall as what it draws. A section stacks what it hosts, so
    /// a height taller than the lowest element leaves a gap nothing explains.</summary>
    [Fact]
    public void TheHeightItReportsIsWhatItDraws()
    {
        var row = Build(new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20));

        var lowest = row.Root.GetChildren().OfType<Control>()
            .Max(child => child.Position.Y + child.Size.Y);

        Assert.Equal(row.Height, lowest);
    }

    /// <summary>
    /// The row is drawn at the settings screen's own text size, and its boxes are
    /// measured around it.
    ///
    /// The defect this exists for is the one the whole change is about: a row whose
    /// words were the game's and whose boxes were a set of constants read as cramped on
    /// every window the constants were not measured on, and would clip the moment the
    /// words grew. So both are asserted - the label carries the screen's own size, and
    /// nothing the row draws hangs below the height it reports at that size.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(34)]
    public void TheRowIsDrawnAtTheScreensOwnSizeAndFitsTheHeightItReports(int size)
    {
        var row = Build(
            new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20),
            text: new GameTextStyle(null, size));

        Assert.Equal(size, Label(row, "KeepLabel").GetThemeFontSize("font_size", "Label"));
        Assert.All(
            row.Root.GetChildren().OfType<Control>(),
            child => Assert.True(
                child.Position.Y + child.Size.Y <= row.Height + 0.01f,
                $"'{child.Name}' hangs below the row at {size}pt"));
    }

    /// <summary>A screen drawing larger text gets a taller row rather than the same row
    /// with the words spilling out of it.</summary>
    [Fact]
    public void ARowOnAScreenWithLargerTextIsTaller()
    {
        var facts = new MyRunsFacts(Runs: 12, Bytes: 6 * 1024 * 1024, Keep: 20);

        Assert.True(
            Build(facts, text: new GameTextStyle(null, 30)).Height >
            Build(facts, text: new GameTextStyle(null, 15)).Height);
    }

    private static MyRunsSettingsRow Build(
        MyRunsFacts facts, Action<int>? keepChanged = null, Action? removePressed = null,
        Action<bool>? fetchChanged = null, Action<bool>? mainMenuChanged = null,
        GameTextStyle? text = null) =>
        MyRunsSettingsRow.Build(
            MyRunsRow.For(facts),
            facts.Keep,
            fetchRunIndex: true,
            Width,
            text ?? GameTextStyle.Fallback,
            keepChanged ?? (_ => { }),
            removePressed ?? (() => { }),
            fetchChanged ?? (_ => throw new InvalidOperationException("Unexpected fetch press")),
            mainMenuChanged ?? (_ => throw new InvalidOperationException("Unexpected main-menu press")));

    private static void Apply(MyRunsSettingsRow row, MyRunsFacts facts) =>
        row.Apply(MyRunsRow.For(facts), facts.Keep);

    private static Label Label(MyRunsSettingsRow row, string name) =>
        row.Root.GetChildren().OfType<Label>().Single(label => label.Name == name);
}
