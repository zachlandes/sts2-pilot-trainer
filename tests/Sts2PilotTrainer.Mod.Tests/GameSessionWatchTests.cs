using System.Reflection;
using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Replay.Tests;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What Runmobile does in a game it is not for, and what it watches for a run the
/// console was used in.
///
/// Both are readings rather than assumptions, and both are checked by driving the
/// state the game's own members put this process in. A test process has no run, so the
/// reading answers "no run in progress" and the latch is what a multiplayer session is
/// driven with - which is exactly what the two patches on the game's multiplayer setup
/// do in the client.
///
/// The latch is process-wide, so these run on their own rather than beside a test that
/// asks the shell for its cards.
/// </summary>
[Collection(nameof(GameSessionWatchTests))]
[CollectionDefinition(nameof(GameSessionWatchTests), DisableParallelization = true)]
public sealed class GameSessionWatchTests : IDisposable
{
    private readonly string _storeRoot = Path.Combine(
        Path.GetTempPath(), $"runmobile-watch-{Guid.NewGuid():N}", "Runmobile", "steam", "0", "profile0");

    public GameSessionWatchTests()
    {
        Directory.CreateDirectory(_storeRoot);
        RunmobileStore.UseRootForTesting(_storeRoot);
    }

    public void Dispose()
    {
        GameSessionWatch.SessionTornDown();
        RunRecorder.RunTornDown();
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _storeRoot[.._storeRoot.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    /// <summary>The passing half: an ordinary client, before any run, is one this mod
    /// may draw its card in.</summary>
    [GameFact]
    public void OutsideAMultiplayerGameTheModDrawsWhatItsModulesContribute()
    {
        _ = EngineHost.StartupPhase();

        Assert.Equal(RunSessionKind.NoRunInProgress, GameSessionWatch.Observed);
        Assert.True(GameSessionWatch.MaySpeak);
        Assert.Equal(RunmobileMod.MenuCardsFrom(RunmobileMod.Modules), RunmobileMod.MenuCards);
        Assert.NotEmpty(RunmobileMod.MenuCards);
    }

    /// <summary>
    /// In a multiplayer game the mod contributes no surface at all.
    ///
    /// Not "no card for the recorder", which has none anyway: every module's surface
    /// goes, because what is suppressed is this mod drawing in a game somebody else is
    /// also playing - an indicator saying a run is not being recorded included. The
    /// recorder's own refusal is the test below and is a separate rule.
    /// </summary>
    [GameFact]
    public void AMultiplayerGameGetsNoModSurfaceAtAll()
    {
        _ = EngineHost.StartupPhase();
        GameSessionWatch.MultiplayerSessionSetUp();

        Assert.Equal(RunSessionKind.MultiplayerKindUnread, GameSessionWatch.Observed);
        Assert.False(GameSessionWatch.MaySpeak);
        Assert.Empty(RunmobileMod.MenuCards);

        // And the modules are untouched: they are enabled and they still contribute.
        // It is the shell that is not asking them.
        Assert.NotEmpty(RunmobileMod.MenuCardsFrom(RunmobileMod.Modules));
    }

    /// <summary>
    /// A multiplayer session set up under a live recording stops it and marks it
    /// unpublishable.
    ///
    /// The safety net, not the rule: a multiplayer game normally never gets as far as
    /// a journal, because the reading at attach refuses first -
    /// <c>LiveRunSessionTests</c> owns that half against a real run. What is asserted
    /// here is what happens to a recording that is already live, and it is the same
    /// answer a console command gets: the recording is kept whole, stops where the
    /// session changed under it, and states through <c>source.native.integrity</c>
    /// that nobody may publish it.
    /// </summary>
    [GameFact]
    public void AMultiplayerSessionUnderALiveRecordingStopsItAndMarksItUnpublishable()
    {
        _ = EngineHost.StartupPhase();
        var capture = RecordedRun.Captured();
        var journalPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RunJournal.FileExtension}";
        RunmobileStore.Write(journalPath, capture.Journal.Render());
        RunRecorder.BeginRecording(capture, journalPath);
        Assert.NotNull(RunRecorder.Active);
        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);

        GameSessionWatch.MultiplayerSessionSetUp();

        Assert.Null(RunRecorder.Active);
        Assert.Equal(NativeSource.NonStandardIntegrity, capture.Integrity);

        // Kept whole and on the file, so the session after a crash reads the same run.
        var written = RunJournal.Parse(RunmobileStore.Read(journalPath)!);
        Assert.True(written.NonStandard);
        Assert.Equal(capture.NextSeq + 1, written.Entries.Count);
        Assert.Equal(RunCaptureState.Recording, capture.State);
    }

    /// <summary>
    /// The suppression ends with the run it was about.
    ///
    /// A client that played a multiplayer game and then started a singleplayer one is
    /// in a singleplayer game, and a suppression nothing lifts would be a mod that went
    /// quiet for the rest of the session - a different bug from the one it prevents.
    /// </summary>
    [GameFact]
    public void TheSuppressionEndsWithTheMultiplayerRun()
    {
        _ = EngineHost.StartupPhase();
        GameSessionWatch.MultiplayerSessionSetUp();
        Assert.False(GameSessionWatch.MaySpeak);

        GameSessionWatch.SessionTornDown();

        Assert.Equal(RunSessionKind.NoRunInProgress, GameSessionWatch.Observed);
        Assert.True(GameSessionWatch.MaySpeak);
    }

    /// <summary>
    /// A console command used before the recorder attaches is held, not dropped.
    ///
    /// The stretch this covers is the one where there is nothing to read: continuing a
    /// saved run is asynchronous, so between the game saying a run is starting and the
    /// run existing <c>LiveRun.State</c> is null for the whole of a save load. A
    /// command typed then is in that run's history exactly like one typed a second
    /// later, and a recording that dropped it would state <c>integrity = "complete"</c>
    /// for a run the console was used in - a wrong label with no error anywhere.
    ///
    /// And the hold does not outlive the stretch: the next run starting clears it, so a
    /// command typed at the main menu is never carried into the run that follows.
    /// </summary>
    [GameFact]
    public void AConsoleCommandUsedBeforeTheRecorderAttachesIsHeldForIt()
    {
        _ = EngineHost.StartupPhase();
        Assert.Null(RunRecorder.Active);

        // The game says a run is starting. There is no run yet, and no reading to take.
        RunRecorder.NoticeRun();
        Assert.Equal(RunSessionKind.NoRunInProgress, LiveRun.ReadSession());
        Assert.False(HeldForAttach());

        RunRecorder.ConsoleCommandUsed();

        Assert.True(HeldForAttach());

        // And the run that follows this one starts from nothing held.
        RunRecorder.NoticeRun();
        Assert.False(HeldForAttach());
    }

    /// <summary>Whether a console command is being held for the recording this run is
    /// about to have. Private because nothing outside the recorder may act on it; read
    /// here because it is the whole of what the hold does before an attach.</summary>
    private static bool HeldForAttach() =>
        (bool)typeof(RunRecorder)
            .GetField("_consoleUsedBeforeAttach", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    /// <summary>
    /// A console command used after the run ended changes neither artifact.
    ///
    /// The window is real: <c>RunManager.OnEnded</c> finishes the recording and writes
    /// its manifest, and nothing clears the recorder until the run is torn down, so it
    /// is still live for as long as the player sits on the score screen with the
    /// console this mod turns on. The command is not in the history the recording
    /// holds - the run's last decision is behind it - so the recorder ignores it. What
    /// is asserted is that the journal and the manifest still say the same thing, which
    /// is the failure this is about: two artifacts disagreeing about `integrity` with
    /// nothing raised anywhere.
    /// </summary>
    [GameFact]
    public void AConsoleCommandAfterTheRunEndedLeavesBothArtifactsSayingComplete()
    {
        _ = EngineHost.StartupPhase();
        var capture = RecordedRun.Captured();
        var journalPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RunJournal.FileExtension}";
        var manifestPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RecordingLibrary.ManifestExtension}";
        RunmobileStore.Write(journalPath, capture.Journal.Render());
        RunRecorder.BeginRecording(capture, journalPath);

        RunRecorder.RunEnded(isVictory: true);
        Assert.NotNull(RunRecorder.Active);
        Assert.Equal(NativeSource.CompleteIntegrity, WrittenIntegrity(manifestPath));

        RunRecorder.ConsoleCommandUsed();

        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);
        Assert.False(RunJournal.Parse(RunmobileStore.Read(journalPath)!).NonStandard);
        Assert.Equal(NativeSource.CompleteIntegrity, WrittenIntegrity(manifestPath));
    }

    /// <summary>What the manifest on disk says about the run it recorded. Read back
    /// through the format's own reader, because the file is the artifact anybody else
    /// acts on.</summary>
    private static string? WrittenIntegrity(string manifestPath) =>
        ManifestJson.Deserialize(RunmobileStore.Read(manifestPath)!).Source.Native!.Integrity;

    /// <summary>
    /// Every member the multiplayer watch attaches to is on this build.
    ///
    /// The shell installs these however the modules answer, and unlike
    /// <see cref="RecorderModule"/> it has no refusal to fall back on: a patch that
    /// resolved to nothing would leave a multiplayer game undetected and this mod
    /// drawing in it. So the members are asserted rather than assumed, and the watch is
    /// asserted to be the shell's rather than a module's.
    /// </summary>
    [GameFact]
    public void EveryMemberTheMultiplayerWatchAttachesToExistsOnThisBuild()
    {
        _ = EngineHost.StartupPhase();

        var targets = GameSessionWatch.PatchClasses
            .SelectMany(patchClass =>
                patchClass.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).OfType<HarmonyPatch>())
            .Select(attribute => attribute.info)
            .ToList();

        Assert.Equal(3, targets.Count);
        Assert.All(targets, target => Assert.NotNull(
            AccessTools.Method(target.declaringType!, target.methodName!, target.argumentTypes)));

        Assert.All(
            GameSessionWatch.PatchClasses,
            type => Assert.Contains(type, RunmobileMod.ShellPatchClasses));
    }

    /// <summary>
    /// The console funnel this build has, and the two entries that reach it.
    ///
    /// <c>ProcessCommand(string)</c> is what a command typed on this client goes
    /// through and <c>ProcessNetCommand</c> is what a peer's goes through; both reach
    /// the three-argument overload, which is why one patch covers both. Asserted
    /// because a build that split them would leave a console command unseen while the
    /// patch still attached to something.
    /// </summary>
    [GameFact]
    public void TheConsolePatchWatchesTheFunnelEveryCommandReaches()
    {
        _ = EngineHost.StartupPhase();
        var console = GameType("MegaCrit.Sts2.Core.DevConsole.DevConsole");

        Assert.NotNull(AccessTools.Method(
            console, RunRecorder.ProcessConsoleCommandMember, RunRecorder.ProcessConsoleCommandArguments));
        Assert.NotNull(AccessTools.Method(
            console, RunRecorder.ProcessConsoleCommandMember, [typeof(string)]));
        Assert.NotNull(AccessTools.Method(console, "ProcessNetCommand"));

        Assert.Contains(
            RunRecorder.PatchClasses
                .SelectMany(patchClass =>
                    patchClass.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).OfType<HarmonyPatch>())
                .Select(attribute => attribute.info),
            info => info.declaringType == console &&
                    info.methodName == RunRecorder.ProcessConsoleCommandMember &&
                    info.argumentTypes is { } arguments &&
                    arguments.SequenceEqual(RunRecorder.ProcessConsoleCommandArguments));
    }

    /// <summary>
    /// Why the queue is not the seam, kept as a fact about this build rather than as
    /// prose.
    ///
    /// A console command reaches the action queue only in a networked game, and as two
    /// types rather than one: the game's own generated <c>INetActionSubtypes</c> list
    /// holds the eleven <c>Net*</c> structs, of which the console's is
    /// <c>NetConsoleCmdGameAction</c>, and the action it builds and puts on the queue
    /// is <c>ConsoleCmdGameAction</c>. In singleplayer <c>DevConsole.ProcessCommand</c>
    /// takes its local branch and builds neither. Singleplayer is the only kind of run
    /// this recorder records, so a watch on the queue would see a console command in
    /// exactly the runs that are never recorded and in none of the runs that are.
    ///
    /// Asserted so that a build which changes the arrangement is noticed rather than
    /// silently missed: the generated list is where a console command would appear on
    /// the queue, and it is still eleven types with the console among them.
    /// </summary>
    [GameFact]
    public void TheConsoleReachesTheQueueOnlyAsANetworkedActionOnThisBuild()
    {
        _ = EngineHost.StartupPhase();
        var subtypes = GameType("MegaCrit.Sts2.Core.GameActions.Multiplayer.INetActionSubtypes");

        var all = (System.Collections.IEnumerable)subtypes
            .GetProperty("All", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.Equal(11, all.Cast<Type>().Count());
        Assert.Contains(
            GameType("MegaCrit.Sts2.Core.DevConsole.NetConsoleCmdGameAction"), all.Cast<Type>());
    }

    private static Type GameType(string typeName) => AppDomain.CurrentDomain.GetAssemblies()
        .Single(assembly => assembly.GetName().Name == "sts2")
        .GetType(typeName)!;
}
