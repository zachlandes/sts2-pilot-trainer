using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int Preflight(string[] args)
    {
        var manifest = ManifestJson.Load(Args.Positional(args, 0, "manifest path"));
        var result = Engine.Preflight.Evaluate(manifest.Environment);

        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine();
        foreach (var field in result.Fields)
        {
            var mark = field.Matches ? "ok  " : "FAIL";
            Console.WriteLine($"  {mark} {field.Field,-16} manifest={field.Expected,-30} local={field.Actual}");
            if (!field.Matches) Console.WriteLine($"       {field.Diagnostic}");
        }

        Console.WriteLine();
        Console.WriteLine("acts this build ships:");
        foreach (var act in Engine.EngineHost.AvailableActs()) Console.WriteLine($"  {act}");

        Console.WriteLine();
        Console.WriteLine(result.Matches
            ? "environment matches; replay may proceed"
            : "environment does NOT match; refusing to replay");
        return result.Matches ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var manifest = ManifestJson.Load(manifestPath);
        var outPath = Args.Value(args, "--out");
        var stopAfter = Args.Value(args, "--stop-after") is { } raw
            ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
            : (int?)null;

        var progress = Enum.Parse<PlayerProgress>(Args.Value(args, "--progress") ?? "AllUnlocked", ignoreCase: true);
        var outcome = Arbiter.Run(manifest, stopAfter, progress);
        var report = outcome.Report;

        Console.WriteLine($"manifest       : {manifest.RunId}");
        Console.WriteLine($"actions        : {manifest.Actions.Count}");
        Console.WriteLine($"status         : {report.Status.ToString().ToUpperInvariant()}");
        Console.WriteLine();

        foreach (var checkpoint in report.Checkpoints)
        {
            Console.WriteLine($"  {(checkpoint.Passed ? "ok  " : "FAIL")} checkpoint {checkpoint.Id} (after action {checkpoint.AfterSeq})");
            foreach (var comparison in checkpoint.Comparisons)
            {
                var mark = comparison.Matches ? " " : "!";
                Console.WriteLine($"      {mark} {comparison.Field,-28} observed={comparison.Expected,-22} engine={comparison.Actual}");
            }
        }

        if (report.Diagnostics.Count > 0)
        {
            Console.WriteLine();
            foreach (var diagnostic in report.Diagnostics) Console.WriteLine($"  ! {diagnostic}");
        }

        Console.WriteLine();
        Console.WriteLine($"final state digest : {report.FinalStateDigest ?? "(none)"}");
        Console.WriteLine($"action history hash: {report.ActionHistoryHash ?? "(none)"}");

        if (outPath is not null)
        {
            ManifestJson.Save(manifest with { Verification = report }, outPath);
            Console.WriteLine($"verified manifest  : {Paths.Display(outPath)}");
        }

        // The canonical state is written beside the manifest so a divergence can be
        // read as a diff rather than guessed at from a digest that simply differs.
        var statePath = Args.Value(args, "--state-out");
        if (statePath is not null && outcome.FinalState is not null)
        {
            File.WriteAllText(statePath, outcome.FinalState.Render());
            Console.WriteLine($"canonical state    : {Paths.Display(statePath)}");
        }

        return report.Status is VerificationStatus.Verified or VerificationStatus.Partial ? 0 : 1;
    }

    /// <summary>
    /// Replays the same manifest in several fresh processes and compares the
    /// canonical state each one ends with.
    ///
    /// Separate processes, not separate sessions: the engine keeps static state, and
    /// a second run in the same process inherits it. A determinism claim that only
    /// holds within one process is not the claim anyone wants.
    /// </summary>
    internal static int Determinism(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var runs = int.Parse(Args.Value(args, "--runs") ?? "2", System.Globalization.CultureInfo.InvariantCulture);
        var outDir = Args.Value(args, "--out") ?? "build/evidence";

        if (runs < 2)
        {
            throw new ManifestException("determinism needs --runs 2 or more; one run cannot disagree with anything.");
        }

        Directory.CreateDirectory(outDir);
        var states = new List<string>();
        var digests = new List<string>();

        for (var i = 0; i < runs; i++)
        {
            var statePath = Path.Combine(outDir, $"determinism-run{i}.state");
            var child = SelfProcess.Run("replay", manifestPath, "--state-out", statePath);
            if (child.ExitCode != 0)
            {
                Console.Write(child.StandardOutput);
                Console.Error.Write(child.StandardError);
                Console.Error.WriteLine($"run {i} did not verify; determinism is not meaningful until it does.");
                return child.ExitCode;
            }

            var state = File.ReadAllText(statePath);
            states.Add(state);
            var digest = child.StandardOutput
                .Split('\n')
                .FirstOrDefault(l => l.StartsWith("final state digest", StringComparison.Ordinal))
                ?.Split(':', 2)[1].Trim() ?? "(unknown)";
            digests.Add(digest);
            Console.WriteLine($"run {i}: {digest}");
        }

        var identical = states.Distinct(StringComparer.Ordinal).Count() == 1;
        Console.WriteLine();
        Console.WriteLine(identical
            ? $"all {runs} fresh processes produced byte-identical canonical state"
            : $"canonical state DIFFERS across {runs} fresh processes");

        if (!identical)
        {
            var reference = ParseState(states[0]);
            for (var i = 1; i < states.Count; i++)
            {
                foreach (var difference in CanonicalStateDiff(reference, ParseState(states[i])))
                {
                    Console.WriteLine($"  run0 vs run{i}: {difference}");
                }
            }
        }

        File.WriteAllText(
            Path.Combine(outDir, "determinism.json"),
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/determinism/v1",
                manifest = Path.GetFileName(manifestPath),
                runs,
                identical,
                digests,
                excluded_by_design = CanonicalState.ExcludedByDesign,
            }, Json.Indented) + "\n");

        return identical ? 0 : 1;
    }

    private static Dictionary<string, string> ParseState(string rendered) =>
        rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.Ordinal);

    private static IEnumerable<string> CanonicalStateDiff(
        Dictionary<string, string> left, Dictionary<string, string> right)
    {
        foreach (var key in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(key, out var l);
            right.TryGetValue(key, out var r);
            if (!string.Equals(l, r, StringComparison.Ordinal))
            {
                yield return $"{key}: '{l ?? "<absent>"}' vs '{r ?? "<absent>"}'";
            }
        }
    }
}
