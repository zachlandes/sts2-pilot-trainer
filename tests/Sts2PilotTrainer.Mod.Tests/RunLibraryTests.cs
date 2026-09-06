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
    /// The Compendium's cheap question and the list it opens are the same walk, so they
    /// cannot disagree about which runs are listed. Asserted as an equivalence rather
    /// than as a value, because what is at stake is that one answer never outruns the
    /// other - not what this particular game's verdicts happen to be.
    /// </summary>
    [GameFact]
    public void TheCheapCheckAnswersExactlyWhenTheListWouldHoldARow()
    {
        Assert.Equal(RunLibrary.Runs().Any(run => run.Listed), RunLibrary.HasAnythingToShow());

        Write("native-a-20260906-120000.replay.json", ManifestJson.Serialize(Recording("native-a")));
        Write("native-b-20260906-130000.replay.json", ManifestJson.Serialize(Recording("native-b")));

        var runs = RunLibrary.Runs();
        Assert.Contains(runs, run => run.RunId == "native-a");
        Assert.Equal(runs.Any(run => run.Listed), RunLibrary.HasAnythingToShow());
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
