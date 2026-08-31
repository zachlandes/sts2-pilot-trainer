using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Materialises the verified combat-start snapshot and replays the whole combat
    /// through it.
    ///
    /// Combat start is the supported boundary, and the whole fight is the unit. That
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
    /// Nothing here scores anything. The report describes the combat and stops.
    /// </summary>
    internal static int CombatSnapshot(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var reportArtifact = EvidenceArtifact.Prepare(outDir, "combat-snapshot.json");
        var manifest = ManifestJson.Load(manifestPath);
        var cacheDir = Args.Value(args, "--cache") ?? "build/snapshots";

        // ── Replay the whole thing once, to have something to be right about ──
        var verifiedPath = Path.Combine(outDir, "combat-snapshot.verified.json");
        var wholeStatePath = Path.Combine(outDir, "combat-snapshot.whole.state");
        var whole = SelfProcess.Run(
            "replay", manifestPath, "--out", verifiedPath, "--state-out", wholeStatePath);
        if (whole.ExitCode != 0)
        {
            Console.Write(whole.StandardOutput);
            Console.Error.Write(whole.StandardError);
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

        var wholeState = File.ReadAllText(wholeStatePath);
        var turns = TurnBoundaries(trace, combatStart);

        Console.WriteLine($"manifest        : {manifest.RunId}");
        Console.WriteLine($"combat starts   : after action {combatStart}");
        Console.WriteLine($"snapshot key    : {key.ToCacheDirectoryName()}");
        Console.WriteLine($"snapshot source : {(cached ? "cache hit" : "materialised now")}");
        Console.WriteLine($"snapshot digest : {DigestOf(snapshot)}");
        Console.WriteLine($"restore         : re-derived in a fresh process, digest matches");
        Console.WriteLine($"whole combat    : {report.Status.ToString().ToUpperInvariant()}, " +
                          $"end state {DigestOf(wholeState)}");
        Console.WriteLine();
        Console.WriteLine("combat, turn by turn (description, not a verdict):");
        foreach (var turn in turns)
        {
            Console.WriteLine(
                $"  turn {turn.Turn}  actions {turn.FirstSeq}..{turn.LastSeq}  " +
                $"player hp {turn.PlayerHpBefore} -> {turn.PlayerHpAfter}");
        }

        reportArtifact.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/combat-snapshot/v1",
                manifest = Path.GetFileName(manifestPath),
                combat_start_seq = combatStart,
                snapshot_key = key,
                snapshot_digest = DigestOf(snapshot),
                snapshot_source = cached ? "cache hit" : "materialised now",
                restore_verified = true,
                whole_combat_status = report.Status.ToString(),
                whole_combat_end_state_digest = DigestOf(wholeState),
                turns,
                // Said out loud in the artifact, not only in the docs.
                comparison_policy =
                    "Ordered description of one completed combat. No score, ranking, or verdict is computed, " +
                    "and no alternative line is replayed: the supported boundary is combat start.",
            }, Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
        return 0;
    }

    /// <summary>
    /// Where the fight begins, read out of the trace rather than declared in the
    /// manifest: the boundary is a fact about what the engine did, and asking the
    /// manifest would let the two disagree.
    /// </summary>
    private static int? CombatStartSeq(ReplayTrace trace) => trace.Steps
        .FirstOrDefault(step => step.After.GetValueOrDefault("combat.in_progress") == "true")?.Seq;

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

    private static string DigestOf(string canonical) =>
        "sha256:" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
}
