namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The refusal a fight that never finished opening is stopped with.
///
/// A half-open fight was refused by the boundary, which compares card by card and
/// reported the recording's five-card hand against the one card dealt so far. That
/// sentence is true about the comparison and wrong about what happened, and a player
/// reading it concludes the recording is broken. This guards that the timeout's own
/// sentence says the fight did not open rather than that something did not match.
/// </summary>
public sealed class RefusalCopyTests
{
    [Fact]
    public void TheRefusalSaysTheFightDidNotOpenRatherThanThatSomethingDidNotMatch()
    {
        var refusal = TrainerCopy.FightDidNotOpen;

        Assert.StartsWith("The fight didn't finish opening", refusal, StringComparison.Ordinal);
        Assert.Contains(TrainerCopy.RefusalNoHarm, refusal, StringComparison.Ordinal);
    }
}
