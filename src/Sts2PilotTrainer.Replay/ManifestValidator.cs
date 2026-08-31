using System.Text.RegularExpressions;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Structural checks a manifest must pass before an arbiter will spend a process on
/// it. Every rule here rejects a specific way a manifest can look plausible and be
/// wrong; each has a matching negative test, because a validator nobody has fed a
/// bad input to is a validator that has never been shown to reject anything.
/// </summary>
public static partial class ManifestValidator
{
    /// <summary>
    /// The game's seed alphabet, read from the shipping assembly and matching
    /// MegaCrit's own documentation: O and I are absent because they are replaced by
    /// 0 and 1. So an O or an I in a seed is a known misreading with a known
    /// correction, and accepting one silently would key an artifact to a run that
    /// cannot exist.
    /// </summary>
    public const string SeedAlphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private static readonly string[] KnownGameModes = ["standard", "custom", "daily"];

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    private static partial Regex BuildVersionPattern { get; }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}$")]
    private static partial Regex BuildDatePattern { get; }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex ContentHashPattern { get; }

    public static ValidationResult Validate(ReplayManifest manifest)
    {
        var problems = new List<string>();

        ValidateEnvironment(manifest.Environment, problems);
        ValidateSource(manifest.Source, problems);
        ValidateActions(manifest.Actions, problems);
        ValidateCheckpoints(manifest.Checkpoints, manifest.Actions, problems);

        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            problems.Add("run_id is empty. Every artifact needs a stable identifier that is not a video title.");
        }

        return new ValidationResult(problems.Count == 0, problems);
    }

    private static void ValidateEnvironment(EnvironmentIdentity env, List<string> problems)
    {
        if (!BuildVersionPattern.IsMatch(env.BuildVersion.Value))
        {
            problems.Add($"environment.build_version '{env.BuildVersion.Value}' is not of the form vMAJOR.MINOR.PATCH.");
        }

        if (!BuildDatePattern.IsMatch(env.BuildDateUtc.Value))
        {
            problems.Add(
                $"environment.build_date_utc '{env.BuildDateUtc.Value}' is not of the form YYYY.MM.DD. " +
                "This is compared against the game's version overlay, which renders the UTC date.");
        }

        if (!KnownGameModes.Contains(env.GameMode.Value, StringComparer.Ordinal))
        {
            problems.Add(
                $"environment.game_mode '{env.GameMode.Value}' is not one of: {string.Join(", ", KnownGameModes)}. " +
                "Game mode is persisted by the game on every run and changes run setup, so it is part of identity.");
        }

        ValidateSeed(env.Seed.Value, problems);

        if (!ContentHashPattern.IsMatch(env.ContentHash.Value))
        {
            problems.Add(
                $"environment.content_hash '{env.ContentHash.Value}' is not a decimal integer. " +
                "It is the game's own ModelDb id-database hash, which is what its multiplayer layer compares.");
        }

        if (env.Ascension.Value is < 0 or > 20)
        {
            problems.Add($"environment.ascension {env.Ascension.Value} is outside the range the game offers.");
        }

        if (!env.Character.Value.StartsWith("CHARACTER.", StringComparison.Ordinal))
        {
            problems.Add($"environment.character '{env.Character.Value}' is not a model id (expected CHARACTER.*).");
        }

        if (env.Acts.Value.Count == 0)
        {
            problems.Add(
                "environment.acts is empty. The acts a run climbs are part of its identity - this game ships " +
                "more than one act at some indices, and the wrong variant generates different content from " +
                "the same seed without changing the map.");
        }

        foreach (var act in env.Acts.Value.Where(a => !a.StartsWith("ACT.", StringComparison.Ordinal)))
        {
            problems.Add($"environment.acts contains '{act}', which is not a model id (expected ACT.*).");
        }

        // The point of recording provenance is that it can be checked. An identity
        // field nobody claims to have observed or derived is a field somebody typed.
        foreach (var (name, source) in new (string, FactSource)[]
                 {
                     ("build_version", env.BuildVersion.Source),
                     ("build_date_utc", env.BuildDateUtc.Source),
                     ("game_mode", env.GameMode.Source),
                     ("seed", env.Seed.Source),
                     ("content_hash", env.ContentHash.Source),
                     ("ascension", env.Ascension.Source),
                     ("character", env.Character.Source),
                     ("acts", env.Acts.Source),
                 })
        {
            if (source == FactSource.Engine)
            {
                problems.Add(
                    $"environment.{name} is marked source=engine. Environment identity states what the " +
                    "engine must be, so it cannot be something the engine produced - that would be circular.");
            }
        }
    }

    private static void ValidateSeed(string seed, List<string> problems)
    {
        if (seed.Length == 0)
        {
            problems.Add("environment.seed is empty.");
            return;
        }

        var illegal = seed.Where(c => !SeedAlphabet.Contains(c, StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        if (illegal.Length > 0)
        {
            var hints = illegal
                .Select(c => c switch
                {
                    'O' => "'O' is not in the alphabet - the game renders that character as '0'",
                    'I' => "'I' is not in the alphabet - the game renders that character as '1'",
                    _ => $"'{c}' is not in the alphabet",
                });
            problems.Add(
                $"environment.seed '{seed}' contains characters the game never generates: " +
                string.Join("; ", hints) + ".");
        }
    }

    private static void ValidateSource(SourceProvenance source, List<string> problems)
    {
        if (source.Kind == "vod" && source.Video is null)
        {
            problems.Add("source.kind is 'vod' but source.video is absent, so no reader could re-check any observation.");
        }

        if (string.IsNullOrWhiteSpace(source.Coverage))
        {
            problems.Add(
                "source.coverage is empty. A partial history is acceptable; a partial history that does not " +
                "say where it stops is not.");
        }
    }

    private static void ValidateActions(IReadOnlyList<ActionRecord> actions, List<string> problems)
    {
        if (actions.Count == 0)
        {
            problems.Add("actions is empty. Exact reconstruction means replaying the ordered history from run start.");
            return;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].Seq != i)
            {
                problems.Add(
                    $"actions[{i}] has seq={actions[i].Seq}, expected {i}. Sequence numbers must be dense and " +
                    "start at 0 - a gap is a missing action wearing a plausible face.");
                break;
            }
        }

        foreach (var action in actions)
        {
            if (action.Source == FactSource.Engine)
            {
                problems.Add(
                    $"actions[{action.Seq}] is marked source=engine. Actions are inputs to the replay; " +
                    "an action the engine produced is not evidence about the run.");
            }

            if (action.Source == FactSource.Observed && action.Evidence?.VideoTimeMs is null)
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) claims to be observed but carries no video timestamp, " +
                    "so the claim cannot be re-checked against the source.");
            }
        }
    }

    private static void ValidateCheckpoints(
        IReadOnlyList<Checkpoint> checkpoints, IReadOnlyList<ActionRecord> actions, List<string> problems)
    {
        if (checkpoints.Count == 0)
        {
            problems.Add(
                "checkpoints is empty. A replay with nothing to disagree with proves only that it ran.");
        }

        var maxSeq = actions.Count - 1;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var checkpoint in checkpoints)
        {
            if (!seenIds.Add(checkpoint.Id))
            {
                problems.Add($"checkpoint id '{checkpoint.Id}' is used more than once.");
            }

            if (checkpoint.AfterSeq < -1 || checkpoint.AfterSeq > maxSeq)
            {
                problems.Add(
                    $"checkpoint '{checkpoint.Id}' has after_seq={checkpoint.AfterSeq}, outside the action range " +
                    $"[-1, {maxSeq}].");
            }

            if (checkpoint.Expect.Count == 0)
            {
                problems.Add($"checkpoint '{checkpoint.Id}' expects nothing, so it can never fail.");
            }

            foreach (var (field, fact) in checkpoint.Expect)
            {
                if (fact.Source == FactSource.Engine)
                {
                    problems.Add(
                        $"checkpoint '{checkpoint.Id}' field '{field}' is marked source=engine. A checkpoint " +
                        "compares the engine against an independent observation; comparing the engine against " +
                        "itself always passes and means nothing.");
                }
            }
        }
    }

    public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Problems)
    {
        public string Describe() => IsValid
            ? "manifest is valid"
            : string.Join("\n", Problems.Select(p => "  - " + p));
    }
}
