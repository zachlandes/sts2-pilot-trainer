namespace Sts2PilotTrainer.Mod;

/// <summary>
/// How a settle hands the process back to the game and how it waits out its budget.
///
/// The two settles in this mod - the recorder's between-decision settle and the fight
/// observer's after-action settle - are the same wait: they poll until the engine has
/// gone quiet, giving up after a budget of the engine's own time. In the retail client
/// that waiting is done on the scene tree's own timer, which is the only thing
/// <c>docs/in-game-host.md</c> records as working for waiting in that process. There is
/// no scene tree in a headless process, so the timer is named here rather than inside
/// the observer, and the attach site supplies the one that fits its host: the retail
/// client's is <see cref="SceneTree"/>; a headless attach supplies a clock driven by
/// the arbiter's own drain instead of by frames.
///
/// It carries no rule about <em>when</em> the engine is settled - that stays in
/// <see cref="RunRecorder.WaitForTheEngine"/> - only how to yield and how to time out.
/// </summary>
internal abstract class SettleClock
{
    /// <summary>A task that completes when the whole settle budget has elapsed, so a
    /// caller polling its completion learns the engine never went quiet.</summary>
    internal abstract System.Threading.Tasks.Task Budget();

    /// <summary>A task that completes after one poll interval, handing the process back
    /// in between so the engine can make progress.</summary>
    internal abstract System.Threading.Tasks.Task Poll();

    /// <summary>The retail client's clock: the scene tree's own timer, which is what a
    /// running game has to wait on and a headless process does not have.</summary>
    internal static SettleClock SceneTree { get; } = new SceneTreeClock();

    private sealed class SceneTreeClock : SettleClock
    {
        internal override System.Threading.Tasks.Task Budget() =>
            RecordedFightRun.LetTheGameRun(RunRecorder.SettleBudgetSeconds);

        internal override System.Threading.Tasks.Task Poll() =>
            RecordedFightRun.LetTheGameRun(RunRecorder.SettlePollSeconds);
    }
}
