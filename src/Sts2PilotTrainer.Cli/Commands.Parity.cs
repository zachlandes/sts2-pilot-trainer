using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// The recorder's per-decision standard: does a fresh replay of a recording
    /// reproduce the journal the recorder wrote, decision for decision?
    ///
    /// <c>gate</c> holds a recording at its boundaries and answers whether it may be
    /// published. This holds it at every decision and answers a question about the
    /// recorder rather than about the recording: whether what it wrote down as the
    /// state each decision began from, and settled into, is what the engine produces
    /// from the decisions alone. A recorder defect shows here at the decision it was
    /// made, which the boundary comparison sees only as a digest that differs several
    /// screens later, if at all.
    ///
    /// The journal is the recorder's own file, written beside the manifest as the run
    /// was played, and carries what the manifest does not: a sample and a complete
    /// digest either side of every decision. A recording without one - the two
    /// committed before their journals were kept, or a reconstruction from a video -
    /// has nothing to hold the replay to and is reported as that rather than counted
    /// either way, in the denominator so the figure cannot read as whole.
    ///
    /// Over a corpus every replay runs in a fresh process, for the reason
    /// <see cref="SelfProcess"/> gives; the one-recording form is what each child
    /// runs. The directories are given, never derived: a player's store is copied by
    /// the person and named with <c>--corpus</c>, and nothing read from it is written
    /// back.
    /// </summary>
    internal static int Parity(string[] args)
    {
        var corpora = Args.Values(args, "--corpus");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var artifact = EvidenceArtifact.Prepare(outDir, "parity.json");

        var entries = corpora.Count > 0
            ? RecordingCorpus.Enumerate(corpora).Select(recording => ParityInAChildProcess(recording, outDir)).ToList()
            : [ParityOfOne(Args.Positional(args, 0, "manifest path"), Args.Value(args, "--journal"), print: true)];

        if (corpora.Count > 0)
        {
            foreach (var entry in entries) PrintLine(entry);
        }

        var summary = ParitySummary.Of(entries);
        Console.WriteLine();
        foreach (var line in summary.Describe()) Console.WriteLine(line);

        artifact.WriteAtomic(
            JsonSerializer.Serialize(
                new
                {
                    schema = "sts2-pilot-trainer/parity/v1",
                    arbiter_version = Arbiter.Version,
                    standard =
                        "Every recording with a journal this build reads replays through the real engine to " +
                        "the journal's own sample and complete digest either side of every decision, and " +
                        "every recording's integrity is complete. A recording without a journal, or whose " +
                        "continuity is broken, is counted in the denominator and holds nothing.",
                    corpus = corpora.Count > 0 ? corpora.Select(Paths.Display).ToList() : null,
                    at_parity = summary.Holds,
                    summary,
                    recordings = entries,
                },
                Json.Indented) + "\n");
        Console.WriteLine($"parity artifact: {Paths.Display(artifact.Path)}");

        return summary.Holds ? 0 : 1;
    }

    /// <summary>One recording, replayed here: the shape a corpus run spawns per recording.</summary>
    private static ParityEntry ParityOfOne(string manifestPath, string? journalArg, bool print)
    {
        if (journalArg is not null && !File.Exists(journalArg))
        {
            throw new ManifestException(
                $"Journal '{journalArg}' does not exist. Name a journal that is on hand, or leave --journal off " +
                "to read the manifest's own sibling where there is one.");
        }

        var sibling = JournalBeside(manifestPath);
        var classified = Classify(manifestPath, journalArg ?? (File.Exists(sibling) ? sibling : null));
        var entry = classified.Entry;
        if (entry.Status == ParityStatus.Comparable)
        {
            entry = Compare(entry, classified.Manifest!, classified.Journal!);
        }

        if (print)
        {
            Console.WriteLine($"manifest  : {entry.RunId}");
            Console.WriteLine($"journal   : {entry.Journal ?? "(none)"}");
            Console.WriteLine($"integrity : {entry.Integrity ?? "(not a native recording)"}");
            if (entry.Decisions is { } decisions)
            {
                Console.WriteLine($"decisions : {decisions.ToString(CultureInfo.InvariantCulture)} in the journal, " +
                                  $"{(entry.ReplayedDecisions ?? 0).ToString(CultureInfo.InvariantCulture)} replayed");
            }

            Console.WriteLine();
            Console.WriteLine(entry.Verdict);
            foreach (var diagnostic in entry.ReplayDiagnostics) Console.WriteLine($"  ! {diagnostic}");
        }

        return entry;
    }

    /// <summary>
    /// What can be said of a recording before anything is replayed. Only a native
    /// recording with a journal this build reads and an integrity of <c>complete</c>
    /// goes on to a replay; everything else is its own answer.
    /// </summary>
    private static Classified Classify(string manifestPath, string? journalPath)
    {
        var manifestName = Path.GetFileName(manifestPath);
        var journalName = journalPath is null ? null : Path.GetFileName(journalPath);
        ReplayManifest manifest;
        try
        {
            manifest = ManifestJson.Load(manifestPath);
        }
        catch (ManifestException ex)
        {
            return new Classified(new ParityEntry(
                Path.GetFileNameWithoutExtension(manifestName), manifestName, journalName, null, null, null,
                ParityStatus.Refused, $"this build cannot read the manifest: {ex.Message}"));
        }

        var native = manifest.Source.Native;
        var entry = new ParityEntry(
            manifest.RunId, manifestName, journalName, manifest.Source.Kind, native?.Integrity, native?.Continuity,
            ParityStatus.Comparable, "");

        if (native is null)
        {
            return new Classified(entry with
            {
                Status = ParityStatus.NotNative,
                Detail = $"a {manifest.Source.Kind} reconstruction has no journal by construction; " +
                         "nothing per decision to hold a replay to",
            });
        }

        if (native.Integrity != NativeSource.CompleteIntegrity)
        {
            return new Classified(entry with
            {
                Status = ParityStatus.Integrity,
                Detail = $"integrity is '{native.Integrity}', so the recorder itself says this recording " +
                         "is not a complete account of the run" +
                         (native.Unmapped is { Count: > 0 } unmapped
                             ? $": {ManifestValidator.Describe(unmapped[0])}"
                             : ""),
            });
        }

        if (string.Equals(native.Continuity, NativeSource.BrokenContinuity, StringComparison.Ordinal))
        {
            return new Classified(entry with
            {
                Status = ParityStatus.Continuity,
                Detail = $"continuity is '{native.Continuity}', so the recorder stopped watching this run and " +
                         "started again; a journal with a hole in it is not held to a replay",
            });
        }

        if (journalPath is null)
        {
            return new Classified(entry with
            {
                Status = ParityStatus.NoJournal,
                Detail = "no journal beside the manifest; nothing per decision to hold a replay to",
            });
        }

        RunJournal journal;
        try
        {
            journal = RunJournal.Parse(File.ReadAllText(journalPath));
        }
        catch (UnreadableJournalSchemaException ex)
        {
            return new Classified(entry with { Status = ParityStatus.JournalUnreadable, Detail = ex.Message });
        }
        catch (ManifestException ex)
        {
            return new Classified(entry with
            {
                Status = ParityStatus.Refused,
                Detail = $"the journal is in this build's own schema and cannot be read: {ex.Message}",
            });
        }

        if (!string.Equals(journal.RunId, manifest.RunId, StringComparison.Ordinal))
        {
            return new Classified(entry with
            {
                Status = ParityStatus.Refused,
                Detail = $"the journal is of run '{journal.RunId}' and the manifest of run '{manifest.RunId}'; " +
                         "a journal is held only to the recording it was written for",
            });
        }

        return new Classified(entry, manifest, journal);
    }

    /// <summary>A recording's answer before any replay, with the two files read
    /// where there is a replay still to run.</summary>
    private sealed record Classified(ParityEntry Entry, ReplayManifest? Manifest = null, RunJournal? Journal = null);

    /// <summary>The replay, and the journal held to it.</summary>
    private static ParityEntry Compare(ParityEntry entry, ReplayManifest manifest, RunJournal journal)
    {
        ArbiterOutcome outcome;
        try
        {
            outcome = Arbiter.Run(manifest, progress: RecordedFightEntry.SuppliedProgressFor(manifest));
        }
        catch (ManifestException ex)
        {
            return entry with { Status = ParityStatus.Refused, Detail = ex.Message };
        }

        var report = outcome.Report;
        if (report.Trace is null)
        {
            return entry with
            {
                Status = ParityStatus.Refused,
                Detail = $"the replay was {report.Status.ToString().ToLowerInvariant()} before any decision ran",
                ReplayDiagnostics = report.Diagnostics,
            };
        }

        var result = TraceParity.Compare(journal.Trace, report.Trace);
        return entry with
        {
            Status = result.AtParity ? ParityStatus.Parity : ParityStatus.Diverged,
            Detail = result.AtParity ? result.OpeningNote ?? "" : result.Describe(),
            Decisions = result.Decisions,
            ReplayedDecisions = result.ReplayedDecisions,
            OpeningHiddenState = result.OpeningHiddenState,
            Divergence = result.Divergence is { } divergence
                ? new ParityDivergenceRecord(
                    divergence.Seq, divergence.Verb, divergence.Kind.ToString(), divergence.HiddenStateOnly,
                    divergence.Differences)
                : null,
            ReplayDiagnostics = report.Diagnostics,
        };
    }

    /// <summary>
    /// One recording of a corpus, in a process of its own where it has a replay to
    /// run. The child is this same command over one recording; what it decides is
    /// read back from its artifact rather than its exit code, because a recording
    /// with nothing to compare exits clean and is still not at parity.
    /// </summary>
    private static ParityEntry ParityInAChildProcess(CorpusRecording recording, string outDir)
    {
        var classified = Classify(recording.ManifestPath, recording.JournalPath).Entry;
        if (classified.Status != ParityStatus.Comparable) return classified;

        var childOut = Path.Combine(outDir, "parity", classified.RunId);
        var childArtifact = Path.Combine(childOut, "parity.json");
        if (File.Exists(childArtifact)) File.Delete(childArtifact);
        var child = SelfProcess.Run(
            "parity", recording.ManifestPath, "--journal", recording.JournalPath!, "--out", childOut);
        if (!File.Exists(childArtifact))
        {
            Console.Error.Write(child.StandardError);
            return classified with
            {
                Status = ParityStatus.Refused,
                Detail = $"the replay process exited {child.ExitCode.ToString(CultureInfo.InvariantCulture)} " +
                         "without writing its result",
            };
        }

        using var document = JsonDocument.Parse(File.ReadAllText(childArtifact));
        var entry = document.RootElement.GetProperty("recordings")[0].Deserialize<ParityEntry>(Json.Indented)
            ?? throw new ManifestException($"The replay process wrote no recording into {childArtifact}.");
        return entry;
    }

    private static void PrintLine(ParityEntry entry)
    {
        var counts = entry.Decisions is { } decisions
            ? $"  {decisions.ToString(CultureInfo.InvariantCulture)} in the journal, " +
              $"{(entry.ReplayedDecisions ?? 0).ToString(CultureInfo.InvariantCulture)} replayed"
            : "";
        Console.WriteLine($"  {entry.Mark,-10} {entry.RunId}{counts}");
        foreach (var line in entry.Detail.Split('\n').Where(line => line.Length > 0))
        {
            Console.WriteLine($"             {line}");
        }

        foreach (var diagnostic in entry.ReplayDiagnostics) Console.WriteLine($"             ! {diagnostic}");
    }

    private static string JournalBeside(string manifestPath) =>
        manifestPath.EndsWith(RecordingLibrary.ManifestExtension, StringComparison.Ordinal)
            ? manifestPath[..^RecordingLibrary.ManifestExtension.Length] + RunJournal.FileExtension
            : manifestPath + RunJournal.FileExtension;

    internal enum ParityStatus
    {
        /// <summary>A native recording with a journal this build reads; not yet replayed.</summary>
        Comparable,

        Parity,
        Diverged,
        NoJournal,
        JournalUnreadable,
        NotNative,
        Integrity,

        /// <summary>A recording the recorder stopped watching and picked up again;
        /// counted, never held, because the journal cannot account for the run.</summary>
        Continuity,
        Refused,
    }

    /// <summary>One recording's line in the artifact.</summary>
    internal sealed record ParityEntry(
        [property: JsonPropertyName("run_id")] string RunId,
        [property: JsonPropertyName("manifest")] string Manifest,
        [property: JsonPropertyName("journal")] string? Journal,
        [property: JsonPropertyName("source_kind")] string? SourceKind,
        [property: JsonPropertyName("integrity")] string? Integrity,
        [property: JsonPropertyName("continuity")] string? Continuity,
        [property: JsonPropertyName("status")]
        [property: JsonConverter(typeof(ParityStatusName))]
        ParityStatus Status,
        [property: JsonPropertyName("detail")] string Detail)
    {
        [JsonPropertyName("decisions")]
        public int? Decisions { get; init; }

        [JsonPropertyName("replayed_decisions")]
        public int? ReplayedDecisions { get; init; }

        [JsonPropertyName("divergence")]
        public ParityDivergenceRecord? Divergence { get; init; }

        /// <summary>The two opening digests where they differed with the opening
        /// samples equal, as <c>recorded -> replayed</c>; reported and never counted,
        /// for the reason <see cref="TraceParity"/> gives.</summary>
        [JsonPropertyName("opening_hidden_state")]
        public string? OpeningHiddenState { get; init; }

        [JsonPropertyName("replay_diagnostics")]
        public IReadOnlyList<string> ReplayDiagnostics { get; init; } = [];

        /// <summary>Whether this recording counts against the bar.</summary>
        [JsonIgnore]
        public bool Fails => Status is ParityStatus.Diverged or ParityStatus.Integrity or ParityStatus.Refused;

        [JsonIgnore]
        internal string Mark => Status switch
        {
            ParityStatus.Parity => "PARITY",
            ParityStatus.Diverged => "DIVERGED",
            ParityStatus.NoJournal => "no journal",
            ParityStatus.JournalUnreadable => "unread",
            ParityStatus.NotNative => "not native",
            ParityStatus.Integrity => "INTEGRITY",
            ParityStatus.Continuity => "broken",
            ParityStatus.Refused => "REFUSED",
            _ => Status.ToString(),
        };

        [JsonIgnore]
        internal string Verdict => Status switch
        {
            ParityStatus.Parity =>
                "PARITY - every decision replays to the sample and digest the recorder wrote" +
                (Detail.Length == 0 ? "" : $"\n{Detail}"),
            ParityStatus.Diverged => $"DIVERGED\n{Detail}",
            ParityStatus.NoJournal => $"NO JOURNAL - {Detail}",
            ParityStatus.JournalUnreadable => $"JOURNAL NOT READ - {Detail}",
            ParityStatus.NotNative => $"NOT COMPARED - {Detail}",
            ParityStatus.Integrity => $"INTEGRITY - {Detail}",
            ParityStatus.Continuity => $"NOT COMPARED - {Detail}",
            ParityStatus.Refused => $"REFUSED - {Detail}",
            _ => Detail,
        };
    }

    /// <summary>The status as the artifact spells it: <c>no-journal</c> rather than
    /// a C# identifier.</summary>
    internal sealed class ParityStatusName() : JsonStringEnumConverter<ParityStatus>(JsonNamingPolicy.KebabCaseLower);

    internal sealed record ParityDivergenceRecord(
        [property: JsonPropertyName("seq")] int Seq,
        [property: JsonPropertyName("verb")] string Verb,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("hidden_state_only")] bool HiddenStateOnly,
        [property: JsonPropertyName("differences")] IReadOnlyList<string> Differences);

    /// <summary>The figure, with every recording that is not in it named by why.</summary>
    internal sealed record ParitySummary(
        [property: JsonPropertyName("native_recordings")] int NativeRecordings,
        [property: JsonPropertyName("at_parity")] int AtParity,
        [property: JsonPropertyName("diverged")] int Diverged,
        [property: JsonPropertyName("without_journal")] int WithoutJournal,
        [property: JsonPropertyName("journal_unreadable")] int JournalUnreadable,
        [property: JsonPropertyName("integrity_not_complete")] int IntegrityNotComplete,
        [property: JsonPropertyName("continuity_broken")] int ContinuityBroken,
        [property: JsonPropertyName("refused")] int Refused,
        [property: JsonPropertyName("not_native")] int NotNative)
    {
        [JsonIgnore]
        public bool Holds => Diverged == 0 && IntegrityNotComplete == 0 && Refused == 0;

        internal static ParitySummary Of(IReadOnlyList<ParityEntry> entries) =>
            new(
                entries.Count(entry => entry.Status != ParityStatus.NotNative),
                entries.Count(entry => entry.Status == ParityStatus.Parity),
                entries.Count(entry => entry.Status == ParityStatus.Diverged),
                entries.Count(entry => entry.Status == ParityStatus.NoJournal),
                entries.Count(entry => entry.Status == ParityStatus.JournalUnreadable),
                entries.Count(entry => entry.Status == ParityStatus.Integrity),
                entries.Count(entry => entry.Status == ParityStatus.Continuity),
                entries.Count(entry => entry.Status == ParityStatus.Refused),
                entries.Count(entry => entry.Status == ParityStatus.NotNative));

        internal IEnumerable<string> Describe()
        {
            var n = (int value) => value.ToString(CultureInfo.InvariantCulture);
            yield return $"parity: {n(AtParity)} of {n(NativeRecordings)} native recording(s) " +
                         $"({n(AtParity + Diverged)} with a journal this build reads, " +
                         $"{n(WithoutJournal)} without a journal, " +
                         $"{n(JournalUnreadable)} with a journal it cannot read, " +
                         $"{n(IntegrityNotComplete)} with an integrity other than complete, " +
                         $"{n(ContinuityBroken)} with a broken continuity, " +
                         $"{n(Refused)} refused; {n(NotNative)} not native)";
            yield return Holds
                ? "AT PARITY - every recording with a journal replays decision for decision, and none is incomplete"
                : "NOT AT PARITY - see the recording marked above";
        }
    }
}
