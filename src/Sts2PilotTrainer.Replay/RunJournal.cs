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
/// state and complete state digest that followed it. <see cref="RunCapture.Resume"/>
/// is the only reader that matters, and it rebuilds the capture rather than reading
/// the journal as a result - so a session continued from the game's own save
/// publishes exactly what an uninterrupted one would have.
///
/// The digests are why this is not merely a log. They are what lets a resumed session
/// ask whether the run the game came back in is the run this journal describes,
/// which is a question no amount of replaying the history could answer.
/// </summary>
public sealed record RunJournal
{
    public const string Schema = "sts2-pilot-trainer/run-journal/v1";

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
    /// recording nothing downstream can establish. A session that resumed at a
    /// rolled-back save, or that could not read a decision it saw, knows the history
    /// has a hole in it; the session after it would read a journal whose last digest
    /// matches the live one and publish that hole as a continuous account of the run.
    /// </summary>
    public IReadOnlyList<string> Refusals { get; init; } = [];

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

    /// <summary>The whole journal as it would be on disk. For a caller writing one in
    /// a single pass; a recorder appends instead.</summary>
    public string Render() =>
        RenderHeader() +
        string.Concat(Entries.Select(RenderEntry)) +
        string.Concat(Refusals.Select(RenderRefusal));

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
        for (var index = 1; index < lines.Count; index++)
        {
            try
            {
                if (JsonSerializer.Deserialize<JournalRefusal>(lines[index], Compact) is { Reason: not null } refusal)
                {
                    refusals.Add(refusal.Reason);
                    continue;
                }

                var entry = JsonSerializer.Deserialize<RunJournalEntry>(lines[index], Compact)
                    ?? throw new ManifestException("A run journal entry deserialized to null.");
                ManifestJson.ValidateRequiredMembers(entry, "Run journal entry");
                entries.Add(entry);
            }
            catch (Exception exception) when (
                index == lines.Count - 1 &&
                exception is JsonException or ManifestException or InvalidOperationException)
            {
                // The last line of a file a crash interrupted. Everything before it
                // finished being written and is a real recording of what happened.
                break;
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
            if (Entries[index].Seq == index - 1) continue;
            throw new ManifestException(
                $"This run journal holds seq {Entries[index].Seq.ToString(CultureInfo.InvariantCulture)} where " +
                $"{(index - 1).ToString(CultureInfo.InvariantCulture)} was expected. A gap is a missing " +
                "decision wearing a plausible face.");
        }

        if (string.IsNullOrWhiteSpace(RunId) || string.IsNullOrWhiteSpace(RecorderVersion))
        {
            throw new ManifestException(
                "This run journal names no run id or no recorder version, so nothing it holds could be keyed " +
                "or traced.");
        }
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
}

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
