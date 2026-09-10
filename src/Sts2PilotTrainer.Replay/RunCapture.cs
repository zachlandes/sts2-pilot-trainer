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

    /// <summary>The recorder met a decision it could not name and stopped there.
    /// <see cref="RunCapture.Stop"/> says what it met. Everything before it is kept
    /// and nothing after it is recorded.</summary>
    Unmapped,
}

/// <summary>
/// One reading of the run: the sampled canonical state and the complete digest of
/// the same instant. Handed over as a pair because the two answer different
/// questions about one moment, and a sample paired with another moment's digest
/// would be a reading nobody took.
/// </summary>
public sealed record StateReading(IReadOnlyDictionary<string, string> State, string Digest);

/// <summary>
/// A run somebody is playing, recorded decision by decision into a native manifest.
///
/// <see cref="FightCapture"/> is this for one fight, and this is the same idea over a
/// whole run: the game announces each decision, the host samples the settled state
/// after it, and the rules about what those samples mean live here rather than in the
/// mod that supplies them. It delegates the inside of a fight to a
/// <see cref="FightCapture"/> per fight, so a fight a person plays goes through one
/// capture path whether the recorded-fight journey or the recorder is watching.
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
/// did not witness. A recorder that resumes must account for the gap between sessions,
/// so <see cref="Resume"/> compares the state the game resumed into against the state
/// the journal last recorded and
/// marks <see cref="Continuity"/> broken when they differ, except when the live state
/// is the room-entry boundary of the fight the journal still held open. That is the
/// game's observed save rollback: its later fight decisions remain as discarded
/// evidence and recording resumes from the boundary. Every other mismatch stays a
/// break rather than being repaired.
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
    private readonly List<string> _refusals = [];
    private readonly List<DiscardedBranch> _discarded = [];
    private readonly List<JournalDiscardedBranch> _journalDiscarded = [];
    private readonly List<string> _journalRecords = [];
    private readonly SortedDictionary<int, JournalBookmark> _bookmarks = [];

    private FightCapture? _fight;
    private JournalStop? _stop;
    private RunCoverage? _coverage;
    private int _coveredSteps = -1;

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

    /// <summary>
    /// One of <see cref="NativeSource.Integrities"/>: whether anything happened in
    /// this run that stops the recording being published, and what.
    ///
    /// Separate from <see cref="Continuity"/> and from <see cref="State"/> because it
    /// is a different fact about a different thing. Continuity says whether the
    /// recorder watched the whole run; this says whether what it watched can be
    /// published. A run that used the console is recorded to its end, keeps every
    /// decision it made and is never publishable, so nothing there stops. A run whose
    /// recorder met a decision it could not name stops at that decision, keeps every
    /// decision before it, and is never publishable either.
    /// </summary>
    public string Integrity { get; private set; } = NativeSource.CompleteIntegrity;

    /// <summary>Where the recorder stopped, or null while it has not.</summary>
    public JournalStop? Stop => _stop;

    public RunCaptureState State { get; private set; } = RunCaptureState.Recording;

    /// <summary>Why this recording is not a continuous account of the run, or null
    /// while it is.</summary>
    public string? Refusal => _refusals.Count == 0 ? null : string.Join(" ", _refusals);

    /// <summary>Every refusal raised against this recording, in the order they were
    /// raised. One per line of the journal, so a later session is told about each of
    /// them rather than about one sentence they were joined into.</summary>
    public IReadOnlyList<string> Refusals => _refusals;

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

    /// <summary>Observed fight branches removed by the game's room-entry rollback.</summary>
    public IReadOnlyList<DiscardedBranch> Discarded => _discarded;

    /// <summary>The rollback line a caller must append while attaching, if any.</summary>
    public string? ResumptionRecord { get; private set; }

    /// <summary>The journal as it stands: the header, the opening reading, one entry
    /// per decision and one line per refusal. What a crash leaves behind.</summary>
    public RunJournal Journal => new()
    {
        SchemaId = RunJournal.Schema,
        RunId = RunId,
        RecorderVersion = RecorderVersion,
        Identity = Identity,
        WitnessedRunStart = WitnessedRunStart,
        Entries = [Opening, .. _entries],
        Refusals = _refusals.ToList(),
        NonStandard = _nonStandard,
        Stop = _stop,
        Discarded = _journalDiscarded.ToList(),
        Bookmarks = _bookmarks.Values.ToList(),
        SerializedRecords = _journalRecords.ToList(),
    };

    private bool _nonStandard;

    /// <summary>Whether the player has this fight bookmarked right now.</summary>
    public bool IsBookmarked(int fight) => _bookmarks.TryGetValue(fight, out var mark) && mark.On;

    /// <summary>
    /// What this history covers, derived once per recorded decision.
    ///
    /// The step list only grows - a rollback rebuilds the capture rather than
    /// shortening it - so the step count is what makes a derivation stale, and every
    /// reader here shares one pass. A per-frame surface asks two of these readers.
    /// </summary>
    private RunCoverage Coverage
    {
        get
        {
            if (_coverage is null || _coveredSteps != _steps.Count)
            {
                _coverage = RunCoverage.Of(Trace);
                _coveredSteps = _steps.Count;
            }

            return _coverage;
        }
    }

    /// <summary>
    /// The last fight this recording finished, or null while it has finished none or
    /// is still inside one.
    ///
    /// Read off the same coverage the boundaries come from, so the fight it names is
    /// one with a combat_start and an ordinal a bookmark can key on. Whether the run has
    /// since left that fight's floor is <see cref="MovedOnFromLastFight"/>, the other
    /// half of the one moment a bookmark can be pressed: from a fight's last action to
    /// the map move that leaves its floor, which covers the loot screen and the card
    /// screen behind it on a win and the game's ending on a loss, where the run never
    /// moves on.
    /// </summary>
    public int? LastEndedFight => Coverage.Fights.LastOrDefault() is { Finished: true } fight
        ? fight.Fight
        : null;

    /// <summary>Whether a floor was entered after <see cref="LastEndedFight"/> ended.
    /// False where there is no such fight.</summary>
    public bool MovedOnFromLastFight
    {
        get
        {
            var coverage = Coverage;
            return coverage.Fights.LastOrDefault() is { Finished: true } fight &&
                   coverage.Floors.Any(floor => floor.EnteredAfterSeq > fight.EndSeq);
        }
    }

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

        var capture = new RunCapture(start, witnessedRunStart: true, NativeSource.ContinuousContinuity, opening);
        capture._journalRecords.Add(RunJournal.RenderEntry(opening));
        return capture;
    }

    /// <summary>
    /// Picks a run back up from the journal a previous session left behind.
    ///
    /// The game saves a run when a room is entered, so a session continued from the
    /// game's own save resumes at the last such point. Whether that is where this
    /// recorder stopped watching is the question: the journal's last entry carries the
    /// complete state digest of the moment it was written, and the live game carries
    /// the digest of the moment it resumed into. Equal means nothing happened in
    /// between that the recorder missed. A return to the entry of the fight the journal
    /// still holds open is the game's observed save rollback, so the unwound decisions
    /// are marked discarded and the replayable history resumes there. Anything else is
    /// marked broken rather than repaired.
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
        capture._journalDiscarded.AddRange(journal.Discarded);
        capture._discarded.AddRange(journal.Discarded.Select(ToDiscardedBranch));

        // A stop is replayed into the same stopped state, before the digest is
        // compared: the recording ends where the recorder stopped, and the run the
        // game came back in is past that point by exactly the decision nothing could
        // name. That is not a hole in the watch, and it is not compared as one.
        if (journal.Stop is { } stop) capture.StopAt(stop);

        // A refusal an earlier session raised is still a hole in this recording's
        // account of the run, and is the reason it is on the file at all: the digest
        // comparison below can only see what happened since the journal's last entry,
        // so a session that recorded on past its own break would otherwise resume as
        // continuous.
        foreach (var reason in journal.Refusals) capture.Break(reason);

        // Same reasoning, for the same reason: the console having been used in this
        // run is a fact about it that no later reading of the live game could recover,
        // and a session that resumed without it would publish a run the console had
        // been used in.
        if (journal.NonStandard) capture.MarkNonStandard();

        // Each press stands as the file holds it, including one taken off: the line
        // is on the file either way, and the manifest emits only what is on.
        foreach (var bookmark in journal.Bookmarks) capture._bookmarks[bookmark.Fight] = bookmark;

        var last = capture._entries.Count > 0 ? capture._entries[^1] : journal.Opening;
        if (capture._stop is null && !string.Equals(last.Digest, liveDigest, StringComparison.Ordinal))
        {
            var rolledBackTo = capture.Journal.Entries
                .LastOrDefault(entry => string.Equals(entry.Digest, liveDigest, StringComparison.Ordinal));
            if (rolledBackTo is not null && capture.IsObservedFightRollback(rolledBackTo))
            {
                capture = capture.RollBack(rolledBackTo);
            }
            else
            {
                capture.Break(rolledBackTo is null
                    ? "The run this session resumed into is not one this recording ever saw. The recorder cannot " +
                      "say what happened between the decision it last watched and the state the game came back in."
                    : $"The game resumed this run at decision " +
                      $"{rolledBackTo.Seq.ToString(CultureInfo.InvariantCulture)}, and the recorder had watched it " +
                      $"to decision {last.Seq.ToString(CultureInfo.InvariantCulture)}. This is not the game's " +
                      "rollback of a live fight to its room-entry boundary, so the recorder cannot account for it.");
            }
        }

        if (!journal.WitnessedRunStart)
        {
            capture.Break(
                "This journal was written by a recorder that did not see the run begin, so the history it holds " +
                "does not start where the run did.");
        }

        capture._journalRecords.Clear();
        capture._journalRecords.AddRange(journal.SerializedRecords ??
            journal.Entries.Select(RunJournal.RenderEntry));
        if (capture.ResumptionRecord is { } resumption) capture._journalRecords.Add(resumption);
        foreach (var reason in capture.Refusals.Skip(journal.Refusals.Count))
        {
            capture._journalRecords.Add(RunJournal.RenderRefusal(reason));
        }

        return capture;
    }

    /// <summary>
    /// True only for the game's observed save behavior: the recording ended in a
    /// live fight and the resumed digest is that same fight's entry decision.
    /// </summary>
    private bool IsObservedFightRollback(RunJournalEntry target)
    {
        var coverage = Coverage;
        var roomEntry = coverage.Floors.LastOrDefault();
        var fight = roomEntry is null ? null : coverage.FightsOn(roomEntry).LastOrDefault();
        return fight is { Finished: false } && roomEntry!.EnteredAfterSeq == target.Seq &&
               _entries.Any(entry => entry.Seq > target.Seq);
    }

    private RunCapture RollBack(RunJournalEntry target)
    {
        var removed = _entries.Where(entry => entry.Seq > target.Seq).ToList();
        var rollback = new JournalRollback
        {
            RollbackToSeq = target.Seq,
            RollbackToDigest = target.Digest,
            DiscardedFromSeq = removed[0].Seq,
            DiscardedThroughSeq = removed[^1].Seq,
        };

        var rebuilt = new RunCapture(
            new RunRecordingStart
            {
                RunId = RunId,
                RecorderVersion = RecorderVersion,
                Identity = Identity,
                State = Opening.State,
                Digest = Opening.Digest,
                RunClockMs = Opening.RunClockMs,
            },
            WitnessedRunStart,
            Continuity,
            Opening);
        foreach (var entry in _entries.Where(entry => entry.Seq <= target.Seq)) rebuilt.Replay(entry);
        rebuilt._discarded.AddRange(_discarded);
        rebuilt._journalDiscarded.AddRange(_journalDiscarded);
        var discarded = new JournalDiscardedBranch(rollback, target, removed);
        rebuilt._journalDiscarded.Add(discarded);
        rebuilt._discarded.Add(ToDiscardedBranch(discarded));
        foreach (var refusal in _refusals) rebuilt.Break(refusal);
        if (_nonStandard)
        {
            rebuilt._nonStandard = true;
            rebuilt.Integrity = NativeSource.NonStandardIntegrity;
        }

        // A rollback discards a fight still open, and a bookmark is only ever on a
        // fight that finished, so every one of them is on the far side of the boundary
        foreach (var (fight, bookmark) in _bookmarks) rebuilt._bookmarks[fight] = bookmark;

        rebuilt.ResumptionRecord = RunJournal.RenderRollback(rollback);
        return rebuilt;
    }

    private static DiscardedBranch ToDiscardedBranch(JournalDiscardedBranch branch) => new()
    {
        RollbackToSeq = branch.Rollback.RollbackToSeq,
        RollbackToDigest = branch.Rollback.RollbackToDigest,
        Actions = branch.Entries.Select(entry =>
        {
            if (!Enum.TryParse<ActionVerb>(entry.Verb, out var verb))
            {
                throw new ManifestException($"A discarded journal decision names unknown verb '{entry.Verb}'.");
            }

            return new ActionRecord
            {
                Seq = entry.Seq,
                Verb = verb,
                Args = entry.Args,
                Source = FactSource.Captured,
                Evidence = FactEvidence.AtActionOrdinal(entry.Seq, entry.RunClockMs),
            };
        }).ToList(),
        Trace = new ReplayTrace
        {
            Steps = branch.Entries.Prepend(branch.Boundary).Select(entry => new ReplayStep
            {
                Seq = entry.Seq,
                Verb = entry.Verb,
                Args = entry.Args,
                Before = entry.Before ?? entry.State,
                After = entry.State,
            }).ToList(),
        },
    };

    /// <summary>
    /// Records one decision, the state it began from, and the settled state it left
    /// behind.
    ///
    /// Both readings are the recorder's own. Inside a fight they coincide with the
    /// rule <see cref="FightCapture"/> applies - the state a decision begins from is
    /// the state the one before it left - and the fight's capture still refuses a
    /// gap between them. Out of a fight a gap between the previous after-reading and
    /// this before-reading is not refused here, because the reward screen after a
    /// fight is generated on the client's own clock between the two, and the
    /// before-reading is what a comparison at verification reads.
    /// </summary>
    /// <param name="verb">Which decision the game announced.</param>
    /// <param name="args">Its arguments, as the manifest records them.</param>
    /// <param name="before">The sampled canonical state and digest the decision began from.</param>
    /// <param name="after">The sampled canonical state and digest once the engine settled.</param>
    /// <param name="runClockMs">The game's own run clock, for a person looking for the
    /// moment again in their own recording of the session.</param>
    /// <returns>The journal entry this decision produced, for the caller to append.</returns>
    public RunJournalEntry Record(
        ActionVerb verb,
        IReadOnlyDictionary<string, string> args,
        StateReading before,
        StateReading after,
        int? runClockMs = null)
    {
        if (State == RunCaptureState.Finished)
        {
            throw new ManifestException(
                "This run is over, so there is no decision left to record. A second run is a second recording.");
        }

        if (_stop is { } stop)
        {
            throw new ManifestException(
                $"This recording stopped at decision {stop.Decision.Seq.ToString(CultureInfo.InvariantCulture)}, " +
                $"which the recorder could not name ({ManifestValidator.Describe(stop.Decision)}). Nothing is " +
                "recorded past a stop: a history that skipped a decision and carried on would replay into a " +
                "run that never made it.");
        }

        Require(
            !string.IsNullOrWhiteSpace(after.Digest),
            "Every decision is recorded with the complete canonical state digest that followed it, because that " +
            "is what a boundary standing on it is identified by.");
        Require(
            !string.IsNullOrWhiteSpace(before.Digest),
            "Every decision is recorded with the complete canonical state digest it began from, because that " +
            "is the instant a comparison at verification reads.");

        var entry = new RunJournalEntry
        {
            Seq = NextSeq,
            Verb = verb.ToString(),
            Args = Sorted(args),
            Before = ReplayTrace.Sample(before.State),
            BeforeDigest = before.Digest,
            State = ReplayTrace.Sample(after.State),
            Digest = after.Digest,
            RunClockMs = runClockMs,
        };

        Append(entry, verb);
        _journalRecords.Add(RunJournal.RenderEntry(entry));
        return entry;
    }

    /// <summary>
    /// The recorder met a decision it could not name, and stops here.
    ///
    /// Everything before it is kept; nothing after it is recorded, and a
    /// <see cref="Record"/> that follows is refused rather than written. The
    /// recording is never publishable - a prefix that stops short replays into a run
    /// that never made the next decision - and <see cref="Integrity"/> says so.
    /// <see cref="Continuity"/> is untouched, because the watch had no hole: the
    /// recorder saw the decision and could not say what it was.
    /// </summary>
    /// <param name="decision">What was met, raw and uninterpreted.</param>
    /// <param name="before">The reading of the state the decision began from, which
    /// is the state the recording ends in.</param>
    /// <returns>The journal line to append for it, so the stop survives this session
    /// the same way a decision does.</returns>
    public string MarkUnmapped(UnmappedDecision decision, StateReading before)
    {
        if (State == RunCaptureState.Finished)
        {
            throw new ManifestException(
                "This run is over, so there is no decision left for the recorder to have stopped at.");
        }

        if (_stop is not null)
        {
            throw new ManifestException(
                "This recording has already stopped. A recorder stops once, at the first decision it " +
                "cannot name.");
        }

        if (decision.Seq != NextSeq)
        {
            throw new ManifestException(
                $"The recorder stopped at decision {decision.Seq.ToString(CultureInfo.InvariantCulture)} and " +
                $"the next decision would be {NextSeq.ToString(CultureInfo.InvariantCulture)}. A stop stands " +
                "at the decision after the last one recorded.");
        }

        var stop = new JournalStop(decision, ReplayTrace.Sample(before.State), before.Digest);
        StopAt(stop);
        var line = RunJournal.RenderStop(stop);
        _journalRecords.Add(line);
        return line;
    }

    private void StopAt(JournalStop stop)
    {
        _stop = stop;
        Integrity = NativeSource.UnmappedIntegrity;
        if (State == RunCaptureState.Recording) State = RunCaptureState.Unmapped;
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
    /// <returns>The journal line to append for it, so the refusal survives this
    /// session the same way a decision does.</returns>
    public string MarkBroken(string reason)
    {
        Break(reason);
        var line = RunJournal.RenderRefusal(reason);
        _journalRecords.Add(line);
        return line;
    }

    /// <summary>
    /// This run was not played entirely by the game's own rules.
    ///
    /// The run is kept, whole, and recorded to its end: it is what the player played
    /// and it is theirs. What it is not is publishable, and
    /// <see cref="Integrity"/> is what says so - whatever changed the state is not
    /// among the decisions this history holds, so replaying the history reconstructs a
    /// different run while every value in it is individually true.
    ///
    /// Which of the things that make a run one of these happened is not recorded here
    /// and is not in the field: this is the mark more than one caller writes, and a
    /// caller that knows its own cause says so in its own log line.
    ///
    /// It marks and never stops, which is the difference between this and
    /// <see cref="MarkBroken"/>: a broken watch is a recording that cannot account for
    /// the run, and this is a complete account of a run nobody may publish.
    ///
    /// Nothing the player typed is kept, here or on the file. Which of the game's
    /// commands change a run is not a judgement this class is in a position to make,
    /// and what a recording states is <see cref="Integrity"/> and nothing else - so a
    /// mark is a statement about the run rather than a record of what was entered in
    /// it, and marking twice says the same thing as marking once.
    /// </summary>
    /// <returns>The journal line to append for it, so the mark survives this session
    /// the same way a decision does.</returns>
    public string MarkNonStandard()
    {
        var line = RunJournal.RenderNonStandard();
        if (!_nonStandard) _journalRecords.Add(line);
        _nonStandard = true;
        // A stopped recording stays 'unmapped': the stop names what was met and the
        // validator reads its entries beside that integrity, and either value refuses
        // publication. The mark is still on the journal for a later reader.
        if (_stop is null) Integrity = NativeSource.NonStandardIntegrity;
        return line;
    }

    /// <summary>
    /// The player marked a fight as worth attention, or took the mark off again.
    ///
    /// Only a fight the recording holds and finishes can be marked, because nothing
    /// could have pressed it otherwise: the control exists from a fight's end to the
    /// run moving on. Pressing after the run has ended is allowed - a lost fight is
    /// bookmarked on the game's death screen, which is drawn after the manifest was
    /// written - and the caller that owns the file writes it again. Every press is a
    /// line, so a mark taken off is on the file as what happened and the last line
    /// per fight is what the manifest emits.
    ///
    /// The press is placed at the last decision recorded, and takes that decision's
    /// own run clock: on a loss the game's run is no longer in progress by the time the
    /// death screen is drawn, so a clock read at the press is no reading at all. Both
    /// halves of the evidence then name the same instant on the win path and the loss
    /// path alike.
    /// </summary>
    /// <returns>The journal line to append for it, so the press survives this session
    /// the same way a decision does.</returns>
    /// <exception cref="ManifestException">When the fight is not one this recording
    /// has finished.</exception>
    public string MarkBookmark(int fight, bool on)
    {
        var finished = Coverage.Fights.FirstOrDefault(candidate => candidate.Fight == fight);
        if (finished is not { Finished: true })
        {
            throw new ManifestException(
                $"Fight {fight.ToString(CultureInfo.InvariantCulture)} is not one this recording has " +
                "finished, so there is no moment at which it could have been bookmarked.");
        }

        var afterSeq = _entries.Count == 0 ? -1 : _entries[^1].Seq;
        var bookmark = new JournalBookmark
        {
            Fight = fight,
            On = on,
            AfterSeq = afterSeq,
            RunClockMs = _clocks.GetValueOrDefault(afterSeq),
        };
        _bookmarks[fight] = bookmark;
        var line = RunJournal.RenderBookmark(bookmark);
        _journalRecords.Add(line);
        return line;
    }

    /// <summary>
    /// This recording, as a manifest.
    ///
    /// Only once the run has ended, because a manifest says how it ended and that is
    /// not a value anything here may guess: a defaulted outcome would have a crashed
    /// session's prefix claim the player gave the run up, indistinguishable from one
    /// who did. A caller that wants a manifest of a run still being played calls
    /// <see cref="Finish"/> with the outcome it means. What the journal holds is the
    /// crash-surviving form, and it needs no outcome to be read.
    ///
    /// What this is <em>not</em> is publishable on its own: the validator decides that,
    /// and it refuses a recording whose start nobody witnessed or whose watch has a
    /// hole in it.
    /// </summary>
    /// <exception cref="ManifestException">When the run has not ended.</exception>
    public ReplayManifest ToManifest()
    {
        if (Outcome is null)
        {
            throw new ManifestException(
                "This run has not ended, so nothing can say how it ended. A manifest records won, lost or " +
                "abandoned, and the recorder that reads the run's end is the only thing that knows which.");
        }

        var coverage = Coverage;
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
                    Outcome = Outcome,
                    Integrity = Integrity,
                    Unmapped = _stop is { } stop ? [stop.Decision] : null,
                    Discarded = _discarded.Count == 0 ? null : _discarded.ToList(),
                    Bookmarks = Bookmarks(),
                },
            },
            Actions = _actions.ToList(),
            Checkpoints = Checkpoints(locations),
            Boundaries = [.. locations.Select(location => location.With(Digest(location.AfterSeq)))],
        };
    }

    /// <summary>The marks that are on, as declared facts, or null where none is.</summary>
    private IReadOnlyList<FightBookmark>? Bookmarks()
    {
        var on = _bookmarks.Values.Where(bookmark => bookmark.On).Select(bookmark => new FightBookmark
        {
            Fight = bookmark.Fight,
            Bookmarked = new Fact<bool>(
                true, FactSource.Declared, FactEvidence.AtActionOrdinal(bookmark.AfterSeq, bookmark.RunClockMs)),
        }).ToList();
        return on.Count == 0 ? null : on;
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
        // The entry's own reading rather than the previous after-sample: the two
        // coincide inside a fight and may not outside one, and the journal carries
        // the reading that was taken rather than the one that was assumed.
        var before = entry.Before
            ?? throw new ManifestException(
                $"Decision {entry.Seq.ToString(CultureInfo.InvariantCulture)} carries no reading of the state " +
                "it began from.");
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
            RunCaptureState.Unmapped => "and the recorder stopped at a decision it could not name",
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
        _refusals.Add(reason);
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
