namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The two gates of a live game, kept apart, as a host that has to explain itself
/// needs them.
///
/// <see cref="PreflightResult"/> is one verdict over one list of fields, which is
/// what a command-line arbiter wants: it constructs the run itself, so both gates
/// are questions about the same moment. A host in front of a player is at a
/// different moment. Before the player has a run, the run-identity gate refuses by
/// design - "no run in progress" is the ordinary state of a freshly launched game -
/// and folding that refusal into the prerequisite list would tell someone their
/// install is wrong when it is not.
///
/// So the two results stay separable and the sequencing is recorded rather than
/// inferred: <see cref="RunPresent"/> says whether the run-identity gate was asked
/// about anything at all. Where it was, it is authoritative; nothing here softens
/// it.
/// </summary>
public sealed record LivePreflight(
    PreflightResult Prerequisites,
    PreflightResult RunIdentity,
    bool RunPresent,
    LocalPrerequisites Reading)
{
    /// <summary>
    /// Whether this game can faithfully represent the manifest right now.
    ///
    /// The prerequisites always count. The run-identity gate counts when there is a
    /// run to have identity, and a run that exists and is the wrong run is a refusal
    /// - the manifest's fight cannot be entered from someone else's run.
    /// </summary>
    public bool Matches => Prerequisites.Matches && (!RunPresent || RunIdentity.Matches);

    /// <summary>
    /// Every field a host may show, in the order the gates were asked. Run-identity
    /// fields appear only when a run was there to read, because a gate that was not
    /// asked has no answer to report.
    /// </summary>
    public IReadOnlyList<PreflightField> Fields =>
        RunPresent ? [.. Prerequisites.Fields, .. RunIdentity.Fields] : Prerequisites.Fields;
}
