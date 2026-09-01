using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Decides whether this machine's game is the one a manifest was recorded against,
/// and whether the run in front of us is the run it describes.
///
/// The owner of that decision. It reads through <see cref="LocalEnvironment"/> and
/// judges through <see cref="EnvironmentPreflight"/>, so that what is read and what
/// is required stay separable: the reading knows about v0.111.0, and the rules
/// outlive it.
///
/// It changes nothing, and it refuses rather than approximating. Refusing is the
/// useful behaviour: replaying a run in the wrong environment does not fail, it
/// succeeds at producing a different run, and every downstream check would then be
/// comparing the wrong things confidently.
/// </summary>
public static class Preflight
{
    /// <inheritdoc cref="EnvironmentPreflight.ContentHashScope"/>
    public const string ContentHashScope = EnvironmentPreflight.ContentHashScope;

    /// <summary>
    /// The gate before a run exists: build, content, and the player prerequisites a
    /// run's generation will read.
    /// </summary>
    /// <param name="progress">
    /// Which unlock state to check. The eventual mod entry point must pass
    /// <see cref="PlayerProgress.LocalProfile"/> and so gate on what the player
    /// actually has; the headless arbiter passes the state it will construct the run
    /// with, which is the same question asked of a host rather than a person.
    /// </param>
    public static PreflightResult Evaluate(
        EnvironmentIdentity expected, PlayerProgress progress = PlayerProgress.AllUnlocked) =>
        EnvironmentPreflight.Prerequisites(expected, LocalEnvironment.ReadPrerequisites(expected, progress));

    /// <summary>
    /// The gate on the run that now exists, read back out of the game.
    ///
    /// For the eventual mod this is the player's own run. For the arbiter it is the run it
    /// just constructed, and checking it is not a formality: it is how we learn the
    /// engine built the run the manifest asked for rather than something adjacent to
    /// it - a seed the engine normalised differently, or an act that quietly
    /// defaulted.
    /// </summary>
    public static PreflightResult EvaluateStartedRun(EnvironmentIdentity expected) =>
        EnvironmentPreflight.RunIdentity(expected, LocalEnvironment.ReadStartedRun());

    /// <summary>Both gates, which an eventual mod entry point must ask of a live game.</summary>
    public static PreflightResult EvaluateLiveGame(EnvironmentIdentity expected) =>
        EnvironmentPreflight.Combine(
            Evaluate(expected, PlayerProgress.LocalProfile),
            EvaluateStartedRun(expected));
}
