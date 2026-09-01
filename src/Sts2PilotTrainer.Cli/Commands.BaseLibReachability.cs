using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int BaseLibReachabilityProbe(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var baseLibPath = Args.Positional(args, 1, "BaseLib.dll path");
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("baselib-reachability-probe needs --out <path>.");
        var artifact = EvidenceArtifact.PreparePath(outPath);
        var mode = Args.Value(args, "--mode")
            ?? throw new ManifestException("baselib-reachability-probe needs --mode history|negative.");
        if (mode is not ("history" or "negative"))
        {
            throw new ManifestException($"Unknown BaseLib reachability mode '{mode}'.");
        }
        var result = Engine.BaseLibReachabilityProbe.Run(
            ManifestJson.Load(manifestPath), baseLibPath, injectAffectedCall: mode == "negative");
        artifact.WriteAtomic(JsonSerializer.Serialize(result, Json.Indented) + "\n");
        return 0;
    }

    internal static int BaseLibReachability(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var baseLibPath = Args.Positional(args, 1, "BaseLib.dll path");
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("baselib-reachability needs --out <path>.");
        var reportArtifact = EvidenceArtifact.PreparePath(outPath);
        var outDir = Path.GetDirectoryName(reportArtifact.Path)!;

        var results = new Dictionary<string, BaseLibReachabilityResult>(StringComparer.Ordinal);
        foreach (var mode in new[] { "history", "negative" })
        {
            var probePath = Path.Combine(outDir, $"baselib-reachability-{mode}.json");
            var child = SelfProcess.Run(
                "baselib-reachability-probe", manifestPath, baseLibPath,
                "--mode", mode, "--out", probePath);
            if (child.ExitCode != 0)
            {
                Console.Write(child.StandardOutput);
                Console.Error.Write(child.StandardError);
                return child.ExitCode;
            }
            results[mode] = JsonSerializer.Deserialize<BaseLibReachabilityResult>(
                File.ReadAllText(probePath), ManifestJson.Options)!;
        }

        var history = results["history"];
        var negative = results["negative"];
        var bindingsMatch = Binding(history) == Binding(negative);
        var negativeDetected = negative.AffectedBranchReached && negative.Calls.Any(call =>
            call.ActionSeq == -2 && call.OriginalTaskIncomplete &&
            call.CustomModelParticipant && call.AffectedBranchReached);
        var historyComplete = history.Calls.All(call => call.ActionSeq >= 0);
        var instrumentPassed = bindingsMatch && negativeDetected && historyComplete;
        var branchReachable = history.AffectedBranchReached;

        var report = new
        {
            schema = "sts2-pilot-trainer/baselib-reachability-report/v1",
            instrument_passed = instrumentPassed,
            affected_branch_reached_in_history = branchReachable,
            path_specific_parity_established = instrumentPassed && !branchReachable,
            blocker = branchReachable
                ? "The reconstructed history reaches the measured BaseLib behavior branch; publication requires replay under that exact patch."
                : instrumentPassed
                    ? null
                    : "The reachability instrument or its injected negative control failed.",
            bindings_match = bindingsMatch,
            negative_control_detected = negativeDetected,
            history,
            negative_control = negative,
        };
        reportArtifact.WriteAtomic(JsonSerializer.Serialize(report, Json.Indented) + "\n");
        Console.WriteLine($"BaseLib reachability instrument: {(instrumentPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Affected branch in reconstructed history: {(branchReachable ? "REACHED" : "NOT REACHED")}");
        Console.WriteLine($"report: {Paths.Display(outPath)}");
        return instrumentPassed && !branchReachable ? 0 : 1;
    }

    private static string Binding(BaseLibReachabilityResult result) => JsonSerializer.Serialize(new
    {
        result.Schema,
        result.RunId,
        result.VideoId,
        result.BuildVersion,
        result.BuildCommit,
        result.BaseLibVersion,
        result.BaseLibSha256,
        result.TargetIlSha256,
        result.Seed,
        result.ActionHistoryHash,
    }, Json.Indented);
}
