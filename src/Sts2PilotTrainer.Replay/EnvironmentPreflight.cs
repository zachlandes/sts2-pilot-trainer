using System.Globalization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The rules that decide whether this machine's game is the one a manifest was
/// recorded against, and whether the run in front of us is the run it describes.
///
/// Pure by construction: it compares a manifest against a reading someone else
/// took. That is what lets every rule here have a test on a build with no game
/// installed, and it is why the rules survive a game update that moves where a
/// value is stored - only the reader has to change.
///
/// Two gates, because they answer different questions at different moments:
/// <see cref="Prerequisites"/> asks whether a matching run could be played here at
/// all, and <see cref="RunIdentity"/> asks whether the run that now exists is the
/// right one. An eventual mod entry point must run both against the player's live game. The arbiter runs
/// the first before it constructs a run and the second after, which is how it
/// learns that the engine built what the manifest asked for.
///
/// Everything here refuses rather than approximates. Replaying into a mismatched
/// environment does not fail - it succeeds at producing a different run, and every
/// downstream check then compares the wrong things confidently.
/// </summary>
public static class EnvironmentPreflight
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

    /// <summary>
    /// The one remediation this project offers for a missing unlock. Stated once so
    /// that no diagnostic anywhere can drift into suggesting a shortcut: the save is
    /// a read-only input, and a tool that edits someone's progress to make a replay
    /// possible has destroyed the thing the replay was evidence about.
    /// </summary>
    public const string UnlockRemediation =
        "Unlock the remaining content by playing the game. This tool never writes to your save, your " +
        "progress, your unlocks or your installed build, and there is no supported flag that would.";

    /// <summary>
    /// Everything checkable before a run exists: the build, the content, and the
    /// player prerequisites the run's generation will read.
    /// </summary>
    public static PreflightResult Prerequisites(EnvironmentIdentity expected, LocalPrerequisites actual)
    {
        var fields = new List<PreflightField>
        {
            Compare("build_version", expected.BuildVersion.Value, actual.BuildVersion,
                "Replaying on a different build means different content and different balance. There is no " +
                "migration path: record the build a run came from and refuse anything else. Install the " +
                "matching build through the game's own version selection; this tool never changes it for you."),

            Compare("build_date_utc", expected.BuildDateUtc.Value, actual.BuildDateUtc,
                "The game's version overlay renders the release timestamp in UTC. A mismatch here with a " +
                "matching version usually means the date was compared in local time."),

            Compare("content_hash", expected.ContentHash.Value, actual.ContentHash, ContentHashScope),

            EvaluateSeedAlphabet(expected.Seed.Value),
            EvaluateSupportedMode(expected.GameMode.Value),
            EvaluateMods(expected.Mods.Value),
        };

        fields.AddRange(EvaluateUnlocks(expected, actual));
        return new PreflightResult(fields.All(field => field.Matches), fields);
    }

    /// <summary>
    /// Whether the run that exists right now is the run the manifest describes.
    ///
    /// A null reading is a refusal, not a skip. "No run in progress" is the ordinary
    /// state of a freshly launched game, and answering a question about a run that
    /// does not exist is how a tool ends up giving advice about someone else's.
    /// </summary>
    public static PreflightResult RunIdentity(EnvironmentIdentity expected, LocalRunReading? actual)
    {
        if (actual is null)
        {
            return new PreflightResult(false,
            [
                new PreflightField(
                    "run_present", "a run matching this manifest", "no run in progress", false,
                    $"There is no run to compare against. Start a run with seed '{expected.Seed.Value}' at " +
                    $"ascension {expected.Ascension.Value.ToString(CultureInfo.InvariantCulture)} as " +
                    $"{expected.Character.Value}, then run this again."),
            ]);
        }

        var fields = new List<PreflightField>
        {
            new("run_present", "a run matching this manifest", actual.Origin, true),

            Compare("run_seed", expected.Seed.Value, actual.Seed,
                "This run was generated from a different seed, so it is a different run from the first floor " +
                "onward. Abandon it and start a run on the manifest's seed; nothing can convert one into the " +
                "other after the fact."),

            Compare("run_game_mode", expected.GameMode.Value, actual.GameMode,
                "Game mode is persisted on every run and changes run setup, so the same seed in another mode " +
                "is another run. Start the run again in the mode the manifest records."),

            Compare("run_ascension",
                expected.Ascension.Value.ToString(CultureInfo.InvariantCulture),
                actual.Ascension.ToString(CultureInfo.InvariantCulture),
                "Ascension changes enemy composition and run setup. Start the run again at the ascension the " +
                "manifest records; the level of a run in progress is fixed when it begins."),

            Compare("run_character", expected.Character.Value, actual.Character,
                "A different character draws from different pools, so no part of this run corresponds to the " +
                "manifest's. Start the run again as the character the manifest records."),

            Compare("run_acts", string.Join(", ", expected.Acts.Value), string.Join(", ", actual.Acts),
                "This build ships more than one act at some indices, and the wrong variant generates entirely " +
                "different encounters, events and relics from the same seed while producing the same map - so " +
                "nothing about the map would reveal the substitution."),
        };

        return new PreflightResult(fields.All(field => field.Matches), fields);
    }

    /// <summary>Both gates as one verdict, which is how the arbiter asks the question.</summary>
    public static PreflightResult Combine(PreflightResult prerequisites, PreflightResult runIdentity) =>
        new(prerequisites.Matches && runIdentity.Matches,
            [.. prerequisites.Fields, .. runIdentity.Fields]);

    /// <summary>
    /// Both gates as a live host has to ask them: separably, and with the sequencing
    /// recorded.
    ///
    /// Same rules, same order, no softening - <see cref="RunIdentity"/> still refuses
    /// a null reading, and where a run exists its verdict still counts. What changes
    /// is that the host can tell "you have not started the run yet" apart from "your
    /// install cannot play this", which one combined field list cannot express. See
    /// <see cref="LivePreflight"/>.
    /// </summary>
    public static LivePreflight LiveGame(
        EnvironmentIdentity expected, LocalPrerequisites prerequisites, LocalRunReading? run) =>
        new(Prerequisites(expected, prerequisites), RunIdentity(expected, run), run is not null, prerequisites);

    private static IEnumerable<PreflightField> EvaluateUnlocks(
        EnvironmentIdentity expected, LocalPrerequisites actual)
    {
        var requirement = expected.Unlocks.Value;
        if (!requirement.IsComplete)
        {
            yield return new PreflightField(
                "unlocks_requirement", UnlockRequirement.CompleteCompleteness, requirement.Completeness, false,
                $"The manifest asks for unlock completeness '{requirement.Completeness}', which this arbiter " +
                "cannot check. Only 'complete' is expressible, because it is the only requirement the build " +
                "can enumerate for itself - a partial one would name ids nobody read off the video.");
            yield break;
        }

        yield return new PreflightField(
            "unlocks_requirement", UnlockRequirement.CompleteCompleteness,
            actual.Unlocks.Origin, true);

        foreach (var category in actual.Unlocks.Categories)
        {
            var missing = category.MissingSample.Count == 0
                ? string.Empty
                : $" Missing, for example: {string.Join(", ", category.MissingSample)}.";
            yield return new PreflightField(
                $"unlocks_{category.Name}",
                category.Required.ToString(CultureInfo.InvariantCulture),
                category.Available.ToString(CultureInfo.InvariantCulture),
                category.IsComplete,
                category.IsComplete
                    ? null
                    : $"This environment has {category.Available} of the {category.Required} {category.Name} " +
                      $"this build ships, so its generation pools are smaller than the source run's and the " +
                      $"same seed produces a different run.{missing} {UnlockRemediation}");
        }

        yield return EvaluateActUnlocks(expected, actual);
        yield return EvaluateAscensionCeiling(expected, actual);
    }

    /// <summary>
    /// Whether the acts this run climbs are available in this unlock state.
    ///
    /// Asked of the game, act by act, rather than inferred from the category counts:
    /// a shortfall of one act is invisible in a total, and it is the one shortfall
    /// that changes every fight in the run.
    /// </summary>
    private static PreflightField EvaluateActUnlocks(
        EnvironmentIdentity expected, LocalPrerequisites actual)
    {
        var wanted = string.Join(", ", expected.Acts.Value);
        if (actual.LockedActs.Count == 0)
        {
            return new PreflightField("acts_unlocked", wanted, "all unlocked", true);
        }

        return new PreflightField(
            "acts_unlocked", wanted, $"locked: {string.Join(", ", actual.LockedActs)}", false,
            $"This environment cannot climb {string.Join(", ", actual.LockedActs)}: the game reports the act " +
            "locked under the unlock state a run here would be generated against. An act that is not unlocked " +
            "is not merely unavailable - the run would take the other variant shipped at the same index, which " +
            $"generates different content from the same seed while producing the same map. {UnlockRemediation}");
    }

    /// <summary>
    /// Whether the ascension the manifest records can be played here.
    ///
    /// Only answerable against a real profile: the game records a per-character
    /// ceiling in save progress, and a host that constructs a run directly never
    /// consults it. Saying so is better than reporting a pass nobody measured.
    /// </summary>
    private static PreflightField EvaluateAscensionCeiling(
        EnvironmentIdentity expected, LocalPrerequisites actual)
    {
        var required = expected.Ascension.Value;
        var wanted = $"ascension {required.ToString(CultureInfo.InvariantCulture)} available";

        if (!actual.Unlocks.FromPlayerProfile)
        {
            return new PreflightField(
                "ascension_unlocked", wanted,
                $"not gated: {actual.Unlocks.Origin}", true);
        }

        var ceiling = actual.ProfileAscensionCeiling ?? 0;
        return new PreflightField(
            "ascension_unlocked", wanted,
            $"profile ceiling {ceiling.ToString(CultureInfo.InvariantCulture)} for {expected.Character.Value}",
            ceiling >= required,
            ceiling >= required
                ? null
                : $"This profile's highest available ascension for {expected.Character.Value} is " +
                  $"{ceiling.ToString(CultureInfo.InvariantCulture)}, and the manifest records ascension " +
                  $"{required.ToString(CultureInfo.InvariantCulture)}. The game raises that ceiling when you " +
                  $"finish a run at the level below it. {UnlockRemediation}");
    }

    private static PreflightField Compare(string field, string expected, string actual, string diagnostic)
    {
        var matches = string.Equals(expected, actual, StringComparison.Ordinal);
        return new PreflightField(field, expected, actual, matches, matches ? null : diagnostic);
    }

    /// <summary>
    /// Checks the seed against the alphabet the game can actually produce, which is
    /// a real check and not a formality: the two characters missing from that
    /// alphabet are exactly the two an OCR reader invents.
    /// </summary>
    private static PreflightField EvaluateSeedAlphabet(string seed)
    {
        var illegal = seed
            .Where(c => !ManifestValidator.SeedAlphabet.Contains(c, StringComparison.Ordinal))
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

    private static PreflightField EvaluateSupportedMode(string gameMode) =>
        gameMode == "standard"
            ? new PreflightField("game_mode_supported", "standard", "standard", true)
            : new PreflightField(
                "game_mode_supported", gameMode, "only 'standard' is implemented", false,
                $"Game mode '{gameMode}' is recorded but this milestone only replays standard runs. " +
                "Daily and custom runs carry modifiers that change run setup, so replaying one as standard " +
                "would produce a different run under the same seed.");

    private static PreflightField EvaluateMods(ModEnvironment mods)
    {
        var isVanilla = mods.ReportedCount == 0 && mods.Mods.Count == 0;
        var expectedUtilities = new HashSet<string>(StringComparer.Ordinal)
        {
            "Slay the Relics Exporter",
            "BaseLib",
            "Hindsight",
        };
        var isAuditedSourceTooling =
            mods.ReportedCount == 3 && mods.Mods.Count == 3 &&
            mods.Mods.Select(mod => mod.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expectedUtilities);
        var matches = isVanilla || isAuditedSourceTooling;

        return new PreflightField(
            "mod_environment",
            $"{mods.Name} ({mods.ReportedCount.ToString(CultureInfo.InvariantCulture)} mod(s))",
            isAuditedSourceTooling ? "audited source tooling" : "none loaded",
            matches,
            matches
                ? null
                : $"This host does not load the unrecognized source environment {mods.Name}: " +
                  $"{string.Join("; ", mods.Mods.Select(mod => mod.Name))}. Refusing because its gameplay " +
                  "behavior has not been bounded.");
    }
}
