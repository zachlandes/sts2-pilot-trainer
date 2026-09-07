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

        var gateArtifact = EvidenceArtifact.Prepare(outDir, "publication-gate.json");
        var manifest = ManifestJson.Load(manifestPath);
        var mapObservationPath = Args.Value(args, "--map-observation") ??
            (manifestPath.EndsWith(".replay.json", StringComparison.Ordinal)
                ? manifestPath[..^".replay.json".Length] + ".map-observation.json"
                : manifestPath + ".map-observation.json");
        var baseLibPath = Args.Value(args, "--baselib") ?? "build/parity/BaseLib.dll";

        // Which conditions apply is decided by where the recording came from, and only
        // that. Four of them are absent for a recording this project's own recorder
        // made inside the player's game, and they are absent for three different
        // reasons, which are worth keeping apart because one of them is a weakness.
        //
        // Two read a public video - the map a seed has to reproduce, and the mode an
        // overlay implies - and there is no video. A third, the binding between the
        // mode and BaseLib reports, needs a mode report, and the mode-discrimination
        // probe writes none unless the source is a VOD.
        //
        // The fourth, baselib-path, is different and is a weaker standard rather than
        // an inapplicable one. Its probe measures reachability by replaying the history
        // against BaseLib.dll and refuses any manifest that is not a VOD, so there is
        // no measurement to be had here yet. What stands in its place for a native
        // recording is the mod's own declaration that it does not affect gameplay,
        // which EnvironmentPreflight reads off the loaded mod set. That is a
        // self-report, not an engine measurement, and it is what a native gate rests on
        // until the probe can replay a recorded history.
        //
        // Nothing else weaker stands in. What a video reading infers about the mode,
        // the seed's map and the player's unlocks, a recorder read out of the running
        // game and wrote down as captured facts, and the validator refuses a native
        // recording whose start nobody witnessed or whose watch has a hole in it -
        // which are the two things no replay could establish afterwards. Every engine
        // condition below is the same for both kinds.
        var isNative = manifest.Source.Kind == "native";
        var conditions = new List<Condition>
        {
            new(
                "publication-source",
                "Publication evidence comes from a VOD or from this project's own recorder, never an " +
                "engine-generated fixture.",
                manifest.Source.Kind is "vod" or "native"),

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
                if (!isNative)
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
                }

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

                conditions.Add(reproduction.Passed
                    ? DeclaredBoundariesHold(verifiedPath)
                    : new Condition(
                        "declared-boundaries", DeclaredBoundariesRequirement, false,
                        "A declared boundary can only be checked against a verified reproduction."));

                conditions.Add(reproduction.Passed
                    ? BoundaryDigestsHold(manifestPath, verifiedPath)
                    : new Condition(
                        "combat-boundary", CombatBoundaryRequirement, false,
                        "Boundary digests can only be compared against a verified reproduction."));

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
                AddSkippedEngineConditions(conditions, isNative);
            }
        }
        else
        {
            conditions.Add(new Condition(
                "environment", "The declared build and content hash match this machine, and the declared mode is supported.", false));
            AddSkippedEngineConditions(conditions, isNative);
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
                    "mod list. Each is a useful filter and none is evidence." +
                    (isNative
                        ? " This recording was made by this project's own recorder inside the player's game, " +
                          "so four conditions were not asked. Two read a public video and there is none: no " +
                          "map a seed could be made to reproduce, and no overlay that could imply a mode. The " +
                          "third, the binding between the mode and BaseLib reports, needs a mode report that " +
                          "only a VOD produces. The fourth, baselib-path, was NOT EVALUATED because its probe " +
                          "measures reachability by replaying a VOD manifest and refuses any other kind, so no " +
                          "measurement of BaseLib's affected branch exists for this recording. What stood in " +
                          "its place is WEAKER: the environment check accepted every loaded mod's own " +
                          "declaration that it does not affect gameplay, which is a self-report rather than an " +
                          "engine measurement. What the other three establish for a video, the recorder read " +
                          "out of the running game and recorded as captured facts, and the provenance " +
                          "condition refuses a recording whose start nobody witnessed or whose watch has a " +
                          "hole in it."
                        : string.Empty),
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

    /// <summary>
    /// The conditions nothing got as far as computing, reported as failures.
    ///
    /// The four a native recording never asks are listed only for a kind that asks
    /// them: a native recording that fell over at its environment check has not failed
    /// a seed-topology condition, because there is no video whose map a seed could
    /// reproduce, and it has not failed baselib-path, because the probe would refuse
    /// its manifest.
    /// </summary>
    private static void AddSkippedEngineConditions(List<Condition> conditions, bool isNative)
    {
        if (!isNative)
        {
            conditions.AddRange(
            [
                new Condition("game-mode",
                    "Engine evidence establishes the source mode or path-specific parity for every viable mode.",
                    false),
                new Condition("seed-topology",
                    "The manifest seed independently reproduces the map observed in the same VOD.", false),
                new Condition("baselib-path",
                    "The measured BaseLib behavior branch is unreachable in this exact reconstructed history.",
                    false),
                new Condition("evidence-binding",
                    "Mode and BaseLib evidence bind to one build and reconstructed history.", false),
            ]);
        }

        conditions.AddRange(
    [
        new Condition("reproduction",
            "The reconstructed history replays through the real engine and matches every observed value.", false),
        new Condition("covered-fight", CoveredFightRequirement, false),
        new Condition("declared-boundaries", DeclaredBoundariesRequirement, false),
        new Condition("combat-boundary", CombatBoundaryRequirement, false),
        new Condition("determinism",
            "Fresh processes produce byte-identical canonical state.", false),
        new Condition("rejection", RejectionRequirement, false),
    ]);
    }

    private const string RejectionRequirement =
        "Every required corruption applies, and corrupted and incomplete histories are refused.";

    private const string CoveredFightRequirement =
        "The reproduced history covers a whole fight, from its combat start to the end of that fight.";

    private const string DeclaredBoundariesRequirement =
        "Every boundary the recording declares is one the verified history reaches, at the action it names.";

    private const string CombatBoundaryRequirement =
        "Every compatible boundary digest in the manifest matches the real-engine reproduction.";

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

    /// <summary>
    /// Whether the boundaries the recording declares agree with the history that just
    /// reproduced.
    ///
    /// The validate condition above reads the file on disk, which carries no trace, so
    /// every cross-check between a declared boundary and the run it names sits idle
    /// there - and that file is the one a recorder wrote or a stranger submitted. This
    /// asks the same validator of the copy the replay just produced, where the trace
    /// is, so a boundary naming a fight the run never held or a floor it never reached
    /// is refused at publication rather than in front of a player.
    /// </summary>
    private static Condition DeclaredBoundariesHold(string verifiedManifestPath)
    {
        try
        {
            var refusal = ManifestValidator.RefusalForVerified(ManifestJson.Load(verifiedManifestPath));
            return new Condition(
                "declared-boundaries", DeclaredBoundariesRequirement, refusal is null, refusal);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ManifestException or InvalidOperationException)
        {
            return new Condition(
                "declared-boundaries", DeclaredBoundariesRequirement, false,
                $"The verified manifest could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Whether every boundary both the recording and the verified replay hold has the
    /// same digest.
    ///
    /// Compatibility means the kind's own coordinate: fight for combat start, floor
    /// for floor entry, and fight plus turn for turn start. The declared-boundaries
    /// condition separately proves that declarations point at places the replay
    /// reached; this condition asks the independent reading neither shape check can:
    /// whether the complete hidden state at every shared place is the same.
    /// </summary>
    private static Condition BoundaryDigestsHold(string manifestPath, string verifiedManifestPath)
    {
        try
        {
            var declared = ManifestJson.Load(manifestPath).Boundaries;
            var derived = ManifestJson.Load(verifiedManifestPath).Verification?.Boundaries
                ?? throw new InvalidOperationException(
                    "the verified manifest carries no verification report, so its boundaries cannot be read");

            var compatible = declared
                .Select(boundary => new
                {
                    Declared = boundary,
                    Derived = derived.FirstOrDefault(candidate =>
                        string.Equals(candidate.Kind, boundary.Kind, StringComparison.Ordinal) &&
                        candidate.Fight == boundary.Fight &&
                        candidate.Floor == boundary.Floor &&
                        candidate.Turn == boundary.Turn),
                })
                .Where(pair => pair.Derived is not null)
                .ToList();
            var mismatches = compatible
                .Where(pair => !string.Equals(
                    pair.Declared.Digest.Value, pair.Derived!.Digest.Value, StringComparison.Ordinal))
                .Select(pair =>
                    $"{pair.Declared.Describe()}: the recording declares {pair.Declared.Digest.Value}, " +
                    $"the engine produced {pair.Derived!.Digest.Value}")
                .ToList();

            return new Condition(
                "combat-boundary", CombatBoundaryRequirement, mismatches.Count == 0,
                mismatches.Count == 0 ? null : $"Boundary digest mismatches: {string.Join("; ", mismatches)}.");
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ManifestException or InvalidOperationException)
        {
            return new Condition(
                "combat-boundary", CombatBoundaryRequirement, false,
                $"Boundary digests could not be compared: {exception.Message}");
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
