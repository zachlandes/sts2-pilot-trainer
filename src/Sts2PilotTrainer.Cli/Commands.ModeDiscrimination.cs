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
        var custom = results["custom-default"];
        var daily = results["daily-default"];
        var negative = results["custom-negative"];
        var checkpointNegative = results["checkpoint-negative"];
        var bindingsMatch = results.Values.All(result => Binding(result) == Binding(standard));
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

        var report = new
        {
            schema = "sts2-pilot-trainer/mode-discrimination-report/v1",
            instrument_passed = instrumentPassed,
            mode_established = false,
            path_specific_custom_default_parity = instrumentPassed && customDefaultMatches,
            custom_default_matches_standard_prefix = customDefaultMatches,
            daily_default_matches_standard_prefix = dailyDefaultMatches,
            negative_control_detected = negativeDetected,
            checkpoint_negative_control_detected = checkpointNegativeDetected,
            blocker = negativeDetected
                ? $"Custom configuration '{Engine.ModeDiscriminationProbe.NegativeModifierType}' diverges from the verified prefix; the recording does not identify whether it was active."
                : "The recording does not identify the mode, and the engine probe cannot bind the source to every possible custom or the actual daily modifier configuration.",
            findings = new[] { customFinding, dailyFinding },
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
        Console.WriteLine("Mode identity: UNESTABLISHED");
        Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
        return 1;
    }

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
