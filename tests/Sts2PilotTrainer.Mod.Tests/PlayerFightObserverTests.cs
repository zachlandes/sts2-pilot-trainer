using System.Reflection;
using System.Runtime.Loader;
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
        Func<Task> nextPoll) =>
        Assert.IsAssignableFrom<Task<bool>>(
            ModAssembly().GetType("Sts2PilotTrainer.Mod.PlayerFightObserver")!
                .GetMethod("WaitUntilSettled", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [sink, screensOpen, isSettled, (Func<bool>)(() => false), newBudget, nextPoll]));

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
