using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The two things the retail client proved wrong about when the recorded journey acts,
/// both of which look correct in a process that never draws a frame.
///
/// The fight was handed over a fixed two seconds after the room opened, and the client
/// was still playing its Battle Start banner at two seconds: the boundary read one card
/// of the recording's five and refused a correct entry. And the refusal that followed
/// was put on screen before the return to the main menu, which frees what is in the
/// modal container - so the player was dropped at the menu with nothing said at all.
///
/// Neither is a drawing bug, so neither is caught by looking at a screenshot. What can
/// be checked here is the shape both fixes have: the wait asks whether the thing it is
/// waiting for has happened, and the refusal is explained on the far side of the
/// return rather than the near side.
/// </summary>
public sealed class RecordedFightRunTimingTests
{
    private static string ModAssemblyPath => Path.Combine(AppContext.BaseDirectory, "CombatTrainer.dll");

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
    /// The hand-over waits on the engine's own answer to "may the player act", rather
    /// than on a length of time. Read off the IL because the wait itself is one line
    /// in a state machine no game-free process can run.
    /// </summary>
    [TimingFact]
    public void TheHandOverWaitsOnTheEnginesOwnSignalThatTheFightIsOpen()
    {
        Assert.Contains(
            "WaitUntil",
            Calls(Run().GetMethod("HandOverWhenTheGameHasFinishedMoving", Static)!));

        var readsReadiness = Run()
            .GetMethods(Static)
            .Concat(Run().GetNestedTypes(Static | BindingFlags.Instance)
                .SelectMany(nested => nested.GetMethods(Static | BindingFlags.Instance)))
            .Any(method => Calls(method).Contains("get_IsReadyForThePlayer"));

        Assert.True(readsReadiness, "Nothing in the recorded run asks whether the fight has opened.");
    }

    /// <summary>
    /// A refusal is explained after the return to the main menu, never before it.
    /// Order, not presence: the old code called both, in the order that threw the
    /// explanation away.
    /// </summary>
    [TimingFact]
    public void ARefusalIsExplainedOnTheFarSideOfTheReturnToTheMenu()
    {
        var abandon = Run()
            .GetMethods(Static)
            .Single(method =>
                method.Name == "Abandon" &&
                method.GetParameters() is [{ ParameterType.Name: "String" }, { ParameterType.Name: "String" }]);

        var teardown = Calls(abandon);
        Assert.DoesNotContain("ShowRefusal", teardown);
        Assert.Contains("ExplainOnceTheMenuIsBack", teardown);

        var explain = Calls(Run().GetMethod("ExplainOnceTheMenuIsBack", Static)!);
        var returned = explain.IndexOf("ReturnToMainMenu");
        var explained = explain.IndexOf("ShowRefusal");
        Assert.True(returned >= 0, "The refusal never returns to the main menu.");
        Assert.True(explained >= 0, "The refusal is never put on screen.");
        Assert.True(
            returned < explained,
            "The refusal is shown before the return to the main menu, which frees it again.");
    }

    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static async Task<bool> Invoke(Func<bool> done, Task deadline, Func<Task> nextPoll) =>
        await Assert.IsAssignableFrom<Task<bool>>(
            Run().GetMethod("WaitUntil", Static)!.Invoke(null, [done, deadline, nextPoll]));

    private static Type Run() =>
        ModAssembly().GetType("Sts2PilotTrainer.Mod.RecordedFightRun")
        ?? throw new InvalidOperationException("The mod has no RecordedFightRun.");

    /// <summary>
    /// The methods a method calls, in the order its IL calls them, following the
    /// compiler's state machine when the method is asynchronous - which every method
    /// this file cares about is.
    /// </summary>
    private static List<string> Calls(MethodBase method)
    {
        // Reading a method body resolves the signatures it mentions, and the mod's
        // mention the game. Asked for without this, these tests pass in a full run -
        // where another test has already loaded `sts2` - and fail on their own.
        _ = Sts2PilotTrainer.Engine.EngineHost.StartupPhase();

        var body = Moved(method).GetMethodBody();
        if (body is null) return [];

        var il = body.GetILAsByteArray() ?? [];
        var module = Moved(method).Module;
        var called = new List<string>();
        for (var i = 0; i + 4 < il.Length; i++)
        {
            // call (0x28) and callvirt (0x6F), each followed by a four-byte token.
            if (il[i] != 0x28 && il[i] != 0x6F) continue;
            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                if (module.ResolveMethod(token) is { } resolved) called.Add(resolved.Name);
            }
            catch (Exception)
            {
                // Either the byte was an operand rather than an opcode - which a linear
                // scan cannot rule out - or the token names a game method the stubs
                // this test runs against do not carry. Skipping it can only lose a
                // call, never invent one, and every call these tests look for is the
                // mod's own.
            }
        }

        return called;
    }

    private static MethodBase Moved(MethodBase method) =>
        method.GetCustomAttribute<AsyncStateMachineAttribute>() is { } async
            ? async.StateMachineType.GetMethod(
                  "MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            : method;

    private static Assembly ModAssembly() =>
        AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "CombatTrainer")
        ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);

    public sealed class TimingFactAttribute : FactAttribute
    {
        public TimingFactAttribute()
        {
            if (!File.Exists(Path.Combine(Arbiter.RepoRoot, "build", "lib", "sts2.dll")) ||
                !File.Exists(ModAssemblyPath))
            {
                Skip = "Needs the prepared game and built Combat Trainer mod. Run ./scripts/build.sh.";
            }
        }
    }
}
