using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The recorder's per-decision standard, run by the command that computes it.
///
/// The first fact is the merge-gate condition: <c>parity --corpus manifests</c> holds
/// every committed recording with a journal to a fresh replay and names the ones that
/// have none, so the figure it prints is the committed corpus's real parity and never
/// a whole-looking fraction over the recordings that happened to carry a journal.
/// Today that figure is 0 of 2 - both committed native recordings were made before
/// their journals were kept in a schema this build reads - and the fact holds the
/// command to saying so.
///
/// The rest hold the command to the oracle on a journal built from a real replay's
/// own trace: at parity as written, and refused in the right words when one before
/// field, one digest or the run id is changed. A journal the recorder itself wrote
/// is held to the same oracle in <c>HeadlessGameplayCaptureTests</c>, in process,
/// because a headless recording carries the headless host's patch roster and the
/// retail preflight in front of this command rightly refuses it.
/// </summary>
public sealed class ParityTests
{
    private const string ShortRun = "native-9F8CY60C5BK7-20260906-005737";

    [GameFact]
    public void TheCommittedCorpusIsHeldToParityAndEveryRecordingWithoutAJournalIsNamed()
    {
        InScratch(outDir =>
        {
            var result = Arbiter.Run("parity", "--corpus", "manifests", "--out", outDir);

            Assert.True(result.Verified, result.All);
            Assert.Contains("no journal native-3LACFJ5NJ371-20260906-015901", result.Output, StringComparison.Ordinal);
            Assert.Contains("no journal native-9F8CY60C5BK7-20260906-005737", result.Output, StringComparison.Ordinal);
            Assert.Contains("not native navegreed-OJ-6QXhNgdg", result.Output, StringComparison.Ordinal);
            Assert.Contains("parity: 0 of 2 native recording(s)", result.Output, StringComparison.Ordinal);
            Assert.Contains("AT PARITY", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "parity.json"))).RootElement;
            Assert.True(artifact.GetProperty("at_parity").GetBoolean());
            var summary = artifact.GetProperty("summary");
            Assert.Equal(2, summary.GetProperty("native_recordings").GetInt32());
            Assert.Equal(0, summary.GetProperty("at_parity").GetInt32());
            Assert.Equal(2, summary.GetProperty("without_journal").GetInt32());
            Assert.Equal(1, summary.GetProperty("not_native").GetInt32());
            Assert.Equal(
                ["no-journal", "no-journal", "not-native"],
                artifact.GetProperty("recordings").EnumerateArray()
                    .Select(recording => recording.GetProperty("status").GetString()));
        });
    }

    [GameFact]
    public void AJournalTheReplayReproducesIsAtParity()
    {
        InScratch(directory =>
        {
            var (manifestPath, _) = RecordingWithAJournal(directory);

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.True(result.Verified, result.All);
            Assert.Contains("PARITY - every decision replays", result.Output, StringComparison.Ordinal);
            Assert.Contains("decisions : 51 in the journal, 51 replayed", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("opening reading", result.Output, StringComparison.Ordinal);
        });
    }

    [GameFact]
    public void AJournalWhoseBeforeReadingDiffersIsRefusedAtThatDecisionNamingTheField()
    {
        InScratch(directory =>
        {
            var (manifestPath, journalPath) = RecordingWithAJournal(directory);
            var verb = Rewrite(journalPath, seq: 5, entry => entry["before"]!["player.hp"] = "1");

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains("DIVERGED", result.Output, StringComparison.Ordinal);
            Assert.Contains($"decision 5 ({verb}) before: player.hp: 1 -> ", result.Output, StringComparison.Ordinal);
            Assert.Contains("NOT AT PARITY", result.Output, StringComparison.Ordinal);
        });
    }

    [GameFact]
    public void AJournalWhoseDigestAloneDiffersIsRefusedAsHiddenState()
    {
        InScratch(directory =>
        {
            var (manifestPath, journalPath) = RecordingWithAJournal(directory);
            var verb = Rewrite(journalPath, seq: 5, entry => entry["before_digest"] = "sha256:" + new string('0', 64));

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains(
                $"hidden state differs at decision 5 ({verb}); every sampled field agrees",
                result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "evidence", "parity.json")));
            var divergence = artifact.RootElement.GetProperty("recordings")[0].GetProperty("divergence");
            Assert.Equal(5, divergence.GetProperty("seq").GetInt32());
            Assert.True(divergence.GetProperty("hidden_state_only").GetBoolean());
        });
    }

    [GameFact]
    public void AJournalOfAnotherRunIsRefusedByName()
    {
        InScratch(directory =>
        {
            var (manifestPath, journalPath) = RecordingWithAJournal(directory);
            var lines = File.ReadAllLines(journalPath);
            lines[0] = lines[0].Replace(ShortRun, "native-9F8CY60C5BK7-20260906-005738", StringComparison.Ordinal);
            File.WriteAllLines(journalPath, lines);

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains(
                "REFUSED - the journal is of run 'native-9F8CY60C5BK7-20260906-005738' and the manifest of run " +
                $"'{ShortRun}'",
                result.Output, StringComparison.Ordinal);
        });
    }

    /// <summary>A journal in a schema this build does not read is named as that, and
    /// holds nothing rather than failing the run: the recording is still counted.</summary>
    [GameFact]
    public void AJournalOfAnOlderSchemaIsNamedAndHoldsNothing()
    {
        InScratch(directory =>
        {
            var (manifestPath, journalPath) = RecordingWithAJournal(directory);
            var lines = File.ReadAllLines(journalPath);
            lines[0] = lines[0].Replace(RunJournal.Schema, "sts2-pilot-trainer/run-journal/v1", StringComparison.Ordinal);
            File.WriteAllLines(journalPath, lines);

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.True(result.Verified, result.All);
            Assert.Contains("JOURNAL NOT READ - This run journal declares schema", result.Output, StringComparison.Ordinal);
            Assert.Contains("parity: 0 of 1 native recording(s)", result.Output, StringComparison.Ordinal);
            Assert.Contains("1 with a journal it cannot read", result.Output, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The committed short recording and a journal its own fresh replay would have
    /// written: the replay's trace, every reading and both digests, recorded through
    /// <see cref="RunCapture"/> so the file is what the recorder writes and not a
    /// hand-shaped one.
    /// </summary>
    private static (string ManifestPath, string JournalPath) RecordingWithAJournal(string directory)
    {
        var source = Path.Combine(Arbiter.RepoRoot, "manifests", $"{ShortRun}{RecordingLibrary.ManifestExtension}");
        var replayedPath = Path.Combine(directory, "replayed.json");
        var replayed = Arbiter.Run("replay", source, "--out", replayedPath);
        Assert.True(replayed.Verified, replayed.All);
        var manifest = ManifestJson.Load(replayedPath);
        var trace = manifest.Verification!.Trace!;

        var environment = manifest.Environment;
        var capture = RunCapture.Begin(new RunRecordingStart
        {
            RunId = manifest.RunId,
            RecorderVersion = manifest.Source.Native!.RecorderVersion,
            Identity = new RunIdentityReading
            {
                BuildVersion = environment.BuildVersion.Value,
                BuildDateUtc = environment.BuildDateUtc.Value,
                ContentHash = environment.ContentHash.Value,
                GameMode = environment.GameMode.Value,
                Seed = environment.Seed.Value,
                Ascension = environment.Ascension.Value,
                Character = environment.Character.Value,
                Acts = environment.Acts.Value,
                Unlocks = environment.Unlocks.Value.Inventory!,
                Mods = environment.Mods.Value,
            },
            State = trace.Steps[0].After,
            Digest = trace.Steps[0].AfterDigest!,
            RunClockMs = 0,
        });
        foreach (var step in trace.Steps.Skip(1))
        {
            capture.Record(
                Enum.Parse<ActionVerb>(step.Verb), step.Args,
                new StateReading(step.Before, step.BeforeDigest!), new StateReading(step.After, step.AfterDigest!));
        }

        var manifestPath = Path.Combine(directory, $"{ShortRun}{RecordingLibrary.ManifestExtension}");
        var journalPath = Path.Combine(directory, $"{ShortRun}{RunJournal.FileExtension}");
        File.Copy(source, manifestPath);
        File.WriteAllText(journalPath, capture.Journal.Render());
        return (manifestPath, journalPath);
    }

    /// <summary>Edits one decision's line in place and returns its verb.</summary>
    private static string Rewrite(string journalPath, int seq, Action<JsonObject> edit)
    {
        var lines = File.ReadAllLines(journalPath).ToList();
        var index = lines.FindIndex(line =>
            JsonNode.Parse(line) is JsonObject entry && entry["seq"]?.GetValue<int>() == seq && entry["verb"] is not null);
        Assert.True(index >= 0, $"the journal holds no decision {seq.ToString(CultureInfo.InvariantCulture)}");
        var record = (JsonObject)JsonNode.Parse(lines[index])!;
        edit(record);
        lines[index] = record.ToJsonString();
        File.WriteAllLines(journalPath, lines);
        return record["verb"]!.GetValue<string>();
    }

    private static void InScratch(Action<string> body)
    {
        var directory = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"parity-{Guid.NewGuid():N}");
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
