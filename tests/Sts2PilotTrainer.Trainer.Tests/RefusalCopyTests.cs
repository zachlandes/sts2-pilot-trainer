namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The one refusal whose value is in what it carries rather than in how it reads.
///
/// A fight the retail client never finished opening was refused by the boundary,
/// which compares card by card and reported the recording's five-card hand against
/// the one card dealt so far. That sentence is true about the comparison and wrong
/// about what happened, and a player reading it concludes the recording is broken.
/// The timeout's own refusal replaces it, and the reading that tells the two apart -
/// the player's combat state - has to survive into what is shown, not stop at the log.
/// </summary>
public sealed class RefusalCopyTests
{
    private const string Readiness =
        "room=Monster, combat manager=in progress, player combat state=Start, turn=1";

    [Fact]
    public void TheStateTheWaitGaveUpOnIsCarriedIntoTheRefusal()
    {
        var refusal = TrainerCopy.FightDidNotOpen(Readiness);

        Assert.Contains(Readiness, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalSaysTheFightDidNotOpenRatherThanThatSomethingDidNotMatch()
    {
        var refusal = TrainerCopy.FightDidNotOpen(Readiness);

        Assert.StartsWith("The fight didn't finish opening", refusal, StringComparison.Ordinal);
        Assert.Contains(TrainerCopy.RefusalNoHarm, refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The template rule, checked on the one sentence that is built from a live
    /// reading: nothing in it may name the recording it happened to be entering.
    /// </summary>
    [Fact]
    public void TheRefusalNamesNoRecording()
    {
        var refusal = TrainerCopy.FightDidNotOpen(Readiness);

        Assert.DoesNotContain("NaveGreed", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sludge", refusal, StringComparison.OrdinalIgnoreCase);
    }
}
