using System.Globalization;
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

    /// <summary>
    /// The generated engine fixture's current shape. Pinned rather than accepted as
    /// any version, so a fixture emitted by an older generator cannot pass as one this
    /// build's claims are made about. Version 3 can walk a whole act; version 2 played
    /// its combat to the end; version 1 stopped after the opening turn.
    /// </summary>
    public const int SyntheticFixtureVersion = 3;

    private static readonly string[] KnownGameModes = ["standard", "custom", "daily"];

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    private static partial Regex BuildVersionPattern { get; }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}$")]
    private static partial Regex BuildDatePattern { get; }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex ContentHashPattern { get; }

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$")]
    private static partial Regex SnapshotDigestPattern { get; }

    /// <summary>
    /// The same question, asked of the manifest a verified replay just wrote.
    ///
    /// The boundary cross-checks only fire where a trace is attached, and the only
    /// manifest that carries one is that copy - not the file a recorder wrote or a
    /// stranger submitted. So the publication gate asks its declared-boundary
    /// condition through this rather than validating the file on disk twice, and a
    /// test with no game can ask for the same verdict.
    /// </summary>
    /// <returns>Null where it holds, and the refusal to report otherwise.</returns>
    public static string? RefusalForVerified(ReplayManifest verified)
    {
        var result = Validate(verified);
        return result.IsValid ? null : result.Describe();
    }

    public static ValidationResult Validate(ReplayManifest manifest)
    {
        var problems = new List<string>();

        var maxActionOrdinal = manifest.Actions.Count - 1;
        ValidateSource(manifest, manifest.Source, manifest.Actions, problems);
        ValidateDiscardedBranches(manifest, problems);
        var videoDurationMs = manifest.Source.Video is { DurationSeconds: > 0 } video
            ? checked(video.DurationSeconds * 1000)
            : 0;
        ValidateEnvironment(
            manifest.Environment, manifest.Source.Kind, videoDurationMs, maxActionOrdinal, problems);
        if (manifest.Source.Synthetic is { } synthetic &&
            !string.Equals(
                synthetic.GeneratedBuild, manifest.Environment.BuildVersion.Value, StringComparison.Ordinal))
        {
            problems.Add(
                "source.synthetic.generated_build must match environment.build_version for the pinned fixture.");
        }
        ValidateRunStart(manifest.Source, videoDurationMs, problems);
        ValidateRunSummary(manifest, videoDurationMs, problems);
        ValidateActions(manifest.Actions, manifest.Source.Kind, videoDurationMs, problems);
        ValidateCheckpoints(manifest, videoDurationMs, problems);
        ValidateBoundaries(manifest, problems);
        ValidateEvidenceTimeline(manifest, problems);

        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            problems.Add("run_id is empty. Every artifact needs a stable identifier that is not a video title.");
        }

        return new ValidationResult(problems.Count == 0, problems);
    }

    private static void ValidateEnvironment(
        EnvironmentIdentity env, string sourceKind, int videoDurationMs, int maxActionOrdinal,
        List<string> problems)
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

        var unlocks = env.Unlocks.Value;
        if (!unlocks.IsComplete && !unlocks.IsExact)
        {
            problems.Add(
                $"environment.unlocks.completeness '{unlocks.Completeness}' is not one of: " +
                $"{string.Join(", ", UnlockRequirement.Completenesses)}. Those two are expressible because " +
                "something can check them - the build enumerates what it ships, and a recorder enumerates what " +
                "the player had. Anything else would name unlock ids nobody read.");
        }

        if (sourceKind == "native" && !unlocks.IsExact)
        {
            problems.Add(
                "environment.unlocks.completeness must be 'exact' for a native recording. A recorder running " +
                "inside the player's own game reads the unlock state it was played with rather than inferring " +
                "completeness about its own author.");
        }

        if (unlocks.IsComplete && unlocks.Inventory is not null)
        {
            problems.Add(
                "environment.unlocks names an inventory alongside completeness 'complete'. Completeness against " +
                "the build and an enumerated inventory are two different requirements, and carrying both leaves " +
                "the reader to decide which one was meant.");
        }

        if (unlocks.IsExact)
        {
            if (unlocks.Inventory is not { } inventory)
            {
                problems.Add(
                    "environment.unlocks.completeness is 'exact' and no inventory is present. An exact " +
                    "requirement is exactly the ids it names, so one that names none asks for nothing.");
            }
            else
            {
                foreach (var (name, ids) in inventory.IdLists())
                {
                    if (ids.Any(string.IsNullOrWhiteSpace))
                    {
                        problems.Add($"environment.unlocks.inventory.{name} contains an empty id.");
                    }

                    if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                    {
                        problems.Add(
                            $"environment.unlocks.inventory.{name} names the same id more than once, so what it " +
                            "asks for cannot be read off it.");
                    }
                }

                if (inventory.Runs < 0)
                {
                    problems.Add(
                        $"environment.unlocks.inventory.runs is " +
                        $"{inventory.Runs.ToString(CultureInfo.InvariantCulture)}. The run count is one of the " +
                        "three values the game's unlock state is constructed from, and a negative one is not a " +
                        "state anything could be built into.");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(unlocks.Basis))
        {
            problems.Add(
                "environment.unlocks.basis is empty. Nothing in a video shows a creator's unlock state, so the " +
                "reason for the claim has to travel with it.");
        }

        if (!env.Character.Value.StartsWith("CHARACTER.", StringComparison.Ordinal))
        {
            problems.Add($"environment.character '{env.Character.Value}' is not a model id (expected CHARACTER.*).");
        }

        var mods = env.Mods.Value;
        if (string.IsNullOrWhiteSpace(mods.Name))
        {
            problems.Add("environment.mods.name is empty. The mod environment needs a name artifacts can refer to.");
        }

        if (mods.Mods.Count != mods.ReportedCount)
        {
            problems.Add(
                $"environment.mods lists {mods.Mods.Count} mod(s) but reports {mods.ReportedCount} were loaded. " +
                "An unidentified mod is exactly the gap the content hash cannot close, so the shortfall has to " +
                "be visible rather than rounded away.");
        }

        foreach (var mod in mods.Mods.Where(m => string.IsNullOrWhiteSpace(m.ReplayRisk)))
        {
            problems.Add(
                $"environment.mods entry '{mod.Name}' has no replay-risk assessment. A list of names without " +
                "assessments looks like diligence and carries none.");
        }

        if (mods.HeadlessParityWaiver is { } waiver)
        {
            ValidateParityWaiver(waiver, problems);
        }

        // A patched member with no owner names nobody, and one with no member names
        // nothing. Either would sit in a roster looking like a reading and answer no
        // question the roster exists to answer.
        foreach (var member in mods.Patches?.Members ?? [])
        {
            if (string.IsNullOrWhiteSpace(member.DeclaringType) || string.IsNullOrWhiteSpace(member.Member))
            {
                problems.Add(
                    "environment.mods.patch_roster has an entry that names no member. A roster is read by " +
                    "member name, so an unnamed entry is a row nobody can check.");
            }

            if (member.Owners.Count == 0)
            {
                problems.Add(
                    $"environment.mods.patch_roster entry '{member.DeclaringType}.{member.Member}' names no " +
                    "owner. Who patched a member is the whole reading; a patched member with no patcher is a " +
                    "reading that was not taken.");
            }
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

        ValidateInputFact(
            env.BuildVersion, "environment.build_version", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.BuildDateUtc, "environment.build_date_utc", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.GameMode, "environment.game_mode", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.Seed, "environment.seed", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.ContentHash, "environment.content_hash", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.Ascension, "environment.ascension", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.Unlocks, "environment.unlocks", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.Character, "environment.character", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.Acts, "environment.acts", sourceKind, videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.Mods, "environment.mods", sourceKind, videoDurationMs, maxActionOrdinal, problems);

        if (sourceKind == "native")
        {
            foreach (var (name, source, _) in EnvironmentFactSources(env))
            {
                if (source is not (FactSource.Captured or FactSource.Declared))
                {
                    problems.Add(
                        $"environment.{name} in a native recording is " +
                        $"source={source.ToString().ToLowerInvariant()}. A recorder reads the environment " +
                        "out of the game it is running in, so each field is captured - or declared, where it is " +
                        "a constant this project chose rather than a reading.");
                }
            }
        }

        if (sourceKind == "synthetic-engine")
        {
            foreach (var (name, source) in new (string, FactSource)[]
                     {
                         ("build_version", env.BuildVersion.Source),
                         ("build_date_utc", env.BuildDateUtc.Source),
                         ("game_mode", env.GameMode.Source),
                         ("seed", env.Seed.Source),
                         ("content_hash", env.ContentHash.Source),
                         ("ascension", env.Ascension.Source),
                         ("unlocks", env.Unlocks.Source),
                         ("character", env.Character.Source),
                         ("acts", env.Acts.Source),
                         ("mods", env.Mods.Source),
                     })
            {
                if (source != FactSource.Declared)
                {
                    problems.Add($"environment.{name} in a synthetic fixture must be declared.");
                }
            }

            if (mods.ReportedCount != 0 || mods.Mods.Count != 0 || mods.HeadlessParityWaiver is not null)
            {
                problems.Add("a synthetic-engine fixture must declare the unmodded headless environment.");
            }
        }
    }

    /// <summary>Every environment identity field, paired with the name it is reported
    /// under. One listing, so a rule about "every environment fact" cannot quietly
    /// mean a different set in two places.</summary>
    private static IEnumerable<(string Name, FactSource Source, FactEvidence? Evidence)> EnvironmentFactSources(
        EnvironmentIdentity env)
    {
        yield return ("build_version", env.BuildVersion.Source, env.BuildVersion.Evidence);
        yield return ("build_date_utc", env.BuildDateUtc.Source, env.BuildDateUtc.Evidence);
        yield return ("game_mode", env.GameMode.Source, env.GameMode.Evidence);
        yield return ("seed", env.Seed.Source, env.Seed.Evidence);
        yield return ("content_hash", env.ContentHash.Source, env.ContentHash.Evidence);
        yield return ("ascension", env.Ascension.Source, env.Ascension.Evidence);
        yield return ("unlocks", env.Unlocks.Source, env.Unlocks.Evidence);
        yield return ("character", env.Character.Source, env.Character.Evidence);
        yield return ("acts", env.Acts.Source, env.Acts.Evidence);
        yield return ("mods", env.Mods.Source, env.Mods.Evidence);
    }

    private static void ValidateParityWaiver(HeadlessParityWaiver waiver, List<string> problems)
    {
        problems.Add(
            "environment.mods.headless_parity_waiver is self-attested and cannot establish parity. " +
            "No full source-mod-set parity report is accepted by this milestone.");

        if (string.IsNullOrWhiteSpace(waiver.Justification) ||
            string.IsNullOrWhiteSpace(waiver.ExecutableCommand))
        {
            problems.Add(
                "environment.mods.headless_parity_waiver needs a justification and executable A/B command.");
        }

        if (!waiver.ResidualClosed.Contains("BaseLib v3.4.5 PowerCmd.Apply", StringComparison.Ordinal))
        {
            problems.Add(
                "environment.mods.headless_parity_waiver does not close the BaseLib v3.4.5 " +
                "PowerCmd.Apply continuation residual.");
        }

        if (string.IsNullOrWhiteSpace(waiver.ModdedEventDigest) ||
            !string.Equals(waiver.ModdedEventDigest, waiver.HeadlessEventDigest, StringComparison.Ordinal))
        {
            problems.Add("environment.mods.headless_parity_waiver A/B replay event digests do not match.");
        }

        if (string.IsNullOrWhiteSpace(waiver.ModdedStateChecksum) ||
            !string.Equals(waiver.ModdedStateChecksum, waiver.HeadlessStateChecksum, StringComparison.Ordinal))
        {
            problems.Add("environment.mods.headless_parity_waiver A/B state checksums do not match.");
        }
    }

    /// <summary>
    /// The characters in <paramref name="seed"/> the game's generator could never have
    /// produced. Public because ingestion screens a candidate seed long before there is
    /// a manifest to validate, and the alphabet must have one owner: a screen that
    /// accepted an 'O' would key an artifact to a run that cannot exist.
    /// </summary>
    public static IReadOnlyList<char> IllegalSeedCharacters(string seed) =>
        seed.Where(c => !SeedAlphabet.Contains(c, StringComparison.Ordinal)).Distinct().ToArray();

    private static void ValidateSeed(string seed, List<string> problems)
    {
        if (seed.Length == 0)
        {
            problems.Add("environment.seed is empty.");
            return;
        }

        var illegal = IllegalSeedCharacters(seed);

        if (illegal.Count > 0)
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

    private static void ValidateSource(
        ReplayManifest manifest, SourceProvenance source, IReadOnlyList<ActionRecord> actions,
        List<string> problems)
    {
        if (source.Kind is not ("vod" or "native" or "synthetic-engine"))
        {
            problems.Add(
                $"source.kind '{source.Kind}' is unsupported. This milestone accepts 'vod', 'native' and " +
                "'synthetic-engine'.");
        }

        if (source.Kind != "native" && source.Native is not null)
        {
            problems.Add($"source.native must be absent for a {source.Kind} manifest.");
        }

        if (source.Kind == "vod")
        {
            if (source.Video is null)
            {
                problems.Add("source.video is absent, so no reader could re-check any observation.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(source.Video.Platform))
                {
                    problems.Add("source.video.platform is empty.");
                }
                if (string.IsNullOrWhiteSpace(source.Video.VideoId))
                {
                    problems.Add("source.video.video_id is empty.");
                }
                if (string.IsNullOrWhiteSpace(source.Video.ChannelId))
                {
                    problems.Add("source.video.channel_id is empty.");
                }
                if (string.IsNullOrWhiteSpace(source.Video.ChannelName))
                {
                    problems.Add(
                        "source.video.channel_name is empty, so nothing that shows this recording to a player " +
                        "could name whose run it is without hardcoding it.");
                }
                if (source.Video.DurationSeconds <= 0)
                {
                    problems.Add("source.video.duration_s must be positive so observation timestamps can be bounded.");
                }
            }

            if (source.Synthetic is not null)
            {
                problems.Add("source.synthetic must be absent for a VOD manifest.");
            }
        }
        else if (source.Kind == "native")
        {
            ValidateNativeSource(manifest, source, actions, problems);
        }
        else if (source.Kind == "synthetic-engine")
        {
            if (source.Video is not null || source.RunStart is not null || source.RunSummary is not null)
            {
                problems.Add(
                    "a synthetic-engine source cannot carry video, run-start or run-summary evidence.");
            }

            if (source.Synthetic is not { } synthetic ||
                string.IsNullOrWhiteSpace(synthetic.FixtureId) ||
                synthetic.FixtureVersion != SyntheticFixtureVersion ||
                synthetic.Generator != "sts2-pilot-trainer" ||
                string.IsNullOrWhiteSpace(synthetic.GeneratedBuild))
            {
                problems.Add(
                    $"source.synthetic must identify a version-{SyntheticFixtureVersion} sts2-pilot-trainer " +
                    "engine fixture and its build.");
            }

            if (source.ExtractionMethod != "engine-generated")
            {
                problems.Add("a synthetic-engine source must use extraction_method 'engine-generated'.");
            }
        }

        if (string.IsNullOrWhiteSpace(source.Coverage))
        {
            problems.Add(
                "source.coverage is empty. A partial history is acceptable; a partial history that does not " +
                "say where it stops is not.");
        }
    }

    /// <summary>
    /// A recording this project's own recorder made carries the two facts nothing
    /// downstream could establish, and carries nothing that identifies its author.
    ///
    /// <c>witnessed_run_start</c> is the native counterpart of a video's run-start
    /// evidence and <c>continuity</c> is the counterpart of the end-of-run reading.
    /// Both are refused here rather than deferred, for the same reason
    /// <c>AGENTS.md</c> gives for their video equivalents: a history recorded from
    /// half way through a run, or with an unaccounted gap between sessions, replays
    /// perfectly and reconstructs a different run.
    ///
    /// <c>integrity</c> is the third fact of that kind, and it says whether the
    /// recording may ever be published: a run the console was used in, or one the
    /// recorder stopped watching at a decision it could not name, is kept whole and
    /// refused here, with what the recorder met printed beside the refusal.
    ///
    /// The one rule that relates a source to an argument lives here too: a native
    /// recording names every event option it chose by key as well as by position,
    /// because a recorder can read the key and a video cannot. A file migrated from a
    /// format older than the key is excused, and says so in
    /// <c>migrated_from_version</c>, rather than being refused for a value nothing
    /// could have captured.
    /// </summary>
    private static void ValidateNativeSource(
        ReplayManifest manifest, SourceProvenance source, IReadOnlyList<ActionRecord> actions,
        List<string> problems)
    {
        var maxActionOrdinal = actions.Count - 1;

        if (source.Video is not null || source.Synthetic is not null)
        {
            problems.Add("a native source cannot carry a video or synthetic-fixture block.");
        }

        if (source.RunStart is not null || source.RunSummary is not null)
        {
            problems.Add(
                "a native source cannot carry source.run_start or source.run_summary. Those read a public " +
                "video; a recorder watching the game reports what it witnessed, in source.native.");
        }

        if (source.ExtractionMethod != "captured")
        {
            problems.Add("a native source must use extraction_method 'captured'.");
        }

        if (source.Native is not { } native)
        {
            problems.Add(
                "source.native is absent, so nothing says which recorder produced this history, whether it saw " +
                "the run begin, or whether it watched the whole of it.");
            return;
        }

        if (string.IsNullOrWhiteSpace(native.RecorderVersion))
        {
            problems.Add(
                "source.native.recorder_version is empty. A defect found in one recorder build has to be " +
                "traceable to everything that build wrote.");
        }

        if (!NativeSource.Continuities.Contains(native.Continuity, StringComparer.Ordinal))
        {
            problems.Add(
                $"source.native.continuity '{native.Continuity}' is not one of: " +
                $"{string.Join(", ", NativeSource.Continuities)}.");
        }

        if (!NativeSource.Outcomes.Contains(native.Outcome, StringComparer.Ordinal))
        {
            problems.Add(
                $"source.native.outcome '{native.Outcome}' is not one of: " +
                $"{string.Join(", ", NativeSource.Outcomes)}. Giving up is 'abandoned' and is a completed " +
                "recording: the run is over and the fights in it were really played.");
        }

        RequireCapturedFact(
            native.WitnessedRunStart, "source.native.witnessed_run_start", "native", maxActionOrdinal, problems);

        if (!native.WitnessedRunStart.Value)
        {
            problems.Add(
                "source.native.witnessed_run_start is false. The recorder joined a run already in progress, so " +
                "the history it holds is not this run's from its start - and replaying it from run start " +
                "reconstructs a different run while every other gate passes.");
        }

        // Rewound is whole: every decision from run start was watched and the branch
        // a reload abandoned is discarded beside them, so the history replays. That it
        // may never be shared is the publication gate's refusal, not this one.
        if (!native.HistoryIsWhole)
        {
            problems.Add(
                $"source.native.continuity is '{native.Continuity}'. The recorder stopped and started again, so " +
                "it cannot know what happened in between, and a history with a hole in it is not this run's.");
        }

        if (native.IsRewound && native.Discarded?.Any(branch => branch.Reload) != true)
        {
            problems.Add(
                $"source.native.continuity is '{native.Continuity}' and no discarded branch is marked as the " +
                "reload's. A rewind keeps what it abandoned, so a recording that says it was rewound and holds " +
                "no such branch is not one the recorder wrote.");
        }

        if (!native.IsRewound && native.Discarded?.Any(branch => branch.Reload) == true)
        {
            problems.Add(
                $"source.native.continuity is '{native.Continuity}' and a discarded branch is marked as a " +
                "reload's. A reload rewinds the run behind what was recorded, and a recording holding one " +
                $"is '{NativeSource.RewoundContinuity}'.");
        }

        if (native.MigratedFromVersion is { } from &&
            !NativeSource.MigratableVersions.Contains(from))
        {
            problems.Add(
                $"source.native.migrated_from_version is {from.ToString(CultureInfo.InvariantCulture)}, which " +
                $"is not a format this build migrates from ({string.Join(", ", NativeSource.MigratableVersions)}). " +
                "The field says which older format a file was written in, and only a format this build " +
                "reads could have produced one.");
        }

        ValidateIntegrity(native, actions.Count, problems);
        ValidateBookmarks(native, manifest, maxActionOrdinal, problems);

        // The key beside the index, on every event option a recorder chose. Waived
        // only for a file that says it was written before the key existed: absent
        // there is the migration's doing, and absent anywhere else is a recorder that
        // did not read what it could have.
        if (!native.PredatesVersion(6))
        {
            foreach (var action in actions.Where(action =>
                         action.Verb is ActionVerb.ChooseEventOption or ActionVerb.ChooseNeowBlessing &&
                         !action.Args.ContainsKey("option_key")))
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) in a native recording names no option_key. A " +
                    "recorder reads the option's own key beside its position, which is what lets a build " +
                    "that reordered the options refuse rather than take whatever sits at that index.");
            }
        }
    }

    /// <summary>
    /// Every bookmark names a fight the recording finishes, once, in order, as a
    /// declared fact anchored at the fight's end.
    ///
    /// In the combat_start cross-check's own terms: a finished fight is exactly one
    /// with a combat_start boundary, and a bookmark on any other fight is a mark
    /// nothing could have pressed, because the control exists only once a fight has
    /// ended. The evidence's action is at or after the fight started - a mark on a
    /// fight cannot predate it - and never past the last action the history holds.
    /// </summary>
    private static void ValidateBookmarks(
        NativeSource native, ReplayManifest manifest, int maxActionOrdinal, List<string> problems)
    {
        if (native.Bookmarks is not { } bookmarks) return;

        if (bookmarks.Count == 0)
        {
            problems.Add("source.native.bookmarks is empty; a recording with no bookmarks leaves it absent.");
        }

        var previous = int.MinValue;
        foreach (var (bookmark, index) in bookmarks.Select((bookmark, index) => (bookmark, index)))
        {
            var where = $"source.native.bookmarks[{index.ToString(CultureInfo.InvariantCulture)}]";

            if (bookmark.Fight <= previous)
            {
                problems.Add(
                    $"{where} names fight {bookmark.Fight.ToString(CultureInfo.InvariantCulture)} after fight " +
                    $"{previous.ToString(CultureInfo.InvariantCulture)}. Bookmarks are one per fight, in fight " +
                    "order.");
            }

            previous = bookmark.Fight;

            if (!bookmark.Bookmarked.Value)
            {
                problems.Add(
                    $"{where} says the fight is not bookmarked. A mark taken off is not in the manifest at all; " +
                    "only the journal keeps the press that removed it.");
            }

            if (bookmark.Bookmarked.Source != FactSource.Declared)
            {
                problems.Add(
                    $"{where}.bookmarked is {bookmark.Bookmarked.Source.ToString().ToLowerInvariant()}, and a " +
                    "bookmark is declared: the player said so and the game was not asked.");
            }

            var start = manifest.BoundaryAt(ReplayBoundary.CombatStartKind, fight: bookmark.Fight);
            if (start is null)
            {
                problems.Add(
                    $"{where} names fight {bookmark.Fight.ToString(CultureInfo.InvariantCulture)}, and " +
                    "boundaries declares no combat_start for it. A bookmark is pressed once a fight has ended, " +
                    "and a fight the recording finishes has a combat_start, so this names a fight nobody could " +
                    "have marked.");
            }

            if (bookmark.Bookmarked.Evidence?.ActionOrdinal is not { } pressedAt)
            {
                problems.Add(
                    $"{where}.bookmarked carries no action_ordinal, so nothing says which moment of the run the " +
                    "player was looking at when they pressed it.");
                continue;
            }

            if (pressedAt > maxActionOrdinal)
            {
                problems.Add(
                    $"{where}.bookmarked was pressed after action {pressedAt.ToString(CultureInfo.InvariantCulture)} " +
                    $"and the history holds {(maxActionOrdinal + 1).ToString(CultureInfo.InvariantCulture)} " +
                    "action(s).");
            }
            else if (start is not null && pressedAt < start.AfterSeq)
            {
                problems.Add(
                    $"{where}.bookmarked was pressed after action {pressedAt.ToString(CultureInfo.InvariantCulture)} " +
                    $"and fight {bookmark.Fight.ToString(CultureInfo.InvariantCulture)} starts after action " +
                    $"{start.AfterSeq.ToString(CultureInfo.InvariantCulture)}. A fight cannot be bookmarked " +
                    "before it happened.");
            }
        }
    }

    private static void ValidateDiscardedBranches(ReplayManifest manifest, List<string> problems)
    {
        if (manifest.Source.Native?.Discarded is not { } branches) return;

        var actions = manifest.Actions;
        var verification = manifest.Verification is { Status: VerificationStatus.Verified, Trace: not null }
            ? manifest.Verification
            : null;

        for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            var branch = branches[branchIndex];
            var path = $"source.native.discarded[{branchIndex.ToString(CultureInfo.InvariantCulture)}]";
            // A reload may have rewound to the opening reading, before any decision;
            // the game's own rollback is always to a room entry the history holds.
            if (branch.RollbackToSeq < (branch.Reload ? -1 : 0) || branch.RollbackToSeq >= actions.Count)
            {
                problems.Add($"{path}.rollback_to_seq does not name a decision in the continued history.");
            }
            if (string.IsNullOrWhiteSpace(branch.RollbackToDigest))
            {
                problems.Add($"{path}.rollback_to_digest is empty.");
            }
            if (branch.Actions.Count == 0)
            {
                problems.Add($"{path}.actions is empty. A rollback without an observed decision discards nothing.");
                continue;
            }

            for (var index = 0; index < branch.Actions.Count; index++)
            {
                var action = branch.Actions[index];
                var expected = branch.RollbackToSeq + index + 1;
                if (action.Seq != expected)
                {
                    problems.Add(
                        $"{path}.actions[{index.ToString(CultureInfo.InvariantCulture)}] has seq={action.Seq}, " +
                        $"expected {expected.ToString(CultureInfo.InvariantCulture)}.");
                }
                if (action.Source != FactSource.Captured || action.Evidence?.ActionOrdinal != action.Seq)
                {
                    problems.Add(
                        $"{path}.actions[{index.ToString(CultureInfo.InvariantCulture)}] is not captured at its " +
                        "own action ordinal.");
                }
                ValidateActionArguments(action, problems);
            }

            var traceSteps = branch.Trace.Steps.OrderBy(step => step.Seq).ToList();
            var expectedActions = actions
                .Where(action => action.Seq == branch.RollbackToSeq)
                .Concat(branch.Actions)
                .ToList();
            // The opening reading is a step of the trace and never an action, so a
            // reload's branch from it is held to the same shape less that step.
            var fromOpening = branch.Reload && branch.RollbackToSeq == -1;
            var compared = fromOpening
                ? traceSteps.SkipWhile(step => step.Seq == -1 && step.Verb == RunCapture.RunStartVerb).ToList()
                : traceSteps;
            if (compared.Count != expectedActions.Count ||
                compared.Where((step, index) => !Matches(step, expectedActions[index])).Any() ||
                (fromOpening && compared.Count == traceSteps.Count))
            {
                problems.Add($"{path}.trace does not describe its rollback boundary and discarded actions.");
            }

            // What the game's own rollback returns to is the room entry of a live
            // fight, and the branch shows that fight beginning there. A reload returns
            // to whatever decision the save held, and the branch is what was played
            // after it; nothing about a fight is asked of it.
            if (branch.Reload) continue;

            var branchCoverage = RunCoverage.Of(branch.Trace);
            var floor = branchCoverage.Floors.FirstOrDefault(entry =>
                entry.EnteredAfterSeq == branch.RollbackToSeq);
            var fight = floor is null ? null : branchCoverage.FightsOn(floor).FirstOrDefault();
            if (fight is null)
            {
                problems.Add($"{path}.trace does not show a fight beginning on the rollback floor.");
            }

            if (verification is null) continue;

            var boundary = floor is null
                ? null
                : verification.Boundaries.FirstOrDefault(candidate =>
                    candidate.Kind == ReplayBoundary.FloorEntryKind && candidate.Floor == floor.Floor &&
                    candidate.AfterSeq == branch.RollbackToSeq);
            if (boundary is null || boundary.Digest.Source != FactSource.Engine ||
                !string.Equals(boundary.Digest.Value, branch.RollbackToDigest, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{path} does not identify the verified room-entry state it shares with the continued history.");
            }
        }
    }

    private static bool Matches(ReplayStep step, ActionRecord action) =>
        step.Seq == action.Seq &&
        string.Equals(step.Verb, action.Verb.ToString(), StringComparison.Ordinal) &&
        step.Args.Count == action.Args.Count &&
        step.Args.All(arg => action.Args.TryGetValue(arg.Key, out var value) &&
                            string.Equals(arg.Value, value, StringComparison.Ordinal));

    /// <summary>
    /// What <c>source.native.integrity</c> may say, what has to travel with it, and the
    /// publication refusal it decides.
    ///
    /// The unmapped entries are required exactly when the integrity says the recorder
    /// stopped, and refused otherwise: a stop with nothing named is a hole with no
    /// account of itself, and a named stop on a recording claiming to be complete is
    /// two claims about one run. Each entry stands where the history ends, because
    /// that is what a stop is.
    /// </summary>
    private static void ValidateIntegrity(NativeSource native, int actionCount, List<string> problems)
    {
        if (!NativeSource.Integrities.Contains(native.Integrity, StringComparer.Ordinal))
        {
            problems.Add(
                $"source.native.integrity '{native.Integrity}' is not one of: " +
                $"{string.Join(", ", NativeSource.Integrities)}.");
            return;
        }

        var stopped = string.Equals(native.Integrity, NativeSource.UnmappedIntegrity, StringComparison.Ordinal);
        var unmapped = native.Unmapped ?? [];

        if (stopped && unmapped.Count == 0)
        {
            problems.Add(
                "source.native.integrity is 'unmapped' and source.native.unmapped names nothing. A recorder " +
                "that stopped says what it stopped at, or the hole in this history has no account of itself.");
        }

        if (!stopped && unmapped.Count > 0)
        {
            problems.Add(
                $"source.native.unmapped names {unmapped.Count.ToString(CultureInfo.InvariantCulture)} " +
                $"decision(s) and source.native.integrity is '{native.Integrity}'. A named stop on a recording " +
                "that says it did not stop is two claims about one run.");
        }

        for (var index = 0; index < unmapped.Count; index++)
        {
            var entry = unmapped[index];
            var path = $"source.native.unmapped[{index.ToString(CultureInfo.InvariantCulture)}]";

            if (entry.Seq != actionCount)
            {
                problems.Add(
                    $"{path} would have been decision {entry.Seq.ToString(CultureInfo.InvariantCulture)} and " +
                    $"the history holds {actionCount.ToString(CultureInfo.InvariantCulture)}. A recording ends " +
                    "where its recorder stopped, so the decision it stopped at is the one after its last.");
            }

            if (!UnmappedDecision.Seams.Contains(entry.Seam, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{path} names seam '{entry.Seam}', which is not one of: " +
                    $"{string.Join(", ", UnmappedDecision.Seams)}.");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                problems.Add($"{path} names nothing, so a later build could not say what the recorder met.");
            }

            if (entry.Evidence.ActionOrdinal != entry.Seq)
            {
                problems.Add(
                    $"{path} carries evidence at action ordinal " +
                    $"{entry.Evidence.ActionOrdinal?.ToString(CultureInfo.InvariantCulture) ?? "none"}, and " +
                    "the decision it describes is the one at its own seq.");
            }
        }

        if (native.StatesSomethingOtherThanComplete)
        {
            var met = unmapped.Count == 0
                ? string.Empty
                : " The recorder met: " + string.Join("; ", unmapped.Select(Describe)) + ".";
            problems.Add(
                $"source.native.integrity is '{native.Integrity}'. " +
                (stopped
                    ? "The recorder stopped at a decision it could not name, so the history ends before the run " +
                      "did and replaying it reconstructs a run that never made that decision."
                    : "This run was not played entirely by the game's own rules, so what changed the state is " +
                      "not among the decisions this history holds and replaying them reconstructs a different " +
                      "run.") +
                " The recording is kept and is not publishable." + met);
        }
    }

    /// <summary>One unmapped decision, as a refusal names it.</summary>
    public static string Describe(UnmappedDecision entry) =>
        $"{entry.Seam} {entry.Name}" +
        (entry.Discriminator is { } discriminator ? $" ({discriminator})" : string.Empty) +
        (entry.Args.Count == 0
            ? string.Empty
            : " with " + string.Join(", ", entry.Args.Select(arg => $"{arg.Key}={arg.Value}")));

    /// <summary>
    /// The places a player can be stood in this recording.
    ///
    /// The kinds are a closed set because a host dispatches on them: a kind nothing
    /// knows how to reach would be a place the recording says a player can go and
    /// nothing can take them. Every digest is engine-produced or captured live, since
    /// no video shows draw order or a random stream's position, which is the whole
    /// reason a boundary carries a digest at all.
    ///
    /// Where a manifest carries a verified whole-run trace, every fight the trace
    /// holds and finishes must have a boundary, and every combat_start it declares
    /// must name one of those fights: a run whose third fight has nowhere to be
    /// entered from is a recording that silently offers less than it holds, and a
    /// boundary on a fight the recording stops in the middle of is a place nobody
    /// could enter. There is no completed recorded line to compare against there, and
    /// <see cref="RecordedFights.From"/> cuts only finished fights, so both directions
    /// of the rule are asked of the same set.
    ///
    /// Every declared floor_entry and turn_start is cross-checked against that same
    /// trace, for the same reason and against the same reading: a floor_entry must
    /// name a floor the trace arrives on, at the action it arrives after, and a
    /// turn_start must name a turn the trace's own fight takes, at the action that
    /// started it. Without this the coordinate is checked for shape only, and a
    /// manifest naming a floor the run never stood on passes publication and is
    /// refused later, in front of a player, as an aborted entry. It matters most for a
    /// manifest this project did not derive - a recorder's, or a community submission
    /// - and it matters more now a floor arrival can be restored from a cache rather
    /// than walked. The rule is read off <see cref="RunCoverage"/>, which is what the
    /// derive path builds its boundaries from, so the guard and the deriver cannot
    /// disagree about the same history.
    ///
    /// Only the declared direction is asked of these two. Unlike a fight, a floor or a
    /// turn with no boundary offers nothing a recording owes anybody: a host enters
    /// fights, and the other two are places it may also stand somebody, not places it
    /// must be able to.
    ///
    /// A floor entry is asked more than that, and asked it with no trace, because
    /// standing somebody on a floor needs more than a coordinate the run reached:
    /// <see cref="ValidateFloorEntry"/> refuses here everything
    /// <see cref="FloorEntryPlan.For"/> would abort on in front of a player.
    ///
    /// A generated fixture may declare boundaries and is not required to. The rule
    /// that it may not was written when a boundary meant a publication claim; it does
    /// not - what may be published is the gate's question, and the gate refuses a
    /// synthetic source outright. What a boundary means is where a host can start,
    /// which is exactly what a fixture with no video behind it is for testing. A
    /// fixture is not required to carry one because most of them reach no fight worth
    /// standing anybody in.
    /// </summary>
    private static void ValidateBoundaries(ReplayManifest manifest, List<string> problems)
    {
        var boundaries = manifest.Boundaries;
        var maxSeq = manifest.Actions.Count - 1;
        var isFixture = manifest.Source.Kind == "synthetic-engine";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boundary in boundaries)
        {
            var name = $"boundaries[{boundary.Kind}]";

            if (!ReplayBoundary.Kinds.Contains(boundary.Kind, StringComparer.Ordinal))
            {
                problems.Add(
                    $"boundaries entry has kind '{boundary.Kind}', which is not one of: " +
                    $"{string.Join(", ", ReplayBoundary.Kinds)}. The kinds are a closed set because a host " +
                    "dispatches on them, so an unrecognised one is a place nothing could take a player.");
                continue;
            }

            name = boundary.Describe();

            if (!seen.Add($"{boundary.Kind}|{boundary.Fight}|{boundary.Floor}|{boundary.Turn}"))
            {
                problems.Add($"boundaries names {name} more than once.");
            }

            if (boundary.AfterSeq < -1 || boundary.AfterSeq > maxSeq)
            {
                problems.Add(
                    $"the boundary at {name} has after_seq={boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)}, " +
                    $"outside the action range [-1, {maxSeq.ToString(CultureInfo.InvariantCulture)}].");
            }

            switch (boundary.Kind)
            {
                case ReplayBoundary.CombatStartKind:
                    if (boundary.Fight is not > 0)
                    {
                        problems.Add("a combat_start boundary must name which fight of the run it starts, from 1.");
                    }
                    if (boundary.Floor is not null || boundary.Turn is not null)
                    {
                        problems.Add($"the boundary at {name} names a floor or a turn, which a combat start is not.");
                    }
                    break;
                case ReplayBoundary.FloorEntryKind:
                    if (boundary.Floor is not > 0)
                    {
                        problems.Add("a floor_entry boundary must name which floor of the run it arrives on, from 1.");
                    }
                    if (boundary.Fight is not null || boundary.Turn is not null)
                    {
                        problems.Add($"the boundary at {name} names a fight or a turn, which a floor entry is not.");
                    }
                    if (boundary.Floor is > 0) ValidateFloorEntry(manifest, boundary, isFixture, problems);
                    break;
                case ReplayBoundary.TurnStartKind:
                    if (boundary.Fight is not > 0 || boundary.Turn is not > 0)
                    {
                        problems.Add(
                            "a turn_start boundary must name both the fight it is in and the turn it starts, " +
                            "each from 1.");
                    }
                    if (boundary.Floor is not null)
                    {
                        problems.Add($"the boundary at {name} names a floor, which a turn start is not.");
                    }
                    break;
            }

            if (boundary.Digest.Source == FactSource.Captured)
            {
                RequireCapturedFact(
                    boundary.Digest, $"the digest at {name}", manifest.Source.Kind, maxSeq, problems,
                    boundary.AfterSeq);
            }
            else if (boundary.Digest.Source == FactSource.Engine)
            {
                if (boundary.Digest.Evidence is not null)
                {
                    problems.Add(
                        $"the engine-produced digest at {name} must carry no evidence. It is what replaying the " +
                        "history yielded, not a reading taken at a video timestamp or in a live session; " +
                        "evidence attached to it would describe a reading nobody took.");
                }
            }
            else
            {
                problems.Add(
                    $"the digest at {name} is source={boundary.Digest.Source.ToString().ToLowerInvariant()}. A " +
                    "boundary digest covers draw order and every random stream's position, which no video " +
                    "shows and no reasoning reaches: it is produced by the engine or captured from the live " +
                    "game, or it is not established at all.");
            }

            if (!SnapshotDigestPattern.IsMatch(boundary.Digest.Value))
            {
                problems.Add($"the digest at {name} must be a lowercase sha256 digest.");
            }
        }

        if (!isFixture && !boundaries.Any(boundary => boundary.IsCombatStart))
        {
            problems.Add(
                "boundaries names no combat_start. A recording must carry the boundary a retail host compares " +
                "hidden state against, or nobody can be stood in its fight without trusting a machine-local " +
                "snapshot cache.");
        }

        if (manifest.Verification is not { Status: VerificationStatus.Verified, Trace: { } trace }) return;

        // One reading of the history for all three kinds. The derive path builds its
        // boundaries from this same coverage, so a boundary this refuses is one that
        // path could not have produced.
        var coverage = RunCoverage.Of(trace);
        var coveredFights = coverage.Fights;
        foreach (var boundary in boundaries.Where(boundary => boundary.IsCombatStart))
        {
            var fight = coveredFights.FirstOrDefault(fight => fight.Fight == boundary.Fight);
            if (fight is null)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, but this history's verified trace holds no " +
                    "fight with that ordinal. A combat_start cannot name a fight the recording does not contain.");
            }
            else if (!fight.Finished)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, and this history's verified trace never " +
                    "finishes that fight. A boundary is a place a player can be stood and have their line " +
                    "compared against the recording's completed one, and a fight the recording stops in the " +
                    "middle of has no such line, so declaring it names a place nobody could enter.");
            }
            else if (boundary.AfterSeq != fight.CombatStartSeq)
            {
                problems.Add(
                    $"this history's verified trace starts fight " +
                    $"{fight.Fight.ToString(CultureInfo.InvariantCulture)} after action " +
                    $"{fight.CombatStartSeq.ToString(CultureInfo.InvariantCulture)}, but its combat_start " +
                    $"boundary names action {boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)}. A " +
                    "fight ordinal cannot point to another fight's boundary.");
            }
        }

        foreach (var fight in coveredFights.Where(fight => fight.Finished))
        {
            if (manifest.BoundaryAt(ReplayBoundary.CombatStartKind, fight: fight.Fight) is null)
            {
                problems.Add(
                    $"this history's verified trace holds fight " +
                    $"{fight.Fight.ToString(CultureInfo.InvariantCulture)} and boundaries declares no " +
                    "combat_start for it, so a fight the recording really contains has nowhere to be entered " +
                    "from. Derive it by replaying the run.");
            }
        }

        // A declared coordinate that names nothing in the trace is only ever caught
        // here. The shape rules above read the declaration alone, so they pass a floor
        // this run never stood on and a turn this fight never took.
        foreach (var boundary in boundaries.Where(boundary =>
                     boundary.Kind == ReplayBoundary.FloorEntryKind && boundary.Floor is > 0))
        {
            var floor = coverage.Floors.FirstOrDefault(floor => floor.Floor == boundary.Floor);
            if (floor is null)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, but this history's verified trace never " +
                    "reaches that floor. A floor_entry cannot name an arrival the recording does not contain.");
            }
            else if (floor.EnteredAfterSeq < 0)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, and this history's verified trace starts on " +
                    "that floor rather than arriving on it. A floor entry is the map move that entered the " +
                    "floor, so the floor a run opens on names a place no plan could reach.");
            }
            else if (boundary.AfterSeq != floor.EnteredAfterSeq)
            {
                problems.Add(
                    $"this history's verified trace enters floor " +
                    $"{floor.Floor.ToString(CultureInfo.InvariantCulture)} after action " +
                    $"{floor.EnteredAfterSeq.ToString(CultureInfo.InvariantCulture)}, but its floor_entry " +
                    $"boundary names action {boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)}. A " +
                    "floor cannot point to another arrival's boundary.");
            }
        }

        foreach (var boundary in boundaries.Where(boundary =>
                     boundary.Kind == ReplayBoundary.TurnStartKind &&
                     boundary.Fight is > 0 && boundary.Turn is > 0))
        {
            var fight = coveredFights.FirstOrDefault(fight => fight.Fight == boundary.Fight);
            if (fight is null)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, but this history's verified trace holds no " +
                    "fight with that ordinal. A turn_start cannot name a turn of a fight the recording does " +
                    "not contain.");
                continue;
            }

            if (!fight.Finished)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, and this history's verified trace never " +
                    "finishes that fight. A boundary is a place a player can be stood and have their line " +
                    "compared against the recording's completed one, and a fight the recording stops in the " +
                    "middle of has no such line, so declaring a turn of it names a place nobody could enter.");
                continue;
            }

            var turn = fight.Turns.FirstOrDefault(turn => turn.Turn == boundary.Turn);
            if (turn is null)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, and this history's verified trace never " +
                    $"reaches turn {boundary.Turn.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)} " +
                    $"of fight {fight.Fight.ToString(CultureInfo.InvariantCulture)}. A turn_start cannot name " +
                    "a turn the recorded fight does not take.");
            }
            else if (boundary.AfterSeq != turn.StartedAfterSeq)
            {
                problems.Add(
                    $"this history's verified trace starts turn " +
                    $"{turn.Turn.ToString(CultureInfo.InvariantCulture)} of fight " +
                    $"{fight.Fight.ToString(CultureInfo.InvariantCulture)} after action " +
                    $"{turn.StartedAfterSeq.ToString(CultureInfo.InvariantCulture)}, but its turn_start " +
                    $"boundary names action {boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)}. A " +
                    "turn number cannot point to another turn's boundary.");
            }
        }
    }

    /// <summary>
    /// A video source must show that its run started at the beginning.
    ///
    /// This is the one check that defends against a resumed run. Everything else in
    /// this validator can be satisfied by a recording of a run picked up half way
    /// through, because a resumed run carries the same seed, build, hash and acts.
    /// </summary>
    private static void ValidateRunStart(SourceProvenance source, int videoDurationMs, List<string> problems)
    {
        if (source.Kind != "vod") return;

        if (source.RunStart is not { } start)
        {
            problems.Add(
                "source.run_start is absent. A video source must show that the recording begins at the run's " +
                "beginning: a run resumed from run history matches on seed, build, content hash and acts, so " +
                "nothing else here would notice.");
            return;
        }

        RequireObservedVideoFact(start.FirstObservedRunTimeSeconds, "source.run_start.first_observed_run_time_s", videoDurationMs, problems);
        RequireObservedVideoFact(start.FirstObservedFloor, "source.run_start.first_observed_floor", videoDurationMs, problems);
        RequireObservedVideoFact(start.EnteredFromRunHistory, "source.run_start.entered_from_run_history", videoDurationMs, problems);
        RequireObservedVideoFact(start.ResumeModalSeen, "source.run_start.resume_modal_seen", videoDurationMs, problems);

        if (start.EnteredFromRunHistory.Value)
        {
            problems.Add(
                "source.run_start says the run was entered from run history. That is a resumed run, not a run " +
                "from its start, and an ordered history replayed from run start would reconstruct a different run.");
        }

        if (start.ResumeModalSeen.Value)
        {
            problems.Add(
                "source.run_start says a resume dialog appears in the recording. The run was picked up rather " +
                "than started.");
        }

        if (start.FirstObservedFloor.Value != 1)
        {
            problems.Add(
                $"source.run_start observes floor {start.FirstObservedFloor.Value} first. A run recorded from " +
                "its start is on floor 1 when it first becomes visible.");
        }

        if (start.FirstObservedRunTimeSeconds.Value is var seconds &&
            (seconds < 0 || seconds > RunStartEvidence.MaxRunTimeSecondsAtStart))
        {
            problems.Add(
                $"source.run_start observes the run timer at {seconds}s, outside the " +
                $"0-{RunStartEvidence.MaxRunTimeSecondsAtStart}s a from-start recording shows. The game's run " +
                "timer starts at zero, so a larger reading is time the recording did not capture.");
        }
    }

    /// <summary>
    /// The end-of-run summary is a second reading of the environment from the far end
    /// of the recording. Requiring the two to agree catches a drifted reading and a
    /// recording spliced from two different runs - neither of which any single
    /// reading can catch on its own.
    /// </summary>
    private static void ValidateRunSummary(ReplayManifest manifest, int videoDurationMs, List<string> problems)
    {
        if (manifest.Source.Kind != "vod") return;

        if (manifest.Source.RunSummary is not { } summary)
        {
            problems.Add(
                "source.run_summary is absent. The end-of-run screen re-states the environment thousands of " +
                "seconds after the first reading, and two readings that agree across that gap are much harder " +
                "to get wrong than one.");
            return;
        }

        ValidateVideoTimestamp(summary.VideoTimeMs, "source.run_summary.video_t_ms", videoDurationMs, problems);
        RequireObservedVideoFact(summary.Seed, "source.run_summary.seed", videoDurationMs, problems);
        RequireObservedVideoFact(summary.BuildVersion, "source.run_summary.build_version", videoDurationMs, problems);
        RequireObservedVideoFact(summary.BuildDateUtc, "source.run_summary.build_date_utc", videoDurationMs, problems);
        RequireObservedVideoFact(summary.ContentHash, "source.run_summary.content_hash", videoDurationMs, problems);
        RequireObservedVideoFact(summary.Ascension, "source.run_summary.ascension", videoDurationMs, problems);
        RequireObservedVideoFact(summary.FloorsClimbed, "source.run_summary.floors_climbed", videoDurationMs, problems);
        RequireObservedVideoFact(summary.PlayerMaxHp, "source.run_summary.player_max_hp", videoDurationMs, problems);
        RequireObservedVideoFact(summary.DeckSize, "source.run_summary.deck_size", videoDurationMs, problems);
        RequireObservedVideoFact(summary.RelicCount, "source.run_summary.relic_count", videoDurationMs, problems);

        foreach (var (name, factTimestamp) in SummaryFactTimestamps(summary))
        {
            if (factTimestamp != summary.VideoTimeMs)
            {
                problems.Add(
                    $"source.run_summary.{name} timestamp {factTimestamp}ms does not match the summary " +
                    $"checkpoint timestamp {summary.VideoTimeMs}ms.");
            }
        }

        var latestEarlierTimestamp = EarlierVideoTimestamps(manifest).DefaultIfEmpty(-1).Max();
        if (summary.VideoTimeMs <= latestEarlierTimestamp)
        {
            problems.Add(
                $"source.run_summary at {summary.VideoTimeMs}ms must occur after every opening observation " +
                $"and action; the latest earlier evidence is at {latestEarlierTimestamp}ms.");
        }

        var env = manifest.Environment;
        foreach (var (field, atStart, atEnd) in new[]
                 {
                     ("seed", env.Seed.Value, summary.Seed.Value),
                     ("build_version", env.BuildVersion.Value, summary.BuildVersion.Value),
                     ("build_date_utc", env.BuildDateUtc.Value, summary.BuildDateUtc.Value),
                     ("content_hash", env.ContentHash.Value, summary.ContentHash.Value),
                 })
        {
            if (!string.Equals(atStart, atEnd, StringComparison.Ordinal))
            {
                problems.Add(
                    $"source.run_summary reads {field} as '{atEnd}' where environment.{field} is '{atStart}'. " +
                    "The two ends of the recording disagree, so at least one reading is wrong or the recording " +
                    "covers more than one run.");
            }
        }

        if (summary.Ascension.Value != env.Ascension.Value)
        {
            problems.Add(
                $"source.run_summary reads ascension {summary.Ascension.Value} where environment.ascension is " +
                $"{env.Ascension.Value}.");
        }

        if (summary.NotShown.Count == 0)
        {
            problems.Add(
                "source.run_summary.not_shown is empty. This screen does not display everything - the game mode " +
                "is not on it - and an unstated absence reads as a value that was checked.");
        }
    }

    private static void ValidateActions(
        IReadOnlyList<ActionRecord> actions, string sourceKind, int videoDurationMs, List<string> problems)
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
            ValidateActionArguments(action, problems);

            if (sourceKind == "synthetic-engine")
            {
                if (action.Source != FactSource.Declared || action.Evidence is not null)
                {
                    problems.Add(
                        $"actions[{action.Seq}] ({action.Verb}) in a synthetic fixture must be declared " +
                        "and carry no video evidence.");
                }
                continue;
            }

            if (sourceKind == "native")
            {
                var path = $"actions[{action.Seq}] ({action.Verb})";
                if (action.Source != FactSource.Captured)
                {
                    problems.Add(
                        $"{path} must be source=captured for a native recording: a recorder watched the " +
                        "decision being made rather than reading it off a video.");
                }
                else
                {
                    ValidateCapturedEvidence(
                        action.Evidence, path, sourceKind, actions.Count - 1, problems, action.Seq);
                }
                continue;
            }

            if (action.Source != FactSource.Observed)
            {
                problems.Add($"actions[{action.Seq}] ({action.Verb}) must be source=observed for a VOD replay.");
                continue;
            }

            if (action.Evidence?.VideoTimeMs is not { } timestamp)
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) claims to be observed but carries no video timestamp, " +
                    "so the claim cannot be re-checked against the source.");
            }
            else
            {
                ValidateVideoTimestamp(timestamp, $"actions[{action.Seq}] ({action.Verb})", videoDurationMs, problems);
            }
        }
    }

    /// <summary>
    /// What a shop purchase has to name, which depends on what it bought.
    ///
    /// Four of the merchant's five kinds buy a thing that sits at a position on a
    /// shelf, and both the position and the thing's id are recorded - the position is
    /// what the video shows somebody click and the id is what makes a differently
    /// stocked shop fail loudly. The fifth buys a card removal, which has no shelf and
    /// no id; the card it removes is a separate selection off the screen it opens.
    /// </summary>
    private static void ValidateShopPurchase(ActionRecord action, List<string> problems)
    {
        if (!action.Args.TryGetValue("kind", out var kind)) return;

        if (!ShopPurchaseKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) argument 'kind' is '{kind}'. Known kinds: " +
                $"{string.Join(", ", ShopPurchaseKinds.All)}.");
            return;
        }

        var idArgument = ShopPurchaseKinds.IdArgument(kind);
        var expected = idArgument is null ? Array.Empty<string>() : [idArgument, "option_index"];

        foreach (var name in expected.Where(name => !action.Args.ContainsKey(name)))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) buys a '{kind}' and is missing required argument " +
                $"'{name}'.");
        }

        foreach (var name in new[] { "card_id", "relic_id", "potion_id", "option_index" }
                     .Where(name => !expected.Contains(name, StringComparer.Ordinal))
                     .Where(action.Args.ContainsKey))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) buys a '{kind}' and carries '{name}', which that " +
                "kind of purchase does not have.");
        }
    }

    /// <summary>
    /// What a claimed reward has to name, which depends on what it claimed.
    ///
    /// Two of the five kinds claim a thing a build could have changed - a relic, a
    /// fixed card - and the id is what makes a loot screen stocked differently fail
    /// loudly rather than hand over whatever sits there. The other three claim gold,
    /// a potion or a card removal and have nothing further to name; the card that
    /// comes off a removal's screen is a separate selection, as it is at a merchant.
    /// </summary>
    private static void ValidateClaimReward(ActionRecord action, List<string> problems)
    {
        if (!action.Args.TryGetValue("reward_type", out var kind)) return;
        if (!RewardKinds.All.Contains(kind, StringComparer.Ordinal)) return;

        var idArgument = RewardKinds.IdArgument(kind);
        var expected = idArgument is null ? Array.Empty<string>() : [idArgument];

        foreach (var name in expected.Where(name => !action.Args.ContainsKey(name)))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) claims a '{kind}' reward and is missing required " +
                $"argument '{name}'.");
        }

        foreach (var name in new[] { "relic_id", "card_id" }
                     .Where(name => !expected.Contains(name, StringComparer.Ordinal))
                     .Where(action.Args.ContainsKey))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) claims a '{kind}' reward and carries '{name}', which " +
                "that kind of reward does not have.");
        }
    }

    private static void ValidateActionArguments(ActionRecord action, List<string> problems)
    {
        if (action.Args is null)
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) has null args.");
            return;
        }

        string[] required;
        string[] allowed;
        string[] nonNegativeIntegers;

        switch (action.Verb)
        {
            case ActionVerb.ChooseNeowBlessing:
                // The key beside the index is required of a native recording and
                // allowed of any other; ValidateNativeSource holds that rule, because
                // it relates a source to an argument.
                required = ["option_index"];
                allowed = [.. required, "option_key"];
                nonNegativeIntegers = ["option_index"];
                break;
            case ActionVerb.MapMove:
                required = ["act", "row", "column"];
                allowed = [.. required, Corruption.AlternativeColumn];
                nonNegativeIntegers = [.. required, Corruption.AlternativeColumn];
                break;
            case ActionVerb.PlayCard:
                required = ["card_id", "hand_index"];
                allowed =
                [
                    .. required,
                    "target_index",
                    Corruption.SubstituteCardId,
                    Corruption.SubstituteHandIndex,
                ];
                nonNegativeIntegers =
                ["hand_index", "target_index", Corruption.SubstituteHandIndex];
                break;
            case ActionVerb.EndTurn:
                required = [];
                allowed = [];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.UndoEndTurn:
                // The turn taken back before the enemy turn began. Valid only
                // immediately after an EndTurn of the same turn, which the driver
                // checks; nothing about it is an argument.
                required = [];
                allowed = [];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.ChooseEventOption:
                // The event id is required and the opening blessing's is not: which
                // event a floor generates is a consequence of the whole history before
                // it, and an option index means nothing without the event it indexes.
                // The key beside the index is required of a native recording and
                // allowed of any other; ValidateNativeSource holds that rule.
                required = ["event_id", "option_index"];
                allowed = [.. required, "option_key"];
                nonNegativeIntegers = ["option_index"];
                break;
            case ActionVerb.ClaimReward:
                // The id is required for the two kinds that claim a thing a build could
                // have changed and refused for the rest, which is checked below where
                // the kind is known.
                required = ["reward_type"];
                allowed = ["reward_type", "relic_id", "card_id"];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.TakeCard:
                required = ["card_id", "option_index"];
                allowed = [.. required, Corruption.AlternativeCardId, Corruption.AlternativeOptionIndex];
                nonNegativeIntegers = ["option_index", Corruption.AlternativeOptionIndex];
                break;
            case ActionVerb.TakeCardRewardAlternative:
                // The same question a card reward asks, answered past the cards. The id
                // names which alternative, because a build can reorder them; the index
                // is the one the screen reports, the count of cards offered plus the
                // alternative's own position.
                required = ["option_id", "option_index"];
                allowed = required;
                nonNegativeIntegers = ["option_index"];
                break;
            case ActionVerb.SkipRewards:
                required = [];
                allowed = [];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.SelectCardFromScreen:
                required = ["card_id", "option_index"];
                allowed = [.. required, Corruption.AlternativeOptionIndex];
                nonNegativeIntegers = ["option_index", Corruption.AlternativeOptionIndex];
                break;
            case ActionVerb.SelectBundleFromScreen:
                // A bundle has no id of its own, so its identity is its cards' ids
                // joined with a comma in the order the prompt listed them, beside the
                // position - the same rule every other pick follows.
                required = ["card_ids", "option_index"];
                allowed = [.. required, Corruption.AlternativeOptionIndex];
                nonNegativeIntegers = ["option_index", Corruption.AlternativeOptionIndex];
                break;
            case ActionVerb.SelectRelicFromScreen:
                required = ["relic_id", "option_index"];
                allowed = [.. required, Corruption.AlternativeOptionIndex];
                nonNegativeIntegers = ["option_index", Corruption.AlternativeOptionIndex];
                break;
            case ActionVerb.UsePotion:
                required = ["potion_id", "slot_index"];
                allowed = [.. required, "target_index"];
                nonNegativeIntegers = ["slot_index", "target_index"];
                break;
            case ActionVerb.DiscardPotion:
                required = ["potion_id", "slot_index"];
                allowed = required;
                nonNegativeIntegers = ["slot_index"];
                break;
            case ActionVerb.ShopPurchase:
                // The id and the index are required for four of the five kinds and
                // refused for the fifth, which is checked below where the kind is
                // known: a card removal buys a service and has nothing to name.
                required = ["kind"];
                allowed = ["kind", "option_index", "card_id", "relic_id", "potion_id"];
                nonNegativeIntegers = ["option_index"];
                break;
            case ActionVerb.ProceedToNextAct:
                required = [];
                allowed = [];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.RevealCrystalSphereCell:
                // The tool is set on the minigame before the cell is clicked and decides
                // how many cells the click reveals, so a reveal recorded without it
                // replays as a different reveal.
                required = ["tool", "x", "y"];
                allowed = required;
                nonNegativeIntegers = ["x", "y"];
                break;
            case ActionVerb.TakeChestRelic:
                required = ["relic_id", "option_index"];
                allowed = required;
                nonNegativeIntegers = ["option_index"];
                break;
            case ActionVerb.SkipChestRelic:
                required = [];
                allowed = [];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.ChooseRestSiteOption:
                // Named as well as positioned, for the reason a played card is: which
                // options a rest site offers is a consequence of the run that reached
                // it, so an index alone names nothing that can be checked.
                required = ["option_id", "option_index"];
                allowed = required;
                nonNegativeIntegers = ["option_index"];
                break;
            default:
                problems.Add(
                    $"actions[{action.Seq}] uses verb '{action.Verb}', which this manifest version does not implement.");
                return;
        }

        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var name in required.Where(name => !action.Args.ContainsKey(name)))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) is missing required argument '{name}'.");
        }

        foreach (var name in action.Args.Keys.Where(name => !allowedSet.Contains(name)))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) has unknown argument '{name}'.");
        }

        foreach (var name in nonNegativeIntegers)
        {
            if (!action.Args.TryGetValue(name, out var value)) continue;
            if (!NonNegativeIntegerPattern.IsMatch(value))
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) argument '{name}' must be a canonical non-negative integer.");
            }
            else if (!int.TryParse(
                         value, System.Globalization.NumberStyles.None,
                         System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) argument '{name}' exceeds the Int32 range.");
            }
        }

        if (action.Args.TryGetValue("card_id", out var cardId) && string.IsNullOrWhiteSpace(cardId))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'card_id' is empty.");
        }

        if (action.Args.TryGetValue("event_id", out var eventId) && string.IsNullOrWhiteSpace(eventId))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'event_id' is empty.");
        }

        if (action.Args.TryGetValue("option_id", out var optionId) && string.IsNullOrWhiteSpace(optionId))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'option_id' is empty.");
        }

        if (action.Args.TryGetValue("relic_id", out var relicId) && string.IsNullOrWhiteSpace(relicId))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'relic_id' is empty.");
        }

        if (action.Args.TryGetValue("potion_id", out var potionId) && string.IsNullOrWhiteSpace(potionId))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'potion_id' is empty.");
        }

        if (action.Args.TryGetValue("option_key", out var optionKey) && string.IsNullOrWhiteSpace(optionKey))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'option_key' is empty.");
        }

        if (action.Args.TryGetValue("card_ids", out var cardIds) && string.IsNullOrWhiteSpace(cardIds))
        {
            problems.Add($"actions[{action.Seq}] ({action.Verb}) argument 'card_ids' is empty.");
        }

        // A reward kind the driver cannot name is refused at ingestion rather than at
        // replay, because a manifest that says 'coins' would otherwise look valid right
        // up until an engine is spent on it.
        if (action.Args.TryGetValue("reward_type", out var rewardType) &&
            !RewardKinds.All.Contains(rewardType, StringComparer.Ordinal))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) argument 'reward_type' is '{rewardType}'. Known " +
                $"kinds: {string.Join(", ", RewardKinds.All)}. A card reward opens a second screen and " +
                "is taken with TakeCard, which records which card came back.");
        }

        // The tool decides what a click reveals, so a name the minigame has no tool
        // for is refused at ingestion rather than replayed as whichever tool the
        // minigame happened to hold.
        if (action.Args.TryGetValue("tool", out var tool) &&
            !CrystalSphereTools.All.Contains(tool, StringComparer.Ordinal))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) argument 'tool' is '{tool}'. Known tools: " +
                $"{string.Join(", ", CrystalSphereTools.All)}.");
        }

        if (action.Verb == ActionVerb.ShopPurchase) ValidateShopPurchase(action, problems);
        if (action.Verb == ActionVerb.ClaimReward) ValidateClaimReward(action, problems);

        // An alternative a control is meant to take has to differ from what was taken,
        // or the control corrupts nothing and an arbiter that accepted it would be
        // reported as having failed to reject a corruption nobody made.
        foreach (var (nominated, actual) in new[]
                 {
                     (Corruption.AlternativeCardId, "card_id"),
                     (Corruption.AlternativeOptionIndex, "option_index"),
                     (Corruption.AlternativeColumn, "column"),
                 })
        {
            if (action.Args.TryGetValue(nominated, out var alternative) &&
                action.Args.TryGetValue(actual, out var taken) &&
                string.Equals(alternative, taken, StringComparison.Ordinal))
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) nominates '{nominated}' = '{alternative}', which is " +
                    $"what '{actual}' already says. A negative control pointed at the decision that was made " +
                    "corrupts nothing.");
            }
        }

        if (action.Verb == ActionVerb.TakeCard)
        {
            var hasAlternativeCard = action.Args.ContainsKey(Corruption.AlternativeCardId);
            var hasAlternativeIndex = action.Args.ContainsKey(Corruption.AlternativeOptionIndex);
            if (hasAlternativeCard != hasAlternativeIndex)
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) negative-control alternative card and option index must appear together.");
            }
        }

        var hasSubstituteCard = action.Args.ContainsKey(Corruption.SubstituteCardId);
        var hasSubstituteIndex = action.Args.ContainsKey(Corruption.SubstituteHandIndex);
        if (hasSubstituteCard != hasSubstituteIndex)
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) negative-control substitute card and hand index must appear together.");
        }
        if (action.Args.TryGetValue(Corruption.SubstituteCardId, out var substituteCardId) &&
            string.IsNullOrWhiteSpace(substituteCardId))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) argument '{Corruption.SubstituteCardId}' is empty.");
        }
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)$")]
    private static partial Regex NonNegativeIntegerPattern { get; }

    /// <summary>
    /// Everything <see cref="FloorEntryPlan.For"/> asks of a floor_entry, asked here
    /// instead of in front of a player.
    ///
    /// That plan aborts on a boundary naming an action the history does not contain,
    /// on one whose action is not a map move, on one with no checkpoint at that action
    /// naming <see cref="FloorEntryPlan.RequiredBoundaryFields"/>, and on one whose
    /// arrival names a different floor. A coordinate checked for shape alone passes
    /// publication and is refused later, as an aborted entry, so the same questions are
    /// asked at validation and resolved the same way: where several checkpoints sit at
    /// one action, the one the plan would take is the one this reads.
    ///
    /// A generated fixture is exempt from the checkpoint rule alone. One committed
    /// fixture declares a floor entry its generator writes no arrival for, and a
    /// fixture is not publication evidence - the gate refuses a synthetic source
    /// outright.
    /// </summary>
    private static void ValidateFloorEntry(
        ReplayManifest manifest, ReplayBoundary boundary, bool isFixture, List<string> problems)
    {
        var action = manifest.Actions.FirstOrDefault(candidate => candidate.Seq == boundary.AfterSeq);
        if (action is null)
        {
            problems.Add(
                $"boundaries declares {boundary.Describe()} after action " +
                $"{boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)}, which is not in this history. A " +
                "floor is arrived on by moving on the map, so a boundary naming no decision at all names a " +
                "moment this recording does not contain.");
            return;
        }

        if (action.Verb != ActionVerb.MapMove)
        {
            problems.Add(
                $"boundaries declares {boundary.Describe()} after action " +
                $"{boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)} ({action.Verb}), and a floor is " +
                "arrived on by moving on the map. A boundary pointing at any other action is not the moment it " +
                "claims to be.");
            return;
        }

        if (isFixture) return;

        var arrival = FloorArrival.ArrivalCheckpointAt(manifest.Checkpoints, boundary.AfterSeq);
        if (arrival is null)
        {
            problems.Add(
                $"boundaries declares {boundary.Describe()} after action " +
                $"{boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)} and no checkpoint there names " +
                $"{string.Join(" and ", FloorEntryPlan.RequiredBoundaryFields)}. A floor arrival is proved by " +
                "where the run stands, and standing a player somewhere nobody established is what this arbiter " +
                "exists to prevent.");
            return;
        }

        var declared = boundary.Floor.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
        var stated = arrival.Expect["run.total_floor"].Value;
        if (!string.Equals(stated, declared, StringComparison.Ordinal))
        {
            problems.Add(
                $"boundaries declares {boundary.Describe()}, and the checkpoint at that action says " +
                $"run.total_floor is {stated}. A floor cannot hand over a checkpoint for another floor.");
        }
    }

    private static void ValidateCheckpoints(
        ReplayManifest manifest, int videoDurationMs, List<string> problems)
    {
        var checkpoints = manifest.Checkpoints;
        var actions = manifest.Actions;
        var sourceKind = manifest.Source.Kind;

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

            var arrival = ReferenceEquals(
                FloorArrival.ArrivalCheckpointAt(checkpoints, checkpoint.AfterSeq), checkpoint)
                ? FloorArrival.At(manifest, checkpoint.AfterSeq)
                : null;

            foreach (var (field, fact) in checkpoint.Expect)
            {
                // A value nobody read is admissible here exactly where this validator
                // can re-derive it from the history: the two fields a floor arrival is
                // proved by follow from the map move that arrived and the floor the
                // boundary names, and only in the one checkpoint a floor plan would take
                // as that arrival. Anything else inferred is a reading that was never
                // taken - including the same field in another checkpoint standing at the
                // same action - and the rules below refuse it.
                if (fact.Source == FactSource.Inferred &&
                    arrival is not null && arrival.TryGetValue(field, out var derived))
                {
                    if (!string.Equals(fact.Value, derived, StringComparison.Ordinal))
                    {
                        problems.Add(
                            $"checkpoint '{checkpoint.Id}' field '{field}' is inferred as '{fact.Value}', and " +
                            $"this recording's own history gives '{derived}'. An inferred arrival is admissible " +
                            "because it can be re-derived; one that does not re-derive is a value nobody took.");
                    }
                    else if (string.IsNullOrWhiteSpace(fact.Evidence?.Note))
                    {
                        problems.Add(
                            $"checkpoint '{checkpoint.Id}' field '{field}' is inferred and records no reasoning, " +
                            "so nobody can re-check what it was derived from.");
                    }
                    continue;
                }

                if (sourceKind == "synthetic-engine")
                {
                    if (fact.Source != FactSource.Engine || fact.Evidence is not null)
                    {
                        problems.Add(
                            $"checkpoint '{checkpoint.Id}' field '{field}' in a synthetic fixture must be " +
                            "engine-produced and carry no video evidence.");
                    }
                }
                else if (sourceKind == "native")
                {
                    RequireCapturedFact(
                        fact, $"checkpoint '{checkpoint.Id}' field '{field}'", sourceKind, maxSeq, problems,
                        checkpoint.AfterSeq);
                }
                else
                {
                    RequireObservedVideoFact(
                        fact, $"checkpoint '{checkpoint.Id}' field '{field}'", videoDurationMs, problems);
                }
            }
        }
    }

    private static void ValidateEvidenceTimeline(ReplayManifest manifest, List<string> problems)
    {
        if (manifest.Source.Kind != "vod") return;

        var observedActions = manifest.Actions
            .Where(action => action.Source == FactSource.Observed && action.Evidence?.VideoTimeMs is not null)
            .ToList();
        if (observedActions.FirstOrDefault()?.Evidence?.VideoTimeMs is { } firstActionTimestamp &&
            manifest.Source.RunStart is { } runStart)
        {
            foreach (var (name, timestamp) in RunStartFactTimestamps(runStart))
            {
                if (timestamp >= firstActionTimestamp)
                {
                    problems.Add(
                        $"source.run_start.{name} timestamp {timestamp}ms must precede the first observed action " +
                        $"timestamp {firstActionTimestamp}ms.");
                }
            }
        }

        ActionRecord? previousObserved = null;
        foreach (var action in observedActions)
        {
            if (previousObserved?.Evidence?.VideoTimeMs is { } previousTimestamp &&
                action.Evidence!.VideoTimeMs is { } timestamp && timestamp < previousTimestamp)
            {
                problems.Add(
                    $"actions[{action.Seq}] ({action.Verb}) timestamp {timestamp}ms is earlier than " +
                    $"actions[{previousObserved.Seq}] ({previousObserved.Verb}) timestamp {previousTimestamp}ms. " +
                    "VOD action timestamps must be nondecreasing in sequence order.");
            }
            previousObserved = action;
        }

        foreach (var checkpoint in manifest.Checkpoints)
        {
            foreach (var (field, fact) in checkpoint.Expect)
            {
                if (checkpoint.AfterSeq >= 0 && checkpoint.AfterSeq < manifest.Actions.Count &&
                    manifest.Actions[checkpoint.AfterSeq].Evidence?.VideoTimeMs is { } actionTimestamp &&
                    fact.Evidence?.VideoTimeMs is { } checkpointTimestamp &&
                    checkpointTimestamp < actionTimestamp)
                {
                    problems.Add(
                        $"checkpoint '{checkpoint.Id}' field '{field}' timestamp {checkpointTimestamp}ms is " +
                        $"earlier than its after_seq action {checkpoint.AfterSeq} timestamp {actionTimestamp}ms.");
                }

                var nextActionIndex = checkpoint.AfterSeq + 1;
                if (nextActionIndex >= 0 && nextActionIndex < manifest.Actions.Count &&
                    manifest.Actions[nextActionIndex].Evidence?.VideoTimeMs is { } nextActionTimestamp &&
                    fact.Evidence?.VideoTimeMs is { } checkpointEvidenceTimestamp &&
                    checkpointEvidenceTimestamp > nextActionTimestamp)
                {
                    problems.Add(
                        $"checkpoint '{checkpoint.Id}' field '{field}' timestamp " +
                        $"{checkpointEvidenceTimestamp}ms is later than action {nextActionIndex} timestamp " +
                        $"{nextActionTimestamp}ms, which follows its after_seq position.");
                }
            }
        }
    }

    private static IEnumerable<(string Name, int Timestamp)> RunStartFactTimestamps(RunStartEvidence runStart)
    {
        foreach (var (name, timestamp) in new (string Name, int? Timestamp)[]
                 {
                     ("first_observed_run_time_s", runStart.FirstObservedRunTimeSeconds.Evidence?.VideoTimeMs),
                     ("first_observed_floor", runStart.FirstObservedFloor.Evidence?.VideoTimeMs),
                     ("entered_from_run_history", runStart.EnteredFromRunHistory.Evidence?.VideoTimeMs),
                     ("resume_modal_seen", runStart.ResumeModalSeen.Evidence?.VideoTimeMs),
                 })
        {
            if (timestamp is { } value) yield return (name, value);
        }
    }

    private static IEnumerable<(string Name, int Timestamp)> SummaryFactTimestamps(
        RunSummaryObservation summary)
    {
        foreach (var (name, timestamp) in new (string Name, int? Timestamp)[]
                 {
                     ("seed", summary.Seed.Evidence?.VideoTimeMs),
                     ("build_version", summary.BuildVersion.Evidence?.VideoTimeMs),
                     ("build_date_utc", summary.BuildDateUtc.Evidence?.VideoTimeMs),
                     ("content_hash", summary.ContentHash.Evidence?.VideoTimeMs),
                     ("ascension", summary.Ascension.Evidence?.VideoTimeMs),
                     ("floors_climbed", summary.FloorsClimbed.Evidence?.VideoTimeMs),
                     ("player_max_hp", summary.PlayerMaxHp.Evidence?.VideoTimeMs),
                     ("deck_size", summary.DeckSize.Evidence?.VideoTimeMs),
                     ("relic_count", summary.RelicCount.Evidence?.VideoTimeMs),
                 })
        {
            if (timestamp is { } value) yield return (name, value);
        }
    }

    private static IEnumerable<int> EarlierVideoTimestamps(ReplayManifest manifest)
    {
        var env = manifest.Environment;
        foreach (var timestamp in new int?[]
                 {
                     env.BuildVersion.Evidence?.VideoTimeMs,
                     env.BuildDateUtc.Evidence?.VideoTimeMs,
                     env.GameMode.Evidence?.VideoTimeMs,
                     env.Seed.Evidence?.VideoTimeMs,
                     env.ContentHash.Evidence?.VideoTimeMs,
                     env.Ascension.Evidence?.VideoTimeMs,
                     env.Character.Evidence?.VideoTimeMs,
                     env.Acts.Evidence?.VideoTimeMs,
                     env.Mods.Evidence?.VideoTimeMs,
                     manifest.Source.RunStart?.FirstObservedRunTimeSeconds.Evidence?.VideoTimeMs,
                     manifest.Source.RunStart?.FirstObservedFloor.Evidence?.VideoTimeMs,
                     manifest.Source.RunStart?.EnteredFromRunHistory.Evidence?.VideoTimeMs,
                     manifest.Source.RunStart?.ResumeModalSeen.Evidence?.VideoTimeMs,
                 })
        {
            if (timestamp is { } value) yield return value;
        }

        foreach (var action in manifest.Actions)
        {
            if (action.Evidence?.VideoTimeMs is { } timestamp) yield return timestamp;
        }

        foreach (var checkpoint in manifest.Checkpoints)
        {
            foreach (var fact in checkpoint.Expect.Values)
            {
                if (fact.Evidence?.VideoTimeMs is { } timestamp) yield return timestamp;
            }
        }
    }

    private static void ValidateInputFact<T>(
        Fact<T> fact, string path, string sourceKind, int videoDurationMs, int maxActionOrdinal,
        List<string> problems)
    {
        if (!Enum.IsDefined(fact.Source))
        {
            problems.Add($"{path} has undefined fact source value {(int)fact.Source}.");
            return;
        }

        if (fact.Source == FactSource.Engine)
        {
            problems.Add(
                $"{path} is marked source=engine. Replay inputs cannot be produced by the engine being checked.");
        }

        if (fact.Source == FactSource.Observed)
        {
            RequireObservedVideoFact(fact, path, videoDurationMs, problems);
        }

        if (fact.Source == FactSource.Captured)
        {
            ValidateCapturedEvidence(fact.Evidence, path, sourceKind, maxActionOrdinal, problems);
        }
    }

    private static void RequireObservedVideoFact<T>(
        Fact<T> fact, string path, int videoDurationMs, List<string> problems)
    {
        if (fact.Source != FactSource.Observed)
        {
            problems.Add($"{path} must be source=observed because it is evidence about what the video shows.");
        }
        else if (fact.Evidence?.VideoTimeMs is not { } timestamp)
        {
            problems.Add($"{path} is observed but has no video timestamp, so it cannot be re-checked.");
        }
        else
        {
            ValidateVideoTimestamp(timestamp, path, videoDurationMs, problems);
        }
    }

    /// <summary>
    /// A value a recorder read out of the live game has to say where in the run it
    /// read it. A run has no public clock, so the coordinate is the run's own ordered
    /// history - which is also what its identity is made of.
    /// </summary>
    private static void RequireCapturedFact<T>(
        Fact<T> fact, string path, string sourceKind, int maxActionOrdinal, List<string> problems,
        int? expectedActionOrdinal = null)
    {
        if (fact.Source != FactSource.Captured)
        {
            problems.Add(
                $"{path} must be source=captured because it is what a recorder read out of the game as it " +
                "happened.");
            return;
        }

        ValidateCapturedEvidence(
            fact.Evidence, path, sourceKind, maxActionOrdinal, problems, expectedActionOrdinal);
    }

    /// <summary>
    /// Every captured value in a manifest passes through here, which is what makes
    /// the native-only rule impossible to miss: captured is what a recorder read out
    /// of the live game it was running in, so a recording made from anything else
    /// cannot claim it, and a path added later inherits the refusal rather than having
    /// to remember it.
    /// </summary>
    private static void ValidateCapturedEvidence(
        FactEvidence? evidence, string path, string sourceKind, int maxActionOrdinal, List<string> problems,
        int? expectedActionOrdinal = null)
    {
        if (sourceKind != "native")
        {
            problems.Add(
                $"{path} is marked source=captured and this is a {sourceKind} recording. Captured is what a " +
                "recorder read out of the live game it was running in, which only a native recording has: a " +
                "reading off a video is observed or inferred, a value this project's own replay produced is " +
                "engine, and a fixture's values are declared.");
            return;
        }

        if (evidence?.ActionOrdinal is not { } actionOrdinal)
        {
            problems.Add(
                $"{path} is captured and names no action_ordinal, so nobody could say where in the run it was " +
                "read.");
        }
        else if (actionOrdinal < -1 || actionOrdinal > maxActionOrdinal)
        {
            problems.Add(
                $"{path} was captured at action ordinal {actionOrdinal.ToString(CultureInfo.InvariantCulture)}, " +
                $"outside the action range [-1, {maxActionOrdinal.ToString(CultureInfo.InvariantCulture)}].");
        }
        else if (expectedActionOrdinal is { } expected && actionOrdinal != expected)
        {
            problems.Add(
                $"{path} was captured at action ordinal {actionOrdinal.ToString(CultureInfo.InvariantCulture)}, " +
                $"but it belongs after action {expected.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (evidence?.RunClockMs is < 0)
        {
            problems.Add(
                $"{path} has run_clock_ms={evidence.RunClockMs.Value.ToString(CultureInfo.InvariantCulture)}. " +
                "A run clock cannot name a moment before the run began.");
        }
    }

    private static void ValidateVideoTimestamp(
        int timestamp, string path, int videoDurationMs, List<string> problems)
    {
        if (timestamp < 0 || timestamp > videoDurationMs)
        {
            problems.Add(
                $"{path} has video timestamp {timestamp}ms outside the source video range 0-{videoDurationMs}ms.");
        }
    }

    public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Problems)
    {
        public string Describe() => IsValid
            ? "manifest is valid"
            : string.Join("\n", Problems.Select(p => "  - " + p));
    }
}
