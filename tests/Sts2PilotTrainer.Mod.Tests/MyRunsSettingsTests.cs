using Godot;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

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

        var row = MyRunsSettings.Build(Width, font: null);
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

        var row = MyRunsSettings.Build(Width, font: null);
        row.Fewer.EmitPressed();

        Assert.Equal(4, RunmobileStore.ListFileNames(Recordings).Count);
        Assert.Equal("2 runs · 1 KB", Label(row, "Reading").Text);
    }

    /// <summary>
    /// A disk that cannot be asked yet is a row rather than an exception.
    ///
    /// The settings section hangs off the main menu's own modding entry point, which is
    /// reachable before a save profile is chosen, and the store refuses until one is -
    /// there is no answer yet to whose runs these are. The row says so in one line and
    /// offers nothing, because every control here writes into a file it cannot name.
    /// </summary>
    [Fact]
    public void WithNoSaveProfileChosenTheRowSaysSoAndOffersNothing()
    {
        RunmobileStore.UseRootForTesting(null);

        var row = MyRunsSettings.Build(Width, font: null);

        Assert.Equal("Your runs are read once you have chosen a save profile", Label(row, "Reading").Text);
        Assert.Equal(string.Empty, Label(row, "Detail").Text);
        Assert.True(row.Fewer.Disabled);
        Assert.True(row.More.Disabled);
        Assert.True(row.Remove.Disabled);
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
