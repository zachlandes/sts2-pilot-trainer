using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Damages a verified history in several specific ways and shows that the arbiter
    /// rejects each one.
    ///
    /// The corruptions are derived from the manifest at run time rather than kept as
    /// separate files on disk. Stored copies would drift away from the manifest they
    /// are supposed to be corruptions of, and a stale negative control that still
    /// fails for the old reason looks exactly like a working one.
    /// </summary>
    internal static int NegativeControls(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var manifest = ManifestJson.Load(manifestPath);
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var scratchDir = Path.Combine(outDir, "negative-controls");
        Directory.CreateDirectory(scratchDir);

        // Establish that the uncorrupted history passes, first. Negative controls that
        // have never been shown alongside a positive one prove only that the arbiter
        // says no, which any broken arbiter also does.
        var baseline = SelfProcess.Run("replay", manifestPath);
        var baselinePassed = baseline.ExitCode == 0;
        var baselineDigest = Digest(baseline.StandardOutput);
        Console.WriteLine($"baseline (uncorrupted): {(baselinePassed ? "VERIFIED" : "DID NOT VERIFY")}");
        if (!baselinePassed)
        {
            Console.Write(baseline.StandardOutput);
            Console.Error.WriteLine("The uncorrupted history does not verify, so rejecting a corrupted one proves nothing.");
            return 1;
        }

        Console.WriteLine();
        var results = new List<object>();
        var allRejected = true;

        foreach (var corruption in Corruption.All)
        {
            var corrupted = corruption.Apply(manifest);
            var path = Path.Combine(scratchDir, $"{corruption.Name}.manifest.json");
            ManifestJson.Save(corrupted, path);

            var child = SelfProcess.Run("replay", path);
            var rejected = child.ExitCode != 0;
            allRejected &= rejected;

            var reason = FirstDiagnostic(child.StandardOutput) ?? "(no diagnostic line found)";
            var digest = Digest(child.StandardOutput);
            var endStateChanged = digest is not null && baselineDigest is not null && digest != baselineDigest;

            Console.WriteLine($"{corruption.Name}");
            Console.WriteLine($"  corruption   : {corruption.What}");
            Console.WriteLine($"  video-only   : {corruption.VideoOnly.ToString().ToUpperInvariant()} - {corruption.WhyVideoOnly}");
            Console.WriteLine($"  arbiter      : {(rejected ? "REJECTED" : "ACCEPTED - THIS IS A FAILURE")}");
            Console.WriteLine($"  first divergence: {reason}");
            Console.WriteLine($"  end state       : {(endStateChanged ? "differs from the uncorrupted run" : "IDENTICAL to the uncorrupted run")}");
            Console.WriteLine();

            results.Add(new
            {
                name = corruption.Name,
                corruption = corruption.What,
                video_only_verdict = corruption.VideoOnly.ToString(),
                video_only_reasoning = corruption.WhyVideoOnly,
                arbiter_rejected = rejected,
                first_divergence = reason,
                // Recorded because it bounds what the arbiter can claim. Where this is
                // false, the corruption was caught by a checkpoint bound to a moment
                // inside the turn, and comparing only the run's end state would have
                // accepted it. That is a real limit of digest comparison, and it is the
                // argument for checkpoints being dense rather than terminal.
                end_state_differs = endStateChanged,
            });
        }

        File.WriteAllText(
            Path.Combine(outDir, "negative-controls.json"),
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/negative-controls/v1",
                manifest = Path.GetFileName(manifestPath),
                baseline_verified = baselinePassed,
                all_rejected = allRejected,
                controls = results,
            }, Json.Indented) + "\n");

        Console.WriteLine(allRejected
            ? $"all {Corruption.All.Count} corrupted histories were rejected; the uncorrupted one verified"
            : "AT LEAST ONE CORRUPTED HISTORY WAS ACCEPTED");

        return allRejected ? 0 : 1;
    }

    private static string? Digest(string output) =>
        output.Split('\n')
            .FirstOrDefault(l => l.StartsWith("final state digest", StringComparison.Ordinal))
            ?.Split(':', 2)[1].Trim();

    /// <summary>Pulls the arbiter's first divergence line out of a child run's output.</summary>
    private static string? FirstDiagnostic(string output) =>
        output.Split('\n')
            .Select(line => line.TrimStart())
            .FirstOrDefault(line => line.StartsWith("! ", StringComparison.Ordinal))
            ?[2..]
            .Trim();
}
