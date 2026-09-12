using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What can be checked about the in-game capture on a machine that is not running
/// the game: that the mod ships the recording's fights and refuses a set that is not
/// this recording's, and that every game surface the observer subscribes to or reads
/// is still there on this build. A build that renamed one would install an observer
/// with a hole in it, and the hole would be a fight sampled on one side only, which
/// the capture refuses - but refusing every fight is not a product.
/// </summary>
public sealed class PlayerFightObserverTests
{
    private static string ModAssemblyPath => Path.Combine(AppContext.BaseDirectory, "Runmobile.dll");

    [ObserverFact]
    public void TheModShipsTheRecordingsFightsBoundToTheRecording()
    {
        var shipped = ModAssembly().GetType("Sts2PilotTrainer.Mod.ShippedRecording")!;
        var recording = shipped.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var fights = shipped.GetMethod("ReadFights", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [recording]);

        Assert.NotNull(fights);
        Assert.Equal("navegreed-OJ-6QXhNgdg", fights!.GetType().GetProperty("RunId")!.GetValue(fights));
    }

    [ObserverFact]
    public async Task ASettlementTimeoutRefusesTheCaptureWithoutSampling()
    {
        var capture = FightCapture.Begin(
            "player",
            new Dictionary<string, string> { ["combat.outcome"] = "in_progress" },
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        capture.BeginStep(
            "PlayCard",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["combat.outcome"] = "in_progress" });

        Assert.False(await Settle(
            capture,
            screensOpen: () => 0,
            isSettled: () => false,
            newBudget: () => Task.CompletedTask,
            nextPoll: () => Task.CompletedTask));

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("did not settle within 30 seconds", capture.Refusal, StringComparison.Ordinal);
        Assert.False(capture.HasOpenStep);

        capture.CompleteStep(new Dictionary<string, string> { ["combat.outcome"] = "victory" });
        Assert.Single(capture.Trace.Steps);
        Assert.Throws<ManifestException>(capture.Project);
    }

    /// <summary>
    /// A person at an in-fight card screen is not the engine failing to settle.
    ///
    /// A played card can open a prompt over the hand or a pile, and the selection is
    /// awaited inside the action - so the executor stays running and the queue stays
    /// full for as long as the player is choosing. This wait used to charge that
    /// thinking against the engine's thirty seconds, and a player who took longer had
    /// the fight marked incomplete and got no comparison. That had been true of the
    /// Combat Trainer's own capture since before the recorder existed; both read this
    /// one wait.
    /// </summary>
    [ObserverFact]
    public async Task AnInFightCardScreenDoesNotSpendTheEnginesBudget()
    {
        var capture = FightCapture.Begin(
            "player",
            new Dictionary<string, string> { ["combat.outcome"] = "in_progress" },
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        capture.BeginStep(
            "PlayCard",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["combat.outcome"] = "in_progress" });

        // The screen is up for the first three polls; the engine is idle throughout,
        // which is what a selection awaited inside an action looks like from here.
        var polls = 0;
        var budgetsStartedAt = new List<int>();

        Assert.True(await Settle(
            capture,
            screensOpen: () => polls < 3 ? 1 : 0,
            isSettled: () => true,
            newBudget: () =>
            {
                budgetsStartedAt.Add(polls);
                return new TaskCompletionSource<bool>().Task;
            },
            nextPoll: () =>
            {
                polls++;
                return Task.CompletedTask;
            }));

        // Settled, and the capture is untouched. The engine's budget started only once
        // the screen had come down, so none of the player's thinking was charged to it.
        Assert.Equal(FightCaptureState.Live, capture.State);
        Assert.Null(capture.Refusal);
        Assert.All(budgetsStartedAt, at => Assert.True(at >= 3, $"a budget started at poll {at}, mid-screen"));
        Assert.NotEmpty(budgetsStartedAt);
    }

    /// <summary>The observer's half of the settle, reached the way the observer reaches
    /// it: by name, out of the built mod assembly.</summary>
    private static Task<bool> Settle(
        IFightSampleSink sink,
        Func<int> screensOpen,
        Func<bool> isSettled,
        Func<Task> newBudget,
        Func<Task> nextPoll,
        Task? becameEmpty = null) =>
        Assert.IsAssignableFrom<Task<bool>>(
            ModAssembly().GetType("Sts2PilotTrainer.Mod.PlayerFightObserver")!
                .GetMethod("WaitUntilSettled", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null,
                [
                    sink,
                    screensOpen,
                    becameEmpty ?? Task.CompletedTask,
                    isSettled,
                    (Func<bool>)(() => false),
                    newBudget,
                    nextPoll,
                ]));

    [ObserverFact]
    public void TheActionExecutorStillAnnouncesEveryActionEitherSide()
    {
        var executor = GameType("MegaCrit.Sts2.Core.GameActions.ActionExecutor");
        Assert.NotNull(executor.GetEvent("BeforeActionExecuted"));
        Assert.NotNull(executor.GetEvent("AfterActionExecuted"));
        Assert.Equal(typeof(bool), executor.GetProperty("IsRunning")!.PropertyType);

        var queues = GameType("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet");
        Assert.True(typeof(Task).IsAssignableFrom(queues.GetMethod("BecameEmpty")!.ReturnType));
        Assert.Equal(typeof(bool), queues.GetProperty("IsEmpty")!.PropertyType);
    }

    /// <summary>
    /// The engine announces an action that paused for the player's choice a second
    /// time when it carries on, and that is what the observer's resume rule is written
    /// against: the same object, announced before execution twice, in
    /// <see cref="GameActionState.ReadyToResumeExecuting"/> the second time and under a
    /// new id, finished once. Driven through the engine's own queue and executor with
    /// an action that pauses itself the way a card prompt does, because a build that
    /// stopped re-announcing, or re-announced in another state, would leave the
    /// observer opening a second step for one decision - the refusal every in-fight
    /// prompt used to cost.
    /// </summary>
    [GameFact]
    public void TheExecutorAnnouncesAResumedActionAgainInTheResumingState()
    {
        // Started before any game type is touched: this method's body names game types,
        // and the runtime resolves them on entry, before the host has said where the
        // game assembly is.
        EngineHost.Start();
        DriveAnActionThatPausesForAChoice();
    }

    private static void DriveAnActionThatPausesForAChoice()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        try
        {
            session.StartRun(
                "P1L0TTRA1NER", "CHARACTER.IRONCLAD", 0, "standard",
                ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);
            // Entering the first room is what unpauses the executor: EnterMapPoint
            // pauses it while a room is built and unpauses it for a room that is not a
            // fight. An action enqueued before that waits on a frame that never comes.
            using (var driver = new RunDriver(session)) driver.EnterFirstRoom();
            var queues = RunManager.Instance.ActionQueueSet;
            var executor = RunManager.Instance.ActionExecutor;
            var action = new PausingAction(session.RunState.Players[0].NetId);
            var announced = new List<(uint? Id, GameActionState State)>();
            var finished = 0;
            void Before(GameAction candidate)
            {
                if (ReferenceEquals(candidate, action)) announced.Add((candidate.Id, candidate.State));
            }
            void After(GameAction candidate)
            {
                if (ReferenceEquals(candidate, action)) finished++;
            }

            executor.BeforeActionExecuted += Before;
            executor.AfterActionExecuted += After;
            try
            {
                queues.EnqueueWithoutSynchronizing(action);
                Pump.Drain();
                Assert.Equal(GameActionState.GatheringPlayerChoice, action.State);
                Assert.Equal(0, finished);

                queues.ResumeActionWithoutSynchronizing(action.Id!.Value);
                Pump.Drain();
            }
            finally
            {
                executor.BeforeActionExecuted -= Before;
                executor.AfterActionExecuted -= After;
            }

            Assert.Equal(GameActionState.Finished, action.State);
            Assert.Equal(1, finished);
            Assert.Equal(
                [GameActionState.WaitingForExecution, GameActionState.ReadyToResumeExecuting],
                announced.Select(announcement => announcement.State));
            Assert.NotEqual(announced[0].Id, announced[1].Id);
        }
        finally
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
            HeadlessEngine.Forget();
        }
    }

    /// <summary>
    /// The observer, watching a real card prompt take the retail path: Survivor asks
    /// which card to discard, the engine pauses its PlayCardAction for the answer and
    /// announces it again on resumption, and the observer opens one step for it, tells
    /// the sink it resumed, and never opens a second. This is the recorder break the
    /// retail client hit - two BeginSteps for one action, which the capture refused as
    /// overlapping - reproduced against the engine's own pause rather than a stand-in
    /// action, so a build that routed a prompt differently would fail here.
    /// </summary>
    [GameFact]
    public void TheObserverTellsTheSinkAResumedCardPlayResumedRatherThanOpeningAnotherStep()
    {
        EngineHost.Start();
        WatchAPromptingCardThroughTheObserver();
    }

    private static void WatchAPromptingCardThroughTheObserver()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        try
        {
            session.StartRun(
                "P1L0TTRA1NER", "CHARACTER.IRONCLAD", 0, "standard",
                ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);
            var player = session.RunState.Players[0];
            using var driver = new RunDriver(session);
            EnterTheFirstFight(driver, session);

            // Survivor: a card whose effect asks the player which card to discard.
            var survivor = player.Creature.CombatState!.CreateCard(ModelDb.Card<Survivor>(), player);
            CardPileCmd.AddGeneratedCardToCombat(survivor, PileType.Hand, player).GetAwaiter().GetResult();
            Pump.Drain();
            var inHand = player.PlayerCombatState!.Hand.Cards.Single(card => card.Id == survivor.Id);

            var sink = new RecordingSink();
            using var observer = PlayerFightObserver.Start(
                player,
                () => CanonicalStateProjection.Project(session.RunState).Fields,
                sink,
                fightEnded: () => { },
                sampled: () => { });

            // The driver's selector answers prompts inside the call that asks, which is
            // the headless shortcut the engine takes only when one is installed. Suspend
            // it so the prompt pauses the action the way the retail client's does, and
            // answer the paused choice locally the way the client's hand would.
            var hand = new FirstCard();
            var play = new PlayCardAction(inHand, null);
            using (CardSelectCmd.SuspendSelectorForTest())
            using (CardSelectCmd.PushSelector(hand, localOnly: true))
            {
                RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(play);
                Pump.Drain();
            }

            // The engine did what the retail client does: paused the play for the
            // discard, answered it, resumed it under a new id and finished it.
            Assert.Equal(1, hand.Asked);
            Assert.Equal(GameActionState.Finished, play.State);
            Assert.DoesNotContain(inHand, player.PlayerCombatState!.Hand.Cards);

            // And the observer read the re-announcement as the same decision carrying
            // on. Before the resume rule the second announcement opened a second step,
            // which is the overlap the capture refuses. The settle after the action is
            // a Godot wait this process has no scene tree for, so what follows the
            // resume is not this test's to assert.
            Assert.Equal(["BeginStep:PlayCard", "ResumeStep"], sink.Calls.Take(2));
            Assert.Single(sink.Calls, call => call.StartsWith("BeginStep", StringComparison.Ordinal));
        }
        finally
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
            HeadlessEngine.Forget();
        }
    }

    /// <summary>Neow's first option and the map move into the first fight, the way
    /// the first-fight fixture starts.</summary>
    private static void EnterTheFirstFight(RunDriver driver, GameSession session)
    {
        driver.EnterFirstRoom();
        driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));

        var current = CanonicalStateProjection.Project(session.RunState).Fields["run.map_coord"];
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        var edge = session.CurrentMapTopology().Edges
            .Where(candidate => candidate.FromRow == row && candidate.FromColumn == column)
            .OrderBy(candidate => candidate.ToColumn)
            .First();
        driver.Apply(Record(1, ActionVerb.MapMove,
            ("act", session.RunState.CurrentActIndex.ToString(CultureInfo.InvariantCulture)),
            ("row", edge.ToRow.ToString(CultureInfo.InvariantCulture)),
            ("column", edge.ToColumn.ToString(CultureInfo.InvariantCulture))));

        Assert.Equal("true", CanonicalStateProjection.Project(session.RunState).Fields["combat.in_progress"]);
    }

    private static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
    {
        Seq = seq,
        Verb = verb,
        Args = new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        Source = FactSource.Declared,
    };

    /// <summary>Every call the observer makes on its sink, in order. What each one
    /// means is <see cref="FightCapture"/>'s and is held there; this records which were
    /// made.</summary>
    private sealed class RecordingSink : IFightSampleSink
    {
        public List<string> Calls { get; } = [];

        public void BeginStep(
            string verb, IReadOnlyDictionary<string, string> args, IReadOnlyDictionary<string, string> before,
            bool previousActionFinished) => Calls.Add($"BeginStep:{verb}");

        public void BeginStepWithUnresolvedArgument(
            string verb, IReadOnlyDictionary<string, string> resolved, IReadOnlyDictionary<string, string> before,
            bool previousActionFinished, string unresolved) => Calls.Add($"BeginStepWithUnresolvedArgument:{verb}");

        public void ResumeStep() => Calls.Add("ResumeStep");

        public void CompleteStep(IReadOnlyDictionary<string, string> after) => Calls.Add("CompleteStep");

        public void Finish(IReadOnlyDictionary<string, string> final) => Calls.Add("Finish");

        public void MarkIncomplete(string reason) => Calls.Add($"MarkIncomplete:{reason}");
    }

    /// <summary>The client's hand, answering the paused choice with its first card.</summary>
    private sealed class FirstCard : ICardSelector
    {
        public int Asked { get; private set; }

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Asked++;
            return Task.FromResult(options.Take(Math.Max(minSelect, 1)));
        }

        public CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            throw new NotSupportedException("This fight offers no card reward.");
    }

    /// <summary>An action that pauses for a player's choice the way a card prompt
    /// does, without a prompt: what <c>GameActionPlayerChoiceContext</c> does to the
    /// action whose effect asked, done to itself.</summary>
    private sealed class PausingAction(ulong ownerId) : GameAction
    {
        public override ulong OwnerId => ownerId;

        public override GameActionType ActionType => GameActionType.Any;

        protected override async Task ExecuteAction()
        {
            RunManager.Instance.ActionQueueSet.PauseActionForPlayerChoice(this, PlayerChoiceOptions.None);
            await WaitForActionToResumeExecutingAfterPlayerChoice();
        }

        public override INetAction ToNetAction() =>
            throw new NotSupportedException("A test action is never sent over the network.");
    }

    [ObserverFact]
    public void TheCombatManagerStillSaysWhenATurnStartsAndWhenTheFightEnds()
    {
        var combat = GameType("MegaCrit.Sts2.Core.Combat.CombatManager");
        Assert.NotNull(combat.GetEvent("TurnStarted"));
        Assert.NotNull(combat.GetEvent("CombatEnded"));
        Assert.Equal(typeof(bool), combat.GetProperty("IsOverOrEnding")!.PropertyType);
        Assert.NotNull(GameType("MegaCrit.Sts2.Core.Combat.CombatState").GetProperty("CurrentSide"));
    }

    [ObserverFact]
    public void TheFourPlayerActionsStillCarryWhatTheTraceRecords()
    {
        var play = GameType("MegaCrit.Sts2.Core.GameActions.PlayCardAction");
        Assert.NotNull(play.GetProperty("CardModelId"));
        Assert.NotNull(play.GetProperty("TargetId"));

        var potion = GameType("MegaCrit.Sts2.Core.GameActions.UsePotionAction");
        Assert.Equal(typeof(uint), potion.GetProperty("PotionIndex")!.PropertyType);

        var discard = GameType("MegaCrit.Sts2.Core.GameActions.DiscardPotionGameAction");
        Assert.Equal(
            typeof(uint),
            discard.GetField("_potionSlotIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);

        _ = GameType("MegaCrit.Sts2.Core.GameActions.EndPlayerTurnAction");
        _ = GameType("MegaCrit.Sts2.Core.GameActions.UndoEndPlayerTurnAction");
        Assert.NotNull(GameType("MegaCrit.Sts2.Core.GameActions.GameAction").GetProperty("OwnerId"));
    }

    private static Assembly ModAssembly() =>
        AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "Runmobile")
        ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);

    private static Type GameType(string name)
    {
        _ = Sts2PilotTrainer.Engine.EngineHost.StartupPhase();
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2")
            .GetType(name);
        Assert.True(type is not null, $"This build has no {name}.");
        return type!;
    }

    public sealed class ObserverFactAttribute : FactAttribute
    {
        public ObserverFactAttribute()
        {
            if (!File.Exists(Path.Combine(Arbiter.RepoRoot, "build", "lib", "sts2.dll")) ||
                !File.Exists(ModAssemblyPath))
            {
                Skip = "Needs the prepared game and built Runmobile mod. Run ./scripts/build.sh.";
            }
        }
    }
}
