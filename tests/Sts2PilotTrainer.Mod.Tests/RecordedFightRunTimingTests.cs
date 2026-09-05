using System.Reflection;
using System.Runtime.Loader;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// When the recorded journey hands the fight over, which the retail client proved
/// wrong about in a way a process that never draws a frame cannot see.
///
/// The fight was handed over a fixed two seconds after the room opened, and the client
/// was still playing its Battle Start banner at two seconds: the boundary read one card
/// of the recording's five and refused a correct entry. The wait that replaced the two
/// seconds is what these tests run - it asks whether the fight has opened, polls while
/// it has not, and gives up at its deadline.
/// </summary>
public sealed class RecordedFightRunTimingTests
{
    private static string ModAssemblyPath => Path.Combine(AppContext.BaseDirectory, "Runmobile.dll");

    [TimingFact]
    public async Task AFightThatIsStillOpeningIsWaitedForRatherThanRead()
    {
        var polls = 0;
        var opened = false;
        var never = new TaskCompletionSource<bool>();

        var waited = await Invoke(
            (Func<bool>)(() => opened),
            never.Task,
            (Func<Task>)(() =>
            {
                if (++polls == 3) opened = true;
                return Task.CompletedTask;
            }));

        Assert.True(waited);
        Assert.Equal(3, polls);
    }

    [TimingFact]
    public async Task AFightThatIsAlreadyOpenIsHandedOverWithoutWaiting()
    {
        var polls = 0;
        var never = new TaskCompletionSource<bool>();

        var waited = await Invoke(
            (Func<bool>)(() => true),
            never.Task,
            (Func<Task>)(() => { polls++; return Task.CompletedTask; }));

        Assert.True(waited);
        Assert.Equal(0, polls);
    }

    [TimingFact]
    public async Task AFightThatNeverOpensIsGivenUpOnAtTheDeadline()
    {
        var deadline = new TaskCompletionSource<bool>();
        deadline.SetResult(true);

        var waited = await Invoke(
            (Func<bool>)(() => false),
            deadline.Task,
            (Func<Task>)(() => throw new InvalidOperationException("The deadline had already passed.")));

        Assert.False(waited);
    }

    /// <summary>
    /// The other ordering the retail client proved wrong, and the one that left a
    /// player at the main menu with no account of what had happened at all.
    ///
    /// A refusal is a popup in the game's own modal container, and returning to the
    /// main menu frees what is in that container - so a refusal put up first was
    /// added, freed with the run it was explaining, and left the client's own deferred
    /// focus grab throwing on a disposed button. Neither the return nor the popup can
    /// be made in a process with no client; which of them happens first can be, and
    /// that is the whole of the fix.
    ///
    /// The limit, stated: this proves the seam's order, not that the refusal still
    /// goes through the seam. Re-inverting the two lines inside it fails this test;
    /// calling the popup directly from the teardown would not. That wiring is the
    /// client's to prove, and it is in the re-proof.
    /// </summary>
    [TimingFact]
    public async Task ARefusalIsExplainedOnlyOnceTheReturnToTheMenuHasFinished()
    {
        var explained = 0;
        var returning = new TaskCompletionSource();

        var explaining = Explain(returning.Task, () => explained++);

        Assert.Equal(0, explained);
        Assert.False(explaining.IsCompleted);

        returning.SetResult();
        await explaining;

        Assert.Equal(1, explained);
    }

    private static Task Explain(Task returned, Action explain) =>
        Assert.IsAssignableFrom<Task>(
            Run().GetMethod("ExplainOnceTheMenuIsBack", Static, [typeof(Task), typeof(Action)])!
                .Invoke(null, [returned, explain]));

    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static async Task<bool> Invoke(Func<bool> done, Task deadline, Func<Task> nextPoll) =>
        await Assert.IsAssignableFrom<Task<bool>>(
            Run().GetMethod("WaitUntil", Static)!.Invoke(null, [done, deadline, nextPoll]));

    private static Type Run() =>
        ModAssembly().GetType("Sts2PilotTrainer.Mod.RecordedFightRun")
        ?? throw new InvalidOperationException("The mod has no RecordedFightRun.");

    private static Assembly ModAssembly() =>
        AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "Runmobile")
        ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);

    public sealed class TimingFactAttribute : FactAttribute
    {
        public TimingFactAttribute()
        {
            if (!File.Exists(Path.Combine(Arbiter.RepoRoot, "build", "lib", "sts2.dll")) ||
                !File.Exists(ModAssemblyPath))
            {
                Skip = "Needs the prepared game and built Runmobile mod. Run ./scripts/build.sh.";
            }
        }
    }
}
