using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What a player looks at while their run is being restored.
///
/// The wait is the packaged arbiter replaying the recording's history in its own
/// process, and it happens before there is a run for the transport to hang under. So
/// the phase has to answer for itself: pressing Continue on a fight past the first
/// once drew nothing at all for the best part of a minute.
/// </summary>
public sealed class RestoringNoticeTests
{
    [Fact]
    public void PreparingSaysTheRunIsBeingRestored()
    {
        var notice = RestoringNotice.For(JourneyPhase.Preparing);

        Assert.NotNull(notice);
        Assert.Equal("Restoring your run", notice.Headline);
    }

    /// <summary>Every other phase draws none: one of them has the transport, and the
    /// rest have nothing to say. Asked of the whole enum rather than of a list written
    /// here, so a phase added later is answered for by this test rather than falling
    /// past it.</summary>
    [Fact]
    public void NoOtherPhaseDrawsOne()
    {
        var answered = Enum.GetValues<JourneyPhase>()
            .Where(phase => RestoringNotice.For(phase) is not null)
            .ToList();

        Assert.Equal([JourneyPhase.Preparing], answered);
    }
}

/// <summary>
/// What the notice reads while it waits.
///
/// The ellipsis is what says the wait is alive - there is no figure to put beside it -
/// and it starts on the sentence rather than on a dot. The host that draws it only
/// counts up, so the wrap is this owner's.
/// </summary>
public sealed class RestoringNoticeLineTests
{
    private static readonly RestoringNotice Notice = RestoringNotice.For(JourneyPhase.Preparing)!;

    [Fact]
    public void ItStartsOnTheSentenceRatherThanOnADot()
    {
        Assert.Equal("Restoring your run", Notice.Line(0));
        Assert.Equal("Restoring your run.", Notice.Line(1));
        Assert.Equal("Restoring your run...", Notice.Line(3));
    }

    /// <summary>A host that only counts up cannot run off the end.</summary>
    [Fact]
    public void TheEllipsisCycles()
    {
        Assert.Equal(Notice.Line(0), Notice.Line(RestoringNotice.EllipsisSteps));
        Assert.Equal(Notice.Line(1), Notice.Line(RestoringNotice.EllipsisSteps + 1));
    }
}

/// <summary>
/// What a player is told when the save their run had to be restored from could not be
/// prepared in time.
/// </summary>
public sealed class RestoreTimeoutCopyTests
{
    [Fact]
    public void ItSaysTheRunCouldNotBeRestoredAndNamesTheBound()
    {
        var sentence = TrainerCopy.CouldNotRestoreYourRun(3);

        Assert.Contains("could not restore your run", sentence, StringComparison.Ordinal);
        Assert.Contains("3 minutes", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arbiter", sentence, StringComparison.OrdinalIgnoreCase);
    }
}
