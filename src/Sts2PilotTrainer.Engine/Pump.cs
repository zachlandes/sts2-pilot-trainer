using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Runs the engine's queued work to completion.
///
/// The retail client drains its action queue a frame at a time. There are no frames
/// here, so after every player decision the queue is drained explicitly and the host
/// waits until the executor is idle. Draining fully before the next action is what
/// makes the replay's action ordering mean the same thing it means in the game.
/// </summary>
internal static class Pump
{
    /// <summary>
    /// How long to keep draining before declaring the engine wedged. Generous, because
    /// this is a correctness backstop and not a performance knob: a replay that takes
    /// an extra second is fine, a replay that reports success because it gave up on a
    /// half-finished turn is not.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    internal static void Drain()
    {
        var context = SynchronizationContext.Current as InlineSynchronizationContext;
        context?.DrainPending();

        var executor = RunManager.Instance.ActionExecutor;
        var deadline = DateTime.UtcNow + Budget;

        while (executor.IsRunning)
        {
            context?.DrainPending();
            if (!executor.IsRunning) break;

            if (DateTime.UtcNow > deadline)
            {
                throw new EngineException(
                    "The engine's action executor did not finish within 30s. The headless host is missing " +
                    "something the game was waiting on, and continuing would replay from a half-applied " +
                    "state. This is a host defect, not a manifest defect.");
            }

            Thread.Sleep(1);
        }

        context?.DrainPending();
    }
}

/// <summary>
/// Makes <c>Task.Yield()</c> complete immediately while enabled.
///
/// A yield hands control back to the scheduler and expects something to hand it
/// back. In a process with no frame loop, some of the engine's yields inside the
/// enemy turn never resume. Suppressing them for the duration of a turn boundary
/// makes the chain run straight through.
///
/// Scoped rather than global on purpose: a yield is also how the engine lets other
/// work interleave, and suppressing it everywhere would change more than it needs
/// to. It is enabled around end-turn and nowhere else.
/// </summary>
internal static class YieldSuppression
{
    private static bool _patched;
    private static volatile bool _active;

    internal static IDisposable Enable()
    {
        EnsurePatched();
        _active = true;
        return new Scope();
    }

    private static void EnsurePatched()
    {
        if (_patched) return;
        _patched = true;

        var awaiter = typeof(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter);
        var isCompleted = awaiter.GetProperty("IsCompleted")?.GetGetMethod();
        if (isCompleted is null) return;

        new Harmony("sts2-pilot-trainer.yield").Patch(
            isCompleted,
            prefix: new HarmonyMethod(typeof(YieldSuppression)
                .GetMethod(nameof(CompleteWhenActive), BindingFlags.NonPublic | BindingFlags.Static)!));
    }

    private static bool CompleteWhenActive(ref bool __result)
    {
        if (!_active) return true;
        __result = true;
        return false;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _active = false;
    }
}
