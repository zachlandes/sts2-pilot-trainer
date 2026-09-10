using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The settings row wired to a real disk: what a press does to the files, and what the
/// row says when there is no disk to ask yet.
///
/// <c>MyRunsSettingsRowTests</c> asserts the drawing against facts handed to it, and
/// <c>MyRunsRowTests</c> the words. This is the half neither can reach - that the act a
/// control reports actually reaches the store, and that the standing policy a player
/// just moved is the one the next main menu applies.
/// </summary>
public sealed class MyRunsSettingsTests : IDisposable
{
    private const string Recordings = "recordings";
    private const float Width = 520f;
    private const string Older = "native-ZZZZ-20260901-010000";
    private const string Newest = "native-MMMM-20260906-020000";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-row-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public MyRunsSettingsTests()
    {
        // Reading and applying a policy log through the game's own logger, so the game
        // assembly has to be resolvable. Same reason RecordingRetentionTests does this.
        _ = EngineHost.StartupPhase();

        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
        ContinuableRun.UseReaderForTesting(() => null);
        RecordingRetention.ForgetForTesting();
    }

    public void Dispose()
    {
        RecordingRetention.ForgetForTesting();
        ContinuableRun.UseReaderForTesting(null);
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    /// <summary>
    /// The row's promise is kept by the process the player is in, not by the next one.
    ///
    /// Retention applies a profile's policy once per process and this profile's turn is
    /// already taken by the time the settings screen can be reached, so a policy lowered
    /// on the row has to re-arm it. Without that, the row says a run will go at the next
    /// main menu and none does until the game is restarted.
    /// </summary>
    [Fact]
    public void APolicyLoweredOnTheRowIsAppliedAtTheNextMainMenu()
    {
        WriteSettings(keep: 2);
        Record(Older);
        Record(Newest);

        RecordingRetention.ApplyOnce();
        Assert.Equal(4, RunmobileStore.ListFileNames(Recordings).Count);

        var row = MyRunsSettings.Build(Width, Text());
        row.Fewer.EmitPressed();

        Assert.Equal("1", Label(row, "KeepNumeral").Text);
        Assert.Equal(
            "1 older run will be removed at the main menu · user://Runmobile/recordings",
            Label(row, "Detail").Text);

        RecordingRetention.ApplyOnce();

        Assert.Equal(
            [$"{Newest}.journal.jsonl", $"{Newest}.replay.json"],
            RunmobileStore.ListFileNames(Recordings));
    }

    /// <summary>Moving the policy removes nothing where it stands. A standing policy
    /// acts at the main menu, and a screen that deleted files as the number moved would
    /// be performing the act it is describing.</summary>
    [Fact]
    public void MovingThePolicyRemovesNothingThereAndThen()
    {
        WriteSettings(keep: 2);
        Record(Older);
        Record(Newest);

        var row = MyRunsSettings.Build(Width, Text());
        row.Fewer.EmitPressed();

        Assert.Equal(4, RunmobileStore.ListFileNames(Recordings).Count);
        Assert.Equal("2 runs · 1 KB", Label(row, "Reading").Text);
    }

    [Fact]
    public void SettingsTextReadsEachMappedRoleFromTheLiveScreen()
    {
        var screen = new NSettingsScreen();
        var entry = new MarginContainer();
        var rowLabel = Native("Label", 28);
        var button = new Control { Name = "ModdingButton" };
        var buttonLabel = Native("%ModdingButton/Label", 22);
        var numeral = Native(
            "ScrollContainer/Mask/Clipper/GeneralSettings/VBoxContainer/Screenshake/Paginator/LabelContainer/Mask/Label",
            27);
        var reading = Native(
            "ScrollContainer/Mask/Clipper/SoundSettings/VBoxContainer/MasterVolume/MasterVolumeSlider/SliderValue",
            26);
        entry.AddChild(rowLabel);
        entry.AddChild(button);
        screen.AddChild(buttonLabel);
        screen.AddChild(numeral);
        screen.AddChild(reading);
        screen.AddChild(entry);

        var text = MyRunsSettings.NativeText(screen, button, new GameTextStyle(null, 24));

        Assert.Equal(28, text.Row.Size);
        Assert.Equal(27, text.Numeral.Size);
        Assert.Equal(26, text.Reading.Size);
        Assert.Equal(24, text.Detail.Size);
        Assert.Equal(22, text.Button.Size);
    }

    [Fact]
    public void TheProductionSettingsHostShowsAndPersistsTheFetchControl()
    {
        var column = new VBoxContainer { Size = new Vector2(Width, 400f) };
        var entry = new MarginContainer { Name = "Modding", Size = new Vector2(Width, 30f) };
        var modding = new Control { Name = "ModdingButton", Size = new Vector2(Width, 30f) };
        entry.AddChild(modding);
        column.AddChild(entry);

        var row = MyRunsSettings.Attach(modding, Text());

        Assert.Same(column, row.Root.GetParent());
        Assert.Equal("Runmobile · Show community runs: on", row.Fetch.Text);
        row.Fetch.EmitPressed();
        Assert.False(RunmobileSettings.Read().FetchRunIndex);
        Assert.Equal("Runmobile · Show community runs: off", row.Fetch.Text);
    }

    /// <summary>
    /// The shape the retail client actually has, and the one the row was drawn on top of
    /// the game's own heading in.
    ///
    /// <c>%ModdingButton</c> sits in a <c>MarginContainer</c> beside the "Modding" label,
    /// and that container is one entry in the column's <c>VBoxContainer</c>. A
    /// MarginContainer lays every child out in the same rectangle, so a row added there is
    /// a third thing drawn over the first two rather than a third row - which is what the
    /// client showed. The row belongs in the column, directly after the entry, where the
    /// VBoxContainer stacks it from its own minimum size and moves what follows down.
    /// </summary>
    [Fact]
    public void InTheClientsOwnShapeTheRowGoesInTheColumnAndNotTheModdingEntry()
    {
        var column = new VBoxContainer { Size = new Vector2(Width, 400f) };
        var entry = new MarginContainer { Name = "Modding", Size = new Vector2(Width, 64f) };
        var modding = new Button { Name = "ModdingButton", Size = new Vector2(Width, 64f) };
        var credits = new Control { Name = "Credits", Size = new Vector2(Width, 64f) };
        entry.AddChild(modding);
        column.AddChild(entry);
        column.AddChild(credits);

        var row = MyRunsSettings.Attach(modding, Text());

        Assert.Same(column, row.Root.GetParent());
        Assert.NotSame(entry, row.Root.GetParent());
        Assert.Equal(entry.GetIndex() + 1, row.Root.GetIndex());
        // And ahead of what the column already had below the modding entry, so the row
        // does not land past the credits it is supposed to push down.
        Assert.True(row.Root.GetIndex() < credits.GetIndex());
        // The block carries its own height, which is what a VBoxContainer lays out from.
        Assert.Equal(row.Height, row.Root.CustomMinimumSize.Y);
    }

    /// <summary>
    /// The row is laid out at the settings column, not at the button it hangs off.
    ///
    /// This is the defect the retail client found. The game's modding entry point is a
    /// button a fraction of the column's width, and taking its width fitted only by
    /// arithmetic: at the size the row used to write down, its stepper took half of that
    /// and the label just fitted in the rest. Drawn at the settings screen's own size the
    /// stepper takes the whole of it, the label clips mid-word, and the destructive
    /// control is pushed back across the row onto the game's own label.
    /// </summary>
    [Fact]
    public void TheRowIsLaidOutAtTheSettingsColumnRatherThanAtTheButtonItHangsOff()
    {
        var column = new VBoxContainer { Size = new Vector2(Width, 400f) };
        var entry = new MarginContainer { Name = "Modding", Size = new Vector2(Width, 30f) };
        var modding = new Control { Name = "ModdingButton", Size = new Vector2(Width, 30f) };
        entry.AddChild(modding);
        column.AddChild(entry);

        var row = MyRunsSettings.Attach(modding, Text(26));

        Assert.Equal(Width, row.Root.Size.X);
        // The label keeps most of the row whatever the text grows to, rather than being
        // squeezed out by the controls beside it.
        Assert.True(
            Label(row, "KeepLabel").Size.X > Width / 2f,
            $"the keep label got {Label(row, "KeepLabel").Size.X} of {Width}");
        // And the destructive control stays at the row's right-hand end, inside it.
        // Its right edge rather than its left: the control sizes to its own caption, and
        // that caption now names the mod it belongs to, so where its left edge falls is a
        // reading of the words rather than of the layout.
        Assert.Equal(Width, row.Remove.Position.X + row.Remove.Size.X);
        Assert.True(
            row.Remove.Position.X >= 0f,
            $"the remove control started at {row.Remove.Position.X} of {Width}");
        // Immediately after the game's own modding row, which is where it belongs in the
        // column rather than hung in the gap under a button.
        Assert.Equal(modding.GetIndex() + 1, row.Root.GetIndex());
    }

    /// <summary>
    /// A row built before the screen was laid out takes the column once it is.
    ///
    /// A settings screen has not been laid out when its <c>_Ready</c> runs: every
    /// control still carries the size its scene was saved at. The row is built from that
    /// and laid out again a frame later, which is the only moment the column's width
    /// exists. Without it the row keeps a width that was never the answer and clips its
    /// own words - which is what the retail client showed.
    /// </summary>
    [Fact]
    public void ARowBuiltBeforeTheScreenWasLaidOutTakesTheColumnOnceItIs()
    {
        var row = MyRunsSettings.Build(120f, Text(26));
        var narrowReading = Label(row, "Reading").Size.X;
        var narrowDetail = Label(row, "Detail").Size.X;

        // Drive the resize announcement the host container makes rather than calling
        // the row's layout mechanism directly.
        row.Root.Size = new Vector2(Width, row.Root.Size.Y);

        Assert.Equal(Width, Label(row, "Reading").Size.X);
        Assert.Equal(Width, Label(row, "Detail").Size.X);
        Assert.True(Label(row, "Reading").Size.X > narrowReading);
        Assert.True(Label(row, "Detail").Size.X > narrowDetail);
        Assert.Equal(Width, row.Remove.Position.X + row.Remove.Size.X);
        Assert.Equal(Width, row.Fetch.Position.X + row.Fetch.Size.X);
        Assert.Equal(Width, row.MainMenu.Position.X + row.MainMenu.Size.X);
    }

    /// <summary>
    /// The row never asks its host for width, only for height.
    ///
    /// A container gives a child at least its minimum, so a row that asked for a width
    /// would widen the game's own settings list to match. It did exactly that in the
    /// retail client and dragged every one of the game's rows out to the edge of the
    /// screen. Height is the row's to ask for; width is the column's to give.
    /// </summary>
    [Fact]
    public void TheRowAsksItsHostForHeightAndNeverForWidth()
    {
        var row = MyRunsSettings.Build(120f, Text(26));

        row.Relayout(Width);

        Assert.Equal(0f, row.Root.CustomMinimumSize.X);
        Assert.True(row.Root.CustomMinimumSize.Y > 0f, "the row asked for no height");
        Assert.Equal(Width, row.Root.Size.X);
    }

    /// <summary>
    /// A disk that cannot be asked yet is a row rather than an exception.
    ///
    /// The settings section hangs off the main menu's own modding entry point, which is
    /// reachable before a save profile is chosen, and the store refuses until one is -
    /// there is no answer yet to whose runs these are. The row says so in one line and
    /// offers nothing, because every control here writes into a file it cannot name.
    ///
    /// The store's refusal is raised here rather than left to this process's own
    /// SaveManager: another class in this assembly builds one with a profile on it, so
    /// a test that waited for the real no-profile state would assert nothing depending
    /// on what ran first.
    /// </summary>
    [Fact]
    public void WithNoSaveProfileChosenTheRowSaysSoAndOffersNothing()
    {
        RunmobileStore.UseRootProviderForTesting(
            () => throw new StoreNotReadyException(
                "This game has not chosen a save profile yet, so Runmobile cannot tell whose files these " +
                "would be."));

        var row = MyRunsSettings.Build(Width, Text());

        Assert.Equal("Your runs are read once you have chosen a save profile", Label(row, "Reading").Text);
        Assert.Equal(string.Empty, Label(row, "Detail").Text);
        Assert.True(row.Fewer.Disabled);
        Assert.True(row.More.Disabled);
        Assert.True(row.Remove.Disabled);
    }

    /// <summary>
    /// A store that refused for any other reason names no cause.
    ///
    /// The save-profile line is the one failure this row may name, because it is the one
    /// a player resolves by choosing a profile. A fault told as that line would be a
    /// sentence that is false about a state they cannot act on.
    /// </summary>
    [Fact]
    public void AStoreThatRefusedForAnyOtherReasonNamesNoCause()
    {
        RunmobileStore.UseRootProviderForTesting(() => string.Empty);

        var row = MyRunsSettings.Build(Width, Text());

        Assert.Equal("Your runs could not be read; the game's log says why", Label(row, "Reading").Text);
        Assert.Equal(string.Empty, Label(row, "Detail").Text);
        Assert.True(row.Fewer.Disabled);
        Assert.True(row.More.Disabled);
        Assert.True(row.Remove.Disabled);
    }

    /// <summary>
    /// A game that cannot say which run it can continue is a disk that would not read,
    /// not a pending count guessed at without it.
    /// </summary>
    [Fact]
    public void AGameThatCannotSayWhichRunItCanContinueRefusesTheReading()
    {
        WriteSettings(keep: 1);
        Record(Older);
        Record(Newest);
        ContinuableRun.UseReaderForTesting(
            () => throw new InvalidOperationException("This game has a saved run it could not read."));

        var row = MyRunsSettings.Build(Width, Text());

        Assert.Equal("Your runs could not be read; the game's log says why", Label(row, "Reading").Text);
        Assert.True(row.Remove.Disabled);
    }

    /// <summary>
    /// The row predicts what the next main menu will actually do. Retention never
    /// removes the run the game can continue, so a hand-written policy of zero over
    /// three runs takes two of them and the row says two.
    /// </summary>
    [Fact]
    public void AStandingPurgeThatWillLeaveTheContinuableRunPredictsOneFewer()
    {
        WriteSettings(keep: 0);
        Record(Older);
        Record(Newest);
        ContinuableRun.UseReaderForTesting(() => RecordingLibrary.Index([$"{Newest}.journal.jsonl"])[0].StartedUtc);

        var row = MyRunsSettings.Build(Width, Text());

        Assert.Equal(
            "1 older run will be removed at the main menu · user://Runmobile/recordings",
            Label(row, "Detail").Text);

        RecordingRetention.ApplyOnce();

        Assert.Equal(
            [$"{Newest}.journal.jsonl", $"{Newest}.replay.json"],
            RunmobileStore.ListFileNames(Recordings));
    }

    private static MyRunsSettingsText Text(int rowSize = 16, int buttonSize = 16) =>
        new(
            new GameTextStyle(null, rowSize),
            new GameTextStyle(null, rowSize),
            new GameTextStyle(null, rowSize),
            new GameTextStyle(null, rowSize),
            new GameTextStyle(null, buttonSize));

    /// <summary>
    /// The scroll extent's owner is reachable from where the row is put.
    ///
    /// The settings screen's scroll limit is the <c>NSettingsPanel</c>'s own
    /// <c>Size</c>, written only by that panel's <c>RefreshSize</c>. A row added to the
    /// column inside it has to reach the panel to have that measured again, and it finds
    /// it by walking up rather than by a written path - the row is placed relative to the
    /// game's own modding entry, so a path from the screen would be a second statement of
    /// where that entry lives.
    /// </summary>
    [Fact]
    public void TheRowFindsTheScrolledPanelItWasAddedInside()
    {
        var panel = new NSettingsPanel { Name = "GeneralSettings" };
        var column = new VBoxContainer { Name = "VBoxContainer" };
        var entry = new MarginContainer { Name = "Modding" };
        var modding = new Control { Name = "ModdingButton" };
        panel.AddChild(column);
        column.AddChild(entry);
        entry.AddChild(modding);

        Assert.Same(panel, MyRunsSettings.HostPanel(modding));
        Assert.Same(panel, MyRunsSettings.HostPanel(column));
        Assert.Same(panel, MyRunsSettings.HostPanel(panel));
        Assert.Null(MyRunsSettings.HostPanel(new VBoxContainer()));
        Assert.Null(MyRunsSettings.HostPanel(null));
    }

    /// <summary>
    /// This build's settings panel still declares the command that measures it.
    ///
    /// Nothing here computes a scroll extent: the panel measures its own column, and the
    /// mod asks it to by name. A game build that renamed that command would leave the ask
    /// silent and the last General settings out of reach again, so the borrowed member is
    /// held to this build the way every other one is.
    /// </summary>
    [Fact]
    public void ThisBuildsSettingsPanelStillDeclaresTheCommandThatMeasuresIt()
    {
        Assert.NotNull(typeof(NSettingsPanel).GetMethod(
            "RefreshSize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly));
        Assert.Equal("RefreshSize", NSettingsPanel.MethodName.RefreshSize.ToString());
    }

    /// <summary>
    /// Adding the row has the panel that owns the scroll extent measure itself again.
    ///
    /// <para>This is the defect. <c>NSettingsTabManager</c> hands the whole
    /// <c>NSettingsPanel</c> to the screen's scroll container as its content, and the
    /// container's bottom limit is that panel's own <c>Size</c>. The panel writes it in
    /// <c>RefreshSize</c>, from its column's minimum height, at its own <c>_Ready</c> -
    /// which Godot runs before the screen's, so before the postfix that adds this row.
    /// The extent stayed short by the row's height: the scrollbar reached its own bottom
    /// with View Credits and the settings under it still below the fold, and a drag past
    /// the limit was lerped back on release.</para>
    ///
    /// <para>The ask is what is asserted, because there is no engine here to answer it.
    /// That this build's panel still answers to that name is the test above.</para>
    /// </summary>
    [Fact]
    public void AddingTheRowHasTheScrolledPanelMeasureItsColumnAgain()
    {
        var panel = new NSettingsPanel { Name = "GeneralSettings" };
        var column = new VBoxContainer { Name = "VBoxContainer", Size = new Vector2(Width, 400f) };
        var entry = new MarginContainer { Name = "Modding", Size = new Vector2(Width, 30f) };
        var modding = new Control { Name = "ModdingButton", Size = new Vector2(Width, 30f) };
        panel.AddChild(column);
        column.AddChild(entry);
        entry.AddChild(modding);

        MyRunsSettings.Attach(modding, Text());

        Assert.Contains(NSettingsPanel.MethodName.RefreshSize.ToString(), panel.CalledMethods);
    }

    /// <summary>A settings screen with no such panel is left alone rather than refused:
    /// the row is still the player's, and a build whose settings list is not a scrolled
    /// panel has no extent to measure.</summary>
    [Fact]
    public void AColumnWithNoScrolledPanelAboveItIsLeftAlone()
    {
        var column = new VBoxContainer { Size = new Vector2(Width, 400f) };
        var entry = new MarginContainer { Name = "Modding", Size = new Vector2(Width, 30f) };
        var modding = new Control { Name = "ModdingButton", Size = new Vector2(Width, 30f) };
        entry.AddChild(modding);
        column.AddChild(entry);

        var row = MyRunsSettings.Attach(modding, Text());

        Assert.Same(column, row.Root.GetParent());
    }

    /// <summary>Every control Runmobile puts in the game's own settings list says whose
    /// it is. They are drawn in the game's own type among the game's own rows, so
    /// nothing else on the screen does.</summary>
    [Fact]
    public void EveryControlRunmobileAddsNamesTheModItBelongsTo()
    {
        var row = MyRunsSettings.Build(Width, Text());

        Assert.StartsWith("Runmobile · ", Label(row, "KeepLabel").Text, StringComparison.Ordinal);
        Assert.StartsWith("Runmobile · ", row.Remove.Text, StringComparison.Ordinal);
        Assert.StartsWith("Runmobile · ", row.Fetch.Text, StringComparison.Ordinal);
        Assert.StartsWith("Runmobile · ", row.MainMenu.Text, StringComparison.Ordinal);
    }

    private static Label Native(string name, int size)
    {
        var label = new Label { Name = name };
        label.AddThemeFontOverride("font", new Font());
        label.AddThemeFontSizeOverride("font_size", 17);
        label.Set("AutoSizeEnabled", true);
        label.Set("MaxFontSize", size);
        return label;
    }

    private static void WriteSettings(int keep) =>
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":{{keep}}}""");

    private static void Record(string runId)
    {
        RunmobileStore.Write($"{Recordings}/{runId}{RunJournal.FileExtension}", "{}");
        RunmobileStore.Write($"{Recordings}/{runId}{RecordingLibrary.ManifestExtension}", "{}");
    }

    private static Label Label(MyRunsSettingsRow row, string name) =>
        row.Root.GetChildren().OfType<Label>().Single(label => label.Name == name);
}
