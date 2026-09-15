using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The state a run ended in, read where the game ends it.
///
/// The game ends a run inside a decision: the Architect's PROCEED calls
/// <c>RunManager.WinRun</c>, which calls <c>OnEnded(true)</c> with the run's final
/// state and then <c>GuaranteeKillAllPlayers</c>, which kills the player creature so
/// the client can draw the ending. The recorder reads the decision the run ended on at
/// <c>OnEnded</c>, before that kill, because that is the last reading of the run as it
/// was played; a replay that sampled the same decision once the driver's call returned
/// would read a dead player, a lost fight and a game over, and refuse the recording it
/// was made from. So the headless host reads the same instant through a postfix on the
/// same member, and <see cref="Arbiter"/> takes that reading as the after-state of the
/// action the run ended on and as the run's final state. A hook that observes and
/// changes nothing; <c>docs/headless-fidelity.md</c> owns it beside the others.
/// </summary>
public static class RunEnding
{
    /// <summary>The projection taken at the first <c>OnEnded</c> of the run in
    /// progress, or null while it is still being played. The first, because the kill
    /// that follows a win reaches <c>OnEnded</c> a second time with the player dead.</summary>
    internal static CanonicalState? Reading { get; private set; }

    /// <summary>Whether the game ended the run as a victory, once it has.</summary>
    internal static bool? Victory { get; private set; }

    /// <summary>Called from the <c>RunManager.OnEnded</c> postfix.</summary>
    internal static void Observe(bool isVictory)
    {
        if (Reading is not null) return;
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state is null) return;

        Reading = CanonicalStateProjection.Project(state);
        Victory = isVictory;
    }

    /// <summary>Called from the <c>RunManager.CleanUp</c> postfix: the next run starts
    /// with no ending.</summary>
    internal static void Forget()
    {
        Reading = null;
        Victory = null;
    }
}
