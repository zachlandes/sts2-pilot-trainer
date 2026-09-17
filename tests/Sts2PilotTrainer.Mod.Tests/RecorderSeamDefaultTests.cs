using System.Globalization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Models;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A stranger at any of the recorder's three seams stops the recording naming what
/// it met, instead of going by unrecorded.
///
/// The recorder watches decisions member by member, so a decision arriving by a
/// member nothing patches used to be dropped: a game action a mod added or a game
/// update introduced, a choice synced by a prompt nobody patched. Three defaults
/// close that - at <c>ActionQueueSynchronizer.RequestEnqueue</c>, the local origin of
/// every game action; at the fight observer's switch, for a player-driven action that
/// is none of the five a fight is made of; and at <c>PlayerChoiceSynchronizer.SyncLocalChoice</c>,
/// for an answer no open prompt, screen, selector or announced decision claims - and
/// each reaches the same <c>RunCapture.MarkUnmapped</c> the member patches reach.
/// Driven headlessly through the real recorder, the way <c>HeadlessGameplayCaptureTests</c>
/// drives it, with a game action defined here as the stranger.
///
/// The false positive each default could produce is held as well: the engine's own
/// bookkeeping action is not a stranger, and a prompt the game's own selector answers
/// in both hosts records nothing and stops nothing.
/// </summary>
public sealed class RecorderSeamDefaultTests : IDisposable
{
    private const string Seed = "P1L0TTRA1NER";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"recorder-seam-defaults-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public RecorderSeamDefaultTests()
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
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    /// <summary>The table claims every game action a net action becomes on this
    /// build, and names none the build lacks; a twelfth action fails here before a
    /// recorder meets it.</summary>
    [GameFact]
    public void EveryGameActionANetActionBecomesIsClaimedExcusedOrNamedAsTheConsoles()
    {
        Assert.Empty(NetActionClaims.Verify());
        Assert.Equal(11, NetActionClaims.All.Count);
        Assert.Equal(NetActionClaims.Disposition.EngineDriven, NetActionClaims.For(typeof(ReadyToBeginEnemyTurnAction))!.Disposition);
        Assert.Equal(NetActionClaims.Disposition.NonStandard, NetActionClaims.For(typeof(ConsoleCmdGameAction))!.Disposition);
        Assert.Null(NetActionClaims.For(typeof(Stranger)));
    }

    [GameFact]
    public void AStrangerRequestedOutsideAFightStopsTheRecordingNamingItsType()
    {
        using var recording = Patched();
        var session = StartRecordedRun(out var driver);
        using (driver)
        {
            driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
            DrainSettles();
            var capture = RunRecorder.Active!.Capture;
            Assert.Equal(1, capture.NextSeq);

            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
                new Stranger(session.RunState.Players[0], GameActionType.NonCombat));
            Pump.Drain();
            DrainSettles();

            Assert.Equal(RunCaptureState.Unmapped, capture.State);
            Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
            var stop = capture.Stop!.Decision;
            Assert.Equal(UnmappedDecision.NetActionSeam, stop.Seam);
            Assert.Equal(nameof(Stranger), stop.Name);
            Assert.Equal(nameof(GameActionType.NonCombat), stop.Discriminator);
            Assert.Equal(1, stop.Seq);
        }
    }

    /// <summary>The engine's own readiness action is not a stranger, and a stranger
    /// requested twice - once deferred past the enemy turn and once re-requested - is
    /// classified once, so the second sight is not a second stop.</summary>
    [GameFact]
    public void TheEnginesOwnBookkeepingActionIsNotAStrangerAndAStrangerIsMetOnce()
    {
        using var recording = Patched();
        var session = StartRecordedRun(out var driver);
        using (driver)
        {
            driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
            DrainSettles();
            var capture = RunRecorder.Active!.Capture;

            // The prefix itself, twice for one instance, the way a deferred action
            // re-enters RequestEnqueue; not executed, because the action's own body
            // reaches Godot furniture this process has not got
            var player = session.RunState.Players[0];
            var ready = new ReadyToBeginEnemyTurnAction(player);
            RunRecorder.ActionRequested.Before(ready);
            RunRecorder.ActionRequested.Before(ready);
            var stranger = new Stranger(player, GameActionType.NonCombat);
            RunRecorder.ActionRequested.Before(stranger);
            RunRecorder.ActionRequested.Before(stranger);
            DrainSettles();

            Assert.Equal(RunCaptureState.Unmapped, capture.State);
            Assert.Equal(nameof(Stranger), capture.Stop!.Decision.Name);
            Assert.Empty(capture.Refusals);
        }
    }

    [GameFact]
    public void AStrangerTheExecutorRunsInsideAFightStopsTheRecordingAtTheObserver()
    {
        using var recording = Patched();
        var session = StartRecordedRun(out var driver);
        using (driver)
        {
            HeadlessRuns.EnterTheFirstFight(driver, session);
            DrainSettles();
            var capture = RunRecorder.Active!.Capture;
            Assert.NotNull(capture.Fight);

            // Straight onto the executor's queue, past RequestEnqueue, so the only
            // thing that can meet the stranger is the observer's own switch
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(
                new Stranger(session.RunState.Players[0], GameActionType.CombatPlayPhaseOnly));
            Pump.Drain();
            DrainSettles();

            Assert.Equal(RunCaptureState.Unmapped, capture.State);
            var stop = capture.Stop!.Decision;
            Assert.Equal(UnmappedDecision.NetActionSeam, stop.Seam);
            Assert.Equal(nameof(Stranger), stop.Name);
            Assert.Equal(nameof(GameActionType.CombatPlayPhaseOnly), stop.Discriminator);
        }
    }

    /// <summary>
    /// A stranger met while a decision's step is still open closes that step first
    /// and stops at the ordinal after it.
    ///
    /// The executor runs a played card and then whatever the card enqueued, before
    /// the observer has sampled the card's after-state: the play's step is open when
    /// the stranger arrives. Stopped without closing it, the stop would take the
    /// play's own ordinal with a before-reading the play had already changed, and the
    /// play would be dropped when its after-sample found the recording stopped. So the
    /// play is closed on the stranger's before-sample, the way it is closed before any
    /// decision that follows it, and the stop stands after it. Both ways a stranger
    /// enters a fight end there: straight onto the executor's queue, and requested
    /// through <c>RequestEnqueue</c>, where the request seam leaves an action the
    /// observer will meet to the observer.
    /// </summary>
    [GameTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStrangerMetWhileAPlayIsOpenClosesThePlayAndStopsAfterIt(bool requested)
    {
        using var recording = Patched();
        var session = StartRecordedRun(out var driver);
        using (driver)
        {
            HeadlessRuns.EnterTheFirstFight(driver, session);
            DrainSettles();
            var capture = RunRecorder.Active!.Capture;
            var player = session.RunState.Players[0];
            var playSeq = capture.NextSeq;

            var hand = player.PlayerCombatState!.Hand.Cards.ToList();
            var card = hand.First(candidate => candidate.CanPlay(out _, out _));
            var target = card.TargetType == TargetType.AnyEnemy
                ? CombatManager.Instance!.DebugOnlyGetState()!.Enemies.First(enemy => enemy.IsAlive)
                : null;
            var queues = RunManager.Instance.ActionQueueSet;
            queues.EnqueueWithoutSynchronizing(new PlayCardAction(card, target));
            Pump.Drain();
            Assert.Equal(playSeq, capture.NextSeq);

            // The play has executed and its after-sample is still parked on the clock
            var stranger = new Stranger(player, GameActionType.CombatPlayPhaseOnly);
            if (requested) RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(stranger);
            else queues.EnqueueWithoutSynchronizing(stranger);
            Pump.Drain();
            DrainSettles();

            Assert.Equal(RunCaptureState.Unmapped, capture.State);
            Assert.Empty(capture.Refusals);
            var play = capture.Actions[^1];
            Assert.Equal(ActionVerb.PlayCard, play.Verb);
            Assert.Equal(playSeq, play.Seq);
            Assert.Equal(card.Id.ToString(), play.Args["card_id"]);
            var stop = capture.Stop!.Decision;
            Assert.Equal(playSeq + 1, stop.Seq);
            Assert.Equal(UnmappedDecision.NetActionSeam, stop.Seam);
            Assert.Equal(nameof(Stranger), stop.Name);
        }
    }

    /// <summary>
    /// A prompt the game's own selector answers is the engine answering itself in
    /// both hosts: it opens no prompt, syncs its choice like any other, and the choice
    /// default must read that sync as nobody's decision. The card is put in the hand
    /// with the engine's own command and played through the observer, the shape
    /// <c>CardPromptCaptureTests</c> uses.
    /// </summary>
    [GameFact]
    public void AChoiceTheGamesOwnSelectorAnswersRecordsNothingAndStopsNothing()
    {
        using var recording = Patched();
        var session = StartRecordedRun(out var driver);
        using (driver)
        {
            HeadlessRuns.EnterTheFirstFight(driver, session);
            DrainSettles();
            var capture = RunRecorder.Active!.Capture;
            var player = session.RunState.Players[0];
            var decisionsBefore = capture.NextSeq;

            // Vakuu's own selector: a selector the game declares, which is what
            // CardPrompts.IsTheGamesOwn reads as the engine answering itself
            var gamesOwn = new VakuuCardSelector();
            Assert.True(CardPrompts.IsTheGamesOwn(gamesOwn));
            using (CardSelectCmd.PushSelector(gamesOwn))
            {
                var hand = player.PlayerCombatState!.Hand.Cards.ToList();
                var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
                synchronizer.SyncLocalChoice(
                    player, synchronizer.ReserveChoiceId(player), PlayerChoiceResult.FromMutableCombatCards(hand.Take(1)));
            }

            Pump.Drain();
            DrainSettles();

            Assert.True(capture.State == RunCaptureState.Recording, string.Join("; ", capture.Refusals.Select(r => r.Reason)));
            Assert.Null(capture.Stop);
            Assert.Equal(decisionsBefore, capture.NextSeq);
        }
    }

    [GameFact]
    public void AChoiceSyncedWithNothingClaimingItStopsTheRecordingNamingItsKind()
    {
        using var recording = Patched();
        var session = StartRecordedRun(out var driver);
        using (driver)
        {
            driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
            DrainSettles();
            var capture = RunRecorder.Active!.Capture;
            var player = session.RunState.Players[0];

            var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
            synchronizer.SyncLocalChoice(player, synchronizer.ReserveChoiceId(player), PlayerChoiceResult.FromIndex(2));
            Pump.Drain();
            DrainSettles();

            Assert.Equal(RunCaptureState.Unmapped, capture.State);
            var stop = capture.Stop!.Decision;
            Assert.Equal(UnmappedDecision.PlayerChoiceSeam, stop.Seam);
            Assert.Equal("PlayerChoiceSynchronizer.SyncLocalChoice", stop.Name);
            Assert.Equal(nameof(PlayerChoiceType.Index), stop.Discriminator);
            Assert.Equal("2", stop.Args["index"]);
        }
    }

    // ── The stranger ─────────────────────────────────────────────────────────────

    /// <summary>A game action this build does not have: what a mod or a game update
    /// would put on the queue.</summary>
    private sealed class Stranger(Player player, GameActionType actionType) : GameAction
    {
        public override ulong OwnerId => player.NetId;

        public override GameActionType ActionType => actionType;

        protected override Task ExecuteAction() => Task.CompletedTask;

        public override INetAction ToNetAction() =>
            throw new NotSupportedException("A stranger is never sent over the network in these tests.");
    }

    // ── The run, recorded ────────────────────────────────────────────────────────

    private static GameSession StartRecordedRun(out RunDriver driver)
    {
        RunRecorder.GameIdentitySource = () => EngineHost.Origin == EngineOrigin.HeadlessHost;
        RunRecorder.Clock = new PumpedSettleClock();
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(Seed, "CHARACTER.IRONCLAD", 0, "standard", Acts);
        driver = new RunDriver(session);
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        return session;
    }

    private static void DrainSettles()
    {
        if (RunRecorder.Clock is PumpedSettleClock pumped) pumped.Drain();
    }

    private static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) =>
        HeadlessRuns.Record(seq, verb, args);

    private static Patches Patched() => new();

    private sealed class Patches : IDisposable
    {
        private readonly Harmony _harmony = new($"recorder-seam-defaults.{Guid.NewGuid():N}");
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
}
