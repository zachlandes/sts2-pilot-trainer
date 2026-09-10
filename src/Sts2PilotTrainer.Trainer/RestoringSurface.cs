namespace Sts2PilotTrainer.Trainer;

/// <summary>Which drawing the restoring notice is put up with.</summary>
public enum RestoringSurfaceKind
{
    /// <summary>The client's own loading overlay, borrowed whole.</summary>
    TheGamesLoadingOverlay,

    /// <summary>This mod's own plate, drawn where the borrowed one could not be
    /// loaded or did not carry the label it is read through.</summary>
    DrawnHere,
}

/// <summary>
/// Which of the two drawings the restoring notice gets, and what it says at each
/// step of its ellipsis.
///
/// One question and one answer, kept here rather than inside the host that draws it,
/// because "the game's own overlay could not be borrowed" must never come out as
/// nothing on screen: the wait it covers is the best part of a minute, and a press
/// that draws nothing reads as a press that did not work. The host answers whether
/// the borrowed scene handed over its label and does what it is told; every other
/// outcome would be a second place deciding what a player sees.
/// </summary>
/// <param name="Kind">Which drawing this is.</param>
/// <param name="Headline">The one line it says, before the ellipsis.</param>
public sealed record RestoringSurface(RestoringSurfaceKind Kind, string Headline)
{
    /// <summary>How many steps the ellipsis cycles through, the first of them
    /// being no dots at all.</summary>
    public const int EllipsisSteps = 4;

    /// <summary>The drawing for this notice, given whether the game's own loading
    /// overlay was borrowed and handed over the label it is read through.</summary>
    public static RestoringSurface For(RestoringNotice notice, bool borrowedTheGamesOverlay) =>
        new(
            borrowedTheGamesOverlay ? RestoringSurfaceKind.TheGamesLoadingOverlay : RestoringSurfaceKind.DrawnHere,
            notice.Headline);

    /// <summary>What the label reads at this step. The step is taken modulo the
    /// cycle here, so a host that only ever counts up cannot run off the end.</summary>
    public string Line(int step) =>
        Headline + new string('.', ((step % EllipsisSteps) + EllipsisSteps) % EllipsisSteps);
}
