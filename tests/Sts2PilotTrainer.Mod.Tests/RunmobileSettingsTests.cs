using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one setting this release has, read out of the store.
///
/// There is no screen for it yet, so the file is the whole of the surface and its
/// rules are the whole of the behaviour: an absent file is the default, a file this
/// build cannot read is reported rather than guessed at, and what the player wrote is
/// what happens.
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
    /// about. It carries on with the defaults and says so rather than refusing to run,
    /// because a recorder that stopped because of a settings file would be a worse
    /// answer than one that told you it could not read it.
    /// </summary>
    [Fact]
    public void ASettingsFileFromAnotherBuildIsReportedRatherThanRead()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            """{"schema":"somebody-elses/settings/v9","record_my_runs":false}""");

        Assert.True(RunmobileSettings.Read().RecordMyRuns);
    }

    [Fact]
    public void SoIsOneThatIsNotJsonAtAll()
    {
        RunmobileStore.Write(RunmobileSettings.FileName, "record_my_runs = false");

        Assert.True(RunmobileSettings.Read().RecordMyRuns);
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
