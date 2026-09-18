using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.IO;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// The recorder's second number: for every decision point this build can offer,
    /// how many recordings in a corpus exercise it.
    ///
    /// The denominator is walked off the game assembly by <see cref="DecisionSurface"/>
    /// and never written by anybody; what each recording exercised is projected off its
    /// history by <see cref="DecisionFacts"/>, which needs no replay and no game. Every
    /// point is printed by name with its count, its excusal, or the reason this format
    /// cannot count it, and the bar is that nothing is uncovered: every point either
    /// has a recording or has a reason in <see cref="DecisionExcusals"/> a build can be
    /// held to.
    ///
    /// A recording is credited only where <see cref="RecordingStanding"/> says it holds,
    /// the reading <c>parity</c> makes of the same file against the build this process
    /// would replay with; one it says holds nothing - a recording of another build
    /// first among them, because a store spans builds once the game updates - is
    /// tallied apart as reached and unverified, and a manifest this build cannot read is
    /// named with the parser's words and the rest of the corpus is still counted. The
    /// build under test is read once, here, and written into the artifact's header, so
    /// the number says which build it is a number about.
    ///
    /// <c>--update</c> rewrites the committed record of the denominator and the
    /// excusals on this build, which the game-gated test holds the walks to, so a game
    /// update that adds a rest option or a reward kind shows as a diff in the change
    /// that adopts it rather than as a point nobody counted.
    /// </summary>
    internal static int Coverage(string[] args)
    {
        var corpora = Args.Values(args, "--corpus");
        if (corpora.Count == 0)
        {
            throw new ManifestException("coverage needs at least one --corpus <dir>; the number is over recordings.");
        }

        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var artifact = EvidenceArtifact.Prepare(outDir, "coverage.json");

        var build = GameIdentity.Read().Build;
        var recordings = new List<CoveredRecording>();
        var unreadable = new List<UnreadableRecording>();
        foreach (var recording in RecordingCorpus.Enumerate(corpora))
        {
            var reading = RecordingCorpus.Read(recording.ManifestPath);
            if (reading.Manifest is { } manifest)
            {
                recordings.Add(new CoveredRecording(
                    manifest.RunId, DecisionFacts.Of(manifest),
                    RecordingStanding.Of(manifest.Source.Native, manifest.Environment, build),
                    DecisionFacts.ModelsMet(manifest)));
            }
            else
            {
                unreadable.Add(new UnreadableRecording(Path.GetFileName(recording.ManifestPath), reading.Refusal!));
            }
        }

        var denominator = DecisionSurface.All();
        var report = DecisionCoverage.Over(
            denominator, DecisionExcusals.All, recordings, DecisionSurface.ProducerMap(),
            DecisionSurface.AdmissibleExcusals(denominator), unreadable);

        foreach (var row in report.Rows) Console.WriteLine($"  {row.Describe()}");
        if (report.Unverified.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  reached by these recordings and credited to nothing, because each holds nothing on this build:");
            foreach (var recording in report.Unverified)
            {
                var lines = recording.Standing.Detail.Split('\n');
                Console.WriteLine($"  {recording.RunId}  {lines[0]}");
                foreach (var line in lines.Skip(1)) Console.WriteLine($"  {new string(' ', recording.RunId.Length)}  {line}");
            }
        }

        if (report.Unreadable.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  in the corpus and not read, so nothing of them is counted:");
            foreach (var recording in report.Unreadable) Console.WriteLine($"  {recording.Manifest}  {recording.Detail}");
        }

        if (report.OutsideTheDenominator.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  reached by a recording and produced by no walk of this build:");
            foreach (var row in report.OutsideTheDenominator) Console.WriteLine($"  {row.Describe()}");
        }

        if (report.ExcusedAndReached.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  excused, and reached by a recording of this corpus - the excusal is not needed here:");
            foreach (var point in report.ExcusedAndReached) Console.WriteLine($"  {point}");
        }

        if (report.StaleExcusals.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  excused and produced by no walk of this build, so the excusal comes out:");
            foreach (var point in report.StaleExcusals) Console.WriteLine($"  {point}");
        }

        if (report.InadmissibleExcusals.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  excused in a class the map does not admit for the point, which fails the bar; each names what the map admits:");
            foreach (var excusal in report.InadmissibleExcusals) Console.WriteLine($"  {excusal.Describe()}");
        }

        if (report.MisnamedProducers.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  excused naming a producer the map does not list for the point, which fails the bar; each names what the map lists:");
            foreach (var producer in report.MisnamedProducers) Console.WriteLine($"  {producer.Describe()}");
        }

        Console.WriteLine();
        foreach (var line in report.Totals()) Console.WriteLine(line);
        Console.WriteLine(report.Holds
            ? "COVERED - every point this build offers is reached by a recording, excused in writing, or one this " +
              "format cannot count"
            : "NOT COVERED - see the points above");

        if (Args.Has(args, "--update"))
        {
            var record = EvidenceArtifact.PreparePath(
                Path.Combine(WorktreeLocator.Find(), DecisionSurface.RecordPath), clearExisting: false);
            record.WriteAtomic(DecisionSurface.Record(denominator));
            Console.WriteLine($"denominator record: {Paths.Display(record.Path)}");
            var producerMap = EvidenceArtifact.PreparePath(
                Path.Combine(WorktreeLocator.Find(), DecisionSurface.ProducerMapRecordPath), clearExisting: false);
            producerMap.WriteAtomic(DecisionSurface.ProducerMapRecord());
            Console.WriteLine($"producer map: {Paths.Display(producerMap.Path)}");
        }

        artifact.WriteAtomic(
            JsonSerializer.Serialize(
                new
                {
                    schema = "sts2-pilot-trainer/coverage/v3",
                    arbiter_version = Arbiter.Version,
                    standard =
                        "Every decision point this build offers, walked off the game assembly, is reached by a " +
                        "recording in the corpus or excused in writing in a class the map admits, naming no " +
                        "producer the map does not list for the point; a seam is " +
                        "reached by co-occurrence, a recording that met one of its producers and answered its " +
                        "decision; a kind this format cannot project is listed as such and counted neither way. " +
                        "A recording made on another build, or one the recorder marked broken, unmapped or " +
                        "non-standard, credits nothing and is tallied apart as unverified, and a manifest this " +
                        "build cannot read is named and counts for nothing.",
                    build = new
                    {
                        build_version = build.BuildVersion,
                        build_date_utc = build.BuildDateUtc,
                        content_hash = build.ContentHash,
                    },
                    corpus = corpora.Select(Paths.Display).ToList(),
                    covered = report.Holds,
                    totals = new
                    {
                        points = report.Points,
                        covered = report.Covered,
                        co_occurrence = report.CoOccurrence,
                        excused = report.Excused,
                        uncovered = report.Uncovered,
                        not_projectable = report.NotProjectable,
                        outside_the_denominator = report.OutsideTheDenominator.Count,
                        stale_excusals = report.StaleExcusals.Count,
                        inadmissible_excusals = report.InadmissibleExcusals.Count,
                        misnamed_producers = report.MisnamedProducers.Count,
                        excused_and_reached = report.ExcusedAndReached.Count,
                        recordings = report.Recordings,
                        credited_recordings = report.CreditedRecordings,
                        unverified_recordings = report.Unverified.Count,
                        unreadable_recordings = report.Unreadable.Count,
                    },
                    points = report.Rows.Concat(report.OutsideTheDenominator).Select(row => new
                    {
                        kind = row.Point.Kind,
                        identity = row.Point.Identity,
                        recordings = row.Recordings,
                        unverified_recordings = row.UnverifiedRecordings,
                        state = row.State.ToString(),
                        excuse = row.Excuse?.Reason,
                        excuse_class = row.Excuse is { } excuse ? ExcusalClasses.Name(excuse.Class) : null,
                        excuse_producers = row.Excuse is { NamedProducers.Count: > 0 } named ? named.NamedProducers : null,
                    }),
                    stale_excusals = report.StaleExcusals,
                    inadmissible_excusals = report.InadmissibleExcusals.Select(excusal => new
                    {
                        kind = excusal.Point.Kind,
                        identity = excusal.Point.Identity,
                        claimed = ExcusalClasses.Name(excusal.Claimed),
                        admitted = excusal.Admitted.Select(ExcusalClasses.Name),
                    }),
                    misnamed_producers = report.MisnamedProducers.Select(producer => new
                    {
                        kind = producer.Point.Kind,
                        identity = producer.Point.Identity,
                        producer = producer.Producer,
                        listed = producer.Listed,
                    }),
                    excused_and_reached = report.ExcusedAndReached,
                    unverified_recordings = report.Unverified.Select(recording => new
                    {
                        run_id = recording.RunId,
                        standing = recording.Standing.Kind.ToString(),
                        detail = recording.Standing.Detail,
                        build = recording.Standing.Kind == RecordingStandingKind.AnotherBuild
                            ? recording.Standing.Build
                            : null,
                    }),
                    unreadable_recordings = report.Unreadable.Select(recording => new
                    {
                        manifest = recording.Manifest,
                        detail = recording.Detail,
                    }),
                },
                Json.Indented) + "\n");
        Console.WriteLine($"coverage artifact: {Paths.Display(artifact.Path)}");

        return report.Holds ? 0 : 1;
    }
}
