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
/// Which drawing the restoring notice gets, and what it reads.
///
/// The rule this holds is that there is no third answer. The wait it covers is the
/// best part of a minute, so a client that cannot hand over its own loading overlay
/// gets this mod's own plate rather than nothing: a press that draws nothing is
/// indistinguishable from a press that did not work.
/// </summary>
public sealed class RestoringSurfaceTests
{
    private static readonly RestoringNotice Notice =
        RestoringNotice.For(JourneyPhase.Preparing)!;

    [Fact]
    public void TheGamesOwnOverlayIsUsedWhereItAnswers()
    {
        var surface = RestoringSurface.For(Notice, borrowedTheGamesOverlay: true);

        Assert.Equal(RestoringSurfaceKind.TheGamesLoadingOverlay, surface.Kind);
        Assert.Equal("Restoring your run", surface.Headline);
    }

    /// <summary>A build with no such scene, or one whose scene carries no label, still
    /// says the same sentence: the drawing changes and the notice does not.</summary>
    [Fact]
    public void ABorrowThatFailedIsDrawnHereAndSaysTheSameThing()
    {
        var surface = RestoringSurface.For(Notice, borrowedTheGamesOverlay: false);

        Assert.Equal(RestoringSurfaceKind.DrawnHere, surface.Kind);
        Assert.Equal("Restoring your run", surface.Headline);
        Assert.NotEqual(string.Empty, surface.Line(0));
    }

    /// <summary>The ellipsis is what says the wait is alive, and it starts on the
    /// sentence itself rather than on a dot.</summary>
    [Fact]
    public void TheEllipsisCyclesAndNeverRunsOffTheEnd()
    {
        var surface = RestoringSurface.For(Notice, borrowedTheGamesOverlay: false);

        Assert.Equal("Restoring your run", surface.Line(0));
        Assert.Equal("Restoring your run.", surface.Line(1));
        Assert.Equal("Restoring your run...", surface.Line(3));
        Assert.Equal(surface.Line(0), surface.Line(RestoringSurface.EllipsisSteps));
        Assert.Equal(surface.Line(1), surface.Line(RestoringSurface.EllipsisSteps + 1));
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
