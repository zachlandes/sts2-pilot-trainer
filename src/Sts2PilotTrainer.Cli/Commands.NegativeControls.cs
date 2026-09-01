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
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var reportArtifact = EvidenceArtifact.Prepare(outDir, "negative-controls.json");
        var requireAllControls = Args.Has(args, "--require-all-controls");
        var manifest = ManifestJson.Load(manifestPath);
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
            // A control aimed at a decision this history never made has nothing to
            // damage. Reported as what it is rather than counted as a pass, so a
            // history cannot dodge a control by leaving the decision out.
            if (!corruption.AppliesTo(manifest))
            {
                Console.WriteLine($"{corruption.Name}");
                Console.WriteLine($"  corruption   : {corruption.What}");
                Console.WriteLine(
                    $"  arbiter      : NOT APPLICABLE - this control needs {corruption.Requires}; " +
                    "this history has none");
                Console.WriteLine();
                results.Add(new
                {
                    name = corruption.Name,
                    corruption = corruption.What,
                    video_only_verdict = corruption.VideoOnly.ToString(),
                    video_only_reasoning = corruption.WhyVideoOnly,
                    applicable = false,
                    requires = corruption.Requires,
                });
                continue;
            }

            var corrupted = corruption.Apply(manifest);
            var path = Path.Combine(scratchDir, $"{corruption.Name}.manifest.json");
            ManifestJson.Save(corrupted, path);

            var replayResultPath = Path.Combine(scratchDir, $"{corruption.Name}.result.json");
            if (File.Exists(replayResultPath)) File.Delete(replayResultPath);
            var child = SelfProcess.Run("replay", path, "--out", replayResultPath);
            var replayReport = File.Exists(replayResultPath)
                ? ManifestJson.Load(replayResultPath).Verification
                : null;
            var arbiterRejected = replayReport is
            {
                Status: VerificationStatus.Rejected,
                Diagnostics.Count: > 0,
            };
            var ingestionRejected = replayReport is null && child.ExitCode != 0 &&
                child.StandardError.Contains("Manifest is not valid", StringComparison.Ordinal);
            allRejected &= arbiterRejected;

            var reason = replayReport?.Diagnostics.FirstOrDefault() ??
                         FirstDiagnostic(child.StandardOutput) ??
                         child.StandardError.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ??
                         "(no diagnostic line found)";
            var digest = Digest(child.StandardOutput);
            bool? endStateChanged = digest is null || baselineDigest is null
                ? null
                : digest != baselineDigest;
            var endStateComparison = endStateChanged switch
            {
                true => "Differs",
                false => "Identical",
                null => "Unavailable",
            };
            var endStateDescription = endStateChanged switch
            {
                true => "differs from the uncorrupted run",
                false => "IDENTICAL to the uncorrupted run",
                null => "UNAVAILABLE - the rejected run produced no final state digest",
            };
            var arbiterDescription = arbiterRejected
                ? "REJECTED"
                : ingestionRejected
                    ? "NOT RUN - INGESTION REJECTED THE CONTROL"
                    : "DID NOT REJECT - THIS IS A FAILURE";

            Console.WriteLine($"{corruption.Name}");
            Console.WriteLine($"  corruption   : {corruption.What}");
            Console.WriteLine($"  video-only   : {corruption.VideoOnly.ToString().ToUpperInvariant()} - {corruption.WhyVideoOnly}");
            Console.WriteLine($"  arbiter      : {arbiterDescription}");
            Console.WriteLine($"  first divergence: {reason}");
            Console.WriteLine($"  end state       : {endStateDescription}");
            Console.WriteLine();

            results.Add(new
            {
                name = corruption.Name,
                corruption = corruption.What,
                video_only_verdict = corruption.VideoOnly.ToString(),
                video_only_reasoning = corruption.WhyVideoOnly,
                applicable = true,
                requires = corruption.Requires,
                arbiter_rejected = arbiterRejected,
                ingestion_rejected = ingestionRejected,
                replay_status = replayReport?.Status.ToString() ??
                                (ingestionRejected ? "IngestionRejected" : "NoResult"),
                first_divergence = reason,
                // Recorded because it bounds what the arbiter can claim. Where this is
                // false, the corruption was caught by a checkpoint bound to a moment
                // inside the turn, and comparing only the run's end state would have
                // accepted it. That is a real limit of digest comparison, and it is the
                // argument for checkpoints being dense rather than terminal.
                end_state_differs = endStateChanged is { } value
                    ? JsonSerializer.SerializeToElement(value)
                    : JsonSerializer.SerializeToElement<object?>(null),
                end_state_comparison = endStateComparison,
            });
        }

        var applied = Corruption.All.Count(corruption => corruption.AppliesTo(manifest));
        var skipped = Corruption.All.Count - applied;
        var allControlsApplicable = skipped == 0;
        var passed = allRejected && (!requireAllControls || allControlsApplicable);

        reportArtifact.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/negative-controls/v1",
                manifest = Path.GetFileName(manifestPath),
                baseline_verified = baselinePassed,
                all_rejected = allRejected,
                all_controls_applicable = allControlsApplicable,
                applicable_controls = applied,
                total_controls = Corruption.All.Count,
                controls = results,
            }, Json.Indented) + "\n");

        Console.WriteLine(!allRejected
            ? "AT LEAST ONE CORRUPTED HISTORY WAS ACCEPTED"
            : requireAllControls && !allControlsApplicable
                ? $"ONLY {applied} OF {Corruption.All.Count} REQUIRED CONTROLS APPLIED"
                : $"all {applied} corrupted histories were rejected; the uncorrupted one verified" +
                  (skipped > 0 ? $" ({skipped} control(s) had nothing in this history to damage)" : ""));

        return passed ? 0 : 1;
    }

    private static string? Digest(string output)
    {
        var value = output.Split('\n')
            .FirstOrDefault(line => line.StartsWith("final state digest", StringComparison.Ordinal))
            ?.Split(':', 2)[1].Trim();
        return value is null or "(none)" ? null : value;
    }

    /// <summary>Pulls the arbiter's first divergence line out of a child run's output.</summary>
    private static string? FirstDiagnostic(string output) =>
        output.Split('\n')
            .Select(line => line.TrimStart())
            .FirstOrDefault(line => line.StartsWith("! ", StringComparison.Ordinal))
            ?[2..]
            .Trim();
}
