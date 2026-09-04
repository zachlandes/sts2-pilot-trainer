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
/// right one. The Runmobile host runs both against the player's live game. The
/// arbiter runs the first before it constructs a run and the second after, which is how it
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
    public static PreflightResult Prerequisites(
        EnvironmentIdentity expected, LocalPrerequisites actual, string sourceKind = "vod") =>
        Prerequisites(expected, actual, requireHost: false, sourceKind);

    private static PreflightResult Prerequisites(
        EnvironmentIdentity expected, LocalPrerequisites actual, bool requireHost, string sourceKind = "vod")
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
            EvaluateSourceMods(expected.Mods.Value, sourceKind),
            EvaluateLocalMods(actual.Mods, requireHost),
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
        EnvironmentIdentity expected, LocalPrerequisites prerequisites, LocalRunReading? run,
        string sourceKind = "vod") =>
        new(Prerequisites(expected, prerequisites, requireHost: true, sourceKind), RunIdentity(expected, run),
            run is not null, prerequisites);

    private static IEnumerable<PreflightField> EvaluateUnlocks(
        EnvironmentIdentity expected, LocalPrerequisites actual)
    {
        var requirement = expected.Unlocks.Value;
        if (requirement.IsExact)
        {
            foreach (var field in EvaluateExactUnlocks(requirement, actual)) yield return field;
            yield return EvaluateActUnlocks(expected, actual);
            yield return EvaluateAscensionCeiling(expected, actual);
            yield break;
        }

        if (!requirement.IsComplete)
        {
            yield return new PreflightField(
                "unlocks_requirement", string.Join(" or ", UnlockRequirement.Completenesses),
                requirement.Completeness, false,
                $"The manifest asks for unlock completeness '{requirement.Completeness}', which this arbiter " +
                $"cannot check. The expressible requirements are " +
                $"{string.Join(" and ", UnlockRequirement.Completenesses)}, because something can check each: " +
                "the build enumerates what it ships, and a recorder enumerates what the player had.");
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
    /// An exact requirement names the values the game's own unlock state is
    /// constructed from, so the question is whether this build knows every id in it.
    ///
    /// The state is built and supplied rather than found, which is why the run count
    /// is reported and not compared: nothing about this installation has to match it
    /// for the state to be constructible. The two id lists do have to be known here,
    /// because an id this build never heard of cannot go into a state at all.
    /// </summary>
    private static IEnumerable<PreflightField> EvaluateExactUnlocks(
        UnlockRequirement requirement, LocalPrerequisites actual)
    {
        if (requirement.Inventory is not { } inventory)
        {
            yield return new PreflightField(
                "unlocks_requirement", UnlockRequirement.ExactCompleteness, "no inventory", false,
                "The manifest asks for unlock completeness 'exact' and names no inventory, so there is nothing " +
                "to check this environment against. An exact requirement is exactly the state it names.");
            yield break;
        }

        yield return new PreflightField(
            "unlocks_requirement", UnlockRequirement.ExactCompleteness, actual.Unlocks.Origin, true);

        foreach (var (name, ids) in inventory.IdLists())
        {
            if (actual.Unlocks.ShippedIds is not { } shipped ||
                !shipped.TryGetValue(name, out var available))
            {
                yield return new PreflightField(
                    $"unlocks_{name}", ids.Count.ToString(CultureInfo.InvariantCulture), "not enumerated", false,
                    $"The recording names {ids.Count.ToString(CultureInfo.InvariantCulture)} {name} and this " +
                    "environment did not enumerate what it ships, so the requirement was not checked. An " +
                    "unchecked requirement reported as met is the answer this project exists to prevent.");
                continue;
            }

            var missing = ids.Where(id => !available.Contains(id, StringComparer.Ordinal)).ToList();
            var sample = missing.Count == 0
                ? string.Empty
                : $" Missing, for example: {string.Join(", ", missing.Take(MissingSampleLimit))}.";

            yield return new PreflightField(
                $"unlocks_{name}",
                ids.Count.ToString(CultureInfo.InvariantCulture),
                available.Count.ToString(CultureInfo.InvariantCulture),
                missing.Count == 0,
                missing.Count == 0
                    ? null
                    : $"This build does not ship {missing.Count.ToString(CultureInfo.InvariantCulture)} of the " +
                      $"{ids.Count.ToString(CultureInfo.InvariantCulture)} {name} the recording was played " +
                      $"with, so the unlock state it was generated against cannot be built here and the same " +
                      $"seed produces a different run.{sample} {UnlockRemediation}");
        }

        // Reported rather than compared: the state is constructed from the recording's
        // own run count, so nothing about this installation has to match it.
        yield return new PreflightField(
            "unlocks_runs", inventory.Runs.ToString(CultureInfo.InvariantCulture), "supplied to the run", true);
    }

    /// <summary>How many missing ids a diagnostic names before it stops listing them.
    /// A shortfall of three hundred cards is a sentence, not a wall of ids.</summary>
    private const int MissingSampleLimit = 8;

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

    /// <summary>The mod id the in-game host ships under. Its own failure is a
    /// different problem from somebody else's mod being present, and telling them
    /// apart is what stops a player being sent to disable mods they do not have.
    ///
    /// It is the shell's id, not the Combat Trainer's: the Combat Trainer is one
    /// module inside the mod a player installs, and the mod list only ever shows the
    /// shell. See docs/in-game-host.md.</summary>
    private const string HostModId = "Runmobile";

    /// <summary>The name that same mod declares. Held here rather than read from
    /// <c>TrainerCopy</c> because this project has no dependency on the wording
    /// layer, and because this is a fact about a manifest rather than a sentence
    /// shown to anybody.</summary>
    private const string HostModName = "Runmobile";

    private static PreflightField EvaluateLocalMods(IReadOnlyList<LocalMod> mods, bool requireHost)
    {
        var active = mods
            .Where(mod => mod.State is not ("Disabled" or "DisabledDuplicate"))
            .ToList();
        var hostIsTheOnlyActiveMod = active.Count == 1 &&
                                     active[0] is
                                     {
                                         Id: HostModId,
                                         Name: HostModName,
                                         AffectsGameplay: false,
                                         State: "Loaded",
                                     };
        var permitted = hostIsTheOnlyActiveMod || !requireHost && active.Count == 0;

        // What is actually wrong, kept apart. A game whose only active mod is this one,
        // failed, has nothing to do with compatibility: telling that player to disable
        // every mod except Runmobile sends them to fix somebody else's mod when
        // the only broken thing is ours, and blames a clean install for our defect.
        var otherModsPresent = active.Any(mod => mod.Id != HostModId);
        var hostFailedAlone = !otherModsPresent &&
                              active.Count > 0 &&
                              !active.Any(mod => mod is { Id: HostModId, State: "Loaded" });

        var actual = mods.Count == 0
            ? "none discovered"
            : string.Join("; ", mods.Select(mod =>
                $"{mod.Name} ({mod.Id}, {mod.Version}, state: {mod.State}, " +
                $"affects gameplay: {mod.AffectsGameplay})"));

        return new PreflightField(
            "loaded_mod_environment",
            $"no active local mods except this loaded non-gameplay {HostModName} host",
            actual,
            permitted,
            permitted ? null : Refusal());

        string Refusal()
        {
            if (requireHost && active.Count == 0)
            {
                return $"The running game did not report {HostModName} as loaded, so its mod environment " +
                       $"cannot be established. Restart the game with only {HostModName} enabled, and check " +
                       "again.";
            }

            if (hostFailedAlone) return $"{HostModName} failed to load. Restart the game and check again.";

            // Everything else is another mod actually being there - or, unreachably for
            // a correctly shipped build, this host loading while declaring itself
            // something other than the non-gameplay one its manifest contract requires.
            return "The running game has another active or failed mod. Its behaviour cannot be established as " +
                   "identical to the recording from the content hash, because a failed mod can leave resources " +
                   "loaded and that hash does not cover behaviour patches or mods that declare themselves " +
                   $"non-gameplay. Disable every mod except {HostModName}, restart the game, and check again.";
        }
    }

    private static PreflightField EvaluateSourceMods(ModEnvironment mods, string sourceKind)
    {
        if (sourceKind == "native") return EvaluateNativeSourceMods(mods);

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

    /// <summary>
    /// A native recording's mod list is a reading rather than an audit, so the rule is
    /// a rule rather than a fixed set of names.
    ///
    /// The recorder reads the loaded list out of the game it is running in, which is
    /// why this can be a general rule at all - a video only ever showed a count. Every
    /// mod that was loaded must declare itself non-gameplay: the content hash is blind
    /// to behaviour patches, so a mod that says it changes gameplay is one whose
    /// effect on the run nothing here has bounded.
    /// </summary>
    private static PreflightField EvaluateNativeSourceMods(ModEnvironment mods)
    {
        var undeclared = mods.Mods.Where(mod => mod.AffectsGameplay is null).ToList();
        var gameplayAffecting = mods.Mods.Where(mod => mod.AffectsGameplay is true).ToList();
        var identified = mods.Mods.Count == mods.ReportedCount;
        var matches = identified && gameplayAffecting.Count == 0 && undeclared.Count == 0;

        return new PreflightField(
            "mod_environment",
            $"{mods.Name} ({mods.ReportedCount.ToString(CultureInfo.InvariantCulture)} mod(s))",
            matches
                ? "every recorded mod declares itself non-gameplay"
                : $"{gameplayAffecting.Count.ToString(CultureInfo.InvariantCulture)} gameplay-affecting, " +
                  $"{undeclared.Count.ToString(CultureInfo.InvariantCulture)} undeclared, " +
                  $"{(mods.ReportedCount - mods.Mods.Count).ToString(CultureInfo.InvariantCulture)} unidentified",
            matches,
            matches
                ? null
                : !identified
                    ? $"The recorder reported {mods.ReportedCount.ToString(CultureInfo.InvariantCulture)} mod(s) " +
                      $"loaded and identified {mods.Mods.Count.ToString(CultureInfo.InvariantCulture)}. An " +
                      "unidentified mod is exactly the gap the content hash cannot close."
                    : undeclared.Count > 0
                        ? $"This recording lists mod(s) that say nothing about whether they change gameplay: " +
                          $"{string.Join("; ", undeclared.Select(mod => mod.Name))}. A recorder reads that " +
                          $"declaration out of each mod's own manifest, so an absence here is a reading that was " +
                          $"never taken rather than a mod that changes nothing. {ContentHashScope}"
                        : $"This recording was played with mod(s) that declare themselves gameplay-affecting: " +
                          $"{string.Join("; ", gameplayAffecting.Select(mod => mod.Name))}. {ContentHashScope}");
    }
}
