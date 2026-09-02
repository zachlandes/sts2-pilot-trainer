using System.Reflection;
using System.Runtime.Loader;

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
