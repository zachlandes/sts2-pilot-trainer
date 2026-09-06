namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The verbs format v6 added, measured against the real engine.
///
/// No committed history passes through any of them, and the fixtures that would need
/// a run holding Scroll Boxes, Pael's Wing or a second-act event, which no generated
/// journey reaches. So each is exercised where it lives: <c>./scripts/arbiter
/// verb-probe</c> starts a run, asks the prompt the way the engine asks it, and checks
/// the driver's own selector answers from a queued decision and refuses with its own
/// sentence where the decision is wrong or absent. Every sentence asserted is the
/// driver's or the selector's, so a sentence that changes fails here rather than
/// passing a test that quotes something else.
/// </summary>
public sealed class ResidueVerbTests
{
    /// <summary>
    /// The turn ends through the same action the end-turn button enqueues, and the
    /// undo is refused by measurement: the client offers it only while another player
    /// has not ended their turn, so no singleplayer run reaches the window.
    /// </summary>
    [GameFact]
    public void AnEndedTurnGoesThroughTheActionAndTheUndoIsRefusedOnThisBuild()
    {
        var result = Arbiter.Run("verb-probe", "undo-end-turn");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pass  end-turn-through-the-action", result.All, StringComparison.Ordinal);
        Assert.Contains("took the fight from turn 1 to turn 2", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  undo-refused-on-this-build", result.All, StringComparison.Ordinal);
        Assert.Contains("no singleplayer run on v0.111.0 can", result.All, StringComparison.Ordinal);
    }

    /// <summary>Obtaining Scroll Boxes asks which bundle; the stand-in hands the
    /// question to the selector, and the engine adds the chosen bundle's cards.</summary>
    [GameFact]
    public void ABundleScreenIsAnsweredFromTheManifestAndRefusedWhereItIsWrong()
    {
        var result = Arbiter.Run("verb-probe", "bundle");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pass  bundle-prompt-answered-through-the-stand-in", result.All, StringComparison.Ordinal);
        Assert.Contains("the deck grew by 3 for a bundle of 3", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  bundle-index-past-the-screen", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  bundle-ids-differ", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  bundle-nobody-recorded", result.All, StringComparison.Ordinal);
    }

    /// <summary>The relic screen answers through the same stand-in, and a history
    /// that records one on this build meets the no-caller sentence.</summary>
    [GameFact]
    public void ARelicScreenIsAnsweredFromTheManifestAndNoActionOnThisBuildOpensOne()
    {
        var result = Arbiter.Run("verb-probe", "relic-screen");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pass  relic-prompt-answered-through-the-stand-in", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  relic-index-past-the-screen", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  relic-nobody-recorded", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  relic-answer-no-action-opened", result.All, StringComparison.Ordinal);
        Assert.Contains("No caller reaches RelicSelectCmd.FromChooseARelicScreen on v0.111.0", result.All, StringComparison.Ordinal);
    }

    /// <summary>A card reward answered past its cards comes back through the same
    /// seam a card does, named by the alternative's own id.</summary>
    [GameFact]
    public void ACardRewardsAlternativeIsAnsweredThroughTheSeamAndCheckedByIdAndPosition()
    {
        var result = Arbiter.Run("verb-probe", "card-reward-alternative");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pass  alternative-answered-through-the-seam", result.All, StringComparison.Ordinal);
        Assert.Contains("answered with alternative SACRIFICE and card none", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  alternative-this-reward-does-not-offer", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  alternative-at-the-wrong-position", result.All, StringComparison.Ordinal);
    }

    /// <summary>The Crystal Sphere's screen is stood in for, its reveals go through
    /// the minigame's own members with the recorded tool, and the minigame completes
    /// on its last divination.</summary>
    [GameFact]
    public void ACrystalSphereIsRevealedFromTheManifestThroughTheStoodInScreen()
    {
        var result = Arbiter.Run("verb-probe", "crystal-sphere");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pass  reveal-with-no-sphere-open", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  screen-stood-in-for", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  small-reveal-clears-one-cell", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  reveal-of-a-revealed-cell", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  reveal-outside-the-grid", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  reveal-with-a-tool-the-minigame-has-not-got", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  big-reveal-clears-the-neighbourhood", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  last-divination-finishes-the-minigame", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  reveal-after-the-last-divination", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  loot-decided-completes-the-engines-task", result.All, StringComparison.Ordinal);
        Assert.Contains("pass  reveal-after-the-minigame-completed", result.All, StringComparison.Ordinal);
    }
}
