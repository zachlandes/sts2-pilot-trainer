using System.Reflection;
using System.Runtime.Loader;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The barrier that makes a trainer run unable to persist anything.
///
/// What can be checked on a machine that is not running the game is exactly the part
/// that would otherwise rot silently: that every write the barrier names is a real
/// method on this build, and that with no trainer run live the barrier lets every
/// one of them through. A build that moved or renamed one would install a barrier
/// with a hole in it, and the hole would be a player's progress file rewritten from
/// a run they never played.
///
/// What it does not check is the game actually calling them, which needs the retail
/// client; docs/in-game-host.md records that boundary rather than papering over it.
/// </summary>
public sealed class ProfileWriteBarrierTests
{
    /// <summary>
    /// The mod assembly this test process already has beside it, rather than the one
    /// in the mod's own output directory.
    ///
    /// The same file, and deliberately the copy the runtime would bind to anyway:
    /// loading a second path into the default context is how a process ends up with
    /// two of something and then cannot say which one it read - the shape of trap
    /// docs/in-game-host.md records for the game assembly.
    /// </summary>
    private static string ModAssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "CombatTrainer.dll");

    /// <summary>
    /// Every write the barrier suppresses exists here, and its declaring type does
    /// too. Read off the barrier's own list rather than restated, so a write added to
    /// the list is covered by this the moment it is added.
    /// </summary>
    [BarrierFact]
    public void EveryWriteTheBarrierNamesExistsOnThisBuild()
    {
        var gameAssembly = GameAssembly();
        var named = SuppressedWrites();
        Assert.NotEmpty(named);

        foreach (var (typeName, methodName) in named)
        {
            var type = gameAssembly.GetType(typeName);
            Assert.True(type is not null, $"This build has no {typeName}.");

            var methods = type!
                .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == methodName)
                .ToList();
            Assert.True(methods.Count > 0, $"This build's {typeName} has no '{methodName}'.");

            // The barrier answers a write by returning nothing or by handing back a
            // completed task, and it has no third answer. A write that returned
            // anything else would need a value invented for its callers, which is
            // exactly the kind of plausible wrong answer this project refuses.
            foreach (var method in methods)
            {
                Assert.True(
                    method.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(method.ReturnType),
                    $"{typeName}.{methodName} returns {method.ReturnType.Name}, which the barrier cannot " +
                    "answer without inventing a value.");
            }
        }
    }

    /// <summary>
    /// The two writes that are not covered by starting a run with saving off, and
    /// that this barrier exists for: winning a fight rewrites the progress file, and
    /// an event room saves the run with progress saving defaulted on.
    /// </summary>
    [BarrierFact]
    public void ItCoversTheWritesThatRunManagerShouldSaveDoesNot()
    {
        var named = SuppressedWrites().ToHashSet();

        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveProgressFile"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "UpdateProgressAfterCombatWon"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveRun"), named);
    }

    /// <summary>
    /// The writes and mutations a trainer run leaves in a player's own profile, each
    /// found by measuring the profile directory before and after a retail session.
    ///
    /// Found in the retail client: the trainer's run marked its own starting relic
    /// seen while it was live, the barrier never saw it because nothing was written,
    /// and the game wrote that progress out itself on quit, with no trainer run live
    /// and by a path the barrier must not stop. State that will be written later is a
    /// write that has not happened yet, so it belongs on the same list.
    /// </summary>
    [BarrierFact]
    public void ItCoversTheProgressAMutationLeavesBehindForTheGameToWrite()
    {
        var named = SuppressedWrites().ToHashSet();

        Assert.Contains(("MegaCrit.Sts2.Core.Multiplayer.Replay.CombatReplayWriter", "WriteReplay"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkCardAsSeen"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkRelicAsSeen"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkPotionAsSeen"), named);
    }

    /// <summary>
    /// With no trainer run live the barrier does nothing at all. This is what keeps a
    /// player's own runs saving normally with the mod installed, and it is the reason
    /// the patches can be installed once at start rather than raised and lowered.
    /// </summary>
    [BarrierFact]
    public void WithNoTrainerRunLiveEveryWriteIsLetThrough()
    {
        var barrier = BarrierType();
        Assert.False((bool)barrier.GetProperty(
            "IsActive", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!);

        _ = GameAssembly();
        Assert.True((bool)barrier.GetMethod(
            "SkipVoidWrite", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!);

        object?[] taskWrite = [null];
        Assert.True((bool)barrier.GetMethod(
            "SkipTaskWrite", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, taskWrite)!);
        Assert.Null(taskWrite[0]);
    }

    /// <summary>
    /// Raised, it stops the write and hands a task-returning one a completed task -
    /// its callers await the result, and a null there would take the game down in
    /// place of the write it was preventing.
    /// </summary>
    [BarrierFact]
    public void RaisedItStopsTheWriteAndStillAnswersItsCallers()
    {
        var barrier = BarrierType();
        _ = GameAssembly();

        barrier.GetMethod("Raise", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        try
        {
            Assert.False((bool)barrier.GetMethod(
                "SkipVoidWrite", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!);

            object?[] taskWrite = [null];
            Assert.False((bool)barrier.GetMethod(
                "SkipTaskWrite", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, taskWrite)!);
            var result = Assert.IsAssignableFrom<Task>(taskWrite[0]);
            Assert.True(result.IsCompletedSuccessfully);
        }
        finally
        {
            barrier.GetMethod("Lower", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        }
    }

    [BarrierFact]
    public void EndingATrainerRunLowersTheBarrierForTheNextRun()
    {
        var barrier = BarrierType();
        barrier.GetMethod("Raise", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

        var recordedRun = BarrierType().Assembly.GetType("Sts2PilotTrainer.Mod.RecordedFightRun")!;
        var teardown = recordedRun.GetNestedType(
            "TrainerRunTeardown", BindingFlags.NonPublic)!;
        teardown.GetMethod("AfterRunEnds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

        Assert.False((bool)barrier.GetProperty(
            "IsActive", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!);
        Assert.Equal(
            "None",
            recordedRun.GetProperty("Phase", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!.ToString());
    }

    [BarrierFact]
    public void LeavingATrainerFightQueuesItsResultAndLowersTheBarrier()
    {
        var barrier = BarrierType();
        var recordedRun = barrier.Assembly.GetType("Sts2PilotTrainer.Mod.RecordedFightRun")!;
        var phase = recordedRun.GetProperty("Phase", BindingFlags.Static | BindingFlags.NonPublic)!;
        var teardown = recordedRun.GetNestedType("TrainerRunTeardown", BindingFlags.NonPublic)!;
        var pendingResult = recordedRun.GetField(
            "_resultAfterMainMenu", BindingFlags.Static | BindingFlags.NonPublic)!;

        barrier.GetMethod("Raise", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        phase.SetValue(null, Enum.Parse(phase.PropertyType, "InFight"));
        try
        {
            teardown.GetMethod("AfterRunEnds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

            var screen = Assert.IsType<FightResultScreen>(pendingResult.GetValue(null));
            Assert.Equal(TrainerCopy.LeftNote, screen.Notice);
            Assert.Equal(TrainerCopy.DoneButton, screen.DoneButton);
            Assert.False((bool)barrier.GetProperty(
                "IsActive", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!);
            Assert.Equal("None", phase.GetValue(null)!.ToString());
        }
        finally
        {
            pendingResult.SetValue(null, null);
            recordedRun.GetMethod("Finish", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        }
    }

    private static Type BarrierType()
    {
        var modAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "CombatTrainer")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);
        return modAssembly.GetType("Sts2PilotTrainer.Mod.ProfileWriteBarrier")!;
    }

    private static IReadOnlyList<(string Type, string Method)> SuppressedWrites()
    {
        var field = BarrierType().GetField(
            "SuppressedWrites", BindingFlags.Static | BindingFlags.NonPublic)!;
        var named = new List<(string, string)>();
        foreach (var entry in (System.Collections.IEnumerable)field.GetValue(null)!)
        {
            var type = entry.GetType();
            named.Add((
                (string)type.GetField("Item1")!.GetValue(entry)!,
                (string)type.GetField("Item2")!.GetValue(entry)!));
        }

        return named;
    }

    /// <summary>The game assembly, forced to be loaded first: the barrier is a claim
    /// about this build, and asking before the build is loaded asks about
    /// nothing.</summary>
    private static Assembly GameAssembly()
    {
        _ = Sts2PilotTrainer.Engine.EngineHost.StartupPhase();
        return AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2");
    }

    public sealed class BarrierFactAttribute : FactAttribute
    {
        public BarrierFactAttribute()
        {
            if (!Arbiter.GameAvailable || !File.Exists(ModAssemblyPath))
            {
                Skip = "Needs the prepared game and built Combat Trainer mod. Run ./scripts/build.sh.";
            }
        }
    }
}

/// <summary>
/// The game's own commands the in-game host drives, and the reason they are tested
/// by name.
///
/// Two of the recording's steps are screen commands rather than engine ones, and both
/// were found the hard way: the engine's map-coordinate entry is only the middle of
/// what a clicked node does, and an event screen's continue is not in the event
/// model's option list at all. A build that renamed either would leave the host
/// calling nothing and the journey stopping with a fight that never opens - which is
/// exactly what it looked like before, and took a retail cycle each time to see.
/// This is the check that turns that into a build failure.
/// </summary>
public sealed class GameScreenCommandTests
{
    [BarrierFact]
    public void TheMapScreenStillOwnsTheTravelCommandTheHostDrives()
    {
        var travel = GameType("MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen")
            .GetMethod("TravelToMapCoord", BindingFlags.Instance | BindingFlags.Public);

        Assert.True(travel is not null, "NMapScreen has no TravelToMapCoord on this build.");
        Assert.True(typeof(Task).IsAssignableFrom(travel!.ReturnType));
        Assert.Equal(
            ["MegaCrit.Sts2.Core.Map.MapCoord"],
            travel.GetParameters().Select(parameter => parameter.ParameterType.FullName));
    }

    [BarrierFact]
    public void TheEventRoomStillOwnsTheOptionClickTheHostDrives()
    {
        var clicked = GameType("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom")
            .GetMethod("OptionButtonClicked", BindingFlags.Instance | BindingFlags.Public);

        Assert.True(clicked is not null, "NEventRoom has no OptionButtonClicked on this build.");
        Assert.Equal(
            ["MegaCrit.Sts2.Core.Events.EventOption", "System.Int32"],
            clicked!.GetParameters().Select(parameter => parameter.ParameterType.FullName));
    }

    /// <summary>The flag that tells a screen's continue from a real choice. Without it
    /// the host cannot know when carrying on is all that is left, and it refuses
    /// rather than pick.</summary>
    [BarrierFact]
    public void AnEventOptionStillSaysWhetherItOnlyCarriesOn()
    {
        var isProceed = GameType("MegaCrit.Sts2.Core.Events.EventOption")
            .GetProperty("IsProceed", BindingFlags.Instance | BindingFlags.Public);

        Assert.True(isProceed is not null, "EventOption has no IsProceed on this build.");
        Assert.Equal(typeof(bool), isProceed!.PropertyType);
    }

    private static Type GameType(string name)
    {
        _ = Sts2PilotTrainer.Engine.EngineHost.StartupPhase();
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2")
            .GetType(name);
        Assert.True(type is not null, $"This build has no {name}.");
        return type!;
    }

    public sealed class BarrierFactAttribute : FactAttribute
    {
        public BarrierFactAttribute()
        {
            var mod = Path.Combine(AppContext.BaseDirectory, "CombatTrainer.dll");
            if (!Arbiter.GameAvailable || !File.Exists(mod))
            {
                Skip = "Needs the prepared game and built Combat Trainer mod. Run ./scripts/build.sh.";
            }
        }
    }
}
