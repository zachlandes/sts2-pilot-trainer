namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The rules a recorder uses to nominate the alternative a negative control takes.
///
/// Three controls damage a decision by swapping in something else the same decision
/// offered - another node the map made reachable, another card the reward put up,
/// another position on the card screen - so each of them needs the history to name
/// that alternative. Choosing it is a rule over a list and a choice, which is why it
/// is here and testable rather than inside a Harmony patch only a retail session can
/// run.
///
/// The case that matters most in each is the one with no alternative at all. A
/// nomination invented there would make a control pass while proving nothing, which
/// is worse than the gate failing.
/// </summary>
public sealed class NegativeControlNominationTests
{
    [Fact]
    public void AMapMoveNominatesAnotherNodeTheSameNodeLedTo()
    {
        Assert.Equal(1, Corruption.NominateColumn(3, [3, 1, 5]));
    }

    [Fact]
    public void AndTheSameOneEveryTimeSoTwoReadingsOfOneRunAgree()
    {
        // The game keeps a node's children in a set, whose order is nobody's to rely
        // on. The lowest column is a choice; making one is what stops two recordings
        // of the same run from nominating different nodes.
        Assert.Equal(1, Corruption.NominateColumn(3, [5, 1]));
        Assert.Equal(1, Corruption.NominateColumn(3, [1, 5]));
    }

    [Fact]
    public void AMapMoveFromANodeWithNowhereElseToGoNominatesNothing()
    {
        Assert.Null(Corruption.NominateColumn(3, [3]));
        Assert.Null(Corruption.NominateColumn(3, []));
    }

    [Fact]
    public void ACardRewardNominatesAnotherCardItOffered()
    {
        var nomination = Corruption.NominateCard(["CARD.BASH", "CARD.TREMBLE", "CARD.WHIRLWIND"], takenIndex: 1);

        Assert.Equal(("CARD.BASH", 0), nomination);
    }

    /// <summary>
    /// A second copy of the card that was taken is not an alternative.
    ///
    /// The validator refuses a nomination whose card id equals the one taken, because
    /// the control would then swap a card for itself and corrupt nothing. Two copies
    /// of one card are two positions naming the same card, so the first genuinely
    /// different one is the answer.
    /// </summary>
    [Fact]
    public void ACardRewardThatOfferedTwoCopiesOfWhatWasTakenLooksPastThem()
    {
        Assert.Equal(
            ("CARD.WHIRLWIND", 2),
            Corruption.NominateCard(["CARD.BASH", "CARD.BASH", "CARD.WHIRLWIND"], takenIndex: 0));

        Assert.Null(Corruption.NominateCard(["CARD.BASH", "CARD.BASH"], takenIndex: 0));
    }

    [Fact]
    public void ACardRewardThatOfferedOneCardNominatesNothing()
    {
        Assert.Null(Corruption.NominateCard(["CARD.BASH"], takenIndex: 0));
    }

    /// <summary>
    /// A card screen nominates another copy of the card that was picked.
    ///
    /// Asserted by the card sitting at the nominated position rather than by the
    /// position differing, because <c>enchant-a-different-card</c> moves
    /// <c>option_index</c> and leaves <c>card_id</c> alone: an index-only rule nominates
    /// a position holding some other card, and the replay then refuses because two
    /// fields of the manifest disagree rather than because the run diverged. That is a
    /// control counted as rejected without anything having been demonstrated.
    /// </summary>
    [Fact]
    public void ACardScreenNominatesAnotherCopyOfTheCardThatWasPicked()
    {
        IReadOnlyList<string> deck =
            ["CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD", "CARD.BASH", "CARD.DEFEND_IRONCLAD"];

        var nominated = Corruption.NominateScreenOption(deck, takenIndex: 1, chosenIndexes: [1]);

        Assert.Equal(3, nominated);
        Assert.Equal(deck[1], deck[nominated!.Value]);
    }

    /// <summary>
    /// A screen holding no second copy nominates nothing, however many other cards it
    /// offered.
    ///
    /// The honest outcome, and the gate says so: an enchantment nobody could have put
    /// on an indistinguishable card is not a decision this control can damage.
    /// </summary>
    [Fact]
    public void ACardScreenOfferingOnlyDistinctCardsNominatesNothing()
    {
        Assert.Null(Corruption.NominateScreenOption(
            ["CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD", "CARD.BASH"], takenIndex: 1, chosenIndexes: [1]));
    }

    /// <summary>
    /// And never a copy another answer to the same screen already took.
    ///
    /// The screen's answers are replayed together, so a nomination pointing at a
    /// position another pick claimed would have the replay choose one card twice -
    /// which <c>ManifestCardSelector</c> refuses, making the control fail on its own
    /// illegality rather than on the corruption.
    /// </summary>
    [Fact]
    public void ACardScreenWhoseOtherCopiesWereAllTakenNominatesNothing()
    {
        IReadOnlyList<string> deck =
            ["CARD.DEFEND_IRONCLAD", "CARD.DEFEND_IRONCLAD", "CARD.DEFEND_IRONCLAD"];

        Assert.Equal(2, Corruption.NominateScreenOption(deck, takenIndex: 0, chosenIndexes: [0, 1]));
        Assert.Null(Corruption.NominateScreenOption(deck, takenIndex: 0, chosenIndexes: [0, 1, 2]));
    }

    /// <summary>
    /// A card play nominates another card the hand held at the same energy cost.
    ///
    /// Asserted by the nominated card's cost equalling the played card's rather than by
    /// the indexes differing, because that equality is the whole of what
    /// <c>substitute-same-cost</c> claims: energy conservation and hand accounting both
    /// balance, so nothing arithmetic on the footage separates the two lines. A
    /// substitute of another cost is caught by counting energy, and one the hand did not
    /// hold is refused on card identity - either way the control is counted as rejected
    /// for a reason that is not the one it is named for.
    /// </summary>
    [Fact]
    public void ACardPlayNominatesAnotherCardTheHandHeldAtTheSameCost()
    {
        IReadOnlyList<(string CardId, int EnergyCost, bool TargetsAnEnemy)> hand =
            [("CARD.BASH", 2, true), ("CARD.DEFEND_IRONCLAD", 1, false), ("CARD.WHIRLWIND", 2, true)];

        var nominated = Corruption.NominateSubstitute(hand, playedIndex: 0);

        Assert.Equal(("CARD.WHIRLWIND", 2), nominated);
        Assert.Equal(hand[0].EnergyCost, hand[nominated!.Value.HandIndex].EnergyCost);
        Assert.NotEqual(hand[0].CardId, nominated.Value.CardId);
    }

    /// <summary>
    /// And one that aims where the played card aimed.
    ///
    /// <c>substitute-same-cost</c> keeps every other argument of the play it rewrites,
    /// <c>target_index</c> included, so a substitute that aims at nothing gets that
    /// index handed to it and <c>RunDriver.ResolveTarget</c> refuses on argument shape -
    /// "supplies target_index for X, but that does not target an enemy" - before the
    /// engine replays a thing. Asserted by the targeting agreeing rather than by the
    /// cost alone, because cost alone is what let this through.
    /// </summary>
    [Fact]
    public void ACardPlayAimedAtAnEnemyNominatesOnlyACardThatAlsoAimsAtOne()
    {
        IReadOnlyList<(string CardId, int EnergyCost, bool TargetsAnEnemy)> hand =
            [("CARD.STRIKE_IRONCLAD", 1, true), ("CARD.DEFEND_IRONCLAD", 1, false), ("CARD.CLEAVE", 1, true)];

        var nominated = Corruption.NominateSubstitute(hand, playedIndex: 0);

        Assert.Equal(("CARD.CLEAVE", 2), nominated);
        Assert.True(hand[nominated!.Value.HandIndex].TargetsAnEnemy);
    }

    /// <summary>And the mirror: a play that aims at nothing never nominates an attack,
    /// which would reach the replay needing a target index it has not got.</summary>
    [Fact]
    public void ACardPlayThatAimsAtNothingNominatesOnlyACardThatAimsAtNothing()
    {
        IReadOnlyList<(string CardId, int EnergyCost, bool TargetsAnEnemy)> hand =
            [("CARD.DEFEND_IRONCLAD", 1, false), ("CARD.STRIKE_IRONCLAD", 1, true), ("CARD.FLEX", 1, false)];

        var nominated = Corruption.NominateSubstitute(hand, playedIndex: 0);

        Assert.Equal(("CARD.FLEX", 2), nominated);
        Assert.False(hand[nominated!.Value.HandIndex].TargetsAnEnemy);
    }

    [Fact]
    public void AHandHoldingNothingElseOfThatTargetingNominatesNothing()
    {
        Assert.Null(Corruption.NominateSubstitute(
            [("CARD.STRIKE_IRONCLAD", 1, true), ("CARD.DEFEND_IRONCLAD", 1, false)], playedIndex: 0));
    }

    /// <summary>
    /// Another copy of the card that was played is not a substitution.
    ///
    /// The same card from another position is a hand-index corruption, which is what a
    /// nomination nobody made already produces - and it is not the corruption whose
    /// value is that only the card's face separates the two readings.
    /// </summary>
    [Fact]
    public void AHandHoldingOnlyMoreCopiesOfThePlayedCardLooksPastThem()
    {
        IReadOnlyList<(string CardId, int EnergyCost, bool TargetsAnEnemy)> hand =
            [("CARD.STRIKE_IRONCLAD", 1, true), ("CARD.STRIKE_IRONCLAD", 1, true), ("CARD.CLEAVE", 1, true)];

        var nominated = Corruption.NominateSubstitute(hand, playedIndex: 0);

        Assert.Equal(("CARD.CLEAVE", 2), nominated);
    }

    [Fact]
    public void AHandHoldingNothingElseOfThatCostNominatesNothing()
    {
        Assert.Null(Corruption.NominateSubstitute(
            [("CARD.BASH", 2, true), ("CARD.STRIKE_IRONCLAD", 1, true), ("CARD.CLEAVE", 1, true)],
            playedIndex: 0));
        Assert.Null(Corruption.NominateSubstitute([("CARD.BASH", 2, true)], playedIndex: 0));
    }
}
