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
        var targetExercisePassed = new[] { baseline, patched, negative }.All(result =>
            result.BeforeApplyWasEntered && result.ApplyTaskWasIncomplete && result.PowerApplied &&
            result.PowerAmount == 1 && result.ApplierIsPlayer);
        var patchRegistrationPassed =
            !baseline.PatchRegistered && patched.PatchRegistered && !negative.PatchRegistered;
        var rngParity =
            baseline.BeforeRng.SequenceEqual(patched.BeforeRng) &&
            baseline.AfterRng.SequenceEqual(patched.AfterRng) &&
            baseline.BeforeRng.SequenceEqual(negative.BeforeRng) &&
            baseline.AfterRng.SequenceEqual(negative.AfterRng);
        var behaviorParity =
            baseline.SkipNextDurationTick == patched.SkipNextDurationTick &&
            baseline.AfterStateSha256 == patched.AfterStateSha256;
        var negativeDetected =
            negative.SkipNextDurationTick == baseline.SkipNextDurationTick &&
            negative.SkipNextDurationTick != patched.SkipNextDurationTick &&
            negative.AfterStateSha256 == baseline.AfterStateSha256;
        var instrumentPassed =
            bindingsMatch && targetExercisePassed && patchRegistrationPassed && rngParity && negativeDetected;

        var report = new
        {
            schema = "sts2-pilot-trainer/baselib-powercmd-parity/v2",
            instrument_passed = instrumentPassed,
            publication_parity_established = false,
            blocker = behaviorParity
                ? "The target-level BaseLib comparison matched for this branch, but it does not load the complete source mod set or prove that the reconstructed VOD never reaches another patched branch."
                : "BaseLib v3.4.5 changes SkipNextDurationTick for a player-applied custom debuff at the retail PowerCmd.Apply target. The unmodded host is not behaviorally identical to the source environment.",
            bindings_match = bindingsMatch,
            target_exercise_passed = targetExercisePassed,
            patch_registration_passed = patchRegistrationPassed,
            rng_parity = rngParity,
            behavior_parity = behaviorParity,
            negative_control_detected = negativeDetected,
            baseline,
            patched,
            negative_control = negative,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, Json.Indented) + "\n");
        Console.WriteLine($"BaseLib PowerCmd target probe: {(instrumentPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"BaseLib behavior parity: {(behaviorParity ? "MATCH" : "DIFFERS")}");
        Console.WriteLine("VOD publication parity: NOT ESTABLISHED");
        Console.WriteLine($"report: {Paths.Display(outPath)}");
        return instrumentPassed ? 0 : 1;
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
        result.TargetType,
        result.TargetMethod,
        result.TargetMetadataToken,
        result.TargetIlSha256,
        result.PatchType,
        result.PatchMethod,
        result.PatchModuleMvid,
        result.PatchMetadataToken,
        result.PatchIlSha256,
        result.Seed,
        result.ActionHistoryHash,
    }, Json.Indented);
}
