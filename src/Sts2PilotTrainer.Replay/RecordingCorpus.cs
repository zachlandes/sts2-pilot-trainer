namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The recordings a set of directories holds, as the files each is made of.
///
/// One reader for every command that runs over a corpus and for the tests that hold
/// them to it, so what "the corpus" means cannot differ between <c>parity</c> and
/// <c>coverage</c>. A recording is a manifest, named <c>*.replay.json</c>, and the
/// journal the recorder wrote beside it under the same name where that journal is
/// still there: the committed corpus under <c>manifests/</c> and a copy of a player's
/// own store both take this shape, and a recording reconstructed from a video has a
/// manifest and never a journal.
///
/// Directories are read where they are given and nothing is derived: the store's own
/// location is the mod's knowledge and stays there, so a command that reads a player's
/// recordings is handed a copy of them by the person, never a path this code worked
/// out.
/// </summary>
public static class RecordingCorpus
{
    /// <summary>Every recording under each directory, in directory order and then by
    /// name, one entry per manifest.</summary>
    public static IReadOnlyList<CorpusRecording> Enumerate(IEnumerable<string> directories)
    {
        var recordings = new List<CorpusRecording>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                throw new ManifestException(
                    $"Corpus directory '{directory}' does not exist. A corpus is a directory of " +
                    $"*{RecordingLibrary.ManifestExtension} files, with each recording's " +
                    $"*{RunJournal.FileExtension} beside it where the recorder's journal is still on hand.");
            }

            recordings.AddRange(
                Directory.EnumerateFiles(directory, "*" + RecordingLibrary.ManifestExtension)
                    .Order(StringComparer.Ordinal)
                    .Select(manifestPath =>
                    {
                        var journalPath = manifestPath[..^RecordingLibrary.ManifestExtension.Length] +
                                          RunJournal.FileExtension;
                        return new CorpusRecording(manifestPath, File.Exists(journalPath) ? journalPath : null);
                    }));
        }

        return recordings;
    }

    /// <summary>
    /// A manifest read for a corpus number: the manifest, or the one sentence both
    /// numbers give a file this build cannot read, so a corpus with one such file is
    /// reported recording by recording rather than aborted at the first.
    /// </summary>
    public static CorpusReading Read(string manifestPath)
    {
        try
        {
            return new CorpusReading(ManifestJson.Load(manifestPath), null);
        }
        catch (ManifestException ex)
        {
            return new CorpusReading(null, $"this build cannot read the manifest: {ex.Message}");
        }
    }
}

/// <summary>One manifest read from a corpus: exactly one of the two is set.</summary>
public sealed record CorpusReading(ReplayManifest? Manifest, string? Refusal);

/// <summary>One recording in a corpus: its manifest, and its journal where one is on hand.</summary>
public sealed record CorpusRecording(string ManifestPath, string? JournalPath);
