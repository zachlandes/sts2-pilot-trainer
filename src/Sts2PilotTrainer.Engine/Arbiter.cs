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
        ReplayManifest manifest, int? stopAfterSeq = null,
        PlayerProgress? progress = null,
        string? gameModeOverride = null, IReadOnlyList<string>? modifierTypeNames = null,
        bool measuringBuildDrift = false)
    {
        progress ??= PlayerProgress.AllUnlocked;

        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ManifestException("Manifest is not valid:\n" + validation.Describe());
        }

        var preflight = Preflight.Evaluate(
            manifest.Environment, progress, manifest.Source.Kind, measuringBuildDrift);
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
            gameModeOverride ?? manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value,
            progress,
            modifierTypeNames ?? []);

        // Read the run back out of the engine before touching it.
        //
        // Not a formality: everything above is what we asked the engine for, and this
        // is what it built. A seed the engine normalised differently, or an act that
        // quietly defaulted, would otherwise replay perfectly and be a different run.
        //
        // Compared against the identity actually requested, which differs from the
        // manifest's only when a caller deliberately overrides the mode to ask what a
        // different one would produce. Comparing that against the manifest would fail
        // by construction and prove nothing; comparing it against what was asked for
        // still catches the engine building something else. The substitution is
        // recorded in the diagnostics below, and such a result never verifies the
        // manifest.
        var requestedIdentity = gameModeOverride is null || gameModeOverride == manifest.Environment.GameMode.Value
            ? manifest.Environment
            : manifest.Environment with { GameMode = Fact<string>.Declared(gameModeOverride) };
        var runIdentity = Preflight.EvaluateStartedRun(requestedIdentity);
        preflight = EnvironmentPreflight.Combine(preflight, runIdentity);
        if (!runIdentity.Matches)
        {
            return new ArbiterOutcome(
                new VerificationReport
                {
                    Status = VerificationStatus.Refused,
                    ArbiterVersion = Version,
                    Preflight = preflight,
                    Caveats = Caveats(),
                    Diagnostics = runIdentity.Fields
                        .Where(f => !f.Matches)
                        .Select(f =>
                            $"{f.Field}: manifest says '{f.Expected}', the started run has '{f.Actual}'. {f.Diagnostic}")
                        .ToList(),
                },
                FinalState: null);
        }

        using var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        var checkpointsBySeq = manifest.Checkpoints.ToLookup(c => c.AfterSeq);
        var results = new List<CheckpointResult>();
        var diagnostics = new List<string>();
        var steps = new List<ReplayStep>();

        // The whole canonical state's digest after every action, kept beside the trace
        // rather than in it. A trace samples the fields a comparison reads; a boundary
        // digest covers the draw order and every random stream's position, which is
        // most of what it is for. Which of these seqs turn out to be boundaries is
        // decided once the history has run, by reading the trace.
        var digests = new Dictionary<int, string>();

        // Checkpoints bound to -1 are evaluated before any action runs.
        results.AddRange(Evaluate(checkpointsBySeq[-1], session));

        var state = CanonicalStateProjection.Project(session.RunState);
        steps.Add(new ReplayStep
        {
            Seq = -1,
            Verb = "run_start",
            Before = Sample(state),
            After = Sample(state),
        });
        digests[-1] = state.Digest();

        var ordered = manifest.Actions.OrderBy(a => a.Seq).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var action = ordered[index];
            if (stopAfterSeq is { } actionLimit && action.Seq > actionLimit) break;

            var before = Sample(CanonicalStateProjection.Project(session.RunState));
            try
            {
                // The rest of the replayed history is passed with each action because
                // a card screen is answered inside the call that opens it; see RunDriver.
                var upcoming = ordered
                    .Skip(index + 1)
                    .TakeWhile(next => stopAfterSeq is not { } limit || next.Seq <= limit)
                    .ToList();
                driver.Apply(action, upcoming);
            }
            catch (EngineException ex)
            {
                // Checkpoints that already failed come first. The first diagnostic is
                // read as the first divergence, and an action refusal is often the
                // downstream consequence of a mismatch several actions earlier - a
                // refusal reported alone would name the wrong moment.
                diagnostics.AddRange(CheckpointDiagnostics(results));
                diagnostics.Add($"action {action.Seq} ({action.Verb}): {ex.Message}");
                var refusedState = CanonicalStateProjection.Project(session.RunState);
                steps.Add(new ReplayStep
                {
                    Seq = action.Seq,
                    Verb = action.Verb.ToString(),
                    Args = action.Args,
                    Before = before,
                    After = Sample(refusedState),
                });
                return new ArbiterOutcome(
                    new VerificationReport
                    {
                        Status = VerificationStatus.Rejected,
                        ArbiterVersion = Version,
                        Preflight = preflight,
                        Checkpoints = results,
                        Trace = new ReplayTrace { Steps = steps },
                        Caveats = Caveats(),
                        Diagnostics = diagnostics,
                    },
                    FinalState: refusedState);
            }

            var after = CanonicalStateProjection.Project(session.RunState);
            steps.Add(new ReplayStep
            {
                Seq = action.Seq,
                Verb = action.Verb.ToString(),
                Args = action.Args,
                Before = before,
                After = Sample(after),
            });
            digests[action.Seq] = after.Digest();

            results.AddRange(Evaluate(checkpointsBySeq[action.Seq], session));
        }

        var finalState = CanonicalStateProjection.Project(session.RunState);
        var replayedActions = stopAfterSeq is { } stop
            ? manifest.Actions.Where(a => a.Seq <= stop).ToList()
            : manifest.Actions;
        var isPartial = replayedActions.Count < manifest.Actions.Count;
        var failed = results.Where(r => !r.Passed).ToList();
        diagnostics.AddRange(CheckpointDiagnostics(results));

        if (gameModeOverride is { } overriddenMode && overriddenMode != manifest.Environment.GameMode.Value)
        {
            diagnostics.Add(
                $"Run identity was compared against the requested mode '{overriddenMode}', not the manifest's " +
                $"'{manifest.Environment.GameMode.Value}'. This result cannot verify the source manifest.");
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
                Trace = new ReplayTrace { Steps = steps },
                Boundaries = isPartial ? [] : DeriveBoundaries(steps, digests),
                FinalStateDigest = finalState.Digest(),
                ActionHistoryHash = SnapshotCacheKey.HashActions(replayedActions),
                Caveats = Caveats(),
                Diagnostics = diagnostics,
            },
            finalState);
    }

    /// <summary>
    /// Every boundary this history passed, with the digest the engine produced there.
    ///
    /// Two owners, deliberately. Where the boundaries are is a rule over the trace and
    /// belongs to <see cref="RunCoverage"/>, which has tests that need no game. What
    /// the state was at each is the engine's, and is only available from the process
    /// that just replayed it.
    ///
    /// A boundary whose digest is missing is dropped rather than filled in: the only
    /// way that happens is a seq the replay never reached, and a boundary with an
    /// invented digest is exactly the confident wrong answer this arbiter exists to
    /// prevent.
    /// </summary>
    private static IReadOnlyList<ReplayBoundary> DeriveBoundaries(
        IReadOnlyList<ReplayStep> steps, IReadOnlyDictionary<int, string> digests) =>
        RunCoverage.Of(new ReplayTrace { Steps = steps })
            .Boundaries()
            .Where(boundary => digests.ContainsKey(boundary.AfterSeq))
            .Select(boundary => boundary.With(Fact<string>.Engine(digests[boundary.AfterSeq])))
            .ToList();

    /// <summary>
    /// Every field a checkpoint disagreed on, in the order the checkpoints ran.
    /// </summary>
    private static IEnumerable<string> CheckpointDiagnostics(IEnumerable<CheckpointResult> results) =>
        results
            .Where(result => !result.Passed)
            .SelectMany(result => result.Comparisons
                .Where(comparison => !comparison.Matches)
                .Select(comparison =>
                    $"checkpoint '{result.Id}' (after action {result.AfterSeq}): {comparison.Field} " +
                    $"observed '{comparison.Expected}', engine produced '{comparison.Actual}'"));

    /// <summary>
    /// The part of a canonical state the trace keeps.
    ///
    /// Filtering here rather than projecting differently keeps one owner for what the
    /// engine's state is: the trace and the checkpoints read the same projection, so
    /// a field cannot mean one thing in a comparison and another in the trace.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Sample(CanonicalState state) =>
        ReplayTrace.Sample(state.Fields);

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
        "progress, and the source player's progress is not observable from a video. The preflight checks " +
        "that the environment replaying this run has the complete unlock state the manifest requires; that " +
        "the source player did is the inference recorded in environment.unlocks, and agreement on generated " +
        "content is its evidence rather than an independent establishment of it.",
    ];
}

/// <summary>The report, plus the engine's end state when there was one.</summary>
public sealed record ArbiterOutcome(VerificationReport Report, CanonicalState? FinalState);
