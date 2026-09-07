using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A recording as it is being made: a header and one line per decision, appended as
/// the run is played.
///
/// A manifest is written once, at the end. A run takes an hour and a game can crash,
/// so the recording is also kept in a form that survives one - and the form that
/// survives is the one where finishing a write means finishing a line. Every line is
/// self-contained and the file is only ever appended to, so a crash leaves a prefix
/// that is a real recording of the part of the run that happened, rather than half of
/// a document that describes all of it.
///
/// It carries exactly what <see cref="RunCapture"/> needs to be rebuilt: the run's
/// identity, whether its start was witnessed, and each decision with the sampled
/// state and complete state digest either side of it. <see cref="RunCapture.Resume"/>
/// is the only reader that matters, and it rebuilds the capture rather than reading
/// the journal as a result, preserving the replayable history and any discarded
/// branch evidence established by a verified mid-fight rollback.
///
/// The digests are why this is not merely a log. They are what lets a resumed session
/// ask whether the run the game came back in is the run this journal describes,
/// which is a question no amount of replaying the history could answer.
/// </summary>
public sealed record RunJournal
{
    /// <summary>
    /// Version 2 samples the state before every decision as well as after it. A
    /// version-1 journal is a run in progress on a player's disk under an older mod,
    /// and it is refused exactly as any other schema this build does not read is:
    /// the recorder's resume path refuses a journal it cannot parse rather than
    /// repairing it, so the run is simply not continued as a recording.
    /// </summary>
    public const string Schema = "sts2-pilot-trainer/run-journal/v2";

    /// <summary>The file extension for a journal, so the store's entries say what they
    /// are. JSON Lines rather than JSON, because appending to a JSON document means
    /// rewriting it.</summary>
    public const string FileExtension = ".journal.jsonl";

    public required string SchemaId { get; init; }

    public required string RunId { get; init; }

    public required string RecorderVersion { get; init; }

    public required RunIdentityReading Identity { get; init; }

    /// <summary>Whether the recorder that opened this journal saw the run begin.
    /// Carried in the header rather than derived, because it is a fact about that
    /// session and no later one can establish it.</summary>
    public required bool WitnessedRunStart { get; init; }

    /// <summary>Every entry in the order it was appended: the opening reading first,
    /// then one per decision.</summary>
    public required IReadOnlyList<RunJournalEntry> Entries { get; init; }

    /// <summary>
    /// Every refusal the sessions that wrote this journal raised, in the order they
    /// raised them.
    ///
    /// On the file rather than derived, because a broken watch is the one fact about a
    /// recording nothing downstream can establish. A session that resumed into an
    /// unaccounted state, or that could not read a decision it saw, knows the history
    /// has a hole in it; the session after it would read a journal whose last digest
    /// matches the live one and publish that hole as a continuous account of the run.
    /// </summary>
    public IReadOnlyList<string> Refusals { get; init; } = [];

    /// <summary>
    /// Whether the sessions that wrote this journal saw the console used in this run.
    ///
    /// On the file for the same reason the refusals are: what the console did to the
    /// run is not among the decisions this journal holds, so no later reading of the
    /// live game could recover it. A session that resumed without it would publish a
    /// run the console had been used in, with every value in the recording true.
    ///
    /// A statement about the run rather than a record of what was typed in it, so it
    /// is one flag however many mark lines the file carries and no player-typed text
    /// is ever persisted.
    ///
    /// What an older build makes of one of those lines depends on where it is, and
    /// only one of the two answers is any good. With a record after it, that build
    /// refuses the journal, which is the right way round: it cannot tell that this run
    /// is unpublishable, and refusing to read it is how it says so. As the final line -
    /// the ordinary shape when the console is used and the player then quits to the
    /// menu without deciding anything else - it is indistinguishable to that build from
    /// an append a crash cut short, so its truncation rule drops the line and it resumes
    /// the run as complete. That is a real limit of what an older build can be told,
    /// not a guarantee.
    /// </summary>
    public bool NonStandard { get; init; }

    /// <summary>
    /// The decision the recorder stopped at, or null while it has not.
    ///
    /// On the file for the reason the refusals and the mark are: a stop only the
    /// running session knows about is one a crash takes with it, and the session
    /// after it would carry on recording as if the decision between had not happened.
    /// It carries the sample and digest of the state the recorder was reading when it
    /// stopped, which is the state the unmapped decision began from.
    /// </summary>
    public JournalStop? Stop { get; init; }

    /// <summary>Branches explicitly removed by observed room-entry rollbacks.</summary>
    public IReadOnlyList<JournalDiscardedBranch> Discarded { get; init; } = [];

    /// <summary>The journal's body in append order, retained across a resume.</summary>
    internal IReadOnlyList<string>? SerializedRecords { get; init; }

    /// <summary>The reading taken before any decision.</summary>
    public RunJournalEntry Opening => Entries[0];

    /// <summary>The decisions, without the opening reading.</summary>
    public IEnumerable<RunJournalEntry> Decisions => Entries.Skip(1);

    /// <summary>The header line, written once when the journal is opened.</summary>
    public string RenderHeader() =>
        JsonSerializer.Serialize(
            new JournalHeader
            {
                SchemaId = SchemaId,
                RunId = RunId,
                RecorderVersion = RecorderVersion,
                Identity = Identity,
                WitnessedRunStart = WitnessedRunStart,
            },
            Compact) + "\n";

    /// <summary>One entry, as the line appended for it.</summary>
    public static string RenderEntry(RunJournalEntry entry) =>
        JsonSerializer.Serialize(entry, Compact) + "\n";

    /// <summary>One refusal, as the line appended for it. Appended the moment it is
    /// raised, for the same reason a decision is: a refusal only a running session
    /// knows about is one the session after it cannot be told.</summary>
    public static string RenderRefusal(string reason) =>
        JsonSerializer.Serialize(new JournalRefusal { Reason = reason }, Compact) + "\n";

    /// <summary>The mark that says the console was used in this run, as the line
    /// appended for it. Appended the moment it is seen, for the same reason a refusal
    /// is, and it carries the mark and nothing else.</summary>
    public static string RenderNonStandard() =>
        JsonSerializer.Serialize(new JournalNonStandard { NonStandard = true }, Compact) + "\n";

    /// <summary>The stop, as the line appended for it. Appended the moment the
    /// recorder stops, before it does anything else, so the session after a crash is
    /// told where the recording ends and why.</summary>
    public static string RenderStop(JournalStop stop) =>
        JsonSerializer.Serialize(
            new JournalUnmapped
            {
                Entry = stop.Decision,
                Before = stop.Before,
                BeforeDigest = stop.BeforeDigest,
            },
            Compact) + "\n";

    /// <summary>The append-only receipt for an observed room-entry rollback.</summary>
    public static string RenderRollback(JournalRollback rollback) =>
        JsonSerializer.Serialize(new JournalRollbackLine { Rollback = rollback }, Compact) + "\n";

    /// <summary>The whole journal as it would be on disk. For a caller writing one in
    /// a single pass; a recorder appends instead.</summary>
    public string Render() => SerializedRecords is { } records
        ? RenderHeader() + string.Concat(records)
        : RenderHeader() +
          string.Concat(Entries.Select(RenderEntry)) +
          string.Concat(Refusals.Select(RenderRefusal)) +
          (NonStandard ? RenderNonStandard() : string.Empty) +
          (Stop is { } stop ? RenderStop(stop) : string.Empty);

    /// <summary>
    /// The journal brought back to a boundary an append may follow, or null when it is
    /// already on one.
    ///
    /// An append the process did not survive leaves a final line with no newline after
    /// it, and appending onto that line fuses the next record into it. The fused line
    /// is last, so the session that wrote it can still resume; the session after it
    /// cannot, because one more decision leaves the unreadable line in the middle,
    /// where <see cref="Parse"/>'s truncation rule does not apply and the whole journal
    /// is refused. A crash that cost one decision would then cost every decision after
    /// it.
    ///
    /// Whether that final line is a record or a fragment is <see cref="ReadsAsARecord"/>'s
    /// question, which is the same question <see cref="Parse"/> asks of it - one rule,
    /// so the repair cannot decide a line was lost that the reader kept. A record that
    /// lost only its newline is terminated; a fragment is cut back to where it began.
    /// Nothing before it is touched: the prefix is the recording, and it comes back
    /// byte for byte.
    /// </summary>
    public static JournalRepair? RepairTruncatedTail(string text)
    {
        var lastLineStart = text.LastIndexOf('\n') + 1;
        var lastLine = text[lastLineStart..];
        if (lastLine.Length == 0) return null;

        if (ReadsAsARecord(lastLine)) return new JournalRepair(text + "\n", LostARecord: false);

        return lastLineStart == 0 ? null : new JournalRepair(text[..lastLineStart], LostARecord: true);
    }

    /// <summary>
    /// A journal repaired to a boundary, and whether doing so cost a record.
    ///
    /// The two are different things to say about a recording and only one of them
    /// breaks it: a record that lost its newline is still a decision this journal
    /// holds, while a fragment cut back is a decision nobody has.
    /// </summary>
    public readonly record struct JournalRepair(string Text, bool LostARecord);

    /// <summary>
    /// Reads one line as the record it is, or hands back why it is not one.
    ///
    /// The one place that decides whether a line of a journal is a record. Both the
    /// reader and the repair ask it about a file's final line, and they have to agree:
    /// two rules for "this journal was cut short" would sooner or later disagree about
    /// some shape, and on that shape one of them would be destroying what the other
    /// kept.
    /// </summary>
    private static Exception? ReadRecord(
        string line, out RunJournalEntry? entry, out string? refusal, out bool nonStandard,
        out JournalStop? stop, out JournalRollback? rollback)
    {
        entry = null;
        refusal = null;
        nonStandard = false;
        stop = null;
        rollback = null;
        try
        {
            if (JsonSerializer.Deserialize<JournalRefusal>(line, Compact) is { Reason: not null } read)
            {
                refusal = read.Reason;
                return null;
            }

            if (JsonSerializer.Deserialize<JournalNonStandard>(line, Compact) is { NonStandard: not null } mark)
            {
                nonStandard = mark.NonStandard.Value;
                return null;
            }

            if (JsonSerializer.Deserialize<JournalUnmapped>(line, Compact) is { Entry: not null } stopped)
            {
                if (stopped.Before is null || stopped.BeforeDigest is null)
                {
                    throw new ManifestException(
                        "A run journal's stop carries no reading of the state the recorder stopped in.");
                }

                ManifestJson.ValidateRequiredMembers(stopped.Entry, "Run journal stop");
                stop = new JournalStop(stopped.Entry, stopped.Before, stopped.BeforeDigest);
                return null;
            }

            if (JsonSerializer.Deserialize<JournalRollbackLine>(line, Compact) is { Rollback: not null } rewound)
            {
                ManifestJson.ValidateRequiredMembers(rewound.Rollback, "Run journal rollback");
                rollback = rewound.Rollback;
                return null;
            }

            var value = JsonSerializer.Deserialize<RunJournalEntry>(line, Compact)
                ?? throw new ManifestException("A run journal entry deserialized to null.");
            ManifestJson.ValidateRequiredMembers(value, "Run journal entry");
            entry = value;
            return null;
        }
        catch (Exception exception) when (
            exception is JsonException or ManifestException or InvalidOperationException)
        {
            return exception;
        }
    }

    private static bool ReadsAsARecord(string line) =>
        line.Trim().Length > 0 && ReadRecord(line, out _, out _, out _, out _, out _) is null;

    /// <summary>
    /// Reads a journal back, refusing one this build cannot faithfully interpret.
    ///
    /// A truncated final line is the expected shape of a crash and is dropped rather
    /// than refused: an append that did not finish is a decision that was not
    /// recorded, and the prefix before it is a real recording. Anything else wrong is
    /// a refusal, because a journal read partially would resume a run at a point the
    /// recorder never actually reached.
    /// </summary>
    public static RunJournal Parse(string text)
    {
        var lines = text.Split('\n').Where(line => line.Trim().Length > 0).ToList();
        if (lines.Count == 0)
        {
            throw new ManifestException("This run journal is empty, so it says nothing about any run.");
        }

        var header = ManifestJson.RefuseInvalidJson("Run journal header", () =>
        {
            var value = JsonSerializer.Deserialize<JournalHeader>(lines[0], Compact)
                ?? throw new ManifestException("Run journal header deserialized to null.");
            ManifestJson.ValidateRequiredMembers(value, "Run journal header");
            return value;
        });

        if (!string.Equals(header.SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"This run journal declares schema '{header.SchemaId}', and this build reads '{Schema}'. " +
                "Refusing rather than reading it partially.");
        }

        var entries = new List<RunJournalEntry>();
        var refusals = new List<string>();
        var discarded = new List<JournalDiscardedBranch>();
        var nonStandard = false;
        JournalStop? stop = null;
        for (var index = 1; index < lines.Count; index++)
        {
            if (ReadRecord(
                    lines[index], out var entry, out var refusal, out var marked, out var stopped, out var rollback)
                is { } unreadable)
            {
                // The last line of a file a crash interrupted. Everything before it
                // finished being written and is a real recording of what happened.
                if (index == lines.Count - 1) break;
                throw unreadable;
            }

            if (refusal is not null) refusals.Add(refusal);
            else if (rollback is not null)
            {
                var boundaryIndex = entries.FindLastIndex(candidate =>
                    candidate.Seq == rollback.RollbackToSeq &&
                    string.Equals(candidate.Digest, rollback.RollbackToDigest, StringComparison.Ordinal));
                if (boundaryIndex < 0 || boundaryIndex == entries.Count - 1)
                {
                    throw new ManifestException(
                        "A run journal's rollback does not name an earlier decision and digest with decisions " +
                        "after it. The recorder only writes one after observing the resumed run at that boundary.");
                }

                var trace = new ReplayTrace
                {
                    Steps = entries.Select(candidate => new ReplayStep
                    {
                        Seq = candidate.Seq,
                        Verb = candidate.Verb,
                        Args = candidate.Args,
                        Before = candidate.Before ?? candidate.State,
                        After = candidate.State,
                    }).ToList(),
                };
                var coverage = RunCoverage.Of(trace);
                var roomEntry = coverage.Floors.LastOrDefault();
                var fight = roomEntry is null ? null : coverage.FightsOn(roomEntry).LastOrDefault();
                if (fight is not { Finished: false } || roomEntry!.EnteredAfterSeq != rollback.RollbackToSeq)
                {
                    throw new ManifestException(
                        "A run journal's rollback is not to the room-entry decision before the fight its history " +
                        "still held open. Only the game's observed mid-fight save rollback is continuous.");
                }

                var removed = entries.Skip(boundaryIndex + 1).ToList();
                if (removed[0].Seq != rollback.DiscardedFromSeq || removed[^1].Seq != rollback.DiscardedThroughSeq)
                {
                    throw new ManifestException(
                        "A run journal's rollback does not name exactly the decisions it discards.");
                }

                discarded.Add(new JournalDiscardedBranch(rollback, entries[boundaryIndex], removed));
                entries.RemoveRange(boundaryIndex + 1, removed.Count);
            }
            else if (stopped is not null)
            {
                if (stop is not null)
                {
                    throw new ManifestException(
                        "This run journal says the recorder stopped twice. A recorder stops once, at the first " +
                        "decision it cannot name, so a second stop is a line nothing wrote in order.");
                }

                stop = stopped;
            }
            else if (entry is null) nonStandard |= marked;
            else
            {
                if (stop is not null)
                {
                    throw new ManifestException(
                        $"This run journal holds decision {entry.Seq.ToString(CultureInfo.InvariantCulture)} " +
                        "after the line saying the recorder stopped. Nothing is recorded past a stop, so a " +
                        "decision there is one nothing watched.");
                }

                entries.Add(entry);
            }
        }

        var journal = new RunJournal
        {
            SchemaId = header.SchemaId,
            RunId = header.RunId,
            RecorderVersion = header.RecorderVersion,
            Identity = header.Identity,
            WitnessedRunStart = header.WitnessedRunStart,
            Entries = entries,
            Refusals = refusals,
            NonStandard = nonStandard,
            Stop = stop,
            Discarded = discarded,
            SerializedRecords = NormalizeRecords(lines.Skip(1)),
        };
        journal.RequireReadable();
        return journal;
    }

    /// <summary>
    /// Everything that must hold before a journal can be resumed from or published.
    ///
    /// Checked here rather than at each reader: a journal whose first entry is not the
    /// opening reading, or whose decisions are not dense from zero, describes a run
    /// nobody watched continuously, and every reader would have to notice that
    /// separately.
    /// </summary>
    public void RequireReadable()
    {
        if (Entries.Count == 0)
        {
            throw new ManifestException(
                "This run journal holds no entries, not even the reading taken before the run's first " +
                "decision, so there is no run to continue.");
        }

        if (Entries[0].Seq != -1 || !string.Equals(Entries[0].Verb, RunCapture.RunStartVerb, StringComparison.Ordinal))
        {
            throw new ManifestException(
                "This run journal does not begin with the reading taken before the run's first decision, so " +
                "nothing in it establishes which run it is of.");
        }

        for (var index = 1; index < Entries.Count; index++)
        {
            if (Entries[index].Seq != index - 1)
            {
                throw new ManifestException(
                    $"This run journal holds seq {Entries[index].Seq.ToString(CultureInfo.InvariantCulture)} where " +
                    $"{(index - 1).ToString(CultureInfo.InvariantCulture)} was expected. A gap is a missing " +
                    "decision wearing a plausible face.");
            }

            if (Entries[index].Before is null || Entries[index].BeforeDigest is null)
            {
                throw new ManifestException(
                    $"This run journal's decision {Entries[index].Seq.ToString(CultureInfo.InvariantCulture)} " +
                    "carries no reading of the state it began from. Every decision is sampled either side, " +
                    "because the comparison instant is before each one.");
            }
        }

        if (Stop is { } stop && stop.Decision.Seq != Entries.Count - 1)
        {
            throw new ManifestException(
                $"This run journal says the recorder stopped at decision " +
                $"{stop.Decision.Seq.ToString(CultureInfo.InvariantCulture)} and holds " +
                $"{(Entries.Count - 1).ToString(CultureInfo.InvariantCulture)} decision(s). A recorder stops at " +
                "the decision after its last, so the two have to agree.");
        }

        if (string.IsNullOrWhiteSpace(RunId) || string.IsNullOrWhiteSpace(RecorderVersion))
        {
            throw new ManifestException(
                "This run journal names no run id or no recorder version, so nothing it holds could be keyed " +
                "or traced.");
        }
    }

    private static IReadOnlyList<string> NormalizeRecords(IEnumerable<string> lines)
    {
        var records = new List<string>();
        var sawNonStandard = false;
        foreach (var line in lines)
        {
            bool isNonStandard;
            try
            {
                isNonStandard = JsonSerializer.Deserialize<JournalNonStandard>(line, Compact)
                    is { NonStandard: true };
            }
            catch (JsonException)
            {
                // Parse already established that only a truncated final line may be unreadable
                continue;
            }
            if (isNonStandard && sawNonStandard) continue;
            sawNonStandard |= isNonStandard;
            records.Add(line + "\n");
        }
        return records;
    }

    /// <summary>Compact and single-line, because every line of this file is appended
    /// on its own and read back on its own.</summary>
    internal static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The header on its own, since the entries are separate lines rather
    /// than a property of it.</summary>
    private sealed record JournalHeader
    {
        [JsonPropertyName("schema")]
        public required string SchemaId { get; init; }

        [JsonPropertyName("run_id")]
        public required string RunId { get; init; }

        [JsonPropertyName("recorder_version")]
        public required string RecorderVersion { get; init; }

        [JsonPropertyName("identity")]
        public required RunIdentityReading Identity { get; init; }

        [JsonPropertyName("witnessed_run_start")]
        public required bool WitnessedRunStart { get; init; }
    }

    /// <summary>A refusal line. Told apart from a decision line by carrying this one
    /// property and none of a decision's, so the two shapes cannot be read as each
    /// other.</summary>
    private sealed record JournalRefusal
    {
        [JsonPropertyName("refusal")]
        public string? Reason { get; init; }
    }

    /// <summary>The non-standard mark's line, told apart from the other two shapes the
    /// same way: one property none of them has. It carries no payload, because what a
    /// recording states about the console is that it was used.</summary>
    private sealed record JournalNonStandard
    {
        [JsonPropertyName("non_standard")]
        public bool? NonStandard { get; init; }
    }

    /// <summary>The stop's line, told apart from the other three shapes the same way.
    /// It carries the decision the recorder could not name and the reading of the
    /// state it began from, because that is the state the recording ends in.</summary>
    private sealed record JournalUnmapped
    {
        [JsonPropertyName("unmapped")]
        public UnmappedDecision? Entry { get; init; }

        [JsonPropertyName("before")]
        public IReadOnlyDictionary<string, string>? Before { get; init; }

        [JsonPropertyName("before_digest")]
        public string? BeforeDigest { get; init; }
    }

    private sealed record JournalRollbackLine
    {
        [JsonPropertyName("rollback")]
        public JournalRollback? Rollback { get; init; }
    }
}

/// <summary>The boundary and range established when the live run resumed earlier.</summary>
public sealed record JournalRollback
{
    [JsonPropertyName("rollback_to_seq")]
    public required int RollbackToSeq { get; init; }

    [JsonPropertyName("rollback_to_digest")]
    public required string RollbackToDigest { get; init; }

    [JsonPropertyName("discarded_from_seq")]
    public required int DiscardedFromSeq { get; init; }

    [JsonPropertyName("discarded_through_seq")]
    public required int DiscardedThroughSeq { get; init; }
}

/// <summary>The journal entries one rollback removed from the continued history.</summary>
public sealed record JournalDiscardedBranch(
    JournalRollback Rollback, RunJournalEntry Boundary, IReadOnlyList<RunJournalEntry> Entries);

/// <summary>
/// Where a recorder stopped: the decision it could not name, and the sampled state
/// and complete digest of the moment it met it.
/// </summary>
public sealed record JournalStop(
    UnmappedDecision Decision, IReadOnlyDictionary<string, string> Before, string BeforeDigest);

/// <summary>
/// One line of a journal: a decision, and the state the game settled into after it.
///
/// The sampled state is what a trace keeps and the digest covers the whole canonical
/// state including the draw order and every random stream's position. Both are here
/// because they answer different questions - the sample is what a comparison reads,
/// and the digest is what says a run resumed here is the run this describes.
/// </summary>
public sealed record RunJournalEntry
{
    /// <summary>The decision's position in the run, or -1 for the reading taken before
    /// any decision.</summary>
    [JsonPropertyName("seq")]
    public required int Seq { get; init; }

    /// <summary>The <see cref="ActionVerb"/> name, or
    /// <see cref="RunCapture.RunStartVerb"/> for the opening reading.</summary>
    [JsonPropertyName("verb")]
    public required string Verb { get; init; }

    [JsonPropertyName("args")]
    public IReadOnlyDictionary<string, string> Args { get; init; } =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The sampled canonical state this decision began from, and the complete digest
    /// of that same moment. Both are required on a decision line and absent from the
    /// opening reading, which has nothing before it. Read where the recorder already
    /// stands when a decision is announced, so a comparison at verification can ask
    /// whether a replay arrived at each decision in the state the player made it from.
    /// </summary>
    [JsonPropertyName("before")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Before { get; init; }

    [JsonPropertyName("before_digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeforeDigest { get; init; }

    /// <summary>The sampled canonical state after this decision settled.</summary>
    [JsonPropertyName("state")]
    public required IReadOnlyDictionary<string, string> State { get; init; }

    /// <summary>The complete canonical state digest at that same moment.</summary>
    [JsonPropertyName("digest")]
    public required string Digest { get; init; }

    [JsonPropertyName("run_clock_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RunClockMs { get; init; }
}
