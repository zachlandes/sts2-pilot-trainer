using System.Security.Cryptography;
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
        var summaryArtifact = EvidenceArtifact.Prepare(outDir, "seed-verification-summary.json");

        // One candidate per invocation, in its own process: see SelfProcess.
        var single = Args.Value(args, "--seed");
        if (single is not null)
        {
            RequireCandidate(single);
            var jsonArtifact = EvidenceArtifact.Prepare(outDir, $"seed-verification-{single}.json");
            var svgArtifact = EvidenceArtifact.Prepare(outDir, $"seed-verification-{single}.svg");
            return VerifyOne(observationPath, single, Args.Value(args, "--character") ?? "CHARACTER.IRONCLAD",
                ParseAscension(Args.Value(args, "--ascension") ?? "10"),
                Args.Value(args, "--game-mode") ?? "standard", acts,
                Args.Value(args, "--manifest"), jsonArtifact, svgArtifact);
        }

        var candidates = (Args.Value(args, "--candidates")
                          ?? throw new ManifestException("verify-seed needs --candidates <seed>[,<seed>...] or --seed <seed>."))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (candidates.Length == 0)
        {
            throw new ManifestException("verify-seed needs at least one candidate seed.");
        }
        foreach (var candidate in candidates) RequireCandidate(candidate);
        if (candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Length)
        {
            throw new ManifestException("verify-seed candidate seeds must be distinct.");
        }
        var candidateArtifacts = candidates.ToDictionary(
            candidate => candidate,
            candidate => (
                Json: EvidenceArtifact.Prepare(outDir, $"seed-verification-{candidate}.json"),
                Svg: EvidenceArtifact.Prepare(outDir, $"seed-verification-{candidate}.svg")),
            StringComparer.Ordinal);
        var character = Args.Value(args, "--character") ?? "CHARACTER.IRONCLAD";
        var ascension = Args.Value(args, "--ascension") ?? "10";
        var ascensionValue = ParseAscension(ascension);
        var gameMode = Args.Value(args, "--game-mode") ?? "standard";
        var manifestPath = Args.Value(args, "--manifest");
        var boundManifest = string.IsNullOrWhiteSpace(manifestPath) ? null : ManifestJson.Load(manifestPath);
        var observationHash = Sha256File(observationPath);
        var manifestHash = boundManifest is null ? null : Sha256File(manifestPath!);
        var results = new List<JsonElement>();

        foreach (var candidate in candidates)
        {
            var path = candidateArtifacts[candidate].Json.Path;
            var child = SelfProcess.Run(
                "verify-seed", observationPath,
                "--seed", candidate,
                "--out", outDir,
                "--acts", string.Join(",", acts),
                "--character", character,
                "--ascension", ascension,
                "--game-mode", gameMode,
                "--manifest", manifestPath ?? "");
            Console.Write(child.StandardOutput);
            if (child.ExitCode is not 0 and not 1 || !File.Exists(path))
            {
                Console.Error.Write(child.StandardError);
                return child.ExitCode == 0 ? 1 : child.ExitCode;
            }

            var result = RequireCurrentSeedResult(
                path, candidate, character, ascensionValue, gameMode, acts,
                observationPath, observationHash, manifestPath, manifestHash, boundManifest);
            var matches = result.GetProperty("comparison").GetProperty("matches").GetBoolean();
            if ((child.ExitCode == 0) != matches)
            {
                throw new ManifestException(
                    $"Seed candidate '{candidate}' exit status disagrees with its current comparison artifact.");
            }
            results.Add(result);
        }

        var matching = results
            .Where(r => r.GetProperty("comparison").GetProperty("matches").GetBoolean())
            .Select(r => r.GetProperty("candidate_seed").GetString()!)
            .ToList();
        var expectedSeed = boundManifest?.Environment.Seed.Value;
        var hasRejectedAlternative = expectedSeed is null || results.Any(result =>
            !string.Equals(result.GetProperty("candidate_seed").GetString(), expectedSeed, StringComparison.Ordinal) &&
            !result.GetProperty("comparison").GetProperty("matches").GetBoolean());
        var resolved = matching.Count == 1 &&
            (expectedSeed is null || string.Equals(matching[0], expectedSeed, StringComparison.Ordinal)) &&
            hasRejectedAlternative;

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
            resolved,
            resolved_seed = resolved ? matching[0] : null,
            rejected_alternative_demonstrated = hasRejectedAlternative,
            results,
        };

        summaryArtifact.WriteAtomic(JsonSerializer.Serialize(summary, Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine($"candidates tested : {candidates.Length}");
        Console.WriteLine($"matching          : {(matching.Count == 0 ? "(none)" : string.Join(", ", matching))}");
        Console.WriteLine(resolved
            ? $"resolved seed     : {matching[0]}"
            : "resolved seed     : NOT RESOLVED - see the summary for why");
        Console.WriteLine($"summary           : {Paths.Display(summaryArtifact.Path)}");

        return resolved ? 0 : 1;
    }

    private static int VerifyOne(
        string observationPath, string seed, string character, int ascension, string gameMode,
        IReadOnlyList<string> acts, string? manifestPath,
        EvidenceArtifact jsonArtifact, EvidenceArtifact svgArtifact)
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
            RequireBoundGenerationIdentity(
                observation, character, ascension, gameMode, acts, boundManifest.Environment);
        }

        var session = new GameSession();
        session.StartRun(seed, character, ascension, gameMode, acts);
        session.EnterActForMap(observation.ActIndex);
        var generated = session.CurrentMapTopology();

        var comparison = observation.CompareTo(generated);

        var json = JsonSerializer.Serialize(new
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
                    sha256 = Sha256File(manifestPath!),
                    run_id = boundManifest.RunId,
                    video_id = boundManifest.Source.Video!.VideoId,
                },
            observation = new
            {
                file = Path.GetFileName(observationPath),
                sha256 = Sha256File(observationPath),
                video_id = observation.Video.VideoId,
                method = observation.Method,
            },
            generated,
            comparison,
        }, Json.IndentedKeepingNulls) + "\n";

        svgArtifact.WriteAtomic(MapDiagram.Render(observation, generated, seed, comparison));
        jsonArtifact.WriteAtomic(json);

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

    private static void RequireBoundGenerationIdentity(
        MapObservation observation, string character, int ascension, string gameMode,
        IReadOnlyList<string> acts, EnvironmentIdentity environment)
    {
        if (observation.ActIndex != 0)
        {
            throw new ManifestException(
                $"Publication seed evidence must observe Act 1 at act_index 0, not {observation.ActIndex}.");
        }
        if (environment.Acts.Value.Count == 0)
        {
            throw new ManifestException("The manifest declares no first act for the Act 1 seed check.");
        }
        if (!acts.SequenceEqual(environment.Acts.Value, StringComparer.Ordinal))
        {
            throw new ManifestException(
                "Seed generation acts do not match the manifest, so act_index 0 is not bound to its first declared act.");
        }
        if (!string.Equals(character, environment.Character.Value, StringComparison.Ordinal) ||
            ascension != environment.Ascension.Value ||
            !string.Equals(gameMode, environment.GameMode.Value, StringComparison.Ordinal))
        {
            throw new ManifestException(
                "Seed generation character, ascension, and game mode must match the bound manifest environment.");
        }
        if (!string.Equals(gameMode, "standard", StringComparison.Ordinal))
        {
            throw new ManifestException("Publication seed evidence must generate the standard-mode Act 1 map.");
        }
    }

    private static void RequireCandidate(string candidate)
    {
        if (candidate.Length == 0 || candidate.Any(character =>
                !ManifestValidator.SeedAlphabet.Contains(character, StringComparison.Ordinal)))
        {
            throw new ManifestException($"Seed candidate '{candidate}' is not in the game's seed alphabet.");
        }
    }

    private static int ParseAscension(string value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ascension))
        {
            throw new ManifestException($"Ascension '{value}' is not an Int32 value.");
        }
        return ascension;
    }

    private static JsonElement RequireCurrentSeedResult(
        string path, string candidate, string character, int ascension, string gameMode,
        IReadOnlyList<string> acts, string observationPath, string observationHash,
        string? manifestPath, string? manifestHash, ReplayManifest? boundManifest)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var result = document.RootElement;
            RequireResult(
                result.GetProperty("schema").GetString() == "sts2-pilot-trainer/seed-verification/v1",
                candidate, "schema");
            RequireResult(result.GetProperty("candidate_seed").GetString() == candidate, candidate, "candidate seed");
            RequireResult(result.GetProperty("character").GetString() == character, candidate, "character");
            RequireResult(result.GetProperty("ascension").GetInt32() == ascension, candidate, "ascension");
            RequireResult(result.GetProperty("game_mode").GetString() == gameMode, candidate, "game mode");
            RequireResult(
                result.GetProperty("acts").EnumerateArray().Select(element => element.GetString())
                    .SequenceEqual(acts, StringComparer.Ordinal),
                candidate, "acts");

            var observation = result.GetProperty("observation");
            RequireResult(
                observation.GetProperty("file").GetString() == Path.GetFileName(observationPath) &&
                observation.GetProperty("sha256").GetString() == observationHash,
                candidate, "map observation");

            RequireResult(
                result.TryGetProperty("bound_manifest", out var manifest),
                candidate, "bound manifest state");
            if (boundManifest is null)
            {
                RequireResult(manifest.ValueKind == JsonValueKind.Null, candidate, "unbound manifest state");
            }
            else
            {
                RequireResult(
                    manifest.ValueKind == JsonValueKind.Object &&
                    manifest.GetProperty("file").GetString() == Path.GetFileName(manifestPath) &&
                    manifest.GetProperty("sha256").GetString() == manifestHash &&
                    manifest.GetProperty("run_id").GetString() == boundManifest.RunId &&
                    manifest.GetProperty("video_id").GetString() == boundManifest.Source.Video!.VideoId,
                    candidate, "bound manifest");
            }

            _ = result.GetProperty("comparison").GetProperty("matches").GetBoolean();
            return result.Clone();
        }
        catch (ManifestException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ManifestException(
                $"Seed candidate '{candidate}' did not emit a valid current comparison artifact: {exception.Message}");
        }
    }

    private static void RequireResult(bool condition, string candidate, string binding)
    {
        if (!condition)
        {
            throw new ManifestException(
                $"Seed candidate '{candidate}' emitted a comparison artifact with stale or incorrect {binding} binding.");
        }
    }

    private static string Sha256File(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    internal static string NegativeControlSeed(string seed)
    {
        if (string.IsNullOrEmpty(seed))
        {
            throw new ManifestException("Cannot derive a seed negative control from an empty seed.");
        }

        var index = ManifestValidator.SeedAlphabet.IndexOf(seed[0]);
        if (index < 0)
        {
            throw new ManifestException($"Cannot derive a seed negative control from illegal seed '{seed}'.");
        }

        var replacement = ManifestValidator.SeedAlphabet[(index + 1) % ManifestValidator.SeedAlphabet.Length];
        return replacement + seed[1..];
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
