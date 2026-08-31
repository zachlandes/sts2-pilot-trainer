using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// The prerequisite gate: is this the machine, with the progress, that could
    /// replay this run at all?
    ///
    /// <c>--progress</c> chooses whose unlock state is checked. The mod passes
    /// <c>local-profile</c> and gates on what the player actually has. The headless
    /// arbiter defaults to <c>all-unlocked</c>, which is the state it will construct
    /// the run with - the same question asked of a host rather than of a person, and
    /// reported as such rather than as a reading of anybody's save.
    /// </summary>
    internal static int Preflight(string[] args)
    {
        var manifest = ManifestJson.Load(Args.Positional(args, 0, "manifest path"));
        var progress = ParseProgress(args);
        var result = Engine.Preflight.Evaluate(manifest.Environment, progress);

        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine($"progress : {progress}");
        Console.WriteLine();
        PrintFields(result);

        Console.WriteLine();
        Console.WriteLine("acts this build ships:");
        foreach (var act in Engine.EngineHost.AvailableActs()) Console.WriteLine($"  {act}");

        Console.WriteLine();
        Console.WriteLine(result.Matches
            ? "environment matches; replay may proceed"
            : "environment does NOT match; refusing to replay");
        return result.Matches ? 0 : 1;
    }

    /// <summary>
    /// The mod's gate, run against a real run: start a run at a stated identity - the
    /// stand-in for the player having started one - then read it back out of the game
    /// and compare every dimension against the manifest.
    ///
    /// The point of driving it this way is that nothing is taken on trust. The run
    /// identity reported is the one the engine holds, not the one this command was
    /// asked for, so passing a different seed here produces a genuine refusal rather
    /// than a rehearsed one.
    /// </summary>
    internal static int PreflightLive(string[] args)
    {
        var manifest = ManifestJson.Load(Args.Positional(args, 0, "manifest path"));
        var environment = manifest.Environment;
        var progress = ParseProgress(args);

        var seed = Args.Value(args, "--seed") ?? environment.Seed.Value;
        var gameMode = Args.Value(args, "--game-mode") ?? environment.GameMode.Value;
        var character = Args.Value(args, "--character") ?? environment.Character.Value;
        var ascension = Args.Value(args, "--ascension") is { } raw
            ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
            : environment.Ascension.Value;
        var acts = Args.Value(args, "--acts") is { } actList
            ? actList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : environment.Acts.Value.ToArray();

        var prerequisites = Engine.Preflight.Evaluate(environment, progress);

        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine($"progress : {progress}");
        Console.WriteLine($"started  : seed={seed} mode={gameMode} ascension={ascension} character={character}");
        Console.WriteLine();
        PrintFields(prerequisites);

        // The run is started even when the prerequisites failed, so that the run
        // identity is reported too. A refusal that stops at the first failing field
        // sends someone back for a second run to find the next one.
        PreflightResult runIdentity;
        try
        {
            new GameSession().StartRun(seed, character, ascension, gameMode, acts, progress);
            runIdentity = Engine.Preflight.EvaluateStartedRun(environment);
        }
        catch (EngineException ex)
        {
            runIdentity = new PreflightResult(false,
            [
                new PreflightField("run_present", "a run matching this manifest", "could not be started", false,
                    ex.Message),
            ]);
        }

        PrintFields(runIdentity);

        var combined = EnvironmentPreflight.Combine(prerequisites, runIdentity);
        Console.WriteLine();
        Console.WriteLine(combined.Matches
            ? "environment and run match; replay may proceed"
            : "environment or run does NOT match; refusing to replay");
        return combined.Matches ? 0 : 1;
    }

    private static PlayerProgress ParseProgress(string[] args) =>
        Enum.Parse<PlayerProgress>(
            (Args.Value(args, "--progress") ?? "AllUnlocked").Replace("-", string.Empty, StringComparison.Ordinal),
            ignoreCase: true);

    private static void PrintFields(PreflightResult result)
    {
        foreach (var field in result.Fields)
        {
            var mark = field.Matches ? "ok  " : "FAIL";
            Console.WriteLine($"  {mark} {field.Field,-22} manifest={field.Expected,-30} local={field.Actual}");
            if (!field.Matches) Console.WriteLine($"       {field.Diagnostic}");
        }
    }

    internal static int Replay(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outPath = Args.Value(args, "--out");
        var outArtifact = outPath is null ? null : EvidenceArtifact.PreparePath(outPath);
        var statePath = Args.Value(args, "--state-out");
        var stateArtifact = statePath is null ? null : EvidenceArtifact.PreparePath(statePath);
        var manifest = ManifestJson.Load(manifestPath);
        var stopAfter = Args.Value(args, "--stop-after") is { } raw
            ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
            : (int?)null;

        var progress = ParseProgress(args);
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

        if (Args.Has(args, "--show-trace") && report.Trace is { } trace)
        {
            // An inspection view, not a projection anyone should build on: it prints
            // the sampled fields that changed at each step. The artifact is the stored
            // trace, which keeps both samples whole; see docs/comparison-direction.md.
            Console.WriteLine();
            Console.WriteLine("trace (sampled fields that changed at each step):");
            foreach (var step in trace.Steps)
            {
                var changed = step.After
                    .Where(field => !step.Before.TryGetValue(field.Key, out var was)
                                    || !string.Equals(was, field.Value, StringComparison.Ordinal))
                    .Select(field =>
                        $"{field.Key} {step.Before.GetValueOrDefault(field.Key, "-")} -> {field.Value}")
                    .ToList();
                Console.WriteLine($"  {step.Seq,3} {step.Verb}");
                foreach (var line in changed) Console.WriteLine($"        {line}");
                if (changed.Count == 0) Console.WriteLine("        (nothing sampled changed)");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"final state digest : {report.FinalStateDigest ?? "(none)"}");
        Console.WriteLine($"action history hash: {report.ActionHistoryHash ?? "(none)"}");

        if (outArtifact is not null)
        {
            outArtifact.WriteAtomic(ManifestJson.Serialize(manifest with { Verification = report }) + "\n");
            Console.WriteLine($"verified manifest  : {Paths.Display(outArtifact.Path)}");
        }

        // The canonical state is written beside the manifest so a divergence can be
        // read as a diff rather than guessed at from a digest that simply differs.
        if (stateArtifact is not null && outcome.FinalState is not null)
        {
            stateArtifact.WriteAtomic(outcome.FinalState.Render());
            Console.WriteLine($"canonical state    : {Paths.Display(stateArtifact.Path)}");
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
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var determinismArtifact = EvidenceArtifact.Prepare(outDir, "determinism.json");
        var runs = int.Parse(Args.Value(args, "--runs") ?? "2", System.Globalization.CultureInfo.InvariantCulture);

        if (runs < 2)
        {
            throw new ManifestException("determinism needs --runs 2 or more; one run cannot disagree with anything.");
        }

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

        determinismArtifact.WriteAtomic(
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
