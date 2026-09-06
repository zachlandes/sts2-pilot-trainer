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
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-library-{Guid.NewGuid():N}", "Runmobile", "steam", "account",
        "profile1");

    public RunLibraryStoreTests()
    {
        _ = EngineHost.StartupPhase();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
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

    /// <summary>
    /// The Compendium's question reads no manifest, and it never hides a run the list
    /// would hold.
    ///
    /// Proved against a recording whose manifest this build cannot parse at all: nothing
    /// that opened the file could say anything about the run, and the question still
    /// answers yes from the run id in the recorder's directory index. That is the
    /// one-directional promise - a stored recording shows the button whatever judging it
    /// would say, and the browser is then the thing that judges. The opposite direction
    /// is what must never happen: the browser is the only thing that judges and the
    /// button is the only way to the browser, so a card hidden on a remembered negative
    /// closes the way in for good, which is what a per-build verdict cache did here for
    /// two rounds.
    /// </summary>
    [GameFact]
    public void AStoredRecordingShowsTheCardWithoutAnyManifestBeingRead()
    {
        Write("native-a-20260906-120000.replay.json", "{\"manifest_version\": 9999}");

        Assert.Equal(["native-a-20260906-120000"], RunLibraryStore.StoredRunIds());
        Assert.Empty(RunLibraryStore.MyRecordings());
        Assert.DoesNotContain(RunLibrary.Runs(), run => run.Origin == RunOrigin.Mine);
        Assert.True(RunLibrary.HasAnythingToShow());
    }

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

    private void Write(string name, string content) =>
        RunmobileStore.Write($"{RunLibraryStore.RecordingsDirectory}/{name}", content);

    internal static ReplayManifest BareRecording(string runId) => Recording(runId);

    private static ReplayManifest Recording(string runId) => new()
    {
        RunId = runId,
        Environment = Identity(),
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
                Outcome = "abandoned",
            },
        },
        Actions = [],
        Checkpoints = [],
    };

    /// <summary>The identity a recording carries. Nothing here is under test - the
    /// recordings exist so there is something on disk for the store to find.</summary>
    private static EnvironmentIdentity Identity()
    {
        var evidence = FactEvidence.AtActionOrdinal(-1);
        return new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Captured("v0.111.0", evidence),
            BuildDateUtc = Fact<string>.Captured("2026.08.14", evidence),
            GameMode = Fact<string>.Captured("standard", evidence),
            Seed = Fact<string>.Captured("SFXT47K77RFK", evidence),
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
    [Fact]
    public void EveryRunViewRowReachesTheScreenCarryingItsSecondLine()
    {
        var view = RunView.For(Recording(), RunProgress.Empty);

        var rows = RunBrowserScreen.EnteringRows(view, "native-a");

        Assert.Equal(view.Rows.Select(row => row.Label), rows.Select(row => row.Label));
        Assert.Equal(
            [
                LibraryCopy.PlayFromThisFightNote,
                LibraryCopy.PlayFromThisFloorNote,
                LibraryCopy.ContinueNote,
                LibraryCopy.StartTheRunOverNote,
            ],
            rows.Select(row => row.Note));
    }

    /// <summary>A note says what a row does and a reason says why it is refused; a row
    /// can carry both, and the refused first floor does.</summary>
    [Fact]
    public void ARefusedRowKeepsItsSecondLineBesideItsReason()
    {
        var view = RunView.For(Recording(), RunProgress.Empty, selectedFloor: 1);

        var floor = RunBrowserScreen.EnteringRows(view, "native-a")
            .Single(row => row.Label == LibraryCopy.PlayFromThisFloor);

        Assert.False(floor.Enabled);
        Assert.Equal(LibraryCopy.PlayFromThisFloorNote, floor.Note);
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
                "NMapPointHistoryEntry._Ready",
            ],
            PatchTargets.Targets(RunLibraryModule.PatchClasses).Order(StringComparer.Ordinal));
        Assert.Empty(PatchTargets.Unresolvable(RunLibraryModule.PatchClasses));
        Assert.True(RunLibraryModule.Instance.Enabled);
        Assert.Null(RunLibraryModule.Instance.Refusal);
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
