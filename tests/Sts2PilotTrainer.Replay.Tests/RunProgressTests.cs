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

    [Fact]
    public void APlayerWhoHasPlayedNothingContinuesAtTheFirstFight()
    {
        Assert.Equal(1, RunProgress.Empty.ContinueAt(Run, fightCount: 6));
        Assert.Empty(RunProgress.Empty.PlayedFrom(Run));
    }

    [Fact]
    public void ContinueIsTheFightAfterTheLastOnePlayed()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 1).WithFightPlayed(Run, 2);

        Assert.Equal(3, progress.ContinueAt(Run, fightCount: 6));
    }

    /// <summary>
    /// A player who skipped ahead asked to be where they went, so the next offer
    /// follows them rather than sending them back to fill in a gap.
    /// </summary>
    [Fact]
    public void SkippingAheadMovesContinueAheadRatherThanBackToTheGap()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 5);

        Assert.Equal(6, progress.ContinueAt(Run, fightCount: 8));
        Assert.Equal([5], progress.PlayedFrom(Run));
    }

    [Fact]
    public void ThereIsNothingToContinueToPastTheLastFight()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 3);

        Assert.Null(progress.ContinueAt(Run, fightCount: 3));
        Assert.Null(RunProgress.Empty.ContinueAt(Run, fightCount: 0));
    }

    [Fact]
    public void ProgressThroughOneRunSaysNothingAboutAnother()
    {
        var progress = RunProgress.Empty.WithFightPlayed(Run, 4);

        Assert.Equal(1, progress.ContinueAt("some-other-run", fightCount: 9));
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
    /// The record survives a round trip, and what is on disk is fight ordinals. A
    /// future change that put a run state, a deck or a seed position in here would
    /// fail this test, which is the point of asserting the file rather than the object.
    /// </summary>
    [Fact]
    public void TheWrittenRecordHoldsFightOrdinalsAndNothingResumable()
    {
        var written = RunProgress.Empty.WithFightPlayed(Run, 1).WithFightPlayed(Run, 3).Write();

        Assert.Contains(RunProgress.Schema, written, StringComparison.Ordinal);
        Assert.Contains("fights_played", written, StringComparison.Ordinal);
        using var parsed = System.Text.Json.JsonDocument.Parse(written);
        var members = parsed.RootElement.EnumerateObject().Select(member => member.Name).ToList();
        Assert.Equal(["schema", "fights_played"], members);
        Assert.Equal([1, 3], RunProgress.Read(written).PlayedFrom(Run));
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
}
