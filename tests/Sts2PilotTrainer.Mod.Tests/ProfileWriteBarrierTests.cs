using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using Sts2PilotTrainer.Mod;
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
        Path.Combine(AppContext.BaseDirectory, "Runmobile.dll");

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
    /// The tutorial marks, which write the progress file without going through
    /// <c>SaveProgressFile</c>. Named separately from the list above because that is
    /// the whole point of them: the barrier already covered the method they look like
    /// they go through, and covering it did nothing for these.
    /// </summary>
    [BarrierFact]
    public void ItCoversTheTutorialMarksThatWriteWithoutSaveProgressFile()
    {
        var named = SuppressedWrites().ToHashSet();

        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkFtueAsComplete"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "SetFtuesEnabled"), named);
        Assert.Contains(("MegaCrit.Sts2.Core.Saves.SaveManager", "ResetFtues"), named);
    }

    /// <summary>
    /// The case that was never run, and so the case this escaped through: a profile
    /// with tutorials on.
    ///
    /// <c>ProgressSaveManager.SeenFtue</c> returns true whenever
    /// <c>Progress.EnableFtues</c> is false, so on a profile with tutorials off no
    /// call site ever reaches the mark - and every profile this was measured on had
    /// them off. A player who left them on, which is the default, walks the trainer's
    /// own path through <c>map_select_ftue</c> and <c>can_play_cards_ftue</c> and has
    /// their progress file rewritten from somebody else's run.
    ///
    /// The mark is driven for real here, on the game's own <c>SaveManager</c> with
    /// tutorials explicitly on. What is asserted is the progress the game holds in
    /// memory, because that is what this process can honestly observe: the file write
    /// underneath it goes through Godot's <c>FileAccess</c>, which the headless stubs
    /// answer with a null handle, so a "no file appeared" assertion here would pass
    /// whatever the barrier did. That the mark writes at all is read off the game's
    /// own code - <c>ProgressSaveManager.MarkFtueAsComplete</c> calls
    /// <c>SaveProgress</c>, which calls <c>ISaveStore.WriteFile</c> without going
    /// through the patched <c>SaveProgressFile</c> - and proving it on disk needs the
    /// retail client on a profile with tutorials still on.
    ///
    /// The lowered half is not decoration: it is what says the call reached the mark
    /// at all, and it is the assertion the raised half would fail without the fix.
    /// </summary>
    [BarrierFact]
    public void WithTutorialsOnATrainerRunMarksNoTutorialComplete()
    {
        const string ftueId = "map_select_ftue";
        _ = GameAssembly();
        var harmony = new Harmony($"sts2-pilot-trainer.barrier-test.{Guid.NewGuid():N}");

        try
        {
            // Reflection rather than a compile-time reference to the game, the way
            // every other test here reaches it: a second resolved copy of sts2 is the
            // trap docs/in-game-host.md records, and GameAssembly() above is the one
            // this process has.
            var saveManagerType = GameType("MegaCrit.Sts2.Core.Saves.SaveManager");
            var saveManager = saveManagerType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            Invoke(saveManager, "InitProfileId", (int?)0);

            // A default progress rather than one loaded off disk. InitProgressData
            // reads a character out of the model database, which only a started engine
            // has, and starting one here would leave this process holding a headless
            // host - which is a different refusal from the one ModHostBoundaryTests
            // asserts on. This is the object the mark mutates, which is what is under
            // test.
            var progressProperty = saveManagerType
                .GetProperty("Progress", BindingFlags.Public | BindingFlags.Instance)!;
            progressProperty.SetValue(
                saveManager, Activator.CreateInstance(GameType("MegaCrit.Sts2.Core.Saves.ProgressState")));
            var progress = progressProperty.GetValue(saveManager)!;
            var ftueCompleted = progress.GetType()
                .GetProperty("FtueCompleted", BindingFlags.Public | BindingFlags.Instance)!;
            IEnumerable<string> Completed() => (IEnumerable<string>)ftueCompleted.GetValue(progress)!;

            // Tutorials on, which is what a normal player has and what no fixture had.
            progress.GetType()
                .GetProperty("EnableFtues", BindingFlags.Public | BindingFlags.Instance)!
                .SetValue(progress, true);
            Assert.DoesNotContain(ftueId, Completed());

            ProfileWriteBarrier.Install(harmony);

            ProfileWriteBarrier.Raise();
            Invoke(saveManager, "MarkFtueAsComplete", ftueId);
            Assert.DoesNotContain(ftueId, Completed());

            // Lowered, the same call marks it: the player's own tutorials still work
            // with the mod installed, and this is what says the raised half above
            // suppressed something rather than never arriving.
            ProfileWriteBarrier.Lower();
            Invoke(saveManager, "MarkFtueAsComplete", ftueId);
            Assert.Contains(ftueId, Completed());
        }
        finally
        {
            ProfileWriteBarrier.Lower();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>A type from the game assembly this process loaded, by name.</summary>
    private static Type GameType(string name) =>
        GameAssembly().GetType(name)
            ?? throw new InvalidOperationException($"This build has no {name}.");

    /// <summary>A method on the game's own object, by name. Every parameter here is
    /// the type the game declares, so an overload set of one needs no
    /// disambiguation.</summary>
    private static void Invoke(object target, string method, params object?[] arguments) =>
        target.GetType()
            .GetMethod(method, BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(target, arguments);

    [BarrierFact]
    public void ItInstallsTheBoundariesFoundByTheRetailProof()
    {
        var harmony = new Harmony($"sts2-pilot-trainer.barrier-test.{Guid.NewGuid():N}");
        var gameAssembly = GameAssembly();
        var boundaries = new (string Type, string Method)[]
        {
            ("MegaCrit.Sts2.Core.Multiplayer.Replay.CombatReplayWriter", "WriteReplay"),
            ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkCardAsSeen"),
            ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkRelicAsSeen"),
            ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkPotionAsSeen"),
        };

        try
        {
            ProfileWriteBarrier.Install(harmony);

            foreach (var (typeName, methodName) in boundaries)
            {
                var methods = gameAssembly.GetType(typeName)!
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == methodName);

                Assert.All(methods, method => Assert.Contains(
                    Harmony.GetPatchInfo(method)!.Prefixes,
                    patch => patch.owner == harmony.Id));
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public async Task RaisedItSuppressesInstalledBoundaries()
    {
        var harmony = new Harmony($"sts2-pilot-trainer.barrier-test.{Guid.NewGuid():N}");
        var boundaryType = typeof(WriteBoundary);
        var boundaries = new (string Type, string Method)[]
        {
            (boundaryType.FullName!, nameof(WriteBoundary.VoidWrite)),
            (boundaryType.FullName!, nameof(WriteBoundary.TaskWrite)),
        };

        try
        {
            Assert.Equal(2, ProfileWriteBarrier.Install(harmony, boundaryType.Assembly, boundaries));

            WriteBoundary.VoidWrite();
            await WriteBoundary.TaskWrite();
            Assert.Equal(2, WriteBoundary.Calls);

            WriteBoundary.Calls = 0;
            ProfileWriteBarrier.Raise();
            WriteBoundary.VoidWrite();
            await WriteBoundary.TaskWrite();

            Assert.Equal(0, WriteBoundary.Calls);
        }
        finally
        {
            ProfileWriteBarrier.Lower();
            harmony.UnpatchAll(harmony.Id);
            WriteBoundary.Calls = 0;
        }
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

        // Set through the field the phase is held in rather than through the property.
        // The phase enum lives in the Trainer assembly and a static field of a sibling
        // assembly's value type takes the whole mod down at load, so the mod holds it
        // as a number and reads it back as a cast. See RecordedFightRun._phase.
        var phaseField = recordedRun.GetField("_phase", BindingFlags.Static | BindingFlags.NonPublic)!;
        var teardown = recordedRun.GetNestedType("TrainerRunTeardown", BindingFlags.NonPublic)!;
        var pendingResult = recordedRun.GetField(
            "_resultAfterMainMenu", BindingFlags.Static | BindingFlags.NonPublic)!;

        barrier.GetMethod("Raise", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        phaseField.SetValue(null, (int)Enum.Parse(phase.PropertyType, "InFight"));
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

    private static class WriteBoundary
    {
        internal static int Calls { get; set; }

        public static void VoidWrite() => Calls++;

        public static Task TaskWrite()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static Type BarrierType()
    {
        var modAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "Runmobile")
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
                Skip = "Needs the prepared game and built Runmobile mod. Run ./scripts/build.sh.";
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
            var mod = Path.Combine(AppContext.BaseDirectory, "Runmobile.dll");
            if (!Arbiter.GameAvailable || !File.Exists(mod))
            {
                Skip = "Needs the prepared game and built Runmobile mod. Run ./scripts/build.sh.";
            }
        }
    }
}
