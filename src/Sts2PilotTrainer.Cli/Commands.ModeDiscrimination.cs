using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int ModeDiscriminationProbe(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("mode-discrimination-probe needs --out <path>.");
        var variant = Args.Value(args, "--variant")
            ?? throw new ManifestException("mode-discrimination-probe needs --variant <name>.");
        var artifact = EvidenceArtifact.PreparePath(outPath);
        var result = Engine.ModeDiscriminationProbe.Run(ManifestJson.Load(manifestPath), variant);
        artifact.WriteAtomic(JsonSerializer.Serialize(result, Json.Indented) + "\n");
        return 0;
    }

    internal static int ModeDiscrimination(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("mode-discrimination needs --out <path>.");
        var reportArtifact = EvidenceArtifact.PreparePath(outPath);
        var outDir = Path.GetDirectoryName(reportArtifact.Path)!;
        var variants = new[]
        {
            "standard", "custom-default", "daily-default", "custom-negative", "checkpoint-negative",
        };
        var results = new Dictionary<string, ModeDiscriminationResult>(StringComparer.Ordinal);

        foreach (var variant in variants)
        {
            var probePath = Path.Combine(outDir, $"mode-discrimination-{variant}.json");
            var child = SelfProcess.Run(
                "mode-discrimination-probe", manifestPath,
                "--variant", variant, "--out", probePath);
            if (child.ExitCode != 0)
            {
                Console.Write(child.StandardOutput);
                Console.Error.Write(child.StandardError);
                return child.ExitCode;
            }
            results[variant] = JsonSerializer.Deserialize<ModeDiscriminationResult>(
                File.ReadAllText(probePath), ManifestJson.Options)!;
        }

        var standard = results["standard"];
        var modifierResults = new List<(string Type, ModeDiscriminationResult Result)>();
        foreach (var modifierType in standard.AvailableModifierTypes)
        {
            var variant = Engine.ModeDiscriminationProbe.ModifierVariantPrefix + modifierType;
            var shortName = modifierType[(modifierType.LastIndexOf('.') + 1)..];
            var probePath = Path.Combine(outDir, $"mode-discrimination-modifier-{shortName}.json");
            var child = SelfProcess.Run(
                "mode-discrimination-probe", manifestPath,
                "--variant", variant, "--out", probePath);
            if (child.ExitCode != 0)
            {
                Console.Write(child.StandardOutput);
                Console.Error.Write(child.StandardError);
                return child.ExitCode;
            }
            modifierResults.Add((
                modifierType,
                JsonSerializer.Deserialize<ModeDiscriminationResult>(
                    File.ReadAllText(probePath), ManifestJson.Options)!));
        }

        var custom = results["custom-default"];
        var daily = results["daily-default"];
        var negative = results["custom-negative"];
        var checkpointNegative = results["checkpoint-negative"];
        var baselineBinding = ProbeBinding(standard);
        var bindingMismatches = results
            .Where(entry => entry.Key != "standard")
            .Select(entry => entry.Value)
            .Concat(modifierResults.Select(entry => entry.Result))
            .SelectMany(result => ModeProbeBindingComparer.Compare(
                baselineBinding, ProbeBinding(result)).Mismatches)
            .ToList();
        var bindingsMatch = bindingMismatches.Count == 0;
        var modifierOutcomes = bindingsMatch
            ? modifierResults.Select(entry => new ModifierOutcome(
                entry.Type,
                Classify(standard, entry.Result),
                entry.Result.CompletedHistory,
                entry.Result.AllCheckpointsPassed,
                entry.Result.CheckpointSha256,
                entry.Result.BehavioralStateSha256,
                entry.Result.FinalStateSha256)).ToList()
            : [];
        var unboundModifiers = modifierOutcomes
            .Where(outcome => outcome.Classification == StateOnlyDivergence)
            .Select(outcome => outcome.Type)
            .ToList();
        var singleModifierParity = modifierOutcomes.Count > 0 && unboundModifiers.Count == 0;
        var customDefaultMatches = bindingsMatch && SameBehaviorExceptRecordedMode(standard, custom);
        var dailyDefaultMatches = bindingsMatch && SameBehaviorExceptRecordedMode(standard, daily);
        var negativeDetected = bindingsMatch && !SameBehavior(standard, negative);
        var checkpointNegativeDetected =
            bindingsMatch && checkpointNegative.CompletedHistory &&
            checkpointNegative.BehavioralStateSha256 == standard.BehavioralStateSha256 &&
            checkpointNegative.CheckpointSha256 != standard.CheckpointSha256 &&
            !checkpointNegative.AllCheckpointsPassed;
        var instrumentPassed =
            bindingsMatch && standard.CompletedHistory && standard.AllCheckpointsPassed &&
            negativeDetected && checkpointNegativeDetected;
        var customFinding = customDefaultMatches
            ? "Custom mode with no modifiers matches every observed checkpoint and every canonical field except the recorded run.game_mode; its full final-state digest therefore differs from standard."
            : "Custom mode with no modifiers changes an observed checkpoint, a canonical field other than run.game_mode, or action completion.";
        var dailyFinding = dailyDefaultMatches
            ? "Daily mode without its date-selected modifier set matches every observed checkpoint and every canonical field except the recorded run.game_mode; its full final-state digest therefore differs from standard, and this does not bind a real daily run."
            : "Daily mode without its date-selected modifier set changes an observed checkpoint, a canonical field other than run.game_mode, or action completion, but this does not bind the real daily configuration.";
        var visibleCount = modifierOutcomes.Count(outcome => outcome.Classification == CheckpointVisible);
        var invisibleCount = modifierOutcomes.Count(outcome => outcome.Classification == Invisible);
        var modifierFinding = singleModifierParity
            ? $"Each of the {modifierOutcomes.Count} modifiers this build offers was replayed as a daily: {visibleCount} change an observed checkpoint and are therefore excluded by the recording this history already matches, and {invisibleCount} leave every checkpoint and every canonical field other than the recorded run.game_mode unchanged. No single modifier reproduces the observed checkpoints while altering another canonical field."
            : bindingsMatch
                ? $"These modifiers reproduce every observed checkpoint while changing canonical state beyond run.game_mode, so a daily carrying one is consistent with the recording and not with this replay: {string.Join(", ", unboundModifiers)}."
                : "Modifier outcomes were not classified because probe bindings disagree.";
        var pathSpecificParity = instrumentPassed && customDefaultMatches && singleModifierParity;

        var report = new
        {
            schema = "sts2-pilot-trainer/mode-discrimination-report/v1",
            instrument_passed = instrumentPassed,
            mode_established = false,
            path_specific_custom_default_parity = instrumentPassed && customDefaultMatches,
            path_specific_mode_parity = pathSpecificParity,
            single_modifier_parity = singleModifierParity,
            modifier_space_enumerated = modifierResults.Count,
            modifier_outcomes = modifierOutcomes,
            modifier_probes = modifierResults.Select(entry => entry.Result),
            unbound_modifiers = unboundModifiers,
            combination_space_not_enumerated = true,
            custom_default_matches_standard_prefix = customDefaultMatches,
            daily_default_matches_standard_prefix = dailyDefaultMatches,
            negative_control_detected = negativeDetected,
            checkpoint_negative_control_detected = checkpointNegativeDetected,
            behavioral_state_excluded_fields = new[] { "run.game_mode" },
            blocker = pathSpecificParity
                ? null
                : !bindingsMatch
                    ? $"Probe binding mismatch: {Describe(bindingMismatches[0])}."
                    : unboundModifiers.Count > 0
                        ? $"A daily carrying any of these modifiers reproduces the observed checkpoints while changing canonical state beyond run.game_mode: {string.Join(", ", unboundModifiers)}."
                        : "The recording does not identify the mode, and the engine probe did not establish parity across the enumerated mode configurations.",
            findings = new[] { customFinding, dailyFinding, modifierFinding },
            bindings_match = bindingsMatch,
            binding_mismatches = bindingMismatches,
            standard,
            custom_default = custom,
            daily_default = daily,
            negative_control = negative,
            checkpoint_negative_control = checkpointNegative,
        };
        reportArtifact.WriteAtomic(JsonSerializer.Serialize(report, Json.Indented) + "\n");
        Console.WriteLine($"Mode discrimination instrument: {(instrumentPassed ? "PASS" : "FAIL")}");
        if (!bindingsMatch)
        {
            Console.WriteLine($"Probe binding mismatch: {Describe(bindingMismatches[0])}.");
        }
        Console.WriteLine(customFinding);
        Console.WriteLine(dailyFinding);
        Console.WriteLine(modifierFinding);
        Console.WriteLine("Mode identity: UNESTABLISHED");
        Console.WriteLine(pathSpecificParity
            ? "Path-specific mode parity: ESTABLISHED for this history over every single modifier this build offers; modifier combinations are not enumerated."
            : "Path-specific mode parity: NOT ESTABLISHED");
        Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
        return pathSpecificParity ? 0 : 1;
    }

    private const string CheckpointVisible = ModeParity.CheckpointVisibleName;
    private const string Invisible = ModeParity.InvisibleName;
    private const string StateOnlyDivergence = ModeParity.StateOnlyDivergenceName;

    private static string Classify(ModeDiscriminationResult standard, ModeDiscriminationResult candidate) =>
        ModeParity.WireName(ModeParity.Classify(Comparable(standard), Comparable(candidate)));

    private static ModeParityInputs Comparable(ModeDiscriminationResult result) => new(
        result.CompletedHistory,
        result.AllCheckpointsPassed,
        result.CheckpointSha256,
        result.BehavioralStateSha256);

    internal sealed record ModifierOutcome(
        string Type,
        string Classification,
        bool CompletedHistory,
        bool AllCheckpointsPassed,
        string CheckpointSha256,
        string BehavioralStateSha256,
        string FinalStateSha256);

    private static bool SameBehavior(ModeDiscriminationResult left, ModeDiscriminationResult right) =>
        left.CompletedHistory == right.CompletedHistory &&
        left.AllCheckpointsPassed == right.AllCheckpointsPassed &&
        string.Equals(left.CheckpointSha256, right.CheckpointSha256, StringComparison.Ordinal) &&
        string.Equals(left.BehavioralStateSha256, right.BehavioralStateSha256, StringComparison.Ordinal);

    private static bool SameBehaviorExceptRecordedMode(
        ModeDiscriminationResult standard,
        ModeDiscriminationResult candidate) =>
        SameBehavior(standard, candidate) &&
        !string.Equals(standard.FinalStateSha256, candidate.FinalStateSha256, StringComparison.Ordinal);

    private static ModeProbeBinding ProbeBinding(ModeDiscriminationResult result) => new(
        result.Variant,
        result.Schema,
        result.RunId,
        result.VideoId,
        result.BuildVersion,
        result.BuildCommit,
        result.Seed,
        result.ActionHistoryHash,
        result.AvailableModifierTypes);

    private static string Describe(ModeProbeBindingMismatch mismatch) =>
        $"{mismatch.Field}: {mismatch.BaselineSource}='{mismatch.BaselineValue}', " +
        $"{mismatch.CandidateSource}='{mismatch.CandidateValue}'";
}
