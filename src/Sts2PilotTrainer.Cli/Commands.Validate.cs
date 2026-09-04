using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Checks a manifest's structure and its account of where the recording came
    /// from, before any engine is started.
    ///
    /// These gates are the ones nothing downstream can stand in for. A replay can
    /// tell you an action history is wrong; it cannot tell you the recording was of a
    /// run resumed half way through, because that run replays perfectly well.
    /// </summary>
    internal static int Validate(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var showRejections = Array.IndexOf(args, "--show-rejections") >= 0;
        var reportArtifact = showRejections
            ? EvidenceArtifact.Prepare(outDir, "ingestion-gates.json")
            : null;
        var manifest = ManifestJson.Load(manifestPath);

        var result = ManifestValidator.Validate(manifest);
        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine($"structure: {(result.IsValid ? "VALID" : "INVALID")}");
        if (!result.IsValid) Console.WriteLine(result.Describe());
        Console.WriteLine();

        if (!showRejections)
        {
            return result.IsValid ? 0 : 1;
        }

        // Same shape as the replay negative controls: damage the manifest in specific
        // ways and show each being refused. A gate nobody has fed a bad input to has
        // never been shown to reject anything.
        Console.WriteLine("ingestion gates, fed inputs that should be refused:");
        Console.WriteLine();

        var corruptions = IngestionCorruption.For(manifest);
        var reports = new List<object>();
        var allRejected = true;

        foreach (var corruption in corruptions)
        {
            var corrupted = corruption.Apply(manifest);
            var corruptedResult = ManifestValidator.Validate(corrupted);
            allRejected &= !corruptedResult.IsValid;

            Console.WriteLine($"{corruption.Name}");
            Console.WriteLine($"  corruption : {corruption.What}");
            Console.WriteLine($"  why it matters: {corruption.WhyItMatters}");
            Console.WriteLine($"  verdict    : {(corruptedResult.IsValid ? "ACCEPTED - THIS IS A FAILURE" : "REFUSED")}");
            foreach (var problem in corruptedResult.Problems.Take(1))
            {
                Console.WriteLine($"  because    : {problem}");
            }
            Console.WriteLine();

            reports.Add(new
            {
                name = corruption.Name,
                corruption = corruption.What,
                why_it_matters = corruption.WhyItMatters,
                refused = !corruptedResult.IsValid,
                problems = corruptedResult.Problems,
            });
        }

        reportArtifact!.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/ingestion-gates/v1",
                manifest = Path.GetFileName(manifestPath),
                manifest_valid = result.IsValid,
                all_refused = allRejected,
                cases = reports,
            }, Json.Indented) + "\n");

        Console.WriteLine(allRejected
            ? $"all {corruptions.Count} damaged provenance records were refused; the real one is valid"
            : "AT LEAST ONE DAMAGED PROVENANCE RECORD WAS ACCEPTED");

        return result.IsValid && allRejected ? 0 : 1;
    }
}
