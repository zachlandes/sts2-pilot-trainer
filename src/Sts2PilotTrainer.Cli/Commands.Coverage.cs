using System.Text.Json;
using Sts2PilotTrainer.Engine;
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

        var recordings = RecordingCorpus.Enumerate(corpora)
            .Select(recording =>
            {
                var manifest = ManifestJson.Load(recording.ManifestPath);
                return new CoveredRecording(manifest.RunId, DecisionFacts.Of(manifest));
            })
            .ToList();
        var denominator = DecisionSurface.All();
        var report = DecisionCoverage.Over(denominator, DecisionExcusals.All, recordings);

        foreach (var row in report.Rows) Console.WriteLine($"  {row.Describe()}");
        if (report.OutsideTheDenominator.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  reached by a recording and produced by no walk of this build:");
            foreach (var row in report.OutsideTheDenominator) Console.WriteLine($"  {row.Describe()}");
        }

        if (report.StaleExcusals.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  excused and reached by a recording, or excused and produced by no walk, so the excusal comes out:");
            foreach (var point in report.StaleExcusals) Console.WriteLine($"  {point}");
        }

        Console.WriteLine();
        foreach (var line in report.Totals()) Console.WriteLine(line);
        Console.WriteLine(report.Holds
            ? "COVERED - every point this build offers is reached by a recording, excused in writing, or one this " +
              "format cannot count"
            : "NOT COVERED - see the points above");

        if (Args.Has(args, "--update"))
        {
            var record = EvidenceArtifact.PreparePath(DecisionSurface.RecordPath, clearExisting: false);
            record.WriteAtomic(DecisionSurface.Record(denominator));
            Console.WriteLine($"denominator record: {Paths.Display(record.Path)}");
        }

        artifact.WriteAtomic(
            JsonSerializer.Serialize(
                new
                {
                    schema = "sts2-pilot-trainer/coverage/v1",
                    arbiter_version = Arbiter.Version,
                    standard =
                        "Every decision point this build offers, walked off the game assembly, is reached by a " +
                        "recording in the corpus or excused in writing; a kind this format cannot project is " +
                        "listed as such and counted neither way.",
                    corpus = corpora.Select(Paths.Display).ToList(),
                    covered = report.Holds,
                    totals = new
                    {
                        points = report.Points,
                        covered = report.Covered,
                        excused = report.Excused,
                        uncovered = report.Uncovered,
                        not_projectable = report.NotProjectable,
                        outside_the_denominator = report.OutsideTheDenominator.Count,
                        stale_excusals = report.StaleExcusals.Count,
                        recordings = report.Recordings,
                    },
                    points = report.Rows.Concat(report.OutsideTheDenominator).Select(row => new
                    {
                        kind = row.Point.Kind,
                        identity = row.Point.Identity,
                        recordings = row.Recordings,
                        state = row.State.ToString(),
                        excuse = row.Excuse,
                    }),
                    stale_excusals = report.StaleExcusals,
                },
                Json.Indented) + "\n");
        Console.WriteLine($"coverage artifact: {Paths.Display(artifact.Path)}");

        return report.Holds ? 0 : 1;
    }
}
