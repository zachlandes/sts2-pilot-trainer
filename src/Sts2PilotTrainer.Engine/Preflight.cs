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
    /// Which unlock state to check. The Combat Trainer passes the supplied state it
    /// will construct the recorded run with; callers asking whether a player could
    /// start a run themselves pass <see cref="PlayerProgress.LocalProfile"/>.
    /// </param>
    /// <param name="sourceKind">
    /// What kind of recording this environment belongs to. It selects the mod rule:
    /// a video recording's mod list is judged against the audited set, because
    /// nothing could read a mod's own manifest off a video, and a recording this
    /// project's recorder made is judged by the rule that every mod it read declares
    /// itself non-gameplay. Pass the manifest's own kind wherever there is one.
    /// </param>
    public static PreflightResult Evaluate(
        EnvironmentIdentity expected,
        PlayerProgress progress = PlayerProgress.AllUnlocked,
        string sourceKind = "vod") =>
        EnvironmentPreflight.Prerequisites(
            expected, LocalEnvironment.ReadPrerequisites(expected, progress), sourceKind);

    /// <summary>
    /// The gate on the run that now exists, read back out of the game.
    ///
    /// For a live host this is the run in the retail process. For the arbiter it is
    /// the run it just constructed, and checking it is not a formality: it is how we learn the
    /// engine built the run the manifest asked for rather than something adjacent to
    /// it - a seed the engine normalised differently, or an act that quietly
    /// defaulted.
    /// </summary>
    public static PreflightResult EvaluateStartedRun(EnvironmentIdentity expected) =>
        EnvironmentPreflight.RunIdentity(expected, LocalEnvironment.ReadStartedRun());

    /// <summary>Both gates, which a host must ask of a live game.</summary>
    public static PreflightResult EvaluateLiveGame(EnvironmentIdentity expected, string sourceKind = "vod") =>
        EnvironmentPreflight.Combine(
            Evaluate(expected, PlayerProgress.LocalProfile, sourceKind),
            EvaluateStartedRun(expected));

    /// <summary>
    /// Both gates as the in-game host asks them: the same rules over one reading,
    /// with the two verdicts kept apart.
    ///
    /// Same owners, same order, nothing softened. Both gates are judged from a single
    /// reading, so a screen can never show a row measured at one moment beside a
    /// verdict measured at another, and a host can distinguish "you have not started
    /// the run yet" from "your install cannot play this". Where a run exists
    /// <see cref="EnvironmentPreflight.RunIdentity"/> is still authoritative. See
    /// <see cref="LivePreflight"/>.
    /// </summary>
    /// <param name="progress">
    /// Whose progress the prerequisites are asked about, and the one thing a host has
    /// to decide for itself. A host asking whether the player could play this run
    /// themselves passes <see cref="PlayerProgress.LocalProfile"/>. A host that
    /// constructs the run passes the state it will construct it with, because that is
    /// the environment the run is actually generated in - and a screen that showed a
    /// requirement measured against a profile nothing consults would be reporting a
    /// requirement that is not one. Named rather than defaulted: which question is
    /// being asked is the whole difference between the two answers.
    /// </param>
    public static LivePreflight EvaluateLiveHost(
        EnvironmentIdentity expected, PlayerProgress progress, string sourceKind = "vod") =>
        EnvironmentPreflight.LiveGame(
            expected,
            LocalEnvironment.ReadPrerequisites(expected, progress),
            LocalEnvironment.ReadStartedRun(),
            sourceKind);
}
