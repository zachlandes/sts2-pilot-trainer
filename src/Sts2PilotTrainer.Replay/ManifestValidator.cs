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

    public static ValidationResult Validate(ReplayManifest manifest) => Validate(manifest, null);

    public static ValidationResult ValidateLineReplay(ReplayManifest manifest, int lineFromSeq) =>
        Validate(manifest, lineFromSeq);

    private static ValidationResult Validate(ReplayManifest manifest, int? lineFromSeq)
    {
        var problems = new List<string>();

        ValidateSource(manifest.Source, problems);
        var videoDurationMs = manifest.Source.Video is { DurationSeconds: > 0 } video
            ? checked(video.DurationSeconds * 1000)
            : 0;
        ValidateEnvironment(manifest.Environment, manifest.Source.Kind, videoDurationMs, problems);
        if (manifest.Source.Synthetic is { } synthetic &&
            !string.Equals(
                synthetic.GeneratedBuild, manifest.Environment.BuildVersion.Value, StringComparison.Ordinal))
        {
            problems.Add(
                "source.synthetic.generated_build must match environment.build_version for the pinned fixture.");
        }
        ValidateRunStart(manifest.Source, videoDurationMs, problems);
        ValidateRunSummary(manifest, videoDurationMs, problems);
        ValidateActions(manifest.Actions, manifest.Source.Kind, videoDurationMs, lineFromSeq, problems);
        ValidateCheckpoints(
            manifest.Checkpoints, manifest.Actions, manifest.Source.Kind, videoDurationMs, problems);
        ValidateEvidenceTimeline(manifest, problems);

        if (lineFromSeq is { } start &&
            (start <= 0 || start >= manifest.Actions.Count || manifest.Checkpoints.Any(c => c.AfterSeq >= start)))
        {
            problems.Add(
                "line replay must retain a validated prefix, append at least one line action, and carry no " +
                "checkpoints into the hypothetical suffix.");
        }

        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            problems.Add("run_id is empty. Every artifact needs a stable identifier that is not a video title.");
        }

        return new ValidationResult(problems.Count == 0, problems);
    }

    private static void ValidateEnvironment(
        EnvironmentIdentity env, string sourceKind, int videoDurationMs, List<string> problems)
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

        ValidateInputFact(env.BuildVersion, "environment.build_version", videoDurationMs, problems);
        ValidateInputFact(env.BuildDateUtc, "environment.build_date_utc", videoDurationMs, problems);
        ValidateInputFact(env.GameMode, "environment.game_mode", videoDurationMs, problems);
        ValidateInputFact(env.Seed, "environment.seed", videoDurationMs, problems);
        ValidateInputFact(env.ContentHash, "environment.content_hash", videoDurationMs, problems);
        ValidateInputFact(env.Ascension, "environment.ascension", videoDurationMs, problems);
        ValidateInputFact(env.Character, "environment.character", videoDurationMs, problems);
        ValidateInputFact(env.Acts, "environment.acts", videoDurationMs, problems);
        ValidateInputFact(env.Mods, "environment.mods", videoDurationMs, problems);

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
        if (source.Kind is not ("vod" or "synthetic-engine"))
        {
            problems.Add(
                $"source.kind '{source.Kind}' is unsupported. This milestone accepts 'vod' and " +
                "'synthetic-engine'.");
        }

        if (source.Kind == "vod")
        {
            if (source.Video is null)
            {
                problems.Add("source.video is absent, so no reader could re-check any observation.");
            }
            else if (source.Video.DurationSeconds <= 0)
            {
                problems.Add("source.video.duration_s must be positive so observation timestamps can be bounded.");
            }

            if (source.Synthetic is not null)
            {
                problems.Add("source.synthetic must be absent for a VOD manifest.");
            }
        }
        else if (source.Kind == "synthetic-engine")
        {
            if (source.Video is not null || source.RunStart is not null || source.RunSummary is not null)
            {
                problems.Add("a synthetic-engine source cannot carry video, run-start, or run-summary evidence.");
            }

            if (source.Synthetic is not { } synthetic ||
                string.IsNullOrWhiteSpace(synthetic.FixtureId) ||
                synthetic.FixtureVersion != 1 ||
                synthetic.Generator != "sts2-pilot-trainer" ||
                string.IsNullOrWhiteSpace(synthetic.GeneratedBuild))
            {
                problems.Add(
                    "source.synthetic must identify a version-1 sts2-pilot-trainer engine fixture and its build.");
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
        IReadOnlyList<ActionRecord> actions, string sourceKind, int videoDurationMs,
        int? lineFromSeq, List<string> problems)
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

            var isLineAction = lineFromSeq is { } start && action.Seq >= start;
            if (isLineAction)
            {
                if (action.Source == FactSource.Inferred && string.IsNullOrWhiteSpace(action.Evidence?.Note))
                {
                    problems.Add($"actions[{action.Seq}] ({action.Verb}) inferred line action has no reasoning.");
                }
                else if (action.Source == FactSource.Observed &&
                         (action.Evidence?.VideoTimeMs is not { } lineTimestamp || lineTimestamp < 0))
                {
                    problems.Add(
                        $"actions[{action.Seq}] ({action.Verb}) observed line action has no valid timestamp.");
                }
                else if (action.Source is not (FactSource.Observed or FactSource.Inferred))
                {
                    problems.Add(
                        $"actions[{action.Seq}] ({action.Verb}) line action must be observed or inferred.");
                }
                continue;
            }

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
                allowed = required;
                nonNegativeIntegers = required;
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
        Fact<T> fact, string path, int videoDurationMs, List<string> problems)
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
