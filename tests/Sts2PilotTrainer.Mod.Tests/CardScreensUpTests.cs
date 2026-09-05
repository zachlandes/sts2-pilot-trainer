using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// How many card screens are up in front of the player.
///
/// The shell's, not either feature's: the recorder's settle reads it to keep a reading
/// off a decision somebody has not finished making, and the Combat Trainer's reads it
/// so that a prompt a played card opens does not spend the engine's budget. Behind the
/// recorder's own patches it stopped counting on a build the recorder declines to
/// watch - which is exactly the build the trainer is meant to carry on through.
/// </summary>
public sealed class CardScreensUpTests
{
    public CardScreensUpTests()
    {
        // The screens are the game's types, so this class's own patch classes need the
        // game assembly resolvable. This project does not copy it, and it reaches the
        // default context only once something has started the host - left to whichever
        // test ran first, these fail on a cold ordering.
        _ = EngineHost.StartupPhase();
    }

    /// <summary>
    /// A card screen gives back the count it took, however its own task ends.
    ///
    /// The count is what keeps a settle from reading a run in the middle of a decision,
    /// and it is static because the screens are: one game, one person looking at it. So
    /// a screen counted on the way up and not on the way down leaves it above zero for
    /// the rest of the session, and the next run's settle waits for a screen nobody is
    /// looking at. It used to be taken in a Harmony prefix guarded on a recorder being
    /// active and given back in a postfix guarded the same way, which drifts exactly
    /// when a run is torn down between them; it is one try/finally around the screen's
    /// own task now, so the two cannot decide differently.
    ///
    /// Driven through that wrapper rather than asserted about it, because what has to
    /// hold is the count at the end and not the shape of the code that keeps it.
    /// </summary>
    [Fact]
    public async Task ACardScreenGivesBackTheCountItTookHoweverItEnds()
    {
        var before = CardScreensUp.Count;

        var answered = new TaskCompletionSource<int?>();
        var watching = CardScreensUp.Reward.Observe(answered.Task);
        Assert.Equal(before + 1, CardScreensUp.Count);

        answered.SetResult(1);
        await watching;
        Assert.Equal(before, CardScreensUp.Count);

        // A screen whose task faults is still a screen that has come down. This is the
        // reward screen's own tear-down: NCardRewardSelectionScreen._ExitTree faults its
        // completion source when the run goes away under it.
        var faulted = new TaskCompletionSource<int?>();
        var stranded = CardScreensUp.Reward.Observe(faulted.Task);
        Assert.Equal(before + 1, CardScreensUp.Count);

        faulted.SetException(new InvalidOperationException("the run went away under the screen"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stranded);
        Assert.Equal(before, CardScreensUp.Count);

        // And so is one that is cancelled, which is what the grid screen's own
        // _ExitTree does. Between them these are every way a screen ends, which is what
        // says the count cannot go stale and why nothing resets it.
        var cancelled = new TaskCompletionSource<int?>();
        var dropped = CardScreensUp.WhileOneIsUp(cancelled.Task);
        Assert.Equal(before + 1, CardScreensUp.Count);

        cancelled.SetCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dropped);
        Assert.Equal(before, CardScreensUp.Count);
    }

    /// <summary>
    /// The count never dips below where it started, however the screens interleave.
    ///
    /// It cannot: <c>WhileOnScreen</c> is the only thing that touches the counter and it
    /// takes and gives back in one try/finally, so there is no bare decrement for a
    /// caller to reach. That is the point of the shape - a reset and a stray decrement
    /// once left it at -1, and a negative count silently satisfies the wait that keeps a
    /// reading off a half-made decision.
    /// </summary>
    [Fact]
    public async Task TheScreenCountNeverGoesBelowWhereItStarted()
    {
        var before = CardScreensUp.Count;
        var floor = before;

        var first = new TaskCompletionSource<int?>();
        var second = new TaskCompletionSource<int?>();
        var outer = CardScreensUp.WhileOneIsUp(first.Task);
        var inner = CardScreensUp.WhileOneIsUp(second.Task);
        floor = Math.Min(floor, CardScreensUp.Count);

        second.SetResult(0);
        await inner;
        floor = Math.Min(floor, CardScreensUp.Count);

        first.SetResult(1);
        await outer;
        floor = Math.Min(floor, CardScreensUp.Count);

        Assert.Equal(before, floor);
        Assert.Equal(before, CardScreensUp.Count);
    }
}
