using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int BaseLibParityProbe(string[] args)
    {
        var baseLibPath = Args.Positional(args, 0, "BaseLib.dll path");
        var mode = Args.Value(args, "--mode")
            ?? throw new ManifestException("baselib-parity-probe needs --mode baseline|patched|negative.");
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("baselib-parity-probe needs --out <path>.");
        var result = Engine.BaseLibParityProbe.Run(baseLibPath, mode);
        File.WriteAllText(outPath, JsonSerializer.Serialize(result, Json.Indented) + "\n");
        return 0;
    }

    internal static int BaseLibParity(string[] args)
    {
        var baseLibPath = Args.Positional(args, 0, "BaseLib.dll path");
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("baselib-parity needs --out <path>.");
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath))!;
        Directory.CreateDirectory(outDir);

        var results = new Dictionary<string, BaseLibParityProbeResult>(StringComparer.Ordinal);
        foreach (var mode in new[] { "baseline", "patched", "negative" })
        {
            var probePath = Path.Combine(outDir, $"baselib-powercmd-{mode}.json");
            var child = SelfProcess.Run(
                "baselib-parity-probe", baseLibPath, "--mode", mode, "--out", probePath);
            if (child.ExitCode != 0)
            {
                Console.Write(child.StandardOutput);
                Console.Error.Write(child.StandardError);
                return child.ExitCode;
            }
            results[mode] = JsonSerializer.Deserialize<BaseLibParityProbeResult>(
                File.ReadAllText(probePath), ManifestJson.Options)!;
        }

        var baseline = results["baseline"];
        var patched = results["patched"];
        var negative = results["negative"];
        var bindingsMatch = Binding(baseline) == Binding(patched) && Binding(patched) == Binding(negative);
        var continuationPassed =
            patched.OriginalTaskWasIncomplete && patched.ContinuationWasIncomplete &&
            baseline.EventsSha256 == patched.EventsSha256;
        var outputParity =
            baseline.BeforeStateSha256 == patched.BeforeStateSha256 &&
            baseline.AfterStateSha256 == patched.AfterStateSha256 &&
            baseline.BeforeRng.SequenceEqual(patched.BeforeRng) &&
            baseline.AfterRng.SequenceEqual(patched.AfterRng);
        var negativeDetected =
            negative.AfterStateSha256 != patched.AfterStateSha256 ||
            !negative.AfterRng.SequenceEqual(patched.AfterRng);
        var residualPassed = bindingsMatch && continuationPassed && outputParity && negativeDetected;

        var report = new
        {
            schema = "sts2-pilot-trainer/baselib-powercmd-parity/v1",
            residual_passed = residualPassed,
            publication_parity_established = false,
            blocker =
                "The exact v3.4.5 postfix continuation was exercised with an incomplete original task and " +
                "matched the baseline, but this bounded probe does not load all three source mods or invoke " +
                "the retail PowerCmd.Apply target through Harmony. It cannot establish full environment parity.",
            bindings_match = bindingsMatch,
            continuation_passed = continuationPassed,
            output_parity = outputParity,
            negative_control_detected = negativeDetected,
            baseline,
            patched,
            negative_control = negative,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, Json.Indented) + "\n");
        Console.WriteLine($"BaseLib PowerCmd continuation residual: {(residualPassed ? "PASS" : "FAIL")}");
        Console.WriteLine("VOD publication parity: NOT ESTABLISHED");
        Console.WriteLine($"report: {Paths.Display(outPath)}");
        return residualPassed && negativeDetected ? 0 : 1;
    }

    private static string Binding(BaseLibParityProbeResult result) => JsonSerializer.Serialize(new
    {
        result.BuildVersion,
        result.BuildCommit,
        result.PreparedReceiptSha256,
        result.PreparedOutputSha256,
        result.BaseLibVersion,
        result.BaseLibSha256,
        result.BaseLibManifestSha256,
        result.BaseLibSourceCommit,
        result.PatchType,
        result.PatchMethod,
        result.PatchModuleMvid,
        result.PatchMetadataToken,
        result.PatchIlSha256,
        result.Seed,
        result.ActionHistoryHash,
    }, Json.Indented);
}
