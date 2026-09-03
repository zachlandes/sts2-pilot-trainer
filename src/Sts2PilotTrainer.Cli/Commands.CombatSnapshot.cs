using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Materialises and verifies the combat-start snapshot, then describes exactly
    /// the action history the manifest contains.
    ///
    /// Combat start is the supported boundary, and the whole fight is the intended unit. That
    /// is a product decision with a technical consequence worth stating: resuming
    /// mid-combat would need state to be reset at a turn boundary, and nothing here
    /// does that or is designed around it. See docs/comparison-direction.md.
    ///
    /// "Restore" means re-derive and verify, not deserialise. The snapshot's content
    /// is the canonical state at the boundary together with the key that determines
    /// it; a restore replays the same prefix in a fresh process and refuses unless
    /// the digest matches what was cached. That is slower than loading a blob and
    /// considerably harder to get quietly wrong: a cache that can only be read by
    /// reproducing it cannot drift away from the run it claims to be.
    ///
    /// Nothing here scores anything. The report describes only the covered history.
    /// </summary>
    internal static int CombatSnapshot(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var reportArtifact = EvidenceArtifact.Prepare(outDir, "combat-snapshot.json");
        var manifest = ManifestJson.Load(manifestPath);
        var cacheDir = Args.Value(args, "--cache") ?? "build/snapshots";

        var verifiedPath = Path.Combine(outDir, "combat-snapshot.verified.json");
        var coveredStatePath = Path.Combine(outDir, "combat-snapshot.covered.state");
        var covered = SelfProcess.Run(
            "replay", manifestPath, "--out", verifiedPath, "--state-out", coveredStatePath);
        if (covered.ExitCode != 0)
        {
            Console.Write(covered.StandardOutput);
            Console.Error.Write(covered.StandardError);
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "The manifest does not replay cleanly, so there is no verified combat to snapshot. " +
                "A snapshot of an unverified replay would be a cache of a guess.");
            return 1;
        }

        var report = ManifestJson.Load(verifiedPath).Verification
            ?? throw new ManifestException("The replay wrote no verification report.");
        var trace = report.Trace
            ?? throw new ManifestException("The replay wrote no trace, so combat start cannot be located.");

        var combatStart = CombatStartSeq(trace)
            ?? throw new ManifestException(
                "This history never enters combat, so it has no combat-start boundary. The supported " +
                "boundary is the start of a fight; a history that reaches none has nothing to snapshot.");

        var key = SnapshotCacheKey.For(manifest, combatStart);
        var snapshotDir = key.ResolveCacheDirectory(cacheDir);
        var snapshotPath = SnapshotCacheKey.ResolveCacheArtifact(snapshotDir, "state.canonical");
        var keyPath = SnapshotCacheKey.ResolveCacheArtifact(snapshotDir, "key.json");

        // ── Materialise ─────────────────────────────────────────────────────
        var cached = File.Exists(snapshotPath);
        if (!cached)
        {
            Directory.CreateDirectory(snapshotDir);
            File.WriteAllText(
                snapshotPath,
                ReplayPrefix(manifestPath, combatStart, Path.Combine(outDir, "combat-snapshot.materialise.state")));
            File.WriteAllText(keyPath, JsonSerializer.Serialize(key, Json.Indented) + "\n");
        }

        var snapshot = File.ReadAllText(snapshotPath);

        // ── Restore, in a fresh process, and refuse a drifted cache ─────────
        var restored = ReplayPrefix(
            manifestPath, combatStart, Path.Combine(outDir, "combat-snapshot.restore.state"));
        if (!string.Equals(restored, snapshot, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Restoring the combat-start snapshot produced different state than the cache holds. The cache " +
                "is stale, or the key is not capturing something it should. Refusing to replay a combat from a " +
                "state that is not the snapshot.");
            return 1;
        }

        var snapshotDigest = DigestOf(snapshot);
        if (manifest.Source.CombatStartSnapshotDigest is { } declaredSnapshot &&
            !string.Equals(declaredSnapshot.Value, snapshotDigest, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"The manifest declares combat-start snapshot {declaredSnapshot.Value}, but replaying its " +
                $"recorded prefix produced {snapshotDigest}. Refusing a drifted publication boundary.");
            return 1;
        }

        var coveredState = File.ReadAllText(coveredStatePath);
        var coveredFields = ParseState(coveredState);
        var combatActive = coveredFields.GetValueOrDefault("combat.in_progress") == "true";
        // How the fight ended, not merely that it is no longer running. "Not running"
        // covers a won fight, a lost one, and a history that never reached combat, and
        // a report that could not tell them apart would be describing three different
        // things with one word.
        var combatOutcome = coveredFields.GetValueOrDefault("combat.outcome", "unknown");
        var turns = TurnBoundaries(trace, combatStart);
        var lastSeq = manifest.Actions[^1].Seq;
        var combatState = combatActive
            ? "combat remains active"
            : $"combat finished ({combatOutcome})";

        Console.WriteLine($"manifest        : {manifest.RunId}");
        Console.WriteLine($"combat starts   : after action {combatStart}");
        Console.WriteLine($"snapshot key    : {key.ToCacheDirectoryName()}");
        Console.WriteLine($"snapshot source : {(cached ? "cache hit" : "materialised now")}");
        Console.WriteLine($"snapshot digest : {snapshotDigest}");
        Console.WriteLine($"restore         : re-derived in a fresh process, digest matches");
        Console.WriteLine($"covered history : {report.Status.ToString().ToUpperInvariant()} through action " +
                          $"{lastSeq} ({manifest.Actions.Count} actions), {combatState}, " +
                          $"end state {DigestOf(coveredState)}");
        Console.WriteLine();
        Console.WriteLine("covered combat history, turn by turn (description, not a verdict):");
        foreach (var turn in turns)
        {
            Console.WriteLine(
                $"  turn {turn.Turn}  actions {turn.FirstSeq}..{turn.LastSeq}  " +
                $"player hp {turn.PlayerHpBefore} -> {turn.PlayerHpAfter}");
        }

        reportArtifact.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/combat-snapshot/v2",
                manifest = Path.GetFileName(manifestPath),
                combat_start_seq = combatStart,
                snapshot_key = key,
                snapshot_digest = snapshotDigest,
                snapshot_source = cached ? "cache hit" : "materialised now",
                restore_verified = true,
                covered_history_status = report.Status.ToString(),
                covered_action_count = manifest.Actions.Count,
                covered_through_seq = lastSeq,
                combat_active_at_history_end = combatActive,
                combat_outcome_at_history_end = combatOutcome,
                covered_history_end_state_digest = DigestOf(coveredState),
                turns,
                comparison_policy =
                    "Ordered description of only the manifest's covered history. No score, ranking, or verdict " +
                    "is computed, and no alternative line is replayed: the supported boundary is combat start.",
            }, Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
        return 0;
    }

    /// <summary>
    /// Where the fight begins, read out of the trace rather than declared in the
    /// manifest: the boundary is a fact about what the engine did, and asking the
    /// manifest would let the two disagree.
    ///
    /// Delegated so that the boundary has one definition. The comparison contract and
    /// the publication gate read it through the same call, and a second reading of
    /// "where the fight started" is a second answer waiting to disagree.
    /// </summary>
    private static int? CombatStartSeq(ReplayTrace trace) =>
        CombatProjection.CoverageOf(trace).CombatStartSeq;

    /// <summary>
    /// The combat's turns, in order, with the actions that fall in each and what the
    /// turn cost. Ordered description only - enough for a walkthrough to step through
    /// the fight later without re-solving anything, and nothing that ranks a choice.
    /// </summary>
    private static IReadOnlyList<TurnSpan> TurnBoundaries(ReplayTrace trace, int combatStart)
    {
        var spans = new List<TurnSpan>();
        foreach (var step in trace.Steps.Where(s => s.Seq > combatStart &&
                                                    s.Before.GetValueOrDefault("combat.in_progress") == "true"))
        {
            if (!TryInt(step.Before, "combat.turn", out var turn)) continue;
            TryInt(step.Before, "combat.player_hp", out var hpBefore);
            TryInt(step.After, "combat.player_hp", out var hpAfter);

            var index = spans.FindIndex(span => span.Turn == turn);
            if (index >= 0)
            {
                spans[index] = spans[index] with { LastSeq = step.Seq, PlayerHpAfter = hpAfter };
            }
            else
            {
                spans.Add(new TurnSpan(turn, step.Seq, step.Seq, hpBefore, hpAfter));
            }
        }

        return spans;
    }

    private static bool TryInt(IReadOnlyDictionary<string, string> sample, string field, out int value) =>
        int.TryParse(
            sample.GetValueOrDefault(field), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    internal sealed record TurnSpan(
        int Turn, int FirstSeq, int LastSeq, int PlayerHpBefore, int PlayerHpAfter);

    /// <summary>Replays a manifest up to a sequence number and returns its canonical state.</summary>
    private static string ReplayPrefix(string manifestPath, int upToSeq, string statePath)
    {
        var child = SelfProcess.Run(
            "replay", manifestPath, "--state-out", statePath,
            "--stop-after", upToSeq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (child.ExitCode != 0)
        {
            Console.Write(child.StandardOutput);
            Console.Error.Write(child.StandardError);
            throw new ManifestException(
                $"Replaying {Path.GetFileName(manifestPath)} up to action {upToSeq} did not succeed.");
        }

        return File.ReadAllText(statePath);
    }

    private static string DigestOf(string canonical) => CanonicalState.DigestRendering(canonical);
}
