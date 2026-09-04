using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Constructs the recording's run, walks it through the recording's own decisions
    /// one at a time, and proves the fight it lands in is the recorded one.
    ///
    /// This is the whole of the in-game journey except the drawing. The run is built
    /// at the recording's identity against a supplied complete unlock state, each
    /// recorded decision is executed in order and captioned exactly as the mod's
    /// screens caption it, and the boundary is compared against both what the
    /// recording observed there and the manifest's engine-produced combat-start
    /// snapshot digest. Nothing here is a proxy for the retail client - the retail client runs the same
    /// <see cref="RecordedFightEntry"/> - and nothing here draws a screen.
    ///
    /// It also reports what it did to the profile, before and after, because "this
    /// never writes to your progress" is a claim and a claim wants a measurement.
    /// </summary>
    internal static int EnterFight(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var cacheDir = Args.Value(args, "--cache") ?? "build/snapshots";
        var stepOne = Args.Has(args, "--step");
        var artifact = EvidenceArtifact.Prepare(outDir, "enter-fight.json");

        var recording = ManifestJson.Load(manifestPath);
        if (Args.Value(args, "--control") is { } controlName)
        {
            recording = ApplyEntryControl(recording, controlName);
        }

        // Nullable, because a generated fixture has nobody behind it. Every caption
        // below is a player-facing sentence about a person, so where there is no
        // person there is no caption - the decisions are printed as themselves.
        var creator = RecordingIdentity.CreatorOrNull(recording);
        // The state the recording's run is generated against, which is the recorded
        // player's own where the recording carries it, exactly as replay resolves it.
        var progress = ParseProgress(args, RecordedFightEntry.SuppliedProgressFor(recording));

        Console.WriteLine($"recording       : {recording.RunId}");
        Console.WriteLine(
            $"creator         : {creator ?? "none - a generated fixture, with nobody to attribute it to"}");
        Console.WriteLine($"progress        : {progress} - {LocalEnvironment.OriginOf(progress)}");

        var profileBefore = ProfileReading(recording.Environment);
        var sandboxBefore = SandboxDigest();
        Console.WriteLine($"profile before  : {profileBefore}");
        Console.WriteLine();

        using var entry = RecordedFightEntry.StartHeadless(recording, PlanFor(recording, args), progress);
        var plan = entry.Plan;

        // Every caption the mod shows, produced by the mod's own wording owner from
        // the same readings. Built one step at a time because a caption names what the
        // run is standing in front of, and that is only readable while it is.
        var choices = new List<PrefightChoice>();
        var steps = new List<object>();

        Console.WriteLine(
            $"decisions before {plan.Describe()}: " +
            $"{plan.PrefixActions.Count.ToString(CultureInfo.InvariantCulture)}, " +
            $"reached after action {plan.BoundarySeq.ToString(CultureInfo.InvariantCulture)}");
        if (creator is not null) Console.WriteLine($"  {TrainerCopy.ChoicesShownAsRecorded(creator)}");
        Console.WriteLine();

        while (!entry.AtBoundary)
        {
            var action = entry.NextStep!;

            // Only some decisions have words. A journey to a later fight walks through
            // that fight's predecessors - cards played, turns ended, loot taken - and
            // those are executed and printed as themselves rather than captioned.
            var choice = creator is null ? null : entry.DescribeNextStepOrNull();
            if (choice is not null) choices.Add(choice);
            var journey = choice is null
                ? null
                : PrefightJourney.For(creator!, choices, plan.PrefixActions.Count);

            var step = journey?.Steps[^1];
            if (journey is not null && step is not null)
            {
                Console.WriteLine($"  [{journey.Chip}]  {step.Counter}   {step.Caption}");
            }

            Console.WriteLine(
                $"      action {action.Seq.ToString(CultureInfo.InvariantCulture)} {action.Verb} " +
                $"{string.Join(" ", action.Args.Select(arg => $"{arg.Key}={arg.Value}"))}");

            entry.AdvanceOneStep();
            steps.Add(new
            {
                number = step?.Number,
                counter = step?.Counter,
                caption = step?.Caption,
                seq = action.Seq,
                verb = action.Verb.ToString(),
                args = action.Args,
            });

            if (stepOne) break;
        }

        if (stepOne)
        {
            Console.WriteLine();
            Console.WriteLine(
                "--step stops after one decision. The fight is not entered, and asking whether it started " +
                "where the recording's did is refused rather than answered:");
            try
            {
                entry.VerifyBoundary();
            }
            catch (EngineException refusal)
            {
                Console.WriteLine($"  {refusal.Message}");
            }

            return 1;
        }

        var equality = entry.VerifyBoundary();
        var cachedDigest = CachedSnapshotDigest(plan, cacheDir, out var snapshotSource);
        if (cachedDigest is not null &&
            !string.Equals(cachedDigest, equality.ExpectedDigest, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The cached combat-start snapshot is {cachedDigest}, but the recording declares " +
                $"{equality.ExpectedDigest}. Re-run combat-snapshot before entering the fight.");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"boundary        : checkpoint '{plan.Boundary.Id}', " +
            $"{equality.Comparisons.Count.ToString(CultureInfo.InvariantCulture)} observed value(s)");
        foreach (var comparison in equality.Comparisons)
        {
            Console.WriteLine(
                $"  {(comparison.Matches ? "ok  " : "FAIL")} {comparison.Field,-26} " +
                $"recording={comparison.Expected,-56} game={comparison.Actual}");
        }

        Console.WriteLine();
        Console.WriteLine($"snapshot        : {snapshotSource}");
        Console.WriteLine($"  recorded      : {equality.ExpectedDigest}");
        Console.WriteLine($"  this game     : {equality.ActualDigest}");

        var profileAfter = ProfileReading(recording.Environment);
        var sandboxAfter = SandboxDigest();
        var profileUnchanged = profileBefore == profileAfter && sandboxBefore == sandboxAfter;
        Console.WriteLine();
        Console.WriteLine($"profile after   : {profileAfter}");
        Console.WriteLine(
            $"profile writes  : {(profileUnchanged ? "none - the reading and every byte of the profile store are unchanged" : "CHANGED")}");

        Console.WriteLine();
        Console.WriteLine(equality.Matches
            ? $"ENTERED - this game is standing at {plan.Describe()}" +
              $"{(creator is null ? "" : $" of {creator}'s run")}, exactly as the recording records it."
            : "REFUSED - " + equality.Refusal);

        object? played = null;
        if (Args.Has(args, "--play") && equality.Matches)
        {
            played = PlayAndCompare(
                entry, equality, RecordingIdentity.Creator(recording), manifestPath,
                Args.Value(args, "--recorded-fight"));
        }

        artifact.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/enter-fight/v1",
                manifest = Path.GetFileName(manifestPath),
                run_id = recording.RunId,
                creator,
                control = Args.Value(args, "--control"),
                progress = progress.ToString(),
                progress_origin = entry.ProgressOrigin,
                boundary = plan.Describe(),
                boundary_seq = plan.BoundarySeq,
                boundary_checkpoint = plan.Boundary.Id,
                steps,
                boundary_matches = equality.Matches,
                comparisons = equality.Comparisons,
                recorded_snapshot_digest = equality.ExpectedDigest,
                this_game_digest = equality.ActualDigest,
                snapshot_source = snapshotSource,
                refusal = equality.Refusal,
                profile_before = profileBefore,
                profile_after = profileAfter,
                profile_unchanged = profileUnchanged,
                played,
                entry_policy =
                    "The run is constructed at the recording's identity against a supplied complete unlock " +
                    "state, set up with saving off, and never written anywhere. The recording owns every " +
                    "decision before the fight; the fight itself is nobody's yet.",
            }, Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine($"report: {Paths.Display(artifact.Path)}");
        return equality.Matches && profileUnchanged ? 0 : 1;
    }

    /// <summary>
    /// Which boundary of the recording to walk to.
    ///
    /// The recording's first fight unless asked otherwise, because that is what this
    /// command was for when there was one boundary. A fight and a floor are different
    /// destinations rather than two ways of saying one, so asking for both is refused.
    /// </summary>
    private static IBoundaryPlan PlanFor(ReplayManifest recording, string[] args)
    {
        var fight = Args.Value(args, "--fight");
        var floor = Args.Value(args, "--floor");

        if (fight is not null && floor is not null)
        {
            throw new ManifestException(
                "enter-fight takes --fight or --floor, not both. They are different places to be stood.");
        }

        var selector = floor is not null
            ? new BoundarySelector
            {
                Kind = ReplayBoundary.FloorEntryKind,
                Floor = Ordinal(floor, "--floor"),
            }
            : fight is null
                ? BoundarySelector.FirstFight
                : new BoundarySelector
                {
                    Kind = ReplayBoundary.CombatStartKind,
                    Fight = Ordinal(fight, "--fight"),
                };

        return selector.PlanFor(recording);
    }

    private static int Ordinal(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new ManifestException($"{option} takes a whole number from 1, not '{value}'.");

    /// <summary>
    /// Plays the recording's own fight through the player-side capture and compares
    /// it with the shipped recorded fight, printing exactly what the in-game result
    /// panel would say.
    ///
    /// The recording stands in for the player here, so every summary row reads the
    /// same on both sides and every turn line reads the same twice. That is the
    /// point rather than a limitation: the capture, the projection and the
    /// comparison are the same code the client runs, and a line that came through
    /// them and did not match the engine's own replay of the same actions would be a
    /// defect in the capture. A person's line is what the client provides.
    ///
    /// The profile lines above bracket the entry and not this: the headless host has
    /// no write barrier, so a won fight writes progress into the sandbox here exactly
    /// as it would into a player's profile without one. That is measured and printed,
    /// because it is the reason the mod's barrier exists.
    /// </summary>
    private static object PlayAndCompare(
        RecordedFightEntry entry, BoundaryEquality equality, string creator, string manifestPath,
        string? recordedFightPath)
    {
        recordedFightPath ??= manifestPath.Replace(".replay.json", ".recorded-fights.json", StringComparison.Ordinal);
        var recorded = RecordedFights.Load(recordedFightPath);
        recorded.Bind(entry.Manifest);

        var sandboxBefore = SandboxDigest();
        var capture = entry.BeginCapture(equality);
        entry.PlayRecordedFightHeadless();
        var sandboxAfter = SandboxDigest();

        Console.WriteLine();
        Console.WriteLine(
            $"played          : {creator}'s own {capture.Trace.Steps.Count - 1} fight action(s), through the " +
            "player-side capture");
        Console.WriteLine($"capture         : {capture.State}");
        Console.WriteLine(
            $"sandbox writes  : {(sandboxBefore == sandboxAfter ? "none during the fight" : "CHANGED during the fight")} " +
            "- measured because the headless host has no write barrier; in the client ProfileWriteBarrier " +
            "stops what a won fight would write");

        var yours = capture.Project();
        var comparison = CombatComparison.Between(yours, recorded.Projection(entry.Fight));
        var screen = FightResultScreen.For(creator, comparison);

        Panel(screen);

        return new
        {
            recorded_fight = Path.GetFileName(recordedFightPath),
            capture_state = capture.State.ToString(),
            sandbox_unchanged_during_play = sandboxBefore == sandboxAfter,
            comparison,
            screen,
        };
    }

    /// <summary>
    /// Damages one of the recording's decisions before the fight, using the project's
    /// own negative controls rather than a drift injector written for this command.
    ///
    /// Only the controls that reach a pre-fight decision are offered: a control that
    /// damaged a card play would leave the entry boundary untouched and prove nothing
    /// about it.
    /// </summary>
    private static ReplayManifest ApplyEntryControl(ReplayManifest manifest, string name)
    {
        var control = Corruption.All.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new ManifestException(
                $"'{name}' is not a negative control. Available: " +
                $"{string.Join(", ", Corruption.All.Select(candidate => candidate.Name))}.");

        if (!control.AppliesTo(manifest))
        {
            throw new ManifestException(
                $"Control '{control.Name}' needs {control.Requires}, and this recording has none.");
        }

        var damaged = control.Apply(manifest);

        // A control that damaged a decision after the fight started leaves this
        // boundary untouched, and an entry that then succeeded would look like
        // evidence that drift is caught when nothing had drifted. So the control has
        // to reach the prefix, and one that does not is refused rather than run.
        var prefix = RecordedFightPlan.For(manifest).PrefixActions;
        var damagedPrefix = damaged.Actions
            .OrderBy(action => action.Seq)
            .Take(prefix.Count)
            .ToList();
        var reachesPrefix =
            damagedPrefix.Count != prefix.Count ||
            prefix.Where((action, index) => !SameDecision(action, damagedPrefix[index])).Any();

        if (!reachesPrefix)
        {
            throw new ManifestException(
                $"Control '{control.Name}' changes nothing the recording decides before its fight, so entering " +
                "the damaged history would prove nothing about the combat-start boundary. Use a control that " +
                "damages one of the first " +
                $"{prefix.Count.ToString(CultureInfo.InvariantCulture)} action(s).");
        }

        Console.WriteLine($"control         : {control.Name} - {control.What}");
        return damaged;
    }

    private static bool SameDecision(ActionRecord left, ActionRecord right) =>
        left.Seq == right.Seq &&
        left.Verb == right.Verb &&
        left.Args.Count == right.Args.Count &&
        left.Args.All(arg =>
            right.Args.TryGetValue(arg.Key, out var value) &&
            string.Equals(arg.Value, value, StringComparison.Ordinal));

    /// <summary>The digest the combat-start snapshot cache holds for this exact
    /// history, when this machine has materialised it.</summary>
    private static string? CachedSnapshotDigest(
        IBoundaryPlan plan, string cacheDir, out string source)
    {
        var directory = plan.SnapshotKey.ResolveCacheDirectory(cacheDir);
        var path = SnapshotCacheKey.ResolveCacheArtifact(directory, "state.canonical");
        if (!File.Exists(path))
        {
            source =
                $"recording manifest; no local cache under {plan.SnapshotKey.ToCacheDirectoryName()}";
            return null;
        }

        source = $"cache hit, {plan.SnapshotKey.ToCacheDirectoryName()}";
        return CanonicalState.DigestRendering(File.ReadAllText(path));
    }

    /// <summary>
    /// What this process's profile says, as one line, so before and after can be
    /// compared without a reader having to hold two tables in their head.
    /// </summary>
    private static string ProfileReading(EnvironmentIdentity expected)
    {
        var reading = LocalEnvironment.ReadPrerequisites(expected, PlayerProgress.LocalProfile);
        var categories = string.Join(", ", reading.Unlocks.Categories.Select(category =>
            $"{category.Name} {category.Available.ToString(CultureInfo.InvariantCulture)}/" +
            category.Required.ToString(CultureInfo.InvariantCulture)));
        var ceiling = (reading.ProfileAscensionCeiling ?? 0).ToString(CultureInfo.InvariantCulture);
        return $"ascension ceiling {ceiling} for {expected.Character.Value}; {categories}";
    }

    /// <summary>
    /// The result panel, as far as a terminal can draw it.
    ///
    /// The client draws card art, potion art and two lines on a chart; this prints
    /// the same model in the same order - the summary figures, then the turn
    /// chronology, then the chart's own numbers - so what the panel is made of can be
    /// read where there is no screen. Every word is the panel's own, and nothing here
    /// computes: a cell the chart has no value for is blank, exactly as the chart
    /// draws no point there.
    /// </summary>
    private static void Panel(FightResultScreen screen)
    {
        Console.WriteLine();
        Console.WriteLine($"[{screen.Title}]");
        Console.WriteLine($"  {screen.SameBoundaryNote}");
        Console.WriteLine();
        Row($"  {"",-22}{screen.Columns[0],-14}{screen.Columns[1]}");
        foreach (var row in screen.Rows)
        {
            Row($"  {row.Label,-22}{row.Yours,-14}{row.Theirs}{(row.Matches ? string.Empty : "   (differs)")}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {screen.TurnDetailHeading}");
        Row($"  {screen.Chart.TurnLabel,4}   {screen.Columns[0],-36}{screen.Columns[1]}");
        foreach (var turn in screen.Turns)
        {
            Row($"  {turn.Turn,4}   {Played(turn.Yours, screen.FightOverLabel),-36}" +
                $"{Played(turn.Theirs, screen.FightOverLabel)}");
        }

        Chart(screen.Chart);

        Console.WriteLine();
        foreach (var note in screen.Notes) Console.WriteLine($"  {note}");
        Console.WriteLine($"  [{screen.DoneButton}]");
    }

    /// <summary>
    /// The chart's numbers, turn by turn: both measures for both lines, and the
    /// potions marked on the turn they were spent.
    /// </summary>
    private static void Chart(FightResultChart chart)
    {
        Console.WriteLine();
        Console.WriteLine($"  {chart.Heading}");
        Row($"  {"",4}   {chart.EnemyMeasureLabel,-26}{chart.PlayerMeasureLabel,-26}{TrainerCopy.PotionsUsedRow}");
        Row($"  {chart.TurnLabel,4}   {chart.Yours.Label,-13}{chart.Theirs.Label,-13}" +
            $"{chart.Yours.Label,-13}{chart.Theirs.Label,-13}");

        for (var index = 0; index < chart.Turns.Count; index++)
        {
            var yours = chart.Yours.Points[index];
            var theirs = chart.Theirs.Points[index];
            Row($"  {chart.Turns[index],4}   {Value(yours.EnemyHealthLost),-13}{Value(theirs.EnemyHealthLost),-13}" +
                $"{Value(yours.HealthLost),-13}{Value(theirs.HealthLost),-13}" +
                Potions(chart, yours, theirs));
        }
    }

    /// <summary>One row of the panel. Columns are padded to line up, and a row whose
    /// last columns are empty is not padded into trailing whitespace.</summary>
    private static void Row(string row) => Console.WriteLine(row.TrimEnd());

    /// <summary>A measurement, or nothing at all where this line did not reach the
    /// turn. Never a zero: a zero would say the turn was fought and cost nothing.</summary>
    private static string Value(int? measurement) =>
        measurement?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>The potions either line spent on this turn, named by the side that
    /// spent them.</summary>
    private static string Potions(FightResultChart chart, FightResultPoint yours, FightResultPoint theirs) =>
        string.Join("; ", new[] { (chart.Yours.Label, yours), (chart.Theirs.Label, theirs) }
            .Where(side => side.Item2.PotionModelIds.Count > 0)
            .Select(side =>
                $"{side.Label}: {string.Join(", ", side.Item2.PotionModelIds.Select(ModelIdNames.Display))}"));

    /// <summary>
    /// What one side played on one turn, as a terminal renders the panel's card and
    /// potion art: the cards in the order they were played, then the potions. A side
    /// that never reached the turn says so in the panel's own words.
    /// </summary>
    private static string Played(FightResultTurnSide? side, string fightOver) =>
        side is null
            ? fightOver
            : string.Join(", ", side.CardModelIds.Concat(side.PotionModelIds).Select(ModelIdNames.Display));

    /// <summary>
    /// A digest over every byte of the sandbox profile store.
    ///
    /// The reading above would not notice a write that changed something it does not
    /// report. This would: it is a hash of the files themselves, so any save, any
    /// progress update and any new file changes it.
    /// </summary>
    private static string SandboxDigest()
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "build", "sandbox");
        if (!Directory.Exists(root)) return "sha256:absent";

        var rendering = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            rendering
                .Append(Path.GetRelativePath(root, file))
                .Append('')
                .Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))
                .Append('\n');
        }

        return "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(rendering.ToString())));
    }
}
