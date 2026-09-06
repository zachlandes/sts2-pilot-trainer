using System.Text.Json.Nodes;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The player's own answer about their disk, carried out against a real directory.
///
/// The library decides which recordings a policy names and is tested on names alone;
/// this is the other half - that the files named are the files removed, that nothing
/// else in the directory is touched, and that a purge is an act that finishes rather
/// than a state that repeats.
/// </summary>
public sealed class RecordingRetentionTests : IDisposable
{
    private const string Recordings = "recordings";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-retention-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public RecordingRetentionTests()
    {
        // Applying a policy logs through the game's own logger, so the game assembly
        // has to be resolvable. Same reason RunmobileSettingsTests does this.
        _ = EngineHost.StartupPhase();

        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
        RecordingRetention.ForgetForTesting();
    }

    public void Dispose()
    {
        RecordingRetention.ForgetForTesting();
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [Fact]
    public void APolicyKeepsTheNewestRunsAndRemovesBothFilesOfTheRest()
    {
        Record(Older);
        Record(Newer);
        Record(Newest);

        Assert.Equal(2, RecordingRetention.Apply(Settings(keep: 1)));

        Assert.Equal(
            [$"{Newest}.journal.jsonl", $"{Newest}.replay.json"], RunmobileStore.ListFileNames(Recordings));
    }

    [Fact]
    public void APolicyWithRoomForEverythingRemovesNothing()
    {
        Record(Older);
        Record(Newest);

        Assert.Equal(0, RecordingRetention.Apply(Settings(keep: 50)));
        Assert.Equal(4, RunmobileStore.ListFileNames(Recordings).Count);
    }

    /// <summary>Removing nothing is the internal fallback for a settings file this
    /// build could not read, and there is no way to ask the file for it.</summary>
    [Fact]
    public void KeepingEveryRunRemovesNothing()
    {
        Record(Older);

        Assert.Equal(0, RecordingRetention.Apply(Settings(keep: RunmobileSettings.KeepEveryRun)));
        Assert.Equal(2, RunmobileStore.ListFileNames(Recordings).Count);
    }

    [Fact]
    public void APurgeRemovesEveryRecordedRun()
    {
        Record(Older);
        Record(Newer);
        Record(Newest);

        Assert.Equal(3, RecordingRetention.Apply(Settings(keep: 50, purge: true)));
        Assert.Empty(RunmobileStore.ListFileNames(Recordings));
    }

    /// <summary>
    /// A purge is a thing a player does, not a state they are left in: it is honoured
    /// once and the request is written back off, so the next launch is an ordinary one.
    /// </summary>
    [Fact]
    public void APurgeClearsItsOwnRequestAndDoesNotRunAgain()
    {
        WriteSettings(keep: 50, purge: true);
        Record(Older);

        RecordingRetention.Apply(RunmobileSettings.Read());
        Assert.False(RunmobileSettings.Read().PurgeMyRuns);
        Assert.Equal(50, RunmobileSettings.Read().KeepRecentRuns);

        Record(Newer);
        Assert.Equal(0, RecordingRetention.Apply(RunmobileSettings.Read()));
        Assert.Equal(2, RunmobileStore.ListFileNames(Recordings).Count);
    }

    /// <summary>
    /// The property the whole thing rests on. Everything removed came back from the
    /// library as part of a recording this build recognises, so a player's own file in
    /// that directory survives the most aggressive answer there is.
    /// </summary>
    [Fact]
    public void APurgeLeavesEverythingThatIsNotARecordedRun()
    {
        Record(Older);
        RunmobileStore.Write($"{Recordings}/notes.txt", "mine");
        RunmobileStore.Write($"{Recordings}/navegreed-OJ-6QXhNgdg.replay.json", "{}");
        RunmobileStore.Write(RunmobileSettings.FileName, "{}");

        RecordingRetention.Apply(Settings(keep: 0, purge: true));

        Assert.Equal(
            ["navegreed-OJ-6QXhNgdg.replay.json", "notes.txt"], RunmobileStore.ListFileNames(Recordings));
        Assert.True(RunmobileStore.Exists(RunmobileSettings.FileName));
    }

    [Fact]
    public void APolicyWithNothingRecordedYetIsNotAFailure()
    {
        Assert.Equal(0, RecordingRetention.Apply(Settings(keep: 0)));
    }

    /// <summary>A run whose game stopped before the manifest was written is a run all
    /// the same, and the count is of runs rather than of files.</summary>
    [Fact]
    public void ARunWithOnlyAJournalCountsAsOne()
    {
        RunmobileStore.Write($"{Recordings}/{Older}{RunJournal.FileExtension}", "{}");
        Record(Newest);

        Assert.Equal(1, RecordingRetention.Apply(Settings(keep: 1)));
        Assert.Equal(
            [$"{Newest}.journal.jsonl", $"{Newest}.replay.json"], RunmobileStore.ListFileNames(Recordings));
    }

    /// <summary>Applied once for the profile it ran against, and latched only once it
    /// has run - so a moment where the store could not answer is retried at the next
    /// one.</summary>
    [Fact]
    public void ItIsAppliedOncePerProfile()
    {
        WriteSettings(keep: 0);
        Record(Older);

        RecordingRetention.ApplyOnce();
        Assert.Empty(RunmobileStore.ListFileNames(Recordings));

        Record(Newer);
        RecordingRetention.ApplyOnce();
        Assert.Equal(2, RunmobileStore.ListFileNames(Recordings).Count);
    }

    /// <summary>
    /// Two save profiles do not share a library, so being answered in one is not being
    /// answered in the other: a purge written in the profile a player switches to is
    /// carried out against that profile's own recordings, and the first profile's
    /// files are left where they are.
    /// </summary>
    [Fact]
    public void EachProfileGetsItsOwnPolicy()
    {
        var second = Path.Combine(Path.GetDirectoryName(_root)!, "profile2");
        Directory.CreateDirectory(second);

        WriteSettings(keep: 50);
        Record(Older);
        RecordingRetention.ApplyOnce();

        RunmobileStore.UseRootForTesting(second);
        WriteSettings(keep: 50, purge: true);
        Record(Older);
        Record(Newest);

        RecordingRetention.ApplyOnce();

        Assert.Empty(RunmobileStore.ListFileNames(Recordings));
        Assert.False(RunmobileSettings.Read().PurgeMyRuns);

        RunmobileStore.UseRootForTesting(_root);
        Assert.Equal(2, RunmobileStore.ListFileNames(Recordings).Count);
    }

    /// <summary>
    /// A purge writes back the one member it honoured. Every other member is the
    /// player's own text, refused values included: a policy this build will not apply
    /// is still a sentence they wrote, and normalising it onto disk would make the mod
    /// a writer of the whole file.
    /// </summary>
    [Fact]
    public void APurgeLeavesEveryOtherMemberOfTheFileAsThePlayerWroteIt()
    {
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":-1,"purge_my_runs":true}""");
        Record(Older);

        RecordingRetention.Apply(RunmobileSettings.Read());

        var written = JsonNode.Parse(RunmobileStore.Read(RunmobileSettings.FileName)!)!.AsObject();
        Assert.Equal(-1, (int)written["keep_recent_runs"]!);
        Assert.False((bool)written["purge_my_runs"]!);
        Assert.Empty(RunmobileStore.ListFileNames(Recordings));
    }

    /// <summary>
    /// The player's disk is not the engine layer's to hold hostage. This process is
    /// not a running game, so the shell refuses to adopt it - and a purge the player
    /// asked for is carried out anyway, with the request written back off, because
    /// what retention needs is a profile the store can name rather than a game this
    /// mod can read.
    /// </summary>
    [Fact]
    public void APurgeIsHonouredEvenWhereTheGameCouldNotBeAdopted()
    {
        WriteSettings(keep: 50, purge: true);
        Record(Older);
        Record(Newest);

        Assert.False(RunmobileMod.EnsureAdopted());

        Assert.Empty(RunmobileStore.ListFileNames(Recordings));
        Assert.False(RunmobileSettings.Read().PurgeMyRuns);
    }

    /// <summary>The standing policy is not the engine layer's either: the same
    /// unadoptable process still keeps the number of runs the player asked for.</summary>
    [Fact]
    public void APolicyIsEnforcedEvenWhereTheGameCouldNotBeAdopted()
    {
        WriteSettings(keep: 1);
        Record(Older);
        Record(Newer);
        Record(Newest);

        Assert.False(RunmobileMod.EnsureAdopted());

        Assert.Equal(
            [$"{Newest}.journal.jsonl", $"{Newest}.replay.json"], RunmobileStore.ListFileNames(Recordings));
    }

    private static void WriteSettings(int keep, bool purge = false) =>
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","keep_recent_runs":{{keep}},"purge_my_runs":{{(purge ? "true" : "false")}}}""");

    private static RunmobileSettings Settings(int keep, bool purge = false) =>
        RunmobileSettings.Default with { KeepRecentRuns = keep, PurgeMyRuns = purge };

    private static void Record(string runId)
    {
        RunmobileStore.Write($"{Recordings}/{runId}{RunJournal.FileExtension}", "{}");
        RunmobileStore.Write($"{Recordings}/{runId}{RecordingLibrary.ManifestExtension}", "{}");
    }

    private const string Older = "native-ZZZZ-20260901-010000";
    private const string Newer = "native-AAAA-20260906-010000";
    private const string Newest = "native-MMMM-20260906-020000";
}
