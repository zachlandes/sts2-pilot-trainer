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
