using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What the library remembers about a player, and what it deliberately cannot.
///
/// Two things are being pinned here. One is arithmetic: the number in "Continue: play
/// from fight N" is the first fight after the last one played, and it stops rather than
/// naming a fight the recording does not have. The other is a shape: this record holds
/// fight ordinals and nothing that could be resumed from, which is why a test reads the
/// written file back and asserts what is in it rather than only asserting round trips.
/// </summary>
public sealed class RunProgressTests
{
    private const string Run = "native-SEED-20260906-120000";

    /// <summary>A recording whose fights all finished, so every ordinal up to
    /// <paramref name="count"/> has a combat-start boundary behind it.</summary>
    private static IReadOnlyList<int> Fights(int count) => [.. Enumerable.Range(1, count)];

    [Fact]
    public void APlayerWhoHasPlayedNothingContinuesAtTheFirstFight()
    {
        Assert.Equal(1, RunProgress.Empty.ContinueAt(Run, Fights(6)));
        Assert.Empty(RunProgress.Empty.PlayedFrom(Run));
    }

    [Fact]
    public void ContinueIsTheFightAfterTheLastOnePlayed()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 1).WithFightPlayed(Run, 2);

        Assert.Equal(3, progress.ContinueAt(Run, Fights(6)));
    }

    /// <summary>
    /// A player who skipped ahead asked to be where they went, so the next offer
    /// follows them rather than sending them back to fill in a gap.
    /// </summary>
    [Fact]
    public void SkippingAheadMovesContinueAheadRatherThanBackToTheGap()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 5);

        Assert.Equal(6, progress.ContinueAt(Run, Fights(8)));
        Assert.Equal([5], progress.PlayedFrom(Run));
    }

    [Fact]
    public void ThereIsNothingToContinueToPastTheLastFight()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 3);

        Assert.Null(progress.ContinueAt(Run, Fights(3)));
        Assert.Null(RunProgress.Empty.ContinueAt(Run, Fights(0)));
    }

    [Fact]
    public void ProgressThroughOneRunSaysNothingAboutAnother()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 4);

        Assert.Equal(1, progress.ContinueAt("some-other-run", Fights(9)));
    }

    /// <summary>
    /// A fight the recording stopped inside spends an ordinal and proves no boundary,
    /// so the proved set has a hole in it. Continue names the next fight something
    /// proves rather than the next number, which is a fight the entry would refuse.
    /// </summary>
    [Fact]
    public void ContinueNamesAProvedFightRatherThanTheNextNumber()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 1).WithFightPlayed(Run, 2);

        Assert.Equal(4, progress.ContinueAt(Run, [1, 2, 4]));
        Assert.Null(progress.ContinueAt(Run, [1, 2]));
    }

    /// <summary>
    /// "The last one played" means the last one played from <em>this</em> run. A record
    /// written against a longer recording holds an ordinal this one does not prove, and
    /// anchoring on it would put Continue past everything the recording has and take
    /// the row off the screen while proved fights sat unplayed.
    /// </summary>
    [Fact]
    public void AnOrdinalTheRecordingDoesNotProveIsNotWhereThePlayerIs()
    {
        var progress = RunProgress.Empty
            .WithFightPlayed(Run, 1)
            .WithFightPlayed(Run, 3)
            .WithFightPlayed(Run, 99);

        Assert.Equal(2, progress.ContinueAt(Run, [1, 2, 4]));
    }

    [Fact]
    public void RecordingAFightAlreadyPlayedChangesNothing()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 2);

        Assert.Same(progress, progress.WithFightPlayed(Run, 2));
    }

    [Fact]
    public void AFightOrdinalIsNeverZeroAndAlwaysBelongsToARun()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunProgress.Empty.WithFightPlayed(Run, 0));
        Assert.Throws<ArgumentException>(() => RunProgress.Empty.WithFightPlayed(" ", 1));
    }

    /// <summary>
    /// The record survives a round trip, and what is on disk is fight ordinals and a
    /// floor number. A future change that put a run state, a deck or a seed position in
    /// here would fail this test, which is the point of asserting the file rather than
    /// the object.
    /// </summary>
    [Fact]
    public void TheWrittenRecordHoldsOrdinalsAndAFloorAndNothingResumable()
    {
        var written = RunProgress.Empty
            .WithFightPlayed(Run, 1)
            .WithFightPlayed(Run, 3)
            .WithFloorLoaded(Run, 6)
            .Write();

        Assert.Contains(RunProgress.Schema, written, StringComparison.Ordinal);
        Assert.Contains("fights_played", written, StringComparison.Ordinal);
        using var parsed = System.Text.Json.JsonDocument.Parse(written);
        var members = parsed.RootElement.EnumerateObject().Select(member => member.Name).ToList();
        Assert.Equal(["schema", "fights_played", "last_floor"], members);

        var read = RunProgress.Read(written);
        Assert.Equal([1, 3], read.PlayedFrom(Run));
        Assert.Equal(6, read.LastFloorLoaded(Run, [2, 6]));
    }

    [Fact]
    public void NoFileAtAllIsAPlayerWhoHasPlayedNothing()
    {
        Assert.Empty(RunProgress.Read(null).FightsPlayed);
        Assert.Empty(RunProgress.Read("   ").FightsPlayed);
    }

    /// <summary>
    /// A record written by a build this one does not know is refused rather than
    /// interpreted: its <c>fights_played</c> could mean something this build does not
    /// know about, and the caller is the one that decides to forget it.
    /// </summary>
    [Fact]
    public void ARecordFromAnotherSchemaIsRefused()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            RunProgress.Read("""{"schema":"sts2-pilot-trainer/run-progress/v9","fights_played":{}}"""));

        Assert.Contains(RunProgress.Schema, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The last floor a player loaded, which is the browser's Last floor replayed
    /// column and nothing else. Blank until there is one: a run nobody has stood in
    /// says nothing rather than floor zero.
    /// </summary>
    [Fact]
    public void TheLastFloorLoadedIsBlankUntilAPlayerLoadsOne()
    {
        var progress = RunProgress.Empty.WithFloorLoaded(Run, 6);

        Assert.Null(RunProgress.Empty.LastFloorLoaded(Run, [2, 6, 9]));
        Assert.Equal(6, progress.LastFloorLoaded(Run, [2, 6, 9]));
    }

    /// <summary>
    /// It says where they were, not how far they ever got, so loading an earlier floor
    /// replaces a later one.
    /// </summary>
    [Fact]
    public void LoadingAnEarlierFloorReplacesALaterOne()
    {
        var progress = RunProgress.Empty.WithFloorLoaded(Run, 9).WithFloorLoaded(Run, 3);

        Assert.Equal(3, progress.LastFloorLoaded(Run, [3, 9]));
    }

    /// <summary>A floor this recording does not have is not one of this run's: a record
    /// written against a longer recording would otherwise put a floor in the column the
    /// run does not hold.</summary>
    [Fact]
    public void AFloorTheRecordingNoLongerHasIsNotWhereTheyWere()
    {
        var progress = RunProgress.Empty.WithFloorLoaded(Run, 40);

        Assert.Null(progress.LastFloorLoaded(Run, [2, 6, 9]));
    }

    /// <summary>Recording the same floor twice changes nothing, so a caller can decide
    /// whether a write is worth the disk.</summary>
    [Fact]
    public void RecordingTheSameFloorTwiceReturnsTheSameRecord()
    {
        var progress = RunProgress.Empty.WithFloorLoaded(Run, 6);

        Assert.Same(progress, progress.WithFloorLoaded(Run, 6));
    }

    [Fact]
    public void AFloorIsNumberedFromOneTheWayABoundaryIs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunProgress.Empty.WithFloorLoaded(Run, 0));
        Assert.Throws<ArgumentException>(() => RunProgress.Empty.WithFloorLoaded("  ", 1));
    }

    /// <summary>
    /// A version-1 record is read rather than refused, and it names no floor.
    ///
    /// This is the one direction worth the exception: a v1 file's fight ordinals mean
    /// exactly what they mean now, so refusing it would throw away real progress to
    /// avoid inventing a floor - and no floor is invented, because the column is blank
    /// until the player loads one. It is written back as the current schema.
    /// </summary>
    [Fact]
    public void AVersionOneRecordIsReadAndNamesNoFloor()
    {
        var progress = RunProgress.Read(
            "{\"schema\":\"" + RunProgress.SchemaV1 +
            "\",\"fights_played\":{\"" + Run + "\":[1,3]}}");

        Assert.Equal([1, 3], progress.PlayedFrom(Run));
        Assert.Empty(progress.LastFloor);
        Assert.Contains(RunProgress.Schema, progress.Write(), StringComparison.Ordinal);
    }
}
