using System.Globalization;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>Where a capture of a whole run has got to.</summary>
public enum RunCaptureState
{
    /// <summary>The run is being played and every decision so far has been recorded.</summary>
    Recording,

    /// <summary>The run is over. The manifest is the whole of it.</summary>
    Finished,

    /// <summary>The recorder cannot account for the run continuously.
    /// <see cref="RunCapture.Refusal"/> says why. Nothing is discarded.</summary>
    Broken,
}

/// <summary>
/// A run somebody is playing, recorded decision by decision into a native manifest.
///
/// <see cref="FightCapture"/> is this for one fight, and this is the same idea over a
/// whole run: the game announces each decision, the host samples the settled state
/// after it, and the rules about what those samples mean live here rather than in the
/// mod that supplies them. It delegates the inside of a fight to a
/// <see cref="FightCapture"/> per fight, so a fight a person plays goes through one
/// capture path whether the Combat Trainer or the recorder is watching.
///
/// It records rather than derives. Where the boundaries of the run are is
/// <see cref="RunCoverage"/>'s question, asked of the trace this builds; what the
/// state was at each of them is the only part a recorder can answer, and that is what
/// it keeps. A digest is <see cref="FactSource.Captured"/> here and
/// <see cref="FactSource.Engine"/> when the arbiter re-derives it, and comparing the
/// two is the whole point of publishing one.
///
/// Two facts about a recording cannot be established downstream and are established
/// here. A recorder that joined a run half way through has a history that replays
/// perfectly into a different run, so <see cref="Begin"/> refuses a run whose start it
/// did not witness. A recorder that stopped and started again saw two stretches of a
/// run and cannot know what happened between them, so <see cref="Resume"/> compares
/// the state the game resumed into against the state the journal last recorded and
/// marks <see cref="Continuity"/> broken when they differ. Nothing is ever truncated:
/// a broken recording keeps every decision it saw, and it is the refusal that says it
/// is not this run's history, not the absence of data.
///
/// Nothing here reads the game. Every reading arrives from the caller, which is what
/// keeps every rule in this class testable on a machine that does not own the game.
/// </summary>
public sealed class RunCapture
{
    /// <summary>The verb of the step that marks the state before any decision was
    /// made. The run-level counterpart of <see cref="FightCapture.CombatStartVerb"/>.</summary>
    public const string RunStartVerb = "run_start";

    /// <summary>The kind of the checkpoint taken at the run's last decision. Free text
    /// like every checkpoint kind, and deliberately not one of
    /// <see cref="ReplayBoundary.Kinds"/>: the end of a run is not somewhere a player
    /// can be stood.</summary>
    public const string RunEndCheckpointKind = "run_end";

    /// <summary>How a captured fight names itself to the comparison.</summary>
    public const string FightSourceIdPrefix = "recorded-fight-";

    private readonly List<ReplayStep> _steps = [];
    private readonly List<ActionRecord> _actions = [];
    private readonly List<FightCapture> _fights = [];
    private readonly List<RunJournalEntry> _entries = [];
    private readonly Dictionary<int, string> _digests = [];
    private readonly Dictionary<int, int?> _clocks = [];

    private FightCapture? _fight;

    private RunCapture(
        RunRecordingStart start, bool witnessedRunStart, string continuity, RunJournalEntry opening)
    {
        RunId = start.RunId;
        RecorderVersion = start.RecorderVersion;
        Identity = start.Identity;
        WitnessedRunStart = witnessedRunStart;
        Continuity = continuity;
        Opening = opening;

        var sample = ReplayTrace.Sample(opening.State);
        _steps.Add(new ReplayStep
        {
            Seq = -1,
            Verb = RunStartVerb,
            Before = sample,
            After = sample,
        });
        _digests[-1] = opening.Digest;
        _clocks[-1] = opening.RunClockMs;
    }

    /// <summary>The identifier every artifact of this recording is keyed by.</summary>
    public string RunId { get; }

    /// <summary>Which build of the recorder is writing this.</summary>
    public string RecorderVersion { get; }

    /// <summary>What the recorder read out of the game at run start.</summary>
    public RunIdentityReading Identity { get; }

    /// <summary>Whether the recorder was watching when the run began.</summary>
    public bool WitnessedRunStart { get; }

    /// <summary>One of <see cref="NativeSource.Continuities"/>.</summary>
    public string Continuity { get; private set; }

    public RunCaptureState State { get; private set; } = RunCaptureState.Recording;

    /// <summary>Why this recording is not a continuous account of the run, or null
    /// while it is.</summary>
    public string? Refusal { get; private set; }

    /// <summary>How the run ended, once it has. One of
    /// <see cref="NativeSource.Outcomes"/>.</summary>
    public string? Outcome { get; private set; }

    /// <summary>The sequence number the next decision will take.</summary>
    public int NextSeq => _actions.Count;

    /// <summary>The state before any decision, as the journal recorded it.</summary>
    public RunJournalEntry Opening { get; }

    /// <summary>
    /// The complete state digest after the most recent decision, or the opening
    /// reading's when none has been made.
    ///
    /// What a host asks when it needs to know whether anything happened. A digest
    /// covers the draw order and every random stream's position, so two decisions
    /// apart it is the sharpest available answer to "did the engine do anything" -
    /// which is not the same question as "did the player decide something", and the
    /// caller is the one that knows which it is asking.
    /// </summary>
    public string LastDigest => _entries.Count > 0 ? _entries[^1].Digest : Opening.Digest;

    /// <summary>Everything recorded so far, in order.</summary>
    public ReplayTrace Trace => new() { Steps = _steps.ToList() };

    /// <summary>Every decision so far, as the manifest records them.</summary>
    public IReadOnlyList<ActionRecord> Actions => _actions;

    /// <summary>The fight being played right now, or null between fights.</summary>
    public FightCapture? Fight => _fight;

    /// <summary>Every fight of the run, in the order they were played, including one
    /// still being fought.</summary>
    public IReadOnlyList<FightCapture> Fights => _fights;

    /// <summary>The journal as it stands: the header, the opening reading, and one
    /// entry per decision. What a crash leaves behind.</summary>
    public RunJournal Journal => new()
    {
        SchemaId = RunJournal.Schema,
        RunId = RunId,
        RecorderVersion = RecorderVersion,
        Identity = Identity,
        WitnessedRunStart = WitnessedRunStart,
        Entries = [Opening, .. _entries],
    };

    /// <summary>
    /// Starts recording a run at its beginning.
    /// </summary>
    /// <exception cref="ManifestException">
    /// When the reading handed over is not of a run at its start. A recorder that
    /// attached to a run already in progress would write a history that begins in the
    /// middle of one and replays, from run start, into a different run - and every
    /// other gate would pass. That is the native counterpart of a resumed video, and
    /// it is refused here rather than deferred.
    /// </exception>
    public static RunCapture Begin(RunRecordingStart start)
    {
        Require(!string.IsNullOrWhiteSpace(start.RunId), "A recording needs a run id to be keyed by.");
        Require(
            !string.IsNullOrWhiteSpace(start.RecorderVersion),
            "A recording needs the recorder version that wrote it, so a defect in one build is traceable to " +
            "everything it wrote.");
        Require(
            !string.IsNullOrWhiteSpace(start.Digest),
            "A recording needs the complete canonical state digest of the run's opening, or nothing can say " +
            "which run it began watching.");

        var sample = ReplayTrace.Sample(start.State);
        if (InCombat(sample))
        {
            throw new ManifestException(
                "This run is already in a fight, so the recorder did not see it begin. A history recorded from " +
                "half way through a run replays perfectly into a different run, and nothing downstream could " +
                "see that.");
        }

        if (Floor(sample) is { } floor and > 1)
        {
            throw new ManifestException(
                $"This run is already on floor {floor.ToString(CultureInfo.InvariantCulture)}, so the recorder " +
                "did not see it begin. A history recorded from half way through a run replays perfectly into a " +
                "different run, and nothing downstream could see that.");
        }

        var opening = new RunJournalEntry
        {
            Seq = -1,
            Verb = RunStartVerb,
            State = sample,
            Digest = start.Digest,
            RunClockMs = start.RunClockMs,
        };

        return new RunCapture(start, witnessedRunStart: true, NativeSource.ContinuousContinuity, opening);
    }

    /// <summary>
    /// Picks a run back up from the journal a previous session left behind.
    ///
    /// The game saves a run when a room is entered, so a session continued from the
    /// game's own save resumes at the last such point. Whether that is where this
    /// recorder stopped watching is the question: the journal's last entry carries the
    /// complete state digest of the moment it was written, and the live game carries
    /// the digest of the moment it resumed into. Equal means nothing happened in
    /// between that the recorder missed. Anything else means it did, and the recording
    /// is marked broken rather than repaired - a history missing decisions replays into
    /// a different run while every value in it is individually true.
    /// </summary>
    /// <param name="journal">What the previous session wrote.</param>
    /// <param name="liveDigest">The complete canonical state digest of the run the
    /// game has just resumed into.</param>
    public static RunCapture Resume(RunJournal journal, string liveDigest)
    {
        journal.RequireReadable();
        Require(
            !string.IsNullOrWhiteSpace(liveDigest),
            "Continuing a recording needs the complete canonical state digest of the run the game resumed " +
            "into, or nothing can say whether it is the run the journal describes.");

        var start = new RunRecordingStart
        {
            RunId = journal.RunId,
            RecorderVersion = journal.RecorderVersion,
            Identity = journal.Identity,
            State = journal.Opening.State,
            Digest = journal.Opening.Digest,
            RunClockMs = journal.Opening.RunClockMs,
        };

        var capture = new RunCapture(
            start, journal.WitnessedRunStart, NativeSource.ContinuousContinuity, journal.Opening);
        foreach (var entry in journal.Decisions) capture.Replay(entry);

        var last = capture._entries.Count > 0 ? capture._entries[^1] : journal.Opening;
        if (!string.Equals(last.Digest, liveDigest, StringComparison.Ordinal))
        {
            var rolledBackTo = capture.Journal.Entries
                .LastOrDefault(entry => string.Equals(entry.Digest, liveDigest, StringComparison.Ordinal));
            capture.Break(rolledBackTo is null
                ? "The run this session resumed into is not one this recording ever saw. The recorder cannot " +
                  "say what happened between the decision it last watched and the state the game came back in."
                : $"The game resumed this run at decision " +
                  $"{rolledBackTo.Seq.ToString(CultureInfo.InvariantCulture)}, and the recorder had watched it " +
                  $"to decision {last.Seq.ToString(CultureInfo.InvariantCulture)}. The decisions between them " +
                  "did not happen in the run being played now, so the history is not this run's.");
        }

        if (!journal.WitnessedRunStart)
        {
            capture.Break(
                "This journal was written by a recorder that did not see the run begin, so the history it holds " +
                "does not start where the run did.");
        }

        return capture;
    }

    /// <summary>
    /// Records one decision and the settled state it left behind.
    ///
    /// The state a decision begins from is the state the one before it left, which is
    /// the same continuity rule <see cref="FightCapture"/> applies inside a fight. Out
    /// of a fight nothing is observed between two decisions, so the previous
    /// after-sample is this decision's before-sample rather than a second reading that
    /// could disagree with it.
    /// </summary>
    /// <param name="verb">Which decision the game announced.</param>
    /// <param name="args">Its arguments, as the manifest records them.</param>
    /// <param name="after">The sampled canonical state once the engine settled.</param>
    /// <param name="digest">The complete canonical state digest at that same moment.</param>
    /// <param name="runClockMs">The game's own run clock, for a person looking for the
    /// moment again in their own recording of the session.</param>
    /// <returns>The journal entry this decision produced, for the caller to append.</returns>
    public RunJournalEntry Record(
        ActionVerb verb,
        IReadOnlyDictionary<string, string> args,
        IReadOnlyDictionary<string, string> after,
        string digest,
        int? runClockMs = null)
    {
        if (State == RunCaptureState.Finished)
        {
            throw new ManifestException(
                "This run is over, so there is no decision left to record. A second run is a second recording.");
        }

        Require(
            !string.IsNullOrWhiteSpace(digest),
            "Every decision is recorded with the complete canonical state digest that followed it, because that " +
            "is what a boundary standing on it is identified by.");

        var entry = new RunJournalEntry
        {
            Seq = NextSeq,
            Verb = verb.ToString(),
            Args = Sorted(args),
            State = ReplayTrace.Sample(after),
            Digest = digest,
            RunClockMs = runClockMs,
        };

        Append(entry, verb);
        return entry;
    }

    /// <summary>
    /// The run is over.
    /// </summary>
    /// <param name="outcome">One of <see cref="NativeSource.Outcomes"/>. Giving up is
    /// <c>abandoned</c> and is a completed recording: the run is over, the history is
    /// whole, and the fights in it were really played.</param>
    public void Finish(string outcome)
    {
        if (!NativeSource.Outcomes.Contains(outcome, StringComparer.Ordinal))
        {
            throw new ManifestException(
                $"'{outcome}' is not one of the outcomes a run can end with: " +
                $"{string.Join(", ", NativeSource.Outcomes)}.");
        }

        if (State == RunCaptureState.Finished) return;

        // A fight still live when the run ended was left rather than fought to its
        // end, and FightCapture is what says a left fight has no line to project.
        _fight?.Abandon();
        _fight = null;
        Outcome = outcome;
        if (State == RunCaptureState.Recording) State = RunCaptureState.Finished;
    }

    /// <summary>
    /// The recorder could not account for the run continuously.
    ///
    /// Kept rather than discarded, and reported rather than repaired: the trace is
    /// still what was seen, and it is <see cref="Continuity"/> that says it is not
    /// this run's whole history.
    /// </summary>
    public void MarkBroken(string reason) => Break(reason);

    /// <summary>
    /// This recording, as a manifest.
    ///
    /// Available before the run ends as well as after, because the journal exists so
    /// that a crash keeps the prefix and a prefix is only useful if something can read
    /// it. What it is <em>not</em> is publishable on its own: the validator decides
    /// that, and it refuses a recording whose start nobody witnessed or whose watch
    /// has a hole in it.
    /// </summary>
    public ReplayManifest ToManifest()
    {
        var coverage = RunCoverage.Of(Trace);
        var locations = coverage.Boundaries();

        return new ReplayManifest
        {
            RunId = RunId,
            Environment = Identity.AsEnvironment(),
            Source = new SourceProvenance
            {
                Kind = "native",
                ExtractionMethod = "captured",
                Coverage = Describe(coverage),
                Native = new NativeSource
                {
                    RecorderVersion = RecorderVersion,
                    WitnessedRunStart = Fact<bool>.Captured(
                        WitnessedRunStart, FactEvidence.AtActionOrdinal(-1, Opening.RunClockMs)),
                    Continuity = Continuity,
                    Outcome = Outcome ?? NativeSource.Outcomes[2],
                },
            },
            Actions = _actions.ToList(),
            Checkpoints = Checkpoints(locations),
            Boundaries = [.. locations.Select(location => location.With(Digest(location.AfterSeq)))],
        };
    }

    /// <summary>
    /// Replays one journal entry into this capture, exactly as
    /// <see cref="Record"/> would have.
    ///
    /// A journal is the recording, so reading one back has to build the same capture
    /// the session that wrote it held - the same steps, the same per-fight delegation,
    /// the same boundary digests. A second reading here that differed would mean a
    /// resumed session published something a crashed one could not.
    /// </summary>
    private void Replay(RunJournalEntry entry)
    {
        if (!Enum.TryParse<ActionVerb>(entry.Verb, out var verb))
        {
            throw new ManifestException(
                $"This journal records a decision this build does not know: '{entry.Verb}'. Refusing to " +
                "continue a recording whose history it cannot read.");
        }

        if (entry.Seq != NextSeq)
        {
            throw new ManifestException(
                $"This journal's entries are out of order: it holds seq " +
                $"{entry.Seq.ToString(CultureInfo.InvariantCulture)} where " +
                $"{NextSeq.ToString(CultureInfo.InvariantCulture)} was expected. A gap is a missing decision " +
                "wearing a plausible face.");
        }

        Append(entry, verb);
    }

    private void Append(RunJournalEntry entry, ActionVerb verb)
    {
        var before = _steps[^1].After;
        var after = entry.State;

        _steps.Add(new ReplayStep
        {
            Seq = entry.Seq,
            Verb = entry.Verb,
            Args = entry.Args,
            Before = before,
            After = after,
        });

        _actions.Add(new ActionRecord
        {
            Seq = entry.Seq,
            Verb = verb,
            Args = entry.Args,
            Source = FactSource.Captured,
            Evidence = FactEvidence.AtActionOrdinal(entry.Seq, entry.RunClockMs),
        });

        _digests[entry.Seq] = entry.Digest;
        _clocks[entry.Seq] = entry.RunClockMs;
        _entries.Add(entry);

        DelegateToTheFight(entry, before, after);
    }

    /// <summary>
    /// Hands the inside of a fight to a <see cref="FightCapture"/>.
    ///
    /// The decision that entered the fight is not one of its actions - it is the
    /// boundary the fight begins at, which is exactly what <see cref="FightCapture"/>
    /// keeps as its own first step. Every decision after it, whatever verb it is,
    /// belongs to the fight until the state stops reading as one in progress: a card
    /// screen opened by a card is a decision made inside a fight, and a replay applies
    /// it there.
    /// </summary>
    private void DelegateToTheFight(
        RunJournalEntry entry,
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        if (_fight is null)
        {
            if (!InCombat(after)) return;

            _fight = FightCapture.Begin(
                $"{FightSourceIdPrefix}{(_fights.Count + 1).ToString(CultureInfo.InvariantCulture)}",
                after,
                entry.Digest);
            _fights.Add(_fight);
            return;
        }

        // The previous decision's after-sample is this one's before-sample, which is
        // what makes the previous step closed rather than open: nothing was observed
        // between them.
        _fight.BeginStep(entry.Verb, entry.Args, before, previousActionFinished: true);
        _fight.CompleteStep(after);

        if (_fight.State != FightCaptureState.Live) _fight = null;
    }

    /// <summary>
    /// A checkpoint at every boundary, plus one at the run's last decision.
    ///
    /// The boundary locations are <see cref="RunCoverage"/>'s answer rather than a
    /// second scan of the same trace, so a checkpoint and the boundary beside it
    /// cannot come to mean different moments. The final one is there because a run
    /// that reached no boundary still has an end, and a manifest with nothing to
    /// disagree with proves only that it ran.
    /// </summary>
    private IReadOnlyList<Checkpoint> Checkpoints(IReadOnlyList<BoundaryLocation> locations)
    {
        var checkpoints = locations
            .Select(location => new Checkpoint
            {
                Id = Name(location),
                AfterSeq = location.AfterSeq,
                Kind = location.Kind,
                Expect = Expectations(location.AfterSeq),
            })
            .ToList();

        var lastSeq = _steps[^1].Seq;
        if (checkpoints.All(checkpoint => checkpoint.AfterSeq != lastSeq))
        {
            checkpoints.Add(new Checkpoint
            {
                Id = "run-end",
                AfterSeq = lastSeq,
                Kind = RunEndCheckpointKind,
                Expect = Expectations(lastSeq),
            });
        }

        return checkpoints;
    }

    private IReadOnlyDictionary<string, Fact<string>> Expectations(int afterSeq)
    {
        var step = _steps.First(candidate => candidate.Seq == afterSeq);
        var evidence = FactEvidence.AtActionOrdinal(afterSeq, _clocks.GetValueOrDefault(afterSeq));
        return new SortedDictionary<string, Fact<string>>(
            step.After.ToDictionary(
                field => field.Key,
                field => Fact<string>.Captured(field.Value, evidence),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private Fact<string> Digest(int afterSeq) =>
        _digests.TryGetValue(afterSeq, out var digest)
            ? Fact<string>.Captured(
                digest, FactEvidence.AtActionOrdinal(afterSeq, _clocks.GetValueOrDefault(afterSeq)))
            : throw new ManifestException(
                $"This recording has no state digest after decision " +
                $"{afterSeq.ToString(CultureInfo.InvariantCulture)}, so the boundary standing there is a place " +
                "nothing established the identity of.");

    private static string Name(BoundaryLocation location) => location.Kind switch
    {
        ReplayBoundary.CombatStartKind =>
            $"fight-{location.Fight?.ToString(CultureInfo.InvariantCulture)}-start",
        ReplayBoundary.FloorEntryKind =>
            $"floor-{location.Floor?.ToString(CultureInfo.InvariantCulture)}-entry",
        ReplayBoundary.TurnStartKind =>
            $"fight-{location.Fight?.ToString(CultureInfo.InvariantCulture)}-turn-" +
            $"{location.Turn?.ToString(CultureInfo.InvariantCulture)}",
        _ => throw new ManifestException(
            $"RunCoverage produced a boundary of kind '{location.Kind}', which is not one of: " +
            $"{string.Join(", ", ReplayBoundary.Kinds)}."),
    };

    /// <summary>
    /// What this recording covers, in the words a reader of the manifest gets.
    ///
    /// Interpolated from the run rather than written per recording: a native manifest
    /// is produced by a machine, and a coverage sentence that named a particular run
    /// would be a sentence only one recording could ever carry.
    /// </summary>
    private string Describe(RunCoverage coverage)
    {
        var ending = State switch
        {
            RunCaptureState.Broken => "and the recorder's watch of it has a hole in it",
            RunCaptureState.Finished => $"and the run ended {Outcome}",
            _ => "and the run was still being played when this was written",
        };

        return
            $"The whole run as it was played: " +
            $"{_actions.Count.ToString(CultureInfo.InvariantCulture)} decision(s) from run start across " +
            $"{coverage.Floors.Count.ToString(CultureInfo.InvariantCulture)} floor(s) and " +
            $"{coverage.Fights.Count.ToString(CultureInfo.InvariantCulture)} fight(s), " +
            $"{ending}.";
    }

    private void Break(string reason)
    {
        Continuity = NativeSource.BrokenContinuity;
        Refusal = Refusal is null ? reason : $"{Refusal} {reason}";
        if (State == RunCaptureState.Recording) State = RunCaptureState.Broken;
    }

    private static IReadOnlyDictionary<string, string> Sorted(IReadOnlyDictionary<string, string> args) =>
        new SortedDictionary<string, string>(
            args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static void Require(bool condition, string refusal)
    {
        if (!condition) throw new ManifestException(refusal);
    }

    private const string InProgress = "in_progress";

    private static bool InCombat(IReadOnlyDictionary<string, string> sample) =>
        string.Equals(sample.GetValueOrDefault("combat.outcome", "none"), InProgress, StringComparison.Ordinal);

    private static int? Floor(IReadOnlyDictionary<string, string> sample) =>
        sample.TryGetValue("run.total_floor", out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>
/// Everything a recorder establishes about a run before its first decision.
///
/// Handed over whole rather than read piecemeal, because the environment identity is
/// a single claim about one moment: the run the game had just set up. A field read
/// later would be a reading of a different moment wearing the same name.
/// </summary>
public sealed record RunRecordingStart
{
    /// <summary>What every artifact of this recording is keyed by. Chosen by the
    /// caller, because inventing an identifier is not something a capture of somebody
    /// else's decisions should be doing.</summary>
    public required string RunId { get; init; }

    public required string RecorderVersion { get; init; }

    public required RunIdentityReading Identity { get; init; }

    /// <summary>The sampled canonical state of the run before any decision.</summary>
    public required IReadOnlyDictionary<string, string> State { get; init; }

    /// <summary>The complete canonical state digest at that same moment.</summary>
    public required string Digest { get; init; }

    public int? RunClockMs { get; init; }
}

/// <summary>
/// The run's identity, as a recorder read it out of the game it is running in.
///
/// The same ten values <see cref="EnvironmentIdentity"/> carries, without the
/// provenance: every one of them is <see cref="FactSource.Captured"/> by construction
/// here, and <see cref="AsEnvironment"/> is the one place that says so - a caller
/// assembling the facts itself would be a second answer waiting to mark one of them
/// observed.
/// </summary>
public sealed record RunIdentityReading
{
    [JsonPropertyName("build_version")]
    public required string BuildVersion { get; init; }

    [JsonPropertyName("build_date_utc")]
    public required string BuildDateUtc { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    [JsonPropertyName("game_mode")]
    public required string GameMode { get; init; }

    [JsonPropertyName("seed")]
    public required string Seed { get; init; }

    [JsonPropertyName("ascension")]
    public required int Ascension { get; init; }

    [JsonPropertyName("character")]
    public required string Character { get; init; }

    [JsonPropertyName("acts")]
    public required IReadOnlyList<string> Acts { get; init; }

    /// <summary>The unlock state this run was generated against, as the values the
    /// game's own state is constructed from. Exact rather than complete: a recorder
    /// reads what the player actually had, which a video never could.</summary>
    [JsonPropertyName("unlocks")]
    public required UnlockStateInventory Unlocks { get; init; }

    /// <summary>Every mod the game reported loaded, read out of each one's own
    /// manifest.</summary>
    [JsonPropertyName("mods")]
    public required ModEnvironment Mods { get; init; }

    /// <summary>Why the unlock requirement says what it does. A sentence rather than
    /// a flag, because "the recorder read it" is the whole basis and a reader deserves
    /// to be told which reading.</summary>
    public const string UnlockBasis =
        "Read out of the running game by the recorder at run start: the epochs unlocked, the encounters seen " +
        "and the runs played are the three values the game's own unlock state is constructed from, so this is " +
        "the state the run was actually generated against rather than an inference about it.";

    /// <summary>This reading as the manifest's environment identity, every field
    /// captured at the run's opening.</summary>
    public EnvironmentIdentity AsEnvironment()
    {
        var evidence = FactEvidence.AtActionOrdinal(-1);
        return new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Captured(BuildVersion, evidence),
            BuildDateUtc = Fact<string>.Captured(BuildDateUtc, evidence),
            ContentHash = Fact<string>.Captured(ContentHash, evidence),
            GameMode = Fact<string>.Captured(GameMode, evidence),
            Seed = Fact<string>.Captured(Seed, evidence),
            Ascension = Fact<int>.Captured(Ascension, evidence),
            Character = Fact<string>.Captured(Character, evidence),
            Acts = Fact<IReadOnlyList<string>>.Captured(Acts, evidence),
            Unlocks = Fact<UnlockRequirement>.Captured(
                UnlockRequirement.Exact(UnlockBasis, Unlocks), evidence),
            Mods = Fact<ModEnvironment>.Captured(Mods, evidence),
        };
    }
}
