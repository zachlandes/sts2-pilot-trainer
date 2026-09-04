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
    /// build's claims are made about. Version 2 plays its combat to the end; version 1
    /// stopped after the opening turn.
    /// </summary>
    public const int SyntheticFixtureVersion = 2;

    private static readonly string[] KnownGameModes = ["standard", "custom", "daily"];

    /// <summary>
    /// The reward kinds this history claims with a single click on the loot screen.
    /// The card reward is absent on purpose: it opens a card screen, so taking it is
    /// <see cref="ActionVerb.TakeCard"/>, which records which card came back.
    /// </summary>
    public static readonly string[] ClaimableRewardTypes = ["gold", "potion"];

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    private static partial Regex BuildVersionPattern { get; }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}$")]
    private static partial Regex BuildDatePattern { get; }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex ContentHashPattern { get; }

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$")]
    private static partial Regex SnapshotDigestPattern { get; }

    public static ValidationResult Validate(ReplayManifest manifest)
    {
        var problems = new List<string>();

        var maxActionOrdinal = manifest.Actions.Count - 1;
        ValidateSource(manifest.Source, maxActionOrdinal, problems);
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
        ValidateCheckpoints(
            manifest.Checkpoints, manifest.Actions, manifest.Source.Kind, videoDurationMs, problems);
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
            env.BuildVersion, "environment.build_version", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.BuildDateUtc, "environment.build_date_utc", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.GameMode, "environment.game_mode", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.Seed, "environment.seed", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(
            env.ContentHash, "environment.content_hash", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.Ascension, "environment.ascension", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.Unlocks, "environment.unlocks", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.Character, "environment.character", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.Acts, "environment.acts", videoDurationMs, maxActionOrdinal, problems);
        ValidateInputFact(env.Mods, "environment.mods", videoDurationMs, maxActionOrdinal, problems);

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
        SourceProvenance source, int maxActionOrdinal, List<string> problems)
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
            ValidateNativeSource(source, maxActionOrdinal, problems);
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
    /// half way through a run, or from two disconnected stretches of one, replays
    /// perfectly and reconstructs a different run.
    /// </summary>
    private static void ValidateNativeSource(
        SourceProvenance source, int maxActionOrdinal, List<string> problems)
    {
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
            native.WitnessedRunStart, "source.native.witnessed_run_start", maxActionOrdinal, problems);

        if (!native.WitnessedRunStart.Value)
        {
            problems.Add(
                "source.native.witnessed_run_start is false. The recorder joined a run already in progress, so " +
                "the history it holds is not this run's from its start - and replaying it from run start " +
                "reconstructs a different run while every other gate passes.");
        }

        if (!native.IsContinuous)
        {
            problems.Add(
                $"source.native.continuity is '{native.Continuity}'. The recorder stopped and started again, so " +
                "it cannot know what happened in between, and a history with a hole in it is not this run's.");
        }
    }

    /// <summary>
    /// The places a player can be stood in this recording.
    ///
    /// The kinds are a closed set because a host dispatches on them: a kind nothing
    /// knows how to reach would be a place the recording says a player can go and
    /// nothing can take them. Every digest is engine-produced or captured live, since
    /// no video shows draw order or a random stream's position, which is the whole
    /// reason a boundary carries a digest at all.
    ///
    /// Where a manifest carries a verified whole-run trace, every fight in that trace
    /// must have a boundary: a run whose third fight has nowhere to be entered from
    /// is a recording that silently offers less than it holds.
    /// </summary>
    private static void ValidateBoundaries(ReplayManifest manifest, List<string> problems)
    {
        var boundaries = manifest.Boundaries;
        var maxSeq = manifest.Actions.Count - 1;

        if (manifest.Source.Kind == "synthetic-engine")
        {
            if (boundaries.Count > 0)
            {
                problems.Add(
                    "a synthetic-engine fixture cannot declare boundaries. It makes no publication claim and " +
                    "there is nobody to stand in it.");
            }
            return;
        }

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
                    boundary.Digest, $"the digest at {name}", maxSeq, problems, boundary.AfterSeq);
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

        if (!boundaries.Any(boundary => boundary.IsCombatStart))
        {
            problems.Add(
                "boundaries names no combat_start. A recording must carry the boundary a retail host compares " +
                "hidden state against, or nobody can be stood in its fight without trusting a machine-local " +
                "snapshot cache.");
        }

        if (manifest.Verification is not { Status: VerificationStatus.Verified, Trace: { } trace }) return;

        var coveredFights = RunCoverage.Of(trace).Fights;
        foreach (var boundary in boundaries.Where(boundary => boundary.IsCombatStart))
        {
            var fight = coveredFights.FirstOrDefault(fight => fight.Fight == boundary.Fight);
            if (fight is null)
            {
                problems.Add(
                    $"boundaries declares {boundary.Describe()}, but this history's verified trace holds no " +
                    "fight with that ordinal. A combat_start cannot name a fight the recording does not contain.");
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

        foreach (var fight in coveredFights)
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
                        action.Evidence, path, actions.Count - 1, problems, action.Seq);
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
                required = ["option_index"];
                allowed = required;
                nonNegativeIntegers = required;
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
                    "negative_control_substitute_card_id",
                    "negative_control_substitute_hand_index",
                ];
                nonNegativeIntegers =
                ["hand_index", "target_index", "negative_control_substitute_hand_index"];
                break;
            case ActionVerb.EndTurn:
                required = [];
                allowed = [];
                nonNegativeIntegers = [];
                break;
            case ActionVerb.ChooseEventOption:
                // The event id is required and the opening blessing's is not: which
                // event a floor generates is a consequence of the whole history before
                // it, and an option index means nothing without the event it indexes.
                required = ["event_id", "option_index"];
                allowed = required;
                nonNegativeIntegers = ["option_index"];
                break;
            case ActionVerb.ClaimReward:
                required = ["reward_type"];
                allowed = required;
                nonNegativeIntegers = [];
                break;
            case ActionVerb.TakeCard:
                required = ["card_id", "option_index"];
                allowed = [.. required, Corruption.AlternativeCardId, Corruption.AlternativeOptionIndex];
                nonNegativeIntegers = ["option_index", Corruption.AlternativeOptionIndex];
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

        // A reward kind the driver cannot name is refused at ingestion rather than at
        // replay, because a manifest that says 'coins' would otherwise look valid right
        // up until an engine is spent on it.
        if (action.Args.TryGetValue("reward_type", out var rewardType) &&
            !ClaimableRewardTypes.Contains(rewardType, StringComparer.Ordinal))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) argument 'reward_type' is '{rewardType}'. Known " +
                $"kinds: {string.Join(", ", ClaimableRewardTypes)}. A card reward opens a second screen and " +
                "is taken with TakeCard, which records which card came back.");
        }

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

        var hasSubstituteCard = action.Args.ContainsKey("negative_control_substitute_card_id");
        var hasSubstituteIndex = action.Args.ContainsKey("negative_control_substitute_hand_index");
        if (hasSubstituteCard != hasSubstituteIndex)
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) negative-control substitute card and hand index must appear together.");
        }
        if (action.Args.TryGetValue("negative_control_substitute_card_id", out var substituteCardId) &&
            string.IsNullOrWhiteSpace(substituteCardId))
        {
            problems.Add(
                $"actions[{action.Seq}] ({action.Verb}) argument 'negative_control_substitute_card_id' is empty.");
        }
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)$")]
    private static partial Regex NonNegativeIntegerPattern { get; }

    private static void ValidateCheckpoints(
        IReadOnlyList<Checkpoint> checkpoints, IReadOnlyList<ActionRecord> actions,
        string sourceKind, int videoDurationMs, List<string> problems)
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
                        fact, $"checkpoint '{checkpoint.Id}' field '{field}'", maxSeq, problems,
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
        Fact<T> fact, string path, int videoDurationMs, int maxActionOrdinal, List<string> problems)
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
            ValidateCapturedEvidence(fact.Evidence, path, maxActionOrdinal, problems);
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
        Fact<T> fact, string path, int maxActionOrdinal, List<string> problems,
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
            fact.Evidence, path, maxActionOrdinal, problems, expectedActionOrdinal);
    }

    private static void ValidateCapturedEvidence(
        FactEvidence? evidence, string path, int maxActionOrdinal, List<string> problems,
        int? expectedActionOrdinal = null)
    {
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
