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
/// The latch is process-wide, so these must not run beside a test that asks the shell
/// for its cards. What guarantees that is AssemblyInfo.cs's
/// <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c>, which
/// serializes every test in this assembly.
/// </summary>
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
    /// A module's own patched surfaces go quiet too, not only the cards the shell draws.
    ///
    /// The run library reaches a player through its own Harmony patches - the button in
    /// the Compendium and the plate under run history - so neither of them passes the
    /// point where the menu cards are gated. Both ask the shell instead, and silence is
    /// silence: the button is not there and a history-row press opens nothing at all,
    /// not even a popup saying why, because a refusal would itself be this mod speaking
    /// in somebody else's game.
    ///
    /// Driven through the latch the game's own multiplayer setup sets, so the test fails
    /// if either surface stops consulting the shell.
    /// </summary>
    [GameFact]
    public void AMultiplayerGameGetsNoneOfAModulesPatchedSurfacesEither()
    {
        _ = EngineHost.StartupPhase();
        var recording = RunLibraryStoreTests.BareRecording("native-a");
        RunmobileStore.Write(
            $"{RunLibraryStore.RecordingsDirectory}/native-a-20260906-120000.replay.json",
            ManifestJson.Serialize(recording));

        Assert.True(CompendiumCard.ShowsButton());
        Assert.NotNull(RunHistoryPlateHost.PlateFor(null, recording));

        GameSessionWatch.MultiplayerSessionSetUp();

        Assert.False(CompendiumCard.ShowsButton());
        Assert.Null(RunHistoryPlateHost.PlateFor(null, recording));

        GameSessionWatch.SessionTornDown();

        Assert.True(CompendiumCard.ShowsButton());
        Assert.NotNull(RunHistoryPlateHost.PlateFor(null, recording));
    }

    /// <summary>
    /// A multiplayer setup the game refused leaves the mod watching this client again.
    ///
    /// The latch is set from a prefix, so it fires on a request rather than on a
    /// session, and the teardown that normally clears it only happens where a run
    /// existed. A setup attempted with no run and refused would otherwise leave this
    /// process drawing no card and recording no run for the rest of its life, saying so
    /// in one log line - silent, safe, and still a mod that stopped working.
    /// </summary>
    [GameFact]
    public void AMultiplayerSetupTheGameRefusedLetsTheModSpeakAgain()
    {
        _ = EngineHost.StartupPhase();
        GameSessionWatch.MultiplayerSessionSetUp();
        Assert.False(GameSessionWatch.MaySpeak);

        GameSessionWatch.MultiplayerSetupRefused();

        Assert.Equal(RunSessionKind.NoRunInProgress, GameSessionWatch.Observed);
        Assert.True(GameSessionWatch.MaySpeak);
        Assert.NotEmpty(RunmobileMod.MenuCards);
    }

    /// <summary>A recording live enough for the patches to reach, written the way a
    /// recorder writes one and begun the way the attach begins one.</summary>
    private static RunCapture ARecordingThatBegins(out string journalPath)
    {
        var capture = RecordedRun.Captured();
        journalPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RunJournal.FileExtension}";
        RunmobileStore.Write(journalPath, capture.Journal.Render());
        RunRecorder.BeginRecording(capture, journalPath);

        Assert.NotNull(RunRecorder.Active);
        return capture;
    }

    /// <summary>The same, for a test whose subject is what happens to a recording that
    /// was standing at complete when it began.</summary>
    private static RunCapture ALiveRecording(out string journalPath)
    {
        var capture = ARecordingThatBegins(out journalPath);

        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);
        return capture;
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
    /// A console command used before the recorder attaches reaches the recording it
    /// attaches to.
    ///
    /// The stretch this covers is the one where there is nothing to read: continuing a
    /// saved run is asynchronous, so between the game saying a run is starting and the
    /// run existing there is no run to read for the whole of a save load. A command
    /// typed then is in that run's history exactly like one typed a second later, and a
    /// recording that dropped it would state <c>integrity = "complete"</c> for a run the
    /// console was used in - a wrong label with no error anywhere. So what is asserted
    /// is the recording, not the holding.
    /// </summary>
    [GameFact]
    public void AConsoleCommandUsedBeforeTheRecorderAttachesReachesTheRecording()
    {
        _ = EngineHost.StartupPhase();
        Assert.Null(RunRecorder.Active);

        // The game says a run is starting. There is no run yet, and no reading to take.
        RunRecorder.NoticeRun();
        Assert.Equal(RunSessionKind.NoRunInProgress, LiveRun.ReadSession());

        RunRecorder.ConsoleCommandUsed();
        var capture = ARecordingThatBegins(out var journalPath);

        Assert.Equal(NativeSource.NonStandardIntegrity, capture.Integrity);
        Assert.True(RunJournal.Parse(RunmobileStore.Read(journalPath)!).NonStandard);
    }

    /// <summary>
    /// A console command typed at the main menu is not carried into the run that
    /// follows it.
    ///
    /// The other end of the same hold, and the reason it is cleared when a run starts
    /// rather than only when one ends: a recording marked for something that happened
    /// before its run began states an untruth about that run as surely as a dropped
    /// mark states one about this one.
    /// </summary>
    [GameFact]
    public void AConsoleCommandTypedAtTheMenuIsNotCarriedIntoTheNextRun()
    {
        _ = EngineHost.StartupPhase();
        Assert.Null(RunRecorder.Active);
        RunRecorder.ConsoleCommandUsed();

        RunRecorder.NoticeRun();
        var capture = ARecordingThatBegins(out var journalPath);

        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);
        Assert.False(RunJournal.Parse(RunmobileStore.Read(journalPath)!).NonStandard);
    }

    /// <summary>
    /// The ordinary case: a console command used while a run is being recorded marks
    /// that recording.
    ///
    /// The two either side of it are the edges - before the recorder attached and after
    /// the run ended - and this is the one the ruling is about. The recording keeps
    /// going: it is what the player played, and what it is not is publishable.
    /// </summary>
    [GameFact]
    public void AConsoleCommandUsedDuringTheRunMarksTheRecordingAndKeepsIt()
    {
        _ = EngineHost.StartupPhase();
        var capture = ALiveRecording(out var journalPath);

        RunRecorder.ConsoleCommandUsed();

        Assert.Equal(NativeSource.NonStandardIntegrity, capture.Integrity);
        Assert.True(RunJournal.Parse(RunmobileStore.Read(journalPath)!).NonStandard);
        Assert.NotNull(RunRecorder.Active);
        Assert.Equal(RunCaptureState.Recording, capture.State);
    }

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

        Assert.Equal(4, targets.Count);
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
