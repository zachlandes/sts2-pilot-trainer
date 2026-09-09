using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>The refusal a watcher receives while the game's card screen is still opening.</summary>
public sealed class RecordedCardScreenTests
{
    /// <summary>
    /// The retail re-proof reached this wait and exposed the old sentence as confusing.
    /// Exercise the reader itself with no screen up so the text is held at the same
    /// boundary that puts it in front of the player.
    /// </summary>
    [GameFact]
    public void ACardScreenThatHasNotOpenedYetIsSaidPlainly()
    {
        _ = EngineHost.StartupPhase();

        var refusal = Assert.Throws<RevealNotReadyException>(
            () => RecordedCardScreen.Find("CARD.STRIKE_IRONCLAD", 0));

        Assert.Equal(
            "The card screen for this recording's last decision hasn't opened yet.",
            refusal.Message);
    }
}
