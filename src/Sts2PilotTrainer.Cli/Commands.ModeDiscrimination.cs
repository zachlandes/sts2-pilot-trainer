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
        var modifierOutcomes = new List<ModifierOutcome>();
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
            var result = JsonSerializer.Deserialize<ModeDiscriminationResult>(
                File.ReadAllText(probePath), ManifestJson.Options)!;
            modifierOutcomes.Add(new ModifierOutcome(
                modifierType,
                Classify(standard, result),
                result.CompletedHistory,
                result.AllCheckpointsPassed,
                result.CheckpointSha256,
                result.BehavioralStateSha256));
        }

        var custom = results["custom-default"];
        var daily = results["daily-default"];
        var negative = results["custom-negative"];
        var checkpointNegative = results["checkpoint-negative"];
        var bindingsMatch = results.Values.All(result => Binding(result) == Binding(standard));
        var unboundModifiers = modifierOutcomes
            .Where(outcome => outcome.Classification == StateOnlyDivergence)
            .Select(outcome => outcome.Type)
            .ToList();
        var singleModifierParity = modifierOutcomes.Count > 0 && unboundModifiers.Count == 0;
        var customDefaultMatches = SameBehavior(standard, custom);
        var dailyDefaultMatches = SameBehavior(standard, daily);
        var negativeDetected = !SameBehavior(standard, negative);
        var checkpointNegativeDetected =
            checkpointNegative.CompletedHistory &&
            checkpointNegative.BehavioralStateSha256 == standard.BehavioralStateSha256 &&
            checkpointNegative.CheckpointSha256 != standard.CheckpointSha256 &&
            !checkpointNegative.AllCheckpointsPassed;
        var instrumentPassed =
            bindingsMatch && standard.CompletedHistory && standard.AllCheckpointsPassed &&
            negativeDetected && checkpointNegativeDetected;
        var customFinding = customDefaultMatches
            ? "Custom mode with no modifiers matches every observed checkpoint and the final canonical state."
            : "Custom mode with no modifiers changes an observed checkpoint, the final canonical state, or action completion.";
        var dailyFinding = dailyDefaultMatches
            ? "Daily mode without its date-selected modifier set matches every observed checkpoint and the final canonical state, which does not bind a real daily run."
            : "Daily mode without its date-selected modifier set changes an observed checkpoint, the final canonical state, or action completion, but this does not bind the real daily configuration.";
        var visibleCount = modifierOutcomes.Count(outcome => outcome.Classification == CheckpointVisible);
        var invisibleCount = modifierOutcomes.Count(outcome => outcome.Classification == Invisible);
        var modifierFinding = singleModifierParity
            ? $"Each of the {modifierOutcomes.Count} modifiers this build offers was replayed as a daily: {visibleCount} change an observed checkpoint and are therefore excluded by the recording this history already matches, and {invisibleCount} change nothing observable and nothing in the final canonical state. No single modifier reproduces the observed checkpoints while altering the resulting state."
            : $"These modifiers reproduce every observed checkpoint while changing the final canonical state, so a daily carrying one is consistent with the recording and not with this replay: {string.Join(", ", unboundModifiers)}.";
        var pathSpecificParity = instrumentPassed && customDefaultMatches && singleModifierParity;

        var report = new
        {
            schema = "sts2-pilot-trainer/mode-discrimination-report/v1",
            instrument_passed = instrumentPassed,
            mode_established = false,
            path_specific_custom_default_parity = instrumentPassed && customDefaultMatches,
            path_specific_mode_parity = pathSpecificParity,
            single_modifier_parity = singleModifierParity,
            modifier_space_enumerated = modifierOutcomes.Count,
            modifier_outcomes = modifierOutcomes,
            unbound_modifiers = unboundModifiers,
            combination_space_not_enumerated = true,
            custom_default_matches_standard_prefix = customDefaultMatches,
            daily_default_matches_standard_prefix = dailyDefaultMatches,
            negative_control_detected = negativeDetected,
            checkpoint_negative_control_detected = checkpointNegativeDetected,
            blocker = pathSpecificParity
                ? null
                : unboundModifiers.Count > 0
                    ? $"A daily carrying any of these modifiers reproduces the observed checkpoints while changing the resulting state: {string.Join(", ", unboundModifiers)}."
                    : "The recording does not identify the mode, and the engine probe did not establish parity across the enumerated mode configurations.",
            findings = new[] { customFinding, dailyFinding, modifierFinding },
            bindings_match = bindingsMatch,
            standard,
            custom_default = custom,
            daily_default = daily,
            negative_control = negative,
            checkpoint_negative_control = checkpointNegative,
        };
        reportArtifact.WriteAtomic(JsonSerializer.Serialize(report, Json.Indented) + "\n");
        Console.WriteLine($"Mode discrimination instrument: {(instrumentPassed ? "PASS" : "FAIL")}");
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
        string BehavioralStateSha256);

    private static bool SameBehavior(ModeDiscriminationResult left, ModeDiscriminationResult right) =>
        left.CompletedHistory == right.CompletedHistory &&
        left.AllCheckpointsPassed == right.AllCheckpointsPassed &&
        string.Equals(left.CheckpointSha256, right.CheckpointSha256, StringComparison.Ordinal) &&
        string.Equals(left.BehavioralStateSha256, right.BehavioralStateSha256, StringComparison.Ordinal);

    private static string Binding(ModeDiscriminationResult result) => JsonSerializer.Serialize(new
    {
        result.Schema,
        result.RunId,
        result.VideoId,
        result.BuildVersion,
        result.BuildCommit,
        result.Seed,
        result.ActionHistoryHash,
        result.AvailableModifierTypes,
    }, Json.Indented);
}
