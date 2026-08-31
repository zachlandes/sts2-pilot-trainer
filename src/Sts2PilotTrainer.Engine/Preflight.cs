using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Decides whether this machine's game is the one a manifest was recorded against.
///
/// Reads the local identity and compares it; it changes nothing, and it refuses
/// rather than approximating. Refusing is the useful behaviour: replaying a run in
/// the wrong environment does not fail, it succeeds at producing a different run,
/// and every downstream check would then be comparing the wrong things confidently.
/// </summary>
public static class Preflight
{
    /// <summary>
    /// What a matching content hash does and does not establish.
    ///
    /// It is a checksum over the model-id database. Mods that declare themselves
    /// gameplay-affecting contribute their ids to it, so a match rules out that
    /// class of divergence. It says nothing about a mod that patches behaviour
    /// without adding content, or one that declares itself non-gameplay - the
    /// game's own warning about the hash omitting ids says as much. So the hash is
    /// a necessary gate and never, on its own, proof of behavioural parity.
    /// </summary>
    public const string ContentHashScope =
        "The content hash is a checksum over the model-id database. It covers content added by mods that " +
        "declare themselves gameplay-affecting, and does not cover behaviour patches or mods that declare " +
        "themselves non-gameplay. Hash equality is a necessary gate, not proof of environment parity.";

    public static PreflightResult Evaluate(EnvironmentIdentity expected)
    {
        var actual = GameIdentity.Read();
        var fields = new List<PreflightField>
        {
            Compare("build_version", expected.BuildVersion.Value, actual.BuildVersion,
                "Replaying on a different build means different content and different balance. There is no " +
                "migration path: record the build a run came from and refuse anything else."),

            Compare("build_date_utc", expected.BuildDateUtc.Value, actual.BuildDateUtc,
                "The game's version overlay renders the release timestamp in UTC. A mismatch here with a " +
                "matching version usually means the date was compared in local time."),

            Compare("content_hash", expected.ContentHash.Value, actual.ContentHash, ContentHashScope),
        };

        fields.Add(EvaluateSeed(expected.Seed.Value));
        fields.Add(EvaluateGameMode(expected.GameMode.Value));
        fields.Add(EvaluateMods(expected.Mods.Value));

        return new PreflightResult(fields.All(f => f.Matches), fields);
    }

    private static PreflightField Compare(string field, string expected, string actual, string diagnostic) =>
        new(field, expected, actual, string.Equals(expected, actual, StringComparison.Ordinal),
            string.Equals(expected, actual, StringComparison.Ordinal) ? null : diagnostic);

    /// <summary>
    /// Checks the seed against the alphabet the game can actually produce, which is
    /// a real check and not a formality: the two characters missing from that
    /// alphabet are exactly the two an OCR reader invents.
    /// </summary>
    private static PreflightField EvaluateSeed(string seed)
    {
        var illegal = seed.Where(c => !ManifestValidator.SeedAlphabet.Contains(c, StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        return illegal.Length == 0
            ? new PreflightField("seed_alphabet", "legal", "legal", true)
            : new PreflightField(
                "seed_alphabet", "legal", $"illegal: {string.Join(",", illegal)}", false,
                $"The seed contains {string.Join(", ", illegal.Select(c => $"'{c}'"))}, which this game never " +
                "generates - its alphabet omits O and I, rendering them as 0 and 1. A seed like this was " +
                "misread rather than observed.");
    }

    private const string ModEnvironmentField = "mod_environment";

    private static PreflightField EvaluateMods(ModEnvironment mods)
    {
        var waiver = mods.HeadlessParityWaiver;
        var matches = waiver?.IsEstablished == true;

        return new PreflightField(
            ModEnvironmentField,
            $"{mods.Name} ({mods.ReportedCount} mod(s))",
            "none loaded",
            matches,
            matches
                ? null
                : $"This host loads no mods, while the source environment was {mods.Name}: " +
                  $"{string.Join("; ", mods.Mods.Select(m => m.Name))}. Publication requires an explicit " +
                  "headless parity waiver backed by matching executable A/B event digests and state checksums.");
    }

    private static PreflightField EvaluateGameMode(string gameMode) =>
        gameMode == "standard"
            ? new PreflightField("game_mode", "standard", "standard", true)
            : new PreflightField(
                "game_mode", gameMode, "only 'standard' is implemented", false,
                $"Game mode '{gameMode}' is recorded but this milestone only replays standard runs. " +
                "Daily and custom runs carry modifiers that change run setup, so replaying one as standard " +
                "would produce a different run under the same seed.");
}
