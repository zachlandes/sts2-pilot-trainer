using System.Text.Json.Nodes;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the player told this mod to do, read out of the store.
///
/// The file is the record behind the settings surface: an absent file is the default,
/// a file this build cannot read means do nothing rather than being guessed at, and
/// what the player wrote is what happens. Recording and the sharing-service endpoint
/// remain file-only; the settings row writes the retention, removal, and index-fetch
/// choices where a player moves a control instead of typing.
///
/// Every write here edits the member it names and nothing else, which is the property
/// most of the tests below are about: the rest of the document is the player's own
/// text, refused values included.
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
        RunLibrary.ResetSharedRunsForTesting();
    }

    public void Dispose()
    {
        RunLibrary.ResetSharedRunsForTesting();
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
    public async Task NoSharingServiceConfigurationMakesNoNetworkOperationAvailable()
    {
        Assert.False(RunLibrary.SharingAvailable);
        var fetch = await Assert.ThrowsAsync<ShareValidationException>(
            () => RunLibrary.FetchIndexAsync());
        var lookup = await Assert.ThrowsAsync<ShareValidationException>(
            () => RunLibrary.FindSharedAsync("ABCDEF"));

        Assert.Equal(LibraryCopy.SharingServiceUnavailable, fetch.Message);
        Assert.Equal(LibraryCopy.SharingServiceUnavailable, lookup.Message);
    }

    [Fact]
    public void AnAuthorizedSharingServiceConfigurationEnablesNetworkOperations()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","sharing_service_url":"https://runs.example.test/v1/"}""");

        Assert.True(RunLibrary.SharingAvailable);
    }

    [Theory]
    [InlineData("http://runs.example.test/v1/")]
    [InlineData("https://credential@runs.example.test/v1/")]
    public async Task AnUnauthorizedSharingServiceConfigurationMakesNoNetworkOperationAvailable(
        string endpoint)
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","sharing_service_url":"{{endpoint}}"}""");

        Assert.False(RunLibrary.SharingAvailable);
        await Assert.ThrowsAsync<ShareValidationException>(() => RunLibrary.FetchIndexAsync());
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

    /// <summary>The act a player asks for on the settings row, recorded before anything
    /// is removed so that a game which stops in between finishes at the next main
    /// menu.</summary>
    [Fact]
    public void APurgeCanBeRequestedAndLeavesEveryOtherMemberAlone()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","record_my_runs":false,"keep_recent_runs":7}""");

        RunmobileSettings.RequestPurge();

        var written = JsonNode.Parse(RunmobileStore.Read(RunmobileSettings.FileName)!)!.AsObject();
        Assert.True((bool)written["purge_my_runs"]!);
        Assert.Equal(7, (int)written["keep_recent_runs"]!);
        Assert.False((bool)written["record_my_runs"]!);
        Assert.True(RunmobileSettings.Read().PurgeMyRuns);
    }

    /// <summary>A player who has never written the file can still ask, so the request
    /// is written with the defaults around it rather than refused.</summary>
    [Fact]
    public void ARequestWithNoFileYetWritesTheDefaultsAroundIt()
    {
        RunmobileSettings.RequestPurge();

        var settings = RunmobileSettings.Read();
        Assert.True(settings.PurgeMyRuns);
        Assert.True(settings.RecordMyRuns);
        Assert.Equal(RunmobileSettings.DefaultKeepRecentRuns, settings.KeepRecentRuns);
    }

    [Fact]
    public void ThePolicyCanBeWrittenAndLeavesEveryOtherMemberAlone()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","record_my_runs":false,"purge_my_runs":true}""");

        RunmobileSettings.SetKeepRecentRuns(12);

        var written = JsonNode.Parse(RunmobileStore.Read(RunmobileSettings.FileName)!)!.AsObject();
        Assert.Equal(12, (int)written["keep_recent_runs"]!);
        Assert.True((bool)written["purge_my_runs"]!);
        Assert.False((bool)written["record_my_runs"]!);
    }

    /// <summary>A negative is not a number of runs. The file has an answer for one a
    /// player typed; a caller passing one has a bug.</summary>
    [Fact]
    public void ANegativePolicyIsRefusedRatherThanWritten()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunmobileSettings.SetKeepRecentRuns(-1));
        Assert.Null(RunmobileStore.Read(RunmobileSettings.FileName));
    }

    /// <summary>
    /// A file that is there and is not a settings object is not written over. Reading
    /// one already means "record nothing"; overwriting it would be this mod discarding
    /// something the player wrote in order to store something they meant to add to it.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("not json at all")]
    public void AFileThatIsNotASettingsObjectIsNotWrittenOver(string content)
    {
        RunmobileStore.Write(RunmobileSettings.FileName, content);

        Assert.ThrowsAny<Exception>(RunmobileSettings.RequestPurge);
        Assert.ThrowsAny<Exception>(() => RunmobileSettings.SetKeepRecentRuns(12));
        Assert.Equal(content, RunmobileStore.Read(RunmobileSettings.FileName));
    }

    /// <summary>
    /// A file that will not deserialize is not written into either, whatever its schema
    /// says. <see cref="RunmobileSettings.Read"/> refuses it, and the writer asks the
    /// same reader rather than keeping a second set of rules: a document that is
    /// unreadable to the settings row may not be writable by the control beside it.
    /// Before this, a valid schema over a member of the wrong type let a purge request
    /// be written into a file this build had just declared it could not read.
    /// </summary>
    [Fact]
    public void AFileWhoseMembersDoNotReadIsNotWrittenIntoEither()
    {
        var content = $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":"fifty"}""";
        RunmobileStore.Write(RunmobileSettings.FileName, content);

        Assert.False(RunmobileSettings.Read().Readable);
        Assert.ThrowsAny<Exception>(RunmobileSettings.RequestPurge);
        Assert.ThrowsAny<Exception>(() => RunmobileSettings.SetKeepRecentRuns(12));
        Assert.Equal(content, RunmobileStore.Read(RunmobileSettings.FileName));
    }

    /// <summary>
    /// A file declaring a schema this build does not read is not written into either.
    /// <see cref="RunmobileSettings.Read"/> already refuses it, and editing one member
    /// of it would put this build's meaning of <c>keep_recent_runs</c> or
    /// <c>purge_my_runs</c> into a document written by a build with another - a sentence
    /// neither of them said, in a file this one has declared it cannot read.
    /// </summary>
    [Fact]
    public void AFileThisBuildCannotReadIsNotWrittenIntoEither()
    {
        const string content = """{"schema":"sts2-pilot-trainer/runmobile-settings/v2","keep_recent_runs":7}""";
        RunmobileStore.Write(RunmobileSettings.FileName, content);

        Assert.ThrowsAny<Exception>(RunmobileSettings.RequestPurge);
        Assert.ThrowsAny<Exception>(() => RunmobileSettings.SetKeepRecentRuns(12));
        Assert.Equal(content, RunmobileStore.Read(RunmobileSettings.FileName));
    }

    /// <summary>A file that could be read says so, which is what the settings row shows
    /// the player's own policy from rather than the stand-in.</summary>
    [Fact]
    public void AReadableFileSaysItWasReadAndAnUnreadableOneSaysItWasNot()
    {
        Assert.True(RunmobileSettings.Read().Readable);

        RunmobileStore.Write(
            RunmobileSettings.FileName,
            """{"schema":"sts2-pilot-trainer/runmobile-settings/v2"}""");

        Assert.False(RunmobileSettings.Read().Readable);
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
