namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What a directory of recordings holds and which of them a retention policy names,
/// decided on file names alone and without a game or a disk.
///
/// The two properties this has to get right are opposite in direction. It must find
/// every recording the recorder wrote, or a player's disk keeps growing after they
/// asked it not to; and it must find nothing else, because the caller on the other end
/// deletes what comes back.
/// </summary>
public sealed class RecordingLibraryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARecordingIsNamedForItsSeedAndTheMomentItsRunBegan()
    {
        Assert.Equal("native-OJ6QXhNgdg-20260906-120000", RecordingLibrary.Name("OJ-6QXhNgdg", Noon));
    }

    [Fact]
    public void AnUnseededRunStillGetsAName()
    {
        Assert.Equal("native-unseeded-20260906-120000", RecordingLibrary.Name("---", Noon));
    }

    /// <summary>Composing and reading back are the same owner, so the moment a run
    /// began survives the trip through a file name.</summary>
    [Fact]
    public void TheMomentARunBeganIsReadBackOutOfItsName()
    {
        Assert.Equal(
            Noon.UtcDateTime, RecordingLibrary.StartedUtc(RecordingLibrary.Name("OJ-6QXhNgdg", Noon)));
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("native-seed")]
    [InlineData("native--20260906-120000")]
    [InlineData("native-seed-20260906")]
    [InlineData("native-seed-2026-09-06-120000")]
    [InlineData("native-seed-20261306-120000")]
    [InlineData("native-seed-20260906-990000")]
    [InlineData("navegreed-OJ-6QXhNgdg")]
    public void ANameThisBuildDidNotWriteIsNotARecording(string runId)
    {
        Assert.Null(RecordingLibrary.StartedUtc(runId));
    }

    [Fact]
    public void ARecordingsTwoFilesAreOneEntry()
    {
        var index = RecordingLibrary.Index(
            [$"{Older}.journal.jsonl", $"{Older}.replay.json"]);

        var recording = Assert.Single(index);
        Assert.Equal(Older, recording.RunId);
        Assert.Equal([$"{Older}.journal.jsonl", $"{Older}.replay.json"], recording.FileNames);
    }

    /// <summary>A run still being played has a journal and no manifest yet, and is a
    /// recording all the same - the caller counts runs, not files.</summary>
    [Fact]
    public void ARecordingWithNoManifestYetIsStillOne()
    {
        var recording = Assert.Single(RecordingLibrary.Index([$"{Newer}.journal.jsonl"]));

        Assert.Equal(Newer, recording.RunId);
        Assert.Equal([$"{Newer}.journal.jsonl"], recording.FileNames);
    }

    [Fact]
    public void RecordingsComeBackNewestFirstWhateverTheirSeedsSortLike()
    {
        var index = RecordingLibrary.Index(
            [$"{Older}.replay.json", $"{Newest}.replay.json", $"{Newer}.replay.json"]);

        Assert.Equal([Newest, Newer, Older], index.Select(recording => recording.RunId));
    }

    [Fact]
    public void APolicyKeepsTheNewestAndNamesEveryFileOfTheRest()
    {
        var removing = RecordingLibrary.Cull(
            [
                $"{Older}.replay.json", $"{Older}.journal.jsonl",
                $"{Newer}.replay.json", $"{Newer}.journal.jsonl",
                $"{Newest}.replay.json", $"{Newest}.journal.jsonl",
            ],
            keepNewest: 1);

        Assert.Equal([Newer, Older], removing.Select(recording => recording.RunId));
        Assert.Equal(
            [$"{Newer}.journal.jsonl", $"{Newer}.replay.json"], removing[0].FileNames);
    }

    /// <summary>Keeping none is the purge, and it is the same operation.</summary>
    [Fact]
    public void KeepingNoneNamesEveryRecordingThereIs()
    {
        var removing = RecordingLibrary.Cull(
            [$"{Older}.replay.json", $"{Newest}.replay.json"], keepNewest: 0);

        Assert.Equal([Newest, Older], removing.Select(recording => recording.RunId));
    }

    [Fact]
    public void KeepingANegativeNumberNamesNothing()
    {
        Assert.Empty(RecordingLibrary.Cull([$"{Older}.replay.json"], keepNewest: -1));
    }

    [Fact]
    public void APolicyThatKeepsMoreThanThereAreNamesNothing()
    {
        Assert.Empty(RecordingLibrary.Cull([$"{Older}.replay.json"], keepNewest: 50));
    }

    /// <summary>
    /// The property the caller's delete loop rests on. A file the recorder did not
    /// write is never named, whatever it is called and however small the policy is:
    /// a player's own note, a manifest they copied in under somebody else's name, and
    /// this mod's own settings file all stay where they are.
    /// </summary>
    [Fact]
    public void NothingElseInTheDirectoryIsEverNamed()
    {
        var removing = RecordingLibrary.Cull(
            [
                "settings.json",
                "notes.txt",
                "navegreed-OJ-6QXhNgdg.replay.json",
                $"{Older}.map-observation.json",
                $"{Older}.replay.json.bak",
                $"{Older}.replay.json",
            ],
            keepNewest: 0);

        var recording = Assert.Single(removing);
        Assert.Equal([$"{Older}.replay.json"], recording.FileNames);
    }

    /// <summary>
    /// Which names one seed could have produced, so a caller can narrow a directory of
    /// recordings before opening any of them. It is the same knowledge that composes a
    /// name, which is why it lives beside it.
    /// </summary>
    [Fact]
    public void ANameSaysWhichSeedItsRunWasOn()
    {
        var name = RecordingLibrary.Name("SFXT-47K", new DateTimeOffset(2026, 9, 6, 1, 0, 0, TimeSpan.Zero));

        Assert.True(RecordingLibrary.NamesRunOn(name, "SFXT-47K"));
        Assert.True(RecordingLibrary.NamesRunOn(name, "SFXT47K"));
        Assert.False(RecordingLibrary.NamesRunOn(name, "SFXT47"));
        Assert.False(RecordingLibrary.NamesRunOn(name, "SFXT47KK"));
    }

    /// <summary>A file this library did not name is not a recording of any seed, so
    /// narrowing on one never reaches it.</summary>
    [Fact]
    public void ANameThisLibraryDidNotWriteIsOnNoSeed()
    {
        Assert.False(RecordingLibrary.NamesRunOn("navegreed-OJ-6QXhNgdg", "OJ"));
        Assert.False(RecordingLibrary.NamesRunOn("native-AAAA-nonsense", "AAAA"));
    }

    private const string Older = "native-ZZZZ-20260901-010000";
    private const string Newer = "native-AAAA-20260906-010000";
    private const string Newest = "native-MMMM-20260906-020000";
}
