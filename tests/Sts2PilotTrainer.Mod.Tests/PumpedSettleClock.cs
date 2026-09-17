using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The headless attach's settle clock: the arbiter's drain in place of the scene tree.
///
/// The recorder and the fight observer both settle by polling until the engine is
/// quiet, on a clock the attach site supplies. In the retail client that clock is the
/// scene tree's timer, which spreads the wait across real frames and lets the engine
/// make progress between polls. A headless process has no frames, and the game's own
/// executor has already been drained to idle by the driver before a settle is pumped -
/// so here a poll parks its awaiter and <see cref="Drain"/> is what hands the process
/// back, run once after each action the way a frame loop would tick.
///
/// A poll that self-completed - <c>Task.Yield</c> or a completed task - would resume the
/// settle synchronously while the executor was still running the action that started it,
/// which is a wait that never lets the thing it is waiting for finish. So a poll suspends
/// on a source nothing but <see cref="Drain"/> completes, and the budget never completes
/// on its own: the engine is idle by pump time, so the settle reaches its idle condition
/// within a couple of polls and the budget is never a real timeout here. A genuine wedge
/// is bounded by the test session timeout instead.
/// </summary>
internal sealed class PumpedSettleClock : SettleClock
{
    private readonly object _gate = new();
    private readonly List<TaskCompletionSource> _polls = [];
    private static readonly Task NeverCompletes = new TaskCompletionSource().Task;

    internal override Task Budget() => NeverCompletes;

    internal override Task Poll()
    {
        var poll = new TaskCompletionSource();
        lock (_gate) _polls.Add(poll);
        return poll.Task;
    }

    /// <summary>
    /// Drives every parked settle to completion, the way a frame would: run whatever the
    /// inline context has queued, then complete every outstanding poll so its settle
    /// resumes, and repeat until nothing is waiting on a poll. Completing a poll resumes
    /// its settle inline, which either finishes or parks on a fresh poll the next pass
    /// picks up.
    /// </summary>
    internal void Drain()
    {
        var context = SynchronizationContext.Current as InlineSynchronizationContext;
        for (var guard = 0; guard < 1_000_000; guard++)
        {
            context?.DrainPending();

            List<TaskCompletionSource> waiting;
            lock (_gate)
            {
                waiting = [.. _polls];
                _polls.Clear();
            }

            if (waiting.Count == 0)
            {
                context?.DrainPending();
                lock (_gate)
                {
                    if (_polls.Count == 0) return;
                }

                continue;
            }

            foreach (var poll in waiting) poll.TrySetResult();
        }

        throw new InvalidOperationException(
            "A headless settle did not quiesce after a million pumps; the engine is wedged or a settle is " +
            "polling for a condition that never becomes true.");
    }

    /// <summary>
    /// One frame rather than every frame until quiet: runs what the inline context has
    /// queued, completes every poll outstanding at that moment once, and runs what
    /// resuming them queued. A settle that resumes and parks again is left parked, and
    /// that is what this is for - standing in for the retail client's frames going by
    /// while the engine is mid-way through a decision's own work, so a test can ask
    /// whether the settle read during them. Returns whether a settle is still waiting.
    /// </summary>
    internal bool Tick()
    {
        var context = SynchronizationContext.Current as InlineSynchronizationContext;
        context?.DrainPending();

        List<TaskCompletionSource> waiting;
        lock (_gate)
        {
            waiting = [.. _polls];
            _polls.Clear();
        }

        foreach (var poll in waiting) poll.TrySetResult();
        context?.DrainPending();

        lock (_gate) return _polls.Count > 0;
    }
}
