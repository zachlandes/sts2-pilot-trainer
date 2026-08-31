using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Replays a manifest through the real engine and reports what happened.
///
/// The arbiter has exactly three possible answers and no fourth: it replayed and
/// everything agreed, it declined because the environment does not match, or it
/// replayed and something disagreed. There is no partial credit and no
/// approximation, because the value of the whole apparatus is that a passing result
/// means something.
/// </summary>
public static class Arbiter
{
    public const string Version = "sts2-pilot-trainer/arbiter/0.1.0";

    public static ArbiterOutcome Run(
        ReplayManifest manifest, int? stopAfterSeq = null, PlayerProgress progress = PlayerProgress.AllUnlocked)
    {
        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ManifestException("Manifest is not valid:\n" + validation.Describe());
        }

        var preflight = Preflight.Evaluate(manifest.Environment);
        if (!preflight.Matches)
        {
            return new ArbiterOutcome(
                new VerificationReport
                {
                    Status = VerificationStatus.Refused,
                    ArbiterVersion = Version,
                    Preflight = preflight,
                    Caveats = Caveats(),
                    Diagnostics = preflight.Fields
                        .Where(f => !f.Matches)
                        .Select(f => $"{f.Field}: manifest says '{f.Expected}', this machine has '{f.Actual}'. {f.Diagnostic}")
                        .ToList(),
                },
                FinalState: null);
        }

        var session = new GameSession();
        session.StartRun(
            manifest.Environment.Seed.Value,
            manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value,
            manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value,
            progress);

        var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        var checkpointsBySeq = manifest.Checkpoints.ToLookup(c => c.AfterSeq);
        var results = new List<CheckpointResult>();
        var diagnostics = new List<string>();

        // Checkpoints bound to -1 are evaluated before any action runs.
        results.AddRange(Evaluate(checkpointsBySeq[-1], session));

        foreach (var action in manifest.Actions.OrderBy(a => a.Seq))
        {
            if (stopAfterSeq is { } stop && action.Seq > stop) break;

            try
            {
                driver.Apply(action);
            }
            catch (EngineException ex)
            {
                diagnostics.Add($"action {action.Seq} ({action.Verb}): {ex.Message}");
                return new ArbiterOutcome(
                    new VerificationReport
                    {
                        Status = VerificationStatus.Rejected,
                        ArbiterVersion = Version,
                        Preflight = preflight,
                        Checkpoints = results,
                        Caveats = Caveats(),
                        Diagnostics = diagnostics,
                    },
                    FinalState: CanonicalStateProjection.Project(session.RunState));
            }

            results.AddRange(Evaluate(checkpointsBySeq[action.Seq], session));
        }

        var finalState = CanonicalStateProjection.Project(session.RunState);
        var replayedActions = stopAfterSeq is { } stop
            ? manifest.Actions.Where(a => a.Seq <= stop).ToList()
            : manifest.Actions;
        var isPartial = replayedActions.Count < manifest.Actions.Count;
        var failed = results.Where(r => !r.Passed).ToList();
        foreach (var result in failed)
        {
            foreach (var comparison in result.Comparisons.Where(c => !c.Matches))
            {
                diagnostics.Add(
                    $"checkpoint '{result.Id}' (after action {result.AfterSeq}): {comparison.Field} " +
                    $"observed '{comparison.Expected}', engine produced '{comparison.Actual}'");
            }
        }

        if (isPartial)
        {
            diagnostics.Add(
                $"Partial replay stopped after action {stopAfterSeq}; " +
                $"{manifest.Actions.Count - replayedActions.Count} action(s) and their checkpoints were not evaluated.");
        }

        return new ArbiterOutcome(
            new VerificationReport
            {
                Status = failed.Count > 0
                    ? VerificationStatus.Rejected
                    : isPartial ? VerificationStatus.Partial : VerificationStatus.Verified,
                ArbiterVersion = Version,
                Preflight = preflight,
                Checkpoints = results,
                FinalStateDigest = finalState.Digest(),
                ActionHistoryHash = SnapshotCacheKey.HashActions(replayedActions),
                Caveats = Caveats(),
                Diagnostics = diagnostics,
            },
            finalState);
    }

    /// <summary>
    /// Compares a checkpoint's observations against the engine's canonical state.
    ///
    /// A field the projection does not produce fails rather than passing silently.
    /// A checkpoint that quietly checks nothing is worse than no checkpoint, because
    /// it reports a pass.
    /// </summary>
    private static IEnumerable<CheckpointResult> Evaluate(IEnumerable<Checkpoint> checkpoints, GameSession session)
    {
        foreach (var checkpoint in checkpoints)
        {
            var state = CanonicalStateProjection.Project(session.RunState);
            var comparisons = new List<FieldComparison>();

            foreach (var (field, expected) in checkpoint.Expect.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var present = state.Fields.TryGetValue(field, out var actual);
                comparisons.Add(new FieldComparison(
                    field,
                    expected.Value,
                    present ? actual! : "<no such canonical field>",
                    present && string.Equals(actual, expected.Value, StringComparison.Ordinal)));
            }

            yield return new CheckpointResult(
                checkpoint.Id, checkpoint.AfterSeq, comparisons.All(c => c.Matches), comparisons);
        }
    }

    /// <summary>
    /// What a reader must know before treating a pass as proof. Attached to every
    /// report, pass or fail, because a caveat that only appears on failures is a
    /// caveat nobody reads.
    /// </summary>
    private static IReadOnlyList<string> Caveats() =>
    [
        "This is a headless host, not the retail client. It supplies no graphics, audio, input or scene " +
        "tree, and switches the engine into the mode its own automated tests use so that presentation " +
        "constructors return null instead of loading scenes. See docs/headless-fidelity.md for the full " +
        "list of what is neutralised and the evidence that generation is unaffected.",

        Preflight.ContentHashScope,

        "The environment this replays in has no mods loaded. A source video recorded on a modded install " +
        "may differ in behaviour that the content hash cannot see; agreement at a checkpoint is evidence " +
        "against divergence at that point, not a proof of parity across the run.",

        "Unlock state is assumed to be complete. The game derives a run's content pools from the player's " +
        "progress, and the source player's progress is not observable from a video. Agreement on generated " +
        "content is the evidence for this assumption; it is not independently established.",
    ];
}

/// <summary>The report, plus the engine's end state when there was one.</summary>
public sealed record ArbiterOutcome(VerificationReport Report, CanonicalState? FinalState);
