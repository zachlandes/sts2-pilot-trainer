namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What is on screen while a run is being restored, before there is a run.
///
/// A sibling of <see cref="PlaybackTransport"/> rather than a state of it, and the
/// split is the one the transport's own docstring draws: the tag is a child of the
/// run's persistent interface and is derived from the run's facts, and in
/// <see cref="JourneyPhase.Preparing"/> there is neither. This is derived from the
/// phase alone, because the phase is the whole of what is known then: the packaged
/// arbiter is replaying the recording's history in its own process and says nothing
/// until it is done.
///
/// Total over the phases, like the transport: every phase has an answer, and null is
/// the answer for all but one. Pressing Continue on a fight past the first used to
/// draw nothing at all for the best part of a minute, which reads as a button that
/// did not work.
/// </summary>
/// <param name="Headline">The one line the surface says.</param>
public sealed record RestoringNotice(string Headline)
{
    /// <summary>How many steps the ellipsis cycles through, the first of them being
    /// no dots at all.</summary>
    public const int EllipsisSteps = 4;

    /// <summary>The notice this phase shows, or null where the phase draws none.</summary>
    public static RestoringNotice? For(JourneyPhase phase) =>
        phase == JourneyPhase.Preparing ? new RestoringNotice(TrainerCopy.RestoringYourRun) : null;

    /// <summary>
    /// What the notice reads at this step of its ellipsis.
    ///
    /// The step is taken modulo the cycle here rather than by the host that draws it,
    /// because the host only counts up: the wait has no end it knows about, so nothing
    /// on that side has a reason to wrap, and a step that ran off the end would be a
    /// line nobody wrote.
    /// </summary>
    public string Line(int step) =>
        Headline + new string('.', ((step % EllipsisSteps) + EllipsisSteps) % EllipsisSteps);
}
