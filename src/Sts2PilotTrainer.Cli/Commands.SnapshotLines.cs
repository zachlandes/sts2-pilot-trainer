using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Materialises a verified pre-turn snapshot, restores it once per candidate
    /// line, runs each line, and reports what each one changed.
    ///
    /// "Restore" here means re-derive and verify, not deserialise. The snapshot's
    /// content is the canonical state at that point together with the key that
    /// determines it; a restore replays the same prefix in a fresh process and
    /// refuses unless the digest matches what was cached. That is slower than
    /// loading a blob and considerably harder to get quietly wrong: a cache that can
    /// only be read by reproducing it cannot drift away from the run it claims to be.
    ///
    /// The comparison is deltas and nothing else. Which line is better is a question
    /// about a game, not about a replay, and this tool has no business answering it.
    /// </summary>
    internal static int SnapshotLines(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var manifest = ManifestJson.Load(manifestPath);
        var at = int.Parse(
            Args.Value(args, "--at") ?? throw new ManifestException("snapshot-lines needs --at <seq>."),
            System.Globalization.CultureInfo.InvariantCulture);
        var linePaths = Args.Values(args, "--line");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var cacheDir = Args.Value(args, "--cache") ?? "build/snapshots";

        if (linePaths.Count < 2)
        {
            throw new ManifestException(
                "snapshot-lines needs at least two --line files. One line has nothing to be compared against.");
        }

        Directory.CreateDirectory(outDir);
        var key = SnapshotCacheKey.For(manifest, at);
        var snapshotDir = Path.Combine(cacheDir, key.ToCacheDirectoryName());
        var snapshotPath = Path.Combine(snapshotDir, "state.canonical");

        // ── Materialise ─────────────────────────────────────────────────────
        var cached = File.Exists(snapshotPath);
        if (!cached)
        {
            Directory.CreateDirectory(snapshotDir);
            var built = ReplayPrefix(manifestPath, at, Path.Combine(outDir, "snapshot-materialise.state"));
            File.WriteAllText(snapshotPath, built);
            File.WriteAllText(
                Path.Combine(snapshotDir, "key.json"),
                JsonSerializer.Serialize(key, Json.Indented) + "\n");
        }

        var snapshot = File.ReadAllText(snapshotPath);
        Console.WriteLine($"snapshot key   : {key.ToCacheDirectoryName()}");
        Console.WriteLine($"snapshot source: {(cached ? "cache hit" : "materialised now")}");
        Console.WriteLine($"snapshot digest: {DigestOf(snapshot)}");
        Console.WriteLine();

        // ── Restore, once per line ──────────────────────────────────────────
        var lineReports = new List<object>();
        var diagramLines = new List<LineDiagram.Line>();
        foreach (var linePath in linePaths)
        {
            var line = LoadLine(linePath);
            var extended = manifest with
            {
                Actions = [.. manifest.Actions.Where(a => a.Seq <= at), .. Renumber(line, at + 1)],
                // The line's own actions are the point of the exercise, so nothing
                // downstream should be asserting the original run's checkpoints.
                Checkpoints = manifest.Checkpoints.Where(c => c.AfterSeq <= at).ToList(),
            };

            var scratchManifest = Path.Combine(outDir, $"line-{Path.GetFileNameWithoutExtension(linePath)}.manifest.json");
            ManifestJson.Save(extended, scratchManifest);

            var restored = ReplayPrefix(manifestPath, at, Path.Combine(outDir, "restore-check.state"));
            if (!string.Equals(restored, snapshot, StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"Restoring the snapshot for line '{Path.GetFileName(linePath)}' produced different state " +
                    "than the cache holds. The cache is stale or the key is not capturing something it should. " +
                    "Refusing to compare lines from a state that is not the snapshot.");
                return 1;
            }

            var after = ReplayPrefix(
                scratchManifest, at + line.Count, Path.Combine(outDir, "line-after.state"), at + 1);
            var deltas = Diff(snapshot, after);

            Console.WriteLine($"line {Path.GetFileNameWithoutExtension(linePath)}  ({line.Count} action(s))");
            Console.WriteLine($"  restore verified against snapshot digest: yes");
            foreach (var action in line)
            {
                Console.WriteLine($"    {action.Verb} {string.Join(" ", action.Args.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
            foreach (var delta in deltas)
            {
                Console.WriteLine($"    delta {delta.Field,-30} {delta.Before}  ->  {delta.After}");
            }
            Console.WriteLine();

            diagramLines.Add(new LineDiagram.Line(
                Path.GetFileNameWithoutExtension(linePath),
                line.Select(a => $"{a.Verb} {string.Join(" ", a.Args.Select(kv => kv.Value))}".Trim()).ToList(),
                deltas.Select(d => new LineDiagram.Delta(d.Field, d.Before, d.After)).ToList()));

            lineReports.Add(new
            {
                line = Path.GetFileNameWithoutExtension(linePath),
                actions = line.Select(a => new { a.Seq, verb = a.Verb.ToString(), a.Args }),
                restore_verified = true,
                deltas,
            });
        }

        File.WriteAllText(
            Path.Combine(outDir, "snapshot-lines.svg"),
            LineDiagram.Render(key.ToCacheDirectoryName(), DigestOf(snapshot), diagramLines));

        File.WriteAllText(
            Path.Combine(outDir, "snapshot-lines.json"),
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/snapshot-lines/v1",
                manifest = Path.GetFileName(manifestPath),
                snapshot_key = key,
                snapshot_digest = DigestOf(snapshot),
                // Said out loud in the artifact, not only in the docs: this tool
                // reports what each line changed and nothing about which is better.
                comparison_policy =
                    "Objective state deltas only. No score, ranking, or verdict is computed for either line.",
                lines = lineReports,
            }, Json.Indented) + "\n");

        Console.WriteLine($"diagram: {Paths.Display(Path.Combine(outDir, "snapshot-lines.svg"))}");
        return 0;
    }

    /// <summary>Replays a manifest up to a sequence number and returns its canonical state.</summary>
    private static string ReplayPrefix(
        string manifestPath, int upToSeq, string statePath, int? lineFromSeq = null)
    {
        var command = lineFromSeq is null ? "replay" : "replay-line";
        var arguments = new List<string>
        {
            command,
            manifestPath,
            "--state-out",
            statePath,
            "--stop-after",
            upToSeq.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (lineFromSeq is { } start)
        {
            arguments.Add("--line-from");
            arguments.Add(start.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var child = SelfProcess.Run([.. arguments]);
        if (child.ExitCode != 0)
        {
            Console.Write(child.StandardOutput);
            Console.Error.Write(child.StandardError);
            throw new ManifestException($"Replaying {Path.GetFileName(manifestPath)} up to action {upToSeq} did not succeed.");
        }
        return File.ReadAllText(statePath);
    }

    private static IReadOnlyList<ActionRecord> LoadLine(string path) =>
        JsonSerializer.Deserialize<List<ActionRecord>>(File.ReadAllText(path), ManifestJson.Options)
        ?? throw new ManifestException($"Line file {Path.GetFileName(path)} did not deserialize to a list of actions.");

    private static IEnumerable<ActionRecord> Renumber(IReadOnlyList<ActionRecord> line, int startSeq) =>
        line.Select((action, index) => action with { Seq = startSeq + index });

    private static string DigestOf(string canonical)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        return "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static IReadOnlyList<StateDelta> Diff(string before, string after)
    {
        var left = ParseState(before);
        var right = ParseState(after);
        return left.Keys.Union(right.Keys)
            .Order(StringComparer.Ordinal)
            .Select(key =>
            {
                left.TryGetValue(key, out var l);
                right.TryGetValue(key, out var r);
                return new StateDelta(key, l ?? "<absent>", r ?? "<absent>");
            })
            .Where(d => !string.Equals(d.Before, d.After, StringComparison.Ordinal))
            .ToList();
    }

    internal sealed record StateDelta(string Field, string Before, string After);
}
