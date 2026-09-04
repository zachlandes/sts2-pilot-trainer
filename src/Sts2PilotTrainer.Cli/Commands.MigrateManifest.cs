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
    /// It copies and never derives. The version-4 combat-start digest was produced by
    /// the engine and stays engine-produced as the first entry of
    /// <see cref="ReplayManifest.Boundaries"/>; the boundaries a version-4 manifest
    /// never had are derived by replaying the run, which is the arbiter's job and not
    /// this one's.
    /// </summary>
    internal static int MigrateManifest(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outPath = Args.Value(args, "--out") ?? manifestPath;

        var before = File.ReadAllText(manifestPath);
        var manifest = ManifestJson.Deserialize(before);
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
}
