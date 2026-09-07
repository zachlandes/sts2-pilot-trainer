namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The choice a player is offered once their fight has ended, produced from data
/// rather than written down. Every label is the approved sentence, character for
/// character, and every presence rule is asserted per outcome and per capability.
///
/// The rule the rows carry: the mod never volunteers the recording's answer, and never
/// withholds it from somebody who asks. So the reveal is a row, the row is always
/// there, and what the sitting remembers is a dot on it.
/// </summary>
public sealed class PostFightChoiceTests
{
    [Fact]
    public void OnAWinThisBuildOffersLookThenActThenLeave()
    {
        var choice = PostFightChoice.For("NaveGreed", Facts(won: true));

        Assert.Equal(
            [PostFightAction.ShowTheComparison, PostFightAction.FightItAgain, PostFightAction.Leave],
            choice.Rows.Select(row => row.Action));
        Assert.Equal(["Show the comparison", "Fight it again", "Leave"], choice.Menu.Select(row => row.Label));
        Assert.All(choice.Menu, row => Assert.True(row.Enabled));
    }

    /// <summary>The same rows on a loss. A lost line compares, so the reveal is
    /// offered there exactly as on a win.</summary>
    [Fact]
    public void OnALossTheSameThreeRowsAreOffered()
    {
        var choice = PostFightChoice.For("NaveGreed", Facts(won: false));

        Assert.Equal(
            [PostFightAction.ShowTheComparison, PostFightAction.FightItAgain, PostFightAction.Leave],
            choice.Rows.Select(row => row.Action));
    }

    /// <summary>
    /// The glyph rule extends by one shape. Show the comparison is the hollow eye
    /// because it only looks; fight it again is the filled arrow because it moves the
    /// run; leaving is the game's own back language and carries no glyph.
    /// </summary>
    [Fact]
    public void FilledShapesMoveTheRunAndTheRevealIsHollow()
    {
        var choice = PostFightChoice.For("NaveGreed", Facts(won: true, canWatch: true, canContinueAsYou: true));

        Assert.Equal(TransportGlyph.Reveal, choice.Rows[0].Row.Glyph);
        Assert.Equal(TransportGlyph.Play, choice.Rows[1].Row.Glyph);
        Assert.Equal(TransportGlyph.Again, choice.Rows[2].Row.Glyph);
        Assert.Equal(TransportGlyph.Continue, choice.Rows[3].Row.Glyph);
        Assert.Null(choice.Rows[4].Row.Glyph);
    }

    /// <summary>Rows are present or absent, never disabled: a row for something the
    /// build cannot do would say less than nothing.</summary>
    [Fact]
    public void WatchIsPresentOnlyWhereTheBuildCanRunTheRecordingsFight()
    {
        var without = PostFightChoice.For("NaveGreed", Facts(won: true));
        var with = PostFightChoice.For("NaveGreed", Facts(won: true, canWatch: true));

        Assert.DoesNotContain(without.Rows, row => row.Action == PostFightAction.WatchTheirFight);
        Assert.Equal("Watch NaveGreed's fight", with.Rows[1].Row.Label);
        Assert.Equal(PostFightAction.WatchTheirFight, with.ActionAt(1));
    }

    /// <summary>Continue needs a run to go on with, so it is absent on a loss however
    /// capable the build.</summary>
    [Fact]
    public void ContinueAsYouIsPresentOnlyOnAWinAndOnlyWhereTheBuildCanContinue()
    {
        Assert.DoesNotContain(
            PostFightChoice.For("NaveGreed", Facts(won: true)).Rows,
            row => row.Action == PostFightAction.ContinueAsYou);
        Assert.DoesNotContain(
            PostFightChoice.For("NaveGreed", Facts(won: false, canContinueAsYou: true)).Rows,
            row => row.Action == PostFightAction.ContinueAsYou);

        var offered = PostFightChoice.For("NaveGreed", Facts(won: true, canContinueAsYou: true));
        Assert.Equal("Continue", offered.Rows[^2].Row.Label);
        Assert.Equal("Leave", offered.Rows[^1].Row.Label);
    }

    /// <summary>The sitting's state is a dot on the row already taken and nothing
    /// else: the row keeps its label and stays offered.</summary>
    [Fact]
    public void ARowTakenThisSittingCarriesTheDotAndStaysOffered()
    {
        var cold = PostFightChoice.For("NaveGreed", Facts(won: true));
        var shown = PostFightChoice.For("NaveGreed", Facts(won: true, comparisonShown: true));
        var watched = PostFightChoice.For(
            "NaveGreed", Facts(won: true, canWatch: true, fightWatched: true));

        Assert.False(cold.Rows[0].Row.IsCurrent);
        Assert.True(shown.Rows[0].Row.IsCurrent);
        Assert.True(shown.Rows[0].Row.Enabled);
        Assert.Equal("Show the comparison", shown.Rows[0].Row.Label);
        Assert.True(watched.Rows[1].Row.IsCurrent);
        Assert.False(watched.Rows[0].Row.IsCurrent);
    }

    [Fact]
    public void RefusesARowThatIsNotOffered()
    {
        var choice = PostFightChoice.For("NaveGreed", Facts(won: true));

        Assert.Throws<Replay.ManifestException>(() => choice.ActionAt(3));
        Assert.Throws<Replay.ManifestException>(() => choice.ActionAt(-1));
    }

    /// <summary>Nothing in the wording names a recording: the one row that names
    /// anybody interpolates the creator.</summary>
    [Fact]
    public void AnotherCreatorIsNamedByTheSameSentences()
    {
        var choice = PostFightChoice.For("Someone Else", Facts(won: true, canWatch: true));

        Assert.Equal("Watch Someone Else's fight", choice.Rows[1].Row.Label);
        Assert.DoesNotContain(choice.Menu, row => row.Label.Contains("NaveGreed", StringComparison.Ordinal));
    }

    private static PostFightFacts Facts(
        bool won, bool comparisonShown = false, bool fightWatched = false, bool canWatch = false,
        bool canContinueAsYou = false) =>
        new(won, comparisonShown, fightWatched, canWatch, canContinueAsYou);
}
