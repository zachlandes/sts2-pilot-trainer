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
            Assert.Contains("no journal  native-3LACFJ5NJ371-20260906-015901", result.Output, StringComparison.Ordinal);
            Assert.Contains("no journal  native-9F8CY60C5BK7-20260906-005737", result.Output, StringComparison.Ordinal);
            Assert.Contains("not native  navegreed-OJ-6QXhNgdg", result.Output, StringComparison.Ordinal);
            Assert.Contains("parity: 0 of 2 native recording(s)", result.Output, StringComparison.Ordinal);
            Assert.Contains("AT PARITY", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "parity.json"))).RootElement;
            Assert.True(artifact.GetProperty("at_parity").GetBoolean());
            Assert.Equal("v0.111.0", artifact.GetProperty("build").GetProperty("build_version").GetString());
            var summary = artifact.GetProperty("summary");
            Assert.Equal(2, summary.GetProperty("native_recordings").GetInt32());
            Assert.Equal(0, summary.GetProperty("another_build").GetInt32());
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
    /// The corpus form over recordings that have a replay to run: each is replayed in
    /// a child process and its answer read back from the child's artifact, so this is
    /// the one place the round trip through that artifact runs. Three recordings under
    /// three run ids - one at parity, one whose journal was edited, one whose continuity
    /// the recorder marked broken - and the printed figure counts all three while the
    /// broken one is neither replayed nor held against the bar.
    /// </summary>
    [GameFact]
    public void ACorpusIsReplayedRecordingByRecordingInChildProcessesAndEachAnswerIsReadBack()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);
            var (manifestPath, journalPath) = RecordingWithAJournal(corpus);

            const string divergedRun = "native-9F8CY60C5BK7-20260906-005738";
            var divergedJournal = CopyUnder(manifestPath, journalPath, corpus, divergedRun);
            var verb = Rewrite(divergedJournal, seq: 5, entry => entry["before"]!["player.hp"] = "1");

            const string brokenRun = "native-9F8CY60C5BK7-20260906-005739";
            var brokenManifest = Path.Combine(corpus, $"{brokenRun}{RecordingLibrary.ManifestExtension}");
            CopyUnder(manifestPath, journalPath, corpus, brokenRun);
            var broken = JsonNode.Parse(File.ReadAllText(brokenManifest))!;
            broken["source"]!["native"]!["continuity"] = NativeSource.BrokenContinuity;
            File.WriteAllText(brokenManifest, broken.ToJsonString());

            var outDir = Path.Combine(directory, "evidence");
            var result = Arbiter.Run("parity", "--corpus", corpus, "--out", outDir);

            Assert.False(result.Verified, result.All);
            Assert.Contains($"PARITY      {ShortRun}  51 in the journal, 51 replayed", result.Output, StringComparison.Ordinal);
            Assert.Contains($"DIVERGED    {divergedRun}  51 in the journal, 51 replayed", result.Output, StringComparison.Ordinal);
            Assert.Contains($"decision 5 ({verb}) before: player.hp: 1 -> ", result.Output, StringComparison.Ordinal);
            Assert.Contains($"broken      {brokenRun}", result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "parity: 1 of 3 native recording(s) (2 compared, 0 of another build, 0 without a journal, " +
                "0 with a journal it cannot read, 0 with an integrity other than complete, 1 with a broken continuity, " +
                "0 refused; 0 not native)",
                result.Output, StringComparison.Ordinal);
            Assert.Contains("NOT AT PARITY", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "parity.json"))).RootElement;
            Assert.False(artifact.GetProperty("at_parity").GetBoolean());
            var summary = artifact.GetProperty("summary");
            Assert.Equal(3, summary.GetProperty("native_recordings").GetInt32());
            Assert.Equal(1, summary.GetProperty("at_parity").GetInt32());
            Assert.Equal(1, summary.GetProperty("diverged").GetInt32());
            Assert.Equal(1, summary.GetProperty("continuity_broken").GetInt32());

            var recordings = artifact.GetProperty("recordings").EnumerateArray().ToList();
            Assert.Equal(
                [ShortRun, divergedRun, brokenRun],
                recordings.Select(recording => recording.GetProperty("run_id").GetString()));
            Assert.Equal(
                ["parity", "diverged", "continuity"],
                recordings.Select(recording => recording.GetProperty("status").GetString()));
            Assert.Equal(51, recordings[0].GetProperty("decisions").GetInt32());
            Assert.Equal(51, recordings[0].GetProperty("replayed_decisions").GetInt32());
            var divergence = recordings[1].GetProperty("divergence");
            Assert.Equal(5, divergence.GetProperty("seq").GetInt32());
            Assert.Equal(verb, divergence.GetProperty("verb").GetString());
            Assert.False(divergence.GetProperty("hidden_state_only").GetBoolean());
            Assert.StartsWith("player.hp: 1 -> ", divergence.GetProperty("differences")[0].GetString(), StringComparison.Ordinal);
            Assert.False(recordings[2].TryGetProperty("divergence", out _));
            Assert.False(Directory.Exists(Path.Combine(outDir, "parity", brokenRun)));
        });
    }

    /// <summary>
    /// A recording of another build is classified before anything is replayed, on the
    /// preflight's own three fields and in the sentence <c>replay</c> refuses the same
    /// file with, and fails the bar as that refusal did: never replayed, and never
    /// laundered into an AT PARITY verdict, because a recording this build cannot
    /// replay is unproven on it. The same reading <c>coverage</c> makes of the file,
    /// through <see cref="RecordingStanding"/>; before it, the recording reached the
    /// replay and was reported refused there.
    /// </summary>
    [GameFact]
    public void ARecordingOfAnotherBuildIsNamedBeforeAnythingIsReplayedAndHoldsNothing()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);
            var (manifestPath, journalPath) = RecordingWithAJournal(corpus);
            const string otherBuildRun = "native-9F8CY60C5BK7-20260906-005738";
            var otherBuildManifest = Path.Combine(corpus, $"{otherBuildRun}{RecordingLibrary.ManifestExtension}");
            CopyUnder(manifestPath, journalPath, corpus, otherBuildRun);
            var relabelled = JsonNode.Parse(File.ReadAllText(otherBuildManifest))!;
            relabelled["environment"]!["build_version"]!["Value"] = "v0.112.0";
            relabelled["environment"]!["content_hash"]!["Value"] = "999999999";
            File.WriteAllText(otherBuildManifest, relabelled.ToJsonString());

            var outDir = Path.Combine(directory, "evidence");
            var result = Arbiter.Run("parity", "--corpus", corpus, "--out", outDir);

            Assert.False(result.Verified, result.All);
            Assert.Contains($"PARITY      {ShortRun}  51 in the journal, 51 replayed", result.Output, StringComparison.Ordinal);
            Assert.Contains($"other build {otherBuildRun}", result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "build_version: manifest says 'v0.112.0', this machine has 'v0.111.0'. Replaying on a different build",
                result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "content_hash: manifest says '999999999', this machine has '1568834832'. The content hash is a checksum",
                result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "parity: 1 of 2 native recording(s) (1 compared, 1 of another build, 0 without a journal, " +
                "0 with a journal it cannot read, 0 with an integrity other than complete, 0 with a broken continuity, " +
                "0 refused; 0 not native)",
                result.Output, StringComparison.Ordinal);
            Assert.Contains("NOT AT PARITY", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("REFUSED", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(outDir, "parity", otherBuildRun)));

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "parity.json"))).RootElement;
            Assert.False(artifact.GetProperty("at_parity").GetBoolean());
            var build = artifact.GetProperty("build");
            Assert.Equal("v0.111.0", build.GetProperty("build_version").GetString());
            Assert.Equal("1568834832", build.GetProperty("content_hash").GetString());
            Assert.Equal(1, artifact.GetProperty("summary").GetProperty("another_build").GetInt32());
            var entry = artifact.GetProperty("recordings").EnumerateArray()
                .Single(recording => recording.GetProperty("run_id").GetString() == otherBuildRun);
            Assert.Equal("another-build", entry.GetProperty("status").GetString());
            Assert.StartsWith("build_version: manifest says 'v0.112.0'", entry.GetProperty("detail").GetString(), StringComparison.Ordinal);
        });
    }

    /// <summary>A stale child artifact is never a verdict: the parent clears it before
    /// the child runs, so a child that dies before writing leaves nothing to read as
    /// the previous run's answer.</summary>
    [GameFact]
    public void AChildThatWritesNoArtifactIsRefusedRatherThanReadFromTheLastRun()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);
            RecordingWithAJournal(corpus);
            var outDir = Path.Combine(directory, "evidence");

            var first = Arbiter.Run("parity", "--corpus", corpus, "--out", outDir);
            Assert.True(first.Verified, first.All);
            var childArtifact = Path.Combine(outDir, "parity", ShortRun, "parity.json");
            Assert.True(File.Exists(childArtifact));

            var second = Arbiter.RunWithEnvironment(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    // The child's alone: the parent starts the engine too, to read the
                    // build under test, and is meant to live to read the child's answer
                    ["STS2_PILOT_TRAINER_TEST_CHILD_REQUIRED_INIT_FAILURE"] = "parity-child",
                },
                "parity", "--corpus", corpus, "--out", outDir);

            Assert.False(second.Verified, second.All);
            Assert.Contains($"REFUSED     {ShortRun}", second.Output, StringComparison.Ordinal);
            Assert.Contains("exited 1 without writing its result", second.Output, StringComparison.Ordinal);
            Assert.Contains("Required engine initialization failed", second.All, StringComparison.Ordinal);
            Assert.DoesNotContain($"PARITY      {ShortRun}", second.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(childArtifact));
        });
    }

    [GameFact]
    public void AnExplicitJournalThatDoesNotExistIsRefusedRatherThanReadAsNone()
    {
        InScratch(directory =>
        {
            var (manifestPath, _) = RecordingWithAJournal(directory);
            var missing = Path.Combine(directory, "typo.journal.jsonl");

            var result = Arbiter.Run(
                "parity", manifestPath, "--journal", missing, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains($"Journal '{missing}' does not exist", result.All, StringComparison.Ordinal);
            Assert.DoesNotContain("NO JOURNAL", result.Output, StringComparison.Ordinal);
        });
    }

    /// <summary>A journal in this build's own schema that is malformed is a recorder
    /// defect or corruption, and fails the bar rather than holding nothing.</summary>
    [GameFact]
    public void AMalformedJournalOfThisSchemaIsRefusedAndFailsTheBar()
    {
        InScratch(directory =>
        {
            var (manifestPath, journalPath) = RecordingWithAJournal(directory);
            Rewrite(journalPath, seq: 7, entry => entry.Remove("before"));

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains("REFUSED - the journal is in this build's own schema and cannot be read: ", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("JOURNAL NOT READ", result.Output, StringComparison.Ordinal);
            Assert.Contains("NOT AT PARITY", result.Output, StringComparison.Ordinal);
        });
    }

    /// <summary>A replay the driver refused is not at parity whatever its trace says:
    /// the refused step reads the same either side as the journal's only by accident of
    /// what the decision changed, and the refusal is the verdict.</summary>
    [GameFact]
    public void ARefusedReplayNeverReadsParity()
    {
        InScratch(directory =>
        {
            var (manifestPath, _) = RecordingWithAJournal(directory);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            var last = manifest["actions"]!.AsArray().Single(action => action!["seq"]!.GetValue<int>() == 49)!;
            last["args"]!["card_id"] = "CARD.DEFEND_IRONCLAD";
            File.WriteAllText(manifestPath, manifest.ToJsonString());

            var result = Arbiter.Run("parity", manifestPath, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains("REFUSED - the replay was rejected", result.Output, StringComparison.Ordinal);
            Assert.Contains("action 49 (PlayCard)", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("PARITY - every decision replays", result.Output, StringComparison.Ordinal);
            Assert.Contains("NOT AT PARITY", result.Output, StringComparison.Ordinal);
        });
    }

    [GameFact]
    public void AnEmptyCorpusIsRefusedRatherThanReadAsAtParity()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "Runmobile");
            Directory.CreateDirectory(corpus);
            var outDir = Path.Combine(directory, "evidence");

            var result = Arbiter.Run("parity", "--corpus", corpus, "--out", outDir);

            Assert.False(result.Verified, result.All);
            Assert.Contains("No *.replay.json was found in ", result.All, StringComparison.Ordinal);
            Assert.Contains("so nothing was verified; a player's recordings live under Runmobile/<profile scope>/recordings/", result.All, StringComparison.Ordinal);
            Assert.DoesNotContain("AT PARITY", result.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outDir, "parity.json")));
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


    /// <summary>The same recording under another run id, manifest and journal both,
    /// so a scratch corpus can hold it twice without the child artifacts colliding.</summary>
    private static string CopyUnder(string manifestPath, string journalPath, string directory, string runId)
    {
        var manifestCopy = Path.Combine(directory, $"{runId}{RecordingLibrary.ManifestExtension}");
        var journalCopy = Path.Combine(directory, $"{runId}{RunJournal.FileExtension}");
        File.WriteAllText(manifestCopy, File.ReadAllText(manifestPath).Replace(ShortRun, runId, StringComparison.Ordinal));
        File.WriteAllText(journalCopy, File.ReadAllText(journalPath).Replace(ShortRun, runId, StringComparison.Ordinal));
        return journalCopy;
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
