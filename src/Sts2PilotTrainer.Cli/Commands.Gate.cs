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
    /// they are not evidence: two of the four corruptions in the replay controls pass
    /// every arithmetic check available from the frames, and a run resumed from
    /// history passes every check that is not about the recording itself.
    /// </summary>
    internal static int Gate(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        Directory.CreateDirectory(outDir);
        var manifest = ManifestJson.Load(manifestPath);
        var mapObservationPath = Args.Value(args, "--map-observation") ??
            (manifestPath.EndsWith(".replay.json", StringComparison.Ordinal)
                ? manifestPath[..^".replay.json".Length] + ".map-observation.json"
                : manifestPath + ".map-observation.json");
        var baseLibPath = Args.Value(args, "--baselib") ?? "build/parity/BaseLib.dll";

        var conditions = new List<Condition>
        {
            new(
                "publication-source",
                "Publication evidence comes from a VOD, never an engine-generated fixture.",
                manifest.Source.Kind == "vod"),

            Check("provenance",
                "The recording is of the run it claims, from that run's start.",
                SelfProcess.Run("validate", manifestPath, "--show-rejections", "--out", outDir)),
        };

        var environment = Check("environment",
            "The declared build, content hash and mode match this machine.",
            SelfProcess.Run("preflight", manifestPath));
        conditions.Add(environment);

        if (environment.Passed)
        {
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

            conditions.Add(Check("baselib-path",
                "The measured BaseLib behavior branch is unreachable in this exact reconstructed history.",
                SelfProcess.Run(
                    "baselib-reachability", manifestPath, baseLibPath,
                    "--out", Path.Combine(outDir, "baselib-reachability.json"))));

            conditions.Add(Check("reproduction",
                "The reconstructed history replays through the real engine and matches every observed value.",
                SelfProcess.Run("replay", manifestPath, "--out", Path.Combine(outDir, "verified-manifest.json"))));

            conditions.Add(Check("determinism",
                "Fresh processes produce byte-identical canonical state.",
                SelfProcess.Run("determinism", manifestPath, "--runs", "2", "--out", outDir)));

            conditions.Add(Check("rejection",
                "Corrupted and incomplete histories are refused.",
                SelfProcess.Run("negative-controls", manifestPath, "--out", outDir)));
        }
        else
        {
            conditions.AddRange(
            [
                new Condition("seed-topology",
                    "The manifest seed independently reproduces the map observed in the same VOD.", false),
                new Condition("baselib-path",
                    "The measured BaseLib behavior branch is unreachable in this exact reconstructed history.", false),
                new Condition("reproduction",
                    "The reconstructed history replays through the real engine and matches every observed value.", false),
                new Condition("determinism",
                    "Fresh processes produce byte-identical canonical state.", false),
                new Condition("rejection",
                    "Corrupted and incomplete histories are refused.", false),
            ]);
        }

        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine();
        foreach (var condition in conditions)
        {
            Console.WriteLine($"  {(condition.Passed ? "pass" : "FAIL")}  {condition.Name,-13} {condition.Requirement}");
        }

        var publishable = conditions.All(c => c.Passed);
        Console.WriteLine();
        Console.WriteLine(publishable
            ? "PUBLISHABLE - every condition of the gate holds"
            : "NOT PUBLISHABLE - see the failing condition above");

        File.WriteAllText(
            Path.Combine(outDir, "publication-gate.json"),
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
                    "mod list. Each is a useful filter and none is evidence.",
                conditions = conditions.Select(c => new
                {
                    name = c.Name,
                    requirement = c.Requirement,
                    passed = c.Passed,
                }),
            }, Json.Indented) + "\n");

        return publishable ? 0 : 1;
    }

    private static Condition Check(string name, string requirement, SelfProcess.Result result)
    {
        if (result.ExitCode != 0)
        {
            Console.Write(result.StandardOutput);
            Console.Error.Write(result.StandardError);
        }
        return new Condition(name, requirement, result.ExitCode == 0);
    }

    private sealed record Condition(string Name, string Requirement, bool Passed);
}
