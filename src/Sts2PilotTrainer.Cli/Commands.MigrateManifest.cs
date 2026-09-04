using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Rewrites a manifest on disk in the current format.
    ///
    /// Reading an older manifest is something <see cref="ManifestJson"/> does in
    /// memory, every time, for anyone. Writing one back is this command and nothing
    /// else: a reader that quietly rewrote the file it was handed would edit somebody's
    /// evidence as a side effect of looking at it, and the moment a file changes has to
    /// be a moment a person chose.
    ///
    /// Without <c>--derive-boundaries</c> it copies and never derives. The version-4
    /// combat-start digest was produced by the engine and stays engine-produced as the
    /// first entry of <see cref="ReplayManifest.Boundaries"/>; the boundaries a
    /// version-4 manifest never had are facts about what the engine did, and inventing
    /// them from the shape of a history would be exactly the plausible wrong answer
    /// this project exists to prevent.
    ///
    /// With it, the run is replayed through the real engine and every boundary the
    /// history passes is written in with the digest that replay produced. Still a
    /// moment a person chose, and still not a reader: it needs the game, it takes
    /// minutes, and it rewrites somebody's evidence.
    /// </summary>
    internal static int MigrateManifest(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outPath = Args.Value(args, "--out") ?? manifestPath;

        var before = File.ReadAllText(manifestPath);
        var manifest = ManifestJson.Deserialize(before);
        if (Args.Has(args, "--derive-boundaries")) manifest = WithDerivedBoundaries(manifest);
        var after = ManifestJson.Serialize(manifest) + "\n";

        Console.WriteLine($"manifest : {manifest.RunId}");
        Console.WriteLine(
            $"version  : {ReplayManifest.CurrentManifestVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        foreach (var boundary in manifest.Boundaries)
        {
            Console.WriteLine(
                $"boundary : {boundary.Describe()} after action " +
                $"{boundary.AfterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                $"{boundary.Digest.Source.ToString().ToLowerInvariant()} {boundary.Digest.Value}");
        }

        var rewritingInPlace = string.Equals(
            Path.GetFullPath(outPath), Path.GetFullPath(manifestPath), StringComparison.Ordinal);

        if (rewritingInPlace && string.Equals(before, after, StringComparison.Ordinal))
        {
            Console.WriteLine();
            Console.WriteLine($"unchanged: {Paths.Display(manifestPath)} is already in this format.");
            return 0;
        }

        EvidenceArtifact.PreparePath(outPath, clearExisting: false).WriteAtomic(after);
        Console.WriteLine();
        Console.WriteLine($"migrated : {Paths.Display(outPath)}");
        return 0;
    }

    /// <summary>
    /// The same manifest with every boundary its history passes, derived by replaying
    /// it through the real engine.
    ///
    /// Two refusals rather than one. A replay that did not verify has established
    /// nothing, so there is nothing to write. And a boundary the manifest already
    /// declares has to agree with what this build derived: a disagreement is either
    /// drift in the game or a manifest describing a different run, and quietly
    /// overwriting the older digest would erase the evidence of it.
    /// </summary>
    private static ReplayManifest WithDerivedBoundaries(ReplayManifest manifest)
    {
        var outcome = Arbiter.Run(manifest);
        var report = outcome.Report;

        if (report.Status != VerificationStatus.Verified)
        {
            throw new EngineException(
                $"This history does not reproduce on this build ({report.Status.ToString().ToLowerInvariant()}), " +
                "so there are no boundaries to derive. A boundary is the digest of a state the engine " +
                "actually reached.\n" + string.Join("\n", report.Diagnostics.Select(line => "  - " + line)));
        }

        foreach (var declared in manifest.Boundaries)
        {
            var derived = Matching(report.Boundaries, declared);

            if (derived is null)
            {
                throw new EngineException(
                    $"This manifest declares {declared.Describe()} and replaying it here reaches no such " +
                    "boundary. Rewriting the list would delete a claim rather than check it.");
            }

            if (derived.Digest.Value != declared.Digest.Value || derived.AfterSeq != declared.AfterSeq)
            {
                throw new EngineException(
                    $"This manifest declares {declared.Describe()} after action " +
                    $"{declared.AfterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)} with " +
                    $"digest {declared.Digest.Value}, and replaying it here reaches it after action " +
                    $"{derived.AfterSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)} with " +
                    $"digest {derived.Digest.Value}. Overwriting the older digest would erase the evidence " +
                    "that this build and the one this recording was made on disagree, which is the finding " +
                    "rather than something to smooth away.");
            }
        }

        // A boundary the manifest already declares is kept as it was declared, not
        // replaced by the derived entry the checks above just found identical. The two
        // agree about the digest and say different things about where it came from: a
        // digest captured from a live game names the coordinates somebody would
        // re-check it by, and a derived one is stamped Engine and carries none. Only a
        // boundary this manifest did not have is taken from the replay.
        var boundaries = report.Boundaries
            .Select(derived => Matching(manifest.Boundaries, derived) ?? derived)
            .ToList();

        Console.WriteLine(
            $"derived  : {report.Boundaries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"boundaries from a verified replay");
        return manifest with { Boundaries = boundaries };
    }

    /// <summary>The boundary in a list naming the same place as this one, or null where
    /// the list has none. Matched on the kind's own coordinates, because that is what
    /// identifies a boundary, and asked of both lists here so the check and the rewrite
    /// cannot disagree about which entries are the same one.</summary>
    private static ReplayBoundary? Matching(
        IReadOnlyList<ReplayBoundary> boundaries, ReplayBoundary sought) =>
        boundaries.FirstOrDefault(boundary =>
            boundary.Kind == sought.Kind &&
            boundary.Fight == sought.Fight &&
            boundary.Floor == sought.Floor &&
            boundary.Turn == sought.Turn);
}
