namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// Whether Runmobile puts a row on the game's own main menu.
///
/// The rule is one expression and the whole product decision lives in it, so every
/// combination of its two inputs is asserted here rather than left to be read off the
/// expression. The one that matters most is the pair in the middle: a player's stored
/// choice outranks the run count in both directions, which is what stops finishing a
/// first run from silently taking away a row the player turned on.
/// </summary>
public sealed class MainMenuRowTests
{
    /// <summary>
    /// The reason the row exists. A player who has finished no run cannot reach the
    /// library any other way: the game sends them from Singleplayer to character select
    /// and hides its own Compendium, so the card and the run-history plate both hang off
    /// surfaces they never see.
    /// </summary>
    [Fact]
    public void APlayerWhoHasFinishedNoRunGetsTheRow()
    {
        Assert.True(MainMenuRow.ShownWhen(choice: null, hasFinishedARun: false));
    }

    /// <summary>
    /// And a player who has run history does not, because the Compendium card is there
    /// for them. A mod is not entitled to a permanent line on somebody's main menu.
    /// </summary>
    [Fact]
    public void APlayerWithRunHistoryDoesNot()
    {
        Assert.False(MainMenuRow.ShownWhen(choice: null, hasFinishedARun: true));
    }

    [Fact]
    public void AStoredChoiceOutranksTheRunCountInBothDirections()
    {
        Assert.True(MainMenuRow.ShownWhen(choice: true, hasFinishedARun: true));
        Assert.False(MainMenuRow.ShownWhen(choice: false, hasFinishedARun: false));
        Assert.True(MainMenuRow.ShownWhen(choice: true, hasFinishedARun: false));
        Assert.False(MainMenuRow.ShownWhen(choice: false, hasFinishedARun: true));
    }

    /// <summary>The row and the card carry the same word, because they are two ways to
    /// one library rather than two things.</summary>
    [Fact]
    public void TheRowIsNamedForTheMod()
    {
        Assert.Equal(LibraryCopy.CompendiumCard, LibraryCopy.MainMenuRow);
    }
}
