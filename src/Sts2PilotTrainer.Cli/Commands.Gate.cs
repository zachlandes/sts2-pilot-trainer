using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// The publication gate: may this reconstruction be published as exact?
    ///
    /// One verdict, from running the whole chain. It exists so that "publishable" is
    /// something the tools compute rather than something a person concludes from a
    /// wall of green, and so that the standard is written in one place instead of
    /// being reassembled from a document each time.
    ///
    /// The standard is deliberately narrow and deliberately expensive. Nothing here
    /// may be stood in for by a cheaper proxy - not reader confidence, not arithmetic
    /// on the footage, not a screenshot of a mod list. Those are useful filters and
    /// they are not evidence: four of the ten corruptions in the replay controls pass
    /// every arithmetic check available from the frames, and a run resumed from
    /// history passes every check that is not about the recording itself.
    /// </summary>
    internal static int Gate(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";

        // A different question, asked through the same command because it is the same
        // subject: this one is whether a recording already published still reproduces
        // on the build installed now. It has its own answer file and does not write a
        // publication verdict, because "still works" is not "may be published".
        if (Args.Value(args, "--rekey") is not null) return Rekey(args, manifestPath);

        var gateArtifact = EvidenceArtifact.Prepare(outDir, "publication-gate.json");
        var manifest = ManifestJson.Load(manifestPath);
        var mapObservationPath = Args.Value(args, "--map-observation") ??
            (manifestPath.EndsWith(".replay.json", StringComparison.Ordinal)
                ? manifestPath[..^".replay.json".Length] + ".map-observation.json"
                : manifestPath + ".map-observation.json");
        var baseLibPath = Args.Value(args, "--baselib") ?? "build/parity/BaseLib.dll";

        var conditions = new List<Condition>
        {
            new(
                "publication-source",
                "Publication evidence comes from a VOD, never an engine-generated fixture.",
                manifest.Source.Kind == "vod"),

            Check("provenance",
                "The recording is of the run it claims, from that run's start.",
                SelfProcess.Run("validate", manifestPath, "--show-rejections", "--out", outDir)),
        };

        if (conditions.All(condition => condition.Passed))
        {
            var environment = Check("environment",
                "The declared build and content hash match this machine, and the declared mode is supported.",
                SelfProcess.Run("preflight", manifestPath));
            conditions.Add(environment);

            if (environment.Passed)
            {
                var modeReportPath = Path.Combine(outDir, "mode-discrimination.json");
                var modeCondition = Check("game-mode",
                    "Engine evidence establishes the source mode or path-specific parity for every viable mode.",
                    SelfProcess.Run(
                        "mode-discrimination", manifestPath,
                        "--out", modeReportPath),
                    forwardOutput: true);
                conditions.Add(modeCondition);

                conditions.Add(Check("seed-topology",
                "The manifest seed independently reproduces the map observed in the same VOD.",
                SelfProcess.Run(
                    "verify-seed", mapObservationPath,
                    "--candidates", string.Join(",",
                        manifest.Environment.Seed.Value,
                        NegativeControlSeed(manifest.Environment.Seed.Value)),
                    "--manifest", manifestPath,
                    "--acts", string.Join(",", manifest.Environment.Acts.Value),
                    "--character", manifest.Environment.Character.Value,
                    "--ascension", manifest.Environment.Ascension.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    "--game-mode", manifest.Environment.GameMode.Value,
                    "--out", outDir)));

                var baseLibReportPath = Path.Combine(outDir, "baselib-reachability.json");
                var baseLibCondition = Check("baselib-path",
                    "The measured BaseLib behavior branch is unreachable in this exact reconstructed history.",
                    SelfProcess.Run(
                        "baselib-reachability", manifestPath, baseLibPath,
                        "--out", baseLibReportPath));
                conditions.Add(baseLibCondition);

                conditions.Add(modeCondition.Passed && baseLibCondition.Passed
                    ? CrossBindEvidence(modeReportPath, baseLibReportPath)
                    : new Condition(
                        "evidence-binding",
                        "Mode and BaseLib evidence bind to one build and reconstructed history.",
                        false,
                        "Evidence binding requires passing mode-discrimination and BaseLib-reachability reports."));

                var verifiedPath = Path.Combine(outDir, "verified-manifest.json");
                var reproduction = Check("reproduction",
                "The reconstructed history replays through the real engine and matches every observed value.",
                SelfProcess.Run("replay", manifestPath, "--out", verifiedPath));
                conditions.Add(reproduction);

                conditions.Add(reproduction.Passed
                    ? CoveredFightIsComplete(verifiedPath)
                    : new Condition(
                        "covered-fight", CoveredFightRequirement, false,
                        "A completed fight can only be read out of a verified reproduction."));

                conditions.Add(Check(
                    "combat-boundary",
                    CombatBoundaryRequirement,
                    SelfProcess.Run("combat-snapshot", manifestPath, "--out", outDir)));

                conditions.Add(Check("determinism",
                "Fresh processes produce byte-identical canonical state.",
                SelfProcess.Run("determinism", manifestPath, "--runs", "2", "--out", outDir)));

                conditions.Add(Check("rejection", RejectionRequirement,
                    SelfProcess.Run(
                        "negative-controls", manifestPath, "--out", outDir,
                        "--require-all-controls")));
            }
            else
            {
                AddSkippedEngineConditions(conditions);
            }
        }
        else
        {
            conditions.Add(new Condition(
                "environment", "The declared build and content hash match this machine, and the declared mode is supported.", false));
            AddSkippedEngineConditions(conditions);
        }

        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine();
        foreach (var condition in conditions)
        {
            Console.WriteLine($"  {(condition.Passed ? "pass" : "FAIL")}  {condition.Name,-16} {condition.Requirement}");
            if (!condition.Passed && condition.Diagnostic is not null)
            {
                Console.WriteLine($"       {condition.Diagnostic}");
            }
        }

        var publishable = conditions.All(c => c.Passed);
        Console.WriteLine();
        Console.WriteLine(publishable
            ? "PUBLISHABLE - every condition of the gate holds"
            : "NOT PUBLISHABLE - see the failing condition above");

        gateArtifact.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/publication-gate/v1",
                manifest = Path.GetFileName(manifestPath),
                publishable,
                // Recorded with the verdict so an artifact can never be read as having
                // met a weaker standard than the one that was actually applied.
                standard =
                    "Successful real-engine headless reproduction. No proxy is accepted in place of any " +
                    "condition: not reader confidence, not arithmetic over the footage, not a screenshot of a " +
                    "mod list. Each is a useful filter and none is evidence.",
                conditions = conditions.Select(c => new
                {
                    name = c.Name,
                    requirement = c.Requirement,
                    passed = c.Passed,
                    diagnostic = c.Diagnostic,
                }),
            }, Json.Indented) + "\n");

        return publishable ? 0 : 1;
    }

    private static void AddSkippedEngineConditions(List<Condition> conditions) => conditions.AddRange(
    [
        new Condition("game-mode",
            "Engine evidence establishes the source mode or path-specific parity for every viable mode.", false),
        new Condition("seed-topology",
            "The manifest seed independently reproduces the map observed in the same VOD.", false),
        new Condition("baselib-path",
            "The measured BaseLib behavior branch is unreachable in this exact reconstructed history.", false),
        new Condition("evidence-binding",
            "Mode and BaseLib evidence bind to one build and reconstructed history.", false),
        new Condition("reproduction",
            "The reconstructed history replays through the real engine and matches every observed value.", false),
        new Condition("covered-fight", CoveredFightRequirement, false),
        new Condition("combat-boundary", CombatBoundaryRequirement, false),
        new Condition("determinism",
            "Fresh processes produce byte-identical canonical state.", false),
        new Condition("rejection", RejectionRequirement, false),
    ]);

    private const string RejectionRequirement =
        "Every required corruption applies, and corrupted and incomplete histories are refused.";

    private const string CoveredFightRequirement =
        "The reproduced history covers a whole fight, from its combat start to the end of that fight.";

    private const string CombatBoundaryRequirement =
        "The manifest's combat-start snapshot digest matches a fresh real-engine derivation.";

    /// <summary>
    /// Whether the verified history covers a fight that finished.
    ///
    /// The unit of the product is the whole fight and the boundary is combat start,
    /// so a reconstruction that stops mid-combat is not publishable as a solution to
    /// one - every quantity the comparison reports is defined at the end of a fight.
    /// Read from the trace the reproduction just wrote, and asked through the type
    /// that owns the question, so the gate and the projection cannot disagree about
    /// whether a fight ended. See docs/comparison-direction.md.
    /// </summary>
    private static Condition CoveredFightIsComplete(string verifiedManifestPath)
    {
        try
        {
            var trace = ManifestJson.Load(verifiedManifestPath).Verification?.Trace
                ?? throw new InvalidOperationException(
                    "the verified manifest carries no trace, so the fight cannot be read out of it");
            var coverage = CombatProjection.CoverageOf(trace);
            return new Condition(
                "covered-fight", CoveredFightRequirement, coverage.IsCompletedFight, coverage.Refusal);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ManifestException or InvalidOperationException)
        {
            return new Condition(
                "covered-fight", CoveredFightRequirement, false,
                $"The covered fight could not be read: {exception.Message}");
        }
    }

    private static Condition Check(
        string name,
        string requirement,
        SelfProcess.Result result,
        bool forwardOutput = false)
    {
        if (forwardOutput || result.ExitCode != 0)
        {
            Console.Write(result.StandardOutput);
            Console.Error.Write(result.StandardError);
        }
        return new Condition(name, requirement, result.ExitCode == 0);
    }

    private static Condition CrossBindEvidence(string modeReportPath, string baseLibReportPath)
    {
        const string requirement =
            "Mode and BaseLib evidence bind to one build and reconstructed history.";
        try
        {
            var mode = ReadBinding(modeReportPath, "mode-discrimination", "standard",
                "path_specific_mode_parity");
            var baseLib = ReadBinding(baseLibReportPath, "baselib-reachability", "history",
                "path_specific_parity_established");
            var failed = new[] { mode, baseLib }
                .Where(binding => binding.Fields["internal_pass"] != bool.TrueString)
                .Select(binding => $"{binding.Source}.internal_pass")
                .ToList();
            if (failed.Count > 0)
            {
                return new Condition(
                    "evidence-binding", requirement, false,
                    $"Evidence reports did not pass internally: {string.Join(", ", failed)}.");
            }

            var comparison = EvidenceBindingComparer.Compare(mode, baseLib);
            var diagnostic = comparison.Bound
                ? null
                : string.Join("; ", comparison.Mismatches.Select(mismatch =>
                    $"{mismatch.Field}: {mismatch.LeftSource}='{mismatch.LeftValue}', " +
                    $"{mismatch.RightSource}='{mismatch.RightValue}'"));
            return new Condition("evidence-binding", requirement, comparison.Bound, diagnostic);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new Condition(
                "evidence-binding", requirement, false,
                $"Evidence binding could not read a current report: {exception.Message}");
        }
    }

    /// <summary>
    /// Reduces one probe's report to the values that say which reconstruction it is
    /// about, plus whether it passed on its own terms.
    ///
    /// The two probes have different names for "I passed", which is why that is a
    /// parameter. Everything else is the same question asked of both reports, and
    /// binding them is what stops a stale report on disk being read as a fresh one.
    /// </summary>
    private static EvidenceBinding ReadBinding(
        string path,
        string source,
        string evidenceProperty,
        string passProperty)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var report = document.RootElement;
        var evidence = report.GetProperty(evidenceProperty);
        var passed = report.GetProperty("instrument_passed").GetBoolean() &&
                     report.GetProperty(passProperty).GetBoolean();
        return EvidenceBinding.Of(source,
        [
            ("internal_pass", passed ? bool.TrueString : bool.FalseString),
            ("run_id", evidence.GetProperty("RunId").GetString()!),
            ("video_id", evidence.GetProperty("VideoId").GetString()!),
            ("build_version", evidence.GetProperty("BuildVersion").GetString()!),
            ("build_commit", evidence.GetProperty("BuildCommit").GetString()!),
            ("seed", evidence.GetProperty("Seed").GetString()!),
            ("action_history_hash", evidence.GetProperty("ActionHistoryHash").GetString()!),
            ("final_state_sha256", evidence.GetProperty("FinalStateSha256").GetString()!),
        ]);
    }

    private sealed record Condition(
        string Name,
        string Requirement,
        bool Passed,
        string? Diagnostic = null);
}
