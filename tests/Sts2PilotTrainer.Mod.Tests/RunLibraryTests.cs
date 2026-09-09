using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the library reads off this computer and the one thing it writes.
///
/// The store is pointed at the test's own temporary directory, so what is being tested
/// is the rule rather than where the game says <c>user://</c> is. Two things matter
/// here and neither is visible from the pure derivation: a recording this build cannot
/// read is left out of the list rather than shown as a run nobody can open, and the
/// progress record survives a round trip through a real file.
///
/// They still need the game's assembly, because every path here reports what it skipped
/// through the game's own log. Started explicitly rather than left to whichever test ran
/// first: one of these failed on its own and passed in a full run, which is a test suite
/// that cannot be trusted about which of its members is broken.
/// </summary>
public sealed class RunLibraryStoreTests : IDisposable
{
    private const string Seed = "SFXT47K77RFK";
    private const string Build = "v0.111.0";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-library-{Guid.NewGuid():N}", "Runmobile", "steam", "account",
        "profile1");

    public RunLibraryStoreTests()
    {
        _ = EngineHost.StartupPhase();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
        RunLibrary.ResetSharedRunsForTesting();
    }

    public void Dispose()
    {
        RunLibrary.ResetSharedRunsForTesting();
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [GameFact]
    public void AnEmptyStoreHoldsNoRecordingsAndNoProgress()
    {
        Assert.Empty(RunLibraryStore.MyRecordings());
        Assert.Equal(0, RunLibraryStore.MyRunsBytes());
        Assert.Empty(RunLibraryStore.ReadProgress().FightsPlayed);
    }

    /// <summary>
    /// A journal is a run still being played. There is nothing to play from until the
    /// manifest is written, so it is not in the list - and it is still counted, because
    /// the footer's number is a size of exactly what removing the player's runs would
    /// remove.
    /// </summary>
    [GameFact]
    public void OnlyFinishedRecordingsAreListedAndAllOfTheirFilesAreCounted()
    {
        var manifest = ManifestJson.Serialize(Recording("native-a"));
        Write("native-a-20260906-120000.replay.json", manifest);
        Write("native-b-20260906-130000.journal.jsonl", "{}");

        var recording = Assert.Single(RunLibraryStore.MyRecordings());
        Assert.Equal("native-a", recording.Recording.RunId);
        Assert.True(RunLibraryStore.MyRunsBytes() > manifest.Length);
    }

    /// <summary>
    /// The ordering is the run's own start, read back out of the name by the owner that
    /// composed it - not the file's modification time, which does not survive the
    /// library being copied to another machine.
    /// </summary>
    [GameFact]
    public void RecordingsComeBackNewestRunFirstByWhenTheRunBegan()
    {
        Write("native-old-20260901-090000.replay.json", ManifestJson.Serialize(Recording("native-old")));
        Write("native-new-20260906-120000.replay.json", ManifestJson.Serialize(Recording("native-new")));

        var recordings = RunLibraryStore.MyRecordings();

        Assert.Equal(["native-new", "native-old"], recordings.Select(stored => stored.Recording.RunId));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero), recordings[0].Started);
    }

    /// <summary>
    /// A file whose name is not one the recorder wrote is not a recording: it is not
    /// listed, not read, and not counted against the player's disk - which matters
    /// because the footer's number is a size of exactly what removing their runs would
    /// remove.
    /// </summary>
    [GameFact]
    public void AFileTheRecorderDidNotNameIsNotARunAndIsNotCounted()
    {
        Write("my-notes.replay.json", ManifestJson.Serialize(Recording("my-notes")));

        Assert.Empty(RunLibraryStore.MyRecordings());
        Assert.Equal(0, RunLibraryStore.MyRunsBytes());
    }

    /// <summary>
    /// Refusing rather than approximating, at the one moment a player is deciding
    /// whether the mod works: a file this build cannot read is left out and named in
    /// the log, never listed as a run that would fail when opened.
    /// </summary>
    [GameFact]
    public void ARecordingThisBuildCannotReadIsLeftOutRatherThanListed()
    {
        Write("native-good-20260906-120000.replay.json", ManifestJson.Serialize(Recording("native-good")));
        Write("native-bad-20260906-130000.replay.json", "{\"manifest_version\": 9999}");

        var recordings = RunLibraryStore.MyRecordings();

        Assert.Equal(["native-good"], recordings.Select(stored => stored.Recording.RunId));
    }

    /// <summary>The browser remains reachable when no local or bundled run can provide
    /// its entry, because automatic index fetching and direct code lookup begin there.</summary>
    [GameFact]
    public void AnEmptyLibraryStillShowsTheBrowserEntry()
    {
        Assert.Empty(RunLibraryStore.StoredRunIds());
        Assert.True(CompendiumCard.ShowsButton());
    }

    /// <summary>
    /// A press on a run-history row opens only the recordings that could be that run.
    ///
    /// Proved by leaving a recording of another seed on disk whose manifest this build
    /// cannot parse: a walk of everything stored would have read and refused it, and
    /// narrowing on the seed first never opens it. That narrowing is what keeps the cost
    /// of a press proportional to the runs on one seed rather than to the fifty a player
    /// may keep.
    /// </summary>
    [GameFact]
    public void APressReadsOnlyTheRecordingsOfItsOwnSeed()
    {
        Write("native-SFXT47K77RFK-20260906-120000.replay.json", ManifestJson.Serialize(
            Recording("native-SFXT47K77RFK-20260906-120000")));
        Write("native-OTHERSEED-20260906-130000.replay.json", "{\"manifest_version\": 9999}");

        Assert.Equal(
            ["native-SFXT47K77RFK-20260906-120000"],
            RunLibraryStore.StoredRunIdsOn(Seed));

        var found = RunHistoryPlateHost.RecordingOf(Wanted());

        Assert.Equal("native-SFXT47K77RFK-20260906-120000", found?.RunId);
    }

    /// <summary>
    /// Two runs on one seed with the same character, ascension and build are
    /// indistinguishable from a history row, and the honest answer is none rather than
    /// the first of them.
    /// </summary>
    [GameFact]
    public void TwoRecordingsAHistoryRowCannotBeToldApartAnswerNone()
    {
        Write("native-SFXT47K77RFK-20260906-120000.replay.json", ManifestJson.Serialize(
            Recording("native-SFXT47K77RFK-20260906-120000")));
        Write("native-SFXT47K77RFK-20260906-130000.replay.json", ManifestJson.Serialize(
            Recording("native-SFXT47K77RFK-20260906-130000")));

        Assert.Null(RunHistoryPlateHost.RecordingOf(Wanted()));
    }

    /// <summary>A run more than one person played is a run no recording is a recording
    /// of, because the recorder does not attach to one.</summary>
    [GameFact]
    public void ARunWithNoSinglePlayerCharacterMatchesNothing()
    {
        Write("native-SFXT47K77RFK-20260906-120000.replay.json", ManifestJson.Serialize(
            Recording("native-SFXT47K77RFK-20260906-120000")));

        Assert.Null(RunHistoryPlateHost.RecordingOf(Wanted() with { Character = null }));
    }

    /// <summary>
    /// A run the console was used in says on its plate why it cannot be submitted, and a
    /// recording made before the recorder could tell says nothing rather than "clean".
    ///
    /// The reading is the subject, so the two facts this takes off the process rather
    /// than off the recording - which build this game is, and whether a run is live - are
    /// supplied here. Neither is a fact about the recording, and leaving them to whatever
    /// the test process happens to be would decide a state on something nobody is
    /// asserting.
    /// </summary>
    [GameFact]
    public void ARunTheConsoleWasUsedInSaysSoAndOneThatStatesNothingDoesNot()
    {
        var recorded = BareRecording("native-quiet");

        // A recording from before the recorder could tell states no integrity at all,
        // which is not the same as stating that nothing happened.
        var quiet = recorded with
        {
            Source = recorded.Source with { Native = recorded.Source.Native! with { Integrity = null! } },
        };
        var console = quiet with
        {
            Source = quiet.Source with
            {
                Native = quiet.Source.Native! with { Integrity = NativeSource.NonStandardIntegrity },
            },
        };

        var consoleFacts = RunHistoryPlateHost.FactsFor(null, console);
        var quietFacts = RunHistoryPlateHost.FactsFor(null, quiet);

        Assert.True(consoleFacts.ConsoleUsed);
        Assert.Null(quietFacts.ConsoleUsed);

        var consolePlate = RunHistoryPlate.For(AsIfHere(consoleFacts))!;
        Assert.Null(consolePlate.Head);
        Assert.False(consolePlate.Rows[^1].Enabled);
        Assert.Equal(LibraryCopy.PlateConsoleUsed, consolePlate.Reason);

        var quietPlate = RunHistoryPlate.For(AsIfHere(quietFacts))!;
        Assert.Null(quietPlate.Head);
        Assert.Equal(LibraryCopy.PlateSubmitComing, quietPlate.Reason);
    }

    /// <summary>The same facts with this process's own two readings settled, so what
    /// decides the state is what the recording says.</summary>
    private static RunHistoryFacts AsIfHere(RunHistoryFacts facts) =>
        facts with { RecordedBuild = facts.ThisBuild, RunInProgress = false };

    /// <summary>What the game's history says about the run these recordings are of.
    /// The values are the fixture's own; nothing here is under test but the
    /// matching.</summary>
    private static RunHistoryPlateHost.HistoryIdentity Wanted() =>
        new(Seed, Ascension: 0, Build, "CHARACTER.IRONCLAD");

    /// <summary>A run still being played has a journal and no manifest, so it is not a
    /// run the cheap question can count.</summary>
    [GameFact]
    public void ARunStillBeingPlayedIsNotOneOfTheStoredRunIds()
    {
        Write("native-b-20260906-130000.journal.jsonl", "{}");

        Assert.Empty(RunLibraryStore.StoredRunIds());
    }

    /// <summary>
    /// Opening a run reads that run's manifest and no other's. Proved by leaving an
    /// unreadable recording beside it: a walk of the whole set would have logged and
    /// skipped it, and resolving by id never opens it at all.
    /// </summary>
    [GameFact]
    public void ARecordingIsResolvedByIdFromOneFile()
    {
        Write(
            "native-a-20260906-120000.replay.json",
            ManifestJson.Serialize(Recording("native-a-20260906-120000")));
        Write("native-bad-20260906-130000.replay.json", "{\"manifest_version\": 9999}");

        Assert.Equal(
            "native-a-20260906-120000",
            RunLibraryStore.RecordingFor("native-a-20260906-120000")?.RunId);
        Assert.Null(RunLibraryStore.RecordingFor("native-bad-20260906-130000"));
        Assert.Null(RunLibraryStore.RecordingFor("nobody"));
    }

    [GameFact]
    public void ProgressIsWrittenOnceAndReadBack()
    {
        Assert.True(RunLibraryStore.RecordFightPlayed("native-a", 2));
        Assert.False(RunLibraryStore.RecordFightPlayed("native-a", 2));

        Assert.Equal([2], RunLibraryStore.ReadProgress().PlayedFrom("native-a"));
        Assert.True(RunmobileStore.Exists(RunProgress.FileName));
    }

    /// <summary>
    /// Losing this file loses the pips and the number in Continue, and nothing else, so
    /// a record this build cannot read is forgotten rather than guessed at - and never
    /// takes a player out of the fight they were about to enter.
    /// </summary>
    [GameFact]
    public void AProgressRecordThisBuildCannotReadIsForgottenRatherThanFatal()
    {
        RunmobileStore.Write(RunProgress.FileName, """{"schema":"somebody-elses/v1"}""");

        Assert.Empty(RunLibraryStore.ReadProgress().FightsPlayed);
        Assert.True(RunLibraryStore.RecordFightPlayed("native-a", 1));
    }

    [GameFact]
    public void ADownloadedRecordingMustMatchItsAdvertisedImmutableIdentity()
    {
        var recording = Recording("shared-a");
        var manifestJson = ManifestJson.Serialize(recording);
        var submission = new ShareSubmission("Run", "", "Ada", true);
        var shareId = SharedRunIdentity.For(manifestJson, submission);
        var shared = new SharedRun(
            shareId,
            SharedRunIdentity.CodeFor(shareId),
            ManifestJson.Serialize(recording with { RunId = "different-run" }),
            submission,
            LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed),
            DateTimeOffset.Parse("2026-09-07T12:00:00Z"));

        var error = Assert.Throws<ShareValidationException>(() =>
            RunLibrary.AcceptShared(shared, shared.Code));

        Assert.Contains("advertised sharing identity", error.Message, StringComparison.Ordinal);
    }

    [GameFact]
    public void ADownloadedRecordingWithoutValidSubmissionConsentIsRefused()
    {
        var recording = Recording("shared-a");
        var manifestJson = ManifestJson.Serialize(recording);
        var submission = new ShareSubmission("Run", "", "Ada", false);
        var shareId = SharedRunIdentity.For(manifestJson, submission);
        var shared = new SharedRun(
            shareId,
            SharedRunIdentity.CodeFor(shareId),
            manifestJson,
            submission,
            LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed),
            DateTimeOffset.Parse("2026-09-07T12:00:00Z"));

        var error = Assert.Throws<ShareValidationException>(() =>
            RunLibrary.AcceptShared(shared, shared.Code));

        Assert.Contains("CC0 consent", error.Message, StringComparison.Ordinal);
    }

    [GameFact]
    public void IndexRefreshPreservesAnAcceptedExactCodeResult()
    {
        var recording = Recording($"exact-{Guid.NewGuid():N}");
        var manifestJson = ManifestJson.Serialize(recording);
        var submission = new ShareSubmission("Run", "", "Ada", true);
        var shareId = SharedRunIdentity.For(manifestJson, submission);
        var shared = new SharedRun(
            shareId,
            SharedRunIdentity.CodeFor(shareId),
            manifestJson,
            submission,
            LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Absent),
            DateTimeOffset.Parse("2026-09-07T12:00:00Z"));

        RunLibrary.AcceptShared(shared, shared.Code);
        RunLibrary.AcceptIndex([]);

        var listed = Assert.Single(RunLibrary.Runs(), run => run.EntryId == shared.ShareId);
        Assert.Equal(shared.Code, listed.ShareCode);
    }

    [GameFact]
    public void IndexRefreshCuratesVerifiedDownloadsWithoutReplacingTheirMetadataOrOrder()
    {
        var downloaded = Shared(Recording($"downloaded-{Guid.NewGuid():N}"));
        var cachedOnly = Shared(Recording($"cached-{Guid.NewGuid():N}"));
        var indexedOnly = Shared(Recording($"indexed-{Guid.NewGuid():N}"));
        RunLibrary.AcceptShared(downloaded, downloaded.Code);
        RunLibrary.AcceptShared(cachedOnly, cachedOnly.Code);
        var currentTime = downloaded.SubmittedAt.AddDays(1);
        var stale = SummaryFor(downloaded) with
        {
            Submission = downloaded.Submission with { DisplayName = "Wrong creator" },
            Run = downloaded.Run with
            {
                Character = "CHARACTER.SILENT",
                Fights = [99],
            },
            Environment = downloaded.Summary.Environment with
            {
                Character = downloaded.Summary.Environment.Character with
                {
                    Value = "CHARACTER.SILENT",
                },
            },
            SubmittedAt = currentTime,
            Featured = true,
        };
        var first = SummaryFor(indexedOnly) with { Featured = true };

        RunLibrary.AcceptIndex([first, stale]);

        var online = RunLibrary.Runs()
            .Where(run => run.ShareId is not null)
            .ToList();
        Assert.Equal(
            [indexedOnly.ShareId, downloaded.ShareId, cachedOnly.ShareId],
            online.Select(run => run.ShareId));
        var listed = online[1];
        Assert.Equal(downloaded.Submission.DisplayName, listed.Creator);
        Assert.Equal(downloaded.Run.Character, listed.Character);
        Assert.Equal(downloaded.Run.Fights, listed.Fights);
        Assert.Equal(RunOrigin.Featured, listed.Origin);
        Assert.Equal(currentTime, listed.Recorded);

        var reopened = RunLibrary.AcceptShared(downloaded, downloaded.Code);
        Assert.Equal(downloaded.ManifestJson, reopened.ManifestJson);
        Assert.Equal(downloaded.Submission, reopened.Submission);
        Assert.Equal(RunOrigin.Featured, reopened.Run.Origin);
        Assert.Equal(currentTime, reopened.Run.Recorded);
    }

    [GameFact]
    public void SharedEntriesWithOneManifestRunIdRemainDistinct()
    {
        var first = Shared(Recording("same-run", "FIRSTSEED"));
        var second = Shared(Recording("same-run", "SECONDSEED"));

        RunLibrary.AcceptShared(first);
        RunLibrary.AcceptShared(second);

        var entries = RunLibrary.Runs()
            .Where(run => run.EntryId == first.ShareId || run.EntryId == second.ShareId)
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(
            "FIRSTSEED",
            RunLibrary.RecordingFor(first.ShareId)!.Environment.Seed.Value);
        Assert.Equal(
            "SECONDSEED",
            RunLibrary.RecordingFor(second.ShareId)!.Environment.Seed.Value);
    }

    [GameFact]
    public void SharingStateDoesNotCrossProfileOrEndpointBoundaries()
    {
        ConfigureEndpoint(_root, "https://one.example.test/v1/");
        var shared = Shared(Recording($"scoped-{Guid.NewGuid():N}"));
        var summary = SummaryFor(shared);
        var firstScope = RunLibrary.CurrentSharingScope();
        RunLibrary.AcceptIndex([summary]);
        RunLibrary.AcceptShared(shared, expectedScope: firstScope);
        Assert.False(RunLibrary.ShouldFetchIndex);

        ConfigureEndpoint(_root, "https://two.example.test/v1/");
        Assert.True(RunLibrary.SharingAvailable);
        Assert.True(RunLibrary.ShouldFetchIndex);
        Assert.DoesNotContain(RunLibrary.Runs(), run => run.EntryId == shared.ShareId);
        Assert.False(RunLibrary.AcceptIndex([summary], firstScope));
        Assert.Throws<ShareValidationException>(() =>
            RunLibrary.AcceptShared(shared, expectedScope: firstScope));

        var secondScope = RunLibrary.CurrentSharingScope();
        RunLibrary.AcceptIndex([summary], secondScope);
        Assert.False(RunLibrary.ShouldFetchIndex);
        var secondProfile = Path.Combine(Path.GetDirectoryName(_root)!, "profile2");
        Directory.CreateDirectory(secondProfile);
        ConfigureEndpoint(secondProfile, "https://two.example.test/v1/");

        Assert.True(RunLibrary.SharingAvailable);
        Assert.True(RunLibrary.ShouldFetchIndex);
        Assert.DoesNotContain(RunLibrary.Runs(), run => run.EntryId == shared.ShareId);
    }

    [GameFact]
    public void DownloadedRunMustMatchManifestDerivedIndexMetadata()
    {
        var shared = Shared(Recording($"bound-{Guid.NewGuid():N}"));
        var summary = SummaryFor(shared);
        var differentEnvironment = summary.Environment with
        {
            Seed = summary.Environment.Seed with { Value = "DIFFERENT" },
        };
        IReadOnlyList<SharedRunSummary> mismatches =
        [
            summary with { Run = summary.Run with { RunId = "different-run" } },
            summary with { Run = summary.Run with { Character = "CHARACTER.SILENT" } },
            summary with { Run = summary.Run with { Fights = [1] } },
            summary with { Run = summary.Run with { Outcome = LibraryRun.WonOutcome } },
            summary with { Environment = differentEnvironment },
            summary with { SourceKind = "video" },
            summary with
            {
                Submission = summary.Submission with { Description = "Different" },
            },
        ];

        foreach (var mismatch in mismatches)
        {
            RunLibrary.AcceptIndex([mismatch]);
            var error = Assert.Throws<ShareValidationException>(() =>
                RunLibrary.AcceptShared(shared, shared.Code));
            Assert.Contains("run index entry", error.Message, StringComparison.Ordinal);
        }
    }

    [GameFact]
    public void DownloadedRunDoesNotTreatCurationAsManifestIdentity()
    {
        var shared = Shared(Recording($"curated-{Guid.NewGuid():N}"));
        var advertised = SummaryFor(shared) with
        {
            SubmittedAt = shared.SubmittedAt.AddDays(1),
            Featured = !shared.Featured,
        };
        RunLibrary.AcceptIndex([advertised]);

        var accepted = RunLibrary.AcceptShared(shared, shared.Code);

        Assert.Equal(shared.ShareId, accepted.ShareId);
    }

    [GameFact]
    public void LightweightIndexIdentityUsesTheEnvironmentPreflight()
    {
        var recording = Recording("shared-a");
        var wrongEnvironment = recording.Environment with
        {
            ContentHash = recording.Environment.ContentHash with { Value = "different-content" },
        };

        Assert.Equal(
            RunVerdict.Failed,
            RunVerdicts.For(wrongEnvironment, recording.Source.Kind, recording.RunId, Build));
    }

    [GameFact]
    public void SharedProgressAndIndexAreScopedToTheCurrentProfile()
    {
        var runId = $"shared-progress-{Guid.NewGuid():N}";
        var recording = Recording(runId);
        var submission = new ShareSubmission("Run", "", "Ada", true);
        var shareId = SharedRunIdentity.For(ManifestJson.Serialize(recording), submission);
        var summary = new SharedRunSummary(
            shareId,
            SharedRunIdentity.CodeFor(shareId),
            submission,
            LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed, [99]) with
            {
                Floors = [2, 6],
            },
            recording.Environment,
            recording.Source.Kind,
            DateTimeOffset.Parse("2026-09-07T12:00:00Z"),
            Featured: false);

        try
        {
            RunLibrary.AcceptIndex([summary]);
            Assert.Empty(RunLibrary.Runs().Single(run => run.RunId == runId).FightsPlayed);

            Assert.True(RunLibraryStore.RecordFightPlayed(shareId, 2));
            Assert.True(RunLibraryStore.RecordFloorLoaded(shareId, 6));
            var progressed = RunLibrary.Runs().Single(run => run.RunId == runId);
            Assert.Equal([2], progressed.FightsPlayed);
            Assert.Equal(6, progressed.LastFloorReplayed);

            var secondProfile = Path.Combine(Path.GetDirectoryName(_root)!, "profile2");
            Directory.CreateDirectory(secondProfile);
            RunmobileStore.UseRootForTesting(secondProfile);
            Assert.DoesNotContain(RunLibrary.Runs(), run => run.RunId == runId);
        }
        finally
        {
            RunmobileStore.UseRootForTesting(_root);
            RunLibrary.AcceptIndex([]);
        }
    }

    private static SharedRun Shared(ReplayManifest recording)
    {
        var manifestJson = ManifestJson.Serialize(recording);
        var submission = new ShareSubmission("Run", "", "Ada", true);
        var shareId = SharedRunIdentity.For(manifestJson, submission);
        return new SharedRun(
            shareId,
            SharedRunIdentity.CodeFor(shareId),
            manifestJson,
            submission,
            LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Absent) with
            {
                Creator = submission.DisplayName,
            },
            DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
    }

    private static SharedRunSummary SummaryFor(SharedRun shared) => shared.Summary;

    private static void ConfigureEndpoint(string root, string endpoint)
    {
        RunmobileStore.UseRootForTesting(root);
        RunmobileStore.Write(
            RunmobileSettings.FileName,
            $$"""{"schema":"{{RunmobileSettings.Schema}}","sharing_service_url":"{{endpoint}}"}""");
    }

    private void Write(string name, string content) =>
        RunmobileStore.Write($"{RunLibraryStore.RecordingsDirectory}/{name}", content);

    internal static ReplayManifest BareRecording(string runId) => Recording(runId);

    private static ReplayManifest Recording(string runId, string seed = Seed) => new()
    {
        RunId = runId,
        Environment = Identity(seed),
        Source = new SourceProvenance
        {
            Kind = "native",
            ExtractionMethod = "captured",
            Coverage = "the whole run",
            Native = new NativeSource
            {
                RecorderVersion = "runmobile-recorder/0.1.0",
                WitnessedRunStart = Fact<bool>.Captured(true, FactEvidence.AtActionOrdinal(-1, 0)),
                Continuity = NativeSource.ContinuousContinuity,
                Integrity = NativeSource.CompleteIntegrity,
                Outcome = "abandoned",
            },
        },
        Actions = [],
        Checkpoints = [],
    };

    /// <summary>The identity a recording carries. Nothing here is under test - the
    /// recordings exist so there is something on disk for the store to find.</summary>
    private static EnvironmentIdentity Identity(string seed)
    {
        var evidence = FactEvidence.AtActionOrdinal(-1);
        return new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Captured("v0.111.0", evidence),
            BuildDateUtc = Fact<string>.Captured("2026.08.14", evidence),
            GameMode = Fact<string>.Captured("standard", evidence),
            Seed = Fact<string>.Captured(seed, evidence),
            ContentHash = Fact<string>.Captured("1568834832", evidence),
            Ascension = Fact<int>.Captured(0, evidence),
            Unlocks = Fact<UnlockRequirement>.Captured(
                UnlockRequirement.Complete("no unlock requirement is under test here"), evidence),
            Character = Fact<string>.Captured("CHARACTER.IRONCLAD", evidence),
            Acts = Fact<IReadOnlyList<string>>.Captured(["ACT.UNDERDOCKS"], evidence),
            Mods = Fact<ModEnvironment>.Captured(
                new ModEnvironment { Name = "written by this test", ReportedCount = 0, Mods = [] },
                evidence),
        };
    }
}

public sealed class OnlineIndexTests
{
    [Fact]
    public void FeaturedAndRecentUseTransportClassificationAndSubmissionTime()
    {
        var recording = RunLibraryStoreTests.BareRecording("source");
        var featured = Summary(
            recording,
            Run("featured", RunOrigin.Mine, recorded: null),
            submitted: "2026-09-01T00:00:00Z",
            featured: true);
        var newer = Summary(
            recording,
            Run("newer", RunOrigin.Featured, recorded: null),
            submitted: "2026-09-03T00:00:00Z");
        var older = Summary(
            recording,
            Run("older", RunOrigin.Mine, recorded: DateTimeOffset.MaxValue),
            submitted: "2026-09-02T00:00:00Z");
        var accepted = new[] { featured, older, newer }
            .Select(item => RunLibrary.OnlineRun(item, RunVerdict.Passed))
            .ToArray();

        var browser = RunBrowser.For(LibraryTab.Community, accepted, "v0.111.0");

        Assert.Equal(
            [LibraryCopy.FeaturedGroup, LibraryCopy.RecentGroup],
            browser.Groups.Select(group => group.Heading));
        var acceptedFeatured = Assert.Single(browser.Groups[0].Runs);
        Assert.Equal("featured", acceptedFeatured.RunId);
        Assert.Equal("Ada", acceptedFeatured.Creator);
        Assert.Equal(recording.Environment.Character.Value, acceptedFeatured.Character);
        Assert.Equal(recording.Environment.Ascension.Value, acceptedFeatured.Ascension);
        Assert.Equal(recording.Environment.BuildVersion.Value, acceptedFeatured.RecordedBuild);
        Assert.Empty(acceptedFeatured.FightsPlayed);
        Assert.Equal(["newer", "older"], browser.Groups[1].Runs.Select(run => run.RunId));
    }

    [Fact]
    public void OnlineMetadataWithoutValidSubmissionConsentIsRefused()
    {
        var recording = RunLibraryStoreTests.BareRecording("source");
        var summary = Summary(
            recording,
            Run("run", RunOrigin.Recent, recorded: null),
            submitted: "2026-09-01T00:00:00Z") with
        {
            Submission = new ShareSubmission("Run", "", "Ada", false),
        };

        Assert.Throws<ShareValidationException>(() =>
            RunLibrary.OnlineRun(summary, RunVerdict.Passed));
    }

    private static SharedRunSummary Summary(
        ReplayManifest recording,
        LibraryRun run,
        string submitted,
        bool featured = false) =>
        new(
            run.RunId,
            run.RunId,
            new ShareSubmission("Run", "", "Ada", true),
            run,
            recording.Environment,
            recording.Source.Kind,
            DateTimeOffset.Parse(submitted),
            featured);

    private static LibraryRun Run(
        string runId, RunOrigin origin, DateTimeOffset? recorded) =>
        new(
            runId,
            origin,
            "Wrong creator",
            "WRONG.CHARACTER",
            99,
            "wrong-build",
            [1],
            "won",
            false,
            RunVerdict.Unjudged,
            [99],
            recorded);
}

/// <summary>
/// What the run view's rows carry by the time the screen has them.
///
/// The design's second lines are derived by <c>RunView</c> and were, for a while,
/// dropped on the way to the screen - four rows reading "Play from this fight", "Play
/// from this floor", "Continue: play from fight N" and "Start the run over" with nothing
/// saying where any of them goes. This is the seam that lost them.
/// </summary>
public sealed class RunViewRowMappingTests
{
    /// <summary>
    /// A fight shown this sitting carries the hollow-eye mark on its row, and the
    /// sentence behind the mark names the creator. The set is the Combat Trainer's,
    /// in memory; the screen only draws what it is handed.
    /// </summary>
    [Fact]
    public void AFightShownThisSittingCarriesTheMarkAndItsSentence()
    {
        var marked = RunBrowserScreen.EnteringRows(
            RunView.For(Recording(), RunProgress.Empty, selectedFloor: 2, shownThisSitting: [1]),
            "native-a", RecordingCredit.Named("NaveGreed"));
        var cold = RunBrowserScreen.EnteringRows(
            RunView.For(Recording(), RunProgress.Empty, selectedFloor: 2), "native-a", RecordingCredit.Named("NaveGreed"));

        Assert.Equal(
            "You have seen NaveGreed's fight this sitting. It is cold again next time you launch the game.",
            marked[0].MarkTooltip);
        Assert.All(marked.Skip(1), row => Assert.Null(row.MarkTooltip));
        Assert.All(cold, row => Assert.Null(row.MarkTooltip));
    }

    /// <summary>
    /// Every second line the run view derived reaches the screen unchanged. The
    /// play-from row's is the whole point of the single-row shape - it is what names the
    /// selected floor and what it held - so a screen that dropped it would leave three
    /// rows a player has to guess between.
    /// </summary>
    [Fact]
    public void EveryRunViewRowReachesTheScreenCarryingItsSecondLine()
    {
        var view = RunView.For(Recording(), RunProgress.Empty, selectedFloor: 2);

        var rows = RunBrowserScreen.EnteringRows(view, "native-a");

        Assert.Equal(view.Rows.Select(row => row.Label), rows.Select(row => row.Label));
        Assert.Equal(view.Rows.Select(row => row.Note), rows.Select(row => row.Note));
        Assert.Equal(LibraryCopy.FloorLine(2, FloorKind.Combat), rows[0].Note);
    }

    /// <summary>A refused row's reason reaches the screen in place of the second line:
    /// a row that gave both would be saying where it goes and that it does not go
    /// there.</summary>
    [Fact]
    public void ARefusedRowCarriesItsReasonAndNoSecondLine()
    {
        var view = RunView.For(Recording(), RunProgress.Empty, selectedFloor: 1);

        var floor = RunBrowserScreen.EnteringRows(view, "native-a")
            .Single(row => row.Label == LibraryCopy.PlayFromThisFloor);

        Assert.False(floor.Enabled);
        Assert.Null(floor.Note);
        Assert.Equal(LibraryCopy.RunStartsHere, floor.Reason);
    }

    /// <summary>Floors 1 and 2, with a finished fight on floor 2, so every one of the
    /// four ways in is drawn.</summary>
    private static ReplayManifest Recording() =>
        RunLibraryStoreTests.BareRecording("native-a") with
        {
            Boundaries =
            [
                ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Fact<string>.Engine("floor-2")),
                ReplayBoundary.CombatStart(fight: 1, afterSeq: 10, Fact<string>.Engine("fight-1")),
            ],
        };
}

/// <summary>
/// The library module's own claims about this build.
///
/// A surface that silently failed to appear is a feature a player cannot find and
/// cannot be told about, so the module establishes that its two hooks are still here
/// before it installs anything. These assert that against the loaded assembly rather
/// than against a name list, which is the only reading that answers the real question:
/// will the patch attach.
/// </summary>
public sealed class RunLibraryModuleTests
{
    [GameFact]
    public void EveryMemberTheLibraryHangsOnIsInThisBuild()
    {
        _ = EngineHost.StartupPhase();

        // Named rather than counted, because an empty refusal list is also what a walk
        // that found nothing to check returns: both of the library's patch classes put
        // the type on the class and the method name on the method, and a reader that
        // saw only class attributes reported them clean on every build.
        Assert.Equal(
            [
                "NCompendiumSubmenu.OnSubmenuOpened",
                "NCompendiumSubmenu._Ready",
                "NMainMenu.OnSubmenuStackChanged",
                "NMainMenu.RefreshButtons",
                "NMainMenu._Ready",
                "NMapPointHistoryEntry._Ready",
                "NSettingsScreen._Ready",
            ],
            PatchTargets.Targets(RunLibraryModule.PatchClasses).Order(StringComparer.Ordinal));
        Assert.Empty(PatchTargets.Unresolvable(RunLibraryModule.PatchClasses));
        Assert.True(RunLibraryModule.Instance.Enabled);
        Assert.Null(RunLibraryModule.Instance.Refusal);
    }

    /// <summary>
    /// The main-menu row re-decides where the game shows its button column again, and
    /// not only where the game refreshes its own buttons.
    ///
    /// <c>RefreshButtons</c> is called exactly twice in this build - at the end of
    /// <c>_Ready</c> and after a run is abandoned - so a row that followed it alone kept
    /// whatever it was told at construction. The client showed that: turning the setting
    /// off and walking back out of Settings left the row on the menu until the next
    /// launch. Named here rather than counted, so removing the hook fails this test
    /// instead of quietly restoring the defect.
    /// </summary>
    [GameFact]
    public void TheMainMenuRowFollowsTheHookThatFiresPerVisit()
    {
        _ = EngineHost.StartupPhase();

        var targets = PatchTargets.Targets([typeof(MainMenuLibraryRow)]);

        Assert.Contains("NMainMenu.OnSubmenuStackChanged", targets);
        Assert.Contains("NMainMenu.RefreshButtons", targets);
        Assert.Empty(PatchTargets.Unresolvable([typeof(MainMenuLibraryRow)]));
    }

    /// <summary>
    /// Deciding whether the row belongs on the menu must not need an adopted game.
    ///
    /// The main menu is built one startup phase before the game has a model database, so
    /// adoption there refuses and latches that refusal for the process. Visibility needs
    /// only the settings file and the game's run count, both supplied here without a
    /// running game.
    /// </summary>
    [Fact]
    public void MainMenuVisibilityDoesNotNeedAnAdoptedGame()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runmobile-menu-{Guid.NewGuid():N}");
        RunmobileStore.UseRootForTesting(root);

        try
        {
            RunsFinished.UseReaderForTesting(() => false);
            Assert.True(MainMenuLibraryRow.Shown());

            RunsFinished.UseReaderForTesting(() => true);
            Assert.False(MainMenuLibraryRow.Shown());
        }
        finally
        {
            RunsFinished.UseReaderForTesting(null);
            RunmobileStore.UseRootForTesting(null);
        }
    }

    /// <summary>
    /// A refused adoption keeps the row absent when the menu re-decides visibility.
    /// </summary>
    [GameFact]
    public void RefusedAdoptionKeepsTheMainMenuRowHidden()
    {
        _ = EngineHost.StartupPhase();
        var root = Path.Combine(Path.GetTempPath(), $"runmobile-menu-{Guid.NewGuid():N}");
        RunmobileStore.UseRootForTesting(root);

        try
        {
            RunsFinished.UseReaderForTesting(() => false);
            Assert.False(RunmobileMod.EnsureAdopted());
            Assert.False(MainMenuLibraryRow.Visible());
        }
        finally
        {
            RunsFinished.UseReaderForTesting(null);
            RunmobileStore.UseRootForTesting(null);
        }
    }

    /// <summary>A member this build does not have is named by whichever of the two
    /// declaration styles carries it, so a patch class written the library's way is
    /// checked as well as one written the recorder's.</summary>
    [GameFact]
    public void AMethodLevelTargetThisBuildDoesNotHaveIsRefusedByName()
    {
        _ = EngineHost.StartupPhase();

        var refusal = Assert.Single(PatchTargets.Unresolvable([typeof(AMethodNobodyHas)]));

        Assert.Contains("NoSuchMethodOnThisBuild", refusal, StringComparison.Ordinal);
    }

    [HarmonyPatch(typeof(RunLibraryStoreTests))]
    private static class AMethodNobodyHas
    {
        [HarmonyPatch("NoSuchMethodOnThisBuild")]
        internal static void Postfix()
        {
        }
    }

    /// <summary>The check is a real reading and not a formality: a patch class naming a
    /// member this build does not have is refused, with the member named.</summary>
    [GameFact]
    public void AMemberThisBuildDoesNotHaveIsRefusedByName()
    {
        _ = EngineHost.StartupPhase();

        var refusal = Assert.Single(PatchTargets.Unresolvable([typeof(AMemberNobodyHas)]));

        Assert.Contains("NoSuchMethodOnThisBuild", refusal, StringComparison.Ordinal);
    }

    [HarmonyPatch(typeof(RunLibraryModuleTests), "NoSuchMethodOnThisBuild")]
    private static class AMemberNobodyHas;
}
