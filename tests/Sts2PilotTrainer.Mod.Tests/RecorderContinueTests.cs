using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// An honest Continue keeps the recording, caught headlessly.
///
/// The captain's report: take the opening blessing, leave to the main menu, press
/// Continue, and the version overlay reads RECORDING STOPPED in Neow's room. The
/// journal's last digest and the run the game came back in differed in exactly one
/// field, <c>run.act_floor</c>, read as 1 after the blessing and 0 at the resume.
///
/// The game does not save the act floor: <c>RunManager.EnterMapPointInternal</c> is
/// the one thing that sets it, and a continued run is at act floor 0 from
/// <c>SetUpSavedSingleplayer</c> until <c>LoadIntoLatestMapCoord</c> re-enters the
/// coordinate the save names. The recorder attached as soon as the run had a floor
/// count - a continued run has one on the save before it has a room - so it read the
/// run one step before the game had finished continuing it, and
/// <see cref="RunCapture.Resume"/> was handed a state the journal had never seen. The
/// resume digest was right to refuse it; the reading was taken at the wrong moment.
///
/// A Continue into a live fight has the same gap one step later. The game's save at a
/// floor arrival carries no room, so continuing it re-enters the coordinate and
/// generates the fight afresh: the room is pushed, its assets load over the frames
/// after, and only then is the combat set up. A reading between the push and the
/// combat is not in combat, so it is neither the journal's room-entry state nor any
/// other, and the captain's own mid-fight Continues resumed broken with "this game is
/// not in one the recorder can watch" - a fight opening under the recorder's nose.
///
/// The retail continue sequence is driven here the way <see cref="GameSession.RestoreSavedRun"/>
/// drives it, from the saves the game itself took, with the recorder's readiness asked
/// at the moment the retail attach used to read and again once the run is where the
/// game's own Continue leaves it. Headlessly nothing yields inside those gaps, so each
/// is observed from a prefix on the game member that ends it.
///
/// Two more Continues break for a different reason, one the timing above cannot
/// reach: the state itself. A finished fight stays on the player until the next fight
/// and the projection keeps emitting it, so every reading taken at a shop, a rest
/// site, an event or a loot screen after a won fight carries that fight's residue,
/// and the game's save carries none of it - <c>SerializableRun</c> has no combat
/// member. Continued from that save, the run reads as a moment the journal never saw
/// in exactly the residue fields and nothing else. The shop-arrival test holds the
/// resume to comparing what a save can carry once the complete digests have
/// disagreed, from the game's own saves, with the Ironclad on the whole-act seed
/// because its first two fights are won on attacks alone; the one card screen on the
/// way, the opening blessing's, is answered by the driver's own improvisation.
///
/// The rest are about where the game saved. It saves at every map-point arrival,
/// every fight won and every ancient event finished, and Continue restores the latest
/// of them, so a quit after a purchase, a rest, an event page or a claimed reward
/// comes back with that decision undone and re-offered. That is the game working as
/// designed and not a hole in the watch: the recorder notes each save as it lands,
/// and a resume at the latest of them is continuous with the undone decisions kept as
/// the branch the restore discarded, which the engine then replays from the save's own
/// state. A resume at an older save - a backup, a cloud copy - is a reload and rewound;
/// a resume nowhere in the journal is broken. Each is driven from the game's own
/// saves, and every branch accepted as continuous is replayed here through
/// <see cref="Engine.Arbiter"/> before the test passes.
/// </summary>
public sealed class RecorderContinueTests : IDisposable
{
    private const string Seed = "P1L0TTRA1NER";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    /// <summary>Where the recordings these tests write are kept: a directory named by
    /// this variable, left in place for a demo to read, or a temporary one deleted
    /// with the test.</summary>
    private const string KeepRecordingsVariable = "RECORDER_CONTINUE_RECORDINGS";

    private readonly string _root = Path.Combine(
        Environment.GetEnvironmentVariable(KeepRecordingsVariable) is { Length: > 0 } kept
            ? Path.Combine(kept, $"recorder-continue-{Guid.NewGuid():N}")
            : Path.Combine(Path.GetTempPath(), $"recorder-continue-{Guid.NewGuid():N}"),
        "Runmobile", "steam", "test", "profile1");

    public RecorderContinueTests()
    {
        EngineHost.Start();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        RunmobileStore.UseRootForTesting(null);
        RunRecorder.ResetHostSeamsForTesting();
        HeadlessEngine.Forget();
        if (Environment.GetEnvironmentVariable(KeepRecordingsVariable) is { Length: > 0 }) return;
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [GameFact]
    public void AnHonestContinueAtNeowsRoomKeepsTheRecordingContinuous()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        // The run, recorded through its blessing, and the save the game itself took
        // when Neow's event finished - the save Continue reads.
        InterceptedRunSave? save = null;
        string lastDigest;
        string runId;
        using (RunSaveInterception.Collect(taken => save = taken))
        {
            var session = StartRun();
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
            DrainSettles();

            var capture = RunRecorder.Active!.Capture;
            Assert.Equal(1, capture.NextSeq);
            Assert.Equal("1", capture.Trace.Steps[^1].After["run.act_floor"]);
            runId = capture.RunId;
            lastDigest = capture.LastDigest;

            // Leaving to the main menu: the game tears the run down and the recorder
            // detaches, keeping the journal.
            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        Assert.NotNull(save);
        Assert.Equal("Event", save.PreFinishedRoom);
        Assert.True(File.Exists(Path.Combine(_root, "recordings", $"{runId}{RunJournal.FileExtension}")));

        // Continue. Between the run existing and the game re-entering its room the
        // act floor reads 0, which is the state the recorder used to read.
        using (BeforeTheRoomIsEntered(() =>
               {
                   Assert.Equal("0", LiveRun.Sample()["run.act_floor"]);
                   Assert.NotEqual(lastDigest, LiveRun.Read().Digest);
                   Assert.False(RunRecorder.HasEnteredItsRoom(), "the recorder was ready to read a run the game had not finished continuing");
               }))
        {
            var continued = new GameSession();
            continued.RestoreSavedRun(save.Json);
            using var driver = new RunDriver(continued);

            Assert.True(RunRecorder.HasEnteredItsRoom());
            Assert.Equal(lastDigest, LiveRun.Read().Digest);
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            var resumed = RunRecorder.Active!.Capture;
            Assert.Equal(runId, resumed.RunId);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Null(resumed.Stop);
            Assert.Equal(1, resumed.NextSeq);

            // And the run goes on being recorded from where the journal left off.
            var edge = FirstEdge(continued);
            driver.Apply(Record(1, ActionVerb.MapMove,
                ("act", N(continued.RunState.CurrentActIndex)),
                ("row", N(edge.ToRow)), ("column", N(edge.ToColumn))));
            DrainSettles();

            Assert.Equal(2, resumed.NextSeq);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal("true", Field(continued, "combat.in_progress"));
        }
    }

    [GameFact]
    public void AnHonestContinueIntoALiveFightResumesAtTheFightsEntryAndKeepsWatching()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        // The run, recorded into its first fight and one card past its entry, and the
        // save the game itself took at the floor arrival - before the room was rolled,
        // which is why continuing it opens the fight afresh.
        var saves = new List<InterceptedRunSave>();
        string entryDigest;
        string runId;
        using (RunSaveInterception.Collect(saves.Add))
        {
            var session = StartRun();
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
            DrainSettles();

            var edge = FirstEdge(session);
            driver.Apply(Record(1, ActionVerb.MapMove,
                ("act", N(session.RunState.CurrentActIndex)),
                ("row", N(edge.ToRow)), ("column", N(edge.ToColumn))));
            DrainSettles();
            Assert.Equal("true", Field(session, "combat.in_progress"));

            var capture = RunRecorder.Active!.Capture;
            entryDigest = capture.LastDigest;
            runId = capture.RunId;

            PlayFirstPlayable(session);
            Assert.Equal(3, capture.NextSeq);
            Assert.Equal(ActionVerb.PlayCard, capture.Actions[^1].Verb);

            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        var arrival = Assert.Single(saves, taken => taken.IsFloorEntry && taken.ActFloor == 2);

        // Continue. The room is pushed before its fight is set up, and at that moment
        // the run is in no combat at all.
        using (BeforeTheFightIsSetUp(() =>
               {
                   Assert.IsType<CombatRoom>(LiveRun.State!.CurrentRoom);
                   Assert.False(LiveRun.InCombat);
                   Assert.NotEqual(entryDigest, LiveRun.Read().Digest);
                   Assert.False(RunRecorder.HasEnteredItsRoom(), "the recorder was ready to read a fight the game had not opened");
               }))
        {
            var continued = new GameSession();
            continued.RestoreSavedRun(arrival.Json);
            Pump.Drain();
            using var driver = new RunDriver(continued);

            Assert.True(RunRecorder.HasEnteredItsRoom());
            Assert.Equal(entryDigest, LiveRun.Read().Digest);
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            // The game's own rollback of the live fight to its entry: the card played
            // before the quit is a discarded branch, the watch is continuous, and it
            // resumes at the fight's next decision.
            var resumed = RunRecorder.Active!.Capture;
            Assert.Equal(runId, resumed.RunId);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal(2, resumed.NextSeq);
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(1, discarded.RollbackToSeq);
            Assert.Contains(discarded.Actions, action => action.Verb == ActionVerb.PlayCard);

            PlayFirstPlayable(continued);
            Assert.Equal(3, resumed.NextSeq);
            Assert.Equal(ActionVerb.PlayCard, resumed.Actions[^1].Verb);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
        }
    }

    /// <summary>
    /// Arrive at a shop straight after a won fight, quit, continue: the recorder's
    /// reading at the arrival carries nothing of the finished fight, and neither does
    /// the restored run, so the two are the same state digest for digest and the
    /// resume needs no comparison of what a save can carry to place it.
    /// </summary>
    [GameFact]
    public void AnHonestContinueAtAShopArrivalAfterAWonFightResumesContinuously()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        var saves = new List<InterceptedRunSave>();
        string lastDigest;
        IReadOnlyDictionary<string, string> lastReading;
        string runId;
        int seq;
        using (RunSaveInterception.Collect(saves.Add))
        {
            var session = StartIronclad();
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            driver.EnterFirstRoom();
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
            var actions = new List<ActionRecord>();

            Apply(driver, actions, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
            DrainSettles();
            foreach (var node in RouteTo(session, MapPointType.Shop))
            {
                Move(driver, actions, session, node);
                if (node.PointType == MapPointType.Shop) break;
                Assert.Equal(MapPointType.Monster, node.PointType);
                PlayToVictory(driver, actions, session);
                TakeGoldAndSkipTheRest(driver, actions);
            }

            Assert.Equal(RoomType.Shop, session.RunState.CurrentRoom!.RoomType);

            var capture = RunRecorder.Active!.Capture;
            Assert.Empty(capture.Refusals);
            lastDigest = capture.LastDigest;
            lastReading = capture.Trace.Steps[^1].After;
            Assert.Equal("none", lastReading["combat.outcome"]);
            runId = capture.RunId;
            seq = capture.NextSeq;

            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        var arrival = saves.Last(taken => taken.IsFloorEntry);

        var continued = new GameSession();
        using var continuedDriver = new RunDriver(continued);
        continuedDriver.ImproviseUnrecordedCardSelections();
        continued.RestoreSavedRun(arrival.Json);
        Pump.Drain();

        // The restored run is the recorded state, digest for digest: the game's
        // save carries no combat and the projection now carries none of a finished
        // one, so there is nothing left to differ in.
        Assert.True(RunRecorder.HasEnteredItsRoom());
        var (live, liveDigest) = LiveRun.Read();
        Assert.Empty(ReplayTrace.Differences(lastReading, live));
        Assert.Equal(lastDigest, liveDigest);

        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var resumed = RunRecorder.Active!.Capture;
        Assert.Equal(runId, resumed.RunId);
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Empty(resumed.Refusals);
        Assert.Empty(resumed.Discarded);
        Assert.Equal(seq, resumed.NextSeq);
    }

    /// <summary>
    /// Claim the gold on the loot screen, quit, continue: the game restores its
    /// fight-won save, so the loot screen comes back with the gold on offer again and
    /// the run carries no combat state. The match is the killing play, which is
    /// where the game saved, so the resume is the game's own return to its latest
    /// save: continuous, with the claim kept as the branch the restore discarded and
    /// replayed from the save's own state.
    /// </summary>
    [GameFact]
    public void AnHonestContinueOnTheLootScreenIsContinuousAtTheFightWonSave()
    {
        var lab = new Lab(this);
        int killingPlay;
        ReplayManifest manifest;
        using (lab.Recording())
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            killingPlay = lab.Capture.NextSeq - 1;
            Assert.Equal(killingPlay, lab.Capture.LatestSavePointSeq);
            lab.Apply(ActionVerb.ClaimReward, ("reward_type", "gold"));
            Assert.Equal(ActionVerb.ClaimReward, lab.Capture.Actions[^1].Verb);

            var won = lab.Saves.Last();
            Assert.Equal("Monster", won.PreFinishedRoom);
            lab.QuitAndContinue(won);

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal(RunCaptureState.Recording, resumed.State);
            Assert.Equal(killingPlay + 1, resumed.NextSeq);
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(killingPlay, discarded.RollbackToSeq);
            Assert.False(discarded.Reload);
            Assert.Equal(ActionVerb.ClaimReward, Assert.Single(discarded.Actions).Verb);

            // The gold is on offer again, and claiming it is the next decision.
            lab.Apply(ActionVerb.ClaimReward, ("reward_type", "gold"));
            Assert.Equal(killingPlay + 2, resumed.NextSeq);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);

            // Quit on the loot screen a second time: the branch this leaves was
            // recorded on the restored run, whose loot screen carries a fresh combat
            // state in place of the fight's own, and the branch has to replay anyway.
            lab.QuitAndContinue(won);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Equal(2, lab.Capture.Discarded.Count);
            Assert.All(lab.Capture.Discarded, branch => Assert.Equal(killingPlay, branch.RollbackToSeq));

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        lab.ReplayEveryBranch(manifest);
    }

    /// <summary>Claim the gold, skip the rest, and quit on the map before moving:
    /// the game restores the fight-won save and puts the loot screen back, and both
    /// decisions are the branch.</summary>
    [GameFact]
    public void AnHonestContinueOnTheMapAfterTheRewardsIsContinuousAtTheFightWonSave()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        using (lab.Recording())
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            var killingPlay = lab.Capture.NextSeq - 1;
            lab.TakeGoldAndSkipTheRest();
            Assert.Equal(ActionVerb.SkipRewards, lab.Capture.Actions[^1].Verb);

            lab.QuitAndContinue(lab.Saves.Last());

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(killingPlay, discarded.RollbackToSeq);
            Assert.False(discarded.Reload);
            Assert.Equal(ActionVerb.ClaimReward, discarded.Actions[0].Verb);
            Assert.Equal(ActionVerb.SkipRewards, discarded.Actions[^1].Verb);

            lab.TakeGoldAndSkipTheRest();

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        Assert.NotEmpty(manifest.Source.Native.SavePoints!);
        lab.ReplayEveryBranch(manifest);
    }

    /// <summary>Buy a card, quit, continue: the game restores the shop arrival's
    /// save, the shelf is stocked again and the gold is back, and the recording is
    /// continuous with the purchase as the branch.</summary>
    [GameFact]
    public void AnHonestContinueAfterAShopPurchaseIsContinuousAtTheArrivalSave()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        using (lab.Recording())
        {
            lab.Neow();
            lab.WalkTo(MapPointType.Shop);
            var arrival = lab.Capture.NextSeq - 1;
            Assert.Equal(arrival, lab.Capture.LatestSavePointSeq);
            var goldBefore = lab.Field("player.gold");
            lab.BuyOneThing();
            Assert.Equal(ActionVerb.ShopPurchase, lab.Capture.Actions[^1].Verb);
            Assert.NotEqual(goldBefore, lab.Field("player.gold"));

            lab.QuitAndContinue(lab.Saves.Last(save => save.IsFloorEntry));

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal(goldBefore, lab.Field("player.gold"));
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(arrival, discarded.RollbackToSeq);
            Assert.False(discarded.Reload);
            Assert.Equal(ActionVerb.ShopPurchase, Assert.Single(discarded.Actions).Verb);

            lab.BuyOneThing();
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        Assert.NotEmpty(manifest.Source.Native.SavePoints!);
        lab.ReplayEveryBranch(manifest);
    }

    /// <summary>Rest, quit, continue: the heal is undone by the restore of the rest
    /// site's arrival save, and the recording is continuous with the rest as the
    /// branch.</summary>
    [GameFact]
    public void AnHonestContinueAfterARestIsContinuousAtTheArrivalSave()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        using (lab.Recording())
        {
            lab.Neow();
            lab.WalkTo(MapPointType.RestSite);
            var arrival = lab.Capture.NextSeq - 1;
            var hpBefore = lab.Field("player.hp");
            lab.Rest();
            Assert.Equal(ActionVerb.ChooseRestSiteOption, lab.Capture.Actions[^1].Verb);

            lab.QuitAndContinue(lab.Saves.Last(save => save.IsFloorEntry));

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal(hpBefore, lab.Field("player.hp"));
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(arrival, discarded.RollbackToSeq);
            Assert.False(discarded.Reload);
            Assert.Equal(ActionVerb.ChooseRestSiteOption, Assert.Single(discarded.Actions).Verb);

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        Assert.NotEmpty(manifest.Source.Native.SavePoints!);
        lab.ReplayEveryBranch(manifest);
    }

    /// <summary>Turn an ordinary event's page, quit, continue: an ordinary event
    /// takes no save of its own, so the restore is of the arrival and the page is
    /// re-offered; continuous, with the choice as the branch.</summary>
    [GameFact]
    public void AnHonestContinueAfterAnEventChoiceIsContinuousAtTheArrivalSave()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        using (lab.Recording())
        {
            lab.Neow();
            lab.WalkToAnEvent();
            var arrival = lab.Capture.NextSeq - 1;
            Assert.Equal(arrival, lab.Capture.LatestSavePointSeq);
            var optionsBefore = lab.EventOptionKeys();
            lab.ChooseEventOption();
            Assert.Equal(ActionVerb.ChooseEventOption, lab.Capture.Actions[^1].Verb);
            Assert.Equal(arrival, lab.Capture.LatestSavePointSeq);

            lab.QuitAndContinue(lab.Saves.Last(save => save.IsFloorEntry));

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal(optionsBefore, lab.EventOptionKeys());
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(arrival, discarded.RollbackToSeq);
            Assert.False(discarded.Reload);
            Assert.Equal(ActionVerb.ChooseEventOption, Assert.Single(discarded.Actions).Verb);

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        Assert.NotEmpty(manifest.Source.Native.SavePoints!);
        lab.ReplayEveryBranch(manifest);
    }

    /// <summary>
    /// A run continued outside a fight after a won one is publishable evidence: the
    /// whole continued history reproduces through the engine at every checkpoint and
    /// every declared boundary, and its discarded branch replays from the save it left.
    ///
    /// This is the captain's retail run headlessly: a fight won, the gold claimed,
    /// Save and Quit on the map, Continue, the loot taken again, and the next floor
    /// walked to without a fight on it. The game's fight-won save carries no combat,
    /// so the restored client stands on the loot screen with a fresh
    /// <c>PlayerCombatState</c> at turn 1, while the engine replaying the same history
    /// carries the fight as it was fought. While the projection emitted that finished
    /// fight into every reading until the next one, the arrival after the Continue
    /// disagreed in exactly the residue fields - <c>combat.turn observed '1', engine
    /// produced '2'</c> at the floor-3 entry, in the retail run of 2026-09-15 - and
    /// nothing else. Outside a live fight the two hosts now project the same state, so
    /// the arrival's checkpoint and boundary digest hold.
    /// </summary>
    [GameFact]
    public void ARunContinuedOutsideAFightAfterAWonOneReproducesWholeAtEveryBoundary()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        int killingPlay;
        using (lab.Recording())
        {
            lab.Neow();
            killingPlay = lab.WalkToTheFightBefore(MapPointType.Shop);
            lab.TakeGoldAndSkipTheRest();

            lab.QuitAndContinue(lab.Saves.Last());

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Equal(killingPlay, Assert.Single(resumed.Discarded).RollbackToSeq);

            // The restored loot screen is the finished fight's, and no fight is live.
            Assert.Equal("false", lab.Field("combat.in_progress"));

            lab.TakeGoldAndSkipTheRest();
            lab.MoveTo(MapPointType.Shop);
            var arrival = lab.Capture.NextSeq - 1;
            Assert.Equal("false", lab.Field("combat.in_progress"));
            Assert.Equal("none", lab.Field("combat.outcome"));
            Assert.Equal(arrival, lab.Capture.LatestSavePointSeq);

            manifest = lab.Abandon();
        }

        var native = manifest.Source.Native!;
        Assert.Equal(NativeSource.ContinuousContinuity, native.Continuity);
        Assert.Equal(NativeSource.CompleteIntegrity, native.Integrity);
        Assert.False(native.IsRewound);
        Assert.Contains(manifest.Boundaries, boundary =>
            boundary.Kind == ReplayBoundary.FloorEntryKind && boundary.AfterSeq > killingPlay);

        var replay = lab.ReplayTheWholeHistory(manifest);
        Assert.True(CombatProjection.CoverageOf(replay.Trace!).IsCompletedFight);
        lab.ReplayEveryBranch(manifest);
    }

    /// <summary>Continue from the arrival save after the fight was won: an older save
    /// than the latest, so the fight's decisions are the reload's branch and the
    /// recording is rewound - whole, playable, never shareable.</summary>
    [GameFact]
    public void AContinueFromAnOlderSaveThanTheLatestIsARewind()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        using (lab.Recording())
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            var arrival = lab.Capture.NextSeq - 1;
            lab.PlayToVictory();
            var killingPlay = lab.Capture.NextSeq - 1;
            Assert.Equal(killingPlay, lab.Capture.LatestSavePointSeq);

            var older = lab.Saves.Last(save => save.IsFloorEntry);
            lab.QuitAndContinue(older);

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
            Assert.Equal(RunCaptureState.Recording, resumed.State);
            Assert.Contains($"latest save (after decision {killingPlay})", resumed.Refusal!, StringComparison.Ordinal);
            Assert.Equal(arrival + 1, resumed.NextSeq);
            var discarded = Assert.Single(resumed.Discarded);
            Assert.True(discarded.Reload);
            Assert.Equal(arrival, discarded.RollbackToSeq);
            Assert.Equal(arrival, resumed.LatestSavePointSeq);

            lab.PlayToVictory();
            Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.RewoundContinuity, manifest.Source.Native!.Continuity);
        Assert.True(Assert.Single(manifest.Source.Native.Discarded!).Reload);
    }

    /// <summary>
    /// The game's own rollback, then a reload behind it. A purchase undone by
    /// Continue at the shop arrival's save is the game's own rollback and the
    /// recording continuous; a later Continue from an older backup - the fight-won
    /// save before the shop - is a reload, and it takes the arrival's save off the
    /// continued history. The first branch carries the save it returned to itself,
    /// so the rewound recording validates and stays the player's to play from.
    ///
    /// The reload also remade the decisions the first branch was played from: the
    /// loot and the move to the shop are now the reload's branch, and the continued
    /// history holds what was played in their place. Each branch replays from the
    /// decisions it saw - the first through the shop arrival the reload discarded,
    /// the second from the fight-won save - and the continued history from its own;
    /// replayed from the remade history, the first branch's purchase ran in a room
    /// that was never a shop.
    /// </summary>
    [GameFact]
    public void AReloadBehindAnEarlierOwnRollbackLeavesARewoundRecordingTheValidatorTakes()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        int arrival;
        using (lab.Recording())
        {
            lab.Neow();
            lab.WalkTo(MapPointType.Shop);
            arrival = lab.Capture.NextSeq - 1;
            var shopArrival = lab.Saves.Last(save => save.IsFloorEntry);
            var fightWon = lab.Saves[lab.Saves.IndexOf(shopArrival) - 1];
            Assert.Equal("Monster", fightWon.PreFinishedRoom);
            lab.BuyOneThing();

            lab.QuitAndContinue(shopArrival);
            var continued = lab.Capture;
            Assert.Equal(NativeSource.ContinuousContinuity, continued.Continuity);
            var own = Assert.Single(continued.Discarded);
            Assert.Equal(arrival, own.SavePoint?.AfterSeq);
            lab.BuyOneThing();

            lab.QuitAndContinue(fightWon);
            var rewound = lab.Capture;
            Assert.Equal(NativeSource.RewoundContinuity, rewound.Continuity);
            Assert.Equal(RunCaptureState.Recording, rewound.State);
            Assert.Equal(2, rewound.Discarded.Count);
            Assert.False(rewound.Discarded[0].Reload);
            Assert.Equal(arrival, rewound.Discarded[0].SavePoint?.AfterSeq);
            Assert.True(rewound.Discarded[1].Reload);
            Assert.DoesNotContain(arrival, rewound.SavePoints.Select(point => point.AfterSeq));

            lab.TakeGoldAndSkipTheRest();
            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.RewoundContinuity, manifest.Source.Native!.Continuity);
        Assert.Equal(arrival, manifest.Source.Native.Discarded![0].SavePoint?.AfterSeq);
        Assert.DoesNotContain(arrival, manifest.Source.Native.SavePoints!.Select(point => point.AfterSeq));

        var whole = lab.ReplayTheWholeHistory(manifest);
        var branches = lab.ReplayEveryBranch(manifest);
        var shopArrivalAsTheBranchSawIt = manifest.Source.Native.Discarded[1].Actions
            .Single(action => action.Seq == arrival);
        Assert.Equal(ActionVerb.MapMove, shopArrivalAsTheBranchSawIt.Verb);
        var ownBranchArrival = branches[0].Trace!.Steps.Single(step => step.Seq == arrival);
        Assert.Equal(
            shopArrivalAsTheBranchSawIt.Args.OrderBy(arg => arg.Key, StringComparer.Ordinal),
            ownBranchArrival.Args.OrderBy(arg => arg.Key, StringComparer.Ordinal));
        Assert.Equal(ActionVerb.ShopPurchase, manifest.Source.Native.Discarded[0].Actions[0].Verb);
        // The continued history never stood there: the reload remade the loot
        // decisions and ended short of any move.
        var remade = whole.Trace!.Steps.SingleOrDefault(step => step.Seq == arrival);
        Assert.True(
            remade is null || remade.After["run.map_coord"] != ownBranchArrival.After["run.map_coord"],
            "the continued history was replayed through the shop arrival the reload discarded");
    }

    /// <summary>A session the recorder was not watching won the fight and moved on,
    /// and the game saved there; continued with the recorder, the run is at a moment
    /// the journal never saw, and the recording is broken.</summary>
    [GameFact]
    public void AContinueIntoAStateTheJournalNeverSawIsStillAHole()
    {
        var lab = new Lab(this);
        using (lab.Recording())
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();

            // The mod off for a session: the fight is finished and the loot taken
            // with nobody recording, and the game saves at the fight's end.
            lab.QuitAndContinue(lab.Saves.Last(save => save.IsFloorEntry), attachRecorder: false);
            Assert.Null(RunRecorder.Active);
            lab.PlayToVictory();
            var unwatched = lab.Saves.Last();
            Assert.Equal("Monster", unwatched.PreFinishedRoom);

            lab.QuitAndContinue(unwatched);

            var resumed = lab.Capture;
            Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
            Assert.Equal(RunCaptureState.Broken, resumed.State);
            Assert.Contains("not one this recording ever saw", resumed.Refusal!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A save asked while a decision was in flight whose member then threw: the
    /// prefix read the decision and opened its ticket, the postfix never ran, and the
    /// pump never saw it. The save waits on that ticket for the rest of the recording
    /// and is dropped at its end, so the save points are exactly the ones the game
    /// took on decisions the history holds. Placed on whichever decision happened to
    /// commit later, it would have named a decision the save does not hold, and a
    /// reload to that decision would then have read as the game's own return.
    /// </summary>
    [GameFact]
    public void ASaveAskedDuringADecisionThatNeverReachedThePumpIsNoSavePoint()
    {
        var lab = new Lab(this);
        ReplayManifest manifest;
        int[] gameSaves;
        using (lab.Recording())
        {
            lab.Neow();
            var finishedEvent = lab.Capture.LatestSavePointSeq;

            lab.OpenAMapMoveThatNeverAnnounces();
            RunRecorder.SaveAsked(Task.CompletedTask);

            // Three decisions commit with that ticket still open, and none of them
            // is where the save goes.
            lab.MoveTo(MapPointType.Monster);
            var arrival = lab.Capture.NextSeq - 1;
            lab.PlayOneCard();
            lab.PlayOneCard();
            lab.PlayToVictory();
            gameSaves = [finishedEvent, arrival, lab.Capture.NextSeq - 1];
            Assert.Equal(gameSaves, lab.Capture.SavePoints.Select(point => point.AfterSeq));

            manifest = lab.Abandon();
        }

        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        Assert.Equal(gameSaves, manifest.Source.Native.SavePoints!.Select(point => point.AfterSeq));
    }

    /// <summary>
    /// The Neow re-offer. The blessing is answered, and the game comes back with it
    /// unanswered: Continue restored the run-start save because the save the
    /// finished event asked for never reached the disk. The journal then holds no
    /// save point, so the run-start save is the latest and the recording is
    /// continuous, with the first answer kept as the branch. Where that save did
    /// land, the same Continue is a reload of the older run-start save.
    ///
    /// The re-offered state is a fresh run on the same seed stood in its first room,
    /// which is what the retail restore of the run-start save produces; headlessly the
    /// restore itself stops short of the room. The save that never landed is a journal
    /// with no line for it, which is what a crash before the game's write completed
    /// leaves.
    /// </summary>
    [GameTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ANeowReOfferIsTheGamesOwnRollbackWhereTheFinishSaveNeverLanded(bool finishSaveLanded)
    {
        var lab = new Lab(this);
        string journalPath;
        ReplayManifest manifest;
        using (lab.Recording())
        {
            // The blessing and the card-screen answer it opened share one state, and
            // the save the finished event asked for holds it.
            lab.Neow();
            Assert.Equal(lab.Capture.NextSeq - 1, lab.Capture.LatestSavePointSeq);
            journalPath = lab.JournalPath;
            lab.Quit();
        }

        if (!finishSaveLanded)
        {
            var journal = File.ReadAllText(journalPath);
            var savePoint = journal.LastIndexOf("{\"save_point\"", StringComparison.Ordinal);
            Assert.True(savePoint > 0 && journal.IndexOf('\n', savePoint) == journal.Length - 1);
            File.WriteAllText(journalPath, journal[..savePoint]);
        }

        using (lab.Recording())
        {
            lab.StartAtTheFirstRoom();
            Assert.Equal(RunJournal.Parse(File.ReadAllText(journalPath)).Opening.Digest, LiveRun.Read().Digest);
            lab.Attach();

            var resumed = lab.Capture;
            Assert.Equal(0, resumed.NextSeq);
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(-1, discarded.RollbackToSeq);
            Assert.Equal(ActionVerb.ChooseNeowBlessing, discarded.Actions[0].Verb);
            if (finishSaveLanded)
            {
                Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
                Assert.True(discarded.Reload);
                Assert.Contains("latest save (after decision", resumed.Refusal!, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
                Assert.Empty(resumed.Refusals);
                Assert.False(discarded.Reload);
            }

            // The blessing chosen again, and the run recorded on from it.
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            manifest = lab.Abandon();
        }

        if (finishSaveLanded)
        {
            Assert.Equal(NativeSource.RewoundContinuity, manifest.Source.Native!.Continuity);
        }
        else
        {
            Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
            lab.ReplayEveryBranch(manifest);
        }
    }

    /// <summary>The line the recorder logs beside a refusal names the fields, in
    /// order, and no more than eight of them.</summary>
    [Fact]
    public void TheResumeDifferenceLineIsBounded()
    {
        var last = new RunJournalEntry
        {
            Seq = 7,
            Verb = "MapMove",
            State = Enumerable.Range(0, 12).ToDictionary(i => $"field.{i:d2}", i => "old", StringComparer.Ordinal),
            Digest = "sha256:" + new string('a', 64),
        };
        var live = Enumerable.Range(0, 12).ToDictionary(i => $"field.{i:d2}", i => "new", StringComparer.Ordinal);

        var line = RunRecorder.DescribeResumeDifferences(last, live);

        Assert.StartsWith("the run resumed differs from the journal's reading after decision 7 (MapMove) in 12 field(s): ", line, StringComparison.Ordinal);
        Assert.Contains("field.00: old -> new", line, StringComparison.Ordinal);
        Assert.Contains("field.07: old -> new", line, StringComparison.Ordinal);
        Assert.DoesNotContain("field.08", line, StringComparison.Ordinal);
        Assert.EndsWith("; and 4 more", line, StringComparison.Ordinal);
    }

    // ── The save, Continue and give-up matrix ────────────────────────────────────

    /// <summary>
    /// Every way a player can leave a run and come back to it, or give it up, as one
    /// table. The rows are the scout matrix of 2026-09-14 that found the three
    /// resume defects the tests above each hold one of, and whose treasure-room row
    /// then found the headless host opening a chest inside the map move that entered
    /// it, ahead of the click the client opens it on; here they are kept whole so a
    /// regression in any of them is caught before a player meets it.
    ///
    /// Each row plays its steps through the game's own saves and the game's own
    /// restore, holds every Continue to the recorder's contract at the moment it is
    /// made - <see cref="Lab.ContinueAtTheLatestSave"/> - and then gives the run up
    /// from the pause menu and holds the recording it leaves to one of four verdicts:
    /// publishable, playable but never shareable, refused for want of a finished
    /// fight, or refused for a hole in the watch. A publishable recording is replayed
    /// through the engine at every checkpoint, every boundary and every discarded
    /// branch before the row passes. A row that fails on a Continue names the fields
    /// the restored run differed in, which is what every diagnosis so far had to be
    /// rebuilt from a journal to learn.
    /// </summary>
    public static TheoryData<string> MatrixRows => new(Matrix.Keys);

    [GameTheory]
    [MemberData(nameof(MatrixRows))]
    public void EverySaveContinueAndGiveUpScenarioLeavesTheRecordingItSaysItDoes(string row)
    {
        var scenario = Matrix[row];
        var lab = new Lab(this);
        Lab.GivenUp given;
        using (lab.Recording())
        {
            scenario.Steps(lab);
            given = lab.GiveUp();
        }

        var native = given.Manifest.Source.Native!;
        Assert.Equal("abandoned", native.Outcome);
        Assert.Equal(scenario.Continuity, native.Continuity);
        Assert.Equal(scenario.Branches, native.Discarded?.Count ?? 0);
        switch (scenario.Verdict)
        {
            case Verdict.Publishable:
                Assert.True(given.Validation.IsValid, given.Validation.Describe());
                Assert.False(native.IsRewound);
                lab.ReplayTheWholeHistory(given.Manifest);
                if (scenario.Branches > 0) lab.ReplayEveryBranch(given.Manifest);
                break;
            case Verdict.PlayableNeverShared:
                Assert.True(given.Validation.IsValid, given.Validation.Describe());
                Assert.True(native.IsRewound);
                Assert.Contains(native.Discarded!, branch => branch.Reload);
                lab.ReplayTheWholeHistory(given.Manifest);
                break;
            case Verdict.NoFinishedFight:
                Assert.False(given.Validation.IsValid);
                Assert.Contains("boundaries names no combat_start", given.Validation.Describe(), StringComparison.Ordinal);
                break;
            case Verdict.Hole:
                Assert.False(given.Validation.IsValid);
                Assert.Contains($"continuity is '{NativeSource.BrokenContinuity}'", given.Validation.Describe(), StringComparison.Ordinal);
                break;
        }
    }

    /// <summary>What a recording is once the run is given up. The first two are the
    /// validator's answer and the gate's continuity condition together; the last two
    /// are the validator's refusal, in its own words.</summary>
    private enum Verdict
    {
        /// <summary>Valid, continuous, and reproduces whole through the engine.</summary>
        Publishable,

        /// <summary>Valid and rewound: the player's to play from, refused by the gate.</summary>
        PlayableNeverShared,

        /// <summary>Refused: no fight finished, so no combat_start to be stood in.</summary>
        NoFinishedFight,

        /// <summary>Refused: the watch has a hole in it.</summary>
        Hole,
    }

    private sealed record Scenario(Action<Lab> Steps, string Continuity, int Branches, Verdict Verdict);

    private static Scenario Continuous(int branches, Verdict verdict, Action<Lab> steps) =>
        new(steps, NativeSource.ContinuousContinuity, branches, verdict);

    private static readonly IReadOnlyDictionary<string, Scenario> Matrix = new Dictionary<string, Scenario>(StringComparer.Ordinal)
    {
        ["S01 Neow answered, quit, continue"] = Continuous(0, Verdict.NoFinishedFight, lab =>
        {
            lab.Neow();
            lab.ContinueAtTheLatestSave();
        }),
        ["S01b Neow answered, continue from the run-start save"] = new(lab =>
        {
            lab.Neow();
            lab.ContinueFromTheRunStartSave();
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
        }, NativeSource.RewoundContinuity, 1, Verdict.PlayableNeverShared),
        ["S02 quit on arrival at the first fight, continue, finish"] = Continuous(0, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.ContinueAtTheLatestSave();
            lab.PlayToVictory();
        }),
        ["S03 one card on turn 1, quit, continue, finish"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayToVictory();
        }),
        ["S04 a full turn 1 and one card on turn 2, quit, continue, finish"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayTheTurnOut();
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayToVictory();
        }),
        ["S05 quit mid-fight twice"] = Continuous(2, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayOneCard();
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayToVictory();
        }),
        ["S06 fight won, quit on the loot screen untouched, continue, take the loot"] = Continuous(0, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            lab.ContinueAtTheLatestSave();
            lab.TakeGoldAndSkipTheRest();
        }),
        ["S07 fight won, gold claimed, quit before the card, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            lab.Apply(ActionVerb.ClaimReward, ("reward_type", "gold"));
            lab.ContinueAtTheLatestSave();
            lab.TakeGoldAndSkipTheRest();
        }),
        ["S08 fight won, rewards done, quit on the map, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            lab.TakeGoldAndSkipTheRest();
            lab.ContinueAtTheLatestSave();
            lab.TakeGoldAndSkipTheRest();
        }),
        ["S09 fight won, rewards done, moved on, quit on arrival, continue"] = Continuous(0, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            lab.TakeGoldAndSkipTheRest();
            lab.MoveOn();
            lab.ContinueAtTheLatestSave();
            lab.HandleRoomToEnd();
        }),
        ["S10 shop: quit on arrival, continue, buy, quit, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.WalkTo(MapPointType.Shop);
            lab.ContinueAtTheLatestSave();
            lab.BuyOneThing();
            lab.ContinueAtTheLatestSave();
        }),
        ["S11 rest site: quit on arrival, continue, rest, quit, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.WalkTo(MapPointType.RestSite);
            lab.ContinueAtTheLatestSave();
            lab.Rest();
            lab.ContinueAtTheLatestSave();
        }),
        ["S12 event: quit on arrival, continue, choose, quit, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.WalkToAnEvent();
            lab.ContinueAtTheLatestSave();
            lab.ChooseEventOption();
            lab.ContinueAtTheLatestSave();
        }),
        ["S13 treasure: quit on arrival, continue, take the relic, quit, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.WalkTo(MapPointType.Treasure);
            lab.ContinueAtTheLatestSave();
            lab.TakeChest();
            lab.ContinueAtTheLatestSave();
        }),
        ["S14 give up mid-fight"] = Continuous(0, Verdict.NoFinishedFight, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();
        }),
        ["S15 give up on the loot screen after a won fight"] = Continuous(0, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
        }),
        ["S16 quit mid-fight, continue, give up mid-fight"] = Continuous(1, Verdict.NoFinishedFight, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayOneCard();
        }),
        ["S17 quit on the loot screen untouched, continue, quit again, continue"] = Continuous(0, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayToVictory();
            lab.ContinueAtTheLatestSave();
            lab.ContinueAtTheLatestSave();
            lab.TakeGoldAndSkipTheRest();
        }),
        ["S18 quit on arrival, continue, one card, quit, continue, finish"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.ContinueAtTheLatestSave();
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayToVictory();
        }),
        ["S19 event page chosen, quit on the map, continue"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.WalkToAnEvent();
            lab.ChooseEventOption();
            lab.ContinueAtTheLatestSave();
        }),
        ["S20 rest site arrival right after a won fight, quit, continue"] = Continuous(0, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.WalkTo(MapPointType.RestSite);
            lab.ArrivedStraightFromAWonFight();
            lab.ContinueAtTheLatestSave();
        }),
        ["S21 mod off for a session that won the fight and moved on"] = new(lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();
            lab.ContinueWithoutTheRecorder();
            lab.PlayToVictory();
            lab.TakeGoldAndSkipTheRest();
            lab.MoveOn();
            lab.ContinueIntoAStateTheJournalNeverSaw();
        }, NativeSource.BrokenContinuity, 0, Verdict.Hole),
        ["S22 mod off for a session whose plays were rolled back"] = Continuous(1, Verdict.Publishable, lab =>
        {
            lab.Neow();
            lab.MoveTo(MapPointType.Monster);
            lab.PlayOneCard();
            lab.ContinueWithoutTheRecorder();
            lab.PlayOneCard();
            lab.PlayOneCard();
            lab.ContinueAtTheLatestSave();
            lab.PlayToVictory();
        }),
    };

    // ── The moment between the run and its room ──────────────────────────────────

    /// <summary>Runs <paramref name="observe"/> when the retail continue sequence is
    /// about to re-enter the run's room: the run exists, its save is applied, and
    /// nothing has been entered yet.</summary>
    private static IDisposable BeforeTheRoomIsEntered(Action observe)
    {
        var harmony = new Harmony($"recorder-continue.{Guid.NewGuid():N}");
        Observation.Observe = observe;
        harmony.Patch(
            AccessTools.Method(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord)),
            prefix: new HarmonyMethod(typeof(Observation), nameof(Observation.Before)));
        return new Unpatch(harmony);
    }

    /// <summary>Runs <paramref name="observe"/> when a combat room the run already
    /// stands in is about to have its fight set up.</summary>
    private static IDisposable BeforeTheFightIsSetUp(Action observe)
    {
        var harmony = new Harmony($"recorder-continue.{Guid.NewGuid():N}");
        Observation.Observe = observe;
        harmony.Patch(
            AccessTools.Method(typeof(CombatManager), nameof(CombatManager.SetUpCombat)),
            prefix: new HarmonyMethod(typeof(Observation), nameof(Observation.Before)));
        return new Unpatch(harmony);
    }

    private static class Observation
    {
        internal static Action? Observe;

        internal static void Before() => Observe?.Invoke();
    }

    private sealed class Unpatch(Harmony harmony) : IDisposable
    {
        public void Dispose()
        {
            Observation.Observe = null;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    // ── The lab ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// One recorded Ironclad run on the whole-act seed, driven through the recorder
    /// and the game's own saves: play, quit, continue from a save the game took, and
    /// at the end give up and replay every discarded branch through the engine.
    ///
    /// Every quit is the retail one headlessly - the run torn down, the recorder
    /// detached - and every continue is <see cref="GameSession.RestoreSavedRun"/> on a
    /// save <see cref="RunSaveInterception"/> collected, with the driver constructed
    /// first so the rewards a pre-finished room re-offers are parked the way the
    /// client's own loot screen holds them.
    /// </summary>
    private sealed class Lab(RecorderContinueTests owner)
    {
        private readonly List<ActionRecord> _actions = [];
        private GameSession? _session;
        private RunDriver? _driver;
        private string? _runId;
        private DateTimeOffset? _startedUtc;
        private Watched? _lastWatched;

        internal List<InterceptedRunSave> Saves { get; } = [];

        /// <summary>The run-start save, taken through the game's own serializer at the
        /// moment the retail character-select screen takes it: before the first act
        /// is entered, which no headless path saves at.</summary>
        internal InterceptedRunSave RunStartSave { get; private set; } = null!;

        internal RunCapture Capture => RunRecorder.Active!.Capture;

        internal string JournalPath => Path.Combine(owner._root, "recordings", $"{_runId}{RunJournal.FileExtension}");

        /// <summary>The recorder's patches installed and the game's saves collected,
        /// with a fresh run started and the recorder attached to it - or, on a second
        /// call, nothing started, for the caller to restore or start what it wants.</summary>
        internal IDisposable Recording()
        {
            var patches = Patched();
            RunRecorder.GameIdentitySource = HeadlessIdentity;
            RunRecorder.Clock = new PumpedSettleClock();
            var collecting = RunSaveInterception.Collect(Saves.Add);
            if (_runId is null)
            {
                _session = StartIronclad();
                _driver = new RunDriver(_session);
                _driver.ImproviseUnrecordedCardSelections();
                var runStart = RunManager.Instance.ToSave(null);
                RunStartSave = new InterceptedRunSave(
                    JsonSerializationUtility.ToJson(runStart), runStart.SchemaVersion,
                    InterceptedRunSave.NoPreFinishedRoom, _session.RunState.TotalFloor, _session.RunState.ActFloor,
                    Field("run.map_coord"));
                _driver.EnterFirstRoom();
                Attach();
            }

            return new Scope(() =>
            {
                collecting.Dispose();
                if (RunManager.Instance is { IsInProgress: true }) Quit();
                _driver?.Dispose();
                _driver = null;
                patches.Dispose();
            });
        }

        private sealed class Scope(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }

        internal void Attach()
        {
            Assert.True(RunRecorder.HasEnteredItsRoom());
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
            _runId ??= Capture.RunId;
            _startedUtc ??= LiveRun.RunStartedUtc();
            Assert.Equal(_runId, Capture.RunId);
        }

        /// <summary>A fresh run on the same seed stood in its first room: the state
        /// the retail restore of the run-start save produces. The restored run keeps
        /// the start time the save holds, which is what names the recording, so the
        /// fresh run is given the recorded run's.</summary>
        internal void StartAtTheFirstRoom()
        {
            _session = StartIronclad();
            var startTime = typeof(RunManager).GetField(
                "_startTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            startTime.SetValue(RunManager.Instance, Convert.ChangeType(
                _startedUtc!.Value.ToUnixTimeSeconds(), startTime.FieldType,
                System.Globalization.CultureInfo.InvariantCulture));
            _driver = new RunDriver(_session);
            _driver.ImproviseUnrecordedCardSelections();
            _driver.EnterFirstRoom();
        }

        internal string Field(string field) => RecorderContinueTests.Field(_session!, field);

        internal void Apply(ActionVerb verb, params (string Key, string Value)[] args) =>
            RecorderContinueTests.Apply(_driver!, _actions, verb, args);

        internal void Neow()
        {
            Apply(ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
            DrainSettles();
        }

        internal void MoveTo(MapPointType type) => RecorderContinueTests.MoveTo(_driver!, _actions, _session!, type);

        /// <summary>The map move's prefix run on a reachable node and its postfix
        /// never run: what the recorder is left with when the game member threw
        /// between the two, a decision read and never announced.</summary>
        internal void OpenAMapMoveThatNeverAnnounces()
        {
            var open = RunRecorder.Active!.OpenTicketCount;
            RunRecorder.MapMove.Before(Current(_session!).Children.First().coord);
            Assert.Equal(open + 1, RunRecorder.Active!.OpenTicketCount);
        }

        internal void PlayToVictory() => RecorderContinueTests.PlayToVictory(_driver!, _actions, _session!);

        internal void PlayOneCard()
        {
            var hand = _session!.RunState.Players[0].PlayerCombatState!.Hand.Cards;
            var index = hand.ToList().FindIndex(card => card.CanPlay(out _, out _));
            Assert.True(index >= 0, "no card in the hand could be played");
            var card = hand[index];
            var alive = CombatManager.Instance!.DebugOnlyGetState()!.Enemies.Count(enemy => enemy is { IsAlive: true });
            Apply(ActionVerb.PlayCard,
            [
                ("card_id", card.Id.ToString()),
                ("hand_index", N(index)),
                .. card.TargetType == TargetType.AnyEnemy && alive > 1 ? new[] { ("target_index", "0") } : [],
            ]);
        }

        /// <summary>Plays every playable card and ends the turn.</summary>
        internal void PlayTheTurnOut()
        {
            while (Field("combat.outcome") == "in_progress" &&
                   _session!.RunState.Players[0].PlayerCombatState!.Hand.Cards.Any(card => card.CanPlay(out _, out _)))
            {
                PlayOneCard();
            }

            Assert.Equal("in_progress", Field("combat.outcome"));
            Apply(ActionVerb.EndTurn);
        }

        internal void TakeGoldAndSkipTheRest() => RecorderContinueTests.TakeGoldAndSkipTheRest(_driver!, _actions);

        /// <summary>Moves to the next floor, an ordinary fight where there is one.</summary>
        internal void MoveOn() =>
            Move(_driver!, _actions, _session!, Current(_session!).Children
                .OrderBy(child => child.PointType == MapPointType.Monster ? 0 : 1).ThenBy(child => child.coord.col).First());

        /// <summary>Walks to the nearest node of the given type, playing every room on
        /// the way to its end.</summary>
        internal void WalkTo(MapPointType type)
        {
            foreach (var node in RouteTo(_session!, type, throughUnknown: true))
            {
                Move(_driver!, _actions, _session!, node);
                if (node.PointType == type) return;
                HandleRoomToEnd();
            }
        }

        /// <summary>The floor just arrived on was entered from a won fight's loot
        /// screen, so the arrival is the first reading past that fight.</summary>
        internal void ArrivedStraightFromAWonFight()
        {
            Assert.Equal(ActionVerb.MapMove, _actions[^1].Verb);
            Assert.Contains(_actions[^2].Verb, new[] { ActionVerb.ClaimReward, ActionVerb.SkipRewards });
            Assert.Equal("none", Field("combat.outcome"));
        }

        /// <summary>Walks the cheapest route to the nearest node of the given type
        /// through ordinary fights only, playing every room on the way to its end
        /// except the last one, a fight left won on its loot screen with nothing
        /// claimed; returns the play that won it. The target node is not entered.</summary>
        internal int WalkToTheFightBefore(MapPointType type)
        {
            var route = RouteTo(_session!, type);
            Assert.True(route.Count >= 2 && route[^2].PointType == MapPointType.Monster,
                "the route to that node does not end in an ordinary fight on this seed");
            foreach (var node in route.Take(route.Count - 1))
            {
                Move(_driver!, _actions, _session!, node);
                PlayToVictory();
                if (node == route[^2]) return Capture.NextSeq - 1;
                TakeGoldAndSkipTheRest();
            }

            throw new InvalidOperationException("unreachable");
        }

        /// <summary>Walks into ? nodes until one resolves to an ordinary event room,
        /// playing every room on the way.</summary>
        internal void WalkToAnEvent()
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                foreach (var node in RouteTo(_session!, MapPointType.Unknown, throughUnknown: true))
                {
                    Move(_driver!, _actions, _session!, node);
                    if (node.PointType == MapPointType.Unknown && _session!.RunState.CurrentRoom is EventRoom) return;
                    HandleRoomToEnd();
                }
            }

            throw new InvalidOperationException("no ? node resolved to an event on this seed");
        }

        /// <summary>Plays the room the run stands in to its end, whatever it is.</summary>
        internal void HandleRoomToEnd()
        {
            switch (_session!.RunState.CurrentRoom?.RoomType)
            {
                case RoomType.Monster:
                    PlayToVictory();
                    TakeGoldAndSkipTheRest();
                    break;
                case RoomType.RestSite:
                    Rest();
                    break;
                case RoomType.Treasure:
                    TakeChest();
                    break;
                case RoomType.Shop:
                    BuyOneThing();
                    break;
                case RoomType.Event:
                    for (var page = 0;
                         page < 6 && _session.RunState.CurrentRoom is EventRoom &&
                         RunManager.Instance.EventSynchronizer.GetLocalEvent()?.CurrentOptions.Count > 0;
                         page++)
                    {
                        ChooseEventOption();
                        if (Field("combat.outcome") == "in_progress") PlayToVictory();
                        if (_driver!.UnclaimedRewardKinds.Count > 0) TakeGoldAndSkipTheRest();
                    }

                    break;
                default:
                    throw new InvalidOperationException($"no rule for room {_session.RunState.CurrentRoom?.RoomType}");
            }
        }

        internal void Rest()
        {
            var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().ToList();
            var index = Math.Max(0, options.FindIndex(option => option.OptionId == "HEAL"));
            Apply(ActionVerb.ChooseRestSiteOption, ("option_id", options[index].OptionId), ("option_index", N(index)));
        }

        internal void TakeChest()
        {
            var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics
                ?? throw new InvalidOperationException("the chest offers no relic");
            Apply(ActionVerb.TakeChestRelic, ("relic_id", relics[0].Id.ToString()), ("option_index", "0"));
            if (_driver!.UnclaimedRewardKinds.Count > 0) Apply(ActionVerb.SkipRewards);
        }

        internal void BuyOneThing()
        {
            var room = (MerchantRoom)_session!.RunState.CurrentRoom!;
            var inventory = room.GetLocalInventory();
            var gold = _session.RunState.Players[0].Gold;
            var (entry, position) = inventory.CharacterCardEntries
                .Select((candidate, index) => (candidate, index))
                .Where(card => card.candidate.IsStocked && card.candidate.Cost <= gold)
                .OrderBy(card => card.candidate.Cost)
                .First();
            var id = ((MerchantCardEntry)entry).CreationResult!.Card.Id.ToString();
            Apply(ActionVerb.ShopPurchase,
                ("kind", ShopPurchaseKinds.CharacterCard),
                (ShopPurchaseKinds.IdArgument(ShopPurchaseKinds.CharacterCard)!, id),
                ("option_index", N(position)));
        }

        internal IReadOnlyList<string> EventOptionKeys() =>
            RunManager.Instance.EventSynchronizer.GetLocalEvent().CurrentOptions.Select(RunDriver.OptionKey).ToList();

        internal void ChooseEventOption()
        {
            var localEvent = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            Apply(ActionVerb.ChooseEventOption,
                ("event_id", localEvent.Id.ToString()),
                ("option_index", "0"),
                ("option_key", RunDriver.OptionKey(localEvent.CurrentOptions[0])));
        }

        /// <summary>Leaves to the main menu: the game tears the run down and the
        /// recorder detaches, keeping the journal.</summary>
        internal void Quit()
        {
            if (RunRecorder.Active is { } recorder)
            {
                _runId ??= recorder.Capture.RunId;
                _lastWatched = new Watched(
                    recorder.Capture.LatestSavePointSeq, recorder.Capture.NextSeq, recorder.Capture.Discarded.Count,
                    recorder.Capture.Journal.Entries[^1]);
            }

            _driver?.Dispose();
            _driver = null;
            RunManager.Instance.CleanUp();
            Assert.Null(RunRecorder.Active);
            _session = null;
        }

        /// <summary>Quits, and continues from <paramref name="save"/> the way the
        /// game's own Continue does, then attaches the recorder to what it restored.</summary>
        internal void QuitAndContinue(InterceptedRunSave save, bool attachRecorder = true)
        {
            Quit();
            Continue(save, attachRecorder);
        }

        private void Continue(InterceptedRunSave save, bool attachRecorder)
        {
            _session = new GameSession();
            _driver = new RunDriver(_session);
            _driver.ImproviseUnrecordedCardSelections();
            _session.RestoreSavedRun(save.Json);
            Pump.Drain();
            if (attachRecorder) Attach();
        }

        /// <summary>What the recorder held when the run was last quit under its watch:
        /// the decision the game's latest save holds the state after, how many
        /// decisions there were, how many branches, and its last reading.</summary>
        private sealed record Watched(int LatestSave, int NextSeq, int Branches, RunJournalEntry LastEntry);

        /// <summary>
        /// Quits and continues from the game's latest save, which is what the game's
        /// own Continue restores, and holds the resume to the recorder's contract for
        /// it: continuous, resumed at the save's own decision, with exactly the
        /// decisions made after that save kept as one more discarded branch and no
        /// branch at all where there were none. The contract is the same whatever
        /// room the save was taken in, and the same across a session the recorder
        /// was not watching, whose plays the game rolled back. A refusal fails with
        /// the fields the restored run differed in.
        /// </summary>
        internal void ContinueAtTheLatestSave()
        {
            QuitAndContinue(Saves.Last());
            var watched = _lastWatched!;
            var resumed = Capture;
            Assert.True(
                resumed.Continuity == NativeSource.ContinuousContinuity && resumed.Refusals.Count == 0,
                $"continuity {resumed.Continuity}: {resumed.Refusal}; " +
                RunRecorder.DescribeResumeDifferences(watched.LastEntry, LiveRun.Read().Sample));
            Assert.Equal(RunCaptureState.Recording, resumed.State);
            Assert.Equal(watched.LatestSave + 1, resumed.NextSeq);
            Assert.Equal(watched.LatestSave, resumed.LatestSavePointSeq);
            var undone = watched.NextSeq - 1 - watched.LatestSave;
            if (undone == 0)
            {
                Assert.Equal(watched.Branches, resumed.Discarded.Count);
                return;
            }

            Assert.Equal(watched.Branches + 1, resumed.Discarded.Count);
            var branch = resumed.Discarded[^1];
            Assert.False(branch.Reload);
            Assert.Equal(watched.LatestSave, branch.RollbackToSeq);
            Assert.Equal(undone, branch.Actions.Count);
        }

        /// <summary>Quits and continues from the latest save with the mod off for
        /// the session: nothing watches what is played next.</summary>
        internal void ContinueWithoutTheRecorder()
        {
            QuitAndContinue(Saves.Last(), attachRecorder: false);
            Assert.Null(RunRecorder.Active);
        }

        /// <summary>Quits and continues from a save the game took while nobody was
        /// recording, at a moment the journal never saw: the watch is broken and says
        /// so, and goes on recording into a recording that can never be published.</summary>
        internal void ContinueIntoAStateTheJournalNeverSaw()
        {
            QuitAndContinue(Saves.Last());
            var resumed = Capture;
            Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
            Assert.Equal(RunCaptureState.Broken, resumed.State);
            Assert.Contains("not one this recording ever saw", resumed.Refusal!, StringComparison.Ordinal);
        }

        /// <summary>
        /// Quits and continues from the run-start save while a later save is on disk:
        /// a reload of an older save, so the resume is rewound with every decision
        /// since as the reload's branch. The headless restore of that save stops one
        /// step short of the room the retail restore re-enters, and the recorder must
        /// not read a run that has no room yet - that gap is what broke every retail
        /// Continue - so the row holds that first and then stands the run where the
        /// retail restore leaves it.
        /// </summary>
        internal void ContinueFromTheRunStartSave()
        {
            Quit();
            Continue(RunStartSave, attachRecorder: false);
            Assert.False(RunRecorder.HasEnteredItsRoom());
            Quit();

            StartAtTheFirstRoom();
            Attach();
            var watched = _lastWatched!;
            var resumed = Capture;
            Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
            Assert.Equal(0, resumed.NextSeq);
            Assert.Equal(watched.Branches + 1, resumed.Discarded.Count);
            var branch = resumed.Discarded[^1];
            Assert.True(branch.Reload);
            Assert.Equal(-1, branch.RollbackToSeq);
            Assert.Equal(watched.NextSeq, branch.Actions.Count);
        }

        /// <summary>The recording a given-up run leaves, and the validator's answer on it.</summary>
        internal sealed record GivenUp(ReplayManifest Manifest, ManifestValidator.ValidationResult Validation);

        /// <summary>Gives the run up from the pause menu, which writes the manifest,
        /// and hands it back with the validator's answer on it, whatever that is.</summary>
        internal GivenUp GiveUp()
        {
            Assert.NotNull(_session);
            typeof(RunManager).GetProperty(nameof(RunManager.IsAbandoned))!.SetValue(RunManager.Instance, true);
            RunRecorder.RunEnded(isVictory: false);

            var path = Path.Combine(owner._root, "recordings", $"{_runId}{RecordingLibrary.ManifestExtension}");
            var manifest = ManifestJson.Deserialize(File.ReadAllText(path));
            Assert.Equal("abandoned", manifest.Source.Native!.Outcome);
            return new GivenUp(manifest, ManifestValidator.Validate(manifest));
        }

        /// <summary>Gives the run up and hands the manifest back validated.</summary>
        internal ReplayManifest Abandon()
        {
            var given = GiveUp();
            Assert.True(given.Validation.IsValid, given.Validation.Describe());
            return given.Manifest;
        }

        /// <summary>The continued history replayed through the engine from run start,
        /// past the retail preflight a headless recording's own patch roster rightly
        /// fails: every checkpoint the recorder wrote holds, and every boundary it
        /// declares reproduces at its own coordinate with the digest it captured.</summary>
        internal VerificationReport ReplayTheWholeHistory(ReplayManifest manifest)
        {
            if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
            var session = new GameSession();
            session.StartRun(
                manifest.Environment.Seed.Value, manifest.Environment.Character.Value,
                manifest.Environment.Ascension.Value, manifest.Environment.GameMode.Value,
                manifest.Environment.Acts.Value, RecordedFightEntry.SuppliedProgressFor(manifest));
            try
            {
                var identity = Preflight.EvaluateStartedRun(manifest.Environment);
                Assert.True(identity.Matches);
                var outcome = Engine.Arbiter.ReplayStartedRun(session, manifest, identity, null, null);
                Assert.True(
                    outcome.Report.Status == VerificationStatus.Verified,
                    $"{outcome.Report.Status}; {string.Join(" / ", outcome.Report.Diagnostics)}");

                foreach (var declared in manifest.Boundaries)
                {
                    var derived = outcome.Report.Boundaries.SingleOrDefault(candidate =>
                        candidate.Kind == declared.Kind && candidate.Fight == declared.Fight &&
                        candidate.Floor == declared.Floor && candidate.Turn == declared.Turn);
                    Assert.True(derived is not null, $"the replay did not reach {declared.Describe()}");
                    Assert.True(
                        declared.Digest.Value == derived!.Digest.Value,
                        $"{declared.Describe()}: recorded {declared.Digest.Value}, replayed {derived.Digest.Value}");
                }

                return outcome.Report;
            }
            finally
            {
                if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
            }
        }

        /// <summary>Every discarded branch replayed through the engine from the
        /// continued history, past the retail preflight a headless recording's own
        /// patch roster rightly fails, and held to the state it left from and the
        /// state it ended in.</summary>
        internal IReadOnlyList<VerificationReport> ReplayEveryBranch(ReplayManifest manifest)
        {
            var branches = manifest.Source.Native!.Discarded ?? [];
            Assert.NotEmpty(branches);
            var reports = new List<VerificationReport>();
            for (var index = 0; index < branches.Count; index++)
            {
                if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
                var session = new GameSession();
                session.StartRun(
                    manifest.Environment.Seed.Value, manifest.Environment.Character.Value,
                    manifest.Environment.Ascension.Value, manifest.Environment.GameMode.Value,
                    manifest.Environment.Acts.Value, RecordedFightEntry.SuppliedProgressFor(manifest));
                try
                {
                    var identity = Preflight.EvaluateStartedRun(manifest.Environment);
                    Assert.True(identity.Matches);
                    var outcome = Engine.Arbiter.ReplayDiscardedBranchOnStartedRun(session, manifest, index, identity);
                    Assert.True(
                        outcome.Report.Status == VerificationStatus.Verified,
                        $"branch {index}: {outcome.Report.Status}; {string.Join(" / ", outcome.Report.Diagnostics)}");
                    reports.Add(outcome.Report);
                }
                finally
                {
                    if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
                }
            }

            return reports;
        }
    }

    // ── The run ──────────────────────────────────────────────────────────────────

    private static GameSession StartRun()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(Seed, "CHARACTER.SILENT", 0, "standard", Acts);
        return session;
    }

    private static void DrainSettles()
    {
        if (RunRecorder.Clock is PumpedSettleClock pumped) pumped.Drain();
    }

    private static void PlayFirstPlayable(GameSession session)
    {
        var player = session.RunState.Players[0];
        var hand = player.PlayerCombatState!.Hand.Cards;
        var index = hand.ToList().FindIndex(card => card.CanPlay(out _, out _));
        Assert.True(index >= 0, "no card in the hand could be played");
        var card = hand[index];
        var target = card.TargetType == TargetType.AnyEnemy
            ? CombatManager.Instance!.DebugOnlyGetState()!.Enemies.First(enemy => enemy is { IsAlive: true })
            : null;
        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(new PlayCardAction(card, target));
        Pump.Drain();
        DrainSettles();
    }

    private static bool HeadlessIdentity() => EngineHost.Origin == EngineOrigin.HeadlessHost;

    private static Patches Patched() => new();

    private sealed class Patches : IDisposable
    {
        private readonly Harmony _harmony = new($"recorder-continue-patches.{Guid.NewGuid():N}");
        private readonly Action<CardPrompts.Prompt, IReadOnlyList<CardModel>>? _previousAnswered = CardPrompts.Answered;
        private readonly Action<int?>? _previousReward = CardScreensUp.RewardAnswered;

        internal Patches()
        {
            foreach (var type in CardScreensUp.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in CardPrompts.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in RunRecorder.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            RunRecorder.ReadTheAnswers();
        }

        public void Dispose()
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
            RunRecorder.RunTornDown();
            _harmony.UnpatchAll(_harmony.Id);
            CardPrompts.Answered = _previousAnswered;
            CardScreensUp.RewardAnswered = _previousReward;
            CardPrompts.Forget();
        }
    }

    // ── Small readers ────────────────────────────────────────────────────────────

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];

    private static string N(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static MapEdge FirstEdge(GameSession session)
    {
        var current = Field(session, "run.map_coord");
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), System.Globalization.CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture);
        return session.CurrentMapTopology().Edges
            .Where(edge => edge.FromRow == row && edge.FromColumn == column)
            .OrderBy(edge => edge.ToColumn).First();
    }

    private static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
    {
        Seq = seq,
        Verb = verb,
        Args = new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        Source = FactSource.Declared,
    };

    // ── An Ironclad run that wins on attacks alone ───────────────────────────────

    private const string IroncladSeed = "67L571H38L";

    private static GameSession StartIronclad()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(IroncladSeed, "CHARACTER.IRONCLAD", 0, "standard", Acts);
        return session;
    }

    private static void Apply(
        RunDriver driver, List<ActionRecord> actions, ActionVerb verb, params (string Key, string Value)[] args)
    {
        var action = Record(actions.Count, verb, args);
        driver.Apply(action);
        actions.Add(action);
        DrainSettles();
    }

    private static MapPoint Current(GameSession session)
    {
        var coord = session.RunState.CurrentMapCoord!.Value;
        return session.RunState.Map!.GetPoint(coord.col, coord.row)!;
    }

    private static void MoveTo(RunDriver driver, List<ActionRecord> actions, GameSession session, MapPointType type) =>
        Move(driver, actions, session,
            Current(session).Children.Where(child => child.PointType == type).OrderBy(child => child.coord.col).First());

    private static void Move(RunDriver driver, List<ActionRecord> actions, GameSession session, MapPoint next) =>
        Apply(driver, actions, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)),
            ("row", N(next.coord.row)), ("column", N(next.coord.col)));

    /// <summary>
    /// The cheapest route from the run's node to the nearest node of the given type,
    /// by fights fought. Through ordinary fights only by default, so every room on
    /// the way is one <see cref="PlayToVictory"/> has a rule for; the lab's walk
    /// allows every ordinary room, with a ? node costing a little because it may
    /// resolve to a fight. Elites and the boss are never entered.
    /// </summary>
    private static IReadOnlyList<MapPoint> RouteTo(GameSession session, MapPointType type, bool throughUnknown = false)
    {
        static int Cost(MapPointType pointType) => pointType switch
        {
            MapPointType.Monster => 3,
            MapPointType.Unknown => 1,
            _ => 0,
        };

        var start = Current(session);
        var best = new Dictionary<MapPoint, int> { [start] = 0 };
        var paths = new Dictionary<MapPoint, List<MapPoint>> { [start] = [] };
        var frontier = new PriorityQueue<MapPoint, int>();
        frontier.Enqueue(start, 0);
        while (frontier.TryDequeue(out var node, out var cost))
        {
            if (cost > best[node]) continue;
            foreach (var child in node.Children.OrderBy(child => child.coord.col))
            {
                if (child.PointType is MapPointType.Elite or MapPointType.Boss) continue;
                var next = new List<MapPoint>(paths[node]) { child };
                if (child.PointType == type) return next;
                if (!throughUnknown && child.PointType != MapPointType.Monster) continue;
                if (throughUnknown && child.PointType == MapPointType.Unknown && type == MapPointType.Monster) continue;
                var through = cost + Cost(child.PointType);
                if (best.TryGetValue(child, out var known) && known <= through) continue;
                best[child] = through;
                paths[child] = next;
                frontier.Enqueue(child, through);
            }
        }

        throw new InvalidOperationException($"no route to a {type} node on this seed");
    }

    /// <summary>The first playable attack, else the first playable card, to the end
    /// of the fight: the whole-act fixture's own rule, which wins this seed's first
    /// two fights.</summary>
    private static void PlayToVictory(RunDriver driver, List<ActionRecord> actions, GameSession session)
    {
        for (var turn = 0; turn < 40 && Field(session, "combat.outcome") == "in_progress"; turn++)
        {
            while (Field(session, "combat.outcome") == "in_progress")
            {
                var hand = session.RunState.Players[0].PlayerCombatState!.Hand.Cards;
                var playable = Enumerable.Range(0, hand.Count).Where(i => hand[i].CanPlay(out _, out _)).ToList();
                var index = playable.FirstOrDefault(i => hand[i].Type == CardType.Attack, playable.Count > 0 ? playable[0] : -1);
                if (index < 0) break;
                var card = hand[index];
                var alive = CombatManager.Instance!.DebugOnlyGetState()!.Enemies.Count(enemy => enemy is { IsAlive: true });
                Apply(driver, actions, ActionVerb.PlayCard,
                [
                    ("card_id", card.Id.ToString()),
                    ("hand_index", N(index)),
                    .. card.TargetType == TargetType.AnyEnemy && alive > 1 ? new[] { ("target_index", "0") } : [],
                ]);
            }

            if (Field(session, "combat.outcome") != "in_progress") break;
            Apply(driver, actions, ActionVerb.EndTurn);
        }

        Assert.Equal("victory", Field(session, "combat.outcome"));
    }

    private static void TakeGoldAndSkipTheRest(RunDriver driver, List<ActionRecord> actions)
    {
        if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
        {
            Apply(driver, actions, ActionVerb.ClaimReward, ("reward_type", "gold"));
        }

        if (driver.UnclaimedRewardKinds.Count > 0) Apply(driver, actions, ActionVerb.SkipRewards);
    }
}
