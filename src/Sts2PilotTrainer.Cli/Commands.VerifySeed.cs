using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Checks candidate seeds against a map read from a video.
    ///
    /// The seed only ever reaches us as text somebody read off a low-contrast
    /// overlay. This command never reads that text: it regenerates each candidate's
    /// map through the game's own generator and asks which one the video actually
    /// shows. Exactly one candidate should match; anything else is reported as
    /// unresolved rather than resolved in favour of a guess.
    /// </summary>
    internal static int VerifySeed(string[] args)
    {
        var observationPath = Args.Positional(args, 0, "map-observation path");
        var acts = (Args.Value(args, "--acts") ?? "ACT.UNDERDOCKS,ACT.HIVE,ACT.GLORY")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var outDir = Args.Value(args, "--out") ?? "build/evidence";

        // One candidate per invocation, in its own process: see SelfProcess.
        var single = Args.Value(args, "--seed");
        if (single is not null)
        {
            return VerifyOne(observationPath, single, Args.Value(args, "--character") ?? "CHARACTER.IRONCLAD",
                int.Parse(Args.Value(args, "--ascension") ?? "10", System.Globalization.CultureInfo.InvariantCulture),
                Args.Value(args, "--game-mode") ?? "standard", acts, outDir, Args.Value(args, "--manifest"));
        }

        var candidates = (Args.Value(args, "--candidates")
                          ?? throw new ManifestException("verify-seed needs --candidates <seed>[,<seed>...] or --seed <seed>."))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var character = Args.Value(args, "--character") ?? "CHARACTER.IRONCLAD";
        var ascension = Args.Value(args, "--ascension") ?? "10";
        var gameMode = Args.Value(args, "--game-mode") ?? "standard";

        Directory.CreateDirectory(outDir);
        var results = new List<JsonElement>();

        foreach (var candidate in candidates)
        {
            var child = SelfProcess.Run(
                "verify-seed", observationPath,
                "--seed", candidate,
                "--out", outDir,
                "--acts", string.Join(",", acts),
                "--character", character,
                "--ascension", ascension,
                "--game-mode", gameMode,
                "--manifest", Args.Value(args, "--manifest") ?? "");
            Console.Write(child.StandardOutput);
            var path = Path.Combine(outDir, $"seed-verification-{candidate}.json");
            if (child.ExitCode is not 0 and not 1 || !File.Exists(path))
            {
                Console.Error.Write(child.StandardError);
                return child.ExitCode == 0 ? 1 : child.ExitCode;
            }

            results.Add(JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone());
        }

        var matching = results
            .Where(r => r.GetProperty("comparison").GetProperty("matches").GetBoolean())
            .Select(r => r.GetProperty("candidate_seed").GetString()!)
            .ToList();

        var summary = new
        {
            schema = "sts2-pilot-trainer/seed-verification-summary/v1",
            observation = Path.GetFileName(observationPath),
            candidates,
            matching_candidates = matching,
            // Reported rather than asserted. Two matching candidates would mean the
            // map is not a discriminating fingerprint for this pair, and zero would
            // mean the true seed is not among the candidates - neither is a result
            // to paper over by picking the closest one.
            resolved = matching.Count == 1,
            resolved_seed = matching.Count == 1 ? matching[0] : null,
            results,
        };

        var summaryPath = Path.Combine(outDir, "seed-verification-summary.json");
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine($"candidates tested : {candidates.Length}");
        Console.WriteLine($"matching          : {(matching.Count == 0 ? "(none)" : string.Join(", ", matching))}");
        Console.WriteLine(matching.Count == 1
            ? $"resolved seed     : {matching[0]}"
            : "resolved seed     : NOT RESOLVED - see the summary for why");
        Console.WriteLine($"summary           : {Paths.Display(summaryPath)}");

        return matching.Count == 1 ? 0 : 1;
    }

    private static int VerifyOne(
        string observationPath, string seed, string character, int ascension, string gameMode,
        IReadOnlyList<string> acts, string outDir, string? manifestPath)
    {
        var observation = MapObservation.Load(observationPath);
        ReplayManifest? boundManifest = null;
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            boundManifest = ManifestJson.Load(manifestPath);
            if (boundManifest.Source.Kind != "vod" || boundManifest.Source.Video is null)
            {
                throw new ManifestException("Seed topology evidence can only bind to a VOD manifest.");
            }
            observation.RequireSameVideo(boundManifest.Source.Video);
            if (!string.Equals(seed, boundManifest.Environment.Seed.Value, StringComparison.Ordinal))
            {
                throw new ManifestException(
                    $"Candidate seed '{seed}' does not match manifest seed '{boundManifest.Environment.Seed.Value}'.");
            }
        }

        var session = new GameSession();
        session.StartRun(seed, character, ascension, gameMode, acts);
        session.EnterActForMap(observation.ActIndex);
        var generated = session.CurrentMapTopology();

        var comparison = observation.CompareTo(generated);

        Directory.CreateDirectory(outDir);
        var jsonPath = Path.Combine(outDir, $"seed-verification-{seed}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
        {
            schema = "sts2-pilot-trainer/seed-verification/v1",
            candidate_seed = seed,
            character,
            ascension,
            game_mode = gameMode,
            acts,
            environment = Identity(),
            bound_manifest = boundManifest is null
                ? null
                : new
                {
                    file = Path.GetFileName(manifestPath),
                    boundManifest.RunId,
                    video_id = boundManifest.Source.Video!.VideoId,
                },
            observation = new { file = Path.GetFileName(observationPath), observation.Video.VideoId, observation.Method },
            generated,
            comparison,
        }, Json.Indented) + "\n");

        var svgPath = Path.Combine(outDir, $"seed-verification-{seed}.svg");
        File.WriteAllText(svgPath, MapDiagram.Render(observation, generated, seed, comparison));

        Console.WriteLine(
            $"{seed}: {(comparison.Matches ? "MATCH  " : "MISMATCH")}  " +
            $"{comparison.MatchedNodeCount}/{comparison.ObservedNodeCount} observed nodes agree, " +
            $"{comparison.Problems.Count} problem(s)");
        foreach (var problem in comparison.Problems.Take(4))
        {
            Console.WriteLine($"    {problem}");
        }
        if (comparison.Problems.Count > 4)
        {
            Console.WriteLine($"    ... and {comparison.Problems.Count - 4} more");
        }

        return comparison.Matches ? 0 : 1;
    }

    private static object Identity()
    {
        var identity = GameIdentity.Read();
        return new
        {
            build_version = identity.BuildVersion,
            build_date_utc = identity.BuildDateUtc,
            content_hash = identity.ContentHash,
        };
    }
}
