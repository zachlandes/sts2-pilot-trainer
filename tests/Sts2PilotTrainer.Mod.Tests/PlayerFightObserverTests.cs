using System.Reflection;
using System.Runtime.Loader;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What can be checked about the in-game capture on a machine that is not running
/// the game: that the mod ships the recording's fight and refuses one that is not
/// this recording's, and that every game surface the observer subscribes to or reads
/// is still there on this build. A build that renamed one would install an observer
/// with a hole in it, and the hole would be a fight sampled on one side only, which
/// the capture refuses - but refusing every fight is not a product.
/// </summary>
public sealed class PlayerFightObserverTests
{
    private static string ModAssemblyPath => Path.Combine(AppContext.BaseDirectory, "Runmobile.dll");

    [ObserverFact]
    public void TheModShipsTheRecordingsFightBoundToTheRecording()
    {
        var shipped = ModAssembly().GetType("Sts2PilotTrainer.Mod.ShippedRecording")!;
        var recording = shipped.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var fight = shipped.GetMethod("ReadFight", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [recording]);

        Assert.NotNull(fight);
        Assert.Equal("navegreed-OJ-6QXhNgdg", fight!.GetType().GetProperty("RunId")!.GetValue(fight));
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

        var neverEmpty = new TaskCompletionSource<bool>();
        var deadline = new TaskCompletionSource<bool>();
        var observer = ModAssembly().GetType("Sts2PilotTrainer.Mod.PlayerFightObserver")!;
        var wait = Assert.IsAssignableFrom<Task<bool>>(observer.GetMethod(
            "WaitUntilSettled", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [
                capture,
                neverEmpty.Task,
                deadline.Task,
                (Func<bool>)(() => false),
                (Func<bool>)(() => false),
                (Func<Task>)(() => throw new InvalidOperationException("The first queue wait never settled.")),
            ]));

        deadline.SetResult(true);
        Assert.False(await wait);
        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("did not settle within 30 seconds", capture.Refusal, StringComparison.Ordinal);
        Assert.False(capture.HasOpenStep);

        capture.CompleteStep(new Dictionary<string, string> { ["combat.outcome"] = "victory" });
        Assert.Single(capture.Trace.Steps);
        Assert.Throws<ManifestException>(capture.Project);
    }

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
