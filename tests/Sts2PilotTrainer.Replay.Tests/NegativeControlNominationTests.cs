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
}
