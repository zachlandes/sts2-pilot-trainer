namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What a corpus directory holds: one entry per manifest, with its journal where the
/// recorder's file is beside it, and nothing for anything else in the directory.
/// </summary>
public sealed class RecordingCorpusTests
{
    [Fact]
    public void EveryManifestIsListedWithItsJournalWhereOneIsBesideIt()
    {
        InScratch(directory =>
        {
            Touch(directory, "native-AAAA-20260906-120000.replay.json");
            Touch(directory, "native-AAAA-20260906-120000.journal.jsonl");
            Touch(directory, "native-BBBB-20260906-130000.replay.json");
            Touch(directory, "navegreed-OJ-6QXhNgdg.replay.json");
            Touch(directory, "navegreed-OJ-6QXhNgdg.map-observation.json");
            Touch(directory, "native-CCCC-20260906-140000.journal.jsonl");
            Touch(directory, "settings.json");

            var recordings = RecordingCorpus.Enumerate([directory]);

            Assert.Equal(
                ["native-AAAA-20260906-120000.replay.json", "native-BBBB-20260906-130000.replay.json", "navegreed-OJ-6QXhNgdg.replay.json"],
                recordings.Select(recording => Path.GetFileName(recording.ManifestPath)));
            Assert.Equal(
                ["native-AAAA-20260906-120000.journal.jsonl", null, null],
                recordings.Select(recording =>
                    recording.JournalPath is null ? null : Path.GetFileName(recording.JournalPath)));
        });
    }

    [Fact]
    public void DirectoriesAreReadInTheOrderGiven()
    {
        InScratch(first => InScratch(second =>
        {
            Touch(first, "native-ZZZZ-20260906-120000.replay.json");
            Touch(second, "native-AAAA-20260906-120000.replay.json");

            var recordings = RecordingCorpus.Enumerate([first, second]);

            Assert.Equal(
                ["native-ZZZZ-20260906-120000.replay.json", "native-AAAA-20260906-120000.replay.json"],
                recordings.Select(recording => Path.GetFileName(recording.ManifestPath)));
        }));
    }

    [Fact]
    public void ADirectoryThatIsNotThereIsRefusedInWords()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}");

        var refusal = Assert.Throws<ManifestException>(() => RecordingCorpus.Enumerate([missing]));

        Assert.Contains(missing, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("*.replay.json", refusal.Message, StringComparison.Ordinal);
    }

    private static void Touch(string directory, string name) => File.WriteAllText(Path.Combine(directory, name), "{}");

    private static void InScratch(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
