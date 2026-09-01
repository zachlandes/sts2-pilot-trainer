namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Deliberate damage to a manifest's <em>provenance</em>, for proving the ingestion
/// gates reject things.
///
/// These are a different class from <see cref="Corruption"/>. Those damage the
/// action history, and the engine catches them by replaying and disagreeing. These
/// damage the account of where the recording came from, and no amount of replaying
/// would catch them — a run resumed from history replays perfectly well, it is just
/// not the run the history describes. They have to be caught before an engine is
/// started at all.
/// </summary>
public static class IngestionCorruption
{
    public sealed record Case(
        string Name,
        string What,
        string WhyItMatters,
        Func<ReplayManifest, ReplayManifest> Apply);

    public static IReadOnlyList<Case> All =>
    [
        new("resumed-from-run-history",
            "Marks the recording as having been entered from the run history screen.",
            "One of the three mods in this creator's environment resumes a past run from a chosen floor. A " +
            "resumed run has the same seed, build, content hash and acts as a fresh one, so every environment " +
            "gate passes and the replay runs cleanly - against a recording that does not start where the " +
            "history says it does. Nothing downstream can see this.",
            m => WithRunStart(m, s => s with
            {
                EnteredFromRunHistory = Fact<bool>.Observed(
                    true, FactEvidence.AtVideoTime(0, "corrupted by a negative control")),
            })),

        new("recording-starts-mid-run",
            "Sets the first observed run timer to fifteen minutes and the first floor to 12.",
            "The fingerprint a resumed run leaves even when nobody saw the history screen: the timer carries " +
            "the original run's accumulated time instead of starting near zero.",
            m => WithRunStart(m, s => s with
            {
                FirstObservedRunTimeSeconds = Fact<int>.Observed(
                    900, FactEvidence.AtVideoTime(0, "corrupted by a negative control")),
                FirstObservedFloor = Fact<int>.Observed(
                    12, FactEvidence.AtVideoTime(0, "corrupted by a negative control")),
            })),

        new("ends-on-a-different-run",
            "Changes the seed read from the end-of-run screen, leaving the opening reading alone.",
            "What a recording spliced from two runs looks like, and what a reading that drifted looks like. " +
            "One reading cannot catch either; two readings taken most of an hour apart can.",
            m => WithSummary(m, s => s with
            {
                Seed = Fact<string>.Observed(
                    "MMWN3B7J2JL3", FactEvidence.AtVideoTime(s.VideoTimeMs, "corrupted by a negative control")),
            })),

        new("unidentified-mod",
            "Drops one mod from the environment while leaving the reported count at three.",
            "An unidentified mod is precisely the gap the content hash cannot close, so a shortfall has to be " +
            "visible rather than rounded away by a list that looks complete.",
            m => WithMods(m, e => e with { Mods = e.Mods.Take(e.Mods.Count - 1).ToList() })),
    ];

    private static ReplayManifest WithRunStart(ReplayManifest manifest, Func<RunStartEvidence, RunStartEvidence> change)
    {
        var start = manifest.Source.RunStart
            ?? throw new ManifestException("This corruption needs a manifest that carries run-start evidence.");
        return manifest with
        {
            RunId = manifest.RunId + "+corrupted",
            Source = manifest.Source with { RunStart = change(start) },
        };
    }

    private static ReplayManifest WithSummary(
        ReplayManifest manifest, Func<RunSummaryObservation, RunSummaryObservation> change)
    {
        var summary = manifest.Source.RunSummary
            ?? throw new ManifestException("This corruption needs a manifest that carries a run summary.");
        return manifest with
        {
            RunId = manifest.RunId + "+corrupted",
            Source = manifest.Source with { RunSummary = change(summary) },
        };
    }

    private static ReplayManifest WithMods(ReplayManifest manifest, Func<ModEnvironment, ModEnvironment> change) =>
        manifest with
        {
            RunId = manifest.RunId + "+corrupted",
            Environment = manifest.Environment with
            {
                Mods = manifest.Environment.Mods with { Value = change(manifest.Environment.Mods.Value) },
            },
        };
}
