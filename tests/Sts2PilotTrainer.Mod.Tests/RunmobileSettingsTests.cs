using System.Text.Json.Nodes;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the player told this mod to do, read out of the store.
///
/// There is no screen for it yet, so the file is the whole of the surface and its
/// rules are the whole of the behaviour: an absent file is the default, a file this
/// build cannot read means do nothing rather than being guessed at, and what the
/// player wrote is what happens. Three sentences are sayable in it - record my runs,
/// keep this many, remove them all - and the last is the only member this mod ever
/// writes back.
/// </summary>
public sealed class RunmobileSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-settings-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public RunmobileSettingsTests()
    {
        // Reading a setting logs through the game's own logger, so the game assembly has
        // to be resolvable. This project does not copy it, and it reaches the default
        // context only once something has started the host - left to whichever test ran
        // first, these fail about a third of the time on a cold ordering.
        _ = EngineHost.StartupPhase();

        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [Fact]
    public void APlayerWhoHasNeverTouchedTheFileIsRecording()
    {
        Assert.True(RunmobileSettings.Read().RecordMyRuns);
    }

    [Fact]
    public void APlayerWhoTurnedItOffIsNot()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","record_my_runs":false}""");

        Assert.False(RunmobileSettings.Read().RecordMyRuns);
    }

    /// <summary>
    /// A settings file this build cannot read is not one it may guess at: a newer
    /// writer's <c>record_my_runs</c> could mean something this build does not know
    /// about. It records nothing and says so, because the only thing this file can
    /// say is "off" and a recorder that recorded through a sentence it could not read
    /// would be recording without consent.
    /// </summary>
    [Fact]
    public void ASettingsFileFromAnotherBuildStopsTheRecorderRatherThanBeingRead()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            """{"schema":"somebody-elses/settings/v9","record_my_runs":false}""");

        Assert.False(RunmobileSettings.Read().RecordMyRuns);
    }

    /// <summary>
    /// And so does the file a player writes by hand from the documentation without the
    /// schema member, which is the shape this rule exists for.
    /// </summary>
    [Fact]
    public void SoDoesOneMissingTheSchemaMember()
    {
        RunmobileStore.Write(RunmobileSettings.FileName, """{"record_my_runs":false}""");

        Assert.False(RunmobileSettings.Read().RecordMyRuns);
    }

    [Fact]
    public void SoDoesOneThatIsNotJsonAtAll()
    {
        RunmobileStore.Write(RunmobileSettings.FileName, "record_my_runs = false");

        Assert.False(RunmobileSettings.Read().RecordMyRuns);
    }

    [Fact]
    public void APlayerWhoHasNeverTouchedTheFileKeepsTheirFiftyMostRecentRunsAndPurgesNothing()
    {
        var settings = RunmobileSettings.Read();

        Assert.Equal(RunmobileSettings.DefaultKeepRecentRuns, settings.KeepRecentRuns);
        Assert.False(settings.PurgeMyRuns);
    }

    [Fact]
    public void APlayerWhoWroteAPolicyGetsIt()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":3,"purge_my_runs":true}""");

        var settings = RunmobileSettings.Read();

        Assert.Equal(3, settings.KeepRecentRuns);
        Assert.True(settings.PurgeMyRuns);
        Assert.True(settings.RecordMyRuns);
    }

    /// <summary>
    /// There is no player-facing way to ask for unbounded growth: a negative policy is
    /// refused and the default applied, so the file cannot turn retention off.
    /// </summary>
    [Fact]
    public void ANegativePolicyIsRefusedAndTheDefaultApplied()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":-1,"purge_my_runs":false}""");

        var settings = RunmobileSettings.Read();

        Assert.Equal(RunmobileSettings.DefaultKeepRecentRuns, settings.KeepRecentRuns);
        Assert.True(settings.RecordMyRuns);
        Assert.NotEmpty(RecordingLibrary.Cull(
            [.. Enumerable.Range(0, RunmobileSettings.DefaultKeepRecentRuns + 1).Select(i =>
                RecordingLibrary.Name("seed", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i))
                + RecordingLibrary.ManifestExtension)],
            settings.KeepRecentRuns));
    }

    /// <summary>
    /// A file this build cannot read fails in the direction of doing less, on both
    /// questions: nothing is recorded, and nothing is deleted. A sentence nobody could
    /// read is not somebody asking for their runs to be removed.
    /// </summary>
    [Fact]
    public void ASettingsFileThisBuildCannotReadDeletesNothingEither()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            """{"schema":"somebody-elses/settings/v9","keep_recent_runs":0,"purge_my_runs":true}""");

        var settings = RunmobileSettings.Read();

        Assert.Equal(RunmobileSettings.KeepEveryRun, settings.KeepRecentRuns);
        Assert.False(settings.PurgeMyRuns);
    }

    /// <summary>The one member this mod writes back, so a purge is an act rather than
    /// a state the player is left in - and the only one it touches.</summary>
    [Fact]
    public void ClearingAPurgeWritesBackThatMemberAndNoOther()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":7,"purge_my_runs":true}""");

        RunmobileSettings.ClearPurgeRequest();

        var written = JsonNode.Parse(RunmobileStore.Read(RunmobileSettings.FileName)!)!.AsObject();
        Assert.False((bool)written["purge_my_runs"]!);
        Assert.Equal(7, (int)written["keep_recent_runs"]!);
        Assert.False(RunmobileSettings.Read().PurgeMyRuns);
    }

    /// <summary>The setting is in the store, like everything else this mod writes, so
    /// the protected-files ledger sees it where it sees the rest.</summary>
    [Fact]
    public void TheFileLivesInTheStore()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","record_my_runs":true}""");

        Assert.True(File.Exists(Path.Combine(_root, RunmobileSettings.FileName)));
    }
}
